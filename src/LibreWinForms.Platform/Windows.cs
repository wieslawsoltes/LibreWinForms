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
    Popup = 32,
    InputTransparent = 64,
}

public enum LibreWindowState
{
    Normal,
    Minimized,
    Maximized,
    FullScreen,
}

public enum LibreWindowBorder
{
    Hidden,
    Fixed,
    Resizable,
}

public enum LibreWindowZOrder
{
    Front,
    Back,
}

/// <summary>Identifies a platform-provided mouse cursor without exposing native handles.</summary>
public enum LibreCursorShape
{
    Arrow,
    AppStarting,
    Cross,
    IBeam,
    Wait,
    No,
    SizeAll,
    SizeNESW,
    SizeNS,
    SizeNWSE,
    SizeWE,
    UpArrow,
    Help,
    Hand,
    HSplit,
    VSplit,
    NoMove2D,
    NoMoveHoriz,
    NoMoveVert,
    PanEast,
    PanNE,
    PanNorth,
    PanNW,
    PanSE,
    PanSouth,
    PanSW,
    PanWest,
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
    double InitialDpiScale = 1.0,
    LibreWindowState InitialState = LibreWindowState.Normal,
    bool ShowInTaskbar = true,
    bool CanMinimize = true,
    bool CanMaximize = true,
    LibreSize MinimumSize = default,
    LibreSize MaximumSize = default,
    bool CanClose = true,
    double Opacity = 1d);

/// <summary>Observable state for a live top-level owner supplied by another desktop stack.</summary>
public readonly record struct LibreExternalWindowOwnerState(bool IsVisible, bool IsEnabled);

/// <summary>
/// Controls typed top-level owners whose process-local handles are not allocated by WinForms.
/// </summary>
public interface ILibreExternalWindowOwnerService
{
    bool IsLive(LibreHandle owner);

    bool TryGetState(LibreHandle owner, out LibreExternalWindowOwnerState state);

    bool TrySetEnabled(LibreHandle owner, bool enabled);

    bool TryActivate(LibreHandle owner);
}

public sealed class UnsupportedLibreExternalWindowOwnerService : ILibreExternalWindowOwnerService
{
    public static UnsupportedLibreExternalWindowOwnerService Instance { get; } = new();

    private UnsupportedLibreExternalWindowOwnerService()
    {
    }

    public bool IsLive(LibreHandle owner) => false;

    public bool TryGetState(LibreHandle owner, out LibreExternalWindowOwnerState state)
    {
        state = default;
        return false;
    }

    public bool TrySetEnabled(LibreHandle owner, bool enabled) => false;

    public bool TryActivate(LibreHandle owner) => false;
}

/// <summary>An immutable, tightly packed RGBA8 icon image for a platform window.</summary>
public sealed class LibreWindowIcon
{
    private readonly byte[] _rgbaPixels;

    public LibreWindowIcon(int width, int height, ReadOnlySpan<byte> rgbaPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int requiredLength = checked(checked(width * height) * 4);
        if (rgbaPixels.Length != requiredLength)
        {
            throw new ArgumentException(
                $"RGBA8 pixel data must contain exactly {requiredLength} bytes.",
                nameof(rgbaPixels));
        }

        Width = width;
        Height = height;
        _rgbaPixels = rgbaPixels.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public int PixelByteLength => _rgbaPixels.Length;

    /// <summary>Copies the immutable RGBA8 snapshot into caller-owned storage.</summary>
    public void CopyPixelsTo(Span<byte> destination)
    {
        if (destination.Length < _rgbaPixels.Length)
        {
            throw new ArgumentException("The destination is smaller than the icon pixel data.", nameof(destination));
        }

        _rgbaPixels.CopyTo(destination);
    }
}

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

    /// <summary>Reports a user- or platform-driven top-level window-state transition.</summary>
    void StateChanged(LibreWindowState state);

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

    /// <summary>The non-null title displayed by the platform window.</summary>
    string Title { get; set; }

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

    /// <summary>Gets or sets whether the platform keeps this top-level window above non-topmost windows.</summary>
    bool TopMost { get; set; }

    /// <summary>Gets or sets the platform-managed top-level border mode.</summary>
    LibreWindowBorder Border { get; set; }

    /// <summary>Gets or sets whether the platform exposes this top-level window in its task switcher.</summary>
    bool ShowInTaskbar { get; set; }

    /// <summary>Gets or sets whether native chrome offers minimize.</summary>
    bool CanMinimize { get; set; }

    /// <summary>Gets or sets whether native chrome offers maximize.</summary>
    bool CanMaximize { get; set; }

    /// <summary>Gets or sets whether native chrome offers close and its system menu.</summary>
    bool CanClose { get; set; }

    /// <summary>Gets or sets whole-window opacity, including platform decorations, from zero to one.</summary>
    double Opacity { get; set; }

    /// <summary>Moves this top-level window to the front or back without changing its bounds.</summary>
    void SetZOrder(LibreWindowZOrder value);

    /// <summary>Applies a platform-provided cursor to this top-level window.</summary>
    void SetCursor(LibreCursorShape shape);

    /// <summary>
    ///  Atomically replaces the managed-coordinate window-size limits. Zero maximum dimensions
    ///  are unbounded. Implementations convert the values to their native coordinate space.
    /// </summary>
    void SetSizeConstraints(LibreSize minimum, LibreSize maximum);

    LibreWindowCoordinateMode CoordinateMode { get; }

    /// <summary>Ratio from native window screen-coordinate units to framebuffer pixels.</summary>
    double FramebufferScale { get; }

    /// <summary>DPI/content scale used by canonical font, layout, and control autoscaling.</summary>
    double DpiScale { get; }

    /// <summary>
    ///  Replaces the platform icon set with immutable RGBA8 snapshots. An empty list restores
    ///  the platform default. Implementations must consume or copy all data before returning.
    /// </summary>
    void SetIcons(IReadOnlyList<LibreWindowIcon> icons);

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
