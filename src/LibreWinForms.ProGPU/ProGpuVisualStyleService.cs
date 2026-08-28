// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Drawing2D;
using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>
/// Provides a renderer-neutral portable theme baseline. Platform-specific theme
/// integrations can replace this service without changing canonical WinForms.
/// </summary>
public sealed class ProGpuVisualStyleService : ILibreVisualStyleService
{
    public bool IsEnabled => true;

    public string ThemeFilename => "progpu.theme";

    public string ColorScheme => "NormalColor";

    public bool IsElementDefined(string className, int part)
        => !string.IsNullOrWhiteSpace(className) && part >= 0;

    public void DrawBackground(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        Rectangle? clipRectangle)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentOutOfRangeException.ThrowIfNegative(part);

        GraphicsState saved = graphics.Save();
        try
        {
            if (clipRectangle is Rectangle clip)
            {
                graphics.SetClip(clip, CombineMode.Intersect);
            }

            Color fillColor = state switch
            {
                2 => Color.FromArgb(255, 229, 241, 251),
                3 => Color.FromArgb(255, 204, 228, 247),
                4 => Color.FromArgb(255, 240, 240, 240),
                _ => Color.FromArgb(255, 250, 250, 250),
            };

            using var fill = new SolidBrush(fillColor);
            graphics.FillRectangle(fill, bounds);

            if (bounds.Width > 0 && bounds.Height > 0)
            {
                using var border = new Pen(Color.FromArgb(255, 112, 112, 112));
                graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            }
        }
        finally
        {
            graphics.Restore(saved);
        }
    }

    public Region? GetBackgroundRegion(string className, int part, int state, Rectangle bounds)
    {
        ValidateElement(className, part);
        return bounds.Width < 0 || bounds.Height < 0 ? null : new Region(bounds);
    }

    public Rectangle GetBackgroundContentRectangle(string className, int part, int state, Rectangle bounds)
    {
        ValidateElement(className, part);
        int horizontalInset = Math.Min(3, Math.Max(0, bounds.Width / 2));
        int verticalInset = Math.Min(3, Math.Max(0, bounds.Height / 2));
        return Rectangle.Inflate(bounds, -horizontalInset, -verticalInset);
    }

    public Rectangle GetBackgroundExtent(string className, int part, int state, Rectangle contentBounds)
    {
        ValidateElement(className, part);
        return Rectangle.Inflate(contentBounds, 3, 3);
    }

    public Size GetPartSize(
        string className,
        int part,
        int state,
        Rectangle? bounds,
        LibreVisualStyleSizeType type)
    {
        ValidateElement(className, part);
        if (type is < LibreVisualStyleSizeType.Minimum or > LibreVisualStyleSizeType.Draw)
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        return className.ToUpperInvariant() switch
        {
            "BUTTON" when part is 2 or 3 => new Size(13, 13),
            "BUTTON" when part == 1 => new Size(75, 23),
            "SCROLLBAR" => new Size(17, 17),
            "TRACKBAR" => new Size(16, 16),
            "EDIT" or "COMBOBOX" => new Size(100, 23),
            _ => new Size(16, 16),
        };
    }

    public Color GetColor(string className, int part, int state, LibreVisualStyleColorProperty property)
    {
        ValidateElement(className, part);
        if (property is < LibreVisualStyleColorProperty.Border or > LibreVisualStyleColorProperty.Accent)
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }

        return property switch
        {
            LibreVisualStyleColorProperty.Border => Color.FromArgb(255, 112, 112, 112),
            LibreVisualStyleColorProperty.Fill => state == 4
                ? Color.FromArgb(255, 240, 240, 240)
                : Color.FromArgb(255, 250, 250, 250),
            LibreVisualStyleColorProperty.Text => state == 4
                ? Color.FromArgb(255, 109, 109, 109)
                : Color.FromArgb(255, 0, 0, 0),
            LibreVisualStyleColorProperty.Accent => Color.FromArgb(255, 0, 120, 215),
            _ => throw new ArgumentOutOfRangeException(nameof(property)),
        };
    }

    public int GetInteger(string className, int part, int state, LibreVisualStyleIntegerProperty property)
    {
        ValidateElement(className, part);
        if (property is < LibreVisualStyleIntegerProperty.ProgressChunkSize
            or > LibreVisualStyleIntegerProperty.ProgressSpaceSize)
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }

        return property switch
        {
            LibreVisualStyleIntegerProperty.ProgressChunkSize => 6,
            LibreVisualStyleIntegerProperty.ProgressSpaceSize => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(property)),
        };
    }

    public bool GetBoolean(string className, int part, int state, LibreVisualStyleBooleanProperty property)
    {
        ValidateElement(className, part);
        if (property is < LibreVisualStyleBooleanProperty.Transparent or > LibreVisualStyleBooleanProperty.SourceShrink)
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }

        return property switch
        {
            LibreVisualStyleBooleanProperty.BackgroundFill
                or LibreVisualStyleBooleanProperty.GlyphTransparent
                or LibreVisualStyleBooleanProperty.UniformSizing
                or LibreVisualStyleBooleanProperty.SourceGrow
                or LibreVisualStyleBooleanProperty.SourceShrink => true,
            _ => false,
        };
    }

    public int GetEnumValue(string className, int part, int state, LibreVisualStyleEnumProperty property)
    {
        ValidateElement(className, part);
        if (property is < LibreVisualStyleEnumProperty.BackgroundType or > LibreVisualStyleEnumProperty.TrueSizeScalingType)
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }

        return property switch
        {
            // The baseline renderer paints a stretched border/fill background.
            LibreVisualStyleEnumProperty.BackgroundType => 1,
            LibreVisualStyleEnumProperty.SizingType => 1,
            _ => 0,
        };
    }

    public string GetFilename(string className, int part, int state, LibreVisualStyleFilenameProperty property)
    {
        ValidateElement(className, part);
        if (property is < LibreVisualStyleFilenameProperty.ImageFile or > LibreVisualStyleFilenameProperty.GlyphImageFile)
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }

        return string.Empty;
    }

    public string GetString(string className, int part, int state, LibreVisualStyleStringProperty property)
    {
        ValidateElement(className, part);
        if (property != LibreVisualStyleStringProperty.Text)
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }

        return string.Empty;
    }

    public Font? GetFont(string className, int part, int state, LibreVisualStyleFontProperty property)
    {
        ValidateElement(className, part);
        if (property is < LibreVisualStyleFontProperty.Text or > LibreVisualStyleFontProperty.Glyph)
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }

        return (Font)SystemFonts.DefaultFont.Clone();
    }

    public Rectangle MeasureText(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle? bounds,
        string text,
        LibreVisualStyleTextFormat format)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ValidateElement(className, part);
        ArgumentNullException.ThrowIfNull(text);
        ValidateTextFormat(format);

        using StringFormat stringFormat = CreateTextFormat(format);
        Rectangle? textBounds = bounds is Rectangle layoutBounds
            ? GetTextBounds(layoutBounds, format)
            : null;
        SizeF measured = textBounds is Rectangle constrained
            ? graphics.MeasureString(
                text,
                SystemFonts.DefaultFont,
                new SizeF(Math.Max(0, constrained.Width), Math.Max(0, constrained.Height)),
                stringFormat)
            : graphics.MeasureString(text, SystemFonts.DefaultFont, PointF.Empty, stringFormat);
        int width = Math.Max(0, (int)MathF.Ceiling(measured.Width));
        int height = Math.Max(0, (int)MathF.Ceiling(measured.Height));
        if (textBounds is not Rectangle positioned)
        {
            return new Rectangle(0, 0, width, height);
        }

        int x = format.HasFlag(LibreVisualStyleTextFormat.Right)
            ? positioned.Right - width
            : format.HasFlag(LibreVisualStyleTextFormat.HorizontalCenter)
                ? positioned.Left + ((positioned.Width - width) / 2)
                : positioned.Left;
        int y = format.HasFlag(LibreVisualStyleTextFormat.Bottom)
            ? positioned.Bottom - height
            : format.HasFlag(LibreVisualStyleTextFormat.VerticalCenter)
                ? positioned.Top + ((positioned.Height - height) / 2)
                : positioned.Top;
        return new Rectangle(x, y, width, height);
    }

    public LibreVisualStyleHitTestCode HitTestBackground(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        Region? region,
        Point point,
        LibreVisualStyleHitTestOptions options)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ValidateElement(className, part);
        const LibreVisualStyleHitTestOptions supported = LibreVisualStyleHitTestOptions.FixedBorder
            | LibreVisualStyleHitTestOptions.Caption
            | LibreVisualStyleHitTestOptions.ResizingBorder
            | LibreVisualStyleHitTestOptions.SizingTemplate
            | LibreVisualStyleHitTestOptions.SystemSizingMargins;
        if ((options & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (!bounds.Contains(point) || (region is not null && !region.IsVisible(point, graphics)))
        {
            return LibreVisualStyleHitTestCode.Nowhere;
        }

        int horizontalBorder = Math.Min(3, Math.Max(1, (bounds.Width + 1) / 2));
        int verticalBorder = Math.Min(3, Math.Max(1, (bounds.Height + 1) / 2));
        bool left = options.HasFlag(LibreVisualStyleHitTestOptions.ResizingBorderLeft)
            && point.X < bounds.Left + horizontalBorder;
        bool right = options.HasFlag(LibreVisualStyleHitTestOptions.ResizingBorderRight)
            && point.X >= bounds.Right - horizontalBorder;
        bool top = options.HasFlag(LibreVisualStyleHitTestOptions.ResizingBorderTop)
            && point.Y < bounds.Top + verticalBorder;
        bool bottom = options.HasFlag(LibreVisualStyleHitTestOptions.ResizingBorderBottom)
            && point.Y >= bounds.Bottom - verticalBorder;

        if (top && left)
            return LibreVisualStyleHitTestCode.TopLeft;
        if (top && right)
            return LibreVisualStyleHitTestCode.TopRight;
        if (bottom && left)
            return LibreVisualStyleHitTestCode.BottomLeft;
        if (bottom && right)
            return LibreVisualStyleHitTestCode.BottomRight;
        if (left)
            return LibreVisualStyleHitTestCode.Left;
        if (right)
            return LibreVisualStyleHitTestCode.Right;
        if (top)
            return LibreVisualStyleHitTestCode.Top;
        if (bottom)
            return LibreVisualStyleHitTestCode.Bottom;

        return LibreVisualStyleHitTestCode.Client;
    }

    public LibreVisualStyleTextMetrics GetTextMetrics(
        Graphics graphics,
        string className,
        int part,
        int state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ValidateElement(className, part);

        Font font = SystemFonts.DefaultFont;
        FontStyle style = font.Style;
        int emHeight = font.FontFamily.GetEmHeight(style);
        int lineSpacing = font.FontFamily.GetLineSpacing(style);
        float lineHeight = font.GetHeight(graphics);
        float unitScale = lineSpacing == 0 ? 0f : lineHeight / lineSpacing;
        int height = Math.Max(0, (int)MathF.Ceiling(lineHeight));
        int ascent = Math.Max(0, (int)MathF.Ceiling(font.FontFamily.GetCellAscent(style) * unitScale));
        int descent = Math.Max(0, (int)MathF.Ceiling(font.FontFamily.GetCellDescent(style) * unitScale));
        int externalLeading = Math.Max(0, height - ascent - descent);

        using StringFormat format = CreateTextFormat(LibreVisualStyleTextFormat.NoPadding | LibreVisualStyleTextFormat.SingleLine);
        float alphabetWidth = graphics.MeasureString("abcdefghijklmnopqrstuvwxyz", font, PointF.Empty, format).Width;
        int averageWidth = Math.Max(0, (int)MathF.Ceiling(alphabetWidth / 26f));
        int maxWidth = Math.Max(
            averageWidth,
            (int)MathF.Ceiling(graphics.MeasureString("W", font, PointF.Empty, format).Width));

        return new LibreVisualStyleTextMetrics(
            Height: height,
            Ascent: ascent,
            Descent: descent,
            InternalLeading: 0,
            ExternalLeading: externalLeading,
            AverageCharWidth: averageWidth,
            MaxCharWidth: maxWidth,
            Weight: font.Bold ? 700 : 400,
            Overhang: 0,
            DigitizedAspectX: Math.Max(0, (int)MathF.Round(graphics.DpiX)),
            DigitizedAspectY: Math.Max(0, (int)MathF.Round(graphics.DpiY)),
            FirstChar: ' ',
            LastChar: '\uFFFF',
            DefaultChar: '\uFFFD',
            BreakChar: ' ',
            Italic: font.Italic,
            Underlined: font.Underline,
            StruckOut: font.Strikeout,
            PitchAndFamily: emHeight > 0
                ? LibreVisualStyleTextPitchAndFamily.TrueType
                : LibreVisualStyleTextPitchAndFamily.Vector,
            CharacterSet: GetTextCharacterSet(font.GdiCharSet));
    }

    public LibreVisualStyleMargins GetMargins(
        string className,
        int part,
        int state,
        LibreVisualStyleMarginProperty property)
    {
        ValidateElement(className, part);
        if (property is < LibreVisualStyleMarginProperty.Sizing or > LibreVisualStyleMarginProperty.Caption)
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }

        return property switch
        {
            LibreVisualStyleMarginProperty.Sizing => new(3, 3, 3, 3),
            LibreVisualStyleMarginProperty.Content => new(3, 3, 3, 3),
            LibreVisualStyleMarginProperty.Caption => new(2, 2, 2, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(property)),
        };
    }

    public Point GetPoint(string className, int part, int state, LibreVisualStylePointProperty property)
    {
        ValidateElement(className, part);
        if (property is < LibreVisualStylePointProperty.Offset or > LibreVisualStylePointProperty.MinimumSize5)
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }

        if (property is LibreVisualStylePointProperty.Offset or LibreVisualStylePointProperty.TextShadowOffset)
        {
            return Point.Empty;
        }

        Size minimum = GetPartSize(className, part, state, bounds: null, LibreVisualStyleSizeType.Minimum);
        return new Point(minimum.Width, minimum.Height);
    }

    public bool IsBackgroundPartiallyTransparent(string className, int part, int state)
    {
        ValidateElement(className, part);
        return false;
    }

    public Rectangle DrawEdge(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        LibreVisualStyleEdges edges,
        LibreVisualStyleEdgeStyle style,
        LibreVisualStyleEdgeEffects effects)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ValidateElement(className, part);
        if ((edges & ~(LibreVisualStyleEdges.Left
            | LibreVisualStyleEdges.Top
            | LibreVisualStyleEdges.Right
            | LibreVisualStyleEdges.Bottom
            | LibreVisualStyleEdges.Diagonal)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(edges));
        }

        if (style is < LibreVisualStyleEdgeStyle.Raised or > LibreVisualStyleEdgeStyle.Bump)
        {
            throw new ArgumentOutOfRangeException(nameof(style));
        }

        if ((effects & ~(LibreVisualStyleEdgeEffects.FillInterior
            | LibreVisualStyleEdgeEffects.Flat
            | LibreVisualStyleEdgeEffects.Soft
            | LibreVisualStyleEdgeEffects.Mono)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(effects));
        }

        if (bounds.Width <= 0 || bounds.Height <= 0 || edges == LibreVisualStyleEdges.None)
        {
            return bounds;
        }

        Color light = effects.HasFlag(LibreVisualStyleEdgeEffects.Mono)
            ? Color.White
            : Color.FromArgb(255, 255, 255, 255);
        Color dark = effects.HasFlag(LibreVisualStyleEdgeEffects.Mono)
            ? Color.Black
            : Color.FromArgb(255, 105, 105, 105);
        bool raised = style is LibreVisualStyleEdgeStyle.Raised or LibreVisualStyleEdgeStyle.Bump;
        Color leading = raised ? light : dark;
        Color trailing = raised ? dark : light;

        using var leadingPen = new Pen(leading);
        using var trailingPen = new Pen(trailing);
        int right = bounds.Right - 1;
        int bottom = bounds.Bottom - 1;

        if (edges.HasFlag(LibreVisualStyleEdges.Left))
        {
            graphics.DrawLine(leadingPen, bounds.Left, bounds.Top, bounds.Left, bottom);
        }

        if (edges.HasFlag(LibreVisualStyleEdges.Top))
        {
            graphics.DrawLine(leadingPen, bounds.Left, bounds.Top, right, bounds.Top);
        }

        if (edges.HasFlag(LibreVisualStyleEdges.Right))
        {
            graphics.DrawLine(trailingPen, right, bounds.Top, right, bottom);
        }

        if (edges.HasFlag(LibreVisualStyleEdges.Bottom))
        {
            graphics.DrawLine(trailingPen, bounds.Left, bottom, right, bottom);
        }

        if (edges.HasFlag(LibreVisualStyleEdges.Diagonal))
        {
            graphics.DrawLine(trailingPen, bounds.Left, bottom, right, bounds.Top);
        }

        if (effects.HasFlag(LibreVisualStyleEdgeEffects.FillInterior))
        {
            Rectangle interior = Rectangle.Inflate(bounds, -1, -1);
            if (interior.Width > 0 && interior.Height > 0)
            {
                using var fill = new SolidBrush(GetColor(className, part, state, LibreVisualStyleColorProperty.Fill));
                graphics.FillRectangle(fill, interior);
            }
        }

        int leftInset = edges.HasFlag(LibreVisualStyleEdges.Left) ? 1 : 0;
        int topInset = edges.HasFlag(LibreVisualStyleEdges.Top) ? 1 : 0;
        int rightInset = edges.HasFlag(LibreVisualStyleEdges.Right) ? 1 : 0;
        int bottomInset = edges.HasFlag(LibreVisualStyleEdges.Bottom) ? 1 : 0;
        return Rectangle.FromLTRB(
            bounds.Left + leftInset,
            bounds.Top + topInset,
            bounds.Right - rightInset,
            bounds.Bottom - bottomInset);
    }

    public void DrawText(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        string text,
        bool disabled,
        LibreVisualStyleTextFormat format)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ValidateElement(className, part);
        ArgumentNullException.ThrowIfNull(text);
        ValidateTextFormat(format);

        if (text.Length == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using StringFormat stringFormat = CreateTextFormat(format);
        Rectangle textBounds = GetTextBounds(bounds, format);
        Color textColor = disabled
            ? Color.FromArgb(255, 109, 109, 109)
            : GetColor(className, part, state, LibreVisualStyleColorProperty.Text);
        using var brush = new SolidBrush(textColor);
        graphics.DrawString(text, SystemFonts.DefaultFont, brush, textBounds, stringFormat);
    }

    private static StringFormat CreateTextFormat(LibreVisualStyleTextFormat format)
    {
        var stringFormat = format.HasFlag(LibreVisualStyleTextFormat.NoPadding)
            ? new StringFormat(StringFormat.GenericTypographic)
            : new StringFormat();
        stringFormat.Alignment = format.HasFlag(LibreVisualStyleTextFormat.Right)
            ? StringAlignment.Far
            : format.HasFlag(LibreVisualStyleTextFormat.HorizontalCenter)
                ? StringAlignment.Center
                : StringAlignment.Near;
        stringFormat.LineAlignment = format.HasFlag(LibreVisualStyleTextFormat.Bottom)
            ? StringAlignment.Far
            : format.HasFlag(LibreVisualStyleTextFormat.VerticalCenter)
                ? StringAlignment.Center
                : StringAlignment.Near;
        if (format.HasFlag(LibreVisualStyleTextFormat.SingleLine))
        {
            stringFormat.FormatFlags |= StringFormatFlags.NoWrap;
        }

        if (format.HasFlag(LibreVisualStyleTextFormat.NoClipping))
        {
            stringFormat.FormatFlags |= StringFormatFlags.NoClip;
        }

        if (format.HasFlag(LibreVisualStyleTextFormat.RightToLeft))
        {
            stringFormat.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
        }

        stringFormat.Trimming = format switch
        {
            _ when format.HasFlag(LibreVisualStyleTextFormat.PathEllipsis) => StringTrimming.EllipsisPath,
            _ when format.HasFlag(LibreVisualStyleTextFormat.WordEllipsis) => StringTrimming.EllipsisWord,
            _ when format.HasFlag(LibreVisualStyleTextFormat.EndEllipsis) => StringTrimming.EllipsisCharacter,
            _ => StringTrimming.None,
        };

        stringFormat.HotkeyPrefix = format.HasFlag(LibreVisualStyleTextFormat.NoPrefix)
            ? System.Drawing.Text.HotkeyPrefix.None
            : format.HasFlag(LibreVisualStyleTextFormat.HidePrefix)
                ? System.Drawing.Text.HotkeyPrefix.Hide
                : System.Drawing.Text.HotkeyPrefix.Show;

        return stringFormat;
    }

    private static Rectangle GetTextBounds(Rectangle bounds, LibreVisualStyleTextFormat format)
    {
        Rectangle textBounds = bounds;
        if (format.HasFlag(LibreVisualStyleTextFormat.LeftAndRightPadding) && textBounds.Width > 2)
        {
            textBounds.Inflate(-1, 0);
        }

        return textBounds;
    }

    private static void ValidateTextFormat(LibreVisualStyleTextFormat format)
    {
        const LibreVisualStyleTextFormat supported =
            LibreVisualStyleTextFormat.HorizontalCenter
            | LibreVisualStyleTextFormat.Right
            | LibreVisualStyleTextFormat.VerticalCenter
            | LibreVisualStyleTextFormat.Bottom
            | LibreVisualStyleTextFormat.SingleLine
            | LibreVisualStyleTextFormat.WordBreak
            | LibreVisualStyleTextFormat.EndEllipsis
            | LibreVisualStyleTextFormat.PathEllipsis
            | LibreVisualStyleTextFormat.WordEllipsis
            | LibreVisualStyleTextFormat.RightToLeft
            | LibreVisualStyleTextFormat.NoClipping
            | LibreVisualStyleTextFormat.ExpandTabs
            | LibreVisualStyleTextFormat.NoPrefix
            | LibreVisualStyleTextFormat.HidePrefix
            | LibreVisualStyleTextFormat.NoPadding
            | LibreVisualStyleTextFormat.LeftAndRightPadding;
        if ((format & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static LibreVisualStyleTextCharacterSet GetTextCharacterSet(byte characterSet)
        => characterSet switch
        {
            0 => LibreVisualStyleTextCharacterSet.Ansi,
            1 => LibreVisualStyleTextCharacterSet.Default,
            2 => LibreVisualStyleTextCharacterSet.Symbol,
            77 => LibreVisualStyleTextCharacterSet.Mac,
            128 => LibreVisualStyleTextCharacterSet.ShiftJis,
            129 => LibreVisualStyleTextCharacterSet.Hangul,
            130 => LibreVisualStyleTextCharacterSet.Johab,
            134 => LibreVisualStyleTextCharacterSet.Gb2312,
            136 => LibreVisualStyleTextCharacterSet.ChineseBig5,
            161 => LibreVisualStyleTextCharacterSet.Greek,
            162 => LibreVisualStyleTextCharacterSet.Turkish,
            163 => LibreVisualStyleTextCharacterSet.Vietnamese,
            177 => LibreVisualStyleTextCharacterSet.Hebrew,
            178 => LibreVisualStyleTextCharacterSet.Arabic,
            186 => LibreVisualStyleTextCharacterSet.Baltic,
            204 => LibreVisualStyleTextCharacterSet.Russian,
            222 => LibreVisualStyleTextCharacterSet.Thai,
            238 => LibreVisualStyleTextCharacterSet.EastEurope,
            255 => LibreVisualStyleTextCharacterSet.Oem,
            _ => LibreVisualStyleTextCharacterSet.Default,
        };

    private static void ValidateElement(string className, int part)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentOutOfRangeException.ThrowIfNegative(part);
    }
}
