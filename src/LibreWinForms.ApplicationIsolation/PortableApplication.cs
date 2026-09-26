// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text.Json;

namespace LibreWinForms.ApplicationIsolation;

/// <summary>Explicit executable and string arguments for an independently deployed application.</summary>
public sealed class PortableApplicationStartInfo
{
    /// <summary>Creates launch options for an absolute apphost or managed DLL path.</summary>
    public PortableApplicationStartInfo(string applicationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationPath);
        ApplicationPath = applicationPath;
    }

    /// <summary>Gets the absolute apphost or managed DLL path. Its ordinary dependency closure must be deployed beside it.</summary>
    public string ApplicationPath { get; }

    /// <summary>Gets or sets an absolute dotnet executable path, required for a managed DLL.</summary>
    public string? DotNetHostPath { get; set; }

    /// <summary>Gets application arguments. Each string is passed as one argument without shell interpretation.</summary>
    public IList<string> Arguments { get; } = new List<string>();
}

/// <summary>A child-owned decision and optional text, never an object from its UI or drawing graph.</summary>
public sealed record PortableApplicationReply(bool Accepted, string? Value);

/// <summary>The real process exit code and, when valid, its one-time primitive completion result.</summary>
public sealed record PortableApplicationExit(int ExitCode, PortableApplicationReply? Reply, string? ProtocolError)
{
    /// <summary>Gets whether the process exited successfully and supplied a valid reply. Accepted=false is a valid user decision.</summary>
    public bool Succeeded => ExitCode == 0 && Reply is not null && ProtocolError is null;
}

/// <summary>Launches ordinary applications in separate processes without changing host assembly binding.</summary>
public static class PortableApplication
{
    internal const string ResultPathVariable = "LIBREWINFORMS_APPLICATION_RESULT_PATH";
    internal const string LaunchIdVariable = "LIBREWINFORMS_APPLICATION_LAUNCH_ID";
    internal const int MaximumResultBytes = 32768;

    /// <summary>The largest supported primitive result text, in UTF-16 code units.</summary>
    public const int MaximumValueLength = 4096;

    /// <summary>Starts a child with a fresh private result location and its own ordinary dependency resolution.</summary>
    /// <remarks>No shell, custom load context, assembly hooks, UI embedding or object marshaling is used.</remarks>
    public static PortableApplicationSession Start(PortableApplicationStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        string application = RequireFile(startInfo.ApplicationPath);
        bool managed = string.Equals(Path.GetExtension(application), ".dll", StringComparison.OrdinalIgnoreCase);
        string executable = managed
            ? RequireFile(startInfo.DotNetHostPath ?? throw new ArgumentException("A managed application requires an explicit dotnet host path.", nameof(startInfo)))
            : application;
        ProcessStartInfo processInfo = new(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(application)!
        };
        if (managed)
        {
            processInfo.ArgumentList.Add(application);
        }

        foreach (string argument in startInfo.Arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            processInfo.ArgumentList.Add(argument);
        }

        DirectoryInfo directory = Directory.CreateTempSubdirectory("librewinforms-application-");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string resultPath = Path.Combine(directory.FullName, "result.json");
        string launchId = Guid.NewGuid().ToString("N");
        processInfo.Environment[ResultPathVariable] = resultPath;
        processInfo.Environment[LaunchIdVariable] = launchId;
        Process process = new() { StartInfo = processInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The portable application process was not started.");
            }

            return new PortableApplicationSession(process, directory.FullName, resultPath, launchId);
        }
        catch
        {
            process.Dispose();
            // Start failed: the fresh directory has never been handed to a running child.
            directory.Delete();
            throw;
        }
    }

    /// <summary>Publishes a single bounded primitive result from a launched child.</summary>
    /// <remarks>Call after the child UI closes and its owned objects are disposed. Existing result files are never overwritten.</remarks>
    public static void Complete(bool accepted, string? value)
    {
        if (value?.Length > MaximumValueLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The primitive result exceeds its text limit.");
        }

        string? resultPath = Environment.GetEnvironmentVariable(ResultPathVariable);
        string? launchId = Environment.GetEnvironmentVariable(LaunchIdVariable);
        if (string.IsNullOrEmpty(resultPath) || !Path.IsPathFullyQualified(resultPath)
            || !Guid.TryParseExact(launchId, "N", out _))
        {
            throw new InvalidOperationException("This process has no portable application result channel.");
        }

        using FileStream stream = new(resultPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using Utf8JsonWriter writer = new(stream);
        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WriteString("launchId", launchId);
        writer.WriteNumber("processId", Environment.ProcessId);
        writer.WriteBoolean("accepted", accepted);
        writer.WriteString("value", value);
        writer.WriteEndObject();
        writer.Flush();
    }

    private static string RequireFile(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The executable/application path must be absolute.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The executable/application file does not exist.", path);
        }

        return path;
    }
}
