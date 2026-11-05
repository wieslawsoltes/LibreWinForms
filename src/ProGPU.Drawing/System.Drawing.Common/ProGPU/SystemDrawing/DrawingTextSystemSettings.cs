using System.Drawing;
using System.Drawing.Text;

namespace ProGPU.SystemDrawing;

/// <summary>Independent raw host observations; null means the individual query was unavailable.</summary>
public readonly record struct DrawingTextSystemSettings(
    uint? Enabled, uint? Type, uint? Contrast, uint? Orientation);

/// <summary>GDI+ source state and independent system observations at actual paint, not a raster-mode alias.</summary>
public readonly record struct DrawingGdiPlusTextPolicy(
    TextRenderingHint Hint, int TextContrast, DrawingTextSystemSettings? SystemSettings);

/// <summary>Binds a borrowed host observation source to one actual Graphics target.</summary>
public static class DrawingTextSystemPolicy
{
    /// <summary>Reads independent current Windows host words; other hosts remain unavailable.</summary>
    public static DrawingTextSystemSettings? CaptureCurrentSystemSettings()
        => WindowsTextSystemSettings.Capture();

    /// <summary>
    /// Changes only system-policy observation. The provider is queried at SystemDefault/ClearType paint,
    /// never during measurement, and is released when this Graphics is disposed. Null removes it.
    /// </summary>
    public static void Bind(Graphics graphics, Func<DrawingTextSystemSettings?>? capture)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        graphics.BindTextSystemSettings(capture);
    }
}
