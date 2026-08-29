// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>The focused services required by the first source-first runtime slice.</summary>
public sealed class LibrePlatformServices : IDisposable
{
    private int _disposed;

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            UnsupportedLibreDesktopCaptureService.Instance,
            UnsupportedLibreNativeFontInteropService.Instance,
            UnsupportedLibreNativeGraphicsInteropService.Instance,
            UnsupportedLibreVisualStyleService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            desktopCapture,
            UnsupportedLibreNativeFontInteropService.Instance,
            UnsupportedLibreNativeGraphicsInteropService.Instance,
            UnsupportedLibreVisualStyleService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            desktopCapture,
            nativeFonts,
            nativeGraphics,
            UnsupportedLibreVisualStyleService.Instance,
            DefaultLibreSystemSettingsService.Instance,
            UnsupportedLibreTextRendererService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics,
        ILibreVisualStyleService visualStyles)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            desktopCapture,
            nativeFonts,
            nativeGraphics,
            visualStyles,
            DefaultLibreSystemSettingsService.Instance,
            UnsupportedLibreTextRendererService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics,
        ILibreVisualStyleService visualStyles,
        ILibreSystemSettingsService systemSettings)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            desktopCapture,
            nativeFonts,
            nativeGraphics,
            visualStyles,
            systemSettings,
            UnsupportedLibreTextRendererService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics,
        ILibreVisualStyleService visualStyles,
        ILibreSystemSettingsService systemSettings,
        ILibreTextRendererService textRenderer)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            desktopCapture,
            nativeFonts,
            nativeGraphics,
            visualStyles,
            systemSettings,
            textRenderer,
            DefaultLibrePowerStatusService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics,
        ILibreVisualStyleService visualStyles,
        ILibreSystemSettingsService systemSettings,
        ILibreTextRendererService textRenderer,
        ILibrePowerStatusService powerStatus)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            desktopCapture,
            nativeFonts,
            nativeGraphics,
            visualStyles,
            systemSettings,
            textRenderer,
            powerStatus,
            UnsupportedLibreMessageBoxService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics,
        ILibreVisualStyleService visualStyles,
        ILibreSystemSettingsService systemSettings,
        ILibreTextRendererService textRenderer,
        ILibrePowerStatusService powerStatus,
        ILibreMessageBoxService messageBoxes)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            desktopCapture,
            nativeFonts,
            nativeGraphics,
            visualStyles,
            systemSettings,
            textRenderer,
            powerStatus,
            messageBoxes,
            UnsupportedLibreColorDialogService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics,
        ILibreVisualStyleService visualStyles,
        ILibreSystemSettingsService systemSettings,
        ILibreTextRendererService textRenderer,
        ILibrePowerStatusService powerStatus,
        ILibreMessageBoxService messageBoxes,
        ILibreColorDialogService colorDialogs)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            desktopCapture,
            nativeFonts,
            nativeGraphics,
            visualStyles,
            systemSettings,
            textRenderer,
            powerStatus,
            messageBoxes,
            colorDialogs,
            UnsupportedLibreFontDialogService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics,
        ILibreVisualStyleService visualStyles,
        ILibreSystemSettingsService systemSettings,
        ILibreTextRendererService textRenderer,
        ILibrePowerStatusService powerStatus,
        ILibreMessageBoxService messageBoxes,
        ILibreColorDialogService colorDialogs,
        ILibreFontDialogService fontDialogs)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            desktopCapture,
            nativeFonts,
            nativeGraphics,
            visualStyles,
            systemSettings,
            textRenderer,
            powerStatus,
            messageBoxes,
            colorDialogs,
            fontDialogs,
            UnsupportedLibreFileDialogService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics,
        ILibreVisualStyleService visualStyles,
        ILibreSystemSettingsService systemSettings,
        ILibreTextRendererService textRenderer,
        ILibrePowerStatusService powerStatus,
        ILibreMessageBoxService messageBoxes,
        ILibreColorDialogService colorDialogs,
        ILibreFontDialogService fontDialogs,
        ILibreFileDialogService fileDialogs)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            desktopCapture,
            nativeFonts,
            nativeGraphics,
            visualStyles,
            systemSettings,
            textRenderer,
            powerStatus,
            messageBoxes,
            colorDialogs,
            fontDialogs,
            fileDialogs,
            DefaultLibreInputLanguageService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics,
        ILibreVisualStyleService visualStyles,
        ILibreSystemSettingsService systemSettings,
        ILibreTextRendererService textRenderer,
        ILibrePowerStatusService powerStatus,
        ILibreMessageBoxService messageBoxes,
        ILibreColorDialogService colorDialogs,
        ILibreFontDialogService fontDialogs,
        ILibreFileDialogService fileDialogs,
        ILibreInputLanguageService inputLanguages)
        : this(
            dispatcher,
            timers,
            handles,
            windows,
            monitors,
            painting,
            desktopCapture,
            nativeFonts,
            nativeGraphics,
            visualStyles,
            systemSettings,
            textRenderer,
            powerStatus,
            messageBoxes,
            colorDialogs,
            fontDialogs,
            fileDialogs,
            inputLanguages,
            UnsupportedLibreDragDropService.Instance)
    {
    }

    public LibrePlatformServices(
        ILibreDispatcher dispatcher,
        ILibreTimerService timers,
        ILibreHandleRegistry handles,
        ILibreWindowService windows,
        ILibreMonitorService monitors,
        ILibrePaintService painting,
        ILibreDesktopCaptureService desktopCapture,
        ILibreNativeFontInteropService nativeFonts,
        ILibreNativeGraphicsInteropService nativeGraphics,
        ILibreVisualStyleService visualStyles,
        ILibreSystemSettingsService systemSettings,
        ILibreTextRendererService textRenderer,
        ILibrePowerStatusService powerStatus,
        ILibreMessageBoxService messageBoxes,
        ILibreColorDialogService colorDialogs,
        ILibreFontDialogService fontDialogs,
        ILibreFileDialogService fileDialogs,
        ILibreInputLanguageService inputLanguages,
        ILibreDragDropService dragDrop)
    {
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        ThreadDispatchers = dispatcher as ILibreThreadDispatcherProvider
            ?? new SingleLibreThreadDispatcherProvider(dispatcher);
        Timers = timers ?? throw new ArgumentNullException(nameof(timers));
        Handles = handles ?? throw new ArgumentNullException(nameof(handles));
        Windows = windows ?? throw new ArgumentNullException(nameof(windows));
        Monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        Painting = painting ?? throw new ArgumentNullException(nameof(painting));
        DesktopCapture = desktopCapture ?? throw new ArgumentNullException(nameof(desktopCapture));
        NativeFonts = nativeFonts ?? throw new ArgumentNullException(nameof(nativeFonts));
        NativeGraphics = nativeGraphics ?? throw new ArgumentNullException(nameof(nativeGraphics));
        VisualStyles = visualStyles ?? throw new ArgumentNullException(nameof(visualStyles));
        SystemSettings = systemSettings ?? throw new ArgumentNullException(nameof(systemSettings));
        TextRenderer = textRenderer ?? throw new ArgumentNullException(nameof(textRenderer));
        PowerStatus = powerStatus ?? throw new ArgumentNullException(nameof(powerStatus));
        MessageBoxes = messageBoxes ?? throw new ArgumentNullException(nameof(messageBoxes));
        ColorDialogs = colorDialogs ?? throw new ArgumentNullException(nameof(colorDialogs));
        FontDialogs = fontDialogs ?? throw new ArgumentNullException(nameof(fontDialogs));
        FileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
        InputLanguages = inputLanguages ?? throw new ArgumentNullException(nameof(inputLanguages));
        DragDrop = dragDrop ?? throw new ArgumentNullException(nameof(dragDrop));
    }

    public ILibreDispatcher Dispatcher { get; }

    public ILibreThreadDispatcherProvider ThreadDispatchers { get; }

    public ILibreTimerService Timers { get; }

    public ILibreHandleRegistry Handles { get; }

    public ILibreWindowService Windows { get; }

    public ILibreMonitorService Monitors { get; }

    public ILibrePaintService Painting { get; }

    public ILibreDesktopCaptureService DesktopCapture { get; }

    public ILibreNativeFontInteropService NativeFonts { get; }

    public ILibreNativeGraphicsInteropService NativeGraphics { get; }

    public ILibreVisualStyleService VisualStyles { get; }

    public ILibreSystemSettingsService SystemSettings { get; }

    public ILibreTextRendererService TextRenderer { get; }

    public ILibrePowerStatusService PowerStatus { get; }

    public ILibreMessageBoxService MessageBoxes { get; }

    public ILibreColorDialogService ColorDialogs { get; }

    public ILibreFontDialogService FontDialogs { get; }

    public ILibreFileDialogService FileDialogs { get; }

    public ILibreInputLanguageService InputLanguages { get; }

    public ILibreDragDropService DragDrop { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        HashSet<IDisposable> disposed = new(ReferenceEqualityComparer.Instance);
        DisposeService(DragDrop, disposed);
        DisposeService(InputLanguages, disposed);
        DisposeService(FileDialogs, disposed);
        DisposeService(FontDialogs, disposed);
        DisposeService(ColorDialogs, disposed);
        DisposeService(MessageBoxes, disposed);
        DisposeService(PowerStatus, disposed);
        DisposeService(TextRenderer, disposed);
        DisposeService(SystemSettings, disposed);
        DisposeService(VisualStyles, disposed);
        DisposeService(NativeGraphics, disposed);
        DisposeService(NativeFonts, disposed);
        DisposeService(DesktopCapture, disposed);
        DisposeService(Painting, disposed);
        DisposeService(Monitors, disposed);
        DisposeService(Windows, disposed);
        DisposeService(Handles, disposed);
        DisposeService(Timers, disposed);
        DisposeService(Dispatcher, disposed);
    }

    private static void DisposeService(object service, HashSet<IDisposable> disposed)
    {
        if (service is IDisposable disposable && disposed.Add(disposable))
        {
            disposable.Dispose();
        }
    }
}

/// <summary>Process registration point for exactly one non-Windows WinForms backend.</summary>
public static class LibrePlatform
{
    private static LibrePlatformServices? s_current;

    public static bool IsRegistered => Volatile.Read(ref s_current) is not null;

    public static LibrePlatformServices Current
        => Volatile.Read(ref s_current)
            ?? throw new InvalidOperationException("No LibreWinForms platform backend is registered.");

    public static void Register(LibrePlatformServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (Interlocked.CompareExchange(ref s_current, services, null) is not null)
        {
            throw new InvalidOperationException("A LibreWinForms platform backend is already registered.");
        }
    }
}
