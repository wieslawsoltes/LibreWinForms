// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>Creates and registers the source-built ProGPU/Silk.NET WinForms backend.</summary>
public static class ProGpuPlatform
{
    public static LibrePlatformServices CreateServices()
    {
        ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        ProGpuTimerService timers = new(dispatcher);
        SilkMonitorService monitors = new();
        SilkWindowService windows = new(dispatcher, handles, monitors);

        return new LibrePlatformServices(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            new ProGpuPaintService(dispatcher, handles));
    }

    public static void Register() => LibrePlatform.Register(CreateServices());
}
