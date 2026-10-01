// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

public partial class MaskedTextBox
{
    protected override void OnPaint(PaintEventArgs e)
    {
        Rectangle bounds = PortableEditFrame.GetTextViewport(PortableClientRectangle, BorderStyle);
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            // TextMaskFormat describes public output, not the displayed mask.
            // Reuse the same provider formatting as the original EDIT window.
            string display = GetPortableMaskedDisplay();
            TextFormatFlags flags = GetPortableMaskedFlags();

            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(bounds, CombineMode.Intersect);
                e.Graphics.SetClip(e.ClipRectangle, CombineMode.Intersect);
                Color foreground = Enabled ? ForeColor : SystemColors.GrayText;
                if (!PaintPortableMaskedLayout(e, foreground) && !IsDisposed && !Disposing)
                    TextRenderer.DrawText(e.Graphics, display, Font, bounds, foreground, flags);
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }

        base.OnPaint(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        _portableMaskedFocusVersion++;
        InvalidatePortableMaskedLayout();
        _portableMaskedCaretTimer?.Stop();
        bool releaseCapture = _portableMaskedPointerSelecting && Capture;
        _portableMaskedPointerSelecting = false;
        _portableMaskedPress = default;
        if (releaseCapture) Capture = false;
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        ResetPortableMaskedCaret();
        Invalidate();
    }
}
#endif
