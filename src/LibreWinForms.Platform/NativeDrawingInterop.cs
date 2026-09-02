// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;

namespace LibreWinForms.Platform;

/// <summary>Imports the font selected into an exact native device context.</summary>
public interface ILibreNativeFontInteropService
{
    Font ImportFromDeviceContext(IntPtr deviceContext);
}

/// <summary>Imports native drawing targets and creates host halftone palettes.</summary>
public interface ILibreNativeGraphicsInteropService
{
    Graphics CreateFromDeviceContext(IntPtr deviceContext, IntPtr device);

    Graphics CreateFromWindow(IntPtr window);

    IntPtr CreateHalftonePalette();
}

/// <summary>Explicit default for hosts without native selected-font import.</summary>
public sealed class UnsupportedLibreNativeFontInteropService : ILibreNativeFontInteropService
{
    public static UnsupportedLibreNativeFontInteropService Instance { get; } = new();

    private UnsupportedLibreNativeFontInteropService()
    {
    }

    public Font ImportFromDeviceContext(IntPtr deviceContext)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide native device-context font import.");
}

/// <summary>Explicit default for hosts without native drawing interoperability.</summary>
public sealed class UnsupportedLibreNativeGraphicsInteropService : ILibreNativeGraphicsInteropService
{
    public static UnsupportedLibreNativeGraphicsInteropService Instance { get; } = new();

    private UnsupportedLibreNativeGraphicsInteropService()
    {
    }

    public Graphics CreateFromDeviceContext(IntPtr deviceContext, IntPtr device)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide native device-context drawing import.");

    public Graphics CreateFromWindow(IntPtr window)
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide native window drawing import.");

    public IntPtr CreateHalftonePalette()
        => throw new PlatformNotSupportedException(
            "This LibreWinForms host does not provide native halftone palettes.");
}
