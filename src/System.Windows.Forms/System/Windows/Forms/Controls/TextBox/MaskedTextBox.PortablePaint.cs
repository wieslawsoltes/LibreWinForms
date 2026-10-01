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
            string display = _flagState[s_isNullMask] ? WindowText : GetFormattedDisplayString();
            if (_flagState[s_isNullMask] && _maskedTextProvider.IsPassword)
            {
                display = new string(_maskedTextProvider.PasswordChar, display.Length);
            }

            TextFormatFlags flags = TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix
                | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            flags |= RtlTranslateHorizontal(TextAlign) switch
            {
                HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                HorizontalAlignment.Right => TextFormatFlags.Right,
                _ => TextFormatFlags.Left
            };
            if (RightToLeft == RightToLeft.Yes)
            {
                flags |= TextFormatFlags.RightToLeft;
            }

            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(bounds, CombineMode.Intersect);
                e.Graphics.SetClip(e.ClipRectangle, CombineMode.Intersect);
                TextRenderer.DrawText(e.Graphics, display, Font, bounds, Enabled ? ForeColor : SystemColors.GrayText, flags);
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
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }
}
#endif
