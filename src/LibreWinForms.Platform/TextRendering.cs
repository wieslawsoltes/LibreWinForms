// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;

namespace LibreWinForms.Platform;

[Flags]
public enum LibreTextFormat
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
    TextBoxControl = 65536,
}

/// <summary>Draws and measures canonical WinForms text without exposing an HDC.</summary>
public interface ILibreTextRendererService
{
    void DrawText(
        Graphics graphics,
        string text,
        Font? font,
        Rectangle bounds,
        Color foreColor,
        Color backColor,
        LibreTextFormat format);

    Size MeasureText(
        Graphics? graphics,
        string text,
        Font? font,
        Size proposedSize,
        LibreTextFormat format);
}

/// <summary>Optional owned-layout capability for source editor painting and interaction.</summary>
public interface ILibreTextLayoutService
{
    ILibreTextLayout CreateLayout(Graphics graphics, string text, Font font,
        Size layoutSize, LibreTextFormat format);
}

/// <summary>Optional capability whose owned layouts implement <see cref="ILibreTextRowNavigation"/>.</summary>
public interface ILibreTextRowNavigationService : ILibreTextLayoutService
{
}

/// <summary>Optional capability whose layouts retain original source-row geometry.</summary>
public interface ILibreTextSourceGeometryService : ILibreTextRowNavigationService
{
}

/// <summary>Optional capability whose layouts retain the native EDIT word-selection policy.</summary>
public interface ILibreEditWordBoundaryService : ILibreTextSourceGeometryService
{
}

/// <summary>One immutable word-boundary snapshot belonging to the complete owned layout generation.</summary>
/// <remarks>
/// <para>
/// Positions are strictly increasing original UTF-16 offsets, including zero and
/// the source length (one zero for empty text). LeadingContentStart is the first
/// content boundary under the same EDIT policy and is a member of Positions.
/// The borrowed memory remains immutable until its layout is disposed.
/// These boundaries are not generic Unicode word, wrapping or grapheme breaks.
/// </para>
/// </remarks>
public readonly record struct LibreEditWordBoundaries(ReadOnlyMemory<int> Positions, int LeadingContentStart);

/// <summary>EDIT word boundaries and actual source rows from the same owned text generation.</summary>
public interface ILibreEditWordBoundaryLayout : ILibreTextSourceGeometry
{
    LibreEditWordBoundaries GetWordBoundaries();
}

/// <summary>Source queries over the same owned rows, shaped clusters and alignment as drawing.</summary>
public interface ILibreTextSourceGeometry
{
    int RowCount { get; }
    int GetRowSourceStart(int rowIndex);
    int GetRowIndexFromTextPosition(int textPosition);
    int GetCaretRowIndex(int textPosition, bool trailing);
    PointF GetSourcePositionPoint(int textPosition);
}

/// <summary>Navigation over actual retained horizontal rows, including wrapping and empty rows.</summary>
public interface ILibreTextRowNavigation
{
    LibreTextCaret GetRowBoundary(int textPosition, bool trailing, bool end);
    LibreTextCaret MoveCaretVertically(int textPosition, bool trailing, int direction, float preferredX);
}

public readonly record struct LibreTextCaret(int TextPosition, bool IsTrailing,
    PointF Position, float Height, sbyte BidiLevel);

public readonly record struct LibreTextHit(int TextPosition, bool IsTrailing,
    bool IsInside, RectangleF Bounds, sbyte BidiLevel);

/// <summary>
/// Owns one complete text generation. Coordinates include format alignment but
/// exclude the caller's drawing origin. Returned selection memory is read-only.
/// </summary>
public interface ILibreTextLayout : IDisposable
{
    SizeF ContentSize { get; }
    LibreTextCaret GetCaret(int textPosition, bool trailing = false);
    LibreTextCaret MoveCaret(int textPosition, bool trailing, int visualDirection);
    LibreTextHit HitTest(PointF point);
    ReadOnlyMemory<RectangleF> GetSelectionRectangles(int start, int length);
    void Draw(Graphics graphics, PointF origin, Color color);
}

/// <summary>Explicit default for hosts that have not supplied portable text rendering.</summary>
public sealed class UnsupportedLibreTextRendererService : ILibreTextRendererService
{
    public static UnsupportedLibreTextRendererService Instance { get; } = new();

    private UnsupportedLibreTextRendererService()
    {
    }

    public void DrawText(
        Graphics graphics,
        string text,
        Font? font,
        Rectangle bounds,
        Color foreColor,
        Color backColor,
        LibreTextFormat format)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable text rendering.");

    public Size MeasureText(
        Graphics? graphics,
        string text,
        Font? font,
        Size proposedSize,
        LibreTextFormat format)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide portable text measurement.");
}
