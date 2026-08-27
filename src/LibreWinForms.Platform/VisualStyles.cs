// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;

namespace LibreWinForms.Platform;

public enum LibreVisualStyleSizeType
{
    Minimum,
    True,
    Draw,
}

public enum LibreVisualStyleColorProperty
{
    Border,
    Fill,
    Text,
    Accent,
}

public enum LibreVisualStyleIntegerProperty
{
    ProgressChunkSize,
    ProgressSpaceSize,
}

public enum LibreVisualStyleBooleanProperty
{
    Transparent,
    AutoSize,
    BorderOnly,
    Composited,
    BackgroundFill,
    GlyphTransparent,
    GlyphOnly,
    AlwaysShowSizingBar,
    MirrorImage,
    UniformSizing,
    IntegralSizing,
    SourceGrow,
    SourceShrink,
}

public enum LibreVisualStyleEnumProperty
{
    BackgroundType,
    BorderType,
    FillType,
    SizingType,
    HorizontalAlignment,
    ContentAlignment,
    VerticalAlignment,
    OffsetType,
    IconEffect,
    TextShadowType,
    ImageLayout,
    GlyphType,
    ImageSelectType,
    GlyphFontSizingType,
    TrueSizeScalingType,
}

public enum LibreVisualStyleFilenameProperty
{
    ImageFile,
    ImageFile1,
    ImageFile2,
    ImageFile3,
    ImageFile4,
    ImageFile5,
    StockImageFile,
    GlyphImageFile,
}

public enum LibreVisualStyleStringProperty
{
    Text,
}

public enum LibreVisualStyleFontProperty
{
    Text,
    Glyph,
}

[Flags]
public enum LibreVisualStyleHitTestOptions
{
    None = 0,
    FixedBorder = 1,
    Caption = 2,
    ResizingBorderLeft = 4,
    ResizingBorderTop = 8,
    ResizingBorderRight = 16,
    ResizingBorderBottom = 32,
    ResizingBorder = ResizingBorderLeft | ResizingBorderTop | ResizingBorderRight | ResizingBorderBottom,
    SizingTemplate = 64,
    SystemSizingMargins = 128,
}

public enum LibreVisualStyleHitTestCode
{
    Nowhere,
    Client,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public enum LibreVisualStyleTextCharacterSet
{
    Ansi = 0,
    Default = 1,
    Symbol = 2,
    Mac = 77,
    ShiftJis = 128,
    Hangul = 129,
    Johab = 130,
    Gb2312 = 134,
    ChineseBig5 = 136,
    Greek = 161,
    Turkish = 162,
    Vietnamese = 163,
    Hebrew = 177,
    Arabic = 178,
    Baltic = 186,
    Russian = 204,
    Thai = 222,
    EastEurope = 238,
    Oem = 255,
}

[Flags]
public enum LibreVisualStyleTextPitchAndFamily
{
    None = 0,
    FixedPitch = 1,
    Vector = 2,
    TrueType = 4,
    Device = 8,
}

public readonly record struct LibreVisualStyleTextMetrics(
    int Height,
    int Ascent,
    int Descent,
    int InternalLeading,
    int ExternalLeading,
    int AverageCharWidth,
    int MaxCharWidth,
    int Weight,
    int Overhang,
    int DigitizedAspectX,
    int DigitizedAspectY,
    char FirstChar,
    char LastChar,
    char DefaultChar,
    char BreakChar,
    bool Italic,
    bool Underlined,
    bool StruckOut,
    LibreVisualStyleTextPitchAndFamily PitchAndFamily,
    LibreVisualStyleTextCharacterSet CharacterSet);

public enum LibreVisualStyleMarginProperty
{
    Sizing,
    Content,
    Caption,
}

public enum LibreVisualStylePointProperty
{
    Offset,
    TextShadowOffset,
    MinimumSize,
    MinimumSize1,
    MinimumSize2,
    MinimumSize3,
    MinimumSize4,
    MinimumSize5,
}

public readonly record struct LibreVisualStyleMargins(int Left, int Top, int Right, int Bottom);

[Flags]
public enum LibreVisualStyleEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Diagonal = 16,
}

public enum LibreVisualStyleEdgeStyle
{
    Raised,
    Sunken,
    Etched,
    Bump,
}

[Flags]
public enum LibreVisualStyleEdgeEffects
{
    None = 0,
    FillInterior = 1,
    Flat = 2,
    Soft = 4,
    Mono = 8,
}

[Flags]
public enum LibreVisualStyleTextFormat
{
    Default = 0,
    HorizontalCenter = 1,
    Right = 2,
    VerticalCenter = 4,
    Bottom = 8,
    SingleLine = 16,
    WordBreak = 32,
    EndEllipsis = 64,
    PathEllipsis = 128,
    WordEllipsis = 256,
    RightToLeft = 512,
    NoClipping = 1024,
    ExpandTabs = 2048,
    NoPrefix = 4096,
    HidePrefix = 8192,
    NoPadding = 16384,
    LeftAndRightPadding = 32768,
}

/// <summary>Renders portable visual-style backgrounds without exposing UxTheme handles or device contexts.</summary>
public interface ILibreVisualStyleService
{
    bool IsEnabled { get; }

