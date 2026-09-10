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

    public SilkMonitorService()
        : this(
            static () => Silk.NET.Windowing.Monitor.GetMonitors(null),
            static () => Silk.NET.Windowing.Monitor.GetMainMonitor(null),
            TryGetGlfwMonitorContentScale,
            TryGetGlfwMonitorWorkArea)
    {
    }

    public SilkMonitorService(
        Func<IEnumerable<IMonitor>> getMonitors,
        Func<IMonitor?> getMainMonitor,
        Func<IMonitor, double?>? getDpiScale = null,
        Func<IMonitor, Rectangle<int>?>? getWorkArea = null)
    {
        _getMonitors = getMonitors ?? throw new ArgumentNullException(nameof(getMonitors));
        _getMainMonitor = getMainMonitor ?? throw new ArgumentNullException(nameof(getMainMonitor));
        _getDpiScale = getDpiScale;
        _getWorkArea = getWorkArea;
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
                    _getWorkArea);
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
    {
        ArgumentNullException.ThrowIfNull(monitor);

        Rectangle<int> bounds = monitor.Bounds;
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
            ResolveDpiScale(monitor, width, height, getDpiScale?.Invoke(monitor)),
            ReferenceEquals(monitor, primaryMonitor) || monitor.Index == primaryMonitor?.Index,
            BitsPerPixel: 32,
            DisplayName: monitor.Name);
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

        if (boundsWidth > 0
            && boundsHeight > 0
            && monitor.VideoMode.Resolution is { } resolution
            && resolution.X > 0
            && resolution.Y > 0)
        {
            double scaleX = resolution.X / (double)boundsWidth;
            double scaleY = resolution.Y / (double)boundsHeight;
            if (IsUsableScale(scaleX) && IsUsableScale(scaleY))
            {
                return NormalizeScale((scaleX + scaleY) / 2.0);
            }
        }

        return 1.0;
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
