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

        if (text.Length == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var stringFormat = format.HasFlag(LibreVisualStyleTextFormat.NoPadding)
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

        Rectangle textBounds = bounds;
        if (format.HasFlag(LibreVisualStyleTextFormat.LeftAndRightPadding) && textBounds.Width > 2)
        {
            textBounds.Inflate(-1, 0);
        }

        Color textColor = disabled
            ? Color.FromArgb(255, 109, 109, 109)
            : GetColor(className, part, state, LibreVisualStyleColorProperty.Text);
        using var brush = new SolidBrush(textColor);
        graphics.DrawString(text, SystemFonts.DefaultFont, brush, textBounds, stringFormat);
    }

    private static void ValidateElement(string className, int part)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentOutOfRangeException.ThrowIfNegative(part);
    }
}
