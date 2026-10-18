using ProGPU.Text;

namespace System.Drawing;

public partial class Graphics
{
    internal void EnsureSourceTextTarget() => EnsureNotDisposed();

    private void ValidateSourceLineFrame(Font font)
    {
        _ = font.GetHeight(this);
        if (DpiX != DpiY || font.GdiVerticalFont || font.GdiCharSet is not (0 or 1) ||
            font.Unit is not (GraphicsUnit.Point or GraphicsUnit.Pixel))
            throw new NotSupportedException("Source device line metrics require an ordinary horizontal square-pixel point or pixel font.");
    }

    internal void DrawSourceString(string text, Font font, Brush brush, RectangleF bounds,
        StringFormat format, SourceDeviceLineMetrics metrics)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(metrics);
        ValidateSourceLineFrame(font);
        DrawFormattedString(text, font, brush, bounds, format, pointAnchor: false, sourceMetrics: metrics);
    }

    internal SizeF MeasureSourceString(string text, Font font, SizeF bounds,
        StringFormat format, SourceDeviceLineMetrics metrics)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(metrics);
        ValidateSourceLineFrame(font);
        FormattedTextLayout formatted = CreateFormattedTextLayout(text, font, bounds, format, sourceMetrics: metrics);
        float width = formatted.Layout.ContentSize.X, height = formatted.Layout.ContentSize.Y;
        if (bounds.Width > 0f && float.IsFinite(bounds.Width)) width = MathF.Min(width, bounds.Width);
        if (bounds.Height > 0f && float.IsFinite(bounds.Height)) height = MathF.Min(height, bounds.Height);
        return new SizeF(width, height);
    }
}
