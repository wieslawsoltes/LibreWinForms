// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

[Flags]
public enum LibreWindowOptions
{
    None = 0,
    Visible = 1,
    Resizable = 2,
    Decorated = 4,
    TopMost = 8,
    ToolWindow = 16,
}

public enum LibreWindowState
{
    Normal,
    Minimized,
    Maximized,
    FullScreen,
}

/// <summary>Defines the managed coordinate space exposed by a platform window.</summary>
public enum LibreWindowCoordinateMode
{
    /// <summary>Managed coordinates are 96-DPI logical units and presentation supplies the pixel scale.</summary>
    Logical,

    /// <summary>Managed client coordinates are framebuffer/device pixels and presentation is one-to-one.</summary>
    DevicePixels,
}

/// <summary>Typed creation data for an independent platform window.</summary>
public readonly record struct LibreWindowCreateOptions(
    string Title,
    LibreRectangle Bounds,
    LibreWindowOptions Options,
    LibreHandle Owner,
    LibreWindowCoordinateMode CoordinateMode = LibreWindowCoordinateMode.Logical,
    double InitialDpiScale = 1.0);

/// <summary>Checked conversion between native logical window units and managed coordinates.</summary>
public static class LibreWindowCoordinates
{
    public static LibreRectangle ToManaged(
        LibreRectangle nativeBounds,
        LibreWindowCoordinateMode mode,
        double dpiScale,
        double framebufferScale)
        => Scale(nativeBounds, ResolveManagedScale(mode, dpiScale, framebufferScale));

    public static LibreRectangle ToNative(
        LibreRectangle managedBounds,
        LibreWindowCoordinateMode mode,
        double dpiScale,
        double framebufferScale)
        => Scale(managedBounds, 1.0 / ResolveManagedScale(mode, dpiScale, framebufferScale));

    public static int ToDeviceDpi(double dpiScale)
        => checked((int)Math.Round(96.0 * NormalizeScale(dpiScale), MidpointRounding.AwayFromZero));

    private static LibreRectangle Scale(LibreRectangle bounds, double scale)
        => new(
            ScaleValue(bounds.X, scale),
            ScaleValue(bounds.Y, scale),
            Math.Max(0, ScaleValue(bounds.Width, scale)),
            Math.Max(0, ScaleValue(bounds.Height, scale)));

    private static int ScaleValue(int value, double scale)
        => checked((int)Math.Round(value * scale, MidpointRounding.AwayFromZero));

    private static double ResolveManagedScale(
        LibreWindowCoordinateMode mode,
        double dpiScale,
        double framebufferScale)
    {
        double normalizedDpiScale = NormalizeScale(dpiScale, nameof(dpiScale));
        double normalizedFramebufferScale = NormalizeScale(framebufferScale, nameof(framebufferScale));
        return mode switch
        {
            LibreWindowCoordinateMode.Logical => normalizedFramebufferScale / normalizedDpiScale,
            LibreWindowCoordinateMode.DevicePixels => normalizedFramebufferScale,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown window coordinate mode."),
        };
    }

    private static double NormalizeScale(double scale, string parameterName = "scale")
    {
        if (!double.IsFinite(scale) || scale <= 0.0 || scale > 8.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, scale, "Scale must be finite and in the range (0, 8].");
        }

        return scale;
    }
}

/// <summary>Events raised by a platform window on its dispatcher thread.</summary>
public interface ILibreWindowEvents
{
    /// <summary>Returns <see langword="true"/> to allow the close operation.</summary>
    bool Closing();

    void Closed();

    void BoundsChanged(LibreRectangle bounds);

    /// <summary>
    ///  Reports that the native presentation scale changed. Logical-coordinate windows use
    ///  this only for presentation; device-pixel windows also use it for canonical DPI changes.
    /// </summary>
    void PresentationScaleChanged(double scale);

    void PaintRequested(ILibrePaintFrame frame);

    void Input(in LibreInputEvent inputEvent);
}

/// <summary>A top-level platform window paired with a logical WinForms handle.</summary>
public interface ILibreWindow : IDisposable
{
    LibreHandle Handle { get; }

    /// <summary>The logical owner used for platform transient-window relationships.</summary>
    LibreHandle Owner { get; set; }

    LibreRectangle Bounds { get; set; }

    LibreWindowState State { get; set; }

    bool Visible { get; }

    /// <summary>
    ///  Gets or sets whether the platform window accepts user input. Modal-loop changes to this
    ///  value do not change the corresponding WinForms <c>Control.Enabled</c> property.
    /// </summary>
    bool Enabled { get; set; }

    LibreWindowCoordinateMode CoordinateMode { get; }

    /// <summary>Ratio from native window screen-coordinate units to framebuffer pixels.</summary>
    double FramebufferScale { get; }

    /// <summary>DPI/content scale used by canonical font, layout, and control autoscaling.</summary>
    double DpiScale { get; }

    void Show();

    void Hide();

    void Activate();

    /// <summary>
    /// Synchronously processes paint already pending for this window and
    /// returns after the backend presentation attempt completes.
    /// </summary>
    void PresentPendingPaint();

    void Close();
}

/// <summary>Creates top-level windows without exposing Silk.NET or OS-specific handles.</summary>
public interface ILibreWindowService
{
    ILibreWindow Create(in LibreWindowCreateOptions options, ILibreWindowEvents events);
}
