// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>Captured result of invoking a desktop-dialog process without a shell.</summary>
public readonly record struct LibreDesktopDialogProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs a local desktop dialog executable with an already-tokenized argument list.</summary>
public interface ILibreDesktopDialogProcessRunner
{
    LibreDesktopDialogProcessResult Run(string executable, IReadOnlyList<string> arguments);
}

/// <summary>Default shell-free process runner used by explicit local-desktop adapters.</summary>
public sealed class SystemLibreDesktopDialogProcessRunner : ILibreDesktopDialogProcessRunner
{
    public static SystemLibreDesktopDialogProcessRunner Instance { get; } = new();

    private SystemLibreDesktopDialogProcessRunner()
    {
    }

    public LibreDesktopDialogProcessResult Run(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ProcessStartInfo startInfo = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Desktop dialog process '{executable}' did not start.");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return new(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        catch (Win32Exception exception)
        {
            throw new PlatformNotSupportedException(
                $"The local desktop dialog executable '{executable}' is unavailable.",
                exception);
        }
    }
}

/// <summary>
/// Linux GTK file and folder selection through Zenity. Arguments are passed as discrete process
/// tokens; logical LibreWinForms handles never cross into the native process.
/// </summary>
public sealed partial class ZenityLibreFileDialogService : ILibreFileDialogService
{
    private const char PathSeparator = '\u001F';
    private const string HelpButton = "Help";
    private readonly ILibreDispatcher _dispatcher;
    private readonly ILibreDesktopDialogProcessRunner _runner;
    private readonly string _executable;

    public ZenityLibreFileDialogService(
        ILibreDispatcher dispatcher,
        ILibreDesktopDialogProcessRunner? runner = null,
        string executable = "zenity")
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _runner = runner ?? SystemLibreDesktopDialogProcessRunner.Instance;
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        _executable = executable;
    }

    public LibreFileDialogResult Show(in LibreFileDialogRequest request)
    {
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("File dialogs must be shown on the owning dispatcher thread.");
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The Zenity file-dialog adapter requires Linux.");
        }

        LibreFileDialogRequestValidator.Validate(request);
        List<string> arguments = BuildArguments(request);
        while (true)
        {
            LibreDesktopDialogProcessResult processResult = _runner.Run(_executable, arguments);
            if (processResult.ExitCode == 1)
            {
                return new(false, request.SelectedPaths.ToArray(), request.FilterIndex, IsReadOnlyChecked(request));
            }

            if (processResult.ExitCode != 0)
            {
                string detail = processResult.StandardError.Trim();
                throw new InvalidOperationException(
                    detail.Length == 0
                        ? $"Zenity file dialog failed with exit code {processResult.ExitCode}."
                        : $"Zenity file dialog failed with exit code {processResult.ExitCode}: {detail}");
            }

            string output = TrimLineEnding(processResult.StandardOutput);
            if (request.Options.HasFlag(LibreFileDialogOptions.ShowHelp)
                && request.HelpRequested is not null
                && string.Equals(output, HelpButton, StringComparison.Ordinal))
            {
                request.HelpRequested();
                continue;
            }

            string[] paths = output.Length == 0
                ? []
                : output.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length == 0)
            {
                throw new InvalidOperationException("Zenity accepted the dialog without returning a filesystem path.");
            }

            if (!request.Options.HasFlag(LibreFileDialogOptions.MultiSelect) && paths.Length != 1)
            {
                throw new InvalidOperationException("Zenity returned multiple paths for a single-selection dialog.");
            }

            return new(true, paths, request.FilterIndex, IsReadOnlyChecked(request));
        }
    }

    internal static string ResolveInitialPath(in LibreFileDialogRequest request)
    {
        string selected = request.SelectedPaths.FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path))
            ?? string.Empty;
        if (selected.Length == 0)
        {
            return request.InitialDirectory;
        }

        return !Path.IsPathFullyQualified(selected) && request.InitialDirectory.Length > 0
            ? Path.Join(request.InitialDirectory, selected)
            : selected;
    }

    private static string TrimLineEnding(string value)
        => value.TrimEnd('\r', '\n');

    private static bool IsReadOnlyChecked(in LibreFileDialogRequest request)
        => request.Options.HasFlag(LibreFileDialogOptions.ReadOnlyChecked);
}

internal static class LibreFileDialogRequestValidator
{
    internal static void Validate(in LibreFileDialogRequest request)
    {
        if (!Enum.IsDefined(request.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown file-dialog kind.");
        }

        ArgumentNullException.ThrowIfNull(request.Title);
        ArgumentNullException.ThrowIfNull(request.Description);
        ArgumentNullException.ThrowIfNull(request.InitialDirectory);
        ArgumentNullException.ThrowIfNull(request.SelectedPaths);
        ArgumentNullException.ThrowIfNull(request.DefaultExtension);
        ArgumentNullException.ThrowIfNull(request.Filters);
        ArgumentNullException.ThrowIfNull(request.CustomPlaces);
        if (request.FilterIndex < 0
            || (request.Filters.Count > 0 && (request.FilterIndex < 1 || request.FilterIndex > request.Filters.Count)))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.FilterIndex, "Filter index is out of range.");
        }

        foreach (LibreFileDialogFilter filter in request.Filters)
        {
            ArgumentNullException.ThrowIfNull(filter);
            ArgumentException.ThrowIfNullOrWhiteSpace(filter.Name);
            ArgumentNullException.ThrowIfNull(filter.Patterns);
            if (filter.Patterns.Count == 0 || filter.Patterns.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("Every file-dialog filter requires at least one wildcard pattern.", nameof(request));
            }
        }

        const LibreFileDialogOptions supported = (LibreFileDialogOptions)((1 << 24) - 1);
        if ((request.Options & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Options, "Unknown file-dialog option.");
        }
    }
}

public sealed partial class ZenityLibreFileDialogService
{
    private static List<string> BuildArguments(in LibreFileDialogRequest request)
    {
        List<string> arguments = ["--file-selection", "--modal"];
        if (request.Kind == LibreFileDialogKind.SelectFolder)
        {
            arguments.Add("--directory");
        }
        else if (request.Kind == LibreFileDialogKind.SaveFile)
        {
            arguments.Add("--save");
            if (request.Options.HasFlag(LibreFileDialogOptions.OverwritePrompt))
            {
                arguments.Add("--confirm-overwrite");
            }
        }

        if (request.Options.HasFlag(LibreFileDialogOptions.MultiSelect))
        {
            arguments.Add("--multiple");
            arguments.Add($"--separator={PathSeparator}");
        }

        if (request.Options.HasFlag(LibreFileDialogOptions.ShowHiddenFiles))
        {
            arguments.Add("--show-hidden");
        }

        if (request.Options.HasFlag(LibreFileDialogOptions.ShowHelp) && request.HelpRequested is not null)
        {
            arguments.Add($"--extra-button={HelpButton}");
        }

        string title = request.Title.Length > 0 ? request.Title : request.Description;
        if (title.Length > 0)
        {
            arguments.Add($"--title={title}");
        }

        string initialPath = ResolveInitialPath(request);
        if (initialPath.Length > 0)
        {
            arguments.Add($"--filename={initialPath}");
        }

        foreach (LibreFileDialogFilter filter in request.Filters)
        {
            arguments.Add($"--file-filter={filter.Name} | {string.Join(' ', filter.Patterns)}");
        }

        return arguments;
    }
}
