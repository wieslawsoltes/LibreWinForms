using System.Drawing;
using ProGPU.Text;

namespace ProGPU.SystemDrawing;

/// <summary>
/// Records source GDI text separately from ordinary GDI+ DrawString policy.
/// Source font quality, hint execution and raster parity remain independent
/// contracts; an optional exact device line frame never selects raster policy.
/// </summary>
public static class DrawingGdiText
{
    public static void Draw(Graphics graphics, string text, Font font, Brush brush,
        RectangleF bounds, StringFormat format, SourceDeviceLineMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        graphics.DrawGdiSourceString(text, font, brush, bounds, format, metrics);
    }

    public static DrawingTextLayout CreateLayout(Graphics graphics, string text, Font font,
        SizeF bounds, StringFormat format, SourceDeviceLineMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(format);
        return graphics.CreateRetainedTextLayout(text, font, bounds, format, metrics, sourceGdiText: true);
    }
}
