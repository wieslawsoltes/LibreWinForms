// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>Implements canonical WinForms text rendering through managed ProGPU System.Drawing.</summary>
public sealed class ProGpuTextRendererService : ILibreTextRendererService, ILibreTextSourceGeometryService
{
    public ILibreTextLayout CreateLayout(Graphics graphics, string text, Font font,
        Size layoutSize, LibreTextFormat format)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        ValidateFormat(format);
        // Editor offsets must retain every original UTF-16 source position.
        // Padding belongs to the source client rectangle, not a second layout.
        if (!format.HasFlag(LibreTextFormat.NoPrefix) ||
            !format.HasFlag(LibreTextFormat.NoPadding) ||
            (format & (LibreTextFormat.EndEllipsis | LibreTextFormat.PathEllipsis |
                LibreTextFormat.WordEllipsis | LibreTextFormat.LeftAndRightPadding)) != 0)
            throw new NotSupportedException("Retained editor layout requires untrimmed, unprefixed text without renderer padding.");
        using StringFormat selected = CreateStringFormat(format);
        selected.FormatFlags &= ~StringFormatFlags.LineLimit;
        selected.SetDigitSubstitution(0, StringDigitSubstitute.None);
        return new RetainedLayout(global::ProGPU.SystemDrawing.DrawingTextLayout.Create(
            graphics, text, font, layoutSize, selected));
    }

    private sealed class RetainedLayout(global::ProGPU.SystemDrawing.DrawingTextLayout layout)
#if LIBREWINFORMS_NATIVE_EDIT_WORD_BOUNDARIES
        : ILibreTextLayout, ILibreTextRowNavigation, ILibreEditWordBoundaryLayout
#else
        : ILibreTextLayout, ILibreTextRowNavigation, ILibreTextSourceGeometry
