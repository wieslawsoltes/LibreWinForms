// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>
/// Captures device-pixel desktop rectangles without exposing a native display,
/// HDC, portal object, or renderer texture to canonical WinForms.
/// </summary>
public interface ILibreDesktopCaptureService
{
    /// <summary>
    /// Fills an exact-length row-major RGBA8 destination. Implementations must
    /// not retain the caller-owned span.
    /// </summary>
    void Capture(LibreRectangle sourceRectangle, Span<byte> destinationRgba);
}

/// <summary>An explicit capability boundary for hosts with no desktop-capture adapter.</summary>
public sealed class UnsupportedLibreDesktopCaptureService : ILibreDesktopCaptureService
{
    public static UnsupportedLibreDesktopCaptureService Instance { get; } = new();

    private UnsupportedLibreDesktopCaptureService()
    {
    }

    public void Capture(LibreRectangle sourceRectangle, Span<byte> destinationRgba)
        => throw new PlatformNotSupportedException(
            "Desktop capture requires an explicit local-OS adapter.");
}
