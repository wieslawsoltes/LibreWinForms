// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public partial class ScrollableControl
{
    internal readonly record struct PortableScrollFrame(int X, int Y, int MinX, int MinY, int SmallX, int SmallY);

    internal bool TryGetPortableScrollFrame(bool horizontal, bool vertical, out PortableScrollFrame frame)
    {
        frame = default;
        // These are the source's pixel-valued AutoScroll display and actual
        // line increments. Arbitrary ScrollBar.Value units are not pixels.
        if (!AutoScroll || (horizontal && !HScroll) || (vertical && !VScroll))
            return false;
        var client = ClientRectangle;
        frame = new(_displayRect.X, _displayRect.Y,
            Math.Min(client.Width - _displayRect.Width, 0), Math.Min(client.Height - _displayRect.Height, 0),
            HorizontalScroll.SmallChange, VerticalScroll.SmallChange);
        return true;
    }

    internal void ApplyPortableNativeScroll(int x, int y, PortablePointerDispatchContext dispatch)
    {
        if (!dispatch.IsCurrent)
            return;
        SetDisplayRectLocation(x, y);
        if (dispatch.IsCurrent)
            SyncScrollbars(AutoScroll);
    }
}
#endif
