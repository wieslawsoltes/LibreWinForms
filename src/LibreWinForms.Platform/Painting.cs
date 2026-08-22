// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Schedules drawing without leaking renderer-specific surface objects into canonical WinForms.</summary>
public interface ILibrePaintService
{
    void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle);

    void InvalidateAll(LibreHandle target);

    void Present(LibreHandle target);
}
