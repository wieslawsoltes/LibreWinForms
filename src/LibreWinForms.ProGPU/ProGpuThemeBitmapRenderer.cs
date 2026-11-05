// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Drawing2D;
using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>Draws a borrowed original theme image frame through managed Graphics.</summary>
internal static class ProGpuThemeBitmapRenderer
{
    internal static bool TryDraw(Graphics graphics, Bitmap image, Rectangle source,
        LibreVisualStyleMargins margins, Rectangle destination, Rectangle? clip)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(image);
        // Validate the entire source descriptor before mutating caller state or
        // publishing any image commands. A source frame cannot borrow a sibling
        // state, and the stretch center must have real source pixels.
        if (source.X < 0 || source.Y < 0 || source.Width <= 0 || source.Height <= 0
            || (long)source.X + source.Width > image.Width || (long)source.Y + source.Height > image.Height
            || margins.Left < 0 || margins.Top < 0 || margins.Right < 0 || margins.Bottom < 0
            || (long)margins.Left + margins.Right >= source.Width
            || (long)margins.Top + margins.Bottom >= source.Height)
        {
            throw new ArgumentException("The original theme frame and sizing margins are inconsistent.");
        }

        if (destination.Width <= 0 || destination.Height <= 0) return true;
        if ((long)destination.X + destination.Width > int.MaxValue
            || (long)destination.Y + destination.Height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(destination));
        // Shrinking the fixed borders has a separate native policy. Do not
        // silently overlap them or infer a proportional contraction here.
        if (destination.Width < margins.Left + margins.Right
            || destination.Height < margins.Top + margins.Bottom) return false;

        Span<int> sx = [source.X, source.X + margins.Left, source.Right - margins.Right, source.Right];
        Span<int> sy = [source.Y, source.Y + margins.Top, source.Bottom - margins.Bottom, source.Bottom];
        Span<int> dx = [destination.X, destination.X + margins.Left,
            destination.Right - margins.Right, destination.Right];
        Span<int> dy = [destination.Y, destination.Y + margins.Top,
            destination.Bottom - margins.Bottom, destination.Bottom];
        GraphicsState saved = graphics.Save();
        try
        {
            if (clip is Rectangle rectangle) graphics.SetClip(rectangle, CombineMode.Intersect);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                int width = dx[column + 1] - dx[column], height = dy[row + 1] - dy[row];
                int sourceWidth = sx[column + 1] - sx[column], sourceHeight = sy[row + 1] - sy[row];
                if (width == 0 || height == 0 || sourceWidth == 0 || sourceHeight == 0) continue;
                graphics.DrawImage(image, new Rectangle(dx[column], dy[row], width, height),
                    sx[column], sy[row], sourceWidth, sourceHeight, GraphicsUnit.Pixel);
            }
        }
        finally { graphics.Restore(saved); }
        return true;
    }
}
