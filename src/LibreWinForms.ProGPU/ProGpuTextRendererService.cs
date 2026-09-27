// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
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

    private sealed class RetainedLayout(global::ProGPU.SystemDrawing.DrawingTextLayout layout) : ILibreTextLayout, ILibreTextRowNavigation, ILibreTextSourceGeometry
    {
        private global::ProGPU.SystemDrawing.DrawingTextLayout? _layout = layout;
        private int _selectionStart = -1;
        private int _selectionLength = -1;
        private RectangleF[] _selection = [];
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

        Rectangle textBounds = GetTextBounds(bounds, format);
        if (!backColor.IsEmpty && backColor != Color.Transparent && bounds.Width > 0 && bounds.Height > 0)
        {
            using var background = new SolidBrush(backColor);
            graphics.FillRectangle(background, bounds);
        }

        using StringFormat stringFormat = CreateStringFormat(format);
        using var foreground = new SolidBrush(foreColor);
        graphics.DrawString(text, font ?? SystemFonts.DefaultFont, foreground, textBounds, stringFormat);
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
        float width = proposedSize.Width is <= 0 or int.MaxValue
            ? float.MaxValue
            : proposedSize.Width;
        float height = proposedSize.Height is <= 0 or int.MaxValue
            ? float.MaxValue
            : proposedSize.Height;
        SizeF measured = graphics.MeasureString(text, font, new SizeF(width, height), stringFormat);
        int measuredWidth = Math.Max(0, (int)MathF.Ceiling(measured.Width));
        int measuredHeight = Math.Max(0, (int)MathF.Ceiling(measured.Height));
        if (format.HasFlag(LibreTextFormat.LeftAndRightPadding))
        {
            measuredWidth = checked(measuredWidth + 2);
        }

        return new Size(measuredWidth, measuredHeight);
    }

    private static StringFormat CreateStringFormat(LibreTextFormat format)
    {
        var stringFormat = format.HasFlag(LibreTextFormat.NoPadding)
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

    private static Rectangle GetTextBounds(Rectangle bounds, LibreTextFormat format)
    {
        Rectangle textBounds = bounds;
        if (format.HasFlag(LibreTextFormat.LeftAndRightPadding) && textBounds.Width > 2)
        {
            textBounds.Inflate(-1, 0);
        }

        return textBounds;
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
