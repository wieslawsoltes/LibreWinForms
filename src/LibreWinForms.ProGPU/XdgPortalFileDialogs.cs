// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using LibreWinForms.Platform;
using ProGPU.Backend;

namespace LibreWinForms.ProGPU;

/// <summary>The desktop-portal result code defined by org.freedesktop.portal.Request.</summary>
public enum XdgPortalResponse
{
    Success = 0,
    Cancelled = 1,
    Other = 2,
}

/// <summary>A typed, transport-neutral FileChooser portal request.</summary>
public readonly record struct XdgFileChooserRequest(
    LibreFileDialogKind Kind,
    string ParentWindow,
    string Title,
    string InitialDirectory,
    IReadOnlyList<string> SelectedPaths,
    string DefaultExtension,
    IReadOnlyList<LibreFileDialogFilter> Filters,
    int FilterIndex,
    bool Multiple,
    bool ShowReadOnly,
    bool ReadOnlyChecked);

/// <summary>A typed response received from org.freedesktop.portal.Request.</summary>
public readonly record struct XdgFileChooserResult(
    XdgPortalResponse Response,
    IReadOnlyList<string> Uris,
    int? FilterIndex,
    bool? ReadOnlyChecked);

/// <summary>Executes one typed FileChooser portal request.</summary>
public interface IXdgFileChooserPortal
{
    XdgFileChooserResult Show(in XdgFileChooserRequest request);
}

/// <summary>Keeps an exported portal parent identity alive for one request.</summary>
public interface IXdgPortalParentWindowLease : IDisposable
{
    string Identifier { get; }
}

/// <summary>Converts a managed owner identity into a request-scoped portal parent lease.</summary>
public interface IXdgPortalParentWindowProvider
{
    IXdgPortalParentWindowLease Acquire(LibreHandle owner);
}

/// <summary>Exports a Wayland surface through xdg-foreign for the lifetime of a portal request.</summary>
public interface IXdgPortalWaylandParentExporter
{
    bool TryExport(
        NativeWindowHandle window,
        [NotNullWhen(true)] out IXdgPortalParentWindowLease? lease);
}

/// <summary>An immutable portal parent identifier with an optional one-shot release action.</summary>
public sealed class XdgPortalParentWindowLease : IXdgPortalParentWindowLease
{
    private Action? _release;

    public XdgPortalParentWindowLease(string identifier, Action? release = null)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        Identifier = identifier;
        _release = release;
    }

    public static XdgPortalParentWindowLease Empty { get; } = new(string.Empty);

    public string Identifier { get; }

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

/// <summary>Explicit default until a host supplies a real xdg-foreign protocol implementation.</summary>
public sealed class UnsupportedXdgPortalWaylandParentExporter : IXdgPortalWaylandParentExporter
{
    public static UnsupportedXdgPortalWaylandParentExporter Instance { get; } = new();

    private UnsupportedXdgPortalWaylandParentExporter()
    {
    }

    public bool TryExport(
        NativeWindowHandle window,
        [NotNullWhen(true)] out IXdgPortalParentWindowLease? lease)
    {
        lease = null;
        return false;
    }
}

/// <summary>Formats only native handles that the XDG parent-window protocol can represent safely.</summary>
public static class XdgPortalParentWindow
{
    public static string Format(NativeWindowHandle handle)
        => handle.Kind == NativeWindowKind.X11 && handle.IsValid
            ? $"x11:{(nuint)handle.Handle:x}"
            : string.Empty;
}

/// <summary>Resolves opaque LibreWinForms owners inside the ProGPU backend.</summary>
public sealed class ProGpuXdgPortalParentWindowProvider : IXdgPortalParentWindowProvider, IDisposable
{
    private readonly ILibreHandleRegistry _handles;
    private readonly IXdgPortalWaylandParentExporter _wayland;
    private readonly bool _ownsWayland;
    private int _disposed;

    public ProGpuXdgPortalParentWindowProvider(ILibreHandleRegistry handles)
        : this(handles, UnsupportedXdgPortalWaylandParentExporter.Instance)
    {
    }

    public ProGpuXdgPortalParentWindowProvider(
        ILibreHandleRegistry handles,
        IXdgPortalWaylandParentExporter wayland)
        : this(handles, wayland, ownsWayland: false)
    {
    }

    internal ProGpuXdgPortalParentWindowProvider(
        ILibreHandleRegistry handles,
        IXdgPortalWaylandParentExporter wayland,
        bool ownsWayland)
    {
        _handles = handles ?? throw new ArgumentNullException(nameof(handles));
        _wayland = wayland ?? throw new ArgumentNullException(nameof(wayland));
        _ownsWayland = ownsWayland;
    }

    public IXdgPortalParentWindowLease Acquire(LibreHandle owner)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (owner.IsNull)
        {
            return XdgPortalParentWindowLease.Empty;
        }

        if (!_handles.TryGet(owner, out SilkLibreWindow? window))
        {
            throw new ArgumentException("The file-dialog owner must be a live Silk.NET window.", nameof(owner));
        }

