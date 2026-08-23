// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using LibreWinForms.Platform;
using ProGPU.Backend;
using ProGPU.Scene;
using Silk.NET.Core;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;

namespace LibreWinForms.ProGPU;

public sealed class SilkWindowService : ILibreWindowService
{
    private readonly ProGpuDispatcher _dispatcher;
    private readonly ILibreHandleRegistry _handles;
    private readonly ILibreMonitorService _monitors;

    public SilkWindowService(ProGpuDispatcher dispatcher, ILibreHandleRegistry handles)
        : this(dispatcher, handles, new SilkMonitorService())
    {
    }

    public SilkWindowService(
        ProGpuDispatcher dispatcher,
        ILibreHandleRegistry handles,
        ILibreMonitorService monitors)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _handles = handles ?? throw new ArgumentNullException(nameof(handles));
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
    }

    public ILibreWindow Create(in LibreWindowCreateOptions options, ILibreWindowEvents events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("Silk.NET windows must be created on the dispatcher thread.");
        }

        if (!options.Owner.IsNull && !_handles.TryGet(options.Owner, out SilkLibreWindow? _))
        {
            throw new ArgumentException("The owner must be a live Silk.NET window.", nameof(options));
        }

        return new SilkLibreWindow(_dispatcher, _handles, _monitors, options, events);
    }
}

internal sealed class SilkLibreWindow : ILibreWindow, IProGpuLoopParticipant
{
    private readonly ProGpuDispatcher _dispatcher;
    private readonly ILibreHandleRegistry _handles;
    private readonly ILibreMonitorService _monitors;
    private readonly ILibreWindowEvents _events;
    private readonly IWindow _window;
    private readonly SilkWindowController _controller;
    private readonly LibreWindowCoordinateMode _coordinateMode;
    private readonly ContainerVisual _paintRoot = new();
    private readonly DrawingVisual _fallbackPaintVisual = new();
    private readonly DrawingVisual _transientPaintVisual = new();
    private readonly Dictionary<LibreHandle, DrawingVisual> _paintLayers = [];
    private IInputContext? _input;
    private volatile WgpuContext? _wgpuContext;
    private Compositor? _compositor;
    private bool _paintQueued;
    private bool _presentationQueued;
    private LibreRectangle? _dirtyRectangle;
    private LibreHandle _owner;
    private bool _enabled = true;
    private bool _showInTaskbar;
    private bool _canClose;
    private bool _canMinimize;
    private bool _canMaximize;
    private double _opacity;
    private LibreSize _minimumSize;
    private LibreSize _maximumSize;
    private LibreCursorShape _cursorShape = LibreCursorShape.Arrow;
    private double _reportedDpiScale = 1.0;
    private double _reportedFramebufferScale = 1.0;
    private readonly bool _initializing = true;
    private bool _updatingPresentationGeometry;
    private bool _closed;
    private volatile bool _disposed;

    internal SilkLibreWindow(
        ProGpuDispatcher dispatcher,
        ILibreHandleRegistry handles,
        ILibreMonitorService monitors,
        in LibreWindowCreateOptions options,
        ILibreWindowEvents events)
    {
        _dispatcher = dispatcher;
        _handles = handles;
        _monitors = monitors;
        _events = events;
        _coordinateMode = options.CoordinateMode;
        ValidateSizeConstraints(options.MinimumSize, options.MaximumSize);
        ValidateOpacity(options.Opacity);
        _paintRoot.AddChild(_fallbackPaintVisual);
        _paintRoot.AddTopmostChild(_transientPaintVisual);
        LibreRectangle nativeBounds = LibreWindowCoordinates.ToNative(
            options.Bounds,
            _coordinateMode,
            options.InitialDpiScale,
            options.InitialDpiScale);
        WindowOptions silkOptions = WindowOptions.Default with
        {
            API = GraphicsAPI.None,
            IsVisible = false,
            IsEventDriven = false,
            ShouldSwapAutomatically = false,
            VSync = false,
            FramesPerSecond = 0,
            UpdatesPerSecond = 0,
            Size = new Vector2D<int>(Math.Max(1, nativeBounds.Width), Math.Max(1, nativeBounds.Height)),
            Position = new Vector2D<int>(nativeBounds.X, nativeBounds.Y),
            Title = options.Title,
            WindowState = ToSilkWindowState(options.InitialState),
            TopMost = options.Options.HasFlag(LibreWindowOptions.TopMost),
            WindowBorder = ToSilkWindowBorder(ResolveBorder(options.Options)),
        };

        _window = Silk.NET.Windowing.Window.Create(silkOptions);
        _controller = new SilkWindowController(_window);
        _showInTaskbar = options.ShowInTaskbar;
        _canClose = options.CanClose;
        _canMinimize = options.CanMinimize;
        _canMaximize = options.CanMaximize;
        _opacity = options.Opacity;
        _minimumSize = options.MinimumSize;
        _maximumSize = options.MaximumSize;
        Handle = handles.Allocate(this, LibreHandleKind.Window);
        AttachEvents();
        _window.Initialize();
        ApplyControllerBorder(ResolveBorder(options.Options));
        _controller.SetCanClose(_canClose);
        _controller.SetCanMinimize(_canMinimize);
        _controller.SetCanMaximize(_canMaximize);
        _controller.SetOpacity(_opacity);
        _controller.SetShowInTaskbar(_showInTaskbar);
        _reportedDpiScale = DpiScale;
        _reportedFramebufferScale = FramebufferScale;
        ApplySizeConstraints();
        SetNativeBounds(LibreWindowCoordinates.ToNative(
            options.Bounds,
            _coordinateMode,
            _reportedDpiScale,
            _reportedFramebufferScale));
        _initializing = false;

        Owner = options.Owner;
        _dispatcher.Register(this);
        if (options.Options.HasFlag(LibreWindowOptions.Visible))
        {
            Show();
        }
    }

