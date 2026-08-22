// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>Explicit boundary for the still-pending Silk.NET monitor inventory.</summary>
public sealed class SilkMonitorService : ILibreMonitorService
{
    public IReadOnlyList<LibreMonitor> GetMonitors()
        => throw new PlatformNotSupportedException("Silk.NET monitor enumeration has not been connected yet.");

    public LibreMonitor GetNearest(LibreRectangle bounds)
        => throw new PlatformNotSupportedException("Silk.NET monitor selection has not been connected yet.");
}
