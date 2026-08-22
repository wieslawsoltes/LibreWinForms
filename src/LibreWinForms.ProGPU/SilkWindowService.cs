// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using LibreWinForms.Platform;
using ProGPU.Backend;
using ProGPU.Scene;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;

namespace LibreWinForms.ProGPU;

public sealed class SilkWindowService : ILibreWindowService
{
    private readonly ProGpuDispatcher _dispatcher;
    private readonly ILibreHandleRegistry _handles;

    public SilkWindowService(ProGpuDispatcher dispatcher, ILibreHandleRegistry handles)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _handles = handles ?? throw new ArgumentNullException(nameof(handles));
    }

    public ILibreWindow Create(in LibreWindowCreateOptions options, ILibreWindowEvents events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("Silk.NET windows must be created on the dispatcher thread.");
        }

        return new SilkLibreWindow(_dispatcher, _handles, options, events);
    }
}

internal sealed class SilkLibreWindow : ILibreWindow, IProGpuLoopParticipant
{
    private readonly ProGpuDispatcher _dispatcher;
    private readonly ILibreHandleRegistry _handles;
    private readonly ILibreWindowEvents _events;
    private readonly IWindow _window;
    private readonly SilkWindowController _controller;
    private readonly DrawingVisual _paintVisual = new();
    private IInputContext? _input;
    private WgpuContext? _wgpuContext;
    private Compositor? _compositor;
    private bool _paintQueued;
    private LibreRectangle? _dirtyRectangle;
    private bool _closed;
    private bool _disposed;

    internal SilkLibreWindow(
        ProGpuDispatcher dispatcher,
        ILibreHandleRegistry handles,
        in LibreWindowCreateOptions options,
        ILibreWindowEvents events)
    {
        _dispatcher = dispatcher;
        _handles = handles;
        _events = events;
        WindowOptions silkOptions = WindowOptions.Default with
        {
            API = GraphicsAPI.None,
            IsVisible = false,
            IsEventDriven = false,
            ShouldSwapAutomatically = false,
            VSync = false,
            FramesPerSecond = 0,
            UpdatesPerSecond = 0,
            Size = new Vector2D<int>(Math.Max(1, options.Bounds.Width), Math.Max(1, options.Bounds.Height)),
            Position = new Vector2D<int>(options.Bounds.X, options.Bounds.Y),
            Title = options.Title,
            TopMost = options.Options.HasFlag(LibreWindowOptions.TopMost),
            WindowBorder = ResolveBorder(options.Options),
        };

        _window = Silk.NET.Windowing.Window.Create(silkOptions);
        _controller = new SilkWindowController(_window);
        Handle = handles.Allocate(this, LibreHandleKind.Window);
        AttachEvents();
        _window.Initialize();
        _dispatcher.Register(this);
        if (options.Options.HasFlag(LibreWindowOptions.Visible))
        {
            Show();
        }
    }

    public LibreHandle Handle { get; }

    public LibreRectangle Bounds
    {
        get => new(_window.Position.X, _window.Position.Y, _window.Size.X, _window.Size.Y);
        set
        {
            VerifyAccess();
            _window.Position = new Vector2D<int>(value.X, value.Y);
            _window.Size = new Vector2D<int>(Math.Max(1, value.Width), Math.Max(1, value.Height));
        }
    }

    public LibreWindowState State
    {
        get => _window.WindowState switch
        {
            WindowState.Minimized => LibreWindowState.Minimized,
            WindowState.Maximized => LibreWindowState.Maximized,
            WindowState.Fullscreen => LibreWindowState.FullScreen,
            _ => LibreWindowState.Normal,
        };
        set
        {
            VerifyAccess();
            _window.WindowState = value switch
            {
                LibreWindowState.Minimized => WindowState.Minimized,
                LibreWindowState.Maximized => WindowState.Maximized,
                LibreWindowState.FullScreen => WindowState.Fullscreen,
                _ => WindowState.Normal,
            };
        }
    }

    public bool Visible => _window.IsVisible;

    public double DpiScale => DisplayScaleResolver.ResolveWindowDisplayScale(_window);

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
        _paintVisual.Context.Clear();
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

