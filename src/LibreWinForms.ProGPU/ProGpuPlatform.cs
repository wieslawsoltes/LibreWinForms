// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>Creates and registers the source-built ProGPU/Silk.NET WinForms backend.</summary>
public static class ProGpuPlatform
{
    public static LibrePlatformServices CreateServices()
        => CreateServices(UnsupportedLibreDesktopCaptureService.Instance);

    public static LibrePlatformServices CreateServices(
        ILibreDesktopCaptureService desktopCapture)
    {
        ArgumentNullException.ThrowIfNull(desktopCapture);
        ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        ProGpuTimerService timers = new(dispatcher);
        SilkMonitorService monitors = new();
        SilkWindowService windows = new(dispatcher, handles, monitors);
        ProGpuDesktopCaptureService captureBridge = new(desktopCapture);

        return new LibrePlatformServices(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            new ProGpuPaintService(dispatcher, handles),
            captureBridge);
    }

    public static void Register() => LibrePlatform.Register(CreateServices());

    public static void Register(ILibreDesktopCaptureService desktopCapture)
        => LibrePlatform.Register(CreateServices(desktopCapture));
}
