// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;

namespace System.Windows.Forms;

public abstract partial class ScrollBar
{
    private enum PortableScrollBarPart { None, Decrement, Increment, PageDecrement, PageIncrement, Thumb }

    private sealed class PortableScrollBarPress(PortablePointerDispatchContext dispatch,
        PortableScrollBarFrame frame, PortableScrollBarGeometry geometry, PortableScrollBarPart part,
        Point point, Rectangle bounds, int dpi, Control? parent)
    {
        internal PortablePointerDispatchContext Dispatch { get; } = dispatch;
        internal PortableScrollBarFrame Frame { get; set; } = frame;
        internal object InputIdentity { get; set; } = new();
        internal PortableScrollBarGeometry Geometry { get; } = geometry;
        internal PortableScrollBarPart Part { get; } = part;
        internal int Offset { get; } = geometry.Axis(point) - geometry.Start;
        internal Rectangle Bounds { get; } = bounds;
        internal int Dpi { get; } = dpi;
        internal Control? Parent { get; } = parent;
    }

    private PortableScrollBarPress? _portableScrollBarPress;

    private bool IsCurrentPortableScrollBarPress(PortableScrollBarPress press)
        => ReferenceEquals(_portableScrollBarPress, press) && press.Dispatch.IsCurrentPress
            && Bounds == press.Bounds && DeviceDpi == press.Dpi && Parent == press.Parent
            && ClientRectangle == press.Geometry.Client && IsCurrentPortableScrollBarFrame(press.Frame)
            && ReferenceEquals(_portableScrollBarPress, press) && press.Dispatch.IsCurrentPress;

    internal override bool ProcessPortableMouseDownDefault(MouseEventArgs e, in PortablePointerDispatchContext dispatch)
    {
        if (e.Button != MouseButtons.Left) return true;
        if (!dispatch.IsCurrent || !Enabled || HasChildren) return false;
        PortableScrollBarIdentity identity = _portableScrollBarIdentity ??= new();
        PortableScrollBarFrame frame = new(identity, ReadPortableScrollBarValues());
        PortableScrollBarGeometry geometry = ReadPortableScrollBarGeometry();
        if (!dispatch.IsCurrent || !IsCurrentPortableScrollBarFrame(frame) || !dispatch.IsCurrent) return false;
        PortableScrollBarPart part = geometry.Hit(e.Location);
        if (part == PortableScrollBarPart.None || frame.Values.Last <= frame.Values.Minimum)
            return SetPortableScrollBarHover(e.Location, dispatch, geometry);
        PortableScrollBarPress press = new(dispatch, frame, geometry, part, e.Location, Bounds, DeviceDpi, Parent);
        _portableScrollBarPress = press;
        if (!SetPortableScrollBarHover(e.Location, dispatch, geometry, forceInvalidate: true)) return false;
        if (part == PortableScrollBarPart.Thumb) return true;
        bool increment = part is PortableScrollBarPart.Increment or PortableScrollBarPart.PageIncrement;
        if (frame.Values.Direction == RightToLeft.Yes) increment = !increment;
        bool page = part is PortableScrollBarPart.PageDecrement or PortableScrollBarPart.PageIncrement;
        long step = page ? frame.Values.Large : frame.Values.Small;
        int next = (int)Math.Clamp(frame.Values.Value + (increment ? step : -step), frame.Values.Minimum, frame.Values.Last);
        ScrollEventType type = page
            ? (increment ? ScrollEventType.LargeIncrement : ScrollEventType.LargeDecrement)
            : (increment ? ScrollEventType.SmallIncrement : ScrollEventType.SmallDecrement);
        PortablePointerDispatchContext context = dispatch;
        return ApplyPortablePointerScroll(press, type, next, () => context.IsCurrent);
    }

