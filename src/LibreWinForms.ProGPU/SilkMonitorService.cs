// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace LibreWinForms.ProGPU;

/// <summary>Silk.NET-backed monitor inventory for the portable WinForms screen APIs.</summary>
public sealed class SilkMonitorService : ILibreMonitorService
{
    private readonly Func<IEnumerable<IMonitor>> _getMonitors;
    private readonly Func<IMonitor?> _getMainMonitor;
    private readonly Func<IMonitor, double?>? _getDpiScale;
    private readonly Func<IMonitor, Rectangle<int>?>? _getWorkArea;
    private readonly Func<IMonitor, double?>? _getNativeCoordinateScale;
    private readonly Func<IMonitor, Rectangle<int>?>? _getBounds;

    public SilkMonitorService()
        : this(
            static () => Silk.NET.Windowing.Monitor.GetMonitors(null),
            static () => Silk.NET.Windowing.Monitor.GetMainMonitor(null),
            TryGetGlfwMonitorContentScale,
            TryGetGlfwMonitorWorkArea,
            TryGetGlfwNativeCoordinateScale,
            TryGetGlfwMonitorBounds)
    {
    }

    public SilkMonitorService(
        Func<IEnumerable<IMonitor>> getMonitors,
        Func<IMonitor?> getMainMonitor,
        Func<IMonitor, double?>? getDpiScale = null,
        Func<IMonitor, Rectangle<int>?>? getWorkArea = null)
        : this(getMonitors, getMainMonitor, getDpiScale, getWorkArea, null, null)
    {
    }

    public SilkMonitorService(
        Func<IEnumerable<IMonitor>> getMonitors,
        Func<IMonitor?> getMainMonitor,
        Func<IMonitor, double?>? getDpiScale,
        Func<IMonitor, Rectangle<int>?>? getWorkArea,
        Func<IMonitor, double?>? getNativeCoordinateScale,
        Func<IMonitor, Rectangle<int>?>? getBounds)
    {
        _getMonitors = getMonitors ?? throw new ArgumentNullException(nameof(getMonitors));
        _getMainMonitor = getMainMonitor ?? throw new ArgumentNullException(nameof(getMainMonitor));
        _getDpiScale = getDpiScale;
        _getWorkArea = getWorkArea;
        _getNativeCoordinateScale = getNativeCoordinateScale;
        _getBounds = getBounds;
    }

    public IReadOnlyList<LibreMonitor> GetMonitors()
    {
        try
        {
            IMonitor[] silkMonitors = [.. _getMonitors()];
            if (silkMonitors.Length == 0)
            {
                throw new PlatformNotSupportedException("Silk.NET returned an empty monitor inventory.");
            }

            IMonitor? primaryMonitor = _getMainMonitor();
            LibreMonitor[] monitors = new LibreMonitor[silkMonitors.Length];
            for (int index = 0; index < silkMonitors.Length; index++)
            {
                monitors[index] = ToMonitorInfo(
                    silkMonitors[index],
                    primaryMonitor,
                    _getDpiScale,
                    _getWorkArea,
                    _getNativeCoordinateScale,
                    _getBounds);
            }

            return monitors;
        }
        catch (PlatformNotSupportedException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
            or EntryPointNotFoundException
            or TypeInitializationException
            or InvalidOperationException)
        {
            throw new PlatformNotSupportedException(
                "Silk.NET monitor enumeration is unavailable on the active windowing backend.",
                exception);
        }
    }

    public LibreMonitor GetNearest(LibreRectangle bounds)
        => LibreMonitorSelection.GetNearest(GetMonitors(), bounds);

    public static LibreMonitor ToMonitorInfo(
        IMonitor monitor,
        IMonitor? primaryMonitor,
        Func<IMonitor, double?>? getDpiScale = null,
        Func<IMonitor, Rectangle<int>?>? getWorkArea = null)
        => ToMonitorInfo(monitor, primaryMonitor, getDpiScale, getWorkArea, null, null);

    public static LibreMonitor ToMonitorInfo(
        IMonitor monitor,
        IMonitor? primaryMonitor,
        Func<IMonitor, double?>? getDpiScale,
        Func<IMonitor, Rectangle<int>?>? getWorkArea,
        Func<IMonitor, double?>? getNativeCoordinateScale,
        Func<IMonitor, Rectangle<int>?>? getBounds)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        Rectangle<int>? declaredBounds = getBounds?.Invoke(monitor);
        if (getBounds is not null && (declaredBounds is not { } fullBounds || fullBounds.Size.X <= 0 || fullBounds.Size.Y <= 0))
        {
            throw new PlatformNotSupportedException("The monitor provider did not return valid full desktop bounds.");
        }

        double? declaredDpi = getDpiScale?.Invoke(monitor);
        if (getDpiScale is not null && (declaredDpi is not double dpi || !IsUsableScale(dpi)))
        {
            throw new PlatformNotSupportedException("The monitor provider did not return a valid content DPI scale.");
        }

        Rectangle<int> bounds = declaredBounds ?? monitor.Bounds;
        int width = bounds.Size.X;
        int height = bounds.Size.Y;
        if ((width <= 0 || height <= 0) && monitor.VideoMode.Resolution is { } resolution)
        {
            width = resolution.X;
            height = resolution.Y;
        }

        Rectangle<int> workArea = getWorkArea?.Invoke(monitor) ?? bounds;
        LibreRectangle monitorBounds = new(
            bounds.Origin.X,
            bounds.Origin.Y,
            Math.Max(0, width),
            Math.Max(0, height));
        LibreRectangle monitorWorkArea = new(
            workArea.Origin.X,
            workArea.Origin.Y,
            Math.Max(0, workArea.Size.X),
            Math.Max(0, workArea.Size.Y));

