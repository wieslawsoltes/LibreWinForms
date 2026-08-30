// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>
/// A renderer-owned drawing frame exposed through the normal System.Drawing API.
/// The frame and its Graphics instance are valid only for the paint callback.
/// </summary>
public interface ILibrePaintFrame
{
    System.Drawing.Graphics Graphics { get; }

    LibreRectangle SurfaceBounds { get; }

    LibreRectangle DirtyRectangle { get; }
}

/// <summary>
/// A retained paint frame that stores independently replaceable control layers.
/// Canonical WinForms opens every visible control once in back-to-front order;
/// a missing control is removed when the frame completes.
/// </summary>
public interface ILibreRetainedPaintFrame : ILibrePaintFrame
{
    /// <summary>
    /// Opens or reuses the retained layer identified by <paramref name="target"/>.
    /// <paramref name="bounds"/> and <paramref name="clipRectangle"/> are in
    /// window coordinates. A null <see cref="ILibrePaintLayer.Graphics"/> means
    /// that the existing command recording does not intersect the dirty region
    /// and must be retained unchanged.
    /// </summary>
    ILibrePaintLayer OpenLayer(
        LibreHandle target,
        LibreRectangle bounds,
        LibreRectangle clipRectangle);
}

/// <summary>A frame-scoped retained control layer.</summary>
public interface ILibrePaintLayer : IDisposable
{
    /// <summary>
    /// Gets a recorder when this layer requires repainting; otherwise null when
    /// the renderer retained the existing recording.
    /// </summary>
    System.Drawing.Graphics? Graphics { get; }
}

/// <summary>Schedules drawing without leaking renderer-specific surface objects into canonical WinForms.</summary>
public interface ILibrePaintService
{
    /// <summary>
    /// Creates a disposable Graphics recorder whose local origin maps to
    /// <paramref name="origin"/> and whose visible region is the supplied
    /// window-space clip. A window target commits recorded commands for
    /// presentation when the Graphics is disposed; a logical target may return
    /// a detached recorder for measurement and off-screen use.
    /// </summary>
    System.Drawing.Graphics CreateGraphics(
        LibreHandle target,
        LibrePoint origin,
        LibreRectangle clipRectangle);

    void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle);

    void InvalidateAll(LibreHandle target);

    /// <summary>
    /// Synchronously processes paint already pending for the target window on
    /// its owning dispatcher and returns after the backend presentation attempt
    /// completes. This method does not invalidate an otherwise clean window.
    /// </summary>
    void Present(LibreHandle target);
}

/// <summary>The frame styles supported by canonical reversible screen feedback.</summary>
public enum LibreReversibleFrameStyle
{
    Thick,
    Dashed,
}

/// <summary>An exact non-premultiplied ARGB color transported to a paint backend.</summary>
public readonly record struct LibreArgbColor(int Value);

/// <summary>
/// Draws transient screen-space feedback that is removed by issuing the same
/// operation again. Implementations own the overlay or compositing strategy.
/// </summary>
public interface ILibreReversibleDrawingService
{
    void DrawFrame(LibreRectangle rectangle, LibreArgbColor backColor, LibreReversibleFrameStyle style);

    void DrawLine(LibrePoint start, LibrePoint end, LibreArgbColor backColor);

    void FillRectangle(LibreRectangle rectangle, LibreArgbColor backColor);
}

/// <summary>Explicit default for hosts without reversible screen overlays.</summary>
public sealed class UnsupportedLibreReversibleDrawingService : ILibreReversibleDrawingService
{
    public static UnsupportedLibreReversibleDrawingService Instance { get; } = new();

    private UnsupportedLibreReversibleDrawingService()
    {
    }

    public void DrawFrame(LibreRectangle rectangle, LibreArgbColor backColor, LibreReversibleFrameStyle style)
        => ThrowUnsupported();

    public void DrawLine(LibrePoint start, LibrePoint end, LibreArgbColor backColor)
        => ThrowUnsupported();

    public void FillRectangle(LibreRectangle rectangle, LibreArgbColor backColor)
        => ThrowUnsupported();

    private static void ThrowUnsupported()
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide reversible screen drawing.");
}
