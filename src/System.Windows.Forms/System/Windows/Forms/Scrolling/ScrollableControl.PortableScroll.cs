// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public partial class ScrollableControl
{
    private readonly record struct AutoScrollFrame(int X, int Y, int MinX, int MinY, int SmallX, int SmallY);

    internal override PortableScrollFrame? CapturePortableScrollFrame(in LibreNativeScrollMetadata scroll,
        in PortablePointerDispatchContext dispatch)
    {
        // These are the source's pixel-valued AutoScroll display and actual
        // line increments. Arbitrary ScrollBar.Value units are not pixels.
        if (!AutoScroll || (scroll.X != 0 && !HScroll) || (scroll.Y != 0 && !VScroll))
            return null;
        var client = ClientRectangle;
        return new PortableAutoScrollFrame(new(_displayRect.X, _displayRect.Y,
            Math.Min(client.Width - _displayRect.Width, 0), Math.Min(client.Height - _displayRect.Height, 0),
            HorizontalScroll.SmallChange, VerticalScroll.SmallChange));
    }

    private sealed class PortableAutoScrollFrame(AutoScrollFrame value) : PortableScrollFrame
    {
        internal AutoScrollFrame Value { get; } = value;
        internal override bool Matches(PortableScrollFrame previous)
            => previous is PortableAutoScrollFrame frame && frame.Value == Value;

        internal override PortableScrollPlan Plan(in LibreNativeScrollMetadata scroll, double x, double y)
        {
            double dx = scroll.X * (scroll.Unit == LibreNativeScrollUnit.Points ? scroll.PointScale : Value.SmallX);
            double dy = scroll.Y * (scroll.Unit == LibreNativeScrollUnit.Points ? scroll.PointScale : Value.SmallY);
            (int nextX, double tailX) = AccumulatePortableScroll(Value.X, Value.MinX, dx, x);
            (int nextY, double tailY) = AccumulatePortableScroll(Value.Y, Value.MinY, dy, y);
            return new PortableAutoScrollPlan(new(Value with { X = nextX, Y = nextY }), tailX, tailY);
        }
    }

    private sealed class PortableAutoScrollPlan(PortableAutoScrollFrame frame, double x, double y)
        : PortableScrollPlan(frame, x, y)
    {
        internal override bool Apply(Control consumer, in PortablePointerDispatchContext dispatch)
        {
            if (!dispatch.IsCurrent)
                return false;
            ScrollableControl owner = (ScrollableControl)consumer;
            owner.SetDisplayRectLocation(frame.Value.X, frame.Value.Y);
            if (!dispatch.IsCurrent)
                return false;
            owner.SyncScrollbars(owner.AutoScroll);
            return dispatch.IsCurrent;
        }
    }
}
#endif