        return new LibreMonitor(
            $"silk:{monitor.Index}",
            monitorBounds,
            monitorWorkArea,
            ResolveDpiScale(monitor, width, height, declaredDpi),
            ReferenceEquals(monitor, primaryMonitor) || monitor.Index == primaryMonitor?.Index,
            BitsPerPixel: 32,
            DisplayName: monitor.Name)
        {
            NativeCoordinateScale = getNativeCoordinateScale?.Invoke(monitor)
        };
    }

    public static double ResolveDpiScale(
        IMonitor monitor,
        int boundsWidth,
        int boundsHeight,
        double? explicitScale = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (explicitScale is double scale && IsUsableScale(scale))
        {
            return NormalizeScale(scale);
        }

        // GLFW video modes are in screen coordinates, not framebuffer pixels.
        // In particular Silk's monitor.Bounds is its work area, not full bounds.
        return 1.0;
    }

    private static unsafe double? TryGetGlfwNativeCoordinateScale(IMonitor monitor)
    {
        Glfw glfw = GlfwProvider.GLFW.Value;
        if (glfw.Context.TryGetProcAddress("glfwGetPlatform", out nint address) && address != 0)
        {
            int platform = ((delegate* unmanaged[Cdecl]<int>)address)();
            return platform switch
            {
                0x00060001 or 0x00060004 => 1d, // GLFW_PLATFORM_WIN32 / X11
                // Cocoa's content-scale query measures NSScreen points through
                // convertRectToBacking, so it also declares desktop pixel units.
                0x00060002 => TryGetGlfwMonitorContentScale(monitor),
                _ => null // Wayland/unknown desktop positioning is not declared.
            };
        }

        // Older GLFW has no platform query. Require an actual native provider
        // handle; the host OS alone does not establish X11 rather than Wayland.
        if (OperatingSystem.IsLinux()
            && glfw.Context.TryGetProcAddress("glfwGetX11Display", out address) && address != 0
            && ((delegate* unmanaged[Cdecl]<nint>)address)() != 0)
            return 1d;

        Silk.NET.GLFW.Monitor** monitors = glfw.GetMonitors(out int count);
        if (monitors is null || monitor.Index < 0 || monitor.Index >= count)
            return null;
        if (OperatingSystem.IsWindows()
            && glfw.Context.TryGetProcAddress("glfwGetWin32Monitor", out address) && address != 0
            && ((delegate* unmanaged[Cdecl]<Silk.NET.GLFW.Monitor*, nint>)address)(monitors[monitor.Index]) != 0)
            return 1d;
        if (OperatingSystem.IsMacOS()
            && glfw.Context.TryGetProcAddress("glfwGetCocoaMonitor", out address) && address != 0
            && ((delegate* unmanaged[Cdecl]<Silk.NET.GLFW.Monitor*, uint>)address)(monitors[monitor.Index]) != 0)
            return TryGetGlfwMonitorContentScale(monitor);
        return null;
    }

    private static unsafe Rectangle<int>? TryGetGlfwMonitorBounds(IMonitor monitor)
    {
        Glfw glfw = GlfwProvider.GLFW.Value;
        Silk.NET.GLFW.Monitor** monitors = glfw.GetMonitors(out int count);
        if (monitors is null || monitor.Index < 0 || monitor.Index >= count)
            return null;
        glfw.GetMonitorPos(monitors[monitor.Index], out int x, out int y);
        Silk.NET.GLFW.VideoMode* mode = glfw.GetVideoMode(monitors[monitor.Index]);
        return mode is not null && mode->Width > 0 && mode->Height > 0
            ? new Rectangle<int>(x, y, mode->Width, mode->Height) : null;
    }

    private static bool IsUsableScale(double scale)
        => double.IsFinite(scale) && scale > 0.0 && scale <= 8.0;

    private static double NormalizeScale(double scale)
        => Math.Round(scale, 4, MidpointRounding.AwayFromZero);

    private static unsafe double? TryGetGlfwMonitorContentScale(IMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        try
        {
            Glfw glfw = GlfwProvider.GLFW.Value;
            Silk.NET.GLFW.Monitor** nativeMonitors = glfw.GetMonitors(out int monitorCount);
            if (nativeMonitors is null || monitor.Index < 0 || monitor.Index >= monitorCount)
            {
                return null;
            }

            glfw.GetMonitorContentScale(nativeMonitors[monitor.Index], out float scaleX, out float scaleY);
            return IsUsableScale(scaleX) && IsUsableScale(scaleY)
                ? NormalizeScale((scaleX + scaleY) / 2.0)
                : null;
        }
        catch (Exception exception) when (IsUnavailableGlfwException(exception))
        {
            return null;
        }
    }

    private static unsafe Rectangle<int>? TryGetGlfwMonitorWorkArea(IMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        try
        {
            Glfw glfw = GlfwProvider.GLFW.Value;
            Silk.NET.GLFW.Monitor** nativeMonitors = glfw.GetMonitors(out int monitorCount);
            if (nativeMonitors is null || monitor.Index < 0 || monitor.Index >= monitorCount)
            {
                return null;
            }

            glfw.GetMonitorWorkarea(
                nativeMonitors[monitor.Index],
                out int x,
                out int y,
                out int width,
                out int height);
            return width > 0 && height > 0
                ? new Rectangle<int>(x, y, width, height)
                : null;
        }
        catch (Exception exception) when (IsUnavailableGlfwException(exception))
        {
            return null;
        }
    }

    private static bool IsUnavailableGlfwException(Exception exception)
        => exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or GlfwException
            or TypeInitializationException;
}
