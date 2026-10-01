// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public abstract partial class ScrollBar
{
    // An explicit native-consumer work bound, not a Windows wheel policy.
    private const int MaximumPortableScrollBarLines = 1024;
    private sealed class PortableScrollBarIdentity { }
    private PortableScrollBarIdentity? _portableScrollBarIdentity;
    private PortableScrollBarIdentity? _portableScrollBarPendingValueIdentity;

    private void InvalidatePortableScrollBarFrame() => _portableScrollBarIdentity = null;

    private bool CanConsumePortableScrollBarLines(in LibreNativeScrollMetadata scroll)
        => Enabled && !HasChildren && scroll.Unit == LibreNativeScrollUnit.Lines
            && (_scrollOrientation == ScrollOrientation.HorizontalScroll ? scroll.Y == 0 : scroll.X == 0);

    internal void ValidatePortableScrollBarLines(in LibreNativeScrollMetadata scroll)
    {
        if (!CanConsumePortableScrollBarLines(scroll))
            return;
        double lines = _scrollOrientation == ScrollOrientation.HorizontalScroll ? scroll.X : scroll.Y;
        if (Math.Abs(lines) > MaximumPortableScrollBarLines)
            throw new PlatformNotSupportedException("A native ScrollBar packet exceeds the 1024-line source callback budget.");
    }

    private readonly record struct PortableScrollBarValues(int Value, int Minimum, int Maximum,
        int Large, int Small, RightToLeft Direction, ScrollOrientation Orientation)
    {
        // LargeChange=0 also makes the actual SmallChange zero. Keep the valid
        // public Value range, rather than inventing Maximum+1 as a value.
        internal int Last => (int)Math.Min(Maximum, (long)Maximum - Large + 1);
        internal int Step(int value, bool increment)
            => increment ? (int)Math.Min((long)value + Small, Last)
                : (int)Math.Max((long)value - Small, Minimum);
    }

    private PortableScrollBarValues ReadPortableScrollBarValues()
        => new(_value, _minimum, _maximum, LargeChange, SmallChange, RightToLeft, _scrollOrientation);

    internal override PortableScrollFrame? CapturePortableScrollFrame(in LibreNativeScrollMetadata scroll,
        in PortablePointerDispatchContext dispatch)
    {
        // No arbitrary Value-to-point conversion, cross-axis promotion, or
        // ancestor-bar admission behind another control's preflight target.
        if (!dispatch.IsCurrent || !CanConsumePortableScrollBarLines(scroll))
            return null;
        ValidatePortableScrollBarLines(scroll);
        PortableScrollBarValues values = ReadPortableScrollBarValues();
        if (values.Large < 0 || values.Small < 0 || values.Last < values.Minimum)
            throw new PlatformNotSupportedException("The source ScrollBar metrics exceed their valid numeric range.");
        return new PortableScrollBarFrame(_portableScrollBarIdentity ??= new(), values);
    }

    private bool IsCurrentPortableScrollBarFrame(PortableScrollBarFrame frame)
    {
        if (!Enabled || HasChildren || !ReferenceEquals(_portableScrollBarIdentity, frame.Identity))
            return false;
        PortableScrollBarValues values = ReadPortableScrollBarValues();
        // RightToLeft is virtual. Its getter may change source state or pump
        // input; even an away/back mutation must not validate an old identity.
        return Enabled && !HasChildren && ReferenceEquals(_portableScrollBarIdentity, frame.Identity)
            && values == frame.Values;
    }

    private void SetPortableScrollBarValue(int value, PortableScrollBarIdentity identity)
    {
        _portableScrollBarPendingValueIdentity = identity;
        try { Value = value; }
        finally { _portableScrollBarPendingValueIdentity = null; }
    }

    private sealed class PortableScrollBarFrame(PortableScrollBarIdentity identity,
        PortableScrollBarValues values) : PortableScrollFrame
    {
        internal PortableScrollBarIdentity Identity => identity;
        internal PortableScrollBarValues Values => values;
        internal override bool Matches(PortableScrollFrame previous)
            => previous is PortableScrollBarFrame frame && ReferenceEquals(identity, frame.Identity) && values == frame.Values;

        internal override PortableScrollPlan Plan(in LibreNativeScrollMetadata scroll, double x, double y)
        {
            bool horizontal = values.Orientation == ScrollOrientation.HorizontalScroll;
            double delta = horizontal ? scroll.X : scroll.Y;
            if (Math.Abs(delta) > MaximumPortableScrollBarLines)
                throw new PlatformNotSupportedException("A native ScrollBar packet exceeds the 1024-line source callback budget.");
            double remainder = horizontal ? x : y;
            if (delta != 0 && Math.Sign(delta) != Math.Sign(remainder))
                remainder = 0;
            // Keep the integer and fractional additions separate. Adding a
            // nearly-one tail directly to 1024 could round up to 1025 in double.
            double whole = Math.Truncate(delta);
            double fraction = (delta - whole) + remainder;
            double carry = Math.Truncate(fraction);
            int lines = checked((int)(whole + carry));
            double tail = fraction - carry;
            bool positive = delta != 0 ? delta > 0 : remainder > 0;
            // Preserve DoScroll's actual RightToLeft policy. VScrollBar's
            // getter is always No; native deltas already contain user inversion.
            bool increment = positive == (values.Direction == RightToLeft.Yes);
            int next = values.Value;
            for (int index = 0; index < Math.Abs(lines); index++)
                next = values.Step(next, increment);
            if (values.Small == 0 || (increment ? next >= values.Last : next <= values.Minimum))
                tail = 0;
            var expected = new PortableScrollBarFrame(next == values.Value ? identity : new(), values with { Value = next });
            return new PortableScrollBarPlan(this, expected, Math.Abs(lines), increment,
                horizontal ? tail : 0, horizontal ? 0 : tail);
        }
    }

    private sealed class PortableScrollBarPlan(PortableScrollBarFrame before, PortableScrollBarFrame expected,
        int lines, bool increment, double x, double y) : PortableScrollPlan(expected, x, y)
    {
        internal override bool Apply(Control consumer, in PortablePointerDispatchContext dispatch)
        {
            ScrollBar owner = (ScrollBar)consumer;
            PortablePointerDispatchContext context = dispatch;
            PortableScrollBarFrame current = before;
            bool redirected = false;
            if (!IsCurrent())
                return false;
            for (int index = 0; index < lines; index++)
            {
                int proposed = current.Values.Step(current.Values.Value, increment);
                if (!Scroll(increment ? ScrollEventType.SmallIncrement : ScrollEventType.SmallDecrement, proposed))
                    return false;
            }

            if (lines != 0 && !Scroll(ScrollEventType.EndScroll, current.Values.Value))
                return false;
            // Handler-adjusted NewValue remains authoritative, but cannot
            // retain fractions computed for the original numeric plan.
            return !redirected && context.IsCurrent && owner.IsCurrentPortableScrollBarFrame(expected) && context.IsCurrent;

            bool IsCurrent() => context.IsCurrent && owner.IsCurrentPortableScrollBarFrame(current) && context.IsCurrent;

            bool Scroll(ScrollEventType type, int proposed)
            {
                if (!IsCurrent())
                    return false;
                ScrollEventArgs args = new(type, current.Values.Value, proposed, current.Values.Orientation);
                owner.OnScroll(args);
                if (!IsCurrent())
                    return false;
                redirected |= args.NewValue != proposed;
                int previous = current.Values.Value;
                owner.SetPortableScrollBarValue(args.NewValue, expected.Identity);
                current = new(previous == args.NewValue ? current.Identity : expected.Identity,
                    current.Values with { Value = args.NewValue });
                return IsCurrent();
            }
        }
    }
}
#endif
