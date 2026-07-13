using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace System.Windows.Forms.Integration;

internal sealed class WpfPortableWinFormsApplicationHost :
    Forms.IWinFormsApplicationHost,
    Forms.IWinFormsThreadApplicationHost,
    Forms.IWinFormsTimerHost,
    Forms.IWinFormsIdleHost,
    Forms.IWinFormsModalDialogHost,
    Forms.IWinFormsDispatcherHost,
    Forms.IWinFormsDragDropHost,
    Forms.IWinFormsCoordinateHost,
    Forms.IWinFormsGraphicsHost
{
    private readonly object _gate = new();
    private readonly Dictionary<Forms.Form, Window> _windows = new();
    private readonly HashSet<Forms.Form> _pendingDialogCompletions = new();
    private readonly Dispatcher _dispatcher;

    public static WpfPortableWinFormsApplicationHost Instance { get; } = new();

    private WpfPortableWinFormsApplicationHost()
    {
        _dispatcher = WpfApplication.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    bool Forms.IWinFormsDispatcherHost.CheckAccess() => _dispatcher.CheckAccess();

    void Forms.IWinFormsDispatcherHost.BeginInvoke(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfDispatcherUnavailable();
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, callback);
    }

    void Forms.IWinFormsDispatcherHost.Invoke(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfDispatcherUnavailable();
        if (_dispatcher.CheckAccess())
        {
            callback();
            return;
        }

        _dispatcher.Invoke(callback, DispatcherPriority.Send);
    }

    bool Forms.IWinFormsIdleHost.TryBeginInvokeIdle(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            return false;

        try
        {
            _dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, callback);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    Forms.DragDropEffects Forms.IWinFormsDragDropHost.DoDragDrop(
        Forms.Control source,
        Forms.IDataObject data,
        Forms.DragDropEffects allowedEffects)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(data);
        ThrowIfDispatcherUnavailable();

        if (_dispatcher.CheckAccess())
        {
            return WindowsFormsHost.DoPortableDragDrop(source, data, allowedEffects);
        }

        Forms.DragDropEffects result = Forms.DragDropEffects.None;
        _dispatcher.Invoke(
            () => result = WindowsFormsHost.DoPortableDragDrop(source, data, allowedEffects),
            DispatcherPriority.Send);
        return result;
    }

    bool Forms.IWinFormsCoordinateHost.TryPointToScreen(
        Forms.Control control,
        System.Drawing.Point point,
        out System.Drawing.Point screenPoint)
    {
        return WindowsFormsHost.TryConvertControlPointToScreen(control, point, out screenPoint);
    }

    bool Forms.IWinFormsCoordinateHost.TryPointToClient(
        Forms.Control control,
        System.Drawing.Point point,
        out System.Drawing.Point clientPoint)
    {
        return WindowsFormsHost.TryConvertScreenPointToControl(control, point, out clientPoint);
    }

    bool Forms.IWinFormsGraphicsHost.TryCreateGraphics(
        Forms.Control control,
        out System.Drawing.Graphics graphics)
    {
        return WindowsFormsHost.TryCreateControlGraphics(control, out graphics);
    }

    public void Run(Forms.Form mainForm)
    {
        ArgumentNullException.ThrowIfNull(mainForm);

        WpfApplication? application = WpfApplication.Current;
        bool ownsApplication = application == null;
        application ??= new WpfApplication();

        Window window = CreateWindow(mainForm, owner: null, modal: false);
        if (ownsApplication)
        {
            application.Run(window);
        }
        else
        {
            window.ShowDialog();
        }
    }

    Forms.IWinFormsApplicationThreadContext Forms.IWinFormsThreadApplicationHost.CreateThreadContext(
        Forms.Form? mainForm)
    {
        return new WpfApplicationThreadContext(
            this,
            mainForm,
            _dispatcher,
            _dispatcher.CheckAccess());
    }

    public Forms.DialogResult ShowDialog(Forms.Form form, Forms.IWin32Window? owner)
    {
        ArgumentNullException.ThrowIfNull(form);

        Window? ownerWindow = ResolveOwnerWindow(owner);

        Window window = CreateWindow(form, ownerWindow, modal: true);
        window.ShowDialog();
        return form.DialogResult;
    }

    private Window? ResolveOwnerWindow(Forms.IWin32Window? owner)
    {
        if (owner == null)
        {
            return null;
        }

        if (owner is Forms.Form ownerForm)
        {
            lock (_gate)
            {
                if (_windows.TryGetValue(ownerForm, out Window? ownerWindow))
                {
                    return ownerWindow;
                }
            }
        }

        IntPtr ownerHandle = owner.Handle;
        if (ownerHandle == IntPtr.Zero)
        {
            return null;
        }

        HwndSource? source = HwndSource.FromHwnd(ownerHandle);
        if (source?.RootVisual is Window sourceWindow)
        {
            return sourceWindow;
        }

        return source?.RootVisual is DependencyObject rootVisual
            ? Window.GetWindow(rootVisual)
            : null;
    }

    public void ExitThread()
    {
        WpfApplication? application = WpfApplication.Current;
        if (application == null)
        {
            return;
        }

        InvokeOnDispatcher(application.Dispatcher, application.Shutdown);
    }

    public IDisposable RegisterTimer(int intervalMilliseconds, Action callback)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(intervalMilliseconds, 1);
        ArgumentNullException.ThrowIfNull(callback);

        return new DispatcherTimerRegistration(
            _dispatcher,
            intervalMilliseconds,
            callback);
    }

    private static DispatcherTimerRegistration CreateTimerRegistration(
        Dispatcher dispatcher,
        int intervalMilliseconds,
        Action callback)
    {
        return new DispatcherTimerRegistration(
            dispatcher,
            intervalMilliseconds,
            callback);
    }

    private void ThrowIfDispatcherUnavailable()
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            throw new InvalidOperationException("The WPF UI dispatcher is shutting down.");
        }
    }

    public void RequestDialogCompletion(Forms.Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        Window? window;
        lock (_gate)
        {
            if (!_windows.TryGetValue(form, out window)
                || !_pendingDialogCompletions.Add(form))
            {
                return;
            }
        }

        if (window.Dispatcher.HasShutdownStarted || window.Dispatcher.HasShutdownFinished)
        {
            RemovePendingDialogCompletion(form);
            return;
        }

        try
        {
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(() => CompleteDialogIfRequested(form, window)));
        }
        catch (InvalidOperationException)
        {
            RemovePendingDialogCompletion(form);
        }
    }

    private void RemovePendingDialogCompletion(Forms.Form form)
    {
        lock (_gate)
        {
            _pendingDialogCompletions.Remove(form);
        }
    }

    private void CompleteDialogIfRequested(Forms.Form form, Window window)
    {
        lock (_gate)
        {
            _pendingDialogCompletions.Remove(form);
            if (!_windows.TryGetValue(form, out Window? currentWindow)
                || !ReferenceEquals(currentWindow, window))
            {
                return;
            }
        }

        if (form.DialogResult != Forms.DialogResult.None)
        {
            _ = form.Close(Forms.CloseReason.None);
        }
    }

    private Window CreateWindow(Forms.Form form, Window? owner, bool modal)
    {
        var host = new WindowsFormsHost
        {
            Child = form
        };

        var window = new Window
        {
            Title = GetWindowTitle(form),
            Width = GetInitialDimension(form.Width, 800),
            Height = GetInitialDimension(form.Height, 600),
            MinWidth = form.MinimumSize.Width > 0 ? form.MinimumSize.Width : 0,
            MinHeight = form.MinimumSize.Height > 0 ? form.MinimumSize.Height : 0,
            Content = host,
            ShowInTaskbar = form.ShowInTaskbar,
            ResizeMode = ToResizeMode(form),
            WindowStyle = form.FormBorderStyle == Forms.FormBorderStyle.None ? WindowStyle.None : WindowStyle.SingleBorderWindow,
            WindowStartupLocation = ToStartupLocation(form, owner, modal)
        };

        if (form.MaximumSize.Width > 0)
        {
            window.MaxWidth = form.MaximumSize.Width;
        }

        if (form.MaximumSize.Height > 0)
        {
            window.MaxHeight = form.MaximumSize.Height;
        }

        if (owner != null && modal)
        {
            window.Owner = owner;
        }

        window.WindowState = ToWindowState(form.WindowState);

        bool closingWindowFromForm = false;
        bool closingFormFromWindow = false;

        EventHandler textChangedHandler = (_, _) =>
            InvokeOnDispatcher(window.Dispatcher, () => window.Title = GetWindowTitle(form));
        EventHandler? formDisposedHandler = null;
        Forms.FormClosedEventHandler? formClosedHandler = null;
        formClosedHandler = (_, _) =>
        {
            form.TextChanged -= textChangedHandler;
            form.Disposed -= formDisposedHandler;
            form.FormClosed -= formClosedHandler;
            lock (_gate)
            {
                _windows.Remove(form);
                _pendingDialogCompletions.Remove(form);
            }

            if (!closingFormFromWindow)
            {
                closingWindowFromForm = true;
                InvokeOnDispatcher(
                    window.Dispatcher,
                    () =>
                    {
                        if (window.IsVisible)
                        {
                            window.Close();
                        }
                    });
            }
        };
        formDisposedHandler = (_, _) =>
        {
            form.TextChanged -= textChangedHandler;
            form.Disposed -= formDisposedHandler;
            form.FormClosed -= formClosedHandler;
            lock (_gate)
            {
                _windows.Remove(form);
                _pendingDialogCompletions.Remove(form);
            }

            closingWindowFromForm = true;
            InvokeOnDispatcher(
                window.Dispatcher,
                () =>
                {
                    if (window.IsVisible)
                        window.Close();
                });
        };
        form.TextChanged += textChangedHandler;
        form.Disposed += formDisposedHandler;
        form.FormClosed += formClosedHandler;

        window.Closing += (_, e) =>
        {
            if (closingWindowFromForm)
            {
                return;
            }

            closingFormFromWindow = true;
            if (!form.Close(Forms.CloseReason.UserClosing))
            {
                e.Cancel = true;
                closingFormFromWindow = false;
            }
        };

        window.Closed += (_, _) =>
        {
            form.TextChanged -= textChangedHandler;
            form.Disposed -= formDisposedHandler;
            form.FormClosed -= formClosedHandler;
            lock (_gate)
            {
                _windows.Remove(form);
                _pendingDialogCompletions.Remove(form);
            }
        };

        lock (_gate)
        {
            _windows[form] = window;
        }

        window.Loaded += (_, _) => form.Show();
        return window;
    }

    private static void InvokeOnDispatcher(System.Windows.Threading.Dispatcher dispatcher, Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private sealed class WpfApplicationThreadContext :
        Forms.IWinFormsApplicationThreadContext,
        Forms.IWinFormsTimerHost
    {
        private readonly WpfPortableWinFormsApplicationHost _host;
        private readonly Forms.Form? _mainForm;
        private readonly Dispatcher _loopDispatcher;
        private readonly bool _usesPrimaryDispatcher;
        private readonly int _owningThreadId;
        private readonly BlockingCollection<Action>? _secondaryQueue;
        private DispatcherFrame? _dispatcherFrame;
        private WpfApplication? _ownedApplication;
        private Window? _window;
        private int _exitRequested;
        private int _disposed;

        public WpfApplicationThreadContext(
            WpfPortableWinFormsApplicationHost host,
            Forms.Form? mainForm,
            Dispatcher loopDispatcher,
            bool usesPrimaryDispatcher)
        {
            _host = host;
            _mainForm = mainForm;
            _loopDispatcher = loopDispatcher;
            _usesPrimaryDispatcher = usesPrimaryDispatcher;
            _owningThreadId = Environment.CurrentManagedThreadId;
            if (!usesPrimaryDispatcher)
                _secondaryQueue = new BlockingCollection<Action>();
        }

        public bool CheckAccess() => Environment.CurrentManagedThreadId == _owningThreadId;

        public void BeginInvoke(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ThrowIfUnavailable();
            if (_usesPrimaryDispatcher)
            {
                _loopDispatcher.BeginInvoke(DispatcherPriority.Normal, callback);
            }
            else
            {
                _secondaryQueue!.Add(callback);
            }
        }

        public void Invoke(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ThrowIfUnavailable();
            if (CheckAccess())
            {
                callback();
                return;
            }

            if (_usesPrimaryDispatcher)
            {
                _loopDispatcher.Invoke(callback, DispatcherPriority.Send);
                return;
            }

            using var completed = new ManualResetEventSlim(initialState: false);
            ExceptionDispatchInfo? exception = null;
            BeginInvoke(
                () =>
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception caught)
                    {
                        exception = ExceptionDispatchInfo.Capture(caught);
                    }
                    finally
                    {
                        completed.Set();
                    }
                });
            completed.Wait();
            exception?.Throw();
        }

        public IDisposable RegisterTimer(int intervalMilliseconds, Action callback)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(intervalMilliseconds, 1);
            ArgumentNullException.ThrowIfNull(callback);
            ThrowIfUnavailable();
            return _usesPrimaryDispatcher
                ? CreateTimerRegistration(_loopDispatcher, intervalMilliseconds, callback)
                : new SecondaryTimerRegistration(this, intervalMilliseconds, callback);
        }

        public void Run()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!CheckAccess())
                throw new InvalidOperationException("The portable WinForms application context must run on its owning thread.");

            if (_usesPrimaryDispatcher)
            {
                RunOnPrimaryDispatcher();
            }
            else
            {
                RunOnSecondaryDispatcher();
            }
        }

        public void ExitThread()
        {
            if (Interlocked.Exchange(ref _exitRequested, 1) != 0)
                return;

            if (_usesPrimaryDispatcher)
            {
                ExitPrimaryLoop();
            }
            else
            {
                ExitSecondaryLoop();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            ExitThread();
            CloseWindowOnPrimaryDispatcher();
            _secondaryQueue?.Dispose();
        }

        private void RunOnPrimaryDispatcher()
        {
            if (Volatile.Read(ref _exitRequested) != 0)
                return;

            WpfApplication? application = WpfApplication.Current;
            bool ownsApplication = application == null;
            application ??= new WpfApplication();
            if (ownsApplication)
                _ownedApplication = application;

            if (_mainForm == null)
            {
                if (ownsApplication)
                {
                    if (Volatile.Read(ref _exitRequested) == 0)
                        application.Run();
                }
                else
                {
                    PushDispatcherFrame();
                }

                return;
            }

            Window window = _host.CreateWindow(_mainForm, owner: null, modal: false);
            SetWindow(window);
            window.Closed += OnWindowClosed;
            if (Volatile.Read(ref _exitRequested) != 0)
            {
                window.Close();
                return;
            }

            if (ownsApplication)
            {
                application.Run(window);
            }
            else
            {
                window.ShowDialog();
            }
        }

        private void RunOnSecondaryDispatcher()
        {
            if (Volatile.Read(ref _exitRequested) != 0)
                return;

            TryBeginInvokePrimary(ShowSecondaryWindow);
            while (Volatile.Read(ref _exitRequested) == 0
                && _secondaryQueue!.TryTake(out Action? callback, Timeout.Infinite))
            {
                callback();
            }
            CloseWindowOnPrimaryDispatcher();
        }

        private void PushDispatcherFrame()
        {
            if (Volatile.Read(ref _exitRequested) != 0)
                return;

            var frame = new DispatcherFrame();
            _dispatcherFrame = frame;
            if (Volatile.Read(ref _exitRequested) != 0)
            {
                frame.Continue = false;
                return;
            }

            Dispatcher.PushFrame(frame);
            _dispatcherFrame = null;
        }

        private void ShowSecondaryWindow()
        {
            if (_mainForm == null
                || Volatile.Read(ref _exitRequested) != 0
                || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            Window window = _host.CreateWindow(_mainForm, owner: null, modal: false);
            SetWindow(window);
            window.Closed += OnWindowClosed;
            if (Volatile.Read(ref _exitRequested) != 0)
            {
                window.Close();
                return;
            }

            window.Show();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            ExitThread();
        }

        private void ExitPrimaryLoop()
        {
            WpfApplication? ownedApplication = _ownedApplication;
            if (ownedApplication != null)
            {
                InvokeOnDispatcher(ownedApplication.Dispatcher, ownedApplication.Shutdown);
                return;
            }

            Window? window = Volatile.Read(ref _window);
            if (window != null)
            {
                InvokeOnDispatcher(
                    window.Dispatcher,
                    () =>
                    {
                        if (window.IsVisible)
                            window.Close();
                    });
            }

            StopDispatcherFrame();
        }

        private void ExitSecondaryLoop()
        {
            _secondaryQueue!.CompleteAdding();
            CloseWindowOnPrimaryDispatcher();
        }

        private void StopDispatcherFrame()
        {
            DispatcherFrame? frame = Volatile.Read(ref _dispatcherFrame);
            if (frame == null)
                return;

            if (CheckAccess())
            {
                frame.Continue = false;
                return;
            }

            if (!_loopDispatcher.HasShutdownStarted && !_loopDispatcher.HasShutdownFinished)
            {
                _loopDispatcher.BeginInvoke(
                    DispatcherPriority.Send,
                    new Action(() => frame.Continue = false));
            }
        }

        private void CloseWindowOnPrimaryDispatcher()
        {
            Window? window = Volatile.Read(ref _window);
            if (window == null)
                return;

            TryBeginInvokePrimary(
                () =>
                {
                    if (window.IsVisible)
                        window.Close();
                });
        }

        private void SetWindow(Window window)
        {
            Volatile.Write(ref _window, window);
        }

        private void TryBeginInvokePrimary(Action callback)
        {
            if (_host._dispatcher.HasShutdownStarted || _host._dispatcher.HasShutdownFinished)
                return;

            try
            {
                _host._dispatcher.BeginInvoke(DispatcherPriority.Normal, callback);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ThrowIfUnavailable()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Volatile.Read(ref _exitRequested) != 0)
                throw new InvalidOperationException("The portable WinForms application context is exiting.");
            if (_usesPrimaryDispatcher
                && (_loopDispatcher.HasShutdownStarted || _loopDispatcher.HasShutdownFinished))
                throw new InvalidOperationException("The portable WinForms application dispatcher is shutting down.");
        }

        private sealed class SecondaryTimerRegistration : IDisposable
        {
            private readonly WpfApplicationThreadContext _context;
            private readonly System.Threading.Timer _timer;
            private Action? _callback;
            private int _disposed;

            public SecondaryTimerRegistration(
                WpfApplicationThreadContext context,
                int intervalMilliseconds,
                Action callback)
            {
                _context = context;
                _callback = callback;
                _timer = new System.Threading.Timer(
                    OnTimer,
                    null,
                    intervalMilliseconds,
                    intervalMilliseconds);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                Volatile.Write(ref _callback, null);
                _timer.Dispose();
            }

            private void OnTimer(object? state)
            {
                Action? callback = Volatile.Read(ref _callback);
                if (callback == null)
                    return;

                try
                {
                    _context.BeginInvoke(callback);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    private sealed class DispatcherTimerRegistration : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private Action? _callback;
        private int _disposed;

        public DispatcherTimerRegistration(Dispatcher dispatcher, int intervalMilliseconds, Action callback)
        {
            _callback = callback;
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(intervalMilliseconds)
            };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Volatile.Write(ref _callback, null);
            InvokeOnDispatcher(
                _timer.Dispatcher,
                () =>
                {
                    _timer.Stop();
                    _timer.Tick -= OnTick;
                });
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (Volatile.Read(ref _disposed) == 0)
                Volatile.Read(ref _callback)?.Invoke();
        }
    }

    private static string GetWindowTitle(Forms.Form form)
    {
        return string.IsNullOrWhiteSpace(form.Text) ? form.Name : form.Text;
    }

    private static double GetInitialDimension(int value, int fallback)
    {
        return value > 0 ? value : fallback;
    }

    private static WindowStartupLocation ToStartupLocation(Forms.Form form, Window? owner, bool modal)
    {
        return form.StartPosition switch
        {
            Forms.FormStartPosition.CenterScreen => WindowStartupLocation.CenterScreen,
            Forms.FormStartPosition.CenterParent when owner != null && modal => WindowStartupLocation.CenterOwner,
            _ => WindowStartupLocation.Manual
        };
    }

    private static WindowState ToWindowState(Forms.FormWindowState state)
    {
        return state switch
        {
            Forms.FormWindowState.Minimized => WindowState.Minimized,
            Forms.FormWindowState.Maximized => WindowState.Maximized,
            _ => WindowState.Normal
        };
    }

    private static ResizeMode ToResizeMode(Forms.Form form)
    {
        if (!form.ControlBox)
        {
            return ResizeMode.NoResize;
        }

        return form.FormBorderStyle switch
        {
            Forms.FormBorderStyle.FixedSingle or
            Forms.FormBorderStyle.Fixed3D or
            Forms.FormBorderStyle.FixedDialog or
            Forms.FormBorderStyle.FixedToolWindow => form.MinimizeBox || form.MaximizeBox ? ResizeMode.CanMinimize : ResizeMode.NoResize,
            _ => ResizeMode.CanResize
        };
    }
}
