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

    public bool IsBackgroundPartiallyTransparent(string className, int part, int state)
    {
        ValidateElement(className, part);
        return false;
    }

    private static void ValidateElement(string className, int part)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentOutOfRangeException.ThrowIfNegative(part);
    }
}
