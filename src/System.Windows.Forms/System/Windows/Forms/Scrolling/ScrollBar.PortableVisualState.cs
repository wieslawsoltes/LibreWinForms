// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms;

public abstract partial class ScrollBar
{
    private sealed record PortableScrollBarHover(PortablePointerDispatchContext Dispatch, Point Location);
    private PortableScrollBarHover? _portableScrollBarHover;

    private bool SetPortableScrollBarHover(Point point, in PortablePointerDispatchContext dispatch,
        PortableScrollBarGeometry geometry, bool forceInvalidate = false)
    {
        if (!dispatch.IsCurrent) return false;
        PortableScrollBarPart previous = _portableScrollBarHover is { } old && old.Dispatch.IsCurrentTarget
            ? geometry.Hit(old.Location) : PortableScrollBarPart.None;
        _portableScrollBarHover = new(dispatch, point);
        if (forceInvalidate || previous != geometry.Hit(point)) Invalidate();
        return dispatch.IsCurrent;
    }

    private ScrollBarState PortableScrollBarVisualState(PortableScrollBarPart part,
        PortableScrollBarGeometry geometry, bool enabled)
    {
        if (!enabled) return ScrollBarState.Disabled;
        if (GetStyle(ControlStyles.UserMouse)) return ScrollBarState.Normal;
        PortableScrollBarPart hot = _portableScrollBarHover is { } hover && hover.Dispatch.IsCurrentTarget
            ? geometry.Hit(hover.Location) : PortableScrollBarPart.None;
        // Paint must not invoke virtual source getters to revalidate a press.
        // Configuration setters retire its identity before public callbacks.
        PortableScrollBarPress? press = _portableScrollBarPress;
        bool pressed = press is not null && press.Dispatch.IsCurrentPress
            && ReferenceEquals(_portableScrollBarIdentity, press.Frame.Identity)
            && press.Bounds == Bounds && press.Dpi == DeviceDpi && press.Parent == Parent
            && press.Geometry.Client == geometry.Client;
        if (pressed && press!.Part == part && (part == PortableScrollBarPart.Thumb || hot == part))
            return ScrollBarState.Pressed;
        return hot == part ? ScrollBarState.Hot : ScrollBarState.Normal;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _portableScrollBarHover = null;
        if (_portableScrollBarPress is { } press) press.PointerOnPart = false;
        Invalidate();
        base.OnMouseLeave(e);
    }
}
#endif
