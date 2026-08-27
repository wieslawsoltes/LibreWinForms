// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>Implements canonical WinForms text rendering through managed ProGPU System.Drawing.</summary>
public sealed class ProGpuTextRendererService : ILibreTextRendererService
{
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
