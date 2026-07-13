using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Specifies display and layout options for <see cref="TextRenderer"/>.
/// </summary>
[Flags]
public enum TextFormatFlags
{
    Default = 0x0000_0000,
    Top = 0x0000_0000,
    Left = 0x0000_0000,
    HorizontalCenter = 0x0000_0001,
    Right = 0x0000_0002,
    VerticalCenter = 0x0000_0004,
    Bottom = 0x0000_0008,
    WordBreak = 0x0000_0010,
    SingleLine = 0x0000_0020,
    ExpandTabs = 0x0000_0040,
    NoClipping = 0x0000_0100,
    ExternalLeading = 0x0000_0200,
    NoPrefix = 0x0000_0800,
    Internal = 0x0000_1000,
    TextBoxControl = 0x0000_2000,
    PathEllipsis = 0x0000_4000,
    EndEllipsis = 0x0000_8000,

    [Obsolete("ModifyString mutates strings and should be avoided.")]
    ModifyString = 0x0001_0000,

    RightToLeft = 0x0002_0000,
    WordEllipsis = 0x0004_0000,
    NoFullWidthCharacterBreak = 0x0008_0000,
    HidePrefix = 0x0010_0000,
    PrefixOnly = 0x0020_0000,
    PreserveGraphicsClipping = 0x0100_0000,
    PreserveGraphicsTranslateTransform = 0x0200_0000,
    GlyphOverhangPadding = 0x0000_0000,
    NoPadding = 0x1000_0000,
    LeftAndRightPadding = 0x2000_0000
}

/// <summary>
/// Provides portable text measurement and drawing over System.Drawing graphics.
/// </summary>
public static class TextRenderer
{
    public static Size MeasureText(string? text, Font? font)
    {
        using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
        return MeasureTextCore(
            graphics,
            text,
            font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.Default);
    }

