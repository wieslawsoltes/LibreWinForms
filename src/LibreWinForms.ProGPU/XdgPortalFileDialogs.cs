// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

/// <summary>Converts a managed owner identity into an XDG portal parent-window identifier.</summary>
public interface IXdgPortalParentWindowProvider
{
    string GetParentWindow(LibreHandle owner);
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
public sealed class ProGpuXdgPortalParentWindowProvider : IXdgPortalParentWindowProvider
{
    private readonly ILibreHandleRegistry _handles;

    public ProGpuXdgPortalParentWindowProvider(ILibreHandleRegistry handles)
        => _handles = handles ?? throw new ArgumentNullException(nameof(handles));

    public string GetParentWindow(LibreHandle owner)
    {
        if (owner.IsNull)
        {
            return string.Empty;
        }

        if (!_handles.TryGet(owner, out SilkLibreWindow? window))
        {
            throw new ArgumentException("The file-dialog owner must be a live Silk.NET window.", nameof(owner));
        }

        // A raw wl_surface is not a valid portal parent. Wayland needs an xdg-foreign exported
        // handle, so it deliberately remains unparented until that typed exporter seam exists.
        return XdgPortalParentWindow.Format(window.NativeHandle);
    }
}

/// <summary>Maps canonical WinForms file-dialog state to the XDG desktop portal.</summary>
public sealed class XdgDesktopPortalLibreFileDialogService : ILibreFileDialogService, IDisposable
{
    private readonly ILibreDispatcher _dispatcher;
    private readonly IXdgFileChooserPortal _portal;
    private readonly IXdgPortalParentWindowProvider _parents;

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

        XdgFileChooserRequest portalRequest = new(
            request.Kind,
            _parents.GetParentWindow(request.Owner),
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
        if (_portal is IDisposable disposable)
        {
            disposable.Dispose();
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