    public LibreHandle Handle { get; }

    public LibreWindowCoordinateMode CoordinateMode => _coordinateMode;

    public string Title
    {
        get => _window.Title;
        set
        {
            VerifyAccess();
            ArgumentNullException.ThrowIfNull(value);
            _window.Title = value;
        }
    }

    public LibreHandle Owner
    {
        get => _owner;
        set
        {
            VerifyAccess();
            if (value == Handle)
            {
                throw new ArgumentException("A window cannot own itself.", nameof(value));
            }

            NativeWindowHandle nativeOwner = NativeWindowHandle.Empty;
            if (!value.IsNull)
            {
                if (!_handles.TryGet(value, out SilkLibreWindow? owner))
                {
                    throw new ArgumentException("The owner must be a live Silk.NET window.", nameof(value));
                }

                nativeOwner = owner._controller.Handle;
            }

            _controller.SetParent(nativeOwner);
            _owner = value;
        }
    }

    public LibreRectangle Bounds
    {
        get => LibreWindowCoordinates.ToManaged(
            new LibreRectangle(_window.Position.X, _window.Position.Y, _window.Size.X, _window.Size.Y),
            _coordinateMode,
            DpiScale,
            FramebufferScale);
        set
        {
            VerifyAccess();
            SetNativeBounds(LibreWindowCoordinates.ToNative(
                value,
                _coordinateMode,
                DpiScale,
                FramebufferScale));
        }
    }

    public LibreWindowState State
    {
        get => FromSilkWindowState(_window.WindowState);
        set
        {
            VerifyAccess();
            _window.WindowState = ToSilkWindowState(value);
        }
    }

