// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.ProGPU;

/// <summary>Immutable source-host choices captured before the SDK registers its backend.</summary>
public readonly record struct ProGpuStartupOptions
{
    /// <summary>Requests the existing Cocoa native session without enabling automatic policy.</summary>
    public bool EnableNativeModalSessions { get; }

    private ProGpuStartupOptions(bool enableNativeModalSessions)
        => EnableNativeModalSessions = enableNativeModalSessions;

    /// <summary>
    /// Reads the exact --libre-native-modal-sessions startup switch without
    /// removing application arguments. Everything after -- is application data.
    /// </summary>
    public static ProGpuStartupOptions ParseArguments(ReadOnlySpan<string> arguments)
        => ParseArguments(arguments, OperatingSystem.IsMacOS());

    internal static ProGpuStartupOptions ParseArguments(ReadOnlySpan<string> arguments, bool isMacOS)
    {
        bool requested = false;
        foreach (string argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            if (argument == "--")
                break;
            if (!argument.StartsWith("--libre-native-modal", StringComparison.Ordinal))
                continue;
            if (argument != "--libre-native-modal-sessions" || requested)
                throw new ArgumentException("Use --libre-native-modal-sessions exactly once, without a value.", nameof(arguments));
            requested = true;
        }

        if (requested && !isMacOS)
            throw new PlatformNotSupportedException("Explicit native modal startup requires the Cocoa session provider; Windows and Linux policies are unchanged.");
        return new(requested);
    }
}