        return AcquireNative(window.NativeHandle);
    }

    internal IXdgPortalParentWindowLease AcquireNative(NativeWindowHandle window)
    {
        string staticIdentifier = XdgPortalParentWindow.Format(window);
        if (staticIdentifier.Length > 0)
        {
            return new XdgPortalParentWindowLease(staticIdentifier);
        }

        // A raw wl_surface is not a valid portal parent. The exporter must return an xdg-foreign
        // handle and retain its protocol object until this request-scoped lease is disposed.
        if (window.Kind != NativeWindowKind.Wayland
            || !window.IsValid
            || !_wayland.TryExport(window, out IXdgPortalParentWindowLease? lease))
        {
            return XdgPortalParentWindowLease.Empty;
        }

        if (!lease.Identifier.StartsWith("wayland:", StringComparison.Ordinal)
            || lease.Identifier.Length == "wayland:".Length)
        {
            lease.Dispose();
            throw new InvalidOperationException(
                "The Wayland portal parent exporter returned an invalid xdg-foreign identifier.");
        }

        return lease;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0
            && _ownsWayland
            && _wayland is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

/// <summary>Maps canonical WinForms file-dialog state to the XDG desktop portal.</summary>
public sealed class XdgDesktopPortalLibreFileDialogService : ILibreFileDialogService, IDisposable
{
    private readonly ILibreDispatcher _dispatcher;
    private readonly IXdgFileChooserPortal _portal;
    private readonly IXdgPortalParentWindowProvider _parents;
    private int _disposed;

    public XdgDesktopPortalLibreFileDialogService(
        ILibreDispatcher dispatcher,
        IXdgFileChooserPortal portal,
        IXdgPortalParentWindowProvider parents)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _portal = portal ?? throw new ArgumentNullException(nameof(portal));
        _parents = parents ?? throw new ArgumentNullException(nameof(parents));
    }

    public LibreFileDialogResult Show(in LibreFileDialogRequest request)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("File dialogs must be shown on the owning dispatcher thread.");
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The XDG desktop-portal file-dialog adapter requires Linux.");
        }

        LibreFileDialogRequestValidator.Validate(request);
        if (request.Options.HasFlag(LibreFileDialogOptions.ShowHelp))
        {
            throw new PlatformNotSupportedException("The XDG FileChooser portal does not expose a Help action.");
        }

        if (request.Options.HasFlag(LibreFileDialogOptions.ShowHiddenFiles))
        {
            throw new PlatformNotSupportedException(
                "The XDG FileChooser portal cannot request that hidden files are initially shown.");
        }

        using IXdgPortalParentWindowLease parent = _parents.Acquire(request.Owner);
        XdgFileChooserRequest portalRequest = new(
            request.Kind,
            parent.Identifier,
            request.Title.Length > 0 ? request.Title : request.Description,
            request.InitialDirectory,
            request.SelectedPaths.ToArray(),
            request.DefaultExtension,
            [.. request.Filters],
            request.FilterIndex,
            request.Options.HasFlag(LibreFileDialogOptions.MultiSelect),
            request.Options.HasFlag(LibreFileDialogOptions.ShowReadOnly),
            request.Options.HasFlag(LibreFileDialogOptions.ReadOnlyChecked));

        XdgFileChooserResult result = _portal.Show(portalRequest);
        if (!Enum.IsDefined(result.Response))
        {
            throw new InvalidOperationException($"The XDG portal returned unknown response code {(uint)result.Response}.");
        }

        if (result.Response == XdgPortalResponse.Cancelled)
        {
            return new(false, request.SelectedPaths.ToArray(), request.FilterIndex, portalRequest.ReadOnlyChecked);
        }

        if (result.Response != XdgPortalResponse.Success)
        {
            throw new InvalidOperationException("The XDG portal could not complete the file-dialog request.");
        }

        string[] paths = [.. result.Uris.Select(ToLocalPath)];
        if (paths.Length == 0)
        {
            throw new InvalidOperationException("The XDG portal accepted the dialog without returning a filesystem URI.");
        }

        if (!portalRequest.Multiple && paths.Length != 1)
        {
            throw new InvalidOperationException("The XDG portal returned multiple paths for a single-selection dialog.");
        }

        int filterIndex = result.FilterIndex ?? request.FilterIndex;
        if (filterIndex < 0
            || (request.Filters.Count > 0 && (filterIndex < 1 || filterIndex > request.Filters.Count)))
        {
            throw new InvalidOperationException("The XDG portal returned an invalid selected filter.");
        }

        return new(true, paths, filterIndex, result.ReadOnlyChecked ?? portalRequest.ReadOnlyChecked);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        HashSet<IDisposable> disposed = new(ReferenceEqualityComparer.Instance);
        if (_parents is IDisposable parents && disposed.Add(parents))
        {
            parents.Dispose();
        }

        if (_portal is IDisposable portal && disposed.Add(portal))
        {
            portal.Dispose();
        }
    }

    private static string ToLocalPath(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !uri.IsFile
            || !uri.IsLoopback)
        {
            throw new InvalidOperationException($"The XDG portal returned a non-local filesystem URI: '{value}'.");
        }

        return uri.LocalPath;
    }
}

/// <summary>Uses a secondary adapter only when the preferred desktop integration is unavailable.</summary>
public sealed class PreferredLinuxLibreFileDialogService : ILibreFileDialogService, IDisposable
{
    private readonly ILibreFileDialogService _preferred;
    private readonly ILibreFileDialogService _fallback;
    private int _disposed;

    public PreferredLinuxLibreFileDialogService(
        ILibreFileDialogService preferred,
        ILibreFileDialogService fallback)
    {
        _preferred = preferred ?? throw new ArgumentNullException(nameof(preferred));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public LibreFileDialogResult Show(in LibreFileDialogRequest request)
    {
        try
        {
            return _preferred.Show(request);
        }
        catch (PlatformNotSupportedException)
        {
            return _fallback.Show(request);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_preferred is IDisposable preferred)
        {
            preferred.Dispose();
        }

        if (!ReferenceEquals(_preferred, _fallback) && _fallback is IDisposable fallback)
        {
            fallback.Dispose();
        }
    }
}