    public bool Visible => _window.IsVisible;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            VerifyAccess();
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            _controller.SetEnabled(value);
            if (!value)
            {
                DeliverInput(new LibreInputEvent(
                    LibreInputEventKind.FocusLost,
                    Timestamp(),
                    LibreInputModifiers.None,
                    LibreKey.Unknown,
                    null,
                    default,
                    default,
                    LibrePointerButton.None));
            }
        }
    }

    public bool TopMost
    {
        get => _window.TopMost;
        set
        {
            VerifyAccess();
            _window.TopMost = value;
        }
    }

    public LibreWindowBorder Border
    {
        get => FromSilkWindowBorder(_window.WindowBorder);
        set
        {
            VerifyAccess();
            _window.WindowBorder = ToSilkWindowBorder(value);
            ApplyControllerBorder(value);
        }
    }

    public bool ShowInTaskbar
    {
        get => _showInTaskbar;
        set
        {
            VerifyAccess();
            if (_showInTaskbar == value)
            {
                return;
            }

            _showInTaskbar = value;
            _controller.SetShowInTaskbar(value);
        }
    }

    public bool CanMinimize
    {
        get => _canMinimize;
        set
        {
            VerifyAccess();
            if (_canMinimize == value)
            {
                return;
            }

            _canMinimize = value;
            _controller.SetCanMinimize(value);
        }
    }

    public bool CanMaximize
    {
        get => _canMaximize;
        set
        {
            VerifyAccess();
            if (_canMaximize == value)
            {
                return;
            }

            _canMaximize = value;
            _controller.SetCanMaximize(value);
        }
    }

    public bool CanClose
    {
        get => _canClose;
        set
        {
            VerifyAccess();
            if (_canClose == value)
            {
                return;
            }

            _canClose = value;
            _controller.SetCanClose(value);
        }
    }

    public double Opacity
    {
        get => _opacity;
        set
        {
            VerifyAccess();
            ValidateOpacity(value);
            if (_opacity == value)
            {
                return;
            }

            _opacity = value;
            _controller.SetOpacity(value);
        }
    }

    public void SetZOrder(LibreWindowZOrder value)
    {
        VerifyAccess();
        _controller.SetZOrder(value switch
        {
            LibreWindowZOrder.Front => NativeWindowZOrder.Front,
            LibreWindowZOrder.Back => NativeWindowZOrder.Back,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown window z-order operation."),
        });
    }

    public void SetCursor(LibreCursorShape shape)
    {
        VerifyAccess();
        _ = ToSilkStandardCursor(shape);
        _cursorShape = shape;
        ApplyCursor();
    }

    public void SetSizeConstraints(LibreSize minimum, LibreSize maximum)
    {
        VerifyAccess();
        ValidateSizeConstraints(minimum, maximum);
        _minimumSize = minimum;
        _maximumSize = maximum;
        ApplySizeConstraints();
    }

    public double FramebufferScale => DisplayScaleResolver.ResolveWindowFramebufferScale(_window);

    public double DpiScale => DisplayScaleResolver.ResolveWindowDisplayScale(_window, ResolveMonitorDpiScale());

    public void SetIcons(IReadOnlyList<LibreWindowIcon> icons)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(icons);
        if (icons.Count == 0)
        {
            _window.SetWindowIcon(default);
            return;
        }

        RawImage[] rawImages = new RawImage[icons.Count];
        byte[][] pixelBuffers = new byte[icons.Count][];
        for (int index = 0; index < icons.Count; index++)
        {
            LibreWindowIcon icon = icons[index]
                ?? throw new ArgumentException("Window icon collections cannot contain null entries.", nameof(icons));
            byte[] pixels = new byte[icon.PixelByteLength];
            icon.CopyPixelsTo(pixels);
            pixelBuffers[index] = pixels;
            rawImages[index] = new RawImage(icon.Width, icon.Height, pixels);
        }

        _window.SetWindowIcon(rawImages);
        GC.KeepAlive(pixelBuffers);
    }

    public void Show()
    {
        VerifyAccess();
        _window.IsVisible = true;
    }

    public void Hide()
    {
        VerifyAccess();
        _window.IsVisible = false;
    }

    public void Activate()
    {
        VerifyAccess();
        _window.Focus();
    }

    public void Close()
    {
        VerifyAccess();
        _window.Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        VerifyAccess();
        _disposed = true;
        _dispatcher.Unregister(this);
        _input?.Dispose();
        foreach (DrawingVisual visual in _paintLayers.Values)
        {
            visual.Context.Clear();
        }

        _paintLayers.Clear();
        _fallbackPaintVisual.Context.Clear();
        _transientPaintVisual.Context.Clear();
        _paintRoot.ClearChildren();
        _compositor?.Dispose();
        _wgpuContext?.Dispose();
        _controller.Dispose();
        _window.Dispose();
        _handles.Release(Handle);
        RaiseClosed();
    }

    internal void RequestPaint(LibreRectangle? dirtyRectangle)
    {
        VerifyAccess();
        if (_paintQueued && _dirtyRectangle is { } existing && dirtyRectangle is { } added)
        {
            _dirtyRectangle = Union(existing, added);
        }
        else
        {
            _dirtyRectangle = dirtyRectangle;
        }

        _paintQueued = true;
        _dispatcher.Wake();
    }

    public void PresentPendingPaint()
    {
        VerifyAccess();
        const int maxImmediateAttempts = 3;
        for (int attempt = 0;
             attempt < maxImmediateAttempts && (_paintQueued || _presentationQueued);
             attempt++)
        {
            _window.DoRender();
        }
    }

    internal Graphics CreateGraphics(
        LibrePoint origin,
        LibreRectangle clipRectangle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WgpuContext targetContext = _wgpuContext
            ?? throw new InvalidOperationException("The ProGPU window drawing context is not initialized.");
        DrawingContext recording = new();
        int infrastructureCommandCount = 0;
        Graphics graphics = Graphics.FromProGpuDrawingContext(
            recording,
            ToDrawingRectangle(clipRectangle),
            Matrix4x4.CreateTranslation(origin.X, origin.Y, 0f),
            targetContext,
            () => CompleteGraphics(recording, infrastructureCommandCount));
        ApplyLocalClip(graphics, origin, clipRectangle);
        // Disposing Graphics balances the initial clip with one pop command.
        infrastructureCommandCount = checked(recording.Commands.Count + 1);
        return graphics;
    }

    internal static Graphics CreateDetachedGraphics(
        LibrePoint origin,
        LibreRectangle clipRectangle)
    {
        DrawingContext recording = new();
        Graphics graphics = Graphics.FromProGpuDrawingContext(
            recording,
            ToDrawingRectangle(clipRectangle),
            Matrix4x4.CreateTranslation(origin.X, origin.Y, 0f),
            () => recording.Clear());
        ApplyLocalClip(graphics, origin, clipRectangle);
        return graphics;
    }

    private void CompleteGraphics(
        DrawingContext recording,
        int infrastructureCommandCount)
    {
        if (_disposed || recording.Commands.Count <= infrastructureCommandCount)
        {
            recording.Clear();
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            CommitGraphics(recording);
            return;
        }

        try
        {
            _dispatcher.Post(() => CommitGraphics(recording));
        }
        catch (ObjectDisposedException)
        {
            recording.Clear();
        }
        catch
        {
            recording.Clear();
            throw;
        }
    }

    private void CommitGraphics(DrawingContext recording)
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            _transientPaintVisual.Context.Append(recording);
            _transientPaintVisual.Invalidate();
            _presentationQueued = true;
            _dispatcher.Wake();
        }
        finally
        {
            recording.Clear();
        }
    }

    private static RectangleF ToDrawingRectangle(LibreRectangle rectangle)
        => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    private static void ApplyLocalClip(
        Graphics graphics,
        LibrePoint origin,
        LibreRectangle clipRectangle)
        => graphics.SetClip(new RectangleF(
            clipRectangle.X - origin.X,
            clipRectangle.Y - origin.Y,
            clipRectangle.Width,
            clipRectangle.Height));

    void IProGpuLoopParticipant.Pump()
    {
        if (_disposed)
        {
            return;
        }

        _window.DoEvents();
        _window.DoUpdate();
        if ((_paintQueued || _presentationQueued) && Visible)
        {
            _window.DoRender();
        }
    }

    private static LibreWindowBorder ResolveBorder(LibreWindowOptions options)
    {
        if (!options.HasFlag(LibreWindowOptions.Decorated))
        {
            return LibreWindowBorder.Hidden;
        }

        return options.HasFlag(LibreWindowOptions.Resizable)
            ? LibreWindowBorder.Resizable
            : LibreWindowBorder.Fixed;
    }

    private void ApplyControllerBorder(LibreWindowBorder border)
    {
        _controller.SetDecorations(border == LibreWindowBorder.Hidden
            ? NativeWindowDecorations.None
            : NativeWindowDecorations.Full);
        _controller.SetCanResize(border == LibreWindowBorder.Resizable);
    }

    private static WindowBorder ToSilkWindowBorder(LibreWindowBorder border)
        => border switch
        {
            LibreWindowBorder.Hidden => WindowBorder.Hidden,
            LibreWindowBorder.Fixed => WindowBorder.Fixed,
            LibreWindowBorder.Resizable => WindowBorder.Resizable,
            _ => throw new ArgumentOutOfRangeException(nameof(border), border, "Unknown window border mode."),
        };

    private static LibreWindowBorder FromSilkWindowBorder(WindowBorder border)
        => border switch
        {
            WindowBorder.Hidden => LibreWindowBorder.Hidden,
            WindowBorder.Fixed => LibreWindowBorder.Fixed,
            WindowBorder.Resizable => LibreWindowBorder.Resizable,
            _ => throw new ArgumentOutOfRangeException(nameof(border), border, "Unknown Silk.NET window border mode."),
        };

    private static WindowState ToSilkWindowState(LibreWindowState state)
        => state switch
        {
            LibreWindowState.Normal => WindowState.Normal,
            LibreWindowState.Minimized => WindowState.Minimized,
            LibreWindowState.Maximized => WindowState.Maximized,
            LibreWindowState.FullScreen => WindowState.Fullscreen,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown window state."),
        };

    private static LibreWindowState FromSilkWindowState(WindowState state)
        => state switch
        {
            WindowState.Normal => LibreWindowState.Normal,
            WindowState.Minimized => LibreWindowState.Minimized,
            WindowState.Maximized => LibreWindowState.Maximized,
            WindowState.Fullscreen => LibreWindowState.FullScreen,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown Silk.NET window state."),
        };

    private void SetNativeBounds(LibreRectangle bounds)
    {
        _window.Position = new Vector2D<int>(bounds.X, bounds.Y);
        _window.Size = new Vector2D<int>(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
    }

    private double ResolveMonitorDpiScale()
    {
        try
        {
            LibreRectangle nativeBounds = new(
                _window.Position.X,
                _window.Position.Y,
                _window.Size.X,
                _window.Size.Y);
            return _monitors.GetNearest(nativeBounds).DpiScale;
        }
        catch (PlatformNotSupportedException)
        {
            return FramebufferScale;
        }
    }

    private void AttachEvents()
    {
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Resize += _ => NotifyBoundsChanged();
        _window.FramebufferResize += _ => OnPresentationGeometryChanged();
        _window.Move += _ =>
        {
            if (!NotifyPresentationScaleChanged())
            {
                NotifyBoundsChanged();
            }
        };
        _window.StateChanged += OnStateChanged;
        _window.FocusChanged += focused => EmitInput(focused ? LibreInputEventKind.FocusGained : LibreInputEventKind.FocusLost);
        _window.Closing += OnClosing;
    }

    private void OnStateChanged(WindowState state)
    {
        if (!_initializing)
        {
            _events.StateChanged(FromSilkWindowState(state));
        }
    }

    private void OnPresentationGeometryChanged()
    {
        if (_initializing)
        {
            return;
        }

        NotifyPresentationScaleChanged();
        RequestPaint(dirtyRectangle: null);
    }

    private void NotifyBoundsChanged()
    {
        if (!_initializing && !_updatingPresentationGeometry)
        {
            _events.BoundsChanged(Bounds);
        }
    }

    private bool NotifyPresentationScaleChanged()
    {
        if (_initializing)
        {
            return false;
        }

        double dpiScale = DpiScale;
        double framebufferScale = FramebufferScale;
        bool dpiChanged = Math.Abs(dpiScale - _reportedDpiScale) >= 0.0001;
        bool framebufferChanged = Math.Abs(framebufferScale - _reportedFramebufferScale) >= 0.0001;
        if (!dpiChanged && !framebufferChanged)
        {
            return false;
        }

        double oldDpiScale = _reportedDpiScale;
        double oldFramebufferScale = _reportedFramebufferScale;
        _reportedDpiScale = dpiScale;
        _reportedFramebufferScale = framebufferScale;
        ResizeForPresentationScale(
            oldDpiScale,
            oldFramebufferScale,
            dpiScale,
            framebufferScale,
            dpiChanged);
        ApplySizeConstraints();
        _events.BoundsChanged(Bounds);

        if (dpiChanged)
        {
            _events.PresentationScaleChanged(dpiScale);
        }

        return true;
    }

    private void ResizeForPresentationScale(
        double oldDpiScale,
        double oldFramebufferScale,
        double newDpiScale,
        double newFramebufferScale,
        bool dpiChanged)
    {
        bool preserveLogicalSize = _coordinateMode == LibreWindowCoordinateMode.Logical;
        if (!preserveLogicalSize && !dpiChanged)
        {
            return;
        }

        LibreRectangle oldManagedSize = LibreWindowCoordinates.ToManaged(
            new LibreRectangle(0, 0, _window.Size.X, _window.Size.Y),
            _coordinateMode,
            oldDpiScale,
            oldFramebufferScale);
        int desiredWidth = preserveLogicalSize
            ? oldManagedSize.Width
            : ScaleForDpi(oldManagedSize.Width, newDpiScale, oldDpiScale);
        int desiredHeight = preserveLogicalSize
            ? oldManagedSize.Height
            : ScaleForDpi(oldManagedSize.Height, newDpiScale, oldDpiScale);
        LibreRectangle nativeSize = LibreWindowCoordinates.ToNative(
            new LibreRectangle(0, 0, desiredWidth, desiredHeight),
            _coordinateMode,
            newDpiScale,
            newFramebufferScale);
        if (_window.Size.X != nativeSize.Width || _window.Size.Y != nativeSize.Height)
        {
            _updatingPresentationGeometry = true;
            try
            {
                _window.Size = new Vector2D<int>(Math.Max(1, nativeSize.Width), Math.Max(1, nativeSize.Height));
            }
            finally
            {
                _updatingPresentationGeometry = false;
            }
        }
    }

    private static int ScaleForDpi(int value, double newDpiScale, double oldDpiScale)
        => checked((int)Math.Round(value * newDpiScale / oldDpiScale, MidpointRounding.AwayFromZero));

    private void ApplySizeConstraints()
    {
        LibreRectangle nativeMinimum = LibreWindowCoordinates.ToNative(
            new LibreRectangle(0, 0, _minimumSize.Width, _minimumSize.Height),
            _coordinateMode,
            _reportedDpiScale,
            _reportedFramebufferScale);
        LibreRectangle nativeMaximum = LibreWindowCoordinates.ToNative(
            new LibreRectangle(0, 0, _maximumSize.Width, _maximumSize.Height),
            _coordinateMode,
            _reportedDpiScale,
            _reportedFramebufferScale);
        _controller.SetSizeConstraints(
            new NativeWindowSize(nativeMinimum.Width, nativeMinimum.Height),
            new NativeWindowSize(
                _maximumSize.Width == 0 ? int.MaxValue : nativeMaximum.Width,
                _maximumSize.Height == 0 ? int.MaxValue : nativeMaximum.Height));
    }

    private static void ValidateSizeConstraints(LibreSize minimum, LibreSize maximum)
    {
        if (minimum.Width < 0 || minimum.Height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), minimum, "Minimum dimensions cannot be negative.");
        }

        if (maximum.Width < 0 || maximum.Height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), maximum, "Maximum dimensions cannot be negative.");
        }
    }

    private static void ValidateOpacity(double value)
    {
        if (!double.IsFinite(value) || value is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Window opacity must be finite and between zero and one.");
        }
    }

    private void OnLoad()
    {
        _controller.Attach();
        _wgpuContext = new WgpuContext();
        _wgpuContext.Initialize(_window);
        _compositor = new Compositor(
            _wgpuContext,
            _wgpuContext.SwapChainFormat,
            CompositorOptions.Default with
            {
                EnableGpuHitTesting = false,
                PrimarySampleCount = 1,
            });
        _input = _window.CreateInput();
        ApplyCursor();
        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }

        foreach (IMouse mouse in _input.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnMouseScroll;
        }
    }

    private unsafe void OnRender(double delta)
    {
        _ = delta;
        if ((!_paintQueued && !_presentationQueued) || _wgpuContext is null || _compositor is null)
        {
            return;
        }

        bool repaint = _paintQueued;
        _paintQueued = false;
        _presentationQueued = false;
        LibreRectangle surfaceBounds = GetSurfaceBounds();
        if (repaint)
        {
            LibreRectangle dirty = _dirtyRectangle ?? surfaceBounds;
            _dirtyRectangle = null;
            _transientPaintVisual.Context.Clear();
            using (WgpuContext.PushCurrent(_wgpuContext))
            {
                ProGpuRetainedPaintFrame frame = new(
                    _paintRoot,
                    _fallbackPaintVisual,
                    _transientPaintVisual,
                    _paintLayers,
                    surfaceBounds,
                    dirty);
                try
                {
                    _events.PaintRequested(frame);
                }
                finally
                {
                    frame.Complete();
                }
            }
        }

        _paintRoot.Size = new Vector2(surfaceBounds.Width, surfaceBounds.Height);
        _paintRoot.Invalidate();
        PresentFrame(_wgpuContext, _compositor, surfaceBounds);
    }

    private unsafe void PresentFrame(
        WgpuContext context,
        Compositor compositor,
        LibreRectangle surfaceBounds)
    {
        Vector2D<int> framebufferSize = _window.FramebufferSize;
        uint framebufferWidth = checked((uint)Math.Max(1, framebufferSize.X));
        uint framebufferHeight = checked((uint)Math.Max(1, framebufferSize.Y));
        if (!context.TryReconfigureIfNeeded(framebufferWidth, framebufferHeight) || context.Surface is null)
        {
            RequestPaint(surfaceBounds);
            return;
        }

        SurfaceTexture surfaceTexture = default;
        context.Api.SurfaceGetCurrentTexture(context.Surface, &surfaceTexture);
        TextureView* targetView = null;
        try
        {
            if (surfaceTexture.Status is SurfaceGetCurrentTextureStatus.Outdated or SurfaceGetCurrentTextureStatus.Lost)
            {
                context.TryConfigureSwapChain(framebufferWidth, framebufferHeight, refreshCapabilities: true);
                RequestPaint(surfaceBounds);
                return;
            }

            if (surfaceTexture.Status == SurfaceGetCurrentTextureStatus.Timeout)
            {
                RequestPaint(surfaceBounds);
                return;
            }

            if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
            {
                throw new InvalidOperationException($"WebGPU surface acquisition failed: {surfaceTexture.Status}.");
            }

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = context.SwapChainFormat,
                Dimension = TextureViewDimension.Dimension2D,
                BaseMipLevel = 0,
                MipLevelCount = 1,
                BaseArrayLayer = 0,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All,
            };
            targetView = context.Api.TextureCreateView(surfaceTexture.Texture, &viewDescriptor);
            float dpiScale = _coordinateMode == LibreWindowCoordinateMode.DevicePixels
                ? 1.0f
                : (float)DpiScale;
            compositor.RenderScene(
                _paintRoot,
                checked((uint)surfaceBounds.Width),
                checked((uint)surfaceBounds.Height),
                framebufferWidth,
                framebufferHeight,
                dpiScale,
                targetView);
            context.Api.SurfacePresent(context.Surface);
        }
        finally
        {
            if (targetView is not null)
            {
                context.Api.TextureViewRelease(targetView);
            }

            if (surfaceTexture.Texture is not null)
            {
                context.Api.TextureRelease(surfaceTexture.Texture);
            }
        }
    }

    private void OnClosing()
    {
        if (!_events.Closing())
        {
            _window.IsClosing = false;
            return;
        }

        Dispose();
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        _ = scanCode;
        EmitInput(LibreInputEventKind.KeyDown, key: MapKey(key), modifiers: ReadModifiers(keyboard));
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode)
    {
        _ = scanCode;
        EmitInput(LibreInputEventKind.KeyUp, key: MapKey(key), modifiers: ReadModifiers(keyboard));
    }

    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        _ = keyboard;
        EmitInput(LibreInputEventKind.TextInput, text: character.ToString());
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
        => EmitPointer(LibreInputEventKind.PointerDown, mouse.Position, button);

    private void OnMouseUp(IMouse mouse, MouseButton button)
        => EmitPointer(LibreInputEventKind.PointerUp, mouse.Position, button);

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        _ = mouse;
        EmitPointer(LibreInputEventKind.PointerMove, position, MouseButton.Unknown);
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel wheel)
        => DeliverInput(new LibreInputEvent(
            LibreInputEventKind.PointerWheel,
            Timestamp(),
            ReadModifiers(),
            LibreKey.Unknown,
            null,
            ToManagedPoint(mouse.Position),
            new LibrePoint(checked((int)Math.Round(wheel.X * 120)), checked((int)Math.Round(wheel.Y * 120))),
            LibrePointerButton.None));

    private void ApplyCursor()
    {
        if (_input is null)
        {
            return;
        }

        StandardCursor requested = ToSilkStandardCursor(_cursorShape);
        foreach (IMouse mouse in _input.Mice)
        {
            ICursor cursor = mouse.Cursor;
            cursor.CursorMode = CursorMode.Normal;
            cursor.Type = CursorType.Standard;
            cursor.StandardCursor = cursor.IsSupported(requested)
                ? requested
                : StandardCursor.Arrow;
        }
    }

    private static StandardCursor ToSilkStandardCursor(LibreCursorShape shape)
        => shape switch
        {
            LibreCursorShape.Arrow => StandardCursor.Arrow,
            LibreCursorShape.IBeam => StandardCursor.IBeam,
            LibreCursorShape.Wait => StandardCursor.Wait,
            LibreCursorShape.Cross => StandardCursor.Crosshair,
            LibreCursorShape.SizeWE or LibreCursorShape.HSplit or LibreCursorShape.NoMoveVert or
                LibreCursorShape.PanEast or LibreCursorShape.PanWest => StandardCursor.HResize,
            LibreCursorShape.SizeNS or LibreCursorShape.VSplit or LibreCursorShape.NoMoveHoriz or
                LibreCursorShape.PanNorth or LibreCursorShape.PanSouth => StandardCursor.VResize,
            LibreCursorShape.SizeAll or LibreCursorShape.NoMove2D => StandardCursor.ResizeAll,
            LibreCursorShape.No => StandardCursor.NotAllowed,
            LibreCursorShape.Hand => StandardCursor.Hand,
            LibreCursorShape.AppStarting => StandardCursor.WaitArrow,
            LibreCursorShape.SizeNWSE or LibreCursorShape.PanNW or LibreCursorShape.PanSE =>
                StandardCursor.NwseResize,
            LibreCursorShape.SizeNESW or LibreCursorShape.PanNE or LibreCursorShape.PanSW =>
                StandardCursor.NeswResize,
            LibreCursorShape.Help => StandardCursor.Hand,
            LibreCursorShape.UpArrow => StandardCursor.Arrow,
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown cursor shape."),
        };

    private void EmitPointer(LibreInputEventKind kind, Vector2 position, MouseButton button)
        => DeliverInput(new LibreInputEvent(
            kind,
            Timestamp(),
            ReadModifiers(),
            LibreKey.Unknown,
            null,
            ToManagedPoint(position),
            default,
            button switch
            {
                MouseButton.Left => LibrePointerButton.Primary,
                MouseButton.Right => LibrePointerButton.Secondary,
                MouseButton.Middle => LibrePointerButton.Middle,
                MouseButton.Button4 => LibrePointerButton.XButton1,
                MouseButton.Button5 => LibrePointerButton.XButton2,
                _ => LibrePointerButton.None,
            }));

    private void EmitInput(
        LibreInputEventKind kind,
        LibreKey key = LibreKey.Unknown,
        string? text = null,
        LibreInputModifiers modifiers = LibreInputModifiers.None)
        => DeliverInput(new LibreInputEvent(kind, Timestamp(), modifiers, key, text, default, default, LibrePointerButton.None));

    private void DeliverInput(in LibreInputEvent inputEvent)
    {
        if (_enabled || inputEvent.Kind == LibreInputEventKind.FocusLost)
        {
            _events.Input(inputEvent);
        }
    }

    private LibreInputModifiers ReadModifiers()
    {
        LibreInputModifiers modifiers = LibreInputModifiers.None;
        if (_input is null)
        {
            return modifiers;
        }

        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            modifiers |= ReadModifiers(keyboard);
        }

        return modifiers;
    }

    private static LibreInputModifiers ReadModifiers(IKeyboard keyboard)
    {
        LibreInputModifiers modifiers = LibreInputModifiers.None;
        if (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight)) modifiers |= LibreInputModifiers.Shift;
        if (keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight)) modifiers |= LibreInputModifiers.Control;
        if (keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight)) modifiers |= LibreInputModifiers.Alt;
        if (keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight)) modifiers |= LibreInputModifiers.Meta;
        return modifiers;
    }

    private static LibreKey MapKey(Key key) => key switch
    {
        Key.Space => LibreKey.Space,
        Key.Apostrophe => LibreKey.Apostrophe,
        Key.Comma => LibreKey.Comma,
        Key.Minus => LibreKey.Minus,
        Key.Period => LibreKey.Period,
        Key.Slash => LibreKey.Slash,
        Key.Number0 => LibreKey.D0,
        Key.Number1 => LibreKey.D1,
        Key.Number2 => LibreKey.D2,
        Key.Number3 => LibreKey.D3,
        Key.Number4 => LibreKey.D4,
        Key.Number5 => LibreKey.D5,
        Key.Number6 => LibreKey.D6,
        Key.Number7 => LibreKey.D7,
        Key.Number8 => LibreKey.D8,
        Key.Number9 => LibreKey.D9,
        Key.Semicolon => LibreKey.Semicolon,
        Key.Equal => LibreKey.Equal,
        Key.A => LibreKey.A,
        Key.B => LibreKey.B,
        Key.C => LibreKey.C,
        Key.D => LibreKey.D,
        Key.E => LibreKey.E,
        Key.F => LibreKey.F,
        Key.G => LibreKey.G,
        Key.H => LibreKey.H,
        Key.I => LibreKey.I,
        Key.J => LibreKey.J,
        Key.K => LibreKey.K,
        Key.L => LibreKey.L,
        Key.M => LibreKey.M,
        Key.N => LibreKey.N,
        Key.O => LibreKey.O,
        Key.P => LibreKey.P,
        Key.Q => LibreKey.Q,
        Key.R => LibreKey.R,
        Key.S => LibreKey.S,
        Key.T => LibreKey.T,
        Key.U => LibreKey.U,
        Key.V => LibreKey.V,
        Key.W => LibreKey.W,
        Key.X => LibreKey.X,
        Key.Y => LibreKey.Y,
        Key.Z => LibreKey.Z,
        Key.LeftBracket => LibreKey.LeftBracket,
        Key.BackSlash => LibreKey.Backslash,
        Key.RightBracket => LibreKey.RightBracket,
        Key.GraveAccent => LibreKey.GraveAccent,
        Key.Escape => LibreKey.Escape,
        Key.Enter => LibreKey.Enter,
        Key.Tab => LibreKey.Tab,
        Key.Backspace => LibreKey.Backspace,
        Key.Insert => LibreKey.Insert,
        Key.Delete => LibreKey.Delete,
        Key.Right => LibreKey.Right,
        Key.Left => LibreKey.Left,
        Key.Down => LibreKey.Down,
        Key.Up => LibreKey.Up,
        Key.PageUp => LibreKey.PageUp,
        Key.PageDown => LibreKey.PageDown,
        Key.Home => LibreKey.Home,
        Key.End => LibreKey.End,
        Key.CapsLock => LibreKey.CapsLock,
        Key.ScrollLock => LibreKey.ScrollLock,
        Key.NumLock => LibreKey.NumLock,
        Key.PrintScreen => LibreKey.PrintScreen,
        Key.Pause => LibreKey.Pause,
        Key.F1 => LibreKey.F1,
        Key.F2 => LibreKey.F2,
        Key.F3 => LibreKey.F3,
        Key.F4 => LibreKey.F4,
        Key.F5 => LibreKey.F5,
        Key.F6 => LibreKey.F6,
        Key.F7 => LibreKey.F7,
        Key.F8 => LibreKey.F8,
        Key.F9 => LibreKey.F9,
        Key.F10 => LibreKey.F10,
        Key.F11 => LibreKey.F11,
        Key.F12 => LibreKey.F12,
        Key.F13 => LibreKey.F13,
        Key.F14 => LibreKey.F14,
        Key.F15 => LibreKey.F15,
        Key.F16 => LibreKey.F16,
        Key.F17 => LibreKey.F17,
        Key.F18 => LibreKey.F18,
        Key.F19 => LibreKey.F19,
        Key.F20 => LibreKey.F20,
        Key.F21 => LibreKey.F21,
        Key.F22 => LibreKey.F22,
        Key.F23 => LibreKey.F23,
        Key.F24 => LibreKey.F24,
        Key.F25 => LibreKey.F25,
        Key.Keypad0 => LibreKey.NumPad0,
        Key.Keypad1 => LibreKey.NumPad1,
        Key.Keypad2 => LibreKey.NumPad2,
        Key.Keypad3 => LibreKey.NumPad3,
        Key.Keypad4 => LibreKey.NumPad4,
        Key.Keypad5 => LibreKey.NumPad5,
        Key.Keypad6 => LibreKey.NumPad6,
        Key.Keypad7 => LibreKey.NumPad7,
        Key.Keypad8 => LibreKey.NumPad8,
        Key.Keypad9 => LibreKey.NumPad9,
        Key.KeypadDecimal => LibreKey.NumPadDecimal,
        Key.KeypadDivide => LibreKey.NumPadDivide,
        Key.KeypadMultiply => LibreKey.NumPadMultiply,
        Key.KeypadSubtract => LibreKey.NumPadSubtract,
        Key.KeypadAdd => LibreKey.NumPadAdd,
        Key.KeypadEnter => LibreKey.NumPadEnter,
        Key.KeypadEqual => LibreKey.NumPadEqual,
        Key.ShiftLeft => LibreKey.LeftShift,
        Key.ControlLeft => LibreKey.LeftControl,
        Key.AltLeft => LibreKey.LeftAlt,
        Key.SuperLeft => LibreKey.LeftMeta,
        Key.ShiftRight => LibreKey.RightShift,
        Key.ControlRight => LibreKey.RightControl,
        Key.AltRight => LibreKey.RightAlt,
        Key.SuperRight => LibreKey.RightMeta,
        Key.Menu => LibreKey.Menu,
        _ => LibreKey.Unknown,
    };

    private LibrePoint ToManagedPoint(Vector2 position)
    {
        LibreRectangle mapped = LibreWindowCoordinates.ToManaged(
            new LibreRectangle(
                checked((int)Math.Round(position.X)),
                checked((int)Math.Round(position.Y)),
                0,
                0),
            _coordinateMode,
            DpiScale,
            FramebufferScale);
        return new(mapped.X, mapped.Y);
    }

    private LibreRectangle GetSurfaceBounds()
    {
        if (_coordinateMode == LibreWindowCoordinateMode.DevicePixels)
        {
            Vector2D<int> framebufferSize = _window.FramebufferSize;
            return new(0, 0, Math.Max(1, framebufferSize.X), Math.Max(1, framebufferSize.Y));
        }

        LibreRectangle logical = LibreWindowCoordinates.ToManaged(
            new LibreRectangle(0, 0, _window.Size.X, _window.Size.Y),
            _coordinateMode,
            DpiScale,
            FramebufferScale);
        return new(0, 0, Math.Max(1, logical.Width), Math.Max(1, logical.Height));
    }

    private static LibreRectangle Union(LibreRectangle left, LibreRectangle right)
    {
        int x = Math.Min(left.X, right.X);
        int y = Math.Min(left.Y, right.Y);
        int rectangleRight = Math.Max(left.Right, right.Right);
        int rectangleBottom = Math.Max(left.Bottom, right.Bottom);
        return new LibreRectangle(x, y, checked(rectangleRight - x), checked(rectangleBottom - y));
    }

    private static long Timestamp()
        => checked((long)(Stopwatch.GetTimestamp() * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));

    private void RaiseClosed()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _events.Closed();
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("The Silk.NET window must be used from its dispatcher thread.");
        }
    }
}