    public static Size MeasureText(
        Graphics graphics,
        string? text,
        Font? font,
        Size proposedSize,
        TextFormatFlags flags)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ThrowIfModifyString(flags);
        return MeasureTextCore(graphics, text, font, proposedSize, flags);
    }

    public static void DrawText(
        Graphics graphics,
        string? text,
        Font? font,
        Point point,
        Color foreColor,
        Color backColor)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Font resolvedFont = font ?? SystemFonts.DefaultFont;
        string displayText = ApplyPrefixRules(text, TextFormatFlags.Default);
        Size measured = MeasureTextCore(
            graphics,
            displayText,
            resolvedFont,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        if (!backColor.IsEmpty && measured.Width > 0 && measured.Height > 0)
        {
            using var background = new SolidBrush(backColor);
            graphics.FillRectangle(background, point.X, point.Y, measured.Width, measured.Height);
        }

        using var foreground = new SolidBrush(foreColor);
        graphics.DrawString(displayText, resolvedFont, foreground, point.X, point.Y);
    }

    public static void DrawText(
        Graphics graphics,
        string? text,
        Font? font,
        Rectangle bounds,
        Color foreColor,
        Color backColor,
        TextFormatFlags flags)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ThrowIfModifyString(flags);
        DrawTextCore(graphics, text, font, bounds, foreColor, backColor, flags);
    }

    public static void DrawText(
        Control control,
        string? text,
        Font? font,
        Rectangle bounds,
        Color foreColor,
        Color backColor,
        TextFormatFlags flags)
    {
        ArgumentNullException.ThrowIfNull(control);
        using Graphics graphics = control.CreateGraphics();
        DrawText(graphics, text, font, bounds, foreColor, backColor, flags);
    }

    private static void DrawTextCore(
        Graphics graphics,
        string? text,
        Font? font,
        Rectangle bounds,
        Color foreColor,
        Color backColor,
        TextFormatFlags flags)
    {
        if (string.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        Font resolvedFont = font ?? SystemFonts.DefaultFont;
        string displayText = ApplyPrefixRules(text, flags);
        if (!backColor.IsEmpty)
        {
            using var background = new SolidBrush(backColor);
            graphics.FillRectangle(background, bounds);
        }

        Size measured = MeasureTextCore(
            graphics,
            displayText,
            resolvedFont,
            bounds.Size,
            flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        float x = (flags & TextFormatFlags.Right) != 0
            ? bounds.Right - measured.Width
            : (flags & TextFormatFlags.HorizontalCenter) != 0
                ? bounds.Left + ((bounds.Width - measured.Width) / 2f)
                : bounds.Left;
        float y = (flags & TextFormatFlags.Bottom) != 0
            ? bounds.Bottom - measured.Height
            : (flags & TextFormatFlags.VerticalCenter) != 0
                ? bounds.Top + ((bounds.Height - measured.Height) / 2f)
                : bounds.Top;

        using var foreground = new SolidBrush(foreColor);
        if ((flags & TextFormatFlags.NoClipping) != 0)
        {
            graphics.DrawString(displayText, resolvedFont, foreground, x, y);
        }
        else
        {
            graphics.DrawString(
                displayText,
                resolvedFont,
                foreground,
                new RectangleF(x, y, Math.Max(0f, bounds.Right - x), Math.Max(0f, bounds.Bottom - y)));
        }
    }

    private static Size MeasureTextCore(
        Graphics graphics,
        string? text,
        Font? font,
        Size proposedSize,
        TextFormatFlags flags)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Size.Empty;
        }

        Font resolvedFont = font ?? SystemFonts.DefaultFont;
        string displayText = ApplyPrefixRules(text, flags);
        float maximumWidth = proposedSize.Width > 0 && proposedSize.Width != int.MaxValue
            ? proposedSize.Width
            : float.MaxValue;
        float maximumHeight = proposedSize.Height > 0 && proposedSize.Height != int.MaxValue
            ? proposedSize.Height
            : float.MaxValue;
        SizeF measured = maximumWidth == float.MaxValue && maximumHeight == float.MaxValue
            ? graphics.MeasureString(displayText, resolvedFont)
            : graphics.MeasureString(displayText, resolvedFont, new SizeF(maximumWidth, maximumHeight));

        int horizontalPadding = GetHorizontalPadding(resolvedFont, flags);
        int width = checked((int)Math.Ceiling(measured.Width) + horizontalPadding);
        int height = checked((int)Math.Ceiling(measured.Height));
        if (proposedSize.Width > 0 && proposedSize.Width != int.MaxValue)
        {
            width = Math.Min(width, proposedSize.Width);
        }

        if (proposedSize.Height > 0 && proposedSize.Height != int.MaxValue)
        {
            height = Math.Min(height, proposedSize.Height);
        }

        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    private static int GetHorizontalPadding(Font font, TextFormatFlags flags)
    {
        if ((flags & TextFormatFlags.NoPadding) != 0)
        {
            return 0;
        }

        int overhang = Math.Max(1, (int)Math.Ceiling(font.Size / 6f));
        return (flags & TextFormatFlags.LeftAndRightPadding) != 0
            ? overhang * 4
            : overhang * 2;
    }

    private static string ApplyPrefixRules(string text, TextFormatFlags flags)
    {
        if ((flags & TextFormatFlags.NoPrefix) != 0 || text.IndexOf('&') < 0)
        {
            return text;
        }

        var result = new System.Text.StringBuilder(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (current != '&')
            {
                result.Append(current);
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] == '&')
            {
                result.Append('&');
                index++;
            }
        }

        return result.ToString();
    }

    private static void ThrowIfModifyString(TextFormatFlags flags)
    {
#pragma warning disable CS0618
        if ((flags & TextFormatFlags.ModifyString) != 0)
#pragma warning restore CS0618
        {
            throw new ArgumentOutOfRangeException(nameof(flags), flags, "ModifyString is not supported.");
        }
    }
}