    internal override bool ProcessPortableMouseMoveDefault(MouseEventArgs e, in PortablePointerDispatchContext dispatch)
    {
        if (!dispatch.IsCurrent) return false;
        PortableScrollBarGeometry currentGeometry = ReadPortableScrollBarGeometry();
        if (!SetPortableScrollBarHover(e.Location, dispatch, currentGeometry)) return false;
        PortableScrollBarPress? press = _portableScrollBarPress;
        if (press is null) return true;
        press.InputIdentity = new();
        if (!IsCurrentPortableScrollBarPress(press))
        {
            if (dispatch.IsCurrent && ReferenceEquals(_portableScrollBarPress, press)) _portableScrollBarPress = null;
            return dispatch.IsCurrent;
        }

        if (!dispatch.IsCurrent) return false;
        if (press.Part != PortableScrollBarPart.Thumb || (e.Button & MouseButtons.Left) == 0) return true;
        PortableScrollBarGeometry geometry = press.Geometry;
        int travel = geometry.Track - geometry.Thumb;
        if (travel <= 0) return true;
        long offset = Math.Clamp((long)geometry.Axis(e.Location) - press.Offset - geometry.Arrow, 0, travel);
        long position = geometry.Minimum + (offset * (geometry.Last - geometry.Minimum) + travel / 2) / travel;
        if (press.Frame.Values.Direction == RightToLeft.Yes)
            position = (long)press.Frame.Values.Minimum + press.Frame.Values.Maximum - press.Frame.Values.Large + 1 - position;
        int next = (int)Math.Clamp(position, press.Frame.Values.Minimum, press.Frame.Values.Last);
        if (next == press.Frame.Values.Value) return true;
        PortablePointerDispatchContext context = dispatch;
        return ApplyPortablePointerScroll(press, ScrollEventType.ThumbTrack, next, () => context.IsCurrent);
    }

    internal override void InvokePortableMouseUp(MouseEventArgs e, bool? nativeDoubleClick,
        bool nativeClickEligible, Func<bool>? isCurrentRelease)
    {
        PortableScrollBarPress? press = _portableScrollBarPress;
        if (e.Button == MouseButtons.Left && press is not null)
        {
            object inputIdentity = press.InputIdentity = new();
            bool Current() => ReferenceEquals(press.InputIdentity, inputIdentity) && (isCurrentRelease?.Invoke() ?? true);
            try
            {
                if (!GetStyle(ControlStyles.UserMouse) && IsCurrentPortableScrollBarPress(press) && Current())
                {
                    if (press.Part == PortableScrollBarPart.Thumb
                        && !ApplyPortablePointerScroll(press, ScrollEventType.ThumbPosition, press.Frame.Values.Value, Current)) return;
                    if (!ApplyPortablePointerScroll(press, ScrollEventType.EndScroll, press.Frame.Values.Value, Current)) return;
                }
            }
            finally
            {
                if (ReferenceEquals(_portableScrollBarPress, press))
                {
                    _portableScrollBarPress = null;
                    Invalidate();
                }
            }

            if (!press.Dispatch.IsCurrentPress) return;
        }

        if (isCurrentRelease?.Invoke() != false)
            base.InvokePortableMouseUp(e, nativeDoubleClick, nativeClickEligible, isCurrentRelease);
    }

    private bool ApplyPortablePointerScroll(PortableScrollBarPress press, ScrollEventType type, int proposed, Func<bool> current)
    {
        bool IsCurrent() => current() && IsCurrentPortableScrollBarPress(press) && current();
        try
        {
            if (!IsCurrent()) return false;
            PortableScrollBarFrame before = press.Frame;
            ScrollEventArgs args = new(type, before.Values.Value, proposed, before.Values.Orientation);
            OnScroll(args);
            if (!IsCurrent()) return false;
            PortableScrollBarIdentity identity = args.NewValue == before.Values.Value ? before.Identity : new();
            // Publish our expected value generation before public ValueChanged
            // callbacks. Nested input can replace it; the old packet cannot.
            press.Frame = new(identity, before.Values with { Value = args.NewValue });
            SetPortableScrollBarValue(args.NewValue, identity);
            return IsCurrent();
        }
        catch
        {
            if (current() && ReferenceEquals(_portableScrollBarPress, press)) _portableScrollBarPress = null;
            throw;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        if (!Visible)
        {
            _portableScrollBarPress = null;
            _portableScrollBarHover = null;
        }

        base.OnVisibleChanged(e);
    }

    protected override void OnParentChanged(EventArgs e)
    {
        _portableScrollBarPress = null;
        _portableScrollBarHover = null;
        base.OnParentChanged(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        _portableScrollBarPress = null;
        _portableScrollBarHover = null;
        Invalidate();
        base.OnMouseCaptureChanged(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        _portableScrollBarPress = null;
        _portableScrollBarHover = null;
        base.OnSizeChanged(e);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        _portableScrollBarPress = null;
        _portableScrollBarHover = null;
        base.OnLocationChanged(e);
    }
}
#endif
