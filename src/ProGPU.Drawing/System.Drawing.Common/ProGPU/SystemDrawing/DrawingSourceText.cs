using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using ProGPU.Text;

namespace ProGPU.SystemDrawing;

/// <summary>Explicit source text operations sharing one exact font-owned device line frame.</summary>
public static class DrawingSourceText
{
    public static bool TryGetLineMetrics(Graphics graphics, Font font,
        [NotNullWhen(true)] out SourceDeviceLineMetrics? metrics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(font);
        graphics.EnsureSourceTextTarget();
        _ = font.GetHeight(graphics);
        metrics = null;
        if (graphics.DpiX != graphics.DpiY || font.GdiVerticalFont || font.GdiCharSet is not (0 or 1) ||
            font.Unit is not (GraphicsUnit.Point or GraphicsUnit.Pixel))
            return false;
        float pixels = Graphics.ConvertFontSizeToPixels(font.Size, font.Unit, graphics.DpiY);
        if (!float.IsFinite(pixels) || pixels < 1 || pixels > ushort.MaxValue || pixels != MathF.Truncate(pixels))
            return false;
        return SourceDeviceLineMetrics.TryCreate(font.TtfFont, (ushort)pixels, out metrics);
    }

    public static void Draw(Graphics graphics, string text, Font font, Brush brush,
        RectangleF bounds, StringFormat format, SourceDeviceLineMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        graphics.DrawSourceString(text, font, brush, bounds, format, metrics);
    }

    public static SizeF Measure(Graphics graphics, string text, Font font, SizeF bounds,
        StringFormat format, SourceDeviceLineMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        return graphics.MeasureSourceString(text, font, bounds, format, metrics);
    }

    public static DrawingTextLayout CreateLayout(Graphics graphics, string text, Font font,
        SizeF bounds, StringFormat format, SourceDeviceLineMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(metrics);
        return graphics.CreateRetainedTextLayout(text, font, bounds, format, metrics);
    }
}
