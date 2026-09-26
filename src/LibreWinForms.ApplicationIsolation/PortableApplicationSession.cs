// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text.Json;

namespace LibreWinForms.ApplicationIsolation;

/// <summary>A host-side handle to one child process. Disposing it never terminates the application.</summary>
public sealed class PortableApplicationSession : IDisposable
{
    private readonly Process _process;
    private readonly string _resultPath;
    private readonly string _launchId;

    internal PortableApplicationSession(Process process, string resultDirectory, string resultPath, string launchId)
    {
        _process = process;
        _resultPath = resultPath;
        _launchId = launchId;
        ResultDirectory = resultDirectory;
        ProcessId = process.Id;
    }

    /// <summary>Gets the actual child PID.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the fresh private result directory, retained for caller-owned inspection and cleanup after the child exits.</summary>
    public string ResultDirectory { get; }

    /// <summary>Gets whether the launched process has exited.</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>Waits for exit and reads a bounded, versioned primitive result.</summary>
    /// <remarks>Canceling the wait leaves the child running. The caller may wait again or explicitly call Terminate.</remarks>
    public async Task<PortableApplicationExit> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        int exitCode = _process.ExitCode;
        try
        {
            return new PortableApplicationExit(exitCode, ReadReply(), null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A missing/invalid reply must not disguise the actual child exit status.
            return new PortableApplicationExit(exitCode, null, exception.Message);
        }
    }

    /// <summary>Explicitly requests termination of this process and its descendants.</summary>
    /// <remarks>This is destructive, not normal GUI closure. Use only for an explicit caller termination or timeout policy, then wait for exit.</remarks>
    public void Terminate()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }

    /// <summary>Releases only the host-side process handle. The child and retained result directory are not removed.</summary>
    public void Dispose() => _process.Dispose();

    private PortableApplicationReply ReadReply()
    {
        FileInfo file = new(_resultPath);
        if (file.LinkTarget is not null || (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
        {
            throw new InvalidDataException("The application result must be a regular file, not a link.");
        }

        if (file.Length is <= 0 or > PortableApplication.MaximumResultBytes)
        {
            throw new InvalidDataException("The application result exceeds its bounded protocol size or is empty.");
        }

        using FileStream stream = new(_resultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] bytes = new byte[(int)file.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("The application result changed while being read.");
        }

        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The application result is not an object.");
        }

        int fields = 0;
        bool accepted = false;
        string? value = null;
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            int flag = property.Name switch
            {
                "version" => 1,
                "launchId" => 2,
                "processId" => 4,
                "accepted" => 8,
                "value" => 16,
                _ => 0
            };
            if (flag == 0 || (fields & flag) != 0)
            {
                throw new InvalidDataException("The application result contains an unknown or duplicate field.");
            }

            fields |= flag;
            bool valid = property.Name switch
            {
                "version" => property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int version) && version == 1,
                "launchId" => property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == _launchId,
                "processId" => property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int processId) && processId == ProcessId,
                "accepted" => property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "value" => property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null,
                _ => false
            };
            if (!valid)
            {
                throw new InvalidDataException("The application result has an unsupported version, identity or primitive value.");
            }

            if (property.Name == "accepted")
            {
                accepted = property.Value.GetBoolean();
            }
            else if (property.Name == "value")
            {
                value = property.Value.GetString();
            }
        }

        if (fields != 31 || value?.Length > PortableApplication.MaximumValueLength)
        {
            throw new InvalidDataException("The application result has missing fields or excessive text.");
        }

        return new PortableApplicationReply(accepted, value);
    }
}
