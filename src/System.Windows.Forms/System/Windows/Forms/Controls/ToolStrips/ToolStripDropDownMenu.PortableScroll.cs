// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public partial class ToolStripDropDownMenu
{
    private PortableMenuLayout? _portableScrollLayout;
    private int _portableScrollOffset;

    internal override void InvalidatePortableItemScrollLayout() => _portableScrollLayout = null;

    internal override PortableScrollFrame? CapturePortableScrollFrame(in LibreNativeScrollMetadata scroll,
        in PortablePointerDispatchContext dispatch)
    {
        if (scroll.X != 0)
            return null; // Never claim Y while discarding an unsupported X.
        Rectangle display = DisplayRectangle;
        if (!dispatch.IsCurrent || display.Width <= 0 || display.Height <= 0 || Items.Count == 0)
            return null;
        PortableMenuLayout? layout = _portableScrollLayout;
        if (layout is null || !layout.Matches(this, _portableScrollOffset, 0, 0, display))
        {
            layout = new(this, display);
            if (!dispatch.IsCurrent || !layout.Matches(this, 0, 0, 0, DisplayRectangle))
                return null;
            _portableScrollOffset = 0;
            _portableScrollLayout = layout;
        }

        return new PortableMenuFrame(layout.Identity, _portableScrollOffset);
    }

    private readonly record struct PortableMenuItem(ToolStripItem Item, Rectangle Bounds,
        bool Available, Padding Margin, ToolStrip? Parent, bool ScrollButton);

    // One array per real layout generation, bounded by the actual Items count.
    // The menu owns it. Retained carry frames have only Identity (weak); only a
    // synchronous plan borrows strong item references while source writes run.
    private sealed class PortableMenuLayout
    {
        internal PortableMenuItem[] Items { get; }
        internal Rectangle Display { get; }
        internal bool Scrolling { get; }
        internal int ScrollAmount { get; }
        private readonly Font _font;
        private readonly Padding _padding;
        private readonly ToolStripLayoutStyle _style;
        internal WeakReference<PortableMenuLayout> Identity { get; }

        internal PortableMenuLayout(ToolStripDropDownMenu menu, Rectangle display)
        {
            Display = display;
            Scrolling = menu.RequiresScrollButtons;
            ScrollAmount = menu._scrollAmount;
            _font = menu.Font;
            _padding = menu.Padding;
            _style = menu.LayoutStyle;
            Items = new PortableMenuItem[menu.Items.Count];
            for (int i = 0; i < Items.Length; i++)
            {
                ToolStripItem item = menu.Items[i];
                Items[i] = new(item, item.Bounds, item.Available, item.Margin, item.ParentInternal,
                    ReferenceEquals(item, menu._upScrollButton) || ReferenceEquals(item, menu._downScrollButton));
            }

            Identity = new(this);
        }

        internal bool Matches(ToolStripDropDownMenu menu, int offset, int moved, int delta, Rectangle display)
        {
            if (menu.Items.Count != Items.Length || display != Display || menu.RequiresScrollButtons != Scrolling
                || menu._scrollAmount != checked(ScrollAmount + offset) || !ReferenceEquals(menu.Font, _font)
                || menu.Padding != _padding || menu.LayoutStyle != _style)
                return false;
            for (int i = 0; i < Items.Length; i++)
            {
                PortableMenuItem entry = Items[i];
                Rectangle expected = entry.Bounds;
                expected.Y = checked(expected.Y - offset - (i < moved ? delta : 0));
                if (!ReferenceEquals(menu.Items[i], entry.Item) || entry.Item.IsDisposed
                    || !ReferenceEquals(entry.Item.Owner, menu) || entry.Item.ParentInternal != entry.Parent
                    || entry.Item.Available != entry.Available || entry.Item.Margin != entry.Margin
                    || entry.ScrollButton != (ReferenceEquals(entry.Item, menu._upScrollButton) || ReferenceEquals(entry.Item, menu._downScrollButton))
                    || entry.Item.Bounds != expected)
                    return false;
            }

            return true;
        }

        internal void Status(int offset, out int first, out int minY, out int maxY)
        {
            first = -1;
            minY = int.MaxValue;
            maxY = 0;
            for (int i = 0; i < Items.Length; i++)
            {
                PortableMenuItem entry = Items[i];
                if (!entry.Available || entry.ScrollButton)
                    continue;
                int top = checked(entry.Bounds.Top - offset);
                int bottom = checked(entry.Bounds.Bottom - offset);
                if (first == -1 && Display.Contains(Display.X, top))
                    first = i;
                minY = Math.Min(minY, top);
                maxY = Math.Max(maxY, bottom);
            }
        }

        internal bool Step(int offset, bool up, out int delta)
        {
            delta = 0;
            Status(offset, out int first, out int minY, out int maxY);
            if (!Scrolling || minY == int.MaxValue || Display.Contains(Display.X, up ? minY : maxY))
                return false;
            if (first == -1)
                throw new PlatformNotSupportedException("A native menu line needs an actual visible item top, not a guessed menu height.");
            int adjacent = AdjacentScrollItemIndex(up, first);
            if ((uint)adjacent >= (uint)Items.Length)
                return false;
            delta = checked(Items[adjacent].Bounds.Top - Items[first].Bounds.Top);
            if (up ? delta >= 0 : delta <= 0)
                throw new PlatformNotSupportedException("Native menu item scrolling requires ordered progressing item tops.");
            return true;
        }

        internal void ValidateOffset(int offset)
        {
            _ = checked(ScrollAmount + offset);
            foreach (PortableMenuItem entry in Items)
                _ = checked(entry.Bounds.Y - offset);
        }
    }

    private sealed class PortableMenuFrame(WeakReference<PortableMenuLayout> identity, int offset) : PortableScrollFrame
    {
        internal override bool Matches(PortableScrollFrame previous)
            => previous is PortableMenuFrame frame && ReferenceEquals(identity, frame.Identity) && offset == frame.Offset;
        private WeakReference<PortableMenuLayout> Identity => identity;
        private int Offset => offset;

        internal override PortableScrollPlan Plan(in LibreNativeScrollMetadata scroll, double x, double y)
        {
            if (!identity.TryGetTarget(out PortableMenuLayout? layout))
                throw new InvalidOperationException("The menu scroll layout was retired.");
            double delta = scroll.Y * (scroll.Unit == LibreNativeScrollUnit.Points ? scroll.PointScale : 1);
            if (!double.IsFinite(delta))
                throw new ArgumentException("Native menu scroll exceeds its source metric range.");
            if (delta != 0 && Math.Sign(delta) != Math.Sign(y))
                y = 0;
            double total = delta + y;
            if (!double.IsFinite(total))
                throw new ArgumentException("Native menu scroll exceeds its source accumulation range.");
            double integral = Math.Truncate(total);
            double tail = total - integral;
            int next = offset, steps = 0;
            bool up = total > 0;
            if (scroll.Unit == LibreNativeScrollUnit.Points)
            {
                layout.Status(offset, out _, out int minY, out int maxY);
                int down = layout.Scrolling && minY != int.MaxValue ? Math.Max(0, checked(maxY - (layout.Display.Bottom - 1))) : 0;
                int top = layout.Scrolling && minY != int.MaxValue ? Math.Max(0, checked(layout.Display.Top - minY)) : 0;
                int motion = (int)Math.Clamp(integral, -down, top);
                next = checked(offset - motion);
                if ((total < 0 && motion == -down) || (total > 0 && motion == top))
                    tail = 0;
            }
            else
            {
                // Re-evaluate the original first visible TOP after each full
                // step. No constant item height, filtered adjacency or 120 ticks.
                double requested = Math.Abs(integral);
                while (steps < requested && layout.Step(next, up, out int step))
                {
                    if (steps >= layout.Items.Length)
                        throw new PlatformNotSupportedException("The menu item-step layout does not reach a source boundary.");
                    next = checked(next + step);
                    steps++;
                }

                if (total != 0 && !layout.Step(next, up, out _))
                    tail = 0;
            }

            layout.ValidateOffset(next); // Reject the entire numeric plan before writes.
            return new PortableMenuPlan(layout, offset, next, steps, up, scroll.Unit, tail);
        }
    }

    private sealed class PortableMenuPlan(PortableMenuLayout layout, int start, int end, int steps, bool up,
        LibreNativeScrollUnit unit, double tail)
        : PortableScrollPlan(new PortableMenuFrame(layout.Identity, end), 0, tail)
    {
        internal override bool Apply(Control consumer, in PortablePointerDispatchContext dispatch)
        {
            ToolStripDropDownMenu menu = (ToolStripDropDownMenu)consumer;
            PortableMenuScope scope = new(menu, layout, start, dispatch);
            if (!scope.IsCurrent(0, 0))
                return false;
            int count = unit == LibreNativeScrollUnit.Points ? (end == start ? 0 : 1) : steps;
            for (int i = 0; i < count; i++)
            {
                // The guarded snapshot is compared to actual source bounds on
                // every iteration; a prior accepted step may have run callbacks.
                int delta = end - scope.Offset;
                if (unit == LibreNativeScrollUnit.Lines && !layout.Step(scope.Offset, up, out delta))
                    return false;
                if (!menu.ScrollInternal(delta, scope))
                    return false;
                if (!menu.UpdatePortableScrollStatus(scope, layout))
                    return false;
            }

            return scope.IsCurrent(0, 0);
        }
    }

    private bool UpdatePortableScrollStatus(PortableMenuScope scope, PortableMenuLayout layout)
    {
        if (!scope.IsCurrent(0, 0))
            return false;
        if (!layout.Scrolling)
            return true;
        layout.Status(scope.Offset, out int first, out int minY, out int maxY);
        _indexOfFirstDisplayedItem = first;
        UpScrollButton.Enabled = !layout.Display.Contains(layout.Display.X, minY);
        if (!scope.IsCurrent(0, 0))
            return false;
        DownScrollButton.Enabled = !layout.Display.Contains(layout.Display.X, maxY);
        return scope.IsCurrent(0, 0);
    }

    private sealed class PortableMenuScope(ToolStripDropDownMenu menu, PortableMenuLayout layout, int offset,
        PortablePointerDispatchContext dispatch) : PortableItemScrollScope
    {
        internal int Offset { get; private set; } = offset;
        internal override int Count => layout.Items.Length;
        internal override ToolStripItem Item(int index) => layout.Items[index].Item;
        internal override Point Location(int index, int delta)
            => new(layout.Items[index].Bounds.X, checked(layout.Items[index].Bounds.Y - Offset - delta));
        internal override bool IsCurrent(int moved, int delta)
            => dispatch.IsCurrent && ReferenceEquals(menu._portableScrollLayout, layout)
                && menu._portableScrollOffset == Offset
                && layout.Matches(menu, Offset, moved, delta, menu.DisplayRectangle) && dispatch.IsCurrent;
        internal override void Commit(int delta)
        {
            Offset = checked(Offset + delta);
            menu._portableScrollOffset = Offset;
            menu._scrollAmount = checked(layout.ScrollAmount + Offset);
        }
    }
}
#endif
