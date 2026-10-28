namespace ProGPU.SystemDrawing;

/// <summary>Original GDI font-quality requests, independent of GDI+ raster modes.</summary>
public enum DrawingGdiFontQuality
{
    Default = 0,
    Draft = 1,
    Proof = 2,
    NonAntialiased = 3,
    Antialiased = 4,
    ClearType = 5,
    ClearTypeNatural = 6,
}

/// <summary>Raw host observations. A missing snapshot or orientation remains unknown.</summary>
public readonly record struct DrawingGdiSystemSettings(
    bool FontSmoothingEnabled, int FontSmoothingType, int FontSmoothingContrast,
    int? FontSmoothingOrientation = null);

/// <summary>
/// One source request and independent paint-time system observations. This is
/// transport only: it does not select font execution, coverage or RGB filtering.
/// </summary>
public readonly record struct DrawingGdiTextPolicy
{
    public DrawingGdiTextPolicy(DrawingGdiFontQuality requestedQuality, DrawingGdiSystemSettings? systemSettings)
    {
        ValidateQuality(requestedQuality);
        RequestedQuality = requestedQuality;
        SystemSettings = systemSettings;
    }

    public DrawingGdiFontQuality RequestedQuality { get; }
    public DrawingGdiSystemSettings? SystemSettings { get; }

    public static void ValidateQuality(DrawingGdiFontQuality requestedQuality)
    {
        if (requestedQuality is < DrawingGdiFontQuality.Default or > DrawingGdiFontQuality.ClearTypeNatural)
            throw new ArgumentOutOfRangeException(nameof(requestedQuality));
    }
}
