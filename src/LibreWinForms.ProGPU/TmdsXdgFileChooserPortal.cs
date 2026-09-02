// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using LibreWinForms.ProGPU.Portal.Generated;
using LibreWinForms.Platform;
using Tmds.DBus.Protocol;

namespace LibreWinForms.ProGPU;

/// <summary>
/// XDG FileChooser transport built from compile-time-generated Tmds.DBus.Protocol proxies.
/// </summary>
public sealed class TmdsXdgFileChooserPortal : IXdgFileChooserPortal, IDisposable
{
    private const string Destination = "org.freedesktop.portal.Desktop";
    private static readonly ObjectPath s_desktopPath = new("/org/freedesktop/portal/desktop");
    private readonly Lock _gate = new();
    private Task<DBusConnection>? _connectTask;
    private bool _disposed;

    public XdgFileChooserResult Show(in XdgFileChooserRequest request)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The XDG desktop portal requires Linux.");
        }

        try
        {
            return ShowAsync(request).GetAwaiter().GetResult();
        }
        catch (DBusConnectFailedException exception)
        {
            throw new PlatformNotSupportedException("The XDG session bus is unavailable.", exception);
        }
        catch (DBusConnectionException exception)
        {
            throw new PlatformNotSupportedException("The XDG desktop portal connection is unavailable.", exception);
        }
        catch (DBusErrorReplyException exception) when (IsUnavailable(exception.ErrorName))
        {
            throw new PlatformNotSupportedException("The XDG FileChooser portal is unavailable.", exception);
        }
    }

    public void Dispose()
    {
        Task<DBusConnection>? connectTask;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            connectTask = _connectTask;
            _connectTask = null;
        }

        if (connectTask is null)
        {
            return;
        }

        if (connectTask.IsCompletedSuccessfully)
        {
            connectTask.Result.Dispose();
            return;
        }

        _ = connectTask.ContinueWith(
            static completed =>
            {
                if (completed.IsCompletedSuccessfully)
                {
                    completed.Result.Dispose();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task<XdgFileChooserResult> ShowAsync(XdgFileChooserRequest request)
    {
        DBusConnection connection = await GetConnectionAsync().ConfigureAwait(false);
        FileChooser chooser = new(connection, Destination, s_desktopPath);
        uint version = await chooser.GetVersionAsync().ConfigureAwait(false);
        if (request.Kind == LibreFileDialogKind.SelectFolder && version < 3)
        {
            throw new PlatformNotSupportedException(
                $"The installed XDG FileChooser portal is version {version}; folder selection requires version 3.");
        }

        string token = $"librewinforms_{Guid.NewGuid():N}";
        string sender = GetSenderPathElement(connection.UniqueName);
        ObjectPath expectedPath = new($"/org/freedesktop/portal/desktop/request/{sender}/{token}");
        Request responseRequest = new(connection, Destination, expectedPath);
        TaskCompletionSource<(uint Response, Dictionary<string, VariantValue> Results)> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = await responseRequest.WatchResponseAsync(
            notification => Complete(notification, completion),
            ObserverFlags.EmitAll,
            emitOnCapturedContext: false).ConfigureAwait(false);

        Dictionary<string, VariantValue> options = BuildOptions(request, token);
        ObjectPath returnedPath = request.Kind == LibreFileDialogKind.SaveFile
            ? await chooser.SaveFileAsync(request.ParentWindow, request.Title, options).ConfigureAwait(false)
            : await chooser.OpenFileAsync(request.ParentWindow, request.Title, options).ConfigureAwait(false);
        if (!string.Equals(returnedPath.ToString(), expectedPath.ToString(), StringComparison.Ordinal))
        {
            await new Request(connection, Destination, returnedPath).CloseAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"The XDG portal returned unexpected request path '{returnedPath}' instead of '{expectedPath}'.");
        }

        (uint response, Dictionary<string, VariantValue> results) = await completion.Task.ConfigureAwait(false);
        return ParseResult((XdgPortalResponse)response, results, request.Filters);
    }

    private Task<DBusConnection> GetConnectionAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _connectTask ??= ConnectAsync();
        }
    }

    private static async Task<DBusConnection> ConnectAsync()
    {
        string address = DBusAddress.Session
            ?? throw new PlatformNotSupportedException("The XDG session-bus address is unavailable.");
        DBusConnection connection = new(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal static Dictionary<string, VariantValue> BuildOptions(
        in XdgFileChooserRequest request,
        string token)
    {
        Dictionary<string, VariantValue> options = new(StringComparer.Ordinal)
        {
            ["handle_token"] = token,
            ["modal"] = true,
        };

        if (request.Kind != LibreFileDialogKind.SaveFile)
        {
            options["multiple"] = request.Multiple;
            if (request.Kind == LibreFileDialogKind.SelectFolder)
            {
                options["directory"] = true;
            }
        }

        AddFilters(options, request.Filters, request.FilterIndex);
        AddReadOnlyChoice(options, request.ShowReadOnly, request.ReadOnlyChecked);
        AddInitialLocation(options, request);
        return options;
    }

    private static void AddFilters(
        Dictionary<string, VariantValue> options,
        IReadOnlyList<LibreFileDialogFilter> source,
        int filterIndex)
    {
        if (source.Count == 0)
        {
            return;
        }

        Tmds.DBus.Protocol.Array<Struct<string, Tmds.DBus.Protocol.Array<Struct<uint, string>>>> filters = new(source.Count);
        foreach (LibreFileDialogFilter filter in source)
        {
            Tmds.DBus.Protocol.Array<Struct<uint, string>> patterns = new(filter.Patterns.Count);
            foreach (string pattern in filter.Patterns)
            {
                patterns.Add(Struct.Create(0u, pattern));
            }

            filters.Add(Struct.Create(filter.Name, patterns));
        }

        options["filters"] = filters;
        if (filterIndex > 0)
        {
            options["current_filter"] = filters[filterIndex - 1];
        }
    }

    private static void AddReadOnlyChoice(
        Dictionary<string, VariantValue> options,
        bool showReadOnly,
        bool readOnlyChecked)
    {
        if (!showReadOnly)
        {
            return;
        }

        Tmds.DBus.Protocol.Array<Struct<string, string>> noValues = new();
        Tmds.DBus.Protocol.Array<Struct<string, string, Tmds.DBus.Protocol.Array<Struct<string, string>>, string>> choices = new(1)
        {
            Struct.Create("librewinforms_read_only", "Open as read-only", noValues, readOnlyChecked ? "true" : "false"),
        };
        options["choices"] = choices;
    }

    private static void AddInitialLocation(
        Dictionary<string, VariantValue> options,
        in XdgFileChooserRequest request)
    {
        string selected = request.SelectedPaths.FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path))
            ?? string.Empty;
        string path = selected;
        if (path.Length > 0 && !Path.IsPathFullyQualified(path) && request.InitialDirectory.Length > 0)
        {
            path = Path.Join(request.InitialDirectory, path);
        }

        if (request.Kind == LibreFileDialogKind.SaveFile && selected.Length > 0)
        {
            options["current_name"] = Path.GetFileName(selected);
        }

        if (request.Kind == LibreFileDialogKind.SelectFolder)
        {
            string folder = path.Length > 0 ? path : request.InitialDirectory;
            if (folder.Length > 0)
            {
                options["current_folder"] = VariantValue.Array(ToNullTerminatedBytes(folder));
            }
        }
        else if (path.Length > 0)
        {
            options["current_file"] = VariantValue.Array(ToNullTerminatedBytes(path));
        }
        else if (request.InitialDirectory.Length > 0)
        {
            options["current_folder"] = VariantValue.Array(ToNullTerminatedBytes(request.InitialDirectory));
        }
    }

    private static byte[] ToNullTerminatedBytes(string path)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(path);
        byte[] terminated = new byte[utf8.Length + 1];
        utf8.CopyTo(terminated, 0);
        return terminated;
    }

    internal static XdgFileChooserResult ParseResult(
        XdgPortalResponse response,
        Dictionary<string, VariantValue> results,
        IReadOnlyList<LibreFileDialogFilter> filters)
    {
        if (response != XdgPortalResponse.Success)
        {
            return new(response, [], null, null);
        }

        string[] uris = results.TryGetValue("uris", out VariantValue uriValue)
            ? uriValue.GetArray<string>()
            : [];
        int? filterIndex = results.TryGetValue("current_filter", out VariantValue filterValue)
            ? FindFilter(filterValue, filters)
            : null;
        bool? readOnly = results.TryGetValue("choices", out VariantValue choicesValue)
            ? ReadReadOnlyChoice(choicesValue)
            : null;
        return new(response, uris, filterIndex, readOnly);
    }

    private static int? FindFilter(VariantValue selected, IReadOnlyList<LibreFileDialogFilter> filters)
    {
        string name = selected.GetItem(0).GetString();
        VariantValue patternsValue = selected.GetItem(1);
        string[] patterns = new string[patternsValue.Count];
        for (int i = 0; i < patterns.Length; i++)
        {
            VariantValue pattern = patternsValue.GetItem(i);
            if (pattern.GetItem(0).GetUInt32() != 0)
            {
                return null;
            }

            patterns[i] = pattern.GetItem(1).GetString();
        }

        for (int i = 0; i < filters.Count; i++)
        {
            LibreFileDialogFilter candidate = filters[i];
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal)
                && candidate.Patterns.SequenceEqual(patterns, StringComparer.Ordinal))
            {
                return i + 1;
            }
        }

        return null;
    }

    private static bool? ReadReadOnlyChoice(VariantValue choices)
    {
        for (int i = 0; i < choices.Count; i++)
        {
            VariantValue choice = choices.GetItem(i);
            if (string.Equals(choice.GetItem(0).GetString(), "librewinforms_read_only", StringComparison.Ordinal))
            {
                return string.Equals(choice.GetItem(1).GetString(), "true", StringComparison.Ordinal);
            }
        }

        return null;
    }

    internal static string GetSenderPathElement(string? uniqueName)
    {
        if (string.IsNullOrEmpty(uniqueName) || uniqueName[0] != ':')
        {
            throw new InvalidOperationException("The D-Bus connection did not receive a unique bus name.");
        }

        return uniqueName[1..].Replace('.', '_');
    }

    private static void Complete(
        Notification<(uint Response, Dictionary<string, VariantValue> Results)> notification,
        TaskCompletionSource<(uint Response, Dictionary<string, VariantValue> Results)> completion)
    {
        if (notification.HasValue)
        {
            completion.TrySetResult(notification.Value);
        }
        else if (notification.IsCompletion)
        {
            completion.TrySetException(notification.Exception);
        }
    }

    private static bool IsUnavailable(string errorName)
        => errorName is "org.freedesktop.DBus.Error.ServiceUnknown"
            or "org.freedesktop.DBus.Error.NameHasNoOwner"
            or "org.freedesktop.DBus.Error.UnknownMethod"
            or "org.freedesktop.DBus.Error.UnknownObject";
}
