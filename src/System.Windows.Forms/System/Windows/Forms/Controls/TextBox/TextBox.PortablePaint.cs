// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

public partial class TextBox
{
    protected override void OnPaint(PaintEventArgs e)
    {
        Rectangle bounds = ClientRectangle;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            string text = Text;
            bool placeholder = text.Length == 0 && !Focused;
            if (placeholder)
            {
                text = PlaceholderText;
            }
            else if (_useSystemPasswordChar || _passwordChar != '\0')
            {
                // Portable display policy only. Keep the actual source text and
                // UTF-16 selection untouched; never pass secrets to the renderer.
                char password = _useSystemPasswordChar ? '\u25CF' : _passwordChar;
                text = new string(password, text.Length);
            }

            TextFormatFlags flags = TextFormatFlags.TextBoxControl
                | TextFormatFlags.NoPrefix
                | TextFormatFlags.NoPadding;
            if (!Multiline)
            {
                flags |= TextFormatFlags.SingleLine;
            }
            else if (WordWrap)
            {
                flags |= TextFormatFlags.WordBreak;
            }

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

            // Both portable frame paths already carry parent/layer transforms.
            // Intersect instead of replacing that clip, and restore caller state.
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(bounds, CombineMode.Intersect);
                e.Graphics.SetClip(e.ClipRectangle, CombineMode.Intersect);
                TextRenderer.DrawText(
                    e.Graphics, text, Font, bounds,
                    !Enabled || placeholder ? SystemColors.GrayText : ForeColor,
                    flags);
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }

        base.OnPaint(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }
}
#endif
