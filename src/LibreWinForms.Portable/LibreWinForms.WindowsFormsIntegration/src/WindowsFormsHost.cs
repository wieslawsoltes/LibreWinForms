using System.ComponentModel;
using System.ComponentModel.Design;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Forms = System.Windows.Forms;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImage = System.Drawing.Image;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfSeparator = System.Windows.Controls.Separator;

namespace System.Windows.Forms.Integration;

[DefaultEvent("ChildChanged")]
[DesignerCategory("code")]
[ContentProperty("Child")]
public class WindowsFormsHost : FrameworkElement
{
    private enum DesignHandle
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left
    }

    private enum PortableDragEventKind
    {
        Enter,
        Over,
        Leave,
        Drop
    }

    private sealed class PortableDragSession
    {
        private System.Windows.IDataObject? _wpfData;

        public PortableDragSession(
            WindowsFormsHost sourceHost,
            Forms.IDataObject data,
            Forms.DragDropEffects allowedEffects,
            int sourceButtonMask)
        {
            SourceHost = sourceHost;
            Data = data;
            AllowedEffects = allowedEffects;
            SourceButtonMask = sourceButtonMask;
        }

        public Forms.DragDropEffects AllowedEffects { get; }

        public Forms.Control? CurrentTarget { get; set; }

        public WindowsFormsHost? CurrentTargetHost { get; set; }

        public Forms.DragDropEffects CurrentEffect { get; set; }

        public DependencyObject? CurrentWpfTarget { get; set; }

        public Forms.DragDropEffects CurrentWpfEffect { get; set; }

        public Point CurrentWpfTargetPoint { get; set; }

        public Forms.IDataObject Data { get; }

        public DispatcherFrame Frame { get; } = new();

        public bool IsCompleted { get; set; }

        public Point LastScreenPoint { get; set; }

        public Forms.DragDropEffects Result { get; set; }

        public int SourceButtonMask { get; }

        public WindowsFormsHost SourceHost { get; }

        public System.Windows.IDataObject WpfData => _wpfData ??= CreateWpfDragData(Data);
    }

    private sealed class PortableDropWindowState
    {
        public PortableDropWindowState(bool originalAllowDrop)
        {
            OriginalAllowDrop = originalAllowDrop;
        }

        public int HostCount { get; set; }

        public bool OriginalAllowDrop { get; }
    }

    private sealed class PortableWpfDataObject : System.Windows.IDataObject
    {
        private readonly Forms.IDataObject _source;
        private Dictionary<string, object?>? _data;

        public PortableWpfDataObject(Forms.IDataObject data)
        {
            _source = data;
        }

        public object? GetData(string format) => GetData(format, autoConvert: true);

        public object? GetData(string format, bool autoConvert) =>
            _data != null && _data.TryGetValue(format, out object? value)
                ? value
                : _source.GetData(format, autoConvert);

        public object? GetData(Type format) =>
            GetData(format.FullName ?? format.Name, autoConvert: true);

        public bool GetDataPresent(string format) => GetDataPresent(format, autoConvert: true);

        public bool GetDataPresent(string format, bool autoConvert) =>
            (_data?.ContainsKey(format) ?? false) || _source.GetDataPresent(format, autoConvert);

        public bool GetDataPresent(Type format) =>
            GetDataPresent(format.FullName ?? format.Name, autoConvert: true);

        public string[] GetFormats() => GetFormats(autoConvert: true);

        public string[] GetFormats(bool autoConvert)
        {
            string[] sourceFormats = _source.GetFormats(autoConvert);
            if (_data == null || _data.Count == 0)
            {
                return sourceFormats;
            }

            return sourceFormats
                .Concat(_data.Keys)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public void SetData(object? data)
        {
            ArgumentNullException.ThrowIfNull(data);
            SetData(data.GetType(), data);
        }

        public void SetData(string format, object? data) =>
            SetData(format, data, autoConvert: true);

        public void SetData(string format, object? data, bool autoConvert)
        {
            ArgumentException.ThrowIfNullOrEmpty(format);
            ArgumentNullException.ThrowIfNull(data);
            (_data ??= new Dictionary<string, object?>(StringComparer.Ordinal))[format] = data;
        }

        public void SetData(Type format, object? data)
        {
            ArgumentNullException.ThrowIfNull(format);
            SetData(format.FullName ?? format.Name, data, autoConvert: true);
        }
    }

    private const double DesignHandleSize = 7;
    private static readonly object s_registeredHostsGate = new();
    private static readonly List<WeakReference<WindowsFormsHost>> s_registeredHosts = new();
    private static readonly object s_dragSessionGate = new();
    private static PortableDragSession? s_dragSession;
    private static readonly object s_dropWindowStateGate = new();
    private static readonly ConditionalWeakTable<Window, PortableDropWindowState> s_dropWindowStates = new();
    private static readonly DesignHandle[] s_resizeHandles =
    {
        DesignHandle.TopLeft,
        DesignHandle.Top,
        DesignHandle.TopRight,
        DesignHandle.Right,
        DesignHandle.BottomRight,
        DesignHandle.Bottom,
        DesignHandle.BottomLeft,
        DesignHandle.Left
    };
    private static int s_interopEnabled;

    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize));

    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(SystemFonts.MessageFontStyle));

    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(SystemFonts.MessageFontWeight));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(SystemColors.ControlTextBrush));

    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(default(Thickness)));

    public static readonly DependencyProperty TabIndexProperty =
        DependencyProperty.Register(nameof(TabIndex), typeof(int), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(0));

    private Forms.Control? _child;
    private Forms.Control? _focusedControl;
    private Forms.Control? _capturedControl;
    private Forms.Control? _pressedControl;
    private Forms.ToolStripItem? _pressedToolStripItem;
    private Forms.MouseButtons _pressedButton;
    private Forms.Control? _externalDragTarget;
    private Forms.DragDropEffects _externalDragEffect;
    private Window? _externalDropWindow;
    private long _portableCustomPaintDispatchCount;
    private long _portableDesignerAdornerDispatchCount;
    private long _portableChildInvalidationDispatchCount;
    private long _portableCreateGraphicsDispatchCount;
    private long _portableOwnerDrawDispatchCount;
    private ISelectionService? _designSelectionService;
    private bool _designSelectionServiceLookupComplete;
    private WpfContextMenu? _activeContextMenu;
    private Forms.ToolStripDropDown? _activeToolStripDropDown;
    private readonly ConditionalWeakTable<DrawingImage, CachedImageSource> _imageSourceCache = new();
    private readonly ConditionalWeakTable<object, Forms.IDataObject> _dragDataCache = new();
    private readonly HashSet<Forms.Control> _invalidationTreeSubscriptions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Forms.Control, PortablePaintSurfacePool> _portablePaintSurfacePools = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Forms.Control, PortablePaintSurfacePool> _portableDesignerAdornerSurfacePools = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Forms.Control, PortablePaintSurfacePool> _createGraphicsSurfacePools = new(ReferenceEqualityComparer.Instance);
    private readonly List<PortablePaintSurfacePool> _pendingRetiredPaintSurfacePools = new();
    private readonly List<PortablePaintSurfacePool> _safeRetiredPaintSurfacePools = new();

    public event EventHandler<ChildChangedEventArgs>? ChildChanged;

    public event EventHandler<LayoutExceptionEventArgs>? LayoutError;

    static WindowsFormsHost()
    {
        Forms.ContextMenuStrip.ShowRequested += OnContextMenuStripShowRequested;
    }

    public WindowsFormsHost()
    {
        RegisterHost(this);
        AllowDrop = true;
        Loaded += OnHostLoaded;
        Unloaded += OnHostUnloaded;
    }

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public Forms.Control? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value))
            {
                return;
            }

            Forms.Control? previous = _child;
            CloseActiveContextMenu(Forms.ToolStripDropDownCloseReason.AppFocusChange);
            HandlePortableDragHostUnavailable();
            ClearExternalDragTarget(raiseLeave: true);
            if (_child != null)
            {
                UnsubscribeInvalidationTree(_child);
                ClearRemainingInvalidationSubscriptions();
                if (IsLoaded)
                {
                    NotifyPortableHostLifecycle(_child, attached: false);
                }
            }
            DetachDesignSelectionService();
            _designSelectionServiceLookupComplete = false;

            _child = value;
            _focusedControl = null;
            _capturedControl = null;
            _pressedControl = null;
            _pressedToolStripItem = null;
            _pressedButton = Forms.MouseButtons.None;
            Cursor = System.Windows.Input.Cursors.Arrow;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
            if (_child != null)
            {
                _child.CreateControl();
                SubscribeInvalidationTree(_child);
                EnsureDesignSelectionService();
                if (IsLoaded)
                {
                    NotifyPortableHostLifecycle(_child, attached: true);
                }
            }

            ChildChanged?.Invoke(this, new ChildChangedEventArgs(previous));
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    [Bindable(true)]
    [Category("Behavior")]
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    public PropertyMap PropertyMap { get; } = new();

    public long PortableCustomPaintDispatchCount => Interlocked.Read(ref _portableCustomPaintDispatchCount);

    public long PortableDesignerAdornerDispatchCount => Interlocked.Read(ref _portableDesignerAdornerDispatchCount);

    public long PortableChildInvalidationDispatchCount => Interlocked.Read(ref _portableChildInvalidationDispatchCount);

    public long PortableCreateGraphicsDispatchCount => Interlocked.Read(ref _portableCreateGraphicsDispatchCount);

    public int PortableCreateGraphicsSurfaceCount => _createGraphicsSurfacePools.Count;

    public int PortableInvalidationSubscriptionCount => _invalidationTreeSubscriptions.Count;

    public long PortableOwnerDrawDispatchCount => Interlocked.Read(ref _portableOwnerDrawDispatchCount);

    [Bindable(true)]
    [Category("Behavior")]
    public int TabIndex
    {
        get => (int)GetValue(TabIndexProperty);
        set => SetValue(TabIndexProperty, value);
    }

    public static void EnableWindowsFormsInterop()
    {
        if (Interlocked.Exchange(ref s_interopEnabled, 1) == 0)
        {
            RuntimeHelpers.RunModuleConstructor(typeof(Application).Module.ModuleHandle);
            RuntimeHelpers.RunModuleConstructor(typeof(Clipboard).Module.ModuleHandle);
            WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation();
            WpfPortableWindowActivation.TryRegisterPresentationCoreClipboardService();
            Forms.Application.RegisterPortableApplicationHost(WpfPortableWinFormsApplicationHost.Instance);
        }
    }

    internal static Forms.DragDropEffects DoPortableDragDrop(
        Forms.Control source,
        Forms.IDataObject data,
        Forms.DragDropEffects allowedEffects)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(data);

        WindowsFormsHost? sourceHost = null;
        foreach (WindowsFormsHost host in GetRegisteredHosts())
        {
            if (host._child != null && IsControlInTree(host._child, source))
            {
                sourceHost = host;
                break;
            }
        }

        if (sourceHost == null)
        {
            return Forms.DragDropEffects.None;
        }

        Dispatcher sourceDispatcher = sourceHost.Dispatcher;
        if (!sourceDispatcher.CheckAccess())
        {
            if (sourceDispatcher.HasShutdownStarted || sourceDispatcher.HasShutdownFinished)
            {
                return Forms.DragDropEffects.None;
            }

            Forms.DragDropEffects result = Forms.DragDropEffects.None;
            try
            {
                sourceDispatcher.Invoke(
                    () => result = DoPortableDragDropCore(
                        sourceHost,
                        data,
                        allowedEffects),
                    DispatcherPriority.Send);
            }
            catch (InvalidOperationException)
            {
                return Forms.DragDropEffects.None;
            }

            return result;
        }

        return DoPortableDragDropCore(sourceHost, data, allowedEffects);
    }

    private static Forms.DragDropEffects DoPortableDragDropCore(
        WindowsFormsHost sourceHost,
        Forms.IDataObject data,
        Forms.DragDropEffects allowedEffects)
    {
        if (!sourceHost.IsLoaded || !sourceHost.IsVisible)
        {
            return Forms.DragDropEffects.None;
        }

        int keyState = GetCurrentDragKeyState();
        const int mouseButtonMask = 1 | 2 | 16;
        int sourceButtonMask = keyState & mouseButtonMask;
        if (sourceButtonMask == 0)
        {
            // Portable input raises the exact WinForms mouse-down before a control starts
            // DoDragDrop, while WPF's process-wide Mouse state can lag by a dispatcher
            // turn. The source host's typed input state is authoritative for that gap.
            sourceButtonMask = GetPortablePressedButtonMask(sourceHost._pressedButton);
            keyState |= sourceButtonMask;
        }

        if (sourceButtonMask == 0)
        {
            return Forms.DragDropEffects.None;
        }

        var session = new PortableDragSession(sourceHost, data, allowedEffects, sourceButtonMask);
        lock (s_dragSessionGate)
        {
            if (s_dragSession != null)
            {
                return Forms.DragDropEffects.None;
            }

            // Publish the session before CaptureMouse. WPF may synchronously pump queued
            // pointer input while changing capture; those events already belong to this
            // drag and must not be lost before CaptureMouse returns.
            s_dragSession = session;
        }

        if (!sourceHost.CaptureMouse())
        {
            lock (s_dragSessionGate)
            {
                if (ReferenceEquals(s_dragSession, session))
                {
                    s_dragSession = null;
                }
            }

            return Forms.DragDropEffects.None;
        }

        try
        {
            if (!TryGetCurrentDragScreenPoint(sourceHost, out Point initialScreenPoint))
            {
                CancelPortableDragSession(session);
            }
            else
            {
                UpdatePortableDragTarget(session, initialScreenPoint, keyState, raiseOver: false);
            }

            if (!session.IsCompleted)
            {
                Dispatcher.PushFrame(session.Frame);
            }

            return session.Result;
        }
        finally
        {
            ClearPortableDragTargets(session);
            EndPortableDragSession(session);
            lock (s_dragSessionGate)
            {
                if (ReferenceEquals(s_dragSession, session))
                {
                    s_dragSession = null;
                }
            }

            if (sourceHost.IsMouseCaptured)
            {
                sourceHost.ReleaseMouseCapture();
            }
        }
    }

    internal static bool TryConvertControlPointToScreen(
        Forms.Control control,
        System.Drawing.Point point,
        out System.Drawing.Point screenPoint)
    {
        ArgumentNullException.ThrowIfNull(control);
        foreach (WindowsFormsHost host in GetRegisteredHosts())
        {
            if (host._child == null
                || !host.IsLoaded
                || !host.Dispatcher.CheckAccess()
                || !TryGetHostPoint(host._child, control, point, out Point hostPoint)
                || !TryGetScreenPoint(host, hostPoint, out Point wpfScreenPoint))
            {
                continue;
            }

            screenPoint = new System.Drawing.Point(
                ToWinFormsCoordinate(wpfScreenPoint.X),
                ToWinFormsCoordinate(wpfScreenPoint.Y));
            return true;
        }

        screenPoint = default;
        return false;
    }

    internal static bool TryConvertScreenPointToControl(
        Forms.Control control,
        System.Drawing.Point point,
        out System.Drawing.Point clientPoint)
    {
        ArgumentNullException.ThrowIfNull(control);
        foreach (WindowsFormsHost host in GetRegisteredHosts())
        {
            if (host._child == null
                || !host.IsLoaded
                || !host.Dispatcher.CheckAccess()
                || !IsControlInTree(host._child, control))
            {
                continue;
            }

            Point hostPoint;
            try
            {
                hostPoint = host.PointFromScreen(new Point(point.X, point.Y));
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (!TryConvertHostPointToControl(host._child, control, hostPoint, out Point controlPoint))
            {
                continue;
            }

            clientPoint = new System.Drawing.Point(
                ToWinFormsCoordinate(controlPoint.X),
                ToWinFormsCoordinate(controlPoint.Y));
            return true;
        }

        clientPoint = default;
        return false;
    }

    internal static bool TryCreateControlGraphics(
        Forms.Control control,
        out DrawingGraphics graphics)
    {
        ArgumentNullException.ThrowIfNull(control);
        foreach (WindowsFormsHost host in GetRegisteredHosts())
        {
            if (host._child == null
                || !host.Dispatcher.CheckAccess()
                || !IsControlInTree(host._child, control))
            {
                continue;
            }

            return host.TryCreateHostedControlGraphics(control, out graphics);
        }

        graphics = null!;
        return false;
    }

    internal static bool TryGetOwningWindow(
        Forms.Control control,
        out Window? window)
    {
        ArgumentNullException.ThrowIfNull(control);
        foreach (WindowsFormsHost host in GetRegisteredHosts())
        {
            if (host._child == null || !IsControlInTree(host._child, control))
            {
                continue;
            }

            window = Window.GetWindow(host);
            return window is not null;
        }

        window = null;
        return false;
    }

    private static bool TryConvertHostPointToControl(
        Forms.Control root,
        Forms.Control target,
        Point hostPoint,
        out Point controlPoint)
    {
        double x = hostPoint.X;
        double y = hostPoint.Y;
        for (Forms.Control? current = target; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                controlPoint = new Point(x, y);
                return true;
            }

            x -= current.Left;
            y -= current.Top;
            Point parentOffset = GetChildDisplayOffset(current.Parent);
            x -= parentOffset.X;
            y -= parentOffset.Y;
        }

        controlPoint = default;
        return false;
    }

    private static bool TryUpdatePortableDrag(
        WindowsFormsHost eventHost,
        System.Windows.Input.MouseEventArgs e)
    {
        PortableDragSession? session = GetPortableDragSession();
        if (session == null || session.IsCompleted)
        {
            return false;
        }

        if (!session.SourceHost.IsMouseCaptured)
        {
            CancelPortableDragSession(session);
            return true;
        }

        if (!TryGetDragScreenPoint(eventHost, e, out Point screenPoint))
        {
            CancelPortableDragSession(session);
            return true;
        }

        try
        {
            UpdatePortableDragTarget(session, screenPoint, GetCurrentDragKeyState(), raiseOver: true);
        }
        catch
        {
            AbortPortableDragSession(session);
            throw;
        }

        return true;
    }

    private static bool TryCompletePortableDrag(
        WindowsFormsHost eventHost,
        MouseButtonEventArgs e)
    {
        PortableDragSession? session = GetPortableDragSession();
        if (session == null || session.IsCompleted)
        {
            return false;
        }

        int releasedButton = e.ChangedButton switch
        {
            MouseButton.Left => 1,
            MouseButton.Right => 2,
            MouseButton.Middle => 16,
            _ => 0
        };
        if ((session.SourceButtonMask & releasedButton) == 0)
        {
            return true;
        }

        if (!TryGetDragScreenPoint(eventHost, e, out Point screenPoint))
        {
            CancelPortableDragSession(session);
            return true;
        }

        CompletePortableDragSession(session, screenPoint, GetCurrentDragKeyState());
        return true;
    }

    private static bool TryCancelPortableDrag(WindowsFormsHost eventHost)
    {
        PortableDragSession? session = GetPortableDragSession();
        if (session == null
            || session.IsCompleted
            || !ReferenceEquals(session.SourceHost, eventHost))
        {
            return false;
        }

        CancelPortableDragSession(session);
        return true;
    }

    private static PortableDragSession? GetPortableDragSession()
    {
        lock (s_dragSessionGate)
        {
            return s_dragSession;
        }
    }

    private static void UpdatePortableDragTarget(
        PortableDragSession session,
        Point screenPoint,
        int keyState,
        bool raiseOver)
    {
        session.LastScreenPoint = screenPoint;
        TryFindPortableDropTarget(
            screenPoint,
            out WindowsFormsHost? targetHost,
            out Forms.Control? target);

        if (target != null)
        {
            LeaveCurrentWpfTarget(session, screenPoint, keyState);
            UpdatePortableFormsDragTarget(
                session,
                targetHost,
                target,
                screenPoint,
                keyState,
                raiseOver);
            return;
        }

        LeaveCurrentFormsTarget(session);
        _ = TryFindPortableWpfDropTarget(
            session.SourceHost,
            screenPoint,
            out DependencyObject? wpfTarget,
            out Point wpfTargetPoint);
        UpdatePortableWpfDragTarget(
            session,
            wpfTarget,
            wpfTargetPoint,
            screenPoint,
            keyState,
            raiseOver);
    }

    private static void UpdatePortableFormsDragTarget(
        PortableDragSession session,
        WindowsFormsHost? targetHost,
        Forms.Control target,
        Point screenPoint,
        int keyState,
        bool raiseOver)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!ReferenceEquals(session.CurrentTarget, target))
        {
            LeaveCurrentFormsTarget(session);
            session.CurrentTarget = target;
            session.CurrentTargetHost = targetHost;
            session.CurrentEffect = Forms.DragDropEffects.None;

            Forms.DragDropEffects initialEffect = SelectDefaultDragEffect(
                session.AllowedEffects,
                keyState);
            var enterArgs = CreateFormsDragEventArgs(
                session.Data,
                keyState,
                screenPoint,
                session.AllowedEffects,
                initialEffect);
            target.RaiseDragEnter(enterArgs);
            session.CurrentEffect = NormalizeDragEffect(
                enterArgs.Effect,
                session.AllowedEffects);

            return;
        }

        if (!raiseOver)
        {
            return;
        }

        Forms.DragDropEffects overEffect = session.CurrentEffect != Forms.DragDropEffects.None
            ? session.CurrentEffect
            : SelectDefaultDragEffect(session.AllowedEffects, keyState);
        var overArgs = CreateFormsDragEventArgs(
            session.Data,
            keyState,
            screenPoint,
            session.AllowedEffects,
            overEffect);
        target.RaiseDragOver(overArgs);
        session.CurrentEffect = NormalizeDragEffect(overArgs.Effect, session.AllowedEffects);
    }

    private static void UpdatePortableWpfDragTarget(
        PortableDragSession session,
        DependencyObject? target,
        Point targetPoint,
        Point screenPoint,
        int keyState,
        bool raiseOver)
    {
        if (!ReferenceEquals(session.CurrentWpfTarget, target))
        {
            LeaveCurrentWpfTarget(session, screenPoint, keyState);
            session.CurrentWpfTarget = target;
            session.CurrentWpfEffect = Forms.DragDropEffects.None;
            session.CurrentWpfTargetPoint = targetPoint;

            if (target != null)
            {
                Forms.DragDropEffects initialEffect = SelectDefaultDragEffect(
                    session.AllowedEffects,
                    keyState);
                session.CurrentWpfEffect = RaisePortableWpfDragEvent(
                    session,
                    target,
                    System.Windows.DragDrop.DragEnterEvent,
                    targetPoint,
                    keyState,
                    initialEffect);
            }

            return;
        }

        if (!raiseOver || target == null)
        {
            return;
        }

        session.CurrentWpfTargetPoint = targetPoint;
        Forms.DragDropEffects overEffect = session.CurrentWpfEffect != Forms.DragDropEffects.None
            ? session.CurrentWpfEffect
            : SelectDefaultDragEffect(session.AllowedEffects, keyState);
        session.CurrentWpfEffect = RaisePortableWpfDragEvent(
            session,
            target,
            System.Windows.DragDrop.DragOverEvent,
            targetPoint,
            keyState,
            overEffect);
    }

    private static void CompletePortableDragSession(
        PortableDragSession session,
        Point screenPoint,
        int keyState)
    {
        try
        {
            UpdatePortableDragTarget(session, screenPoint, keyState, raiseOver: false);
            Forms.Control? target = session.CurrentTarget;
            if (target != null)
            {
                Forms.DragDropEffects dropEffect = session.CurrentEffect != Forms.DragDropEffects.None
                    ? session.CurrentEffect
                    : SelectDefaultDragEffect(session.AllowedEffects, keyState);
                var dropArgs = CreateFormsDragEventArgs(
                    session.Data,
                    keyState,
                    screenPoint,
                    session.AllowedEffects,
                    dropEffect);
                target.RaiseDragDrop(dropArgs);
                session.Result = NormalizeDragEffect(dropArgs.Effect, session.AllowedEffects);
            }
            else if (session.CurrentWpfTarget is DependencyObject wpfTarget)
            {
                if (!TryGetPortableWpfTargetPoint(
                        wpfTarget,
                        screenPoint,
                        out Point targetPoint))
                {
                    LeaveCurrentWpfTarget(session, screenPoint, keyState);
                    session.Result = Forms.DragDropEffects.None;
                    return;
                }

                Forms.DragDropEffects dropEffect = session.CurrentWpfEffect != Forms.DragDropEffects.None
                    ? session.CurrentWpfEffect
                    : SelectDefaultDragEffect(session.AllowedEffects, keyState);
                session.CurrentWpfTargetPoint = targetPoint;
                session.Result = RaisePortableWpfDragEvent(
                    session,
                    wpfTarget,
                    System.Windows.DragDrop.DropEvent,
                    targetPoint,
                    keyState,
                    dropEffect);
            }
        }
        finally
        {
            ClearPortableDragTargets(session);
            EndPortableDragSession(session);
        }
    }

    private static void CancelPortableDragSession(PortableDragSession session)
    {
        try
        {
            LeaveCurrentFormsTarget(session);
            LeaveCurrentWpfTarget(
                session,
                session.LastScreenPoint,
                GetCurrentDragKeyState());
        }
        finally
        {
            session.Result = Forms.DragDropEffects.None;
            ClearPortableDragTargets(session);
            EndPortableDragSession(session);
        }
    }

    private static void AbortPortableDragSession(PortableDragSession session)
    {
        session.Result = Forms.DragDropEffects.None;
        ClearPortableDragTargets(session);
        EndPortableDragSession(session);
    }

    private static void ClearPortableDragTargets(PortableDragSession session)
    {
        session.CurrentTarget = null;
        session.CurrentTargetHost = null;
        session.CurrentEffect = Forms.DragDropEffects.None;
        session.CurrentWpfTarget = null;
        session.CurrentWpfEffect = Forms.DragDropEffects.None;
        session.CurrentWpfTargetPoint = default;
    }

    private static void LeaveCurrentFormsTarget(PortableDragSession session)
    {
        Forms.Control? target = session.CurrentTarget;
        session.CurrentTarget = null;
        session.CurrentTargetHost = null;
        session.CurrentEffect = Forms.DragDropEffects.None;
        target?.RaiseDragLeave(EventArgs.Empty);
    }

    private static void LeaveCurrentWpfTarget(
        PortableDragSession session,
        Point screenPoint,
        int keyState)
    {
        DependencyObject? target = session.CurrentWpfTarget;
        Point targetPoint = session.CurrentWpfTargetPoint;
        Forms.DragDropEffects targetEffect = session.CurrentWpfEffect;
        session.CurrentWpfTarget = null;
        session.CurrentWpfEffect = Forms.DragDropEffects.None;
        session.CurrentWpfTargetPoint = default;

        if (target != null)
        {
            if (TryGetPortableWpfTargetPoint(target, screenPoint, out Point convertedPoint))
            {
                targetPoint = convertedPoint;
            }

            _ = RaisePortableWpfDragEvent(
                session,
                target,
                System.Windows.DragDrop.DragLeaveEvent,
                targetPoint,
                keyState,
                targetEffect);
        }
    }

    private static Forms.DragDropEffects RaisePortableWpfDragEvent(
        PortableDragSession session,
        DependencyObject target,
        RoutedEvent routedEvent,
        Point targetPoint,
        int keyState,
        Forms.DragDropEffects acceptedEffect)
    {
        System.Windows.DragDropEffects result = System.Windows.DragDrop.ProcessPortableDragDrop(
            target,
            routedEvent,
            session.WpfData,
            ToWpfDragDropKeyStates(keyState),
            ToWpfDragDropEffects(session.AllowedEffects),
            ToWpfDragDropEffects(acceptedEffect),
            targetPoint);
        return NormalizeDragEffect(
            ToFormsDragDropEffects(result),
            session.AllowedEffects);
    }

    private static void EndPortableDragSession(PortableDragSession session)
    {
        if (session.IsCompleted)
        {
            return;
        }

        session.IsCompleted = true;
        session.Frame.Continue = false;
    }

    private static bool TryFindPortableDropTarget(
        Point screenPoint,
        out WindowsFormsHost? targetHost,
        out Forms.Control? target)
    {
        foreach (WindowsFormsHost host in GetRegisteredHosts())
        {
            if (host._child == null || !host.IsLoaded || !host.IsVisible)
            {
                continue;
            }

            Point hostPoint;
            try
            {
                hostPoint = host.PointFromScreen(screenPoint);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (!IsPointInsideHost(host, hostPoint))
            {
                continue;
            }

            Forms.Control? candidate = FindControlAt(host._child, hostPoint, out _);
            while (candidate != null && (!candidate.AllowDrop || !candidate.Enabled))
            {
                candidate = candidate.Parent;
            }

            if (candidate != null)
            {
                targetHost = host;
                target = candidate;
                return true;
            }
        }

        targetHost = null;
        target = null;
        return false;
    }

    private static bool TryFindPortableWpfDropTarget(
        WindowsFormsHost sourceHost,
        Point screenPoint,
        out DependencyObject? target,
        out Point targetPoint)
    {
        Window? sourceWindow = Window.GetWindow(sourceHost);
        if (sourceWindow != null
            && TryFindPortableWpfDropTarget(
                sourceWindow,
                screenPoint,
                out target,
                out targetPoint))
        {
            return true;
        }

        System.Windows.Application? application = System.Windows.Application.Current;
        if (application != null)
        {
            foreach (Window window in application.Windows)
            {
                if (ReferenceEquals(window, sourceWindow))
                {
                    continue;
                }

                if (TryFindPortableWpfDropTarget(
                    window,
                    screenPoint,
                    out target,
                    out targetPoint))
                {
                    return true;
                }
            }
        }

        target = null;
        targetPoint = default;
        return false;
    }

    private static bool TryFindPortableWpfDropTarget(
        Window window,
        Point screenPoint,
        out DependencyObject? target,
        out Point targetPoint)
    {
        if (!window.IsLoaded
            || !window.IsVisible
            || !window.Dispatcher.CheckAccess())
        {
            target = null;
            targetPoint = default;
            return false;
        }

        Point windowPoint;
        try
        {
            windowPoint = window.PointFromScreen(screenPoint);
        }
        catch (InvalidOperationException)
        {
            target = null;
            targetPoint = default;
            return false;
        }

        if (windowPoint.X < 0
            || windowPoint.Y < 0
            || windowPoint.X >= window.ActualWidth
            || windowPoint.Y >= window.ActualHeight)
        {
            target = null;
            targetPoint = default;
            return false;
        }

        DependencyObject? candidate = window.InputHitTest(windowPoint) as DependencyObject;
        while (candidate != null)
        {
            if (IsPortableWpfDropTarget(candidate)
                && TryGetPortableWpfTargetPoint(candidate, screenPoint, out targetPoint))
            {
                target = candidate;
                return true;
            }

            candidate = GetPortableWpfParent(candidate);
        }

        target = null;
        targetPoint = default;
        return false;
    }

    private static bool IsPortableWpfDropTarget(DependencyObject target)
    {
        if (target is WindowsFormsHost)
        {
            return false;
        }

        if (target is Window window)
        {
            lock (s_dropWindowStateGate)
            {
                if (s_dropWindowStates.TryGetValue(window, out PortableDropWindowState? state)
                    && !state.OriginalAllowDrop)
                {
                    return false;
                }
            }
        }

        return target switch
        {
            UIElement uiElement => uiElement.AllowDrop && uiElement.IsEnabled,
            ContentElement contentElement => contentElement.AllowDrop && contentElement.IsEnabled,
            UIElement3D uiElement3D => uiElement3D.AllowDrop && uiElement3D.IsEnabled,
            _ => false
        };
    }

    private static DependencyObject? GetPortableWpfParent(DependencyObject target)
    {
        if (target is ContentElement contentElement)
        {
            DependencyObject? contentParent = ContentOperations.GetParent(contentElement);
            if (contentParent != null)
            {
                return contentParent;
            }

            if (contentElement is FrameworkContentElement frameworkContentElement
                && frameworkContentElement.Parent != null)
            {
                return frameworkContentElement.Parent;
            }
        }

        if (target is Visual || target is System.Windows.Media.Media3D.Visual3D)
        {
            DependencyObject? visualParent = VisualTreeHelper.GetParent(target);
            if (visualParent != null)
            {
                return visualParent;
            }
        }

        if (target is FrameworkElement frameworkElement && frameworkElement.Parent != null)
        {
            return frameworkElement.Parent;
        }

        return LogicalTreeHelper.GetParent(target);
    }

    private static bool TryGetPortableWpfTargetPoint(
        DependencyObject target,
        Point screenPoint,
        out Point targetPoint)
    {
        if (target is UIElement uiElement)
        {
            try
            {
                targetPoint = uiElement.PointFromScreen(screenPoint);
                return true;
            }
            catch (InvalidOperationException)
            {
                targetPoint = default;
                return false;
            }
        }

        if (target is IInputElement inputElement)
        {
            targetPoint = Mouse.GetPosition(inputElement);
            return true;
        }

        targetPoint = default;
        return false;
    }

    private static bool TryGetDragScreenPoint(
        WindowsFormsHost eventHost,
        System.Windows.Input.MouseEventArgs e,
        out Point screenPoint)
    {
        Window? window = Window.GetWindow(eventHost);
        if (window != null)
        {
            return TryGetScreenPoint(window, e.GetPosition(window), out screenPoint);
        }

        return TryGetScreenPoint(eventHost, e.GetPosition(eventHost), out screenPoint);
    }

    private static bool TryGetCurrentDragScreenPoint(
        WindowsFormsHost sourceHost,
        out Point screenPoint)
    {
        Window? window = Window.GetWindow(sourceHost);
        if (window != null)
        {
            return TryGetScreenPoint(window, Mouse.GetPosition(window), out screenPoint);
        }

        return TryGetScreenPoint(sourceHost, Mouse.GetPosition(sourceHost), out screenPoint);
    }

    private static bool TryGetScreenPoint(
        Visual visual,
        Point visualPoint,
        out Point screenPoint)
    {
        try
        {
            screenPoint = visual.PointToScreen(visualPoint);
            return true;
        }
        catch (InvalidOperationException)
        {
            screenPoint = default;
            return false;
        }
    }

    private static bool IsPointInsideHost(WindowsFormsHost host, Point hostPoint)
    {
        return hostPoint.X >= 0
            && hostPoint.Y >= 0
            && hostPoint.X < host.ActualWidth
            && hostPoint.Y < host.ActualHeight;
    }

    private static int GetCurrentDragKeyState()
    {
        int keyState = 0;
        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            keyState |= 1;
        }

        if (Mouse.RightButton == MouseButtonState.Pressed)
        {
            keyState |= 2;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            keyState |= 4;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            keyState |= 8;
        }

        if (Mouse.MiddleButton == MouseButtonState.Pressed)
        {
            keyState |= 16;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            keyState |= 32;
        }

        return keyState;
    }

    private static int GetPortablePressedButtonMask(Forms.MouseButtons pressedButton)
    {
        int result = 0;
        if ((pressedButton & Forms.MouseButtons.Left) != 0)
        {
            result |= 1;
        }

        if ((pressedButton & Forms.MouseButtons.Right) != 0)
        {
            result |= 2;
        }

        if ((pressedButton & Forms.MouseButtons.Middle) != 0)
        {
            result |= 16;
        }

        return result;
    }

    private static System.Windows.DragDropKeyStates ToWpfDragDropKeyStates(int keyState)
    {
        System.Windows.DragDropKeyStates result = System.Windows.DragDropKeyStates.None;
        if ((keyState & 1) != 0)
        {
            result |= System.Windows.DragDropKeyStates.LeftMouseButton;
        }

        if ((keyState & 2) != 0)
        {
            result |= System.Windows.DragDropKeyStates.RightMouseButton;
        }

        if ((keyState & 4) != 0)
        {
            result |= System.Windows.DragDropKeyStates.ShiftKey;
        }

        if ((keyState & 8) != 0)
        {
            result |= System.Windows.DragDropKeyStates.ControlKey;
        }

        if ((keyState & 16) != 0)
        {
            result |= System.Windows.DragDropKeyStates.MiddleMouseButton;
        }

        if ((keyState & 32) != 0)
        {
            result |= System.Windows.DragDropKeyStates.AltKey;
        }

        return result;
    }

    private static Forms.DragDropEffects SelectDefaultDragEffect(
        Forms.DragDropEffects allowedEffects,
        int keyState)
    {
        bool shift = (keyState & 4) != 0;
        bool control = (keyState & 8) != 0;
        bool alt = (keyState & 32) != 0;

        if ((alt || (shift && control))
            && (allowedEffects & Forms.DragDropEffects.Link) != 0)
        {
            return Forms.DragDropEffects.Link;
        }

        if (control && (allowedEffects & Forms.DragDropEffects.Copy) != 0)
        {
            return Forms.DragDropEffects.Copy;
        }

        if (shift && (allowedEffects & Forms.DragDropEffects.Move) != 0)
        {
            return Forms.DragDropEffects.Move;
        }

        if ((allowedEffects & Forms.DragDropEffects.Move) != 0)
        {
            return Forms.DragDropEffects.Move;
        }

        if ((allowedEffects & Forms.DragDropEffects.Copy) != 0)
        {
            return Forms.DragDropEffects.Copy;
        }

        return (allowedEffects & Forms.DragDropEffects.Link) != 0
            ? Forms.DragDropEffects.Link
            : Forms.DragDropEffects.None;
    }

    private static Forms.DragDropEffects NormalizeDragEffect(
        Forms.DragDropEffects effect,
        Forms.DragDropEffects allowedEffects)
    {
        const Forms.DragDropEffects validEffects =
            Forms.DragDropEffects.Copy |
            Forms.DragDropEffects.Move |
            Forms.DragDropEffects.Link |
            Forms.DragDropEffects.Scroll;
        return effect & allowedEffects & validEffects;
    }

    private static Forms.DragEventArgs CreateFormsDragEventArgs(
        Forms.IDataObject data,
        int keyState,
        Point screenPoint,
        Forms.DragDropEffects allowedEffects,
        Forms.DragDropEffects effect)
    {
        return new Forms.DragEventArgs(
            data,
            keyState,
            ToWinFormsCoordinate(screenPoint.X),
            ToWinFormsCoordinate(screenPoint.Y),
            allowedEffects,
            effect);
    }

    private void ProcessExternalDragEvent(
        System.Windows.DragEventArgs e,
        PortableDragEventKind eventKind)
    {
        if (eventKind == PortableDragEventKind.Leave)
        {
            bool hadTarget = _externalDragTarget != null;
            ClearExternalDragTarget(raiseLeave: true);
            if (hadTarget)
            {
                e.Effects = System.Windows.DragDropEffects.None;
                e.Handled = true;
            }

            return;
        }

        if (_child == null)
        {
            return;
        }

        Point hostPoint = e.GetPosition(this);
        if (!IsPointInsideHost(this, hostPoint))
        {
            ClearExternalDragTarget(raiseLeave: true);
            return;
        }

        if (!TryGetScreenPoint(this, hostPoint, out Point screenPoint))
        {
            ClearExternalDragTarget(raiseLeave: true);
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }

        Forms.Control? target = FindControlAt(_child, hostPoint, out _);
        while (target != null && (!target.AllowDrop || !target.Enabled))
        {
            target = target.Parent;
        }

        Forms.IDataObject data = GetFormsDragData(e.Data);
        int keyState = ToFormsDragKeyState(e.KeyStates);
        Forms.DragDropEffects allowedEffects = ToFormsDragDropEffects(e.AllowedEffects);
        Forms.DragDropEffects suggestedEffect = NormalizeDragEffect(
            ToFormsDragDropEffects(e.Effects),
            allowedEffects);
        if (suggestedEffect == Forms.DragDropEffects.None)
        {
            suggestedEffect = SelectDefaultDragEffect(allowedEffects, keyState);
        }

        bool targetChanged = !ReferenceEquals(_externalDragTarget, target);
        if (targetChanged)
        {
            _externalDragTarget?.RaiseDragLeave(EventArgs.Empty);
            _externalDragTarget = target;
            _externalDragEffect = Forms.DragDropEffects.None;
        }

        if (target != null)
        {
            if (targetChanged || eventKind == PortableDragEventKind.Enter)
            {
                var enterArgs = CreateFormsDragEventArgs(
                    data,
                    keyState,
                    screenPoint,
                    allowedEffects,
                    suggestedEffect);
                target.RaiseDragEnter(enterArgs);
                _externalDragEffect = NormalizeDragEffect(enterArgs.Effect, allowedEffects);
            }
            else if (eventKind == PortableDragEventKind.Over)
            {
                var overArgs = CreateFormsDragEventArgs(
                    data,
                    keyState,
                    screenPoint,
                    allowedEffects,
                    _externalDragEffect != Forms.DragDropEffects.None
                        ? _externalDragEffect
                        : suggestedEffect);
                target.RaiseDragOver(overArgs);
                _externalDragEffect = NormalizeDragEffect(overArgs.Effect, allowedEffects);
            }

            if (eventKind == PortableDragEventKind.Drop)
            {
                var dropArgs = CreateFormsDragEventArgs(
                    data,
                    keyState,
                    screenPoint,
                    allowedEffects,
                    _externalDragEffect != Forms.DragDropEffects.None
                        ? _externalDragEffect
                        : suggestedEffect);
                target.RaiseDragDrop(dropArgs);
                _externalDragEffect = NormalizeDragEffect(dropArgs.Effect, allowedEffects);
            }
        }

        e.Effects = ToWpfDragDropEffects(_externalDragEffect);
        e.Handled = true;

        if (eventKind == PortableDragEventKind.Drop)
        {
            ClearExternalDragTarget(raiseLeave: false);
        }
    }

    private void ClearExternalDragTarget(bool raiseLeave)
    {
        if (raiseLeave)
        {
            _externalDragTarget?.RaiseDragLeave(EventArgs.Empty);
        }

        _externalDragTarget = null;
        _externalDragEffect = Forms.DragDropEffects.None;
    }

    private Forms.IDataObject GetFormsDragData(System.Windows.IDataObject data)
    {
        return _dragDataCache.GetValue(
            data,
            static key => CreateFormsDragData((System.Windows.IDataObject)key));
    }

    private static System.Windows.IDataObject CreateWpfDragData(Forms.IDataObject data)
    {
        if (data is System.Windows.IDataObject wpfData)
        {
            return wpfData;
        }

        string[] formats = data.GetFormats(autoConvert: false);
        if (formats.Length == 1
            && data.GetDataPresent(formats[0], autoConvert: false)
            && data.GetData(formats[0], autoConvert: false) is System.Windows.IDataObject wrappedWpfData)
        {
            return wrappedWpfData;
        }

        return new PortableWpfDataObject(data);
    }

    private static Forms.DataObject CreateFormsDragData(System.Windows.IDataObject data)
    {
        var result = new Forms.DataObject();
        foreach (string format in data.GetFormats(autoConvert: false))
        {
            if (!data.GetDataPresent(format, autoConvert: false))
            {
                continue;
            }

            object? value = data.GetData(format, autoConvert: false);
            if (format == System.Windows.DataFormats.FileDrop
                && value is System.Collections.Specialized.StringCollection paths)
            {
                var fileNames = new string[paths.Count];
                paths.CopyTo(fileNames, 0);
                value = fileNames;
            }

            result.SetData(format, value);
        }

        return result;
    }

    private static int ToFormsDragKeyState(System.Windows.DragDropKeyStates keyStates)
    {
        int result = 0;
        if ((keyStates & System.Windows.DragDropKeyStates.LeftMouseButton) != 0)
        {
            result |= 1;
        }

        if ((keyStates & System.Windows.DragDropKeyStates.RightMouseButton) != 0)
        {
            result |= 2;
        }

        if ((keyStates & System.Windows.DragDropKeyStates.ShiftKey) != 0)
        {
            result |= 4;
        }

        if ((keyStates & System.Windows.DragDropKeyStates.ControlKey) != 0)
        {
            result |= 8;
        }

        if ((keyStates & System.Windows.DragDropKeyStates.MiddleMouseButton) != 0)
        {
            result |= 16;
        }

        if ((keyStates & System.Windows.DragDropKeyStates.AltKey) != 0)
        {
            result |= 32;
        }

        return result;
    }

    private static Forms.DragDropEffects ToFormsDragDropEffects(
        System.Windows.DragDropEffects effects)
    {
        Forms.DragDropEffects result = Forms.DragDropEffects.None;
        if ((effects & System.Windows.DragDropEffects.Copy) != 0)
        {
            result |= Forms.DragDropEffects.Copy;
        }

        if ((effects & System.Windows.DragDropEffects.Move) != 0)
        {
            result |= Forms.DragDropEffects.Move;
        }

        if ((effects & System.Windows.DragDropEffects.Link) != 0)
        {
            result |= Forms.DragDropEffects.Link;
        }

        if ((effects & System.Windows.DragDropEffects.Scroll) != 0)
        {
            result |= Forms.DragDropEffects.Scroll;
        }

        return result;
    }

    private static System.Windows.DragDropEffects ToWpfDragDropEffects(
        Forms.DragDropEffects effects)
    {
        System.Windows.DragDropEffects result = System.Windows.DragDropEffects.None;
        if ((effects & Forms.DragDropEffects.Copy) != 0)
        {
            result |= System.Windows.DragDropEffects.Copy;
        }

        if ((effects & Forms.DragDropEffects.Move) != 0)
        {
            result |= System.Windows.DragDropEffects.Move;
        }

        if ((effects & Forms.DragDropEffects.Link) != 0)
        {
            result |= System.Windows.DragDropEffects.Link;
        }

        if ((effects & Forms.DragDropEffects.Scroll) != 0)
        {
            result |= System.Windows.DragDropEffects.Scroll;
        }

        return result;
    }

    public virtual bool TabInto(System.Windows.Input.TraversalRequest request)
    {
        Focus();
        return true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_child == null)
        {
            return default;
        }

        double width = double.IsInfinity(availableSize.Width) ? _child.Size.Width : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? _child.Size.Height : availableSize.Height;
        if (width <= 0)
        {
            width = 120;
        }

        if (height <= 0)
        {
            height = 80;
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_child != null)
        {
            LayoutControlTree(_child, new Rect(0, 0, finalSize.Width, finalSize.Height));
        }

        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        AdvanceRetiredPortablePaintSurfaces();
        if (_child == null)
        {
            return;
        }

        RenderControl(drawingContext, _child, new Rect(0, 0, ActualWidth, ActualHeight));
        RenderDesignAdorners(drawingContext, _child);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        bool hadPressedControl = _pressedControl != null;
        _pressedControl = null;
        _pressedToolStripItem = null;
        _pressedButton = Forms.MouseButtons.None;
        if (hadPressedControl)
        {
            InvalidateVisual();
        }

        if (_child == null)
        {
            return;
        }

        Point hostPoint = e.GetPosition(this);
        Forms.Control? target = FindDesignAdornerTarget(hostPoint, out Point localPoint, out _)
            ?? FindControlAt(_child, hostPoint, out localPoint);
        if (target == null)
        {
            return;
        }
        target = ResolveDesignInputTarget(target, hostPoint, ref localPoint);
        Forms.DataGridView? editingDataGridView = FindOwningDataGridView(_focusedControl);
        if (editingDataGridView is not null && !IsControlInTree(editingDataGridView, target))
        {
            editingDataGridView.EndEdit();
        }

        Forms.MouseButtons pressedButton = MapMouseButton(e.ChangedButton);
        Focus();
        bool designMode = target.Site?.DesignMode == true;
        bool focusAccepted = target.Focus();
        if (focusAccepted)
        {
            _focusedControl = target;
        }

        if (!designMode && !focusAccepted)
        {
            e.Handled = true;
            return;
        }

        if (!designMode && target.CanSelect)
        {
            _pressedControl = target;
            _pressedButton = pressedButton;
            InvalidateVisual();
        }

        var mouseEventArgs = new Forms.MouseEventArgs(MapMouseButton(e.ChangedButton), e.ClickCount, ToWinFormsCoordinate(localPoint.X), ToWinFormsCoordinate(localPoint.Y), 0);
        target.RaiseMouseDown(mouseEventArgs);

        if (target.Capture)
        {
            _capturedControl = target;
            CaptureMouse();
        }

        if (designMode)
        {
            e.Handled = true;
            return;
        }

        ApplyDefaultSelection(target, localPoint, pressedButton);
        if (target is Forms.DataGridView { EditingControl: { Focused: true } editingControl })
        {
            _focusedControl = editingControl;
        }

        if (pressedButton == Forms.MouseButtons.Left && target is Forms.ToolStrip toolStrip)
        {
            _pressedToolStripItem = SelectToolStripItemAt(toolStrip, localPoint);
        }

        if (e.ChangedButton == MouseButton.Left
            && target is Forms.ComboBox comboBox
            && TryShowComboBoxDropDown(comboBox))
        {
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right && TryShowContextMenu(target, localPoint))
        {
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (TryCompletePortableDrag(this, e))
        {
            e.Handled = true;
            return;
        }

        if (_child == null)
        {
            bool hadPressedControl = _pressedControl != null;
            _pressedControl = null;
            _pressedToolStripItem = null;
            _pressedButton = Forms.MouseButtons.None;
            if (hadPressedControl)
            {
                InvalidateVisual();
            }

            return;
        }

        Point hostPoint = e.GetPosition(this);
        DesignHandle hoverHandle = DesignHandle.None;
        Forms.Control? designTarget = null;
        Point designLocalPoint = default;
        if (_capturedControl == null && MapMouseButtons(e) == Forms.MouseButtons.None)
        {
            designTarget = FindDesignAdornerTarget(hostPoint, out designLocalPoint, out hoverHandle);
        }

        Forms.Control? target = designTarget;
        Point localPoint = designLocalPoint;
        if (target == null)
        {
            target = FindPointerTarget(hostPoint, out localPoint);
        }
        if (target == null)
        {
            bool hadPressedControl = _pressedControl != null;
            _pressedControl = null;
            _pressedToolStripItem = null;
            _pressedButton = Forms.MouseButtons.None;
            Cursor = System.Windows.Input.Cursors.Arrow;
            if (hadPressedControl)
            {
                InvalidateVisual();
            }

            return;
        }
        target = ResolveDesignInputTarget(target, hostPoint, ref localPoint);
        Cursor = hoverHandle != DesignHandle.None
            ? GetDesignHandleCursor(hoverHandle)
            : GetPortableCursor(target.Cursor);

        Forms.MouseButtons releasedButton = MapMouseButton(e.ChangedButton);
        bool matchingPress = ReferenceEquals(_pressedControl, target)
            && _pressedButton == releasedButton;
        Forms.ToolStripItem? pressedToolStripItem = _pressedToolStripItem;
        _pressedControl = null;
        _pressedToolStripItem = null;
        _pressedButton = Forms.MouseButtons.None;
        InvalidateVisual();

        var mouseEventArgs = new Forms.MouseEventArgs(releasedButton, e.ClickCount, ToWinFormsCoordinate(localPoint.X), ToWinFormsCoordinate(localPoint.Y), 0);
        bool designMode = target.Site?.DesignMode == true;
        if (designMode)
        {
            try
            {
                target.RaiseMouseUp(mouseEventArgs);
            }
            finally
            {
                ReleaseDesignerCapture(target);
            }

            e.Handled = true;
            return;
        }

        target.RaiseMouseUp(mouseEventArgs);
        if (matchingPress && target.CanSelect)
        {
            target.RaiseMouseClick(mouseEventArgs);
        }

        if (matchingPress
            && releasedButton == Forms.MouseButtons.Left
            && target is Forms.ToolStrip toolStrip
            && TryActivateToolStripItem(toolStrip, localPoint, pressedToolStripItem))
        {
            e.Handled = true;
            return;
        }

        if (matchingPress
            && e.ChangedButton == MouseButton.Left
            && ApplyDefaultHeaderClick(target, localPoint))
        {
            e.Handled = true;
            return;
        }

        if (matchingPress && e.ClickCount >= 2)
        {
            target.RaiseMouseDoubleClick(mouseEventArgs);
            ApplyDefaultActivation(target, localPoint);
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (TryUpdatePortableDrag(this, e))
        {
            e.Handled = true;
            return;
        }

        if (_child == null)
        {
            Cursor = System.Windows.Input.Cursors.Arrow;
            return;
        }

        Point hostPoint = e.GetPosition(this);
        DesignHandle hoverHandle = DesignHandle.None;
        Forms.Control? designTarget = null;
        Point designLocalPoint = default;
        if (_capturedControl == null && MapMouseButtons(e) == Forms.MouseButtons.None)
        {
            designTarget = FindDesignAdornerTarget(hostPoint, out designLocalPoint, out hoverHandle);
        }

        Forms.Control? target = designTarget;
        Point localPoint = designLocalPoint;
        if (target == null)
        {
            target = FindPointerTarget(hostPoint, out localPoint);
        }
        if (target == null)
        {
            Cursor = System.Windows.Input.Cursors.Arrow;
            return;
        }
        target = ResolveDesignInputTarget(target, hostPoint, ref localPoint);
        Cursor = hoverHandle != DesignHandle.None
            ? GetDesignHandleCursor(hoverHandle)
            : GetPortableCursor(target.Cursor);

        var mouseEventArgs = new Forms.MouseEventArgs(
            MapMouseButtons(e),
            0,
            ToWinFormsCoordinate(localPoint.X),
            ToWinFormsCoordinate(localPoint.Y),
            0);
        target.RaiseMouseMove(mouseEventArgs);
        if (target.Site?.DesignMode == true)
        {
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = System.Windows.Input.Cursors.Arrow;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_child == null)
        {
            return;
        }

        Point hostPoint = e.GetPosition(this);
        Forms.Control? target = FindPointerTarget(hostPoint, out Point localPoint);
        if (target == null)
        {
            return;
        }

        target = ResolveDesignInputTarget(target, hostPoint, ref localPoint);
        target.RaiseMouseWheel(new Forms.MouseEventArgs(
            MapMouseButtons(e),
            0,
            ToWinFormsCoordinate(localPoint.X),
            ToWinFormsCoordinate(localPoint.Y),
            e.Delta));
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _ = TryCancelPortableDrag(this);
    }

    protected override void OnDragEnter(System.Windows.DragEventArgs e)
    {
        base.OnDragEnter(e);
        ProcessExternalDragEvent(e, PortableDragEventKind.Enter);
    }

    protected override void OnDragOver(System.Windows.DragEventArgs e)
    {
        base.OnDragOver(e);
        ProcessExternalDragEvent(e, PortableDragEventKind.Over);
    }

    protected override void OnDragLeave(System.Windows.DragEventArgs e)
    {
        base.OnDragLeave(e);
        ProcessExternalDragEvent(e, PortableDragEventKind.Leave);
    }

    protected override void OnDrop(System.Windows.DragEventArgs e)
    {
        base.OnDrop(e);
        ProcessExternalDragEvent(e, PortableDragEventKind.Drop);
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape && TryCancelPortableDrag(this))
        {
            e.Handled = true;
            return;
        }

        Forms.Control? target = GetFocusedControl();
        if (target == null)
        {
            return;
        }

        System.Windows.Input.Key key = e.Key == System.Windows.Input.Key.System
            ? e.SystemKey
            : e.Key;
        Forms.Keys keyData = MapKey(key, Keyboard.Modifiers);
        if (keyData == Forms.Keys.None)
        {
            return;
        }

        Forms.Keys previousModifiers = Forms.Control.ModifierKeys;
        Forms.Control.ModifierKeys = keyData & Forms.Keys.Modifiers;
        try
        {
            e.Handled = ProcessHostedKeyDown(target, keyData);
        }
        finally
        {
            Forms.Control.ModifierKeys = previousModifiers;
        }
    }

    private static bool ProcessHostedKeyDown(Forms.Control target, Forms.Keys keyData)
    {
        Forms.Message message = CreateHostedKeyMessage(target, keyData, keyDown: true);
        if (Forms.Application.FilterMessage(ref message)
            || target.PreProcessMessage(ref message))
        {
            return true;
        }

        var keyEventArgs = new Forms.KeyEventArgs(keyData);
        Forms.Form? form = target.FindForm();
        if (form?.KeyPreview == true && !ReferenceEquals(form, target))
        {
            form.RaiseKeyDown(keyEventArgs);
            if (!form.Visible || form.IsDisposed)
            {
                return true;
            }
        }

        if (!keyEventArgs.Handled && !keyEventArgs.SuppressKeyPress)
        {
            target.RaiseKeyDown(keyEventArgs);
        }

        if (!keyEventArgs.Handled && (keyEventArgs.KeyCode == Forms.Keys.Enter || keyEventArgs.KeyCode == Forms.Keys.Return))
        {
            var keyPressEventArgs = new Forms.KeyPressEventArgs('\r');
            if (form?.KeyPreview == true && !ReferenceEquals(form, target))
            {
                form.RaiseKeyPress(keyPressEventArgs);
                if (!form.Visible || form.IsDisposed)
                {
                    return true;
                }
            }

            if (!keyPressEventArgs.Handled)
            {
                target.RaiseKeyPress(keyPressEventArgs);
            }
            if (target is Forms.ListView listView && listView.SelectedItems.Count > 0)
            {
                listView.RaiseItemActivate();
                keyPressEventArgs.Handled = true;
            }

            keyEventArgs.Handled = keyPressEventArgs.Handled;
        }

        if (!keyEventArgs.Handled
            && !keyEventArgs.SuppressKeyPress
            && form is Forms.IWinFormsDialogKeyProcessor dialogKeyProcessor
            && dialogKeyProcessor.TryProcessDialogKey(keyData, target))
        {
            keyEventArgs.Handled = true;
            keyEventArgs.SuppressKeyPress = true;
        }

        return keyEventArgs.Handled || keyEventArgs.SuppressKeyPress;
    }

    protected override void OnKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyUp(e);
        Forms.Control? target = GetFocusedControl();
        if (target == null)
        {
            return;
        }

        System.Windows.Input.Key key = e.Key == System.Windows.Input.Key.System
            ? e.SystemKey
            : e.Key;
        Forms.Keys keyData = MapKey(key, Keyboard.Modifiers);
        if (keyData == Forms.Keys.None)
        {
            return;
        }

        Forms.Keys previousModifiers = Forms.Control.ModifierKeys;
        Forms.Control.ModifierKeys = keyData & Forms.Keys.Modifiers;
        try
        {
            Forms.Message message = CreateHostedKeyMessage(target, keyData, keyDown: false);
            if (Forms.Application.FilterMessage(ref message))
            {
                e.Handled = true;
                return;
            }

            var keyEventArgs = new Forms.KeyEventArgs(keyData);
            Forms.Form? form = target.FindForm();
            if (form?.KeyPreview == true && !ReferenceEquals(form, target))
            {
                form.RaiseKeyUp(keyEventArgs);
                if (!form.Visible || form.IsDisposed)
                {
                    e.Handled = true;
                    return;
                }
            }

            if (!keyEventArgs.Handled && !keyEventArgs.SuppressKeyPress)
            {
                target.RaiseKeyUp(keyEventArgs);
            }
            e.Handled = keyEventArgs.Handled || keyEventArgs.SuppressKeyPress;
        }
        finally
        {
            Forms.Control.ModifierKeys = previousModifiers;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        Forms.Control? target = GetFocusedControl();
        if (target == null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        bool handled = false;
        Forms.Form? form = target.FindForm();
        foreach (char ch in e.Text)
        {
            var keyPressEventArgs = new Forms.KeyPressEventArgs(ch);
            if (form?.KeyPreview == true && !ReferenceEquals(form, target))
            {
                form.RaiseKeyPress(keyPressEventArgs);
                if (!form.Visible || form.IsDisposed)
                {
                    handled = true;
                    break;
                }
            }

            if (!keyPressEventArgs.Handled)
            {
                target.RaiseKeyPress(keyPressEventArgs);
            }
            handled |= keyPressEventArgs.Handled;
            if (!keyPressEventArgs.Handled && target is Forms.TextBoxBase textBoxBase)
            {
                textBoxBase.ApplyTextInput(ch.ToString(CultureInfo.InvariantCulture));
                handled = true;
            }
        }

        e.Handled = handled;
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new WindowsFormsHostAutomationPeer(this);
    }

    private static bool TryShowContextMenu(Forms.Control target, Point localPoint)
    {
        Forms.ContextMenuStrip? contextMenuStrip = FindContextMenuStrip(target);
        if (contextMenuStrip == null)
        {
            return false;
        }

        contextMenuStrip.Show(target, new System.Drawing.Point(ToWinFormsCoordinate(localPoint.X), ToWinFormsCoordinate(localPoint.Y)));
        return contextMenuStrip.Visible;
    }

    private bool ShowContextMenuStrip(Forms.ContextMenuStrip contextMenuStrip, Point hostPoint)
    {
        var contextMenu = new WpfContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = hostPoint.X,
            VerticalOffset = hostPoint.Y
        };

        CloseActiveContextMenu(Forms.ToolStripDropDownCloseReason.AppClicked);
        contextMenuStrip.Visible = true;
        foreach (Forms.ToolStripItem item in contextMenuStrip.Items)
        {
            if (CreateContextMenuItem(contextMenuStrip, item) is object menuItem)
            {
                contextMenu.Items.Add(menuItem);
            }
        }

        bool closingFromWpf = false;
        bool closingFromStrip = false;
        Forms.ToolStripDropDownClosedEventHandler stripClosed = null!;
        stripClosed = delegate
        {
            if (!closingFromWpf && contextMenu.IsOpen)
            {
                closingFromStrip = true;
                contextMenu.IsOpen = false;
                closingFromStrip = false;
            }

            if (ReferenceEquals(_activeContextMenu, contextMenu))
            {
                _activeContextMenu = null;
                _activeToolStripDropDown = null;
            }

            contextMenuStrip.Closed -= stripClosed;
        };
        contextMenuStrip.Closed += stripClosed;

        contextMenu.Closed += delegate
        {
            contextMenuStrip.Closed -= stripClosed;
            if (!closingFromStrip)
            {
                closingFromWpf = true;
                contextMenuStrip.Close(Forms.ToolStripDropDownCloseReason.AppClicked);
                closingFromWpf = false;
            }

            if (ReferenceEquals(_activeContextMenu, contextMenu))
            {
                _activeContextMenu = null;
                _activeToolStripDropDown = null;
            }
        };

        _activeContextMenu = contextMenu;
        _activeToolStripDropDown = contextMenuStrip;
        contextMenu.IsOpen = true;
        return true;
    }

    private static void OnContextMenuStripShowRequested(object? sender, Forms.ContextMenuStripShowRequestedEventArgs e)
    {
        List<WindowsFormsHost> hosts = GetRegisteredHosts();
        foreach (WindowsFormsHost host in hosts)
        {
            if (host.TryShowContextMenuStripForControl(e.ContextMenuStrip, e.Control, e.Position))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private static List<WindowsFormsHost> GetRegisteredHosts()
    {
        var hosts = new List<WindowsFormsHost>();
        lock (s_registeredHostsGate)
        {
            for (int i = s_registeredHosts.Count - 1; i >= 0; i--)
            {
                if (s_registeredHosts[i].TryGetTarget(out WindowsFormsHost? host))
                {
                    hosts.Add(host);
                }
                else
                {
                    s_registeredHosts.RemoveAt(i);
                }
            }
        }

        return hosts;
    }

    private static void RegisterHost(WindowsFormsHost host)
    {
        lock (s_registeredHostsGate)
        {
            for (int i = s_registeredHosts.Count - 1; i >= 0; i--)
            {
                if (!s_registeredHosts[i].TryGetTarget(out WindowsFormsHost? existing))
                {
                    s_registeredHosts.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(existing, host))
                {
                    return;
                }
            }

            s_registeredHosts.Add(new WeakReference<WindowsFormsHost>(host));
        }
    }

    private bool TryShowContextMenuStripForControl(Forms.ContextMenuStrip contextMenuStrip, Forms.Control control, System.Drawing.Point position)
    {
        if (_child == null || !TryGetHostPoint(_child, control, position, out Point hostPoint))
        {
            return false;
        }

        return ShowContextMenuStrip(contextMenuStrip, hostPoint);
    }

    private bool TryShowComboBoxDropDown(Forms.ComboBox comboBox)
    {
        if (_child == null
            || comboBox.Items.Count == 0
            || !TryGetHostPoint(_child, comboBox, new System.Drawing.Point(0, comboBox.Height), out Point hostPoint))
        {
            return false;
        }

        var contextMenu = new WpfContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = hostPoint.X,
            VerticalOffset = hostPoint.Y,
            MinWidth = Math.Max(0, comboBox.Width)
        };

        CloseActiveContextMenu(Forms.ToolStripDropDownCloseReason.AppClicked);
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            int itemIndex = i;
            var menuItem = new WpfMenuItem
            {
                Header = comboBox.Items[i]?.ToString() ?? string.Empty,
                IsCheckable = true,
                IsChecked = itemIndex == comboBox.SelectedIndex
            };
            menuItem.Click += delegate
            {
                comboBox.SelectedIndex = itemIndex;
                InvalidateVisual();
                contextMenu.IsOpen = false;
            };
            contextMenu.Items.Add(menuItem);
        }

        contextMenu.Closed += delegate
        {
            comboBox.DroppedDown = false;
            if (ReferenceEquals(_activeContextMenu, contextMenu))
            {
                _activeContextMenu = null;
                _activeToolStripDropDown = null;
            }
        };

        _activeContextMenu = contextMenu;
        _activeToolStripDropDown = null;
        comboBox.DroppedDown = true;
        contextMenu.IsOpen = true;
        return true;
    }

    private bool TryShowToolStripComboBoxDropDown(
        Forms.ToolStrip toolStrip,
        Forms.ToolStripComboBox comboBox,
        Rect itemBounds)
    {
        Forms.ComboBox embeddedComboBox = comboBox.ComboBox;
        if (_child == null
            || embeddedComboBox.Items.Count == 0
            || !TryGetHostPoint(
                _child,
                toolStrip,
                new System.Drawing.Point(
                    ToWinFormsCoordinate(itemBounds.X),
                    ToWinFormsCoordinate(itemBounds.Bottom)),
                out Point hostPoint))
        {
            return false;
        }

        var contextMenu = new WpfContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = hostPoint.X,
            VerticalOffset = hostPoint.Y,
            MinWidth = Math.Max(0, itemBounds.Width)
        };

        CloseActiveContextMenu(Forms.ToolStripDropDownCloseReason.AppClicked);
        for (int i = 0; i < embeddedComboBox.Items.Count; i++)
        {
            int itemIndex = i;
            var menuItem = new WpfMenuItem
            {
                Header = embeddedComboBox.Items[i]?.ToString() ?? string.Empty,
                IsCheckable = true,
                IsChecked = itemIndex == comboBox.SelectedIndex
            };
            menuItem.Click += delegate
            {
                comboBox.SelectedIndex = itemIndex;
                InvalidateVisual();
                contextMenu.IsOpen = false;
            };
            contextMenu.Items.Add(menuItem);
        }

        contextMenu.Closed += delegate
        {
            embeddedComboBox.DroppedDown = false;
            if (ReferenceEquals(_activeContextMenu, contextMenu))
            {
                _activeContextMenu = null;
                _activeToolStripDropDown = null;
            }
        };

        _activeContextMenu = contextMenu;
        _activeToolStripDropDown = null;
        embeddedComboBox.DroppedDown = true;
        contextMenu.IsOpen = true;
        return true;
    }

    private Forms.ToolStripItem? SelectToolStripItemAt(Forms.ToolStrip toolStrip, Point localPoint)
    {
        Forms.ToolStripItem? selectedItem = TryGetToolStripItemAt(toolStrip, localPoint, out _);
        foreach (Forms.ToolStripItem item in toolStrip.Items)
        {
            item.Selected = ReferenceEquals(item, selectedItem);
        }

        if (selectedItem != null)
        {
            InvalidateVisual();
        }

        return selectedItem;
    }

    private bool TryShowToolStripDropDown(
        Forms.ToolStrip toolStrip,
        Forms.ToolStripDropDown dropDown,
        Forms.ToolStripItemCollection items,
        Rect itemBounds,
        Action showDropDown)
    {
        if (_child == null
            || items.Count == 0
            || !TryGetHostPoint(
                _child,
                toolStrip,
                new System.Drawing.Point(
                    ToWinFormsCoordinate(itemBounds.X),
                    ToWinFormsCoordinate(itemBounds.Bottom)),
                out Point hostPoint))
        {
            return false;
        }

        var contextMenu = new WpfContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = hostPoint.X,
            VerticalOffset = hostPoint.Y,
            MinWidth = Math.Max(0, itemBounds.Width)
        };

        CloseActiveContextMenu(Forms.ToolStripDropDownCloseReason.AppClicked);
        foreach (Forms.ToolStripItem item in items)
        {
            if (CreateContextMenuItem(dropDown, item) is object menuItem)
            {
                contextMenu.Items.Add(menuItem);
            }
        }

        bool closingFromWpf = false;
        bool closingFromStrip = false;
        Forms.ToolStripDropDownClosedEventHandler stripClosed = null!;
        stripClosed = delegate
        {
            if (!closingFromWpf && contextMenu.IsOpen)
            {
                closingFromStrip = true;
                contextMenu.IsOpen = false;
                closingFromStrip = false;
            }

            if (ReferenceEquals(_activeContextMenu, contextMenu))
            {
                _activeContextMenu = null;
                _activeToolStripDropDown = null;
            }

            dropDown.Closed -= stripClosed;
        };
        dropDown.Closed += stripClosed;

        contextMenu.Closed += delegate
        {
            dropDown.Closed -= stripClosed;
            if (!closingFromStrip)
            {
                closingFromWpf = true;
                dropDown.Close(Forms.ToolStripDropDownCloseReason.AppClicked);
                closingFromWpf = false;
            }

            if (ReferenceEquals(_activeContextMenu, contextMenu))
            {
                _activeContextMenu = null;
                _activeToolStripDropDown = null;
            }
        };

        _activeContextMenu = contextMenu;
        _activeToolStripDropDown = dropDown;
        showDropDown();
        contextMenu.IsOpen = true;
        return true;
    }

    private bool TryActivateToolStripItem(
        Forms.ToolStrip toolStrip,
        Point localPoint,
        Forms.ToolStripItem? pressedItem)
    {
        Forms.ToolStripItem? releasedItem = TryGetToolStripItemAt(toolStrip, localPoint, out Rect itemBounds);
        if (releasedItem == null
            || !ReferenceEquals(releasedItem, pressedItem)
            || !releasedItem.Enabled)
        {
            return false;
        }

        if (releasedItem is Forms.ToolStripComboBox comboBox)
        {
            return TryShowToolStripComboBoxDropDown(toolStrip, comboBox, itemBounds);
        }

        if (releasedItem is Forms.ToolStripMenuItem { DropDownItems.Count: > 0 } menuItem)
        {
            return TryShowToolStripDropDown(
                toolStrip,
                menuItem.DropDown,
                menuItem.DropDownItems,
                itemBounds,
                menuItem.ShowDropDown);
        }

        if (releasedItem is Forms.ToolStripDropDownButton { DropDownItems.Count: > 0 } dropDownButton)
        {
            return TryShowToolStripDropDown(
                toolStrip,
                dropDownButton.DropDown,
                dropDownButton.DropDownItems,
                itemBounds,
                dropDownButton.ShowDropDown);
        }

        if (releasedItem is Forms.ToolStripControlHost { Control: Forms.NumericUpDown numericUpDown })
        {
            if (localPoint.X >= itemBounds.Right - 18)
            {
                decimal delta = localPoint.Y < itemBounds.Top + (itemBounds.Height / 2)
                    ? numericUpDown.Increment
                    : -numericUpDown.Increment;
                numericUpDown.Value = Math.Clamp(
                    numericUpDown.Value + delta,
                    numericUpDown.Minimum,
                    numericUpDown.Maximum);
                InvalidateVisual();
            }
            else
            {
                numericUpDown.Focus();
            }

            return true;
        }

        releasedItem.PerformClick();
        return true;
    }

    private Forms.ToolStripItem? TryGetToolStripItemAt(
        Forms.ToolStrip toolStrip,
        Point localPoint,
        out Rect itemBounds)
    {
        Rect stripBounds = new(0, 0, Math.Max(0, toolStrip.Width), Math.Max(0, toolStrip.Height));
        int itemIndex = 0;
        double x = stripBounds.X + 4;
        while (TryGetNextMainToolStripItem(toolStrip, stripBounds, ref itemIndex, ref x, out Forms.ToolStripItem item, out Rect bounds))
        {
            if (bounds.Contains(localPoint))
            {
                itemBounds = bounds;
                return item;
            }
        }

        itemBounds = Rect.Empty;
        return null;
    }

    private static bool TryGetHostPoint(Forms.Control root, Forms.Control source, System.Drawing.Point sourcePoint, out Point hostPoint)
    {
        double x = sourcePoint.X;
        double y = sourcePoint.Y;
        for (Forms.Control? current = source; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                hostPoint = new Point(x, y);
                return true;
            }

            x += current.Left;
            y += current.Top;
            Point parentOffset = GetChildDisplayOffset(current.Parent);
            x += parentOffset.X;
            y += parentOffset.Y;
        }

        hostPoint = default;
        return false;
    }

    private void CloseActiveContextMenu(Forms.ToolStripDropDownCloseReason closeReason)
    {
        Forms.ToolStripDropDown? activeStrip = _activeToolStripDropDown;
        if (activeStrip?.Visible == true)
        {
            activeStrip.Close(closeReason);
        }
        else if (_activeContextMenu != null)
        {
            _activeContextMenu.IsOpen = false;
        }

        _activeContextMenu = null;
        _activeToolStripDropDown = null;
    }

    private Forms.Control? GetFocusedControl()
    {
        if (_child == null)
        {
            return null;
        }

        if (_child is Forms.ContainerControl container)
        {
            Forms.Control? activeControl = container.ActiveControl;
            if (activeControl != null
                && activeControl.Focused
                && IsControlInTree(_child, activeControl))
            {
                _focusedControl = activeControl;
                return activeControl;
            }

            return _child;
        }

        if (_focusedControl != null
            && _focusedControl.Focused
            && IsControlInTree(_child, _focusedControl))
        {
            return _focusedControl;
        }

        Forms.Control? focusedControl = FindFocusedControl(_child);
        if (focusedControl != null)
        {
            _focusedControl = focusedControl;
            return focusedControl;
        }

        return _child;
    }

    private static Forms.Control? FindFocusedControl(Forms.Control root)
    {
        if (root.Focused)
        {
            return root;
        }

        foreach (Forms.Control child in root.Controls)
        {
            Forms.Control? focusedControl = FindFocusedControl(child);
            if (focusedControl != null)
            {
                return focusedControl;
            }
        }

        return null;
    }

    private static bool IsControlInTree(Forms.Control root, Forms.Control target)
    {
        if (ReferenceEquals(root, target))
        {
            return true;
        }

        foreach (Forms.Control child in root.Controls)
        {
            if (IsControlInTree(child, target))
            {
                return true;
            }
        }

        return false;
    }

    private static Forms.ContextMenuStrip? FindContextMenuStrip(Forms.Control control)
    {
        for (Forms.Control? current = control; current != null; current = current.Parent)
        {
            if (current.ContextMenuStrip != null)
            {
                return current.ContextMenuStrip;
            }
        }

        return null;
    }

    private static object? CreateContextMenuItem(Forms.ToolStripDropDown owner, Forms.ToolStripItem item)
    {
        if (!item.Visible || !item.Available)
        {
            return null;
        }

        if (item is Forms.ToolStripSeparator)
        {
            return new WpfSeparator();
        }

        string text = string.IsNullOrEmpty(item.Text) ? item.Name : item.Text;
        var menuItem = new WpfMenuItem
        {
            Header = text,
            IsEnabled = item.Enabled
        };

        if (item is Forms.ToolStripMenuItem toolStripMenuItem)
        {
            menuItem.IsCheckable = toolStripMenuItem.CheckOnClick;
            menuItem.IsChecked = toolStripMenuItem.Checked;
            foreach (Forms.ToolStripItem child in toolStripMenuItem.DropDownItems)
            {
                if (CreateContextMenuItem(owner, child) is object childItem)
                {
                    menuItem.Items.Add(childItem);
                }
            }

            if (menuItem.Items.Count > 0)
            {
                menuItem.SubmenuOpened += delegate
                {
                    toolStripMenuItem.ShowDropDown();
                };
                menuItem.SubmenuClosed += delegate
                {
                    toolStripMenuItem.DropDown.Close(Forms.ToolStripDropDownCloseReason.AppClicked);
                };
            }
        }

        if (menuItem.Items.Count == 0)
        {
            menuItem.Click += delegate
            {
                if (item is Forms.ToolStripMenuItem clickedMenuItem && clickedMenuItem.CheckOnClick)
                {
                    clickedMenuItem.Checked = !clickedMenuItem.Checked;
                    menuItem.IsChecked = clickedMenuItem.Checked;
                }

                item.PerformClick();
                owner.Close(Forms.ToolStripDropDownCloseReason.ItemClicked);
            };
        }

        return menuItem;
    }

    private static void ApplyDefaultSelection(Forms.Control target, Point localPoint, Forms.MouseButtons pressedButton)
    {
        if (target is Forms.DataGridView dataGridView && pressedButton == Forms.MouseButtons.Left)
        {
            int x = ToWinFormsCoordinate(localPoint.X);
            int y = ToWinFormsCoordinate(localPoint.Y);
            Forms.DataGridView.HitTestInfo hit = dataGridView.HitTest(x, y);
            if (hit.Type == Forms.DataGridViewHitTestType.Cell)
            {
                Forms.DataGridViewCell cell = dataGridView.Rows[hit.RowIndex].Cells[hit.ColumnIndex];
                bool wasCurrentCell = ReferenceEquals(dataGridView.CurrentCell, cell);
                dataGridView.CurrentCell = cell;
                if (dataGridView.EditMode == Forms.DataGridViewEditMode.EditOnEnter
                    || (wasCurrentCell && dataGridView.EditMode != Forms.DataGridViewEditMode.EditProgrammatically))
                {
                    dataGridView.BeginEdit(selectAll: true);
                }
            }
        }
        else if (target is Forms.CheckedListBox checkedListBox)
        {
            int x = ToWinFormsCoordinate(localPoint.X);
            int y = ToWinFormsCoordinate(localPoint.Y);
            int index = checkedListBox.IndexFromPoint(new System.Drawing.Point(x, y));
            if (index >= 0 && index < checkedListBox.Items.Count)
            {
                checkedListBox.SelectedIndex = index;
                checkedListBox.TryToggleItemAt(x, y);
            }
        }
        else if (target is Forms.ComboBox)
        {
        }
        else if (target is Forms.ListBox listBox)
        {
            int index = listBox.IndexFromPoint(new System.Drawing.Point(ToWinFormsCoordinate(localPoint.X), ToWinFormsCoordinate(localPoint.Y)));
            if (index >= 0 && index < listBox.Items.Count)
            {
                listBox.SelectedIndex = index;
            }
        }
        else if (target is Forms.TreeView treeView)
        {
            int x = ToWinFormsCoordinate(localPoint.X);
            int y = ToWinFormsCoordinate(localPoint.Y);
            if (pressedButton == Forms.MouseButtons.Left && treeView.TryToggleExpansionAt(x, y))
            {
                return;
            }

            Forms.TreeNode? node = treeView.GetNodeAt(x, y);
            if (node != null)
            {
                treeView.SelectedNode = node;
            }
        }
        else if (target is Forms.ListView listView)
        {
            int x = ToWinFormsCoordinate(localPoint.X);
            int y = ToWinFormsCoordinate(localPoint.Y);
            if (listView.TryToggleItemCheckAt(x, y))
            {
                return;
            }

            Forms.ListViewItem? item = listView.GetItemAt(x, y);
            if (item != null)
            {
                if (!listView.MultiSelect
                    || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                {
                    listView.SelectedItems.Clear();
                }

                item.Selected = true;
                listView.Invalidate();
            }
        }
    }

    private static Forms.DataGridView? FindOwningDataGridView(Forms.Control? control)
    {
        for (Forms.Control? current = control; current is not null; current = current.Parent)
        {
            if (current is Forms.DataGridView dataGridView)
            {
                return dataGridView;
            }
        }

        return null;
    }

    private static void ApplyDefaultActivation(Forms.Control target, Point localPoint)
    {
        if (target is Forms.ListView listView)
        {
            listView.TryActivateItemAt(
                ToWinFormsCoordinate(localPoint.X),
                ToWinFormsCoordinate(localPoint.Y));
        }
    }

    private static bool ApplyDefaultHeaderClick(Forms.Control target, Point localPoint)
    {
        return target is Forms.ListView listView
            && listView.TryRaiseColumnClickAt(
                ToWinFormsCoordinate(localPoint.X),
                ToWinFormsCoordinate(localPoint.Y));
    }

    private void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        AttachExternalDropWindow();
        _designSelectionServiceLookupComplete = false;
        EnsureDesignSelectionService();
        if (_child != null)
        {
            NotifyPortableHostLifecycle(_child, attached: true);
        }
    }

    private void OnHostUnloaded(object sender, RoutedEventArgs e)
    {
        if (_child != null)
        {
            NotifyPortableHostLifecycle(_child, attached: false);
        }
        CloseActiveContextMenu(Forms.ToolStripDropDownCloseReason.AppFocusChange);
        DetachExternalDropWindow();
        ClearExternalDragTarget(raiseLeave: true);
        HandlePortableDragHostUnavailable();
        DetachDesignSelectionService();
        DisposePortablePaintSurfaces();
    }

    private void HandlePortableDragHostUnavailable()
    {
        PortableDragSession? session = GetPortableDragSession();
        if (session != null && !session.IsCompleted)
        {
            if (ReferenceEquals(session.SourceHost, this))
            {
                CancelPortableDragSession(session);
            }
            else if (ReferenceEquals(session.CurrentTargetHost, this))
            {
                session.CurrentTarget?.RaiseDragLeave(EventArgs.Empty);
                session.CurrentTarget = null;
                session.CurrentTargetHost = null;
                session.CurrentEffect = Forms.DragDropEffects.None;
            }
        }
    }

    private void AttachExternalDropWindow()
    {
        Window? window = Window.GetWindow(this);
        if (ReferenceEquals(window, _externalDropWindow))
        {
            return;
        }

        DetachExternalDropWindow();
        if (window == null)
        {
            return;
        }

        _externalDropWindow = window;
        lock (s_dropWindowStateGate)
        {
            PortableDropWindowState state = s_dropWindowStates.GetValue(
                window,
                static candidate => new PortableDropWindowState(candidate.AllowDrop));
            state.HostCount++;
            window.AllowDrop = true;
        }
        window.AddHandler(
            System.Windows.DragDrop.DragEnterEvent,
            new System.Windows.DragEventHandler(OnWindowDragEnter));
        window.AddHandler(
            System.Windows.DragDrop.DragOverEvent,
            new System.Windows.DragEventHandler(OnWindowDragOver));
        window.AddHandler(
            System.Windows.DragDrop.DragLeaveEvent,
            new System.Windows.DragEventHandler(OnWindowDragLeave));
        window.AddHandler(
            System.Windows.DragDrop.DropEvent,
            new System.Windows.DragEventHandler(OnWindowDrop));
    }

    private void DetachExternalDropWindow()
    {
        Window? window = _externalDropWindow;
        if (window == null)
        {
            return;
        }

        window.RemoveHandler(
            System.Windows.DragDrop.DragEnterEvent,
            new System.Windows.DragEventHandler(OnWindowDragEnter));
        window.RemoveHandler(
            System.Windows.DragDrop.DragOverEvent,
            new System.Windows.DragEventHandler(OnWindowDragOver));
        window.RemoveHandler(
            System.Windows.DragDrop.DragLeaveEvent,
            new System.Windows.DragEventHandler(OnWindowDragLeave));
        window.RemoveHandler(
            System.Windows.DragDrop.DropEvent,
            new System.Windows.DragEventHandler(OnWindowDrop));
        lock (s_dropWindowStateGate)
        {
            if (s_dropWindowStates.TryGetValue(window, out PortableDropWindowState? state))
            {
                state.HostCount--;
                if (state.HostCount <= 0)
                {
                    window.AllowDrop = state.OriginalAllowDrop;
                    s_dropWindowStates.Remove(window);
                }
            }
        }

        _externalDropWindow = null;
    }

    private void OnWindowDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        ProcessExternalDragEvent(e, PortableDragEventKind.Enter);
    }

    private void OnWindowDragOver(object sender, System.Windows.DragEventArgs e)
    {
        ProcessExternalDragEvent(e, PortableDragEventKind.Over);
    }

    private void OnWindowDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        ProcessExternalDragEvent(e, PortableDragEventKind.Leave);
    }

    private void OnWindowDrop(object sender, System.Windows.DragEventArgs e)
    {
        ProcessExternalDragEvent(e, PortableDragEventKind.Drop);
    }

    private void EnsureDesignSelectionService()
    {
        if (_designSelectionService is not null || _designSelectionServiceLookupComplete)
        {
            return;
        }

        _designSelectionServiceLookupComplete = true;
        ISelectionService? selectionService = _child is null
            ? null
            : FindDesignSelectionService(_child);
        if (ReferenceEquals(selectionService, _designSelectionService))
        {
            return;
        }

        DetachDesignSelectionService();
        _designSelectionService = selectionService;
        if (_designSelectionService is not null)
        {
            _designSelectionService.SelectionChanged += OnDesignSelectionChanged;
        }
    }

    private void DetachDesignSelectionService()
    {
        if (_designSelectionService is not null)
        {
            _designSelectionService.SelectionChanged -= OnDesignSelectionChanged;
            _designSelectionService = null;
        }
    }

    private void OnDesignSelectionChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private static ISelectionService? FindDesignSelectionService(Forms.Control control)
    {
        if (control.Site?.DesignMode == true
            && control.Site.GetService(typeof(ISelectionService)) is ISelectionService selectionService)
        {
            return selectionService;
        }

        foreach (Forms.Control child in control.Controls)
        {
            ISelectionService? childSelectionService = FindDesignSelectionService(child);
            if (childSelectionService is not null)
            {
                return childSelectionService;
            }
        }

        return null;
    }

    private void RenderDesignAdorners(DrawingContext drawingContext, Forms.Control root)
    {
        EnsureDesignSelectionService();
        if (_designSelectionService is null)
        {
            return;
        }

        object? primarySelection = _designSelectionService.PrimarySelection;
        foreach (object? selected in _designSelectionService.GetSelectedComponents())
        {
            if (selected is not Forms.Control control
                || !TryGetControlHostBounds(root, control, out Rect bounds))
            {
                continue;
            }

            bool primary = ReferenceEquals(primarySelection, control);
            Pen border = new(SystemColors.HighlightBrush, primary ? 1.5 : 1);
            drawingContext.DrawRectangle(null, border, bounds);
            if (!primary || ReferenceEquals(control, root) || control.Dock != Forms.DockStyle.None)
            {
                continue;
            }

            foreach (DesignHandle handle in s_resizeHandles)
            {
                Rect handleBounds = GetDesignHandleBounds(bounds, handle);
                drawingContext.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 1), handleBounds);
            }
        }
    }

    private Forms.Control? FindDesignAdornerTarget(
        Point hostPoint,
        out Point localPoint,
        out DesignHandle handle)
    {
        EnsureDesignSelectionService();
        if (_child is null
            || _designSelectionService?.PrimarySelection is not Forms.Control control
            || ReferenceEquals(control, _child)
            || control.Dock != Forms.DockStyle.None
            || !TryGetControlHostBounds(_child, control, out Rect bounds))
        {
            localPoint = default;
            handle = DesignHandle.None;
            return null;
        }

        foreach (DesignHandle candidate in s_resizeHandles)
        {
            if (!GetDesignHandleBounds(bounds, candidate).Contains(hostPoint))
            {
                continue;
            }

            localPoint = new Point(hostPoint.X - bounds.X, hostPoint.Y - bounds.Y);
            handle = candidate;
            return control;
        }

        localPoint = default;
        handle = DesignHandle.None;
        return null;
    }

    private static bool TryGetControlHostBounds(
        Forms.Control root,
        Forms.Control target,
        out Rect bounds)
    {
        double x = 0;
        double y = 0;
        for (Forms.Control? current = target; current != null; current = current.Parent)
        {
            if (!current.Visible)
            {
                bounds = Rect.Empty;
                return false;
            }

            if (ReferenceEquals(current, root))
            {
                bounds = new Rect(x, y, target.Width, target.Height);
                return bounds.Width > 0 && bounds.Height > 0;
            }

            x += current.Left;
            y += current.Top;
            Point parentOffset = GetChildDisplayOffset(current.Parent);
            x += parentOffset.X;
            y += parentOffset.Y;
        }

        bounds = Rect.Empty;
        return false;
    }

    private static Rect GetDesignHandleBounds(Rect bounds, DesignHandle handle)
    {
        double half = DesignHandleSize / 2;
        (double x, double y) = handle switch
        {
            DesignHandle.TopLeft => (bounds.Left, bounds.Top),
            DesignHandle.Top => (bounds.Left + bounds.Width / 2, bounds.Top),
            DesignHandle.TopRight => (bounds.Right, bounds.Top),
            DesignHandle.Right => (bounds.Right, bounds.Top + bounds.Height / 2),
            DesignHandle.BottomRight => (bounds.Right, bounds.Bottom),
            DesignHandle.Bottom => (bounds.Left + bounds.Width / 2, bounds.Bottom),
            DesignHandle.BottomLeft => (bounds.Left, bounds.Bottom),
            DesignHandle.Left => (bounds.Left, bounds.Top + bounds.Height / 2),
            _ => (double.NaN, double.NaN)
        };
        return new Rect(x - half, y - half, DesignHandleSize, DesignHandleSize);
    }

    private static System.Windows.Input.Cursor GetDesignHandleCursor(DesignHandle handle)
    {
        return handle switch
        {
            DesignHandle.TopLeft or DesignHandle.BottomRight => System.Windows.Input.Cursors.SizeNWSE,
            DesignHandle.TopRight or DesignHandle.BottomLeft => System.Windows.Input.Cursors.SizeNESW,
            DesignHandle.Left or DesignHandle.Right => System.Windows.Input.Cursors.SizeWE,
            DesignHandle.Top or DesignHandle.Bottom => System.Windows.Input.Cursors.SizeNS,
            _ => System.Windows.Input.Cursors.Arrow
        };
    }

    private static System.Windows.Input.Cursor GetPortableCursor(Forms.Cursor cursor)
    {
        return cursor.PortableKind switch
        {
            Forms.PortableCursorKind.Wait => System.Windows.Input.Cursors.Wait,
            Forms.PortableCursorKind.IBeam => System.Windows.Input.Cursors.IBeam,
            Forms.PortableCursorKind.SizeWE => System.Windows.Input.Cursors.SizeWE,
            Forms.PortableCursorKind.SizeNS => System.Windows.Input.Cursors.SizeNS,
            _ => System.Windows.Input.Cursors.Arrow
        };
    }

    private static Forms.Control? FindControlAt(Forms.Control root, Point hostPoint, out Point localPoint)
    {
        return FindControlAt(root, new Point(0, 0), hostPoint, out localPoint);
    }

    private Forms.Control? FindPointerTarget(Point hostPoint, out Point localPoint)
    {
        if (_child != null
            && _capturedControl != null
            && TryConvertHostPoint(_child, _capturedControl, hostPoint, out localPoint))
        {
            return _capturedControl;
        }

        _capturedControl = null;
        if (_child == null)
        {
            localPoint = default;
            return null;
        }

        return FindControlAt(_child, hostPoint, out localPoint);
    }

    private Forms.Control ResolveDesignInputTarget(
        Forms.Control target,
        Point hostPoint,
        ref Point localPoint)
    {
        if (_child == null || target.Site?.DesignMode == true)
        {
            return target;
        }

        for (Forms.Control? current = target.Parent; current != null; current = current.Parent)
        {
            if (current.Site?.DesignMode != true)
            {
                continue;
            }

            if (TryConvertHostPoint(_child, current, hostPoint, out Point designPoint))
            {
                localPoint = designPoint;
                return current;
            }

            break;
        }

        return target;
    }

    private void ReleaseDesignerCapture(Forms.Control target)
    {
        if (!ReferenceEquals(_capturedControl, target) || target.Capture)
        {
            return;
        }

        _capturedControl = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private static bool TryConvertHostPoint(
        Forms.Control root,
        Forms.Control target,
        Point hostPoint,
        out Point localPoint)
    {
        double x = 0;
        double y = 0;
        for (Forms.Control? current = target; current != null; current = current.Parent)
        {
            x += current.Left;
            y += current.Top;
            Point parentOffset = GetChildDisplayOffset(current.Parent);
            x += parentOffset.X;
            y += parentOffset.Y;
            if (ReferenceEquals(current, root))
            {
                localPoint = new Point(hostPoint.X - x, hostPoint.Y - y);
                return true;
            }
        }

        localPoint = default;
        return false;
    }

    private static Forms.Control? FindControlAt(Forms.Control control, Point parentOrigin, Point hostPoint, out Point localPoint)
    {
        Point origin = new(parentOrigin.X + control.Left, parentOrigin.Y + control.Top);
        Rect bounds = new(origin.X, origin.Y, control.Width, control.Height);
        if (!control.Visible || !bounds.Contains(hostPoint))
        {
            localPoint = default;
            return null;
        }

        Point childOffset = GetChildDisplayOffset(control);
        Point childOrigin = new(origin.X + childOffset.X, origin.Y + childOffset.Y);
        for (int i = control.Controls.Count - 1; i >= 0; i--)
        {
            Forms.Control child = control.Controls[i];
            Forms.Control? result = FindControlAt(child, childOrigin, hostPoint, out localPoint);
            if (result != null)
            {
                return result;
            }
        }

        localPoint = new Point(hostPoint.X - origin.X, hostPoint.Y - origin.Y);
        return control;
    }

    private static Forms.MouseButtons MapMouseButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => Forms.MouseButtons.Left,
            MouseButton.Right => Forms.MouseButtons.Right,
            MouseButton.Middle => Forms.MouseButtons.Middle,
            _ => Forms.MouseButtons.None
        };
    }

    private static Forms.MouseButtons MapMouseButtons(System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            return Forms.MouseButtons.Left;
        }

        if (e.RightButton == MouseButtonState.Pressed)
        {
            return Forms.MouseButtons.Right;
        }

        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            return Forms.MouseButtons.Middle;
        }

        return Forms.MouseButtons.None;
    }

    private static int ToWinFormsCoordinate(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return (int)Math.Round(value);
    }

    private static Forms.Message CreateHostedKeyMessage(
        Forms.Control target,
        Forms.Keys keyData,
        bool keyDown)
    {
        bool systemKey = (keyData & Forms.Keys.Alt) != 0;
        return new Forms.Message
        {
            HWnd = target.Handle,
            Msg = keyDown
                ? (systemKey ? 0x0104 : 0x0100)
                : (systemKey ? 0x0105 : 0x0101),
            WParam = new IntPtr((int)(keyData & Forms.Keys.KeyCode))
        };
    }

    private static Forms.Keys MapKey(System.Windows.Input.Key key, ModifierKeys modifiers)
    {
        Forms.Keys keyData = key switch
        {
            System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift => Forms.Keys.ShiftKey,
            System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl => Forms.Keys.ControlKey,
            System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt => Forms.Keys.Menu,
            System.Windows.Input.Key.CapsLock => Forms.Keys.CapsLock,
            System.Windows.Input.Key.Enter => Forms.Keys.Enter,
            System.Windows.Input.Key.Tab => Forms.Keys.Tab,
            System.Windows.Input.Key.Escape => Forms.Keys.Escape,
            System.Windows.Input.Key.Space => Forms.Keys.Space,
            System.Windows.Input.Key.Back => Forms.Keys.Back,
            System.Windows.Input.Key.Delete => Forms.Keys.Delete,
            System.Windows.Input.Key.Home => Forms.Keys.Home,
            System.Windows.Input.Key.End => Forms.Keys.End,
            System.Windows.Input.Key.PageUp => Forms.Keys.PageUp,
            System.Windows.Input.Key.PageDown => Forms.Keys.PageDown,
            System.Windows.Input.Key.Left => Forms.Keys.Left,
            System.Windows.Input.Key.Right => Forms.Keys.Right,
            System.Windows.Input.Key.Up => Forms.Keys.Up,
            System.Windows.Input.Key.Down => Forms.Keys.Down,
            System.Windows.Input.Key.Insert => Forms.Keys.Insert,
            >= System.Windows.Input.Key.F1 and <= System.Windows.Input.Key.F12
                => (Forms.Keys)((int)Forms.Keys.F1 + ((int)key - (int)System.Windows.Input.Key.F1)),
            >= System.Windows.Input.Key.NumPad0 and <= System.Windows.Input.Key.NumPad9
                => (Forms.Keys)((int)Forms.Keys.NumPad0 + ((int)key - (int)System.Windows.Input.Key.NumPad0)),
            System.Windows.Input.Key.Multiply => Forms.Keys.Multiply,
            System.Windows.Input.Key.Add => Forms.Keys.Add,
            System.Windows.Input.Key.Subtract => Forms.Keys.Subtract,
            System.Windows.Input.Key.Decimal => Forms.Keys.Decimal,
            System.Windows.Input.Key.Divide => Forms.Keys.Divide,
            System.Windows.Input.Key.OemSemicolon => Forms.Keys.OemSemicolon,
            System.Windows.Input.Key.OemPlus => Forms.Keys.Oemplus,
            System.Windows.Input.Key.OemComma => Forms.Keys.Oemcomma,
            System.Windows.Input.Key.OemMinus => Forms.Keys.OemMinus,
            System.Windows.Input.Key.OemPeriod => Forms.Keys.OemPeriod,
            System.Windows.Input.Key.OemQuestion => Forms.Keys.OemQuestion,
            System.Windows.Input.Key.OemTilde => Forms.Keys.Oemtilde,
            System.Windows.Input.Key.OemOpenBrackets => Forms.Keys.OemOpenBrackets,
            System.Windows.Input.Key.OemPipe => Forms.Keys.OemPipe,
            System.Windows.Input.Key.OemCloseBrackets => Forms.Keys.OemCloseBrackets,
            System.Windows.Input.Key.OemQuotes => Forms.Keys.OemQuotes,
            >= System.Windows.Input.Key.A and <= System.Windows.Input.Key.Z
                => (Forms.Keys)((int)Forms.Keys.A + ((int)key - (int)System.Windows.Input.Key.A)),
            >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9
                => (Forms.Keys)((int)Forms.Keys.D0 + ((int)key - (int)System.Windows.Input.Key.D0)),
            _ => Forms.Keys.None
        };

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            keyData |= Forms.Keys.Control;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            keyData |= Forms.Keys.Shift;
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            keyData |= Forms.Keys.Alt;
        }

        return keyData;
    }

    private void OnChildInvalidated(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _portableChildInvalidationDispatchCount);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnHostedControlAdded(object? sender, Forms.ControlEventArgs e)
    {
        SubscribeInvalidationTree(e.Control);
        if (IsLoaded)
        {
            NotifyPortableHostLifecycle(e.Control, attached: true);
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnHostedControlRemoved(object? sender, Forms.ControlEventArgs e)
    {
        if (IsLoaded)
        {
            NotifyPortableHostLifecycle(e.Control, attached: false);
        }
        UnsubscribeInvalidationTree(e.Control);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void SubscribeInvalidationTree(Forms.Control control)
    {
        if (!_invalidationTreeSubscriptions.Add(control))
        {
            return;
        }

        control.Invalidated += OnChildInvalidated;
        control.ControlAdded += OnHostedControlAdded;
        control.ControlRemoved += OnHostedControlRemoved;
        foreach (Forms.Control child in control.Controls)
        {
            SubscribeInvalidationTree(child);
        }
    }

    private static void NotifyPortableHostLifecycle(Forms.Control control, bool attached)
    {
        if (control is Forms.IPortableWinFormsHostLifecycle lifecycle)
        {
            if (attached)
            {
                lifecycle.OnPortableHostAttached();
            }
            else
            {
                lifecycle.OnPortableHostDetached();
            }
        }

        foreach (Forms.Control child in control.Controls)
        {
            NotifyPortableHostLifecycle(child, attached);
        }
    }

    private void UnsubscribeInvalidationTree(Forms.Control control)
    {
        if (!_invalidationTreeSubscriptions.Remove(control))
        {
            return;
        }

        control.Invalidated -= OnChildInvalidated;
        control.ControlAdded -= OnHostedControlAdded;
        control.ControlRemoved -= OnHostedControlRemoved;
        foreach (Forms.Control child in control.Controls)
        {
            UnsubscribeInvalidationTree(child);
        }

        RetirePortablePaintSurfacePool(control);
    }

    private void ClearRemainingInvalidationSubscriptions()
    {
        foreach (Forms.Control control in _invalidationTreeSubscriptions)
        {
            control.Invalidated -= OnChildInvalidated;
            control.ControlAdded -= OnHostedControlAdded;
            control.ControlRemoved -= OnHostedControlRemoved;
        }

        _invalidationTreeSubscriptions.Clear();
    }

    private static void LayoutControlTree(Forms.Control control, Rect bounds)
    {
        int width = Math.Max(0, (int)Math.Round(bounds.Width));
        int height = Math.Max(0, (int)Math.Round(bounds.Height));
        control.Location = new System.Drawing.Point((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y));
        control.Size = new System.Drawing.Size(width, height);

        if (control.Controls.Count == 0)
        {
            return;
        }

        if (control is Forms.SplitContainer splitContainer)
        {
            LayoutSplitContainer(splitContainer, width, height);
            return;
        }

        if (control is Forms.TabControl tabControl)
        {
            LayoutTabControl(tabControl, width, height);
            return;
        }

        int top = 0;
        int bottom = height;
        var fillControls = new List<Forms.Control>();
        foreach (Forms.Control child in control.Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            switch (child.Dock)
            {
                case Forms.DockStyle.Top:
                    int topHeight = GetPreferredHeight(child);
                    LayoutControlTree(child, new Rect(0, top, width, topHeight));
                    top += topHeight;
                    break;
                case Forms.DockStyle.Bottom:
                    int bottomHeight = GetPreferredHeight(child);
                    bottom -= bottomHeight;
                    LayoutControlTree(child, new Rect(0, bottom, width, bottomHeight));
                    break;
                case Forms.DockStyle.Left:
                    int leftWidth = GetPreferredWidth(child);
                    LayoutControlTree(child, new Rect(0, top, leftWidth, Math.Max(0, bottom - top)));
                    break;
                case Forms.DockStyle.Right:
                    int rightWidth = GetPreferredWidth(child);
                    LayoutControlTree(child, new Rect(Math.Max(0, width - rightWidth), top, rightWidth, Math.Max(0, bottom - top)));
                    break;
                case Forms.DockStyle.Fill:
                    fillControls.Add(child);
                    break;
                default:
                    LayoutControlTree(child, new Rect(child.Left, child.Top, child.Width, child.Height));
                    break;
            }
        }

        foreach (Forms.Control child in fillControls)
        {
            LayoutControlTree(child, new Rect(0, top, width, Math.Max(0, bottom - top)));
        }
    }

    private static void LayoutSplitContainer(Forms.SplitContainer splitContainer, int width, int height)
    {
        int splitterSize = splitContainer.SplitterWidth;
        if (splitContainer.Orientation == Forms.Orientation.Horizontal)
        {
            int available = Math.Max(0, height - splitterSize);
            int distance = splitContainer.SplitterDistance > 0 ? splitContainer.SplitterDistance : available / 2;
            distance = ClampSplitterDistance(
                distance,
                available,
                splitContainer.Panel1MinSize,
                splitContainer.Panel2MinSize);
            LayoutControlTree(splitContainer.Panel1, new Rect(0, 0, width, distance));
            LayoutControlTree(splitContainer.Panel2, new Rect(0, distance + splitterSize, width, Math.Max(0, height - distance - splitterSize)));
        }
        else
        {
            int available = Math.Max(0, width - splitterSize);
            int distance = splitContainer.SplitterDistance > 0 ? splitContainer.SplitterDistance : available / 2;
            distance = ClampSplitterDistance(
                distance,
                available,
                splitContainer.Panel1MinSize,
                splitContainer.Panel2MinSize);
            LayoutControlTree(splitContainer.Panel1, new Rect(0, 0, distance, height));
            LayoutControlTree(splitContainer.Panel2, new Rect(distance + splitterSize, 0, Math.Max(0, width - distance - splitterSize), height));
        }
    }

    private static int ClampSplitterDistance(int distance, int available, int panel1MinSize, int panel2MinSize)
    {
        int firstMinimum = Math.Min(panel1MinSize, available);
        int secondMinimum = Math.Min(panel2MinSize, Math.Max(0, available - firstMinimum));
        return Math.Clamp(distance, firstMinimum, Math.Max(firstMinimum, available - secondMinimum));
    }

    private static void LayoutTabControl(Forms.TabControl tabControl, int width, int height)
    {
        const int tabHeaderHeight = 24;
        int contentTop = Math.Min(tabHeaderHeight, height);
        int contentWidth = Math.Max(0, width - 4);
        int contentHeight = Math.Max(0, height - contentTop - 2);

        foreach (Forms.TabPage page in tabControl.TabPages)
        {
            LayoutControlTree(page, new Rect(2, contentTop, contentWidth, contentHeight));
        }
    }

    private static int GetPreferredHeight(Forms.Control control)
    {
        if (control.Height > 0)
        {
            return control.Height;
        }

        return control is Forms.ToolStrip ? 24 : 20;
    }

    private static int GetPreferredWidth(Forms.Control control)
    {
        if (control.Width > 0)
        {
            return control.Width;
        }

        return 120;
    }

    private void RenderControl(DrawingContext drawingContext, Forms.Control control, Rect bounds)
    {
        if (!control.Visible || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        drawingContext.PushClip(new RectangleGeometry(bounds));
        try
        {
            Brush background = CreateBrush(control.BackColor, Background ?? SystemColors.ControlBrush);
            Brush foreground = CreateBrush(control.ForeColor, Foreground);
            drawingContext.DrawRectangle(background, null, bounds);

            bool renderChildren = true;
            if (control is Forms.TreeView treeView)
            {
                RenderTreeView(drawingContext, treeView, bounds, foreground);
                renderChildren = false;
            }
            else if (control is Forms.TabControl tabControl)
            {
                RenderTabControl(drawingContext, tabControl, bounds, foreground);
                renderChildren = false;
            }
            else if (control is Forms.SplitContainer splitContainer)
            {
                RenderSplitContainer(drawingContext, splitContainer, bounds);
            }
            else if (control is Forms.PropertyGrid propertyGrid)
            {
                RenderPropertyGrid(drawingContext, propertyGrid, bounds, foreground);
            }
            else if (control is Forms.DataGridView dataGridView)
            {
                RenderDataGridView(drawingContext, dataGridView, bounds, foreground);
            }
            else if (control is Forms.ComboBox comboBox)
            {
                RenderComboBox(drawingContext, comboBox, bounds, foreground);
            }
            else if (control is Forms.CheckedListBox checkedListBox)
            {
                RenderListBox(drawingContext, checkedListBox, bounds, foreground, true);
            }
            else if (control is Forms.ListBox listBox)
            {
                RenderListBox(drawingContext, listBox, bounds, foreground, false);
            }
            else if (control is Forms.ListView listView)
            {
                RenderListView(drawingContext, listView, bounds, foreground);
            }
            else if (control is Forms.ToolStrip toolStrip)
            {
                RenderToolStrip(drawingContext, toolStrip, bounds, foreground);
            }
            else if (control is Forms.CheckBox checkBox)
            {
                RenderCheckBox(drawingContext, checkBox, bounds, foreground);
            }
            else if (control is Forms.RadioButton radioButton)
            {
                RenderRadioButton(drawingContext, radioButton, bounds, foreground);
            }
            else if (control is Forms.ButtonBase buttonBase)
            {
                RenderButton(
                    drawingContext,
                    buttonBase,
                    bounds,
                    foreground,
                    isPressed: ReferenceEquals(_pressedControl, buttonBase));
            }
            else if (control is Forms.TabPage)
            {
                drawingContext.DrawRectangle(SystemColors.ControlBrush, null, bounds);
            }
            else if (RenderPortableCustomPaint(drawingContext, control, bounds))
            {
            }
            else if (!string.IsNullOrEmpty(control.Text))
            {
                DrawText(drawingContext, control.Text, new Point(bounds.X + 4, bounds.Y + 3), foreground, 12);
            }

            RenderCreateGraphicsSurface(drawingContext, control, bounds);

            if (renderChildren)
            {
                Point childOffset = GetChildDisplayOffset(control);
                foreach (Forms.Control child in control.Controls)
                {
                    Rect childBounds = new(
                        bounds.X + childOffset.X + child.Left,
                        bounds.Y + childOffset.Y + child.Top,
                        Math.Max(0, child.Width),
                        Math.Max(0, child.Height));
                    RenderControl(drawingContext, child, childBounds);
                }
            }

            if (control is Forms.UserControl userControl)
            {
                DrawBorder(drawingContext, userControl.BorderStyle, bounds);
            }

            RenderPortableDesignerAdornments(drawingContext, control, bounds);
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    private static Point GetChildDisplayOffset(Forms.Control? control)
    {
        if (control is not Forms.ScrollableControl scrollable || !scrollable.AutoScroll)
        {
            return default;
        }

        System.Drawing.Point location = scrollable.DisplayRectangle.Location;
        return new Point(location.X, location.Y);
    }

    private bool RenderPortableCustomPaint(
        DrawingContext drawingContext,
        Forms.Control control,
        Rect bounds)
    {
        if (control is not Forms.IPortableWinFormsPaintSource paintSource
            || !paintSource.SupportsPortablePainting)
        {
            return false;
        }

        int width = Math.Max(0, ToWinFormsCoordinate(bounds.Width));
        int height = Math.Max(0, ToWinFormsCoordinate(bounds.Height));
        if (width == 0 || height == 0)
        {
            return true;
        }

        if (TryGetNativeDrawingContext(
                drawingContext,
                out ProGPU.Scene.DrawingContext nativeContext,
                out Matrix4x4 outerTransform))
        {
            Matrix4x4 clientTransform = Matrix4x4.CreateTranslation((float)bounds.X, (float)bounds.Y, 0f)
                * outerTransform;
            using DrawingGraphics graphics = DrawingGraphics.FromProGpuDrawingContext(nativeContext, clientTransform);
            PaintPortableControl(paintSource, graphics, width, height);
        }
        else
        {
            PortablePaintSurface surface = GetPortablePaintSurfacePool(control).AcquireFixed(width, height);
            if (surface.Source == null)
            {
                return false;
            }

            using (DrawingGraphics graphics = DrawingGraphics.FromImage(surface.Bitmap))
            {
                graphics.Clear(DrawingColor.Transparent);
                PaintPortableControl(paintSource, graphics, width, height);
            }

            drawingContext.DrawImage(surface.Source, bounds);
        }

        Interlocked.Increment(ref _portableCustomPaintDispatchCount);
        return true;
    }

    private static void PaintPortableControl(
        Forms.IPortableWinFormsPaintSource paintSource,
        DrawingGraphics graphics,
        int width,
        int height)
    {
        var paintEventArgs = new Forms.PaintEventArgs(
            graphics,
            new DrawingRectangle(0, 0, width, height));
        paintSource.PaintPortableBackground(paintEventArgs);
        paintSource.PaintPortable(paintEventArgs);
    }

    private void RenderPortableDesignerAdornments(
        DrawingContext drawingContext,
        Forms.Control control,
        Rect bounds)
    {
        if (control is not Forms.IPortableWinFormsAdornerSource adornerSource
            || !adornerSource.SupportsPortableAdornments)
        {
            return;
        }

        int width = Math.Max(0, ToWinFormsCoordinate(bounds.Width));
        int height = Math.Max(0, ToWinFormsCoordinate(bounds.Height));
        if (width == 0 || height == 0)
        {
            return;
        }

        if (TryGetNativeDrawingContext(
                drawingContext,
                out ProGPU.Scene.DrawingContext nativeContext,
                out Matrix4x4 outerTransform))
        {
            Matrix4x4 clientTransform = Matrix4x4.CreateTranslation((float)bounds.X, (float)bounds.Y, 0f)
                * outerTransform;
            using DrawingGraphics graphics = DrawingGraphics.FromProGpuDrawingContext(nativeContext, clientTransform);
            PaintPortableDesignerAdornments(adornerSource, graphics, width, height);
        }
        else
        {
            PortablePaintSurface surface = GetPortableDesignerAdornerSurfacePool(control).AcquireFixed(width, height);
            if (surface.Source == null)
            {
                return;
            }

            using (DrawingGraphics graphics = DrawingGraphics.FromImage(surface.Bitmap))
            {
                graphics.Clear(DrawingColor.Transparent);
                PaintPortableDesignerAdornments(adornerSource, graphics, width, height);
            }

            drawingContext.DrawImage(surface.Source, bounds);
        }

        Interlocked.Increment(ref _portableDesignerAdornerDispatchCount);
    }

    private static void PaintPortableDesignerAdornments(
        Forms.IPortableWinFormsAdornerSource adornerSource,
        DrawingGraphics graphics,
        int width,
        int height)
    {
        var paintEventArgs = new Forms.PaintEventArgs(
            graphics,
            new DrawingRectangle(0, 0, width, height));
        adornerSource.PaintPortableAdornments(paintEventArgs);
    }

    private static bool TryGetNativeDrawingContext(
        DrawingContext drawingContext,
        out ProGPU.Scene.DrawingContext nativeContext,
        out Matrix4x4 outerTransform)
    {
        nativeContext = null!;
        outerTransform = Matrix4x4.Identity;
        if (drawingContext is IPortableNativeDrawingContextStateSource nativeContextStateSource
            && nativeContextStateSource.TryGetPortableNativeDrawingContextState(out var state)
            && state.NativeDrawingContext is ProGPU.Scene.DrawingContext resolvedStateContext)
        {
            nativeContext = resolvedStateContext;
            outerTransform = state.Transform;
            return true;
        }

        if (drawingContext is not IPortableNativeDrawingContextSource nativeContextSource
            || !nativeContextSource.TryGetPortableNativeDrawingContext(out object? nativeContextObject)
            || nativeContextObject is not ProGPU.Scene.DrawingContext resolvedContext)
        {
            return false;
        }

        nativeContext = resolvedContext;
        return true;
    }

    private void RenderCheckBox(DrawingContext drawingContext, Forms.CheckBox checkBox, Rect bounds, Brush foreground)
    {
        if (checkBox.Appearance == Forms.Appearance.Button)
        {
            RenderButton(
                drawingContext,
                checkBox,
                bounds,
                foreground,
                isPressed: checkBox.Checked || ReferenceEquals(_pressedControl, checkBox));
            return;
        }

        Brush effectiveForeground = checkBox.Enabled ? foreground : SystemColors.GrayTextBrush;
        Rect glyphArea = Inset(bounds, 2);
        double glyphSize = Math.Max(0, Math.Min(13, glyphArea.Height));
        Rect glyphBounds = AlignContent(glyphArea, glyphSize, glyphSize, checkBox.CheckAlign);
        if (glyphSize > 0)
        {
            drawingContext.DrawRectangle(
                checkBox.Enabled ? SystemColors.WindowBrush : SystemColors.ControlBrush,
                new Pen(SystemColors.ControlDarkBrush, 1),
                glyphBounds);

            if (checkBox.CheckState == Forms.CheckState.Checked)
            {
                DrawCheckMark(drawingContext, glyphBounds, effectiveForeground);
            }
            else if (checkBox.CheckState == Forms.CheckState.Indeterminate)
            {
                drawingContext.DrawRectangle(effectiveForeground, null, Inset(glyphBounds, 3));
            }
        }

        Rect contentBounds = GetCheckableContentBounds(bounds, glyphBounds, checkBox.CheckAlign);
        RenderButtonContent(drawingContext, checkBox, contentBounds, effectiveForeground);
    }

    private void RenderRadioButton(DrawingContext drawingContext, Forms.RadioButton radioButton, Rect bounds, Brush foreground)
    {
        if (radioButton.Appearance == Forms.Appearance.Button)
        {
            RenderButton(
                drawingContext,
                radioButton,
                bounds,
                foreground,
                isPressed: radioButton.Checked || ReferenceEquals(_pressedControl, radioButton));
            return;
        }

        Brush effectiveForeground = radioButton.Enabled ? foreground : SystemColors.GrayTextBrush;
        Rect glyphArea = Inset(bounds, 2);
        double glyphSize = Math.Max(0, Math.Min(13, glyphArea.Height));
        Rect glyphBounds = AlignContent(glyphArea, glyphSize, glyphSize, radioButton.CheckAlign);
        if (glyphSize > 0)
        {
            Point center = new(glyphBounds.X + glyphBounds.Width / 2, glyphBounds.Y + glyphBounds.Height / 2);
            double radius = glyphSize / 2;
            drawingContext.DrawEllipse(
                radioButton.Enabled ? SystemColors.WindowBrush : SystemColors.ControlBrush,
                new Pen(SystemColors.ControlDarkBrush, 1),
                center,
                radius,
                radius);
            if (radioButton.Checked)
            {
                double dotRadius = Math.Max(1, radius - 3.5);
                drawingContext.DrawEllipse(effectiveForeground, null, center, dotRadius, dotRadius);
            }
        }

        Rect contentBounds = GetCheckableContentBounds(bounds, glyphBounds, radioButton.CheckAlign);
        RenderButtonContent(drawingContext, radioButton, contentBounds, effectiveForeground);
    }

    private void RenderButton(
        DrawingContext drawingContext,
        Forms.ButtonBase button,
        Rect bounds,
        Brush foreground,
        bool isPressed)
    {
        Brush fill = isPressed
            ? SystemColors.ControlDarkBrush
            : CreateBrush(button.BackColor, SystemColors.ControlBrush);
        Brush effectiveForeground = button.Enabled ? foreground : SystemColors.GrayTextBrush;
        double borderThickness = button is Forms.Button { IsDefault: true } ? 2 : 1;
        Rect chromeBounds = Inset(bounds, borderThickness / 2);
        drawingContext.DrawRectangle(fill, new Pen(SystemColors.ControlDarkBrush, borderThickness), chromeBounds);

        Rect contentBounds = Inset(bounds, 4);
        if (isPressed)
        {
            contentBounds = new Rect(
                contentBounds.X + 1,
                contentBounds.Y + 1,
                Math.Max(0, contentBounds.Width - 1),
                Math.Max(0, contentBounds.Height - 1));
        }

        RenderButtonContent(drawingContext, button, contentBounds, effectiveForeground);
    }

    private void RenderButtonContent(
        DrawingContext drawingContext,
        Forms.ButtonBase button,
        Rect contentBounds,
        Brush foreground)
    {
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
        {
            return;
        }

        if (TryGetImageSource(button.Image, out ImageSource? imageSource)
            && imageSource is { } availableImage)
        {
            double sourceWidth = Math.Max(1, availableImage.Width);
            double sourceHeight = Math.Max(1, availableImage.Height);
            double scale = Math.Min(1, Math.Min(contentBounds.Width / sourceWidth, contentBounds.Height / sourceHeight));
            Rect imageBounds = AlignContent(
                contentBounds,
                sourceWidth * scale,
                sourceHeight * scale,
                button.ImageAlign);
            drawingContext.DrawImage(availableImage, imageBounds);
        }

        if (string.IsNullOrEmpty(button.Text))
        {
            return;
        }

        FormattedText formatted = CreateFormattedText(button.Text, foreground, 12);
        Rect textBounds = AlignContent(
            contentBounds,
            Math.Min(contentBounds.Width, formatted.WidthIncludingTrailingWhitespace),
            Math.Min(contentBounds.Height, formatted.Height),
            button.TextAlign);
        drawingContext.PushClip(new RectangleGeometry(contentBounds));
        try
        {
            drawingContext.DrawText(formatted, new Point(textBounds.X, textBounds.Y));
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    private static void DrawCheckMark(DrawingContext drawingContext, Rect bounds, Brush brush)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(
                new Point(bounds.X + bounds.Width * 0.2, bounds.Y + bounds.Height * 0.52),
                isFilled: false,
                isClosed: false);
            context.LineTo(
                new Point(bounds.X + bounds.Width * 0.43, bounds.Y + bounds.Height * 0.75),
                isStroked: true,
                isSmoothJoin: false);
            context.LineTo(
                new Point(bounds.X + bounds.Width * 0.82, bounds.Y + bounds.Height * 0.25),
                isStroked: true,
                isSmoothJoin: false);
        }

        drawingContext.DrawGeometry(null, new Pen(brush, 1.5), geometry);
    }

    private bool TryGetImageSource(DrawingImage? image, out ImageSource? imageSource)
    {
        imageSource = null;
        if (image is null)
        {
            return false;
        }

        CachedImageSource cached = _imageSourceCache.GetValue(
            image,
            static key => new CachedImageSource(CreateImageSource(key)));
        imageSource = cached.Source;
        return imageSource != null;
    }

    private static Rect GetCheckableContentBounds(
        Rect bounds,
        Rect glyphBounds,
        System.Drawing.ContentAlignment checkAlign)
    {
        const double spacing = 4;
        if (IsRightAligned(checkAlign))
        {
            return new Rect(
                bounds.X + spacing,
                bounds.Y,
                Math.Max(0, glyphBounds.X - bounds.X - spacing * 2),
                bounds.Height);
        }

        return new Rect(
            glyphBounds.Right + spacing,
            bounds.Y,
            Math.Max(0, bounds.Right - glyphBounds.Right - spacing * 2),
            bounds.Height);
    }

    private static Rect AlignContent(
        Rect bounds,
        double width,
        double height,
        System.Drawing.ContentAlignment alignment)
    {
        width = Math.Max(0, Math.Min(bounds.Width, width));
        height = Math.Max(0, Math.Min(bounds.Height, height));

        double x = IsRightAligned(alignment)
            ? bounds.Right - width
            : IsCenterAlignedHorizontally(alignment)
                ? bounds.X + (bounds.Width - width) / 2
                : bounds.X;
        double y = IsBottomAligned(alignment)
            ? bounds.Bottom - height
            : IsCenterAlignedVertically(alignment)
                ? bounds.Y + (bounds.Height - height) / 2
                : bounds.Y;
        return new Rect(x, y, width, height);
    }

    private static Rect Inset(Rect bounds, double amount)
    {
        double horizontal = Math.Min(Math.Max(0, amount), bounds.Width / 2);
        double vertical = Math.Min(Math.Max(0, amount), bounds.Height / 2);
        return new Rect(
            bounds.X + horizontal,
            bounds.Y + vertical,
            Math.Max(0, bounds.Width - horizontal * 2),
            Math.Max(0, bounds.Height - vertical * 2));
    }

    private static bool IsRightAligned(System.Drawing.ContentAlignment alignment)
    {
        return alignment is System.Drawing.ContentAlignment.TopRight
            or System.Drawing.ContentAlignment.MiddleRight
            or System.Drawing.ContentAlignment.BottomRight;
    }

    private static bool IsCenterAlignedHorizontally(System.Drawing.ContentAlignment alignment)
    {
        return alignment is System.Drawing.ContentAlignment.TopCenter
            or System.Drawing.ContentAlignment.MiddleCenter
            or System.Drawing.ContentAlignment.BottomCenter;
    }

    private static bool IsBottomAligned(System.Drawing.ContentAlignment alignment)
    {
        return alignment is System.Drawing.ContentAlignment.BottomLeft
            or System.Drawing.ContentAlignment.BottomCenter
            or System.Drawing.ContentAlignment.BottomRight;
    }

    private static bool IsCenterAlignedVertically(System.Drawing.ContentAlignment alignment)
    {
        return alignment is System.Drawing.ContentAlignment.MiddleLeft
            or System.Drawing.ContentAlignment.MiddleCenter
            or System.Drawing.ContentAlignment.MiddleRight;
    }

    private void RenderSplitContainer(DrawingContext drawingContext, Forms.SplitContainer splitContainer, Rect bounds)
    {
        double splitterSize = splitContainer.SplitterWidth;
        Pen splitterPen = new(SystemColors.ControlDarkBrush, 1);
        if (splitContainer.Orientation == Forms.Orientation.Horizontal)
        {
            double splitterTop = bounds.Y + splitContainer.Panel1.Height;
            drawingContext.DrawRectangle(SystemColors.ControlBrush, splitterPen, new Rect(bounds.X, splitterTop, bounds.Width, splitterSize));
        }
        else
        {
            double splitterLeft = bounds.X + splitContainer.Panel1.Width;
            drawingContext.DrawRectangle(SystemColors.ControlBrush, splitterPen, new Rect(splitterLeft, bounds.Y, splitterSize, bounds.Height));
        }
    }

    private void RenderTabControl(DrawingContext drawingContext, Forms.TabControl tabControl, Rect bounds, Brush foreground)
    {
        const double tabHeaderHeight = 24;
        drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), bounds);

        double x = bounds.X + 2;
        for (int i = 0; i < tabControl.TabPages.Count; i++)
        {
            Forms.TabPage page = tabControl.TabPages[i];
            string text = string.IsNullOrEmpty(page.Text) ? page.Name : page.Text;
            double width = Math.Max(56, MeasureText(text, 12) + 18);
            Rect tabBounds = new(x, bounds.Y + 2, width, tabHeaderHeight - 2);
            bool selected = i == tabControl.SelectedIndex;
            drawingContext.DrawRectangle(
                selected ? SystemColors.WindowBrush : SystemColors.ControlBrush,
                new Pen(SystemColors.ControlDarkBrush, 1),
                tabBounds);
            DrawTextInBounds(drawingContext, text, new Rect(tabBounds.X + 8, tabBounds.Y + 4, Math.Max(0, tabBounds.Width - 16), tabBounds.Height - 6), foreground, 12);
            x += width - 1;
            if (x > bounds.Right)
            {
                break;
            }
        }

        Rect contentBounds = new(bounds.X + 1, bounds.Y + tabHeaderHeight, Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - tabHeaderHeight - 1));
        drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlDarkBrush, 1), contentBounds);

        Forms.TabPage? selectedPage = tabControl.SelectedTab;
        if (selectedPage != null)
        {
            Rect pageBounds = new(bounds.X + selectedPage.Left, bounds.Y + selectedPage.Top, selectedPage.Width, selectedPage.Height);
            RenderControl(drawingContext, selectedPage, pageBounds);
        }
    }

    private void RenderComboBox(DrawingContext drawingContext, Forms.ComboBox comboBox, Rect bounds, Brush foreground)
    {
        Pen borderPen = new(SystemColors.ControlDarkBrush, 1);
        drawingContext.DrawRectangle(SystemColors.WindowBrush, borderPen, bounds);

        string text = comboBox.SelectedItem?.ToString() ?? comboBox.Text;
        Rect textBounds = new(bounds.X + 5, bounds.Y + 2, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 4));
        bool ownerDrawn = comboBox.SelectedIndex >= 0
            && comboBox.DrawMode != Forms.DrawMode.Normal
            && TryRenderListItemOwnerDraw(drawingContext, comboBox, comboBox.SelectedIndex, bounds, textBounds);
        if (!ownerDrawn)
        {
            DrawTextInBounds(drawingContext, text, textBounds, comboBox.Enabled ? foreground : SystemColors.GrayTextBrush, 12);
        }

        Rect buttonBounds = new(Math.Max(bounds.X, bounds.Right - 18), bounds.Y + 1, 17, Math.Max(0, bounds.Height - 2));
        drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), buttonBounds);
        Point p1 = new(buttonBounds.X + 5, buttonBounds.Y + Math.Max(6, buttonBounds.Height / 2 - 1));
        Point p2 = new(buttonBounds.X + 12, p1.Y);
        Point p3 = new(buttonBounds.X + 8.5, p1.Y + 4);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(p1, true, true);
            context.LineTo(p2, true, false);
            context.LineTo(p3, true, false);
        }
        drawingContext.DrawGeometry(SystemColors.ControlTextBrush, null, geometry);
    }

    private void RenderListBox(DrawingContext drawingContext, Forms.ListBox listBox, Rect bounds, Brush foreground, bool checkedItems)
    {
        DrawBorder(drawingContext, listBox.BorderStyle, bounds);
        ResetPortablePaintSurfacePool(listBox);
        const double lineHeight = 18;
        double y = bounds.Y + 2;
        for (int i = 0; i < listBox.Items.Count && y < bounds.Bottom; i++)
        {
            Rect rowBounds = new(bounds.X + 1, y, Math.Max(0, bounds.Width - 2), lineHeight);
            bool selected = i == listBox.SelectedIndex;
            if (selected)
            {
                drawingContext.DrawRectangle(SystemColors.HighlightBrush, null, rowBounds);
            }

            Forms.CheckedListBox? checkedListBox = checkedItems
                ? listBox as Forms.CheckedListBox
                : null;
            double textX = rowBounds.X + 4 + (checkedListBox != null ? 18 : 0);

            Rect itemTextBounds = new(textX, rowBounds.Y + 1, Math.Max(0, rowBounds.Right - textX - 2), lineHeight - 2);
            bool ownerDrawn = listBox.DrawMode != Forms.DrawMode.Normal
                && TryRenderListItemOwnerDraw(drawingContext, listBox, i, bounds, rowBounds);
            if (!ownerDrawn)
            {
                string text = listBox.Items[i]?.ToString() ?? string.Empty;
                DrawTextInBounds(
                    drawingContext,
                    text,
                    itemTextBounds,
                    selected ? SystemColors.HighlightTextBrush : foreground,
                    12);
            }

            if (checkedListBox != null)
            {
                Rect checkBounds = new(rowBounds.X + 4, rowBounds.Y + 3, 12, 12);
                drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlDarkBrush, 1), checkBounds);
                if (checkedListBox.GetItemChecked(i))
                {
                    DrawText(drawingContext, "x", new Point(checkBounds.X + 2, checkBounds.Y - 1), SystemColors.ControlTextBrush, 11);
                }
            }

            y += lineHeight;
        }
    }

    private bool TryRenderListItemOwnerDraw(
        DrawingContext drawingContext,
        Forms.ListBox listBox,
        int index,
        Rect controlBounds,
        Rect itemBounds)
    {
        DrawingRectangle drawBounds = new(
            (int)Math.Round(itemBounds.X - controlBounds.X),
            (int)Math.Round(itemBounds.Y - controlBounds.Y),
            Math.Max(1, (int)Math.Ceiling(itemBounds.Width)),
            Math.Max(1, (int)Math.Ceiling(itemBounds.Height)));

        Forms.DrawItemState state = Forms.DrawItemState.None;
        if (index == listBox.SelectedIndex)
        {
            state |= Forms.DrawItemState.Selected;
        }

        if (!listBox.Enabled)
        {
            state |= Forms.DrawItemState.Disabled;
        }

        drawingContext.PushClip(new RectangleGeometry(itemBounds));
        try
        {
            if (TryGetNativeDrawingContext(
                    drawingContext,
                    out ProGPU.Scene.DrawingContext nativeContext,
                    out Matrix4x4 outerTransform))
            {
                Matrix4x4 clientTransform = Matrix4x4.CreateTranslation(
                        (float)controlBounds.X,
                        (float)controlBounds.Y,
                        0f)
                    * outerTransform;
                using DrawingGraphics graphics = DrawingGraphics.FromProGpuDrawingContext(nativeContext, clientTransform);
                RaiseListItemOwnerDraw(listBox, graphics, drawBounds, index, state);
            }
            else
            {
                int surfaceWidth = Math.Max(1, (int)Math.Ceiling(controlBounds.Width));
                int surfaceHeight = Math.Max(1, drawBounds.Height);
                PortablePaintSurface surface = GetPortablePaintSurfacePool(listBox).AcquireNext(surfaceWidth, surfaceHeight);
                if (surface.Source == null)
                {
                    return false;
                }

                using (DrawingGraphics graphics = DrawingGraphics.FromImage(surface.Bitmap))
                {
                    graphics.Clear(DrawingColor.Transparent);
                    graphics.TranslateTransform(0, -drawBounds.Y);
                    RaiseListItemOwnerDraw(listBox, graphics, drawBounds, index, state);
                }

                drawingContext.DrawImage(
                    surface.Source,
                    new Rect(controlBounds.X, itemBounds.Y, surfaceWidth, surfaceHeight));
            }

            Interlocked.Increment(ref _portableOwnerDrawDispatchCount);
            return true;
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    private static void RaiseListItemOwnerDraw(
        Forms.ListBox listBox,
        DrawingGraphics graphics,
        DrawingRectangle drawBounds,
        int index,
        Forms.DrawItemState state)
    {
        Forms.DrawItemEventArgs eventArgs = new(graphics, listBox.Font, drawBounds, index, state);
        listBox.RaiseDrawItem(eventArgs);
    }

    private void RenderPropertyGrid(DrawingContext drawingContext, Forms.PropertyGrid propertyGrid, Rect bounds, Brush foreground)
    {
        DrawBorder(drawingContext, Forms.BorderStyle.Fixed3D, bounds);

        double y = bounds.Y + 1;
        if (propertyGrid.ToolbarVisible)
        {
            Rect toolbarBounds = new(bounds.X + 1, y, Math.Max(0, bounds.Width - 2), 22);
            drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), toolbarBounds);
            string selectionText = propertyGrid.SelectedObject?.GetType().Name ?? "Properties";
            if (propertyGrid.SelectedObjectCount > 1)
            {
                selectionText += " (" + propertyGrid.SelectedObjectCount.ToString(CultureInfo.CurrentCulture) + ")";
            }

            DrawTextInBounds(drawingContext, selectionText, new Rect(toolbarBounds.X + 5, toolbarBounds.Y + 3, Math.Max(0, toolbarBounds.Width - 10), 17), foreground, 12);
            y = toolbarBounds.Bottom;
        }

        double helpHeight = propertyGrid.HelpVisible ? Math.Min(42, Math.Max(0, bounds.Height * 0.18)) : 0;
        double rowsBottom = Math.Max(y, bounds.Bottom - helpHeight);
        double nameWidth = Math.Max(70, Math.Min(bounds.Width * 0.48, bounds.Width - 80));
        const double rowHeight = 18;

        foreach (Forms.PropertyGridDisplayRow row in propertyGrid.DisplayRows)
        {
            if (y + rowHeight > rowsBottom)
            {
                break;
            }

            Rect rowBounds = new(bounds.X + 1, y, Math.Max(0, bounds.Width - 2), rowHeight);
            if (row.IsCategory)
            {
                drawingContext.DrawRectangle(SystemColors.ControlLightBrush, new Pen(SystemColors.ControlDarkBrush, 1), rowBounds);
                DrawTextInBounds(drawingContext, row.Label, new Rect(rowBounds.X + 4, rowBounds.Y + 1, Math.Max(0, rowBounds.Width - 8), rowHeight - 2), foreground, 12);
            }
            else
            {
                drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlLightBrush, 1), rowBounds);
                Rect nameBounds = new(rowBounds.X + 4, rowBounds.Y + 1, Math.Max(0, nameWidth - 6), rowHeight - 2);
                Rect valueBounds = new(rowBounds.X + nameWidth + 4, rowBounds.Y + 1, Math.Max(0, rowBounds.Width - nameWidth - 8), rowHeight - 2);
                drawingContext.DrawLine(new Pen(SystemColors.ControlLightBrush, 1), new Point(rowBounds.X + nameWidth, rowBounds.Y), new Point(rowBounds.X + nameWidth, rowBounds.Bottom));
                DrawTextInBounds(drawingContext, row.Label, nameBounds, foreground, 12);
                DrawTextInBounds(drawingContext, row.ValueText, valueBounds, foreground, 12);
            }

            y += rowHeight;
        }

        if (helpHeight > 0)
        {
            Rect helpBounds = new(bounds.X + 1, rowsBottom, Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Bottom - rowsBottom - 1));
            drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), helpBounds);
            string helpText = propertyGrid.SelectedGridItem?.PropertyDescriptor?.Description ?? string.Empty;
            if (string.IsNullOrEmpty(helpText))
            {
                helpText = propertyGrid.SelectedGridItem?.Label ?? string.Empty;
            }

            DrawTextInBounds(drawingContext, helpText, new Rect(helpBounds.X + 5, helpBounds.Y + 4, Math.Max(0, helpBounds.Width - 10), Math.Max(0, helpBounds.Height - 8)), foreground, 11);
        }
    }

    private void RenderDataGridView(DrawingContext drawingContext, Forms.DataGridView dataGridView, Rect bounds, Brush foreground)
    {
        DrawBorder(drawingContext, Forms.BorderStyle.Fixed3D, bounds);

        DrawingRectangle topLeft = dataGridView.GetCellDisplayRectangle(-1, -1, cutOverflow: true);
        if (topLeft.Width > 0 && topLeft.Height > 0)
        {
            drawingContext.DrawRectangle(
                SystemColors.ControlBrush,
                new Pen(SystemColors.ControlDarkBrush, 1),
                OffsetDataGridViewRectangle(bounds, topLeft));
        }

        for (int columnIndex = 0; columnIndex < dataGridView.Columns.Count; columnIndex++)
        {
            Forms.DataGridViewColumn column = dataGridView.Columns[columnIndex];
            DrawingRectangle displayRectangle = dataGridView.GetCellDisplayRectangle(columnIndex, -1, cutOverflow: true);
            if (displayRectangle.Width <= 0 || displayRectangle.Height <= 0)
            {
                break;
            }

            Rect headerBounds = OffsetDataGridViewRectangle(bounds, displayRectangle);
            drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), headerBounds);
            string header = string.IsNullOrEmpty(column.HeaderText) ? column.Name : column.HeaderText;
            DrawTextInBounds(
                drawingContext,
                header,
                new Rect(headerBounds.X + 4, headerBounds.Y + 3, Math.Max(0, headerBounds.Width - 8), Math.Max(0, headerBounds.Height - 4)),
                foreground,
                12);
        }

        for (int rowIndex = 0; rowIndex < dataGridView.Rows.Count; rowIndex++)
        {
            Forms.DataGridViewRow row = dataGridView.Rows[rowIndex];
            DrawingRectangle rowHeaderRectangle = dataGridView.GetCellDisplayRectangle(-1, rowIndex, cutOverflow: true);
            DrawingRectangle firstCellRectangle = dataGridView.Columns.Count > 0
                ? dataGridView.GetCellDisplayRectangle(0, rowIndex, cutOverflow: true)
                : rowHeaderRectangle;
            if (firstCellRectangle.Height <= 0)
            {
                break;
            }

            if (rowHeaderRectangle.Width > 0 && rowHeaderRectangle.Height > 0)
            {
                Rect rowHeaderBounds = OffsetDataGridViewRectangle(bounds, rowHeaderRectangle);
                drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlLightBrush, 1), rowHeaderBounds);
                DrawTextInBounds(
                    drawingContext,
                    (rowIndex + 1).ToString(CultureInfo.CurrentCulture),
                    new Rect(rowHeaderBounds.X + 3, rowHeaderBounds.Y + 2, Math.Max(0, rowHeaderBounds.Width - 6), Math.Max(0, rowHeaderBounds.Height - 4)),
                    foreground,
                    11);
            }

            for (int columnIndex = 0; columnIndex < dataGridView.Columns.Count; columnIndex++)
            {
                DrawingRectangle displayRectangle = dataGridView.GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: true);
                if (displayRectangle.Width <= 0 || displayRectangle.Height <= 0)
                {
                    break;
                }

                Rect cellBounds = OffsetDataGridViewRectangle(bounds, displayRectangle);
                bool current = ReferenceEquals(dataGridView.CurrentCell, columnIndex < row.Cells.Count ? row.Cells[columnIndex] : null);
                drawingContext.DrawRectangle(current ? SystemColors.HighlightBrush : SystemColors.WindowBrush, new Pen(SystemColors.ControlLightBrush, 1), cellBounds);
                string text = columnIndex < row.Cells.Count ? Convert.ToString(row.Cells[columnIndex].Value, CultureInfo.CurrentCulture) ?? string.Empty : string.Empty;
                DrawTextInBounds(
                    drawingContext,
                    text,
                    new Rect(cellBounds.X + 4, cellBounds.Y + 2, Math.Max(0, cellBounds.Width - 8), Math.Max(0, cellBounds.Height - 4)),
                    current ? SystemColors.HighlightTextBrush : foreground,
                    12);
            }
        }
    }

    private static Rect OffsetDataGridViewRectangle(Rect bounds, DrawingRectangle rectangle)
    {
        return new Rect(
            bounds.X + rectangle.X,
            bounds.Y + rectangle.Y,
            rectangle.Width,
            rectangle.Height);
    }

    private void RenderListView(DrawingContext drawingContext, Forms.ListView listView, Rect bounds, Brush foreground)
    {
        DrawBorder(drawingContext, listView.BorderStyle, bounds);

        const double headerHeight = 20;
        bool showDetails = listView.View == Forms.View.Details;

        if (showDetails && listView.HeaderStyle != Forms.ColumnHeaderStyle.None)
        {
            double x = bounds.X + 1;
            double y = bounds.Y + 1;
            drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), new Rect(bounds.X + 1, y, Math.Max(0, bounds.Width - 2), headerHeight));
            foreach (Forms.ColumnHeader column in listView.Columns)
            {
                double width = column.Width > 0 ? column.Width : 120;
                Rect headerBounds = new(x, y, width, headerHeight);
                drawingContext.DrawLine(new Pen(SystemColors.ControlDarkBrush, 1), new Point(headerBounds.Right, headerBounds.Y), new Point(headerBounds.Right, headerBounds.Bottom));
                double textInset = 4;
                if (!string.IsNullOrEmpty(column.ImageKey)
                    && TryGetImageListImageSource(listView.SmallImageList, -1, column.ImageKey, out ImageSource? headerImageSource)
                    && headerImageSource != null)
                {
                    double imageWidth = Math.Min(16, Math.Max(0, headerBounds.Width - 8));
                    double imageHeight = Math.Min(16, headerHeight - 4);
                    drawingContext.DrawImage(headerImageSource, new Rect(headerBounds.X + 4, headerBounds.Y + 2, imageWidth, imageHeight));
                    textInset += imageWidth + 3;
                }

                DrawTextInBounds(drawingContext, column.Text, new Rect(headerBounds.X + textInset, headerBounds.Y + 3, Math.Max(0, headerBounds.Width - textInset - 4), headerHeight - 4), foreground, 12);
                x += width;
                if (x > bounds.Right)
                {
                    break;
                }
            }
        }

        Rect visibleBounds = new(bounds.X + 1, bounds.Y + 1, Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - 2));
        for (int itemIndex = 0; itemIndex < listView.Items.Count; itemIndex++)
        {
            Forms.ListViewItem item = listView.Items[itemIndex];
            DrawingRectangle localItemBounds = listView.GetItemRect(itemIndex);
            Rect itemBounds = new(
                bounds.X + localItemBounds.X,
                bounds.Y + localItemBounds.Y,
                localItemBounds.Width,
                localItemBounds.Height);
            if (!itemBounds.IntersectsWith(visibleBounds))
            {
                continue;
            }

            bool selected = item.Selected || listView.SelectedItems.Contains(item);
            if (selected)
            {
                drawingContext.DrawRectangle(SystemColors.HighlightBrush, null, itemBounds);
            }
            else if (listView.GridLines)
            {
                drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlLightBrush, 1), itemBounds);
            }

            double contentLeft = itemBounds.X + 4;
            if (listView.CheckBoxes)
            {
                int localCheckTop = listView.View == Forms.View.LargeIcon
                    ? localItemBounds.Top + 4
                    : localItemBounds.Top + Math.Max(0, (localItemBounds.Height - 12) / 2);
                DrawingRectangle localCheckBounds = new(localItemBounds.Left + 4, localCheckTop, 12, 12);
                Rect checkBounds = new(
                    bounds.X + localCheckBounds.X,
                    bounds.Y + localCheckBounds.Y,
                    localCheckBounds.Width,
                    localCheckBounds.Height);
                drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlDarkBrush, 1), checkBounds);
                if (item.Checked)
                {
                    DrawText(drawingContext, "x", new Point(checkBounds.X + 2, checkBounds.Y - 1), SystemColors.ControlTextBrush, 11);
                }

                if (listView.View != Forms.View.LargeIcon)
                {
                    contentLeft = checkBounds.Right + 4;
                }
            }

            Forms.ImageList? itemImageList = listView.View is Forms.View.LargeIcon or Forms.View.Tile
                ? listView.LargeImageList ?? listView.SmallImageList
                : listView.SmallImageList ?? listView.LargeImageList;
            bool hasImage = TryGetImageListImageSource(itemImageList, item.ImageIndex, null, out ImageSource? imageSource)
                && imageSource != null;

            if (listView.View == Forms.View.LargeIcon)
            {
                double imageWidth = hasImage ? Math.Min(itemImageList!.ImageSize.Width, Math.Max(0, itemBounds.Width - 12)) : 0;
                double imageHeight = hasImage ? Math.Min(itemImageList!.ImageSize.Height, Math.Max(0, itemBounds.Height - 26)) : 0;
                if (hasImage)
                {
                    double imageX = itemBounds.X + Math.Max(4, (itemBounds.Width - imageWidth) / 2);
                    drawingContext.DrawImage(imageSource!, new Rect(imageX, itemBounds.Y + 4, imageWidth, imageHeight));
                }

                Rect textBounds = new(itemBounds.X + 4, itemBounds.Y + 8 + imageHeight, Math.Max(0, itemBounds.Width - 8), Math.Max(0, itemBounds.Height - imageHeight - 10));
                double textWidth = MeasureText(item.Text, 12);
                double textX = textBounds.X + Math.Max(0, (textBounds.Width - textWidth) / 2);
                DrawTextInBounds(drawingContext, item.Text, new Rect(textX, textBounds.Y, Math.Max(0, textBounds.Right - textX), textBounds.Height), selected ? SystemColors.HighlightTextBrush : foreground, 12);
                continue;
            }

            if (hasImage)
            {
                double imageWidth = Math.Min(itemImageList!.ImageSize.Width, Math.Max(0, itemBounds.Right - contentLeft - 4));
                double imageHeight = Math.Min(itemImageList.ImageSize.Height, Math.Max(0, itemBounds.Height - 4));
                double imageY = itemBounds.Y + Math.Max(2, (itemBounds.Height - imageHeight) / 2);
                drawingContext.DrawImage(imageSource!, new Rect(contentLeft, imageY, imageWidth, imageHeight));
                contentLeft += imageWidth + 4;
            }

            if (showDetails)
            {
                double x = itemBounds.X;
                int columnCount = Math.Max(listView.Columns.Count, item.SubItems.Count);
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    double width = columnIndex < listView.Columns.Count && listView.Columns[columnIndex].Width > 0
                        ? listView.Columns[columnIndex].Width
                        : 120;
                    string text = columnIndex < item.SubItems.Count ? item.SubItems[columnIndex].Text : string.Empty;
                    if (columnIndex == 0 && string.IsNullOrEmpty(text))
                    {
                        text = item.Text;
                    }

                    double cellTextLeft = columnIndex == 0 ? contentLeft : x + 4;
                    Rect cellBounds = new(cellTextLeft, itemBounds.Y + 1, Math.Max(0, x + width - cellTextLeft - 4), itemBounds.Height - 2);
                    DrawTextInBounds(drawingContext, text, cellBounds, selected ? SystemColors.HighlightTextBrush : foreground, 12);
                    x += width;
                    if (x > bounds.Right)
                    {
                        break;
                    }
                }
            }
            else
            {
                DrawTextInBounds(
                    drawingContext,
                    item.Text,
                    new Rect(contentLeft, itemBounds.Y + 2, Math.Max(0, itemBounds.Right - contentLeft - 4), itemBounds.Height - 4),
                    selected ? SystemColors.HighlightTextBrush : foreground,
                    12);
            }
        }
    }

    private bool TryGetImageListImageSource(
        Forms.ImageList? imageList,
        int imageIndex,
        string? imageKey,
        out ImageSource? imageSource)
    {
        imageSource = null;
        if (imageList == null || imageList.Images.Count == 0)
        {
            return false;
        }

        DrawingImage? image = !string.IsNullOrEmpty(imageKey)
            ? imageList.Images[imageKey]
            : imageIndex >= 0 && imageIndex < imageList.Images.Count
                ? imageList.Images[imageIndex]
                : null;
        if (image == null)
        {
            return false;
        }

        CachedImageSource cached = _imageSourceCache.GetValue(image, static key => new CachedImageSource(CreateImageSource(key)));
        imageSource = cached.Source;
        return imageSource != null;
    }

    private void RenderToolStrip(DrawingContext drawingContext, Forms.ToolStrip toolStrip, Rect bounds, Brush foreground)
    {
        drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), bounds);
        int itemIndex = 0;
        double x = bounds.X + 4;
        while (TryGetNextMainToolStripItem(toolStrip, bounds, ref itemIndex, ref x, out Forms.ToolStripItem item, out Rect itemBounds))
        {
            if (item is Forms.ToolStripSeparator)
            {
                double separatorX = itemBounds.X + (itemBounds.Width / 2);
                drawingContext.DrawLine(
                    new Pen(SystemColors.ControlDarkBrush, 1),
                    new Point(separatorX, bounds.Y + 4),
                    new Point(separatorX, bounds.Bottom - 4));
                continue;
            }

            if (item.Selected)
            {
                drawingContext.DrawRectangle(SystemColors.HighlightBrush, null, itemBounds);
            }

            Brush itemForeground = item.Enabled ? foreground : SystemColors.GrayTextBrush;
            if (item is Forms.ToolStripComboBox comboBox)
            {
                RenderToolStripComboBox(drawingContext, comboBox, itemBounds, itemForeground);
            }
            else if (item is Forms.ToolStripControlHost controlHost)
            {
                RenderToolStripControlHost(drawingContext, controlHost, itemBounds, itemForeground);
            }
            else
            {
                RenderToolStripButtonLikeItem(drawingContext, item, itemBounds, itemForeground);
            }
        }
    }

    private bool TryGetNextMainToolStripItem(
        Forms.ToolStrip toolStrip,
        Rect stripBounds,
        ref int itemIndex,
        ref double x,
        out Forms.ToolStripItem item,
        out Rect itemBounds)
    {
        while (itemIndex < toolStrip.Items.Count)
        {
            Forms.ToolStripItem candidate = toolStrip.Items[itemIndex++];
            if (!candidate.Visible
                || !candidate.Available
                || candidate.Overflow == Forms.ToolStripItemOverflow.Always)
            {
                continue;
            }

            double width = GetToolStripItemWidth(candidate);
            Rect candidateBounds = new(
                x,
                stripBounds.Y + 2,
                width,
                Math.Max(0, stripBounds.Height - 4));
            if (candidate.Overflow == Forms.ToolStripItemOverflow.AsNeeded
                && candidateBounds.Right > stripBounds.Right - 4)
            {
                continue;
            }

            x = candidateBounds.Right + 2;
            item = candidate;
            itemBounds = candidateBounds;
            return true;
        }

        item = null!;
        itemBounds = Rect.Empty;
        return false;
    }

    private double GetToolStripItemWidth(Forms.ToolStripItem item)
    {
        int configuredWidth = item.Width > 0 ? item.Width : item.Size.Width;
        if (configuredWidth > 0)
        {
            return configuredWidth;
        }

        if (item is Forms.ToolStripSeparator)
        {
            return 8;
        }

        if (item is Forms.ToolStripComboBox comboBox)
        {
            string selectedText = comboBox.SelectedItem?.ToString() ?? comboBox.Text;
            return Math.Max(80, MeasureText(selectedText, 12) + 28);
        }

        if (item is Forms.ToolStripControlHost controlHost)
        {
            return Math.Max(24, controlHost.Control.Width > 0 ? controlHost.Control.Width : 80);
        }

        string text = GetToolStripItemText(item);
        bool showImage = item.Image != null
            && item.DisplayStyle is Forms.ToolStripItemDisplayStyle.Image or Forms.ToolStripItemDisplayStyle.ImageAndText;
        bool showText = item.DisplayStyle is Forms.ToolStripItemDisplayStyle.Text or Forms.ToolStripItemDisplayStyle.ImageAndText;
        double contentWidth = showText ? MeasureText(text, 12) : 0;
        if (showImage)
        {
            contentWidth += 16 + (showText && !string.IsNullOrEmpty(text) ? 4 : 0);
        }

        return Math.Max(18, contentWidth + 12);
    }

    private static string GetToolStripItemText(Forms.ToolStripItem item)
    {
        return string.IsNullOrEmpty(item.Text) ? item.Name : item.Text;
    }

    private void RenderToolStripComboBox(
        DrawingContext drawingContext,
        Forms.ToolStripComboBox comboBox,
        Rect bounds,
        Brush foreground)
    {
        drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlDarkBrush, 1), bounds);
        double arrowWidth = Math.Min(18, Math.Max(12, bounds.Width / 3));
        Rect textBounds = new(bounds.X + 4, bounds.Y + 2, Math.Max(0, bounds.Width - arrowWidth - 6), Math.Max(0, bounds.Height - 4));
        string text = comboBox.SelectedItem?.ToString() ?? comboBox.Text;
        DrawTextInBounds(drawingContext, text, textBounds, foreground, 12);

        double arrowCenterX = bounds.Right - (arrowWidth / 2);
        double arrowCenterY = bounds.Top + (bounds.Height / 2);
        var arrow = new StreamGeometry();
        using (StreamGeometryContext context = arrow.Open())
        {
            context.BeginFigure(new Point(arrowCenterX - 3, arrowCenterY - 1), isFilled: true, isClosed: true);
            context.LineTo(new Point(arrowCenterX + 3, arrowCenterY - 1), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(arrowCenterX, arrowCenterY + 3), isStroked: true, isSmoothJoin: false);
        }

        drawingContext.DrawGeometry(foreground, null, arrow);
    }

    private void RenderToolStripControlHost(
        DrawingContext drawingContext,
        Forms.ToolStripControlHost controlHost,
        Rect bounds,
        Brush foreground)
    {
        Forms.Control control = controlHost.Control;
        if (control is Forms.ProgressBar progressBar)
        {
            drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlDarkBrush, 1), bounds);
            int range = Math.Max(1, progressBar.Maximum - progressBar.Minimum);
            double progress = Math.Clamp((progressBar.Value - progressBar.Minimum) / (double)range, 0, 1);
            Rect progressBounds = new(
                bounds.X + 2,
                bounds.Y + 2,
                Math.Max(0, (bounds.Width - 4) * progress),
                Math.Max(0, bounds.Height - 4));
            drawingContext.DrawRectangle(SystemColors.HighlightBrush, null, progressBounds);
            return;
        }

        if (control is Forms.NumericUpDown numericUpDown)
        {
            drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlDarkBrush, 1), bounds);
            DrawTextInBounds(
                drawingContext,
                numericUpDown.Value.ToString(CultureInfo.CurrentCulture),
                new Rect(bounds.X + 4, bounds.Y + 2, Math.Max(0, bounds.Width - 22), Math.Max(0, bounds.Height - 4)),
                foreground,
                12);
            double buttonLeft = bounds.Right - 18;
            drawingContext.DrawLine(new Pen(SystemColors.ControlDarkBrush, 1), new Point(buttonLeft, bounds.Top), new Point(buttonLeft, bounds.Bottom));
            drawingContext.DrawLine(new Pen(SystemColors.ControlDarkBrush, 1), new Point(buttonLeft, bounds.Top + (bounds.Height / 2)), new Point(bounds.Right, bounds.Top + (bounds.Height / 2)));
            DrawSpinnerArrow(drawingContext, new Point(buttonLeft + 9, bounds.Top + (bounds.Height / 4)), up: true, foreground);
            DrawSpinnerArrow(drawingContext, new Point(buttonLeft + 9, bounds.Top + ((bounds.Height * 3) / 4)), up: false, foreground);
            return;
        }

        drawingContext.DrawRectangle(
            CreateBrush(control.BackColor, SystemColors.WindowBrush),
            new Pen(SystemColors.ControlDarkBrush, 1),
            bounds);
        DrawTextInBounds(
            drawingContext,
            control.Text,
            new Rect(bounds.X + 4, bounds.Y + 2, Math.Max(0, bounds.Width - 8), Math.Max(0, bounds.Height - 4)),
            foreground,
            12);
    }

    private static void DrawSpinnerArrow(DrawingContext drawingContext, Point center, bool up, Brush foreground)
    {
        double direction = up ? -1 : 1;
        var arrow = new StreamGeometry();
        using (StreamGeometryContext context = arrow.Open())
        {
            context.BeginFigure(new Point(center.X - 3, center.Y + (2 * direction)), isFilled: true, isClosed: true);
            context.LineTo(new Point(center.X + 3, center.Y + (2 * direction)), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(center.X, center.Y - (2 * direction)), isStroked: true, isSmoothJoin: false);
        }

        drawingContext.DrawGeometry(foreground, null, arrow);
    }

    private void RenderToolStripButtonLikeItem(
        DrawingContext drawingContext,
        Forms.ToolStripItem item,
        Rect bounds,
        Brush foreground)
    {
        string text = GetToolStripItemText(item);
        bool showImage = item.Image != null
            && item.DisplayStyle is Forms.ToolStripItemDisplayStyle.Image or Forms.ToolStripItemDisplayStyle.ImageAndText;
        bool showText = item.DisplayStyle is Forms.ToolStripItemDisplayStyle.Text or Forms.ToolStripItemDisplayStyle.ImageAndText;
        double contentLeft = bounds.X + 6;
        if (showImage && item.Image != null)
        {
            CachedImageSource cached = _imageSourceCache.GetValue(item.Image, static key => new CachedImageSource(CreateImageSource(key)));
            if (cached.Source != null)
            {
                double imageSize = Math.Min(16, Math.Max(0, bounds.Height - 4));
                drawingContext.DrawImage(
                    cached.Source,
                    new Rect(contentLeft, bounds.Y + ((bounds.Height - imageSize) / 2), imageSize, imageSize));
                contentLeft += imageSize + 4;
            }
        }

        if (showText)
        {
            DrawTextInBounds(
                drawingContext,
                text,
                new Rect(contentLeft, bounds.Y + 2, Math.Max(0, bounds.Right - contentLeft - 4), Math.Max(0, bounds.Height - 4)),
                foreground,
                12);
        }
    }

    private void RenderTreeView(DrawingContext drawingContext, Forms.TreeView treeView, Rect bounds, Brush foreground)
    {
        DrawBorder(drawingContext, treeView.BorderStyle, bounds);
        ResetPortablePaintSurfacePool(treeView);

        Forms.TreeNodeLayoutEnumerator layouts = treeView.GetVisibleNodeLayouts().GetEnumerator();
        while (layouts.MoveNext())
        {
            Forms.TreeNodeLayout layout = layouts.Current;
            Rect rowBounds = TranslateTreeNodeBounds(bounds, layout.RowBounds);
            if (rowBounds.Bottom <= bounds.Top + 1)
            {
                continue;
            }

            if (rowBounds.Top >= bounds.Bottom - 1)
            {
                break;
            }

            RenderTreeNode(drawingContext, treeView, layout, bounds, rowBounds, foreground);
        }
    }

    private void RenderTreeNode(
        DrawingContext drawingContext,
        Forms.TreeView treeView,
        Forms.TreeNodeLayout layout,
        Rect bounds,
        Rect rowBounds,
        Brush foreground)
    {
        Forms.TreeNode node = layout.Node;
        double lineHeight = layout.RowBounds.Height;
        Forms.TreeNodeStates state = GetTreeNodeState(treeView, node);
        DrawingRectangle ownerAllBounds = layout.OwnerDrawBounds;

        bool ownerDrawAllDefault = true;
        if (treeView.DrawMode == Forms.TreeViewDrawMode.OwnerDrawAll)
        {
            node.Bounds = ownerAllBounds;
            TryRenderTreeNodeOwnerDraw(
                drawingContext,
                treeView,
                node,
                bounds,
                ownerAllBounds,
                lineHeight,
                state,
                out ownerDrawAllDefault);
        }

        if (treeView.DrawMode != Forms.TreeViewDrawMode.OwnerDrawAll || ownerDrawAllDefault)
        {
            if (!layout.GlyphBounds.IsEmpty)
            {
                DrawTreeNodeGlyph(
                    drawingContext,
                    TranslateTreeNodeBounds(bounds, layout.GlyphBounds),
                    node.IsExpanded,
                    foreground);
            }

            if (!layout.ImageBounds.IsEmpty
                && TryGetTreeNodeImageSource(treeView, node, out ImageSource? imageSource))
            {
                drawingContext.DrawImage(imageSource, TranslateTreeNodeBounds(bounds, layout.ImageBounds));
            }

            DrawingRectangle textBounds = layout.TextBounds;
            node.Bounds = textBounds;

            bool ownerDrawTextDefault = true;
            if (treeView.DrawMode == Forms.TreeViewDrawMode.OwnerDrawText)
            {
                TryRenderTreeNodeOwnerDraw(
                    drawingContext,
                    treeView,
                    node,
                    bounds,
                    textBounds,
                    lineHeight,
                    state,
                    out ownerDrawTextDefault);
            }

            if (treeView.DrawMode != Forms.TreeViewDrawMode.OwnerDrawText || ownerDrawTextDefault)
            {
                bool selected = ReferenceEquals(treeView.SelectedNode, node);
                if (selected)
                {
                    drawingContext.DrawRectangle(
                        SystemColors.HighlightBrush,
                        null,
                        TranslateTreeNodeBounds(bounds, layout.SelectionBounds));
                }

                Rect translatedTextBounds = TranslateTreeNodeBounds(bounds, textBounds);
                DrawText(
                    drawingContext,
                    node.Text,
                    new Point(translatedTextBounds.X, translatedTextBounds.Y + 1),
                    selected ? SystemColors.HighlightTextBrush : foreground,
                    12);
            }

        }
    }

    private static Rect TranslateTreeNodeBounds(Rect treeBounds, DrawingRectangle nodeBounds)
    {
        return new Rect(
            treeBounds.X + nodeBounds.X,
            treeBounds.Y + nodeBounds.Y,
            nodeBounds.Width,
            nodeBounds.Height);
    }

    private static void DrawTreeNodeGlyph(DrawingContext drawingContext, Rect hitBounds, bool expanded, Brush foreground)
    {
        const double boxSize = 9;
        double boxLeft = hitBounds.X + Math.Max(0, (hitBounds.Width - boxSize) / 2);
        double boxTop = hitBounds.Y + Math.Max(0, (hitBounds.Height - boxSize) / 2);
        var box = new Rect(boxLeft, boxTop, boxSize, boxSize);
        var pen = new Pen(foreground, 1);
        drawingContext.DrawRectangle(null, pen, box);
        double centerX = boxLeft + (boxSize / 2);
        double centerY = boxTop + (boxSize / 2);
        drawingContext.DrawLine(pen, new Point(boxLeft + 2, centerY), new Point(box.Right - 2, centerY));
        if (!expanded)
        {
            drawingContext.DrawLine(pen, new Point(centerX, boxTop + 2), new Point(centerX, box.Bottom - 2));
        }
    }

    private static Forms.TreeNodeStates GetTreeNodeState(Forms.TreeView treeView, Forms.TreeNode node)
    {
        Forms.TreeNodeStates state = Forms.TreeNodeStates.Default;
        if (node.Checked)
        {
            state |= Forms.TreeNodeStates.Checked;
        }

        if (ReferenceEquals(treeView.SelectedNode, node))
        {
            state |= Forms.TreeNodeStates.Selected;
            if (treeView.Focused || treeView.ContainsFocus)
            {
                state |= Forms.TreeNodeStates.Focused;
            }
        }

        return state;
    }

    private bool TryRenderTreeNodeOwnerDraw(
        DrawingContext drawingContext,
        Forms.TreeView treeView,
        Forms.TreeNode node,
        Rect treeBounds,
        DrawingRectangle eventBounds,
        double lineHeight,
        Forms.TreeNodeStates state,
        out bool drawDefault)
    {
        drawDefault = true;
        Rect rowClip = new(
            treeBounds.X,
            treeBounds.Y + eventBounds.Y,
            Math.Max(0, treeBounds.Width),
            Math.Max(0, lineHeight));
        drawingContext.PushClip(new RectangleGeometry(rowClip));
        try
        {
            if (TryGetNativeDrawingContext(
                    drawingContext,
                    out ProGPU.Scene.DrawingContext nativeContext,
                    out Matrix4x4 outerTransform))
            {
                Matrix4x4 clientTransform = Matrix4x4.CreateTranslation(
                        (float)treeBounds.X,
                        (float)treeBounds.Y,
                        0f)
                    * outerTransform;
                using DrawingGraphics graphics = DrawingGraphics.FromProGpuDrawingContext(nativeContext, clientTransform);
                drawDefault = RaiseTreeNodeOwnerDraw(treeView, graphics, node, eventBounds, state);
            }
            else
            {
                int surfaceWidth = Math.Max(1, (int)Math.Ceiling(treeBounds.Width));
                int surfaceHeight = Math.Max(1, (int)Math.Ceiling(lineHeight));
                PortablePaintSurface surface = GetPortablePaintSurfacePool(treeView).AcquireNext(surfaceWidth, surfaceHeight);
                if (surface.Source == null)
                {
                    return false;
                }

                using (DrawingGraphics graphics = DrawingGraphics.FromImage(surface.Bitmap))
                {
                    graphics.Clear(DrawingColor.Transparent);
                    graphics.TranslateTransform(0, -eventBounds.Y);
                    drawDefault = RaiseTreeNodeOwnerDraw(treeView, graphics, node, eventBounds, state);
                }

                drawingContext.DrawImage(
                    surface.Source,
                    new Rect(treeBounds.X, rowClip.Y, surfaceWidth, surfaceHeight));
            }

            Interlocked.Increment(ref _portableOwnerDrawDispatchCount);
            return true;
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    private static bool RaiseTreeNodeOwnerDraw(
        Forms.TreeView treeView,
        DrawingGraphics graphics,
        Forms.TreeNode node,
        DrawingRectangle eventBounds,
        Forms.TreeNodeStates state)
    {
        Forms.DrawTreeNodeEventArgs eventArgs = new(graphics, node, eventBounds)
        {
            State = state
        };
        treeView.RaiseDrawNode(eventArgs);
        return eventArgs.DrawDefault;
    }

    private bool TryGetTreeNodeImageSource(Forms.TreeView treeView, Forms.TreeNode node, out ImageSource? imageSource)
    {
        imageSource = null;
        Forms.ImageList? imageList = treeView.ImageList;
        if (imageList == null || imageList.Images.Count == 0)
        {
            return false;
        }

        DrawingImage? image = TryGetTreeNodeImage(treeView, node, imageList);
        if (image == null)
        {
            return false;
        }

        CachedImageSource cached = _imageSourceCache.GetValue(image, static key => new CachedImageSource(CreateImageSource(key)));
        imageSource = cached.Source;
        return imageSource != null;
    }

    private static DrawingImage? TryGetTreeNodeImage(Forms.TreeView treeView, Forms.TreeNode node, Forms.ImageList imageList)
    {
        bool selected = ReferenceEquals(treeView.SelectedNode, node);
        string key = selected ? node.SelectedImageKey : node.ImageKey;
        if (!string.IsNullOrEmpty(key))
        {
            DrawingImage? keyedImage = imageList.Images[key];
            if (keyedImage != null)
            {
                return keyedImage;
            }
        }

        int index = selected ? node.SelectedImageIndex : node.ImageIndex;
        if (index < 0)
        {
            index = selected ? treeView.SelectedImageIndex : treeView.ImageIndex;
        }

        return index >= 0 && index < imageList.Images.Count ? imageList.Images[index] : null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible",
        Justification = "The GPU-direct carrier and pixel fallback are different ImageSource implementations.")]
    private static ImageSource? CreateImageSource(DrawingImage image)
    {
        if (image is IProGpuTextureSource textureSource)
        {
            return PortableNativeImageSourceFactory.Create(
                new ProGpuDrawingImageSource(image, textureSource));
        }

        return CreatePixelImageSource(image);
    }

    private static WriteableBitmap? CreatePixelImageSource(DrawingImage image)
    {
        DrawingBitmap? bitmap = image as DrawingBitmap;
        bool ownsBitmap = false;
        if (bitmap == null)
        {
            bitmap = new DrawingBitmap(image);
            ownsBitmap = true;
        }

        try
        {
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppPArgb);
            try
            {
                int byteCount = Math.Abs(data.Stride) * data.Height;
                byte[] pixels = new byte[byteCount];
                Marshal.Copy(data.Scan0, pixels, 0, byteCount);

                var source = new WriteableBitmap(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Pbgra32, null);
                GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    source.WritePixels(new Int32Rect(0, 0, bitmap.Width, bitmap.Height), handle.AddrOfPinnedObject(), byteCount, data.Stride);
                }
                finally
                {
                    handle.Free();
                }

                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        finally
        {
            if (ownsBitmap)
            {
                bitmap.Dispose();
            }
        }
    }

    private sealed class ProGpuDrawingImageSource : IPortableNativeImageSource
    {
        private readonly DrawingImage _image;
        private readonly IProGpuTextureSource _textureSource;

        public ProGpuDrawingImageSource(DrawingImage image, IProGpuTextureSource textureSource)
        {
            _image = image;
            _textureSource = textureSource;
        }

        public int PixelWidth => _image.Width;

        public int PixelHeight => _image.Height;

        public bool TryGetPortableNativeImage(out object? nativeImage)
        {
            if (_textureSource.TryGetGpuTexture(out GpuTexture texture))
            {
                nativeImage = texture;
                return true;
            }

            nativeImage = null;
            return false;
        }
    }

    private PortablePaintSurfacePool GetPortablePaintSurfacePool(Forms.Control control)
    {
        if (!_portablePaintSurfacePools.TryGetValue(control, out PortablePaintSurfacePool? pool))
        {
            pool = new PortablePaintSurfacePool();
            _portablePaintSurfacePools.Add(control, pool);
        }

        return pool;
    }

    private PortablePaintSurfacePool GetPortableDesignerAdornerSurfacePool(Forms.Control control)
    {
        if (!_portableDesignerAdornerSurfacePools.TryGetValue(control, out PortablePaintSurfacePool? pool))
        {
            pool = new PortablePaintSurfacePool();
            _portableDesignerAdornerSurfacePools.Add(control, pool);
        }

        return pool;
    }

    private bool TryCreateHostedControlGraphics(
        Forms.Control control,
        out DrawingGraphics graphics)
    {
        if (control.IsDisposed)
        {
            graphics = null!;
            return false;
        }

        int width = Math.Max(1, control.ClientSize.Width);
        int height = Math.Max(1, control.ClientSize.Height);
        if (!_createGraphicsSurfacePools.TryGetValue(control, out PortablePaintSurfacePool? pool))
        {
            pool = new PortablePaintSurfacePool();
            _createGraphicsSurfacePools.Add(control, pool);
        }

        PortablePaintSurface surface = pool.AcquireFixed(width, height);
        surface.MarkForPresentation();
        graphics = DrawingGraphics.FromImage(surface.Bitmap);
        Interlocked.Increment(ref _portableCreateGraphicsDispatchCount);
        control.Invalidate();
        return true;
    }

    private void RenderCreateGraphicsSurface(
        DrawingContext drawingContext,
        Forms.Control control,
        Rect bounds)
    {
        if (!_createGraphicsSurfacePools.TryGetValue(control, out PortablePaintSurfacePool? pool)
            || !pool.TryGetFixed(out PortablePaintSurface surface)
            || !surface.HasPresentationContent
            || surface.Source == null)
        {
            return;
        }

        drawingContext.DrawImage(surface.Source, bounds);
    }

    private void ResetPortablePaintSurfacePool(Forms.Control control)
    {
        if (_portablePaintSurfacePools.TryGetValue(control, out PortablePaintSurfacePool? pool))
        {
            pool.ResetSequence();
        }
    }

    private void RetirePortablePaintSurfacePool(Forms.Control control)
    {
        if (_portablePaintSurfacePools.Remove(control, out PortablePaintSurfacePool? pool))
        {
            _pendingRetiredPaintSurfacePools.Add(pool);
        }

        if (_portableDesignerAdornerSurfacePools.Remove(control, out pool))
        {
            _pendingRetiredPaintSurfacePools.Add(pool);
        }

        if (_createGraphicsSurfacePools.Remove(control, out pool))
        {
            _pendingRetiredPaintSurfacePools.Add(pool);
        }
    }

    private void AdvanceRetiredPortablePaintSurfaces()
    {
        foreach (PortablePaintSurfacePool pool in _safeRetiredPaintSurfacePools)
        {
            pool.Dispose();
        }

        _safeRetiredPaintSurfacePools.Clear();
        _safeRetiredPaintSurfacePools.AddRange(_pendingRetiredPaintSurfacePools);
        _pendingRetiredPaintSurfacePools.Clear();
    }

    private void DisposePortablePaintSurfaces()
    {
        foreach (PortablePaintSurfacePool pool in _portablePaintSurfacePools.Values)
        {
            pool.Dispose();
        }

        foreach (PortablePaintSurfacePool pool in _createGraphicsSurfacePools.Values)
        {
            pool.Dispose();
        }

        foreach (PortablePaintSurfacePool pool in _portableDesignerAdornerSurfacePools.Values)
        {
            pool.Dispose();
        }

        foreach (PortablePaintSurfacePool pool in _pendingRetiredPaintSurfacePools)
        {
            pool.Dispose();
        }

        foreach (PortablePaintSurfacePool pool in _safeRetiredPaintSurfacePools)
        {
            pool.Dispose();
        }

        _portablePaintSurfacePools.Clear();
        _portableDesignerAdornerSurfacePools.Clear();
        _createGraphicsSurfacePools.Clear();
        _pendingRetiredPaintSurfacePools.Clear();
        _safeRetiredPaintSurfacePools.Clear();
    }

    private sealed class PortablePaintSurfacePool : IDisposable
    {
        private readonly List<PortablePaintSurface> _surfaces = new();
        private readonly List<PortablePaintSurface> _retiredSurfaces = new();
        private int _nextSurfaceIndex;
        private bool _isDisposed;

        public PortablePaintSurface AcquireFixed(int width, int height)
        {
            return Acquire(0, width, height);
        }

        public PortablePaintSurface AcquireNext(int width, int height)
        {
            return Acquire(_nextSurfaceIndex++, width, height);
        }

        public bool TryGetFixed(out PortablePaintSurface surface)
        {
            if (_surfaces.Count != 0)
            {
                surface = _surfaces[0];
                return true;
            }

            surface = null!;
            return false;
        }

        public void ResetSequence()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _nextSurfaceIndex = 0;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            foreach (PortablePaintSurface surface in _surfaces)
            {
                surface.Dispose();
            }

            foreach (PortablePaintSurface surface in _retiredSurfaces)
            {
                surface.Dispose();
            }

            _surfaces.Clear();
            _retiredSurfaces.Clear();
        }

        private PortablePaintSurface Acquire(int index, int width, int height)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (index < _surfaces.Count)
            {
                PortablePaintSurface current = _surfaces[index];
                if (current.Width == width && current.Height == height)
                {
                    return current;
                }

                _retiredSurfaces.Add(current);
                PortablePaintSurface replacement = new(width, height);
                _surfaces[index] = replacement;
                return replacement;
            }

            while (_surfaces.Count < index)
            {
                _surfaces.Add(new PortablePaintSurface(1, 1));
            }

            PortablePaintSurface surface = new(width, height);
            _surfaces.Add(surface);
            return surface;
        }
    }

    private sealed class PortablePaintSurface : IDisposable
    {
        private bool _hasPresentationContent;

        public PortablePaintSurface(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            Bitmap = new DrawingBitmap(Width, Height, DrawingPixelFormat.Format32bppPArgb);
            Source = CreateImageSource(Bitmap);
        }

        public DrawingBitmap Bitmap { get; }

        public int Height { get; }

        public ImageSource? Source { get; }

        public int Width { get; }

        public bool HasPresentationContent
            => _hasPresentationContent;

        public void MarkForPresentation()
        {
            _hasPresentationContent = true;
        }

        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }

    private sealed class CachedImageSource
    {
        public CachedImageSource(ImageSource? source)
        {
            Source = source;
        }

        public ImageSource? Source { get; }
    }

    private static void DrawBorder(DrawingContext drawingContext, Forms.BorderStyle borderStyle, Rect bounds)
    {
        if (borderStyle == Forms.BorderStyle.None || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (borderStyle == Forms.BorderStyle.FixedSingle)
        {
            drawingContext.DrawRectangle(null, new Pen(SystemColors.WindowFrameBrush, 1), bounds);
            return;
        }

        drawingContext.DrawRectangle(null, new Pen(SystemColors.ControlDarkDarkBrush, 1), bounds);
        if (bounds.Width > 2 && bounds.Height > 2)
        {
            Rect innerBounds = new(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2);
            drawingContext.DrawRectangle(null, new Pen(SystemColors.ControlLightLightBrush, 1), innerBounds);
        }
    }

    private Brush CreateBrush(System.Drawing.Color color, Brush fallback)
    {
        if (color.IsEmpty)
        {
            return fallback;
        }

        return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
    }

    private void DrawText(DrawingContext drawingContext, string text, Point origin, Brush brush, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        FormattedText formatted = CreateFormattedText(text, brush, fontSize);
        drawingContext.DrawText(formatted, origin);
    }

    private void DrawTextInBounds(DrawingContext drawingContext, string text, Rect bounds, Brush brush, double fontSize)
    {
        if (string.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        drawingContext.PushClip(new RectangleGeometry(bounds));
        try
        {
            DrawText(drawingContext, text, new Point(bounds.X, bounds.Y), brush, fontSize);
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    private double MeasureText(string text, double fontSize)
    {
        return string.IsNullOrEmpty(text) ? 0 : CreateFormattedText(text, Brushes.Black, fontSize).WidthIncludingTrailingWhitespace;
    }

    private FormattedText CreateFormattedText(string text, Brush brush, double fontSize)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal),
            fontSize,
            brush,
            pixelsPerDip);
    }
}

public sealed class WindowsFormsHostAutomationPeer : FrameworkElementAutomationPeer
{
    public WindowsFormsHostAutomationPeer(WindowsFormsHost owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Pane;
    }

    protected override string GetClassNameCore()
    {
        return nameof(WindowsFormsHost);
    }
}
