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
            bool placeholder = TextLength == 0 && !Focused;
            string text = GetPortableDisplayText(placeholder);
            TextFormatFlags flags = GetPortableEditorTextFlags();

            // Both portable frame paths already carry parent/layer transforms.
            // Intersect instead of replacing that clip, and restore caller state.
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(bounds, CombineMode.Intersect);
                e.Graphics.SetClip(e.ClipRectangle, CombineMode.Intersect);
                Color foreground = !Enabled || placeholder ? SystemColors.GrayText : ForeColor;
                if (!PaintPortableTextLayout(e, text, placeholder, flags, foreground))
                    TextRenderer.DrawText(e.Graphics, text, Font, bounds, foreground, flags);
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
        ReleasePortableTextLayout();
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        ResetPortableCaretBlink();
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        _portableCaretTimer?.Stop();
        if (_portablePointerSelecting && Capture) Capture = false;
        _portablePointerSelecting = false;
        base.OnLostFocus(e);
        Invalidate();
    }

    private string GetPortableDisplayText(bool placeholder)
    {
        string text = placeholder ? PlaceholderText : Text;
        if (!placeholder && (_useSystemPasswordChar || _passwordChar != '\0'))
            return new string(_useSystemPasswordChar ? '\u25CF' : _passwordChar, text.Length);
        return text;
    }

    private TextFormatFlags GetPortableEditorTextFlags()
    {
        // The source client clip stays fixed while the retained paragraph scrolls.
        TextFormatFlags flags = TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding | TextFormatFlags.NoClipping;
        if (!Multiline) flags |= TextFormatFlags.SingleLine;
        else if (WordWrap) flags |= TextFormatFlags.WordBreak;
        flags |= RtlTranslateHorizontal(TextAlign) switch
        {
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            HorizontalAlignment.Right => TextFormatFlags.Right,
            _ => TextFormatFlags.Left
        };
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
        return flags;
    }
}
#endif
