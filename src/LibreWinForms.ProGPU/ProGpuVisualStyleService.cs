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
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentOutOfRangeException.ThrowIfNegative(part);
        return bounds.Width < 0 || bounds.Height < 0 ? null : new Region(bounds);
    }
}
