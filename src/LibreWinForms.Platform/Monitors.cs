// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Stable monitor information used by WinForms screen and DPI logic.</summary>
public readonly record struct LibreMonitor(
    string Id,
    LibreRectangle Bounds,
    LibreRectangle WorkArea,
    double DpiScale,
    bool IsPrimary);

public interface ILibreMonitorService
{
    IReadOnlyList<LibreMonitor> GetMonitors();

    LibreMonitor GetNearest(LibreRectangle bounds);
}