#endif
    {
        private global::ProGPU.SystemDrawing.DrawingTextLayout? _layout = layout;
        private int _selectionStart = -1;
        private int _selectionLength = -1;
        private RectangleF[] _selection = [];
#if LIBREWINFORMS_NATIVE_EDIT_WORD_BOUNDARIES
        private LibreEditWordBoundaries? _wordBoundaries;

        public LibreEditWordBoundaries GetWordBoundaries()
        {
            var current = Layout;
            if (_wordBoundaries is { } boundaries) return boundaries;
            var result = current.GetEditWordBoundaries(out var snapshot);
            boundaries = ProGpuEditWordBoundaryCapture.Copy(result, snapshot, current.TextLength);
            ObjectDisposedException.ThrowIf(!ReferenceEquals(current, _layout), this);
            // Cache only a completely validated, source-owned inventory. The
            // Drawing generation caches explicit failures and binding errors.
            _wordBoundaries = boundaries;
            return boundaries;
        }
#endif
        private global::ProGPU.SystemDrawing.DrawingTextLayout Layout
            => _layout ?? throw new ObjectDisposedException(nameof(RetainedLayout));

        public SizeF ContentSize => Layout.ContentSize;
        public int RowCount => Layout.RowCount;
        public int GetRowSourceStart(int rowIndex) => Layout.GetRowSourceStart(rowIndex);
        public int GetRowIndexFromTextPosition(int textPosition) => Layout.GetRowIndexFromTextPosition(textPosition);
        public int GetCaretRowIndex(int textPosition, bool trailing) => Layout.GetCaretRowIndex(textPosition, trailing);
        public PointF GetSourcePositionPoint(int textPosition) => Layout.GetSourcePositionPoint(textPosition);
        public LibreTextCaret GetCaret(int textPosition, bool trailing = false)
            => Convert(Layout.GetCaretStop(textPosition, trailing));
        public LibreTextCaret MoveCaret(int textPosition, bool trailing, int visualDirection)
            => Convert(Layout.MoveCaretVisually(textPosition, trailing, visualDirection));
        public LibreTextCaret GetRowBoundary(int textPosition, bool trailing, bool end)
            => Convert(Layout.GetRowBoundary(textPosition, trailing, end));
        public LibreTextCaret MoveCaretVertically(int textPosition, bool trailing, int direction, float preferredX)
            => Convert(Layout.MoveCaretVertically(textPosition, trailing, direction, preferredX));
        public LibreTextHit HitTest(PointF point)
        {
            var hit = Layout.HitTestPoint(point);
            return new(hit.TextPosition, hit.IsTrailingHit, hit.IsInside,
                new RectangleF(hit.Bounds.X, hit.Bounds.Y, hit.Bounds.Width, hit.Bounds.Height), hit.BidiLevel);
        }

        public ReadOnlyMemory<RectangleF> GetSelectionRectangles(int start, int length)
        {
            var current = Layout;
            if (_selectionStart != start || _selectionLength != length)
            {
                var bounds = current.GetSelectionRectangles(start, length);
                var rectangles = new RectangleF[bounds.Count];
                for (int i = 0; i < rectangles.Length; i++)
                    rectangles[i] = new RectangleF(bounds[i].X, bounds[i].Y, bounds[i].Width, bounds[i].Height);
                _selection = rectangles;
                _selectionStart = start;
                _selectionLength = length;
            }

            return _selection;
        }

        public void Draw(Graphics graphics, PointF origin, Color color)
        {
            var current = Layout;
            using var brush = new SolidBrush(color);
            current.Draw(graphics, brush, origin);
        }

        public void Dispose()
        {
            _layout = null;
            _selection = [];
#if LIBREWINFORMS_NATIVE_EDIT_WORD_BOUNDARIES
            _wordBoundaries = null;
#endif
        }

        private static LibreTextCaret Convert(global::ProGPU.Text.TextCaretStop caret)
            => new(caret.TextPosition, caret.IsTrailing, new PointF(caret.Position.X, caret.Position.Y),
                caret.Height, caret.BidiLevel);
    }

    public void DrawText(
        Graphics graphics,
        string text,
        Font? font,
        Rectangle bounds,
        Color foreColor,
        Color backColor,
        LibreTextFormat format)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(text);
        ValidateFormat(format);

        if (text.Length == 0 || foreColor == Color.Transparent)
        {
            return;
        }

        Font selectedFont = font ?? SystemFonts.DefaultFont;
        (int left, int right) = GetTextMargins(graphics, selectedFont, format);
        if (!backColor.IsEmpty && backColor != Color.Transparent && bounds.Width > 0 && bounds.Height > 0)
        {
            using var background = new SolidBrush(backColor);
            graphics.FillRectangle(background, bounds);
        }

        // A nonpositive DrawString rectangle means an unbounded point draw.
        // Exhausting the text viewport must not turn padding into overflowing ink.
        if (bounds.Width <= left + right || bounds.Height <= 0)
        {
            return;
        }

        RectangleF textBounds = new(checked(bounds.X + left), bounds.Y,
            bounds.Width - left - right, bounds.Height);
        using StringFormat stringFormat = CreateStringFormat(format);
        if (format.HasFlag(LibreTextFormat.SingleLine))
        {
            // DrawText clips an oversized single line; its vertical viewport
            // must not make DrawString's ellipsis trimming remove every glyph.
            float lineHeight = selectedFont.GetHeight(graphics);
            if (lineHeight > textBounds.Height)
            {
                float remaining = textBounds.Height - lineHeight;
                textBounds.Y += stringFormat.LineAlignment switch
                {
                    StringAlignment.Center => remaining / 2f,
                    StringAlignment.Far => remaining,
                    _ => 0f,
                };
                textBounds.Height = lineHeight;
            }
        }

        using var foreground = new SolidBrush(foreColor);
        // Margins constrain alignment/wrapping, not glyph overhang. Clip at the
        // original caller rectangle so italic ink can use its reserved padding.
        GraphicsState state = graphics.Save();
        try
        {
            if (!format.HasFlag(LibreTextFormat.NoClipping))
            {
                graphics.SetClip(bounds, CombineMode.Intersect);
            }

            stringFormat.FormatFlags |= StringFormatFlags.NoClip;
            graphics.DrawString(text, selectedFont, foreground, textBounds, stringFormat);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    public Size MeasureText(
        Graphics? graphics,
        string text,
        Font? font,
        Size proposedSize,
        LibreTextFormat format)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateFormat(format);
        if (text.Length == 0)
        {
            return Size.Empty;
        }

        if (graphics is not null)
        {
            return MeasureTextCore(graphics, text, font ?? SystemFonts.DefaultFont, proposedSize, format);
        }

        using var target = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using Graphics measureGraphics = Graphics.FromImage(target);
        return MeasureTextCore(measureGraphics, text, font ?? SystemFonts.DefaultFont, proposedSize, format);
    }

    private static Size MeasureTextCore(
        Graphics graphics,
        string text,
        Font font,
        Size proposedSize,
        LibreTextFormat format)
    {
        using StringFormat stringFormat = CreateStringFormat(format);
        (int left, int right) = GetTextMargins(graphics, font, format);
        // DrawTextEx measures in the space remaining after both margins. Its
        // minimum content width is one, not an unconstrained paragraph.
        float width = proposedSize.Width == int.MaxValue
            ? float.MaxValue
            : Math.Max(1L, (long)proposedSize.Width - left - right);
        // DT_CALCRECT extends the bottom to the last line, even without
        // DT_SINGLELINE. A proposed height is not a fitting/trimming viewport:
        // using it here can feed a clipped height back into source AutoSize.
        SizeF measured = graphics.MeasureString(text, font, new SizeF(width, float.MaxValue), stringFormat);
        int measuredWidth = Math.Max(0, (int)MathF.Ceiling(measured.Width));
        int measuredHeight = Math.Max(0, (int)MathF.Ceiling(measured.Height));
        return new Size(checked(measuredWidth + left + right), measuredHeight);
    }

    private static StringFormat CreateStringFormat(LibreTextFormat format)
    {
        var stringFormat = format.HasFlag(LibreTextFormat.NoPadding)
            && !format.HasFlag(LibreTextFormat.LeftAndRightPadding)
            ? new StringFormat(StringFormat.GenericTypographic)
            : new StringFormat();
        stringFormat.Alignment = format.HasFlag(LibreTextFormat.Right)
            ? StringAlignment.Far
            : format.HasFlag(LibreTextFormat.HorizontalCenter)
                ? StringAlignment.Center
                : StringAlignment.Near;
        stringFormat.LineAlignment = format.HasFlag(LibreTextFormat.Bottom)
            ? StringAlignment.Far
            : format.HasFlag(LibreTextFormat.VerticalCenter)
                ? StringAlignment.Center
                : StringAlignment.Near;
        if (!format.HasFlag(LibreTextFormat.WordBreak) || format.HasFlag(LibreTextFormat.SingleLine))
        {
            stringFormat.FormatFlags |= StringFormatFlags.NoWrap;
        }

        if (format.HasFlag(LibreTextFormat.NoClipping))
        {
            stringFormat.FormatFlags |= StringFormatFlags.NoClip;
        }

        if (format.HasFlag(LibreTextFormat.RightToLeft))
        {
            stringFormat.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
        }

        stringFormat.Trimming = format switch
        {
            _ when format.HasFlag(LibreTextFormat.PathEllipsis) => StringTrimming.EllipsisPath,
            _ when format.HasFlag(LibreTextFormat.WordEllipsis) => StringTrimming.EllipsisWord,
            _ when format.HasFlag(LibreTextFormat.EndEllipsis) => StringTrimming.EllipsisCharacter,
            _ => StringTrimming.None,
        };
        stringFormat.HotkeyPrefix = format.HasFlag(LibreTextFormat.NoPrefix)
            ? HotkeyPrefix.None
            : format.HasFlag(LibreTextFormat.HidePrefix)
                ? HotkeyPrefix.Hide
                : HotkeyPrefix.Show;
        return stringFormat;
    }

    private static (int Left, int Right) GetTextMargins(Graphics graphics, Font font, LibreTextFormat format)
    {
        bool noPadding = format.HasFlag(LibreTextFormat.NoPadding);
        bool leftAndRightPadding = format.HasFlag(LibreTextFormat.LeftAndRightPadding);
        if (noPadding && !leftAndRightPadding)
        {
            return default;
        }

        // Canonical TextRenderer has already realized the source font to pixels.
        // Direct service callers retain their actual Drawing target-DPI semantics.
        int height = checked((int)MathF.Ceiling(font.GetHeight(graphics)));
        return TextRendererMargins.Get(height, noPadding, leftAndRightPadding);
    }

    private static void ValidateFormat(LibreTextFormat format)
    {
        const LibreTextFormat supported = LibreTextFormat.HorizontalCenter
            | LibreTextFormat.Right
            | LibreTextFormat.VerticalCenter
            | LibreTextFormat.Bottom
            | LibreTextFormat.SingleLine
            | LibreTextFormat.WordBreak
            | LibreTextFormat.EndEllipsis
            | LibreTextFormat.PathEllipsis
            | LibreTextFormat.WordEllipsis
            | LibreTextFormat.RightToLeft
            | LibreTextFormat.NoClipping
            | LibreTextFormat.ExpandTabs
            | LibreTextFormat.NoPrefix
            | LibreTextFormat.HidePrefix
            | LibreTextFormat.NoPadding
            | LibreTextFormat.LeftAndRightPadding
            | LibreTextFormat.TextBoxControl;
        if ((format & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }
    }
}
