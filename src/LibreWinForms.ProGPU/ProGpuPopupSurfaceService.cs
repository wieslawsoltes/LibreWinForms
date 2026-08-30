// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>
/// Owns undecorated, non-activating Silk popup windows whose retained content is
/// recorded through the same ProGPU System.Drawing path as canonical controls.
/// </summary>
public sealed class ProGpuPopupSurfaceService : ILibrePopupSurfaceService, IDisposable
{
    private static readonly LibreAdornerId s_contentAdorner = new(1);
    private readonly ILibreDispatcher _dispatcher;
    private readonly ILibreWindowService _windows;
    private readonly ILibreAdornerService _adorners;
    private readonly Dictionary<PopupKey, Session> _sessions = [];
    private bool _disposed;

    public ProGpuPopupSurfaceService(
        ILibreDispatcher dispatcher,
        ILibreWindowService windows,
        ILibreAdornerService adorners)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _adorners = adorners ?? throw new ArgumentNullException(nameof(adorners));
    }

    public System.Drawing.Graphics CreateGraphics(in LibrePopupSurfaceRequest request)
    {
        VerifyAccess();
        Validate(request);
        PopupKey key = new(request.Owner, request.Popup);
        if (_sessions.TryGetValue(key, out Session? session)
            && !session.CanUpdate(request))
        {
            Remove(key, session);
            session = null;
        }

        if (session is null)
        {
            session = new Session(this, key, _windows, request);
            _sessions.Add(key, session);
        }
        else
        {
            session.Update(request);
        }

        try
        {
            System.Drawing.Graphics graphics = _adorners.CreateGraphics(
                session.Window.Handle,
                s_contentAdorner,
                new LibreRectangle(0, 0, request.ScreenBounds.Width, request.ScreenBounds.Height),
                new LibreRectangle(0, 0, request.ScreenBounds.Width, request.ScreenBounds.Height));
            session.Show();
            return graphics;
        }
        catch
        {
            Remove(key, session);
            throw;
        }
    }

    public void Hide(LibreHandle owner, LibrePopupId popup)
    {
        VerifyAccess();
        if (owner.IsNull)
        {
            throw new ArgumentException("A popup owner cannot be null.", nameof(owner));
        }

        if (popup.IsNull)
        {
            throw new ArgumentException("A popup identity cannot be null.", nameof(popup));
        }

        PopupKey key = new(owner, popup);
        if (_sessions.Remove(key, out Session? session))
        {
            session.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        VerifyAccess();
        _disposed = true;
        Session[] sessions = [.. _sessions.Values];
        _sessions.Clear();
        foreach (Session session in sessions)
        {
            session.Dispose();
        }
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("Popup surfaces must be updated on the owning dispatcher thread.");
        }
    }

    private static void Validate(in LibrePopupSurfaceRequest request)
    {
        if (request.Owner.IsNull)
        {
            throw new ArgumentException("A popup owner cannot be null.", nameof(request));
        }

        if (request.Popup.IsNull)
        {
            throw new ArgumentException("A popup identity cannot be null.", nameof(request));
        }

        if (request.ScreenBounds.Width <= 0 || request.ScreenBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Popup dimensions must be positive.");
        }

        if (!double.IsFinite(request.DpiScale) || request.DpiScale is <= 0d or > 8d)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Popup DPI scale must be finite and in the range (0, 8].");
        }

        if (request.DismissalPolicy != LibrePopupDismissalPolicy.Explicit)
        {
            throw new PlatformNotSupportedException(
                "The ProGPU popup host currently requires canonical code to control dismissal explicitly.");
        }
    }

    private void Remove(PopupKey key, Session session)
    {
        _sessions.Remove(key);
        session.Dispose();
    }

    private void Closed(PopupKey key, Session session)
    {
        if (_sessions.TryGetValue(key, out Session? current) && ReferenceEquals(current, session))
        {
            _sessions.Remove(key);
        }
    }

    private readonly record struct PopupKey(LibreHandle Owner, LibrePopupId Popup);

    private sealed class Session : ILibreWindowEvents, IDisposable
    {
        private readonly ProGpuPopupSurfaceService _service;
        private readonly PopupKey _key;
        private readonly bool _inputTransparent;
        private readonly double _dpiScale;
        private bool _disposed;

        internal Session(
            ProGpuPopupSurfaceService service,
            PopupKey key,
            ILibreWindowService windows,
            in LibrePopupSurfaceRequest request)
        {
            _service = service;
            _key = key;
            _inputTransparent = request.InputTransparent;
            _dpiScale = request.DpiScale;
            LibreSize fixedSize = new(request.ScreenBounds.Width, request.ScreenBounds.Height);
            LibreWindowOptions options = LibreWindowOptions.TopMost
                | LibreWindowOptions.ToolWindow
                | LibreWindowOptions.Popup;
            if (request.InputTransparent)
            {
                options |= LibreWindowOptions.InputTransparent;
            }

            Window = windows.Create(
                new LibreWindowCreateOptions(
                    string.Empty,
                    request.ScreenBounds,
                    options,
                    request.Owner,
                    LibreWindowCoordinateMode.Logical,
                    request.DpiScale,
                    LibreWindowState.Normal,
                    ShowInTaskbar: false,
                    CanMinimize: false,
                    CanMaximize: false,
                    MinimumSize: fixedSize,
                    MaximumSize: fixedSize,
                    CanClose: false),
                this);
        }

        internal ILibreWindow Window { get; }

        internal bool CanUpdate(in LibrePopupSurfaceRequest request)
            => _inputTransparent == request.InputTransparent
                && Math.Abs(_dpiScale - request.DpiScale) < 0.0001;

        internal void Update(in LibrePopupSurfaceRequest request)
        {
            Window.Bounds = request.ScreenBounds;
            LibreSize fixedSize = new(request.ScreenBounds.Width, request.ScreenBounds.Height);
            Window.SetSizeConstraints(fixedSize, fixedSize);
        }

        internal void Show()
        {
            if (!Window.Visible)
            {
                Window.Show();
            }

            Window.SetZOrder(LibreWindowZOrder.Front);
        }

        public bool Closing() => true;

        public void Closed() => _service.Closed(_key, this);

        public void BoundsChanged(LibreRectangle bounds)
        {
            _ = bounds;
        }

        public void StateChanged(LibreWindowState state)
        {
            _ = state;
        }

        public void PresentationScaleChanged(double scale)
        {
            _ = scale;
        }

        public void PaintRequested(ILibrePaintFrame frame)
        {
            _ = frame;
        }

        public void Input(in LibreInputEvent inputEvent)
        {
            _ = inputEvent;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Window.Dispose();
        }
    }
}
