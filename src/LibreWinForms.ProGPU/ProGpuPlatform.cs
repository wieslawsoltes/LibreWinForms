// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>Creates and registers the source-built ProGPU/Silk.NET WinForms backend.</summary>
public static class ProGpuPlatform
{
    public static LibrePlatformServices CreateServices()
        => CreateServices(
            UnsupportedLibreDesktopCaptureService.Instance,
            UnsupportedLibreNativeFontInteropService.Instance,
            UnsupportedLibreNativeGraphicsInteropService.Instance);

    public static LibrePlatformServices CreateServices(
        ILibreDesktopCaptureService desktopCapture)
        => CreateServices(
            desktopCapture,
            UnsupportedLibreNativeFontInteropService.Instance,
            UnsupportedLibreNativeGraphicsInteropService.Instance);

    public static LibrePlatformServices CreateServices(
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics)
    {
        ArgumentNullException.ThrowIfNull(desktopCapture);
        ArgumentNullException.ThrowIfNull(nativeFonts);
        ArgumentNullException.ThrowIfNull(nativeGraphics);
        ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        ProGpuTimerService timers = new(dispatcher);
        SilkMonitorService monitors = new();
        SilkWindowService windows = new(dispatcher, handles, monitors);
        ProGpuPaintService painting = new(dispatcher, handles);
        ProGpuTextRendererService textRenderer = new();
        ILibreFileDialogService fileDialogs = OperatingSystem.IsLinux()
            ? new PreferredLinuxLibreFileDialogService(
                new XdgDesktopPortalLibreFileDialogService(
                    dispatcher,
                    new TmdsXdgFileChooserPortal(),
                    new ProGpuXdgPortalParentWindowProvider(
                        handles,
                        new LibWaylandXdgForeignPortalParentExporter(),
                        ownsWayland: true)),
                new ZenityLibreFileDialogService(dispatcher))
            : OperatingSystem.IsMacOS()
                ? new MacOsAppKitFileDialogService(dispatcher, handles)
                : UnsupportedLibreFileDialogService.Instance;
        ProGpuDesktopCaptureService captureBridge = new(desktopCapture);
        ProGpuNativeDrawingInteropService nativeBridge;
        try
        {
            nativeBridge = new ProGpuNativeDrawingInteropService(nativeFonts, nativeGraphics);
        }
        catch
        {
            captureBridge.Dispose();
            if (fileDialogs is IDisposable disposableFileDialogs)
            {
                disposableFileDialogs.Dispose();
            }

            throw;
        }

        return new LibrePlatformServices(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            captureBridge,
            nativeBridge,
            nativeBridge,
            new ProGpuVisualStyleService(),
            DefaultLibreSystemSettingsService.Instance,
            textRenderer,
            DefaultLibrePowerStatusService.Instance,
            new ManagedLibreMessageBoxService(
                dispatcher,
                handles,
                windows,
                monitors,
                painting,
                textRenderer),
            new ManagedLibreColorDialogService(
                dispatcher,
                handles,
                windows,
                monitors,
                painting,
                textRenderer),
            new ManagedLibreFontDialogService(
                dispatcher,
                handles,
                windows,
                monitors,
                painting,
                textRenderer,
                new ProGpuFontCatalog()),
            fileDialogs);
    }

    public static void Register() => LibrePlatform.Register(CreateServices());

    public static void Register(ILibreDesktopCaptureService desktopCapture)
        => LibrePlatform.Register(CreateServices(desktopCapture));

    public static void Register(
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics)
        => LibrePlatform.Register(CreateServices(desktopCapture, nativeFonts, nativeGraphics));
}