    void IProGpuLoopParticipant.Pump()
    {
        if (_disposed)
        {
            return;
        }

        _window.DoEvents();
        _window.DoUpdate();
        if (_paintQueued && Visible)
        {
            _window.DoRender();
        }
    }

    private static WindowBorder ResolveBorder(LibreWindowOptions options)
    {
        if (!options.HasFlag(LibreWindowOptions.Decorated))
        {
            return WindowBorder.Hidden;
        }

        return options.HasFlag(LibreWindowOptions.Resizable) ? WindowBorder.Resizable : WindowBorder.Fixed;
    }

    private void AttachEvents()
    {
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Resize += _ => _events.BoundsChanged(Bounds);
        _window.Move += _ => _events.BoundsChanged(Bounds);
        _window.FocusChanged += focused => EmitInput(focused ? LibreInputEventKind.FocusGained : LibreInputEventKind.FocusLost);
        _window.Closing += OnClosing;
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
        if (!_paintQueued || _wgpuContext is null || _compositor is null)
        {
            return;
        }

        _paintQueued = false;
        LibreRectangle surfaceBounds = new(0, 0, Math.Max(1, _window.Size.X), Math.Max(1, _window.Size.Y));
        LibreRectangle dirty = _dirtyRectangle ?? surfaceBounds;
        _dirtyRectangle = null;

        _paintVisual.Context.Clear();
        using (WgpuContext.PushCurrent(_wgpuContext))
        using (Graphics graphics = Graphics.FromProGpuDrawingContext(
            _paintVisual.Context,
            new RectangleF(0f, 0f, surfaceBounds.Width, surfaceBounds.Height)))
        {
            _events.PaintRequested(new ProGpuPaintFrame(graphics, surfaceBounds, dirty));
        }

        _paintVisual.Size = new Vector2(surfaceBounds.Width, surfaceBounds.Height);
        _paintVisual.Invalidate();
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
            float dpiScale = (float)DpiScale;
            compositor.RenderScene(
                _paintVisual,
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

    private sealed record ProGpuPaintFrame(
        Graphics Graphics,
        LibreRectangle SurfaceBounds,
        LibreRectangle DirtyRectangle) : ILibrePaintFrame;

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
        EmitInput(LibreInputEventKind.KeyDown, key: (int)key, modifiers: ReadModifiers(keyboard));
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode)
    {
        _ = scanCode;
        EmitInput(LibreInputEventKind.KeyUp, key: (int)key, modifiers: ReadModifiers(keyboard));
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
        => _events.Input(new LibreInputEvent(
            LibreInputEventKind.PointerWheel,
            Timestamp(),
            LibreInputModifiers.None,
            0,
            null,
            ToPoint(mouse.Position),
            new LibrePoint(checked((int)Math.Round(wheel.X * 120)), checked((int)Math.Round(wheel.Y * 120))),
            LibrePointerButton.None));

    private void EmitPointer(LibreInputEventKind kind, Vector2 position, MouseButton button)
        => _events.Input(new LibreInputEvent(
            kind,
            Timestamp(),
            LibreInputModifiers.None,
            0,
            null,
            ToPoint(position),
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
        int key = 0,
        string? text = null,
        LibreInputModifiers modifiers = LibreInputModifiers.None)
        => _events.Input(new LibreInputEvent(kind, Timestamp(), modifiers, key, text, default, default, LibrePointerButton.None));

    private static LibreInputModifiers ReadModifiers(IKeyboard keyboard)
    {
        LibreInputModifiers modifiers = LibreInputModifiers.None;
        if (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight)) modifiers |= LibreInputModifiers.Shift;
        if (keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight)) modifiers |= LibreInputModifiers.Control;
        if (keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight)) modifiers |= LibreInputModifiers.Alt;
        if (keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight)) modifiers |= LibreInputModifiers.Meta;
        return modifiers;
    }

    private static LibrePoint ToPoint(Vector2 position)
        => new(checked((int)Math.Round(position.X)), checked((int)Math.Round(position.Y)));

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
