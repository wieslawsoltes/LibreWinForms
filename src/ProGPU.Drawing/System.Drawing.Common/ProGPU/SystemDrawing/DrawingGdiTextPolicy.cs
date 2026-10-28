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
        SystemSnapshot = systemSettings is { } value
            ? new(value.FontSmoothingEnabled ? 1U : 0U, unchecked((uint)value.FontSmoothingType),
                unchecked((uint)value.FontSmoothingContrast), value.FontSmoothingOrientation is { } orientation
                    ? unchecked((uint)orientation) : null)
            : null;
    }

    private DrawingGdiTextPolicy(DrawingGdiFontQuality quality, DrawingTextSystemSettings? snapshot, bool raw)
    {
        ValidateQuality(quality);
        RequestedQuality = quality;
        SystemSnapshot = snapshot;
        SystemSettings = snapshot is { Enabled: { } enabled, Type: { } type, Contrast: { } contrast } value
            ? new(enabled != 0, unchecked((int)type), unchecked((int)contrast),
                value.Orientation is { } orientation ? unchecked((int)orientation) : null)
            : null;
    }

    /// <summary>Preserves per-query unavailability and all original host words without overload ambiguity.</summary>
    public static DrawingGdiTextPolicy FromSystemSnapshot(DrawingGdiFontQuality quality,
        DrawingTextSystemSettings? snapshot) => new(quality, snapshot, true);

    public DrawingTextSystemSettings? SystemSnapshot { get; }

    public DrawingGdiFontQuality RequestedQuality { get; }
    public DrawingGdiSystemSettings? SystemSettings { get; }

    public static void ValidateQuality(DrawingGdiFontQuality requestedQuality)
    {
        if (requestedQuality is < DrawingGdiFontQuality.Default or > DrawingGdiFontQuality.ClearTypeNatural)
            throw new ArgumentOutOfRangeException(nameof(requestedQuality));
    }
}
