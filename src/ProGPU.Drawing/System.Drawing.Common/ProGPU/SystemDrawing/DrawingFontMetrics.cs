using System.Drawing;

namespace ProGPU.SystemDrawing;

/// <summary>Explicit font-owned metrics for source APIs whose policy differs from Drawing line spacing.</summary>
public static class DrawingFontMetrics
{
    /// <summary>
    /// Reads an exact static TrueType VDMX height for an ordinary ANSI/default point font.
    /// Unavailable metadata and other source contracts return false without altering the font.
    /// Malformed font metadata fails rather than selecting a rounded line-height replacement.
    /// </summary>
    public static bool TryGetAutoScaleHeight(Font font, int referenceDpi, out int height)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referenceDpi);
        height = 0;
        if (font.Unit != GraphicsUnit.Point || font.GdiVerticalFont || font.GdiCharSet is not (0 or 1))
            return false;
        // Preserve upstream FontCache.Data's floating expression and ceiling of
        // the requested em size. Do not round the completed line metric.
        double pixels = Math.Ceiling(referenceDpi * font.SizeInPoints / 72);
        if (!double.IsFinite(pixels) || pixels < 1 || pixels > ushort.MaxValue ||
            !font.TtfFont.TryGetVerticalDeviceMetrics((ushort)pixels, out var metrics))
            return false;
        height = metrics.Height;
        return true;
    }
}
