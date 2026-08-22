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

    void Present(LibreHandle target);
}
