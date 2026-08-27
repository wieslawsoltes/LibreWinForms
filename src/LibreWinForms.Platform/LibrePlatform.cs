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
            UnsupportedLibreDesktopCaptureService.Instance)
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
    {
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Timers = timers ?? throw new ArgumentNullException(nameof(timers));
        Handles = handles ?? throw new ArgumentNullException(nameof(handles));
        Windows = windows ?? throw new ArgumentNullException(nameof(windows));
        Monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        Painting = painting ?? throw new ArgumentNullException(nameof(painting));
        DesktopCapture = desktopCapture ?? throw new ArgumentNullException(nameof(desktopCapture));
    }

    public ILibreDispatcher Dispatcher { get; }

    public ILibreTimerService Timers { get; }

    public ILibreHandleRegistry Handles { get; }

    public ILibreWindowService Windows { get; }

    public ILibreMonitorService Monitors { get; }

    public ILibrePaintService Painting { get; }

    public ILibreDesktopCaptureService DesktopCapture { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        HashSet<IDisposable> disposed = new(ReferenceEqualityComparer.Instance);
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
