// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using Silk.NET.Windowing;

namespace LibreWinForms.ProGPU;

/// <summary>Silk.NET-backed monitor inventory for the portable WinForms screen APIs.</summary>
public sealed class SilkMonitorService : ILibreMonitorService
{
    public IReadOnlyList<LibreMonitor> GetMonitors()
    {
        try
        {
            IMonitor[] silkMonitors = [.. Silk.NET.Windowing.Monitor.GetMonitors(null)];
            if (silkMonitors.Length == 0)
            {
                throw new PlatformNotSupportedException("Silk.NET returned an empty monitor inventory.");
            }

            int primaryIndex = Silk.NET.Windowing.Monitor.GetMainMonitor(null).Index;
            LibreMonitor[] monitors = new LibreMonitor[silkMonitors.Length];
            for (int index = 0; index < silkMonitors.Length; index++)
            {
                IMonitor monitor = silkMonitors[index];
                Silk.NET.Maths.Rectangle<int> bounds = monitor.Bounds;
                LibreRectangle rectangle = new(
                    bounds.Origin.X,
                    bounds.Origin.Y,
                    bounds.Size.X,
                    bounds.Size.Y);

                // Silk.NET 2.x does not expose monitor work area, color depth, or
                // physical DPI. Keep those values explicit and conservative; a
                // created window reports its actual framebuffer scale separately.
                monitors[index] = new LibreMonitor(
                    $"silk:{monitor.Index}",
                    rectangle,
                    rectangle,
                    1.0,
                    monitor.Index == primaryIndex,
                    32,
                    monitor.Name);
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
}
