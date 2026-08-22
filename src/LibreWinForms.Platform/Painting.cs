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
    void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle);

    void InvalidateAll(LibreHandle target);

    void Present(LibreHandle target);
}