    bool IsElementDefined(string className, int part);

    void DrawBackground(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        Rectangle? clipRectangle);

    /// <summary>Returns a caller-owned managed region, or null when the element has no drawable background.</summary>
    Region? GetBackgroundRegion(string className, int part, int state, Rectangle bounds);

    Rectangle GetBackgroundContentRectangle(string className, int part, int state, Rectangle bounds);

    Size GetPartSize(
        string className,
        int part,
        int state,
        Rectangle? bounds,
        LibreVisualStyleSizeType type);

    Color GetColor(string className, int part, int state, LibreVisualStyleColorProperty property);

    int GetInteger(string className, int part, int state, LibreVisualStyleIntegerProperty property);

    bool GetBoolean(string className, int part, int state, LibreVisualStyleBooleanProperty property);

    int GetEnumValue(string className, int part, int state, LibreVisualStyleEnumProperty property);

    string GetFilename(string className, int part, int state, LibreVisualStyleFilenameProperty property);

    string GetString(string className, int part, int state, LibreVisualStyleStringProperty property);

    /// <summary>Returns a caller-owned font, or null when the theme does not define one.</summary>
    Font? GetFont(string className, int part, int state, LibreVisualStyleFontProperty property);

    Rectangle MeasureText(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle? bounds,
        string text,
        LibreVisualStyleTextFormat format);

    LibreVisualStyleHitTestCode HitTestBackground(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        Region? region,
        Point point,
        LibreVisualStyleHitTestOptions options);

    LibreVisualStyleTextMetrics GetTextMetrics(
        Graphics graphics,
        string className,
        int part,
        int state);

    LibreVisualStyleMargins GetMargins(
        string className,
        int part,
        int state,
        LibreVisualStyleMarginProperty property);

    Point GetPoint(string className, int part, int state, LibreVisualStylePointProperty property);

    bool IsBackgroundPartiallyTransparent(string className, int part, int state);

    Rectangle DrawEdge(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        LibreVisualStyleEdges edges,
        LibreVisualStyleEdgeStyle style,
        LibreVisualStyleEdgeEffects effects);

    void DrawText(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        string text,
        bool disabled,
        LibreVisualStyleTextFormat format);
}

/// <summary>Explicit default for hosts that do not provide portable visual-style rendering.</summary>
public sealed class UnsupportedLibreVisualStyleService : ILibreVisualStyleService
{
    public static UnsupportedLibreVisualStyleService Instance { get; } = new();

    private UnsupportedLibreVisualStyleService()
    {
    }

    public bool IsEnabled => false;

    public bool IsElementDefined(string className, int part) => false;

    public void DrawBackground(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        Rectangle? clipRectangle)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style background rendering.");

    public Region? GetBackgroundRegion(string className, int part, int state, Rectangle bounds)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style background regions.");

    public Rectangle GetBackgroundContentRectangle(string className, int part, int state, Rectangle bounds)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style content metrics.");

    public Size GetPartSize(
        string className,
        int part,
        int state,
        Rectangle? bounds,
        LibreVisualStyleSizeType type)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style part metrics.");

    public Color GetColor(string className, int part, int state, LibreVisualStyleColorProperty property)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style colors.");

    public int GetInteger(string className, int part, int state, LibreVisualStyleIntegerProperty property)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style integer properties.");

    public bool GetBoolean(string className, int part, int state, LibreVisualStyleBooleanProperty property)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style Boolean properties.");

    public int GetEnumValue(string className, int part, int state, LibreVisualStyleEnumProperty property)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style enum properties.");

    public string GetFilename(string className, int part, int state, LibreVisualStyleFilenameProperty property)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style filename properties.");

    public string GetString(string className, int part, int state, LibreVisualStyleStringProperty property)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style string properties.");

    public Font? GetFont(string className, int part, int state, LibreVisualStyleFontProperty property)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style font properties.");

    public Rectangle MeasureText(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle? bounds,
        string text,
        LibreVisualStyleTextFormat format)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style text measurement.");

    public LibreVisualStyleHitTestCode HitTestBackground(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        Region? region,
        Point point,
        LibreVisualStyleHitTestOptions options)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style background hit testing.");

    public LibreVisualStyleTextMetrics GetTextMetrics(
        Graphics graphics,
        string className,
        int part,
        int state)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style text metrics.");

    public LibreVisualStyleMargins GetMargins(
        string className,
        int part,
        int state,
        LibreVisualStyleMarginProperty property)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style margin properties.");

    public Point GetPoint(string className, int part, int state, LibreVisualStylePointProperty property)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style point properties.");

    public bool IsBackgroundPartiallyTransparent(string className, int part, int state)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style transparency properties.");

    public Rectangle DrawEdge(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        LibreVisualStyleEdges edges,
        LibreVisualStyleEdgeStyle style,
        LibreVisualStyleEdgeEffects effects)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style edge rendering.");

    public void DrawText(
        Graphics graphics,
        string className,
        int part,
        int state,
        Rectangle bounds,
        string text,
        bool disabled,
        LibreVisualStyleTextFormat format)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable visual-style text rendering.");
}
