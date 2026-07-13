using System.ComponentModel;
using System.Drawing;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows.Forms;

public interface IWin32Window
{
    IntPtr Handle { get; }
}

public delegate void MethodInvoker();

public delegate void ControlEventHandler(object? sender, ControlEventArgs e);

public class Control : Component, IWin32Window, ISynchronizeInvoke, IPortableWinFormsPaintSource
{
    private static long s_nextHandle = 0x10000;
    private static readonly object s_handleSync = new();
    private static readonly Dictionary<IntPtr, Control> s_controlsByHandle = new();

    private sealed class PortableControlAsyncResult : IAsyncResult
    {
        private readonly Control _owner;
        private readonly IWinFormsDispatcherHost? _dispatcherHost;
        private readonly Delegate _method;
        private readonly object?[]? _args;
        private readonly ManualResetEventSlim _completion = new(initialState: false);
        private System.Runtime.ExceptionServices.ExceptionDispatchInfo? _exception;
        private object? _result;
        private int _executionState;
        private int _executingThreadId;

        public PortableControlAsyncResult(
            Control owner,
            IWinFormsDispatcherHost? dispatcherHost,
            Delegate method,
            object?[]? args)
        {
            _owner = owner;
            _dispatcherHost = dispatcherHost;
            _method = method;
            _args = args;
        }

        public object? AsyncState => null;

        public WaitHandle AsyncWaitHandle => _completion.WaitHandle;

        public bool CompletedSynchronously => false;

        public bool IsCompleted => Volatile.Read(ref _executionState) == 2;

        public bool IsOwnedBy(Control owner) => ReferenceEquals(_owner, owner);

        public bool HasDispatcherAccess => _dispatcherHost?.CheckAccess() == true;

        public void TryExecute()
        {
            if (Interlocked.CompareExchange(ref _executionState, 1, 0) != 0)
            {
                return;
            }

            Volatile.Write(ref _executingThreadId, Environment.CurrentManagedThreadId);
            try
            {
                _result = InvokePortableDelegate(_method, _args);
            }
            catch (Exception exception)
            {
                _exception = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                Volatile.Write(ref _executingThreadId, 0);
                Volatile.Write(ref _executionState, 2);
                _completion.Set();
            }
        }

        public object? GetResult()
        {
            if (!IsCompleted)
            {
                if (Volatile.Read(ref _executingThreadId) == Environment.CurrentManagedThreadId)
                {
                    throw new InvalidOperationException(
                        "EndInvoke cannot wait for the callback that is currently executing on this thread.");
                }

                _completion.Wait();
            }

            _exception?.Throw();
            return _result;
        }
    }

    private bool _isHandleCreated;
    private IntPtr _handle;
    private Point _location;
    private Size _size;
    private bool _visible = true;
    private bool _enabled = true;
    private bool _focused;
    private ControlStyles _controlStyles;
    private long _portablePaintVersion;
    private MouseEventHandler? _designerMouseDown;
    private MouseEventHandler? _designerMouseMove;
    private MouseEventHandler? _designerMouseUp;

    public static bool CheckForIllegalCrossThreadCalls { get; set; }

    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;
    public event EventHandler? SizeChanged;
    public event EventHandler? Resize;
    public event EventHandler? Enter;
    public event EventHandler? Leave;
    public event EventHandler? Click;
    public event EventHandler? DoubleClick;
    public event MouseEventHandler? MouseClick;
    public event MouseEventHandler? MouseDown;
    public event EventHandler? MouseLeave;
    public event MouseEventHandler? MouseMove;
    public event MouseEventHandler? MouseUp;
    public event MouseEventHandler? MouseWheel;
    public event MouseEventHandler? MouseDoubleClick;
    public event KeyEventHandler? KeyDown;
    public event KeyEventHandler? KeyUp;
    public event KeyPressEventHandler? KeyPress;
    public event PreviewKeyDownEventHandler? PreviewKeyDown;
    public event PaintEventHandler? Paint;
    public event EventHandler? TextChanged;
    public event EventHandler? VisibleChanged;
    public event EventHandler? LocationChanged;
    public event EventHandler? HandleCreated;
    public event EventHandler? Invalidated;
    public event ControlEventHandler? ControlAdded;
    public event ControlEventHandler? ControlRemoved;
    public event CancelEventHandler? Validating;
    public event EventHandler? Validated;
    public event DragEventHandler? DragDrop;
    public event DragEventHandler? DragEnter;
    public event EventHandler? DragLeave;
    public event DragEventHandler? DragOver;

    public virtual bool AutoSize { get; set; }

    public bool AllowDrop { get; set; }

    public AnchorStyles Anchor { get; set; } = AnchorStyles.Top | AnchorStyles.Left;

    public Color BackColor { get; set; } = SystemColors.Control;

    public Image? BackgroundImage { get; set; }

    public virtual Rectangle Bounds
    {
        get => new(Location, Size);
        set
        {
            Location = value.Location;
            Size = value.Size;
        }
    }

    public virtual bool Capture { get; set; }

    public virtual bool CanFocus => CanSelect;

    public virtual bool CanSelect
    {
        get
        {
            if (IsDisposed || !Enabled || !Visible)
            {
                return false;
            }

            for (Control? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ancestor.IsDisposed || !ancestor.Enabled || !ancestor.Visible)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public bool CausesValidation { get; set; } = true;

    public virtual Rectangle ClientRectangle => new(Point.Empty, Size);

    public ControlCollection Controls { get; }

    public ControlBindingsCollection DataBindings { get; }

    public virtual Rectangle DisplayRectangle => ClientRectangle;

    public DockStyle Dock { get; set; }

    public virtual bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            Invalidate();
            Parent?.Invalidate();
        }
    }

    public virtual bool Focused => _focused;

    public virtual bool ContainsFocus => Focused || Controls.Any(static control => control.ContainsFocus);

    public Font Font { get; set; } = SystemFonts.DefaultFont;

    public static Font DefaultFont => SystemFonts.DefaultFont;

    public Cursor Cursor { get; set; } = Cursors.Default;

    public Color ForeColor { get; set; } = SystemColors.ControlText;

    public IntPtr Handle
    {
        get => EnsureHandle();
        protected set
        {
            _handle = value;
            if (_handle != IntPtr.Zero && !_isHandleCreated)
            {
                OnHandleCreated(EventArgs.Empty);
            }
        }
    }

    public virtual int Height
    {
        get => Size.Height;
        set => Size = new Size(Size.Width, value);
    }

    public virtual int Left
    {
        get => Location.X;
        set => Location = new Point(value, Location.Y);
    }

    public virtual Point Location
    {
        get => _location;
        set
        {
            if (_location == value)
            {
                return;
            }

            _location = value;
            LocationChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            Parent?.Invalidate();
        }
    }

    public Padding Margin { get; set; } = Padding.Empty;

    public Padding Padding { get; set; } = Padding.Empty;

    public DockPaddingEdges DockPadding { get; } = new();

    public Size MaximumSize { get; set; }

    public Size MinimumSize { get; set; }

    public string Name { get; set; } = string.Empty;

    public Control? Parent { get; internal set; }

    public RightToLeft RightToLeft { get; set; }

    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool ResizeRedraw { get; set; }

    public virtual Size Size
    {
        get => _size;
        set
        {
            Size normalized = new(
                Math.Max(0, value.Width),
                Math.Max(0, value.Height));
            if (_size == normalized)
            {
                return;
            }

            _size = normalized;
            OnResize(EventArgs.Empty);
            Invalidate();
            Parent?.Invalidate();
        }
    }

    public virtual Size ClientSize
    {
        get => Size;
        set => Size = value;
    }

    public object? Tag { get; set; }

    public int TabIndex { get; set; }

    public bool TabStop { get; set; } = true;

    private string _text = string.Empty;

    public virtual string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            OnTextChanged(EventArgs.Empty);
        }
    }

    public ContextMenuStrip? ContextMenuStrip { get; set; }

    public virtual int Top
    {
        get => Location.Y;
        set => Location = new Point(Location.X, value);
    }

    public virtual bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            OnVisibleChanged(EventArgs.Empty);
            Parent?.Invalidate();
        }
    }

    public virtual int Width
    {
        get => Size.Width;
        set => Size = new Size(value, Size.Height);
    }

    public bool IsDisposed { get; private set; }

    public virtual bool IsHandleCreated => _isHandleCreated || _handle != IntPtr.Zero;

    public static Keys ModifierKeys { get; set; }

    public bool InvokeRequired => Application.GetDispatcherHost() is { } dispatcherHost
        && !dispatcherHost.CheckAccess();

    internal Size DefaultSizeForDesigner => DefaultSize;

    protected virtual Size DefaultSize => Size.Empty;

    public Control()
    {
        Controls = new ControlCollection(this);
        DataBindings = new ControlBindingsCollection(this);
    }

    internal void AddDesignerMouseHandlers(
        MouseEventHandler mouseDown,
        MouseEventHandler mouseMove,
        MouseEventHandler mouseUp)
    {
        _designerMouseDown += mouseDown;
        _designerMouseMove += mouseMove;
        _designerMouseUp += mouseUp;
    }

    internal void RemoveDesignerMouseHandlers(
        MouseEventHandler mouseDown,
        MouseEventHandler mouseMove,
        MouseEventHandler mouseUp)
    {
        _designerMouseDown -= mouseDown;
        _designerMouseMove -= mouseMove;
        _designerMouseUp -= mouseUp;
    }

    public DragDropEffects DoDragDrop(object data, DragDropEffects allowedEffects)
    {
        ArgumentNullException.ThrowIfNull(data);

        const DragDropEffects validEffects =
            DragDropEffects.Copy |
            DragDropEffects.Move |
            DragDropEffects.Link |
            DragDropEffects.Scroll;
        if ((allowedEffects & ~validEffects) != DragDropEffects.None)
        {
            throw new InvalidEnumArgumentException(
                nameof(allowedEffects),
                (int)allowedEffects,
                typeof(DragDropEffects));
        }

        if (allowedEffects == DragDropEffects.None)
        {
            return DragDropEffects.None;
        }

        IDataObject dataObject = data as IDataObject ?? new DataObject(data);
        return Application.DoDragDrop(this, dataObject, allowedEffects);
    }

    public virtual bool Focus()
    {
        if (!CanFocus)
        {
            return false;
        }

        ContainerControl? container = FindForm();
        if (container == null)
        {
            for (Control? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ancestor is ContainerControl candidate)
                {
                    container = candidate;
                }
            }
        }

        if (container != null && !ReferenceEquals(container, this))
        {
            return container.TryActivateControl(this);
        }

        SetFocusedState(true);
        return true;
    }

    public virtual void Select()
    {
        Focus();
    }

    public void BringToFront()
    {
        if (Parent == null)
        {
            return;
        }

        Parent.Controls.SetChildIndex(this, Parent.Controls.Count - 1);
    }

    public void SendToBack()
    {
        Parent?.Controls.SetChildIndex(this, 0);
    }

    public void RaiseMouseDown(MouseEventArgs e)
    {
        if (Site?.DesignMode == true && _designerMouseDown is not null)
        {
            _designerMouseDown(this, e);
            return;
        }

        OnMouseDown(e);
    }

    public void RaiseMouseMove(MouseEventArgs e)
    {
        if (Site?.DesignMode == true && _designerMouseMove is not null)
        {
            _designerMouseMove(this, e);
            return;
        }

        OnMouseMove(e);
    }

    public void RaiseMouseUp(MouseEventArgs e)
    {
        if (Site?.DesignMode == true && _designerMouseUp is not null)
        {
            _designerMouseUp(this, e);
            return;
        }

        OnMouseUp(e);
    }

    public void RaiseMouseClick(MouseEventArgs e)
    {
        if (!CanSelect)
        {
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            OnClick(e);
        }

        OnMouseClick(e);
    }

    public void RaiseMouseDoubleClick(MouseEventArgs e)
    {
        OnMouseDoubleClick(e);
        OnDoubleClick(EventArgs.Empty);
    }

    public void RaiseMouseWheel(MouseEventArgs e)
    {
        OnMouseWheel(e);
    }

    public void RaiseDragEnter(DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!AllowDrop)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        OnDragEnter(e);
    }

    public void RaiseDragOver(DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!AllowDrop)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        OnDragOver(e);
    }

    public void RaiseDragLeave(EventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        OnDragLeave(e);
    }

    public void RaiseDragDrop(DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!AllowDrop)
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        OnDragDrop(e);
    }

    public void RaisePaintBackground(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        OnPaintBackground(e);
    }

    public void RaisePaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        OnPaint(e);
    }

    public void RaiseKeyDown(KeyEventArgs e)
    {
        OnKeyDown(e);
    }

    public void RaiseKeyUp(KeyEventArgs e)
    {
        OnKeyUp(e);
    }

    public void RaiseKeyPress(KeyPressEventArgs e)
    {
        OnKeyPress(e);
    }

    public IAsyncResult BeginInvoke(Delegate method, params object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);

        IWinFormsDispatcherHost? dispatcherHost = Application.GetDispatcherHost();
        var asyncResult = new PortableControlAsyncResult(this, dispatcherHost, method, args);
        if (dispatcherHost != null)
        {
            dispatcherHost.BeginInvoke(asyncResult.TryExecute);
        }
        else
        {
            ThreadPool.QueueUserWorkItem(
                static state => ((PortableControlAsyncResult)state!).TryExecute(),
                asyncResult,
                preferLocal: false);
        }

        return asyncResult;
    }

    /// <summary>
    /// Strongly typed single-argument dispatcher path used by source-built
    /// applications such as SharpDevelop. The closure keeps this common
    /// WinForms callback shape off the transitional DynamicInvoke fallback.
    /// </summary>
    public IAsyncResult BeginInvoke<T>(Action<T> method, T argument)
    {
        ArgumentNullException.ThrowIfNull(method);
        return BeginInvoke((Action)(() => method(argument)));
    }

    public object? EndInvoke(IAsyncResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result is not PortableControlAsyncResult asyncResult || !asyncResult.IsOwnedBy(this))
        {
            throw new ArgumentException(
                "The asynchronous result was not created by BeginInvoke on this control.",
                nameof(result));
        }

        // Pump this callback directly when EndInvoke is called on its dispatcher
        // before the posted operation runs. The queued callback becomes a no-op.
        if (!asyncResult.IsCompleted && asyncResult.HasDispatcherAccess)
        {
            asyncResult.TryExecute();
        }

        return asyncResult.GetResult();
    }

    public object? Invoke(Delegate method)
    {
        return Invoke(method, null);
    }

    public object? Invoke(Delegate method, params object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);

        IWinFormsDispatcherHost? dispatcherHost = Application.GetDispatcherHost();
        if (dispatcherHost == null || dispatcherHost.CheckAccess())
        {
            return InvokePortableDelegate(method, args);
        }

        object? result = null;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? exception = null;
        dispatcherHost.Invoke(
            () =>
            {
                try
                {
                    result = InvokePortableDelegate(method, args);
                }
                catch (Exception caught)
                {
                    exception = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught);
                }
            });
        exception?.Throw();
        return result;
    }

    /// <summary>
    /// Strongly typed synchronous counterpart to <see cref="BeginInvoke{T}(Action{T}, T)"/>.
    /// </summary>
    public void Invoke<T>(Action<T> method, T argument)
    {
        ArgumentNullException.ThrowIfNull(method);
        _ = Invoke((Action)(() => method(argument)));
    }

    private static object? InvokePortableDelegate(Delegate method, object?[]? args)
    {
        int argumentCount = args?.Length ?? 0;
        if (argumentCount == 0)
        {
            if (method is Action action)
            {
                action();
                return null;
            }

            if (method is MethodInvoker methodInvoker)
            {
                methodInvoker();
                return null;
            }

            if (method is Func<object?> objectFunction)
            {
                return objectFunction();
            }
        }
        else if (argumentCount == 2
            && method is EventHandler eventHandler
            && args![1] is EventArgs eventArgs)
        {
            eventHandler(args[0], eventArgs);
            return null;
        }

        // Transitional compatibility for arbitrary delegate signatures. Common
        // WinForms callbacks stay on the typed paths above; no reflected member
        // discovery or expression-built adapter is used by the dispatcher seam.
        return method.DynamicInvoke(args);
    }

    public virtual void CreateControl()
    {
        _ = EnsureHandle();
        foreach (Control child in Controls)
        {
            child.CreateControl();
        }
    }

    public virtual Graphics CreateGraphics()
    {
        if (Application.TryCreateGraphics(this, out Graphics graphics))
        {
            return graphics;
        }

        return Graphics.FromHwnd(Handle);
    }

    public static Control? FromChildHandle(IntPtr handle)
    {
        lock (s_handleSync)
        {
            return s_controlsByHandle.GetValueOrDefault(handle);
        }
    }

    public bool Contains(Control control)
    {
        foreach (Control child in Controls)
        {
            if (ReferenceEquals(child, control) || child.Contains(control))
            {
                return true;
            }
        }

        return false;
    }

    public Form? FindForm()
    {
        for (Control? current = this; current != null; current = current.Parent)
        {
            if (current is Form form)
            {
                return form;
            }
        }

        return null;
    }

    public Form? ParentForm => FindForm();

    internal bool TryValidateControl()
    {
        var validating = new CancelEventArgs();
        OnValidating(validating);
        if (validating.Cancel)
        {
            return false;
        }

        OnValidated(EventArgs.Empty);
        return true;
    }

    internal void SetFocusedState(bool value)
    {
        if (_focused == value)
        {
            return;
        }

        _focused = value;
        if (value)
        {
            OnEnter(EventArgs.Empty);
            OnGotFocus(EventArgs.Empty);
        }
        else
        {
            OnLostFocus(EventArgs.Empty);
            OnLeave(EventArgs.Empty);
        }

        Invalidate();
    }

    public virtual void Invalidate()
    {
        Interlocked.Increment(ref _portablePaintVersion);
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Invalidate(Rectangle rc)
    {
        Invalidate();
    }

    public Point PointToClient(Point p)
    {
        if (Application.TryPointToClient(this, p, out Point clientPoint))
        {
            return clientPoint;
        }

        int x = p.X;
        int y = p.Y;
        for (Control? current = this; current != null; current = current.Parent)
        {
            x -= current.Left;
            y -= current.Top;
            if (current.Parent is ScrollableControl scrollableParent && scrollableParent.AutoScroll)
            {
                Point displayOffset = scrollableParent.DisplayRectangle.Location;
                x -= displayOffset.X;
                y -= displayOffset.Y;
            }
        }

        return new Point(x, y);
    }

    public Point PointToScreen(Point p)
    {
        if (Application.TryPointToScreen(this, p, out Point screenPoint))
        {
            return screenPoint;
        }

        int x = p.X;
        int y = p.Y;
        for (Control? current = this; current != null; current = current.Parent)
        {
            x += current.Left;
            y += current.Top;
            if (current.Parent is ScrollableControl scrollableParent && scrollableParent.AutoScroll)
            {
                Point displayOffset = scrollableParent.DisplayRectangle.Location;
                x += displayOffset.X;
                y += displayOffset.Y;
            }
        }

        return new Point(x, y);
    }

    public virtual void Refresh()
    {
        Invalidate();
    }

    public virtual void Show()
    {
        Visible = true;
    }

    public virtual void Hide()
    {
        Visible = false;
    }

    public virtual void SuspendLayout()
    {
    }

    public virtual void ResumeLayout(bool performLayout)
    {
        if (performLayout)
        {
            PerformLayout();
        }
    }

    public virtual void ResumeLayout()
    {
        ResumeLayout(true);
    }

    public virtual void PerformLayout()
    {
    }

    public virtual void SetBounds(int x, int y, int width, int height)
    {
        Bounds = new Rectangle(x, y, width, height);
    }

    public void Scale(SizeF factor)
    {
        if (!float.IsFinite(factor.Width)
            || !float.IsFinite(factor.Height)
            || factor.Width < 0
            || factor.Height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor));
        }

        ScaleControl(factor, BoundsSpecified.All);
        if (ScaleChildren)
        {
            foreach (Control child in Controls)
            {
                child.Scale(factor);
            }
        }

        PerformLayout();
        Invalidate();
    }

    protected virtual bool ScaleChildren => true;

    protected virtual void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        Rectangle bounds = Bounds;
        int left = (specified & BoundsSpecified.X) != 0
            ? ScaleCoordinate(bounds.X, factor.Width)
            : bounds.X;
        int top = (specified & BoundsSpecified.Y) != 0
            ? ScaleCoordinate(bounds.Y, factor.Height)
            : bounds.Y;
        int width = (specified & BoundsSpecified.Width) != 0
            ? ScaleCoordinate(bounds.Width, factor.Width)
            : bounds.Width;
        int height = (specified & BoundsSpecified.Height) != 0
            ? ScaleCoordinate(bounds.Height, factor.Height)
            : bounds.Height;
        Bounds = new Rectangle(left, top, width, height);
    }

    private static int ScaleCoordinate(int value, float factor)
    {
        return checked((int)Math.Round(value * (double)factor, MidpointRounding.AwayFromZero));
    }

    protected virtual bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        return Parent?.ProcessCmdKey(ref msg, keyData) ?? false;
    }

    public virtual bool PreProcessMessage(ref Message msg)
    {
        const int WmKeyDown = 0x0100;
        const int WmSysKeyDown = 0x0104;

        if (msg.Msg != WmKeyDown && msg.Msg != WmSysKeyDown)
        {
            return false;
        }

        Keys keyCode = (Keys)unchecked((int)msg.WParam.ToInt64()) & Keys.KeyCode;
        return ProcessCmdKey(ref msg, keyCode | ModifierKeys);
    }

    protected virtual void OnDragDrop(DragEventArgs e)
    {
        DragDrop?.Invoke(this, e);
    }

    protected virtual void OnDragEnter(DragEventArgs e)
    {
        DragEnter?.Invoke(this, e);
    }

    protected virtual void OnDragLeave(EventArgs e)
    {
        DragLeave?.Invoke(this, e);
    }

    protected virtual void OnDragOver(DragEventArgs e)
    {
        DragOver?.Invoke(this, e);
    }

    protected virtual void OnHandleCreated(EventArgs e)
    {
        _isHandleCreated = true;
        HandleCreated?.Invoke(this, e);
    }

    private IntPtr EnsureHandle()
    {
        if (_handle == IntPtr.Zero)
        {
            _handle = new IntPtr(Interlocked.Increment(ref s_nextHandle));
            lock (s_handleSync)
            {
                s_controlsByHandle[_handle] = this;
            }
            if (!_isHandleCreated)
            {
                OnHandleCreated(EventArgs.Empty);
            }
        }

        return _handle;
    }

    protected virtual void OnMouseClick(MouseEventArgs e)
    {
        MouseClick?.Invoke(this, e);
    }

    protected virtual void OnClick(EventArgs e)
    {
        Click?.Invoke(this, e);
    }

    protected virtual void OnDoubleClick(EventArgs e)
    {
        DoubleClick?.Invoke(this, e);
    }

    protected virtual void OnMouseDown(MouseEventArgs e)
    {
        MouseDown?.Invoke(this, e);
    }

    protected virtual void OnMouseLeave(EventArgs e)
    {
        MouseLeave?.Invoke(this, e);
    }

    protected virtual void OnMouseMove(MouseEventArgs e)
    {
        MouseMove?.Invoke(this, e);
    }

    protected virtual void OnMouseUp(MouseEventArgs e)
    {
        MouseUp?.Invoke(this, e);
    }

    protected virtual void OnMouseWheel(MouseEventArgs e)
    {
        MouseWheel?.Invoke(this, e);
    }

    protected virtual void OnMouseDoubleClick(MouseEventArgs e)
    {
        MouseDoubleClick?.Invoke(this, e);
    }

    protected virtual void OnKeyPress(KeyPressEventArgs e)
    {
        KeyPress?.Invoke(this, e);
    }

    protected virtual void OnEnter(EventArgs e)
    {
        Enter?.Invoke(this, e);
    }

    protected virtual void OnGotFocus(EventArgs e)
    {
        GotFocus?.Invoke(this, e);
    }

    protected virtual void OnKeyDown(KeyEventArgs e)
    {
        KeyDown?.Invoke(this, e);
    }

    protected virtual void OnKeyUp(KeyEventArgs e)
    {
        KeyUp?.Invoke(this, e);
    }

    protected virtual void OnTextChanged(EventArgs e)
    {
        TextChanged?.Invoke(this, e);
    }

    protected virtual void OnPaint(PaintEventArgs e)
    {
        Paint?.Invoke(this, e);
    }

    protected virtual void OnVisibleChanged(EventArgs e)
    {
        VisibleChanged?.Invoke(this, e);
    }

    protected virtual void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected virtual void OnControlAdded(ControlEventArgs e)
    {
        ControlAdded?.Invoke(this, e);
    }

    protected virtual void OnControlRemoved(ControlEventArgs e)
    {
        ControlRemoved?.Invoke(this, e);
    }

    protected virtual void OnLostFocus(EventArgs e)
    {
        LostFocus?.Invoke(this, e);
    }

    protected virtual void OnLeave(EventArgs e)
    {
        Leave?.Invoke(this, e);
    }

    protected virtual void OnValidating(CancelEventArgs e)
    {
        Validating?.Invoke(this, e);
    }

    protected virtual void OnValidated(EventArgs e)
    {
        Validated?.Invoke(this, e);
    }

    protected virtual void OnSizeChanged(EventArgs e)
    {
        SizeChanged?.Invoke(this, e);
    }

    protected virtual void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
    {
        PreviewKeyDown?.Invoke(this, e);
    }

    protected virtual void OnResize(EventArgs e)
    {
        Resize?.Invoke(this, e);
        OnSizeChanged(e);
    }

    protected void SetStyle(ControlStyles flag, bool value)
    {
        ControlStyles next = value ? _controlStyles | flag : _controlStyles & ~flag;
        if (_controlStyles == next)
        {
            return;
        }

        _controlStyles = next;
        Invalidate();
    }

    protected bool GetStyle(ControlStyles flag)
    {
        return (_controlStyles & flag) == flag;
    }

    bool IPortableWinFormsPaintSource.SupportsPortablePainting => Paint != null
        || (_controlStyles & (ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer)) != 0;

    long IPortableWinFormsPaintSource.PortablePaintVersion => Interlocked.Read(ref _portablePaintVersion);

    void IPortableWinFormsPaintSource.PaintPortableBackground(PaintEventArgs e)
    {
        RaisePaintBackground(e);
    }

    void IPortableWinFormsPaintSource.PaintPortable(PaintEventArgs e)
    {
        RaisePaint(e);
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        if (_handle != IntPtr.Zero)
        {
            lock (s_handleSync)
            {
                s_controlsByHandle.Remove(_handle);
            }
        }

        base.Dispose(disposing);
    }

    public class ControlCollection : Collection<Control>
    {
        private readonly Control _owner;

        public ControlCollection(Control owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, Control item)
        {
            ArgumentNullException.ThrowIfNull(item);
            ValidateParentingCycle(item);

            if (ReferenceEquals(item.Parent, _owner))
            {
                int existingIndex = IndexOf(item);
                if (existingIndex >= 0)
                {
                    MoveExistingItem(existingIndex, index);
                    return;
                }

                // Repair an inconsistent parent pointer before attaching the
                // control to the owner's authoritative child collection.
                item.Parent = null;
            }
            else
            {
                item.Parent?.Controls.Remove(item);
            }

            base.InsertItem(index, item);
            item.Parent = _owner;
            if (item is RadioButton radioButton)
            {
                radioButton.PerformAutoUpdates();
            }

            if (_owner is TabControl tabControl && item is TabPage tabPage)
            {
                tabControl.RegisterControlTabPage(tabPage, index);
            }

            if (_owner.IsHandleCreated)
            {
                item.CreateControl();
            }

            _owner.OnControlAdded(new ControlEventArgs(item));
            _owner.Invalidate();
        }

        protected override void SetItem(int index, Control item)
        {
            ArgumentNullException.ThrowIfNull(item);
            ValidateParentingCycle(item);

            Control previous = this[index];
            if (ReferenceEquals(previous, item))
            {
                return;
            }

            if (ReferenceEquals(item.Parent, _owner))
            {
                int existingIndex = IndexOf(item);
                if (existingIndex >= 0)
                {
                    MoveExistingItem(existingIndex, index);
                    int previousIndex = IndexOf(previous);
                    if (previousIndex >= 0)
                    {
                        RemoveItem(previousIndex);
                    }

                    return;
                }

                item.Parent = null;
            }
            else
            {
                item.Parent?.Controls.Remove(item);
            }

            if (ReferenceEquals(previous.Parent, _owner))
            {
                previous.Parent = null;
            }

            base.SetItem(index, item);
            item.Parent = _owner;

            if (_owner is TabControl tabControl)
            {
                if (previous is TabPage previousPage)
                {
                    tabControl.UnregisterControlTabPage(previousPage);
                }

                if (item is TabPage nextPage)
                {
                    tabControl.RegisterControlTabPage(nextPage, index);
                }
            }

            if (_owner.IsHandleCreated)
            {
                item.CreateControl();
            }

            _owner.OnControlRemoved(new ControlEventArgs(previous));
            _owner.OnControlAdded(new ControlEventArgs(item));
            _owner.Invalidate();
        }

        protected override void RemoveItem(int index)
        {
            Control control = this[index];
            base.RemoveItem(index);
            if (ReferenceEquals(control.Parent, _owner))
            {
                control.Parent = null;
            }

            if (_owner is TabControl tabControl && control is TabPage tabPage)
            {
                tabControl.UnregisterControlTabPage(tabPage);
            }

            _owner.OnControlRemoved(new ControlEventArgs(control));
            _owner.Invalidate();
        }

        protected override void ClearItems()
        {
            TabPage[]? tabPages = _owner is TabControl
                ? this.OfType<TabPage>().ToArray()
                : null;
            Control[] removedControls = this.ToArray();
            foreach (Control control in removedControls)
            {
                if (ReferenceEquals(control.Parent, _owner))
                {
                    control.Parent = null;
                }
            }

            base.ClearItems();
            if (_owner is TabControl tabControl && tabPages != null)
            {
                tabControl.UnregisterControlTabPages(tabPages);
            }

            foreach (Control control in removedControls)
            {
                _owner.OnControlRemoved(new ControlEventArgs(control));
            }

            _owner.Invalidate();
        }

        public void SetChildIndex(Control child, int newIndex)
        {
            int oldIndex = IndexOf(child);
            if (oldIndex < 0)
            {
                return;
            }

            MoveExistingItem(oldIndex, newIndex);
        }

        public void AddRange(Control[] controls)
        {
            foreach (Control control in controls)
            {
                Add(control);
            }
        }

        private void MoveExistingItem(int oldIndex, int requestedIndex)
        {
            if (Count <= 1)
            {
                return;
            }

            int newIndex = Math.Clamp(requestedIndex, 0, Count - 1);
            if (oldIndex == newIndex)
            {
                return;
            }

            Control item = this[oldIndex];
            base.RemoveItem(oldIndex);
            base.InsertItem(newIndex, item);
            if (_owner is TabControl tabControl && item is TabPage tabPage)
            {
                tabControl.MoveControlTabPage(tabPage, newIndex);
            }

            _owner.Invalidate();
        }

        private void ValidateParentingCycle(Control item)
        {
            for (Control? ancestor = _owner; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, item))
                {
                    throw new ArgumentException("A control cannot be parented to itself or one of its descendants.", nameof(item));
                }
            }
        }
    }
}

public abstract class ScrollProperties
{
    private readonly ScrollableControl _owner;
    private readonly bool _horizontal;
    private int _minimum;
    private int _maximum = 100;
    private int _largeChange = 10;
    private int _smallChange = 1;
    private int _value;
    private bool _visible;

    internal ScrollProperties(ScrollableControl owner, bool horizontal)
    {
        _owner = owner;
        _horizontal = horizontal;
    }

    public bool Enabled { get; set; } = true;

    public int LargeChange
    {
        get
        {
            _owner.SynchronizeScrollProperties();
            return Math.Min(_largeChange, Math.Max(0, _maximum - _minimum + 1));
        }
        set
        {
            _largeChange = Math.Max(0, value);
            CoerceValue();
        }
    }

    public int Maximum
    {
        get
        {
            _owner.SynchronizeScrollProperties();
            return _maximum;
        }
        set
        {
            _maximum = Math.Max(_minimum, value);
            CoerceValue();
        }
    }

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = Math.Max(0, value);
            _maximum = Math.Max(_minimum, _maximum);
            CoerceValue();
        }
    }

    public int SmallChange
    {
        get => Math.Min(_smallChange, LargeChange);
        set => _smallChange = Math.Max(0, value);
    }

    public int Value
    {
        get
        {
            _owner.SynchronizeScrollProperties();
            return _value;
        }
        set => _owner.SetScrollValue(this, value);
    }

    public bool Visible
    {
        get
        {
            _owner.SynchronizeScrollProperties();
            return _visible;
        }
    }

    internal bool IsHorizontal => _horizontal;

    internal int ValueCore => _value;

    internal void ApplyMetrics(int maximum, int largeChange, bool visible)
    {
        _maximum = Math.Max(_minimum, maximum);
        _largeChange = Math.Max(0, largeChange);
        _visible = visible;
        CoerceValue();
    }

    internal int ClampValue(int value)
    {
        int effectiveLargeChange = Math.Min(_largeChange, Math.Max(0, _maximum - _minimum + 1));
        int maximumValue = Math.Max(_minimum, _maximum - Math.Max(0, effectiveLargeChange - 1));
        return Math.Clamp(value, _minimum, maximumValue);
    }

    internal bool SetValueCore(int value)
    {
        int next = ClampValue(value);
        if (_value == next)
        {
            return false;
        }

        _value = next;
        return true;
    }

    private void CoerceValue()
    {
        _value = ClampValue(_value);
    }
}

public sealed class HScrollProperties : ScrollProperties
{
    internal HScrollProperties(ScrollableControl owner)
        : base(owner, horizontal: true)
    {
    }
}

public sealed class VScrollProperties : ScrollProperties
{
    internal VScrollProperties(ScrollableControl owner)
        : base(owner, horizontal: false)
    {
    }
}

public class ScrollableControl : Control
{
    private readonly HScrollProperties _horizontalScroll;
    private readonly VScrollProperties _verticalScroll;
    private bool _autoScroll;
    private Size _autoScrollMargin;
    private Size _autoScrollMinSize;
    private bool _synchronizingScrollProperties;

    public ScrollableControl()
    {
        _horizontalScroll = new HScrollProperties(this);
        _verticalScroll = new VScrollProperties(this);
    }

    public event ScrollEventHandler? Scroll;

    public virtual bool AutoScroll
    {
        get => _autoScroll;
        set
        {
            if (_autoScroll == value)
            {
                return;
            }

            _autoScroll = value;
            SynchronizeScrollProperties();
            if (!value)
            {
                _horizontalScroll.SetValueCore(0);
                _verticalScroll.SetValueCore(0);
            }

            Invalidate();
        }
    }

    public Size AutoScrollMargin
    {
        get => _autoScrollMargin;
        set
        {
            Size normalized = new(Math.Max(0, value.Width), Math.Max(0, value.Height));
            if (_autoScrollMargin == normalized)
            {
                return;
            }

            _autoScrollMargin = normalized;
            SynchronizeScrollProperties();
            Invalidate();
        }
    }

    public Size AutoScrollMinSize
    {
        get => _autoScrollMinSize;
        set
        {
            Size normalized = new(Math.Max(0, value.Width), Math.Max(0, value.Height));
            if (_autoScrollMinSize == normalized)
            {
                return;
            }

            _autoScrollMinSize = normalized;
            SynchronizeScrollProperties();
            Invalidate();
        }
    }

    public Point AutoScrollPosition
    {
        get
        {
            SynchronizeScrollProperties();
            return new Point(-_horizontalScroll.ValueCore, -_verticalScroll.ValueCore);
        }
        set
        {
            HorizontalScroll.Value = Math.Abs(value.X);
            VerticalScroll.Value = Math.Abs(value.Y);
        }
    }

    public HScrollProperties HorizontalScroll => _horizontalScroll;

    public VScrollProperties VerticalScroll => _verticalScroll;

    public override Rectangle DisplayRectangle
    {
        get
        {
            SynchronizeScrollProperties();
            Size extent = GetScrollExtent();
            return new Rectangle(
                -_horizontalScroll.ValueCore,
                -_verticalScroll.ValueCore,
                Math.Max(ClientSize.Width, extent.Width),
                Math.Max(ClientSize.Height, extent.Height));
        }
    }

    internal Point ChildDisplayOffset
    {
        get
        {
            if (!AutoScroll)
            {
                return Point.Empty;
            }

            SynchronizeScrollProperties();
            return new Point(-_horizontalScroll.ValueCore, -_verticalScroll.ValueCore);
        }
    }

    internal void SynchronizeScrollProperties()
    {
        if (_synchronizingScrollProperties)
        {
            return;
        }

        _synchronizingScrollProperties = true;
        try
        {
            Size extent = AutoScroll ? GetScrollExtent() : ClientSize;
            int clientWidth = Math.Max(0, ClientSize.Width);
            int clientHeight = Math.Max(0, ClientSize.Height);
            bool horizontalVisible = AutoScroll && extent.Width > clientWidth;
            bool verticalVisible = AutoScroll && extent.Height > clientHeight;
            _horizontalScroll.ApplyMetrics(
                Math.Max(0, extent.Width - 1),
                clientWidth,
                horizontalVisible);
            _verticalScroll.ApplyMetrics(
                Math.Max(0, extent.Height - 1),
                clientHeight,
                verticalVisible);
        }
        finally
        {
            _synchronizingScrollProperties = false;
        }
    }

    internal void SetScrollValue(ScrollProperties properties, int value)
    {
        SynchronizeScrollProperties();
        int oldValue = properties.ValueCore;
        if (!properties.SetValueCore(value))
        {
            return;
        }

        Scroll?.Invoke(this, new ScrollEventArgs(ScrollEventType.ThumbPosition, oldValue, properties.ValueCore));
        Invalidate();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        SynchronizeScrollProperties();
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);
        SynchronizeScrollProperties();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SynchronizeScrollProperties();
    }

    private Size GetScrollExtent()
    {
        int width = Math.Max(ClientSize.Width, AutoScrollMinSize.Width);
        int height = Math.Max(ClientSize.Height, AutoScrollMinSize.Height);
        foreach (Control child in Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            width = Math.Max(width, child.Right + AutoScrollMargin.Width);
            height = Math.Max(height, child.Bottom + AutoScrollMargin.Height);
        }

        return new Size(Math.Max(0, width), Math.Max(0, height));
    }
}

public class ContainerControl : ScrollableControl
{
    private Control? _activeControl;

    public Control? ActiveControl
    {
        get => _activeControl;
        set
        {
            if (value != null && !ReferenceEquals(value, this) && !Contains(value))
            {
                throw new ArgumentException("The active control must be contained by this container.", nameof(value));
            }

            _ = TryActivateControl(value);
        }
    }

    public SizeF AutoScaleDimensions { get; set; }

    public AutoScaleMode AutoScaleMode { get; set; }

    public bool Validate()
    {
        return _activeControl?.TryValidateControl() ?? true;
    }

    public virtual bool ValidateChildren()
    {
        return ValidateChildrenCore(this);
    }

    internal bool TryActivateControl(Control? control)
    {
        if (ReferenceEquals(_activeControl, control))
        {
            return true;
        }

        if (control != null && !control.CanSelect)
        {
            return false;
        }

        Control? previous = _activeControl;
        if (previous != null
            && control?.CausesValidation == true
            && !previous.TryValidateControl())
        {
            return false;
        }

        previous?.SetFocusedState(false);
        _activeControl = control;
        control?.SetFocusedState(true);
        return true;
    }

    internal bool TryValidateForActivation(Control activator)
    {
        return !activator.CausesValidation
            || _activeControl == null
            || ReferenceEquals(_activeControl, activator)
            || _activeControl.TryValidateControl();
    }

    private static bool ValidateChildrenCore(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.TryValidateControl() || !ValidateChildrenCore(child))
            {
                return false;
            }
        }

        return true;
    }
}

public class Form : ContainerControl, IWinFormsDialogKeyProcessor
{
    private bool _shown;
    private bool _closed;
    private bool _isModal;
    private int _dialogDispatchDepth;
    private bool _completionRequestedDuringDispatch;
    private DialogResult _dialogResult;
    private IButtonControl? _acceptButton;
    private IButtonControl? _cancelButton;

    public event CancelEventHandler? Closing;
    public event FormClosingEventHandler? FormClosing;
    public event EventHandler? Closed;
    public event FormClosedEventHandler? FormClosed;
    public event EventHandler? Shown;

    public IButtonControl? AcceptButton
    {
        get => _acceptButton;
        set
        {
            if (ReferenceEquals(_acceptButton, value))
            {
                return;
            }

            _acceptButton?.NotifyDefault(false);
            _acceptButton = value;
            _acceptButton?.NotifyDefault(true);
        }
    }

    public IButtonControl? CancelButton
    {
        get => _cancelButton;
        set
        {
            _cancelButton = value;
            if (_cancelButton != null && _cancelButton.DialogResult == DialogResult.None)
            {
                _cancelButton.DialogResult = DialogResult.Cancel;
            }
        }
    }

    public Size ClientSize
    {
        get => Size;
        set => Size = value;
    }

    public DialogResult DialogResult
    {
        get => _dialogResult;
        set
        {
            int numericValue = (int)value;
            if (numericValue < (int)DialogResult.None
                || numericValue > (int)DialogResult.Continue
                || numericValue is 8 or 9)
            {
                throw new InvalidEnumArgumentException(nameof(value), numericValue, typeof(DialogResult));
            }

            if (_dialogResult == value)
            {
                return;
            }

            _dialogResult = value;
            if (_isModal && value != DialogResult.None)
            {
                if (_dialogDispatchDepth > 0)
                {
                    _completionRequestedDuringDispatch = true;
                }
                else
                {
                    Application.RequestDialogCompletion(this);
                }
            }
        }
    }

    public FormBorderStyle FormBorderStyle { get; set; }

    public bool MaximizeBox { get; set; } = true;

    public bool MinimizeBox { get; set; } = true;

    public bool ControlBox { get; set; } = true;

    public bool ShowIcon { get; set; } = true;

    public bool ShowInTaskbar { get; set; } = true;

    public Icon? Icon { get; set; }

    public Form? Owner { get; set; }

    public bool KeyPreview { get; set; }

    public bool RightToLeftLayout { get; set; }

    public FormStartPosition StartPosition { get; set; }

    public FormWindowState WindowState { get; set; }

    public override void Show()
    {
        base.Show();
        RaiseShownOnce();
    }

    public DialogResult ShowDialog()
    {
        return ShowDialogCore(owner: null);
    }

    public DialogResult ShowDialog(IWin32Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return ShowDialogCore(owner);
    }

    private DialogResult ShowDialogCore(IWin32Window? owner)
    {
        _dialogResult = DialogResult.None;
        _closed = false;
        _shown = false;
        _dialogDispatchDepth = 0;
        _completionRequestedDuringDispatch = false;
        _isModal = true;
        try
        {
            if (Application.TryShowDialog(this, owner, out DialogResult result))
            {
                _dialogResult = result;
                return result;
            }

            RaiseShownOnce();
            return DialogResult;
        }
        finally
        {
            _isModal = false;
        }
    }

    public void Close()
    {
        _ = Close(CloseReason.UserClosing);
    }

    public bool Close(CloseReason closeReason)
    {
        if (_closed)
        {
            return true;
        }

        bool resultDriven = _isModal && closeReason == CloseReason.None;
        if (_isModal && !resultDriven && _dialogResult == DialogResult.None)
        {
            _dialogResult = DialogResult.Cancel;
        }

        var closing = new CancelEventArgs();
        OnClosing(closing);
        if (closing.Cancel)
        {
            ResetCanceledDialogResult();
            return false;
        }

        var formClosing = new FormClosingEventArgs(closeReason, false);
        OnFormClosing(formClosing);
        if (formClosing.Cancel)
        {
            ResetCanceledDialogResult();
            return false;
        }

        if (_isModal && _dialogResult == DialogResult.None)
        {
            ResetCanceledDialogResult();
            return false;
        }

        _closed = true;
        Visible = false;
        _ = TryActivateControl(control: null);
        OnClosed(EventArgs.Empty);
        OnFormClosed(new FormClosedEventArgs(closeReason));

        return true;
    }

    internal void BeginDialogResultDispatch()
    {
        _dialogDispatchDepth++;
    }

    internal void EndDialogResultDispatch()
    {
        if (_dialogDispatchDepth <= 0)
        {
            return;
        }

        _dialogDispatchDepth--;
        if (_dialogDispatchDepth == 0 && _completionRequestedDuringDispatch)
        {
            _completionRequestedDuringDispatch = false;
            if (_isModal && _dialogResult != DialogResult.None)
            {
                Application.RequestDialogCompletion(this);
            }
        }
    }

    bool IWinFormsDialogKeyProcessor.TryProcessDialogKey(Keys keyData, Control? focusedControl)
    {
        Keys keyCode = (Keys)((int)keyData & 0xFFFF);
        if ((keyData & (Keys.Control | Keys.Alt)) != 0)
        {
            return false;
        }

        IButtonControl? button = keyCode == Keys.Return
            ? focusedControl as IButtonControl ?? _acceptButton
            : keyCode == Keys.Escape
                ? _cancelButton
                : null;

        if (button == null || button is Control control && !control.CanSelect)
        {
            return false;
        }

        button.PerformClick();
        return true;
    }

    private void ResetCanceledDialogResult()
    {
        if (_isModal && _dialogResult != DialogResult.None)
        {
            _dialogResult = DialogResult.None;
        }

        _completionRequestedDuringDispatch = false;
    }

    private void RaiseShownOnce()
    {
        if (_shown)
        {
            return;
        }

        _shown = true;
        OnShown(EventArgs.Empty);
    }

    protected virtual void OnClosing(CancelEventArgs e)
    {
        Closing?.Invoke(this, e);
    }

    protected virtual void OnClosed(EventArgs e)
    {
        Closed?.Invoke(this, e);
    }

    protected virtual void OnFormClosed(FormClosedEventArgs e)
    {
        FormClosed?.Invoke(this, e);
    }

    protected virtual void OnFormClosing(FormClosingEventArgs e)
    {
        FormClosing?.Invoke(this, e);
    }

    protected virtual void OnShown(EventArgs e)
    {
        Shown?.Invoke(this, e);
    }
}

public class UserControl : ContainerControl
{
    public UserControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }
}

public class Panel : ScrollableControl
{
    public BorderStyle BorderStyle { get; set; }
}

public class SplitterPanel : Panel
{
}

public class ToolStripPanel : Panel
{
    public void Join(ToolStrip toolStrip)
    {
        if (!Controls.Contains(toolStrip))
        {
            Controls.Add(toolStrip);
        }
    }
}

public class ToolStripContentPanel : Panel
{
}

public class ToolStripContainer : ContainerControl
{
    public ToolStripContainer()
    {
        TopToolStripPanel = new ToolStripPanel();
        BottomToolStripPanel = new ToolStripPanel();
        LeftToolStripPanel = new ToolStripPanel();
        RightToolStripPanel = new ToolStripPanel();
        ContentPanel = new ToolStripContentPanel();

        Controls.Add(TopToolStripPanel);
        Controls.Add(BottomToolStripPanel);
        Controls.Add(LeftToolStripPanel);
        Controls.Add(RightToolStripPanel);
        Controls.Add(ContentPanel);
    }

    public ToolStripPanel TopToolStripPanel { get; }

    public ToolStripPanel BottomToolStripPanel { get; }

    public ToolStripPanel LeftToolStripPanel { get; }

    public ToolStripPanel RightToolStripPanel { get; }

    public ToolStripContentPanel ContentPanel { get; }
}

public class SplitContainer : ContainerControl, ISupportInitialize
{
    private bool _initializing;
    private Orientation _orientation = Orientation.Vertical;
    private int _panel1MinSize = 25;
    private int _newPanel1MinSize = 25;
    private int _panel2MinSize = 25;
    private int _newPanel2MinSize = 25;
    private int _splitterDistance = 50;
    private int _splitterWidth = 4;
    private int _newSplitterWidth = 4;

    public SplitContainer()
    {
        Controls.Add(Panel1);
        Controls.Add(Panel2);
    }

    [DefaultValue(Orientation.Vertical)]
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (value is < Orientation.Horizontal or > Orientation.Vertical)
            {
                throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(Orientation));
            }

            if (_orientation == value)
            {
                return;
            }

            _orientation = value;
            InvalidateSplitLayout();
        }
    }

    public SplitterPanel Panel1 { get; } = new();

    public SplitterPanel Panel2 { get; } = new();

    [DefaultValue(25)]
    public int Panel1MinSize
    {
        get => _panel1MinSize;
        set
        {
            _newPanel1MinSize = value;
            if (_initializing || _panel1MinSize == value)
            {
                return;
            }

            ApplyPanel1MinSize(value);
        }
    }

    [DefaultValue(25)]
    public int Panel2MinSize
    {
        get => _panel2MinSize;
        set
        {
            _newPanel2MinSize = value;
            if (_initializing || _panel2MinSize == value)
            {
                return;
            }

            ApplyPanel2MinSize(value);
        }
    }

    [DefaultValue(50)]
    public int SplitterDistance
    {
        get => _splitterDistance;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (_splitterDistance == value)
            {
                return;
            }

            _splitterDistance = value;
            InvalidateSplitLayout();
        }
    }

    [DefaultValue(4)]
    public int SplitterWidth
    {
        get => _splitterWidth;
        set
        {
            _newSplitterWidth = value;
            if (_initializing || _splitterWidth == value)
            {
                return;
            }

            ApplySplitterWidth(value);
        }
    }

    public void BeginInit()
    {
        _initializing = true;
    }

    public void EndInit()
    {
        _initializing = false;
        if (_newPanel1MinSize != _panel1MinSize)
        {
            ApplyPanel1MinSize(_newPanel1MinSize);
        }

        if (_newPanel2MinSize != _panel2MinSize)
        {
            ApplyPanel2MinSize(_newPanel2MinSize);
        }

        if (_newSplitterWidth != _splitterWidth)
        {
            ApplySplitterWidth(_newSplitterWidth);
        }
    }

    private void InvalidateSplitLayout()
    {
        Invalidate();
    }

    private void ApplySplitterWidth(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        _splitterWidth = value;
        InvalidateSplitLayout();
    }

    private void ApplyPanel1MinSize(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        _panel1MinSize = value;
        InvalidateSplitLayout();
    }

    private void ApplyPanel2MinSize(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        _panel2MinSize = value;
        InvalidateSplitLayout();
    }
}

public class Splitter : Control
{
    public int MinExtra { get; set; } = 25;

    public int MinSize { get; set; } = 25;
}

public class ButtonBase : Control
{
    private FlatStyle _flatStyle = FlatStyle.Standard;
    private Image? _image;
    private ContentAlignment _imageAlign = ContentAlignment.MiddleCenter;
    private ContentAlignment _textAlign = ContentAlignment.MiddleCenter;
    private bool _spaceKeyDown;

    [DefaultValue(FlatStyle.Standard)]
    public FlatStyle FlatStyle
    {
        get => _flatStyle;
        set
        {
            if (value is < FlatStyle.Flat or > FlatStyle.System)
            {
                throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FlatStyle));
            }

            if (_flatStyle == value)
            {
                return;
            }

            _flatStyle = value;
            Invalidate();
        }
    }

    [DefaultValue(null)]
    public Image? Image
    {
        get => _image;
        set
        {
            if (ReferenceEquals(_image, value))
            {
                return;
            }

            _image = value;
            Invalidate();
        }
    }

    [DefaultValue(ContentAlignment.MiddleCenter)]
    public ContentAlignment ImageAlign
    {
        get => _imageAlign;
        set
        {
            ValidateContentAlignment(value);
            if (_imageAlign == value)
            {
                return;
            }

            _imageAlign = value;
            Invalidate();
        }
    }

    [DefaultValue(ContentAlignment.MiddleCenter)]
    public virtual ContentAlignment TextAlign
    {
        get => _textAlign;
        set
        {
            ValidateContentAlignment(value);
            if (_textAlign == value)
            {
                return;
            }

            _textAlign = value;
            Invalidate();
        }
    }

    public bool UseCompatibleTextRendering { get; set; }

    public bool UseVisualStyleBackColor { get; set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (CanSelect && e.KeyCode == Keys.Space)
        {
            _spaceKeyDown = true;
            e.Handled = true;
            Invalidate();
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        bool performClick = _spaceKeyDown && CanSelect && e.KeyCode == Keys.Space;
        if (e.KeyCode == Keys.Space)
        {
            _spaceKeyDown = false;
            e.Handled = true;
            Invalidate();
        }

        if (performClick)
        {
            OnClick(EventArgs.Empty);
        }

        base.OnKeyUp(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected static void ValidateContentAlignment(ContentAlignment value)
    {
        if (value is not (ContentAlignment.TopLeft
            or ContentAlignment.TopCenter
            or ContentAlignment.TopRight
            or ContentAlignment.MiddleLeft
            or ContentAlignment.MiddleCenter
            or ContentAlignment.MiddleRight
            or ContentAlignment.BottomLeft
            or ContentAlignment.BottomCenter
            or ContentAlignment.BottomRight))
        {
            throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ContentAlignment));
        }
    }
}

public interface IButtonControl
{
    DialogResult DialogResult { get; set; }

    void NotifyDefault(bool value);

    void PerformClick();
}

public class Button : ButtonBase, IButtonControl
{
    private bool _isDefault;
    private DialogResult _dialogResult;

    protected override Size DefaultSize => new(75, 23);

    public DialogResult DialogResult
    {
        get => _dialogResult;
        set
        {
            int numericValue = (int)value;
            if (numericValue < (int)DialogResult.None
                || numericValue > (int)DialogResult.Continue
                || numericValue is 8 or 9)
            {
                throw new InvalidEnumArgumentException(nameof(value), numericValue, typeof(DialogResult));
            }

            _dialogResult = value;
        }
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public bool IsDefault => _isDefault;

    public void NotifyDefault(bool value)
    {
        if (_isDefault == value)
        {
            return;
        }

        _isDefault = value;
        Invalidate();
    }

    public void PerformClick()
    {
        if (!CanSelect)
        {
            return;
        }

        OnClick(EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e)
    {
        if (!CanSelect)
        {
            return;
        }

        Form? form = FindForm();
        if (form != null && !form.TryValidateForActivation(this))
        {
            return;
        }

        form?.BeginDialogResultDispatch();
        try
        {
            if (form != null)
            {
                form.DialogResult = DialogResult;
            }

            base.OnClick(e);
        }
        finally
        {
            form?.EndDialogResultDispatch();
        }
    }
}

public class Label : Control
{
    public ContentAlignment ImageAlign { get; set; } = ContentAlignment.MiddleCenter;

    public ContentAlignment TextAlign { get; set; } = ContentAlignment.TopLeft;

    public BorderStyle BorderStyle { get; set; }

    public FlatStyle FlatStyle { get; set; }

    public bool UseCompatibleTextRendering { get; set; }

    public bool UseMnemonic { get; set; } = true;
}

[DefaultProperty(nameof(Checked))]
[DefaultEvent(nameof(CheckedChanged))]
[DefaultBindingProperty(nameof(CheckState))]
public class CheckBox : ButtonBase
{
    private CheckState _checkState;
    private Appearance _appearance;
    private ContentAlignment _checkAlign = ContentAlignment.MiddleLeft;

    public CheckBox()
    {
        AutoCheck = true;
        TextAlign = ContentAlignment.MiddleLeft;
        UseCompatibleTextRendering = true;
        UseVisualStyleBackColor = true;
    }

    public event EventHandler? CheckedChanged;

    public event EventHandler? CheckStateChanged;

    public event EventHandler? AppearanceChanged;

    [DefaultValue(Appearance.Normal)]
    public Appearance Appearance
    {
        get => _appearance;
        set
        {
            ValidateAppearance(value);
            if (_appearance == value)
            {
                return;
            }

            _appearance = value;
            Invalidate();
            OnAppearanceChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(true)]
    public bool AutoCheck { get; set; }

    [DefaultValue(ContentAlignment.MiddleLeft)]
    public ContentAlignment CheckAlign
    {
        get => _checkAlign;
        set
        {
            ValidateContentAlignment(value);
            if (_checkAlign == value)
            {
                return;
            }

            _checkAlign = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checkState != CheckState.Unchecked;
        set
        {
            if (value != Checked)
            {
                CheckState = value ? CheckState.Checked : CheckState.Unchecked;
            }
        }
    }

    [DefaultValue(CheckState.Unchecked)]
    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            ValidateCheckState(value);
            if (_checkState == value)
            {
                return;
            }

            bool wasChecked = Checked;
            _checkState = value;
            if (wasChecked != Checked)
            {
                OnCheckedChanged(EventArgs.Empty);
            }

            OnCheckStateChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(false)]
    public bool ThreeState { get; set; }

    protected override Size DefaultSize => new(104, 24);

    protected override void OnClick(EventArgs e)
    {
        if (AutoCheck)
        {
            CheckState = CheckState switch
            {
                CheckState.Unchecked => CheckState.Checked,
                CheckState.Checked when ThreeState => CheckState.Indeterminate,
                _ => CheckState.Unchecked
            };
        }

        base.OnClick(e);
    }

    protected virtual void OnAppearanceChanged(EventArgs e)
    {
        AppearanceChanged?.Invoke(this, e);
    }

    protected virtual void OnCheckedChanged(EventArgs e)
    {
        CheckedChanged?.Invoke(this, e);
    }

    protected virtual void OnCheckStateChanged(EventArgs e)
    {
        Invalidate();
        CheckStateChanged?.Invoke(this, e);
    }

    public override string ToString()
    {
        return base.ToString() + ", CheckState: " + ((int)CheckState).ToString(CultureInfo.CurrentCulture);
    }

    private static void ValidateAppearance(Appearance value)
    {
        if (value is < Appearance.Normal or > Appearance.Button)
        {
            throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(Appearance));
        }
    }

    private static void ValidateCheckState(CheckState value)
    {
        if (value is < CheckState.Unchecked or > CheckState.Indeterminate)
        {
            throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(CheckState));
        }
    }
}

[DefaultProperty(nameof(Checked))]
[DefaultEvent(nameof(CheckedChanged))]
[DefaultBindingProperty(nameof(Checked))]
public class RadioButton : ButtonBase
{
    private bool _autoCheck = true;
    private bool _checked;
    private Appearance _appearance;
    private ContentAlignment _checkAlign = ContentAlignment.MiddleLeft;

    public RadioButton()
    {
        TextAlign = ContentAlignment.MiddleLeft;
        TabStop = false;
        UseCompatibleTextRendering = true;
        UseVisualStyleBackColor = true;
    }

    public event EventHandler? CheckedChanged;

    public event EventHandler? AppearanceChanged;

    [DefaultValue(true)]
    public bool AutoCheck
    {
        get => _autoCheck;
        set
        {
            if (_autoCheck == value)
            {
                return;
            }

            _autoCheck = value;
            PerformAutoUpdates();
        }
    }

    [DefaultValue(Appearance.Normal)]
    public Appearance Appearance
    {
        get => _appearance;
        set
        {
            if (value is < Appearance.Normal or > Appearance.Button)
            {
                throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(Appearance));
            }

            if (_appearance == value)
            {
                return;
            }

            _appearance = value;
            Invalidate();
            OnAppearanceChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(ContentAlignment.MiddleLeft)]
    public ContentAlignment CheckAlign
    {
        get => _checkAlign;
        set
        {
            ValidateContentAlignment(value);
            if (_checkAlign == value)
            {
                return;
            }

            _checkAlign = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
            PerformAutoUpdates();
            OnCheckedChanged(EventArgs.Empty);
        }
    }

    protected override Size DefaultSize => new(104, 24);

    public void PerformClick()
    {
        if (CanSelect)
        {
            OnClick(EventArgs.Empty);
        }
    }

    internal void PerformAutoUpdates()
    {
        if (!AutoCheck)
        {
            return;
        }

        TabStop = Checked;
        if (!Checked || Parent is null)
        {
            return;
        }

        foreach (Control sibling in Parent.Controls)
        {
            if (sibling is RadioButton radioButton
                && !ReferenceEquals(radioButton, this)
                && radioButton.AutoCheck
                && radioButton.Checked)
            {
                radioButton.Checked = false;
            }
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (AutoCheck)
        {
            Checked = true;
        }

        base.OnClick(e);
    }

    protected virtual void OnAppearanceChanged(EventArgs e)
    {
        AppearanceChanged?.Invoke(this, e);
    }

    protected virtual void OnCheckedChanged(EventArgs e)
    {
        CheckedChanged?.Invoke(this, e);
    }

    public override string ToString()
    {
        return base.ToString() + ", Checked: " + Checked.ToString(CultureInfo.CurrentCulture);
    }
}

public class GroupBox : Control
{
}

public class TextBoxBase : Control
{
    private string _selectedText = string.Empty;

    public BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;

    public bool Multiline { get; set; }

    public bool ReadOnly { get; set; }

    public int TextLength => Text.Length;

    public string SelectedText
    {
        get => _selectedText;
        set => ReplaceSelection(value ?? string.Empty);
    }

    public virtual bool CanUndo { get; set; }

    public bool WordWrap { get; set; } = true;

    public ScrollBars ScrollBars { get; set; }

    public string[] Lines
    {
        get => Text.Split('\n');
        set => Text = value != null ? string.Join('\n', value) : string.Empty;
    }

    public int SelectionLength { get; set; }

    public int SelectionStart { get; set; }

    public virtual void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Select(TextLength, 0);
        ReplaceSelection(text);
    }

    public void Select(int start, int length)
    {
        SelectionStart = Math.Clamp(start, 0, TextLength);
        SelectionLength = Math.Clamp(length, 0, TextLength - SelectionStart);
        _selectedText = SelectionLength > 0
            ? Text.Substring(SelectionStart, SelectionLength)
            : string.Empty;
    }

    public void SelectAll()
    {
        Select(0, TextLength);
    }

    public virtual void Cut()
    {
        if (!ReadOnly && SelectionLength > 0)
        {
            Clipboard.SetText(SelectedText);
            ReplaceSelection(string.Empty);
        }
    }

    public virtual void Copy()
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            Clipboard.SetText(SelectedText);
        }
    }

    public virtual void Paste()
    {
        if (!ReadOnly && Clipboard.ContainsText())
        {
            ReplaceSelection(Clipboard.GetText());
        }
    }

    public virtual void Undo()
    {
    }

    public virtual void ScrollToCaret()
    {
    }

    public virtual void Clear()
    {
        Text = string.Empty;
        SelectionStart = 0;
        SelectionLength = 0;
        _selectedText = string.Empty;
    }

    public virtual void ApplyTextInput(string text)
    {
        if (!ReadOnly)
        {
            ReplaceSelection(text ?? string.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || ReadOnly)
        {
            return;
        }

        if (e.KeyCode == Keys.Back && SelectionStart > 0 && SelectionLength == 0)
        {
            Select(SelectionStart - 1, 1);
            ReplaceSelection(string.Empty);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Delete && SelectionStart < TextLength)
        {
            if (SelectionLength == 0)
            {
                Select(SelectionStart, 1);
            }

            ReplaceSelection(string.Empty);
            e.Handled = true;
        }
    }

    private void ReplaceSelection(string replacement)
    {
        if (ReadOnly)
        {
            return;
        }

        int start = Math.Clamp(SelectionStart, 0, TextLength);
        int length = Math.Clamp(SelectionLength, 0, TextLength - start);
        Text = Text.Remove(start, length).Insert(start, replacement);
        SelectionStart = start + replacement.Length;
        SelectionLength = 0;
        _selectedText = string.Empty;
    }
}

public class TextBox : TextBoxBase
{
    public char PasswordChar { get; set; }
}

public class RichTextBox : TextBoxBase
{
}

public class ScrollBar : Control
{
    private int _value;

    public event ScrollEventHandler? Scroll;

    public int LargeChange { get; set; } = 10;

    public int Maximum { get; set; } = 100;

    public int Minimum { get; set; }

    public int SmallChange { get; set; } = 1;

    public int Value
    {
        get => _value;
        set
        {
            int oldValue = _value;
            _value = Math.Max(Minimum, Math.Min(Maximum, value));
            if (oldValue != _value)
            {
                Scroll?.Invoke(this, new ScrollEventArgs(ScrollEventType.ThumbPosition, oldValue, _value));
            }
        }
    }
}

public class VScrollBar : ScrollBar
{
}

public class ListBox : Control
{
    private readonly List<int> _selectedIndices = new();
    public event DrawItemEventHandler? DrawItem;
    public event MeasureItemEventHandler? MeasureItem;
    public event EventHandler? SelectedIndexChanged;

    private int _selectedIndex = -1;

    public BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;

    public DrawMode DrawMode { get; set; }

    public bool FormattingEnabled { get; set; }

    public bool IntegralHeight { get; set; } = true;

    public ListBox()
    {
        Items = new ListBoxObjectCollection(this);
        SelectedItems = new SelectedObjectCollection(this);
    }

    public ListBoxObjectCollection Items { get; }

    public SelectedObjectCollection SelectedItems { get; }

    public SelectionMode SelectionMode { get; set; } = SelectionMode.One;

    public bool Sorted { get; set; }

    public virtual int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value)
            {
                return;
            }

            if (value < -1 || value >= Items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetSelectedCore(value, true, clearExisting: true);
            OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    public object? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    public int IndexFromPoint(Point p)
    {
        return Items.Count == 0 ? -1 : Math.Max(0, Math.Min(Items.Count - 1, p.Y / Math.Max(1, Font.Height)));
    }

    public sealed class ListBoxObjectCollection : Collection<object>
    {
        private readonly ListBox _owner;

        internal ListBoxObjectCollection(ListBox owner)
        {
            _owner = owner;
        }

        public new int Add(object item)
        {
            if (!_owner.Sorted)
            {
                base.Add(item);
                return Count - 1;
            }

            string text = item?.ToString() ?? string.Empty;
            int index = 0;
            while (index < Count && string.Compare(this[index]?.ToString(), text, StringComparison.CurrentCulture) <= 0)
            {
                index++;
            }

            Insert(index, item);
            return index;
        }
    }

    public sealed class SelectedObjectCollection : IReadOnlyList<object>, ICollection
    {
        private readonly ListBox _owner;

        internal SelectedObjectCollection(ListBox owner)
        {
            _owner = owner;
        }

        public int Count => _owner._selectedIndices.Count;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public object this[int index] => _owner.Items[_owner._selectedIndices[index]];

        public bool Contains(object item)
        {
            return Snapshot().Contains(item);
        }

        public void CopyTo(Array array, int index)
        {
            ((ICollection)Snapshot()).CopyTo(array, index);
        }

        public IEnumerator<object> GetEnumerator()
        {
            return Snapshot().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private List<object> Snapshot()
        {
            var items = new List<object>(_owner._selectedIndices.Count);
            foreach (int index in _owner._selectedIndices)
            {
                if (index >= 0 && index < _owner.Items.Count)
                {
                    items.Add(_owner.Items[index]);
                }
            }

            return items;
        }
    }

    public void SetSelected(int index, bool value)
    {
        ValidateItemIndex(index);
        SetSelectedCore(index, value, clearExisting: value && SelectionMode == SelectionMode.One);
        OnSelectedIndexChanged(EventArgs.Empty);
    }

    public bool GetSelected(int index)
    {
        ValidateItemIndex(index);
        return _selectedIndices.Contains(index);
    }

    private void SetSelectedCore(int index, bool value, bool clearExisting)
    {
        if (clearExisting)
        {
            _selectedIndices.Clear();
        }

        if (index < 0)
        {
            _selectedIndex = -1;
            return;
        }

        if (value)
        {
            if (!_selectedIndices.Contains(index))
            {
                _selectedIndices.Add(index);
                _selectedIndices.Sort();
            }
        }
        else
        {
            _selectedIndices.Remove(index);
        }

        _selectedIndex = _selectedIndices.Count > 0 ? _selectedIndices[0] : -1;
    }

    private void ValidateItemIndex(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    protected virtual void OnDrawItem(DrawItemEventArgs e)
    {
        DrawItem?.Invoke(this, e);
    }

    public void RaiseDrawItem(DrawItemEventArgs e)
    {
        OnDrawItem(e);
    }

    protected virtual void OnMeasureItem(MeasureItemEventArgs e)
    {
        MeasureItem?.Invoke(this, e);
    }

    public void RaiseMeasureItem(MeasureItemEventArgs e)
    {
        OnMeasureItem(e);
    }

    protected virtual void OnSelectedIndexChanged(EventArgs e)
    {
        SelectedIndexChanged?.Invoke(this, e);
    }
}

public class CheckedListBox : ListBox
{
    private readonly Dictionary<int, CheckState> _checkedStates = new();

    public CheckedListBox()
    {
        CheckedIndices = new CheckedIndexCollection(this);
        CheckedItems = new CheckedItemCollection(this);
    }

    public event ItemCheckEventHandler? ItemCheck;

    public bool CheckOnClick { get; set; }

    public CheckedIndexCollection CheckedIndices { get; }

    public CheckedItemCollection CheckedItems { get; }

    public void SetItemChecked(int index, bool value)
    {
        SetItemCheckState(index, value ? CheckState.Checked : CheckState.Unchecked);
    }

    public bool GetItemChecked(int index)
    {
        return GetItemCheckState(index) != CheckState.Unchecked;
    }

    public void SetItemCheckState(int index, CheckState value)
    {
        ValidateItemIndex(index);
        CheckState oldValue = GetItemCheckState(index);
        if (oldValue == value)
        {
            return;
        }

        var eventArgs = new ItemCheckEventArgs(index, value, oldValue);
        OnItemCheck(eventArgs);
        if (eventArgs.NewValue == CheckState.Unchecked)
        {
            _checkedStates.Remove(index);
        }
        else
        {
            _checkedStates[index] = eventArgs.NewValue;
        }

        Invalidate();
    }

    public CheckState GetItemCheckState(int index)
    {
        ValidateItemIndex(index);
        return _checkedStates.TryGetValue(index, out CheckState value)
            ? value
            : CheckState.Unchecked;
    }

    public bool TryToggleItemAt(int x, int y)
    {
        int index = IndexFromPoint(new Point(x, y));
        if (index < 0 || index >= Items.Count)
        {
            return false;
        }

        if (!CheckOnClick && x > 22)
        {
            return false;
        }

        SetItemCheckState(index, GetItemChecked(index) ? CheckState.Unchecked : CheckState.Checked);
        return true;
    }

    protected virtual void OnItemCheck(ItemCheckEventArgs e)
    {
        ItemCheck?.Invoke(this, e);
    }

    private void ValidateItemIndex(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public sealed class CheckedIndexCollection : IReadOnlyList<int>, ICollection
    {
        private readonly CheckedListBox _owner;

        internal CheckedIndexCollection(CheckedListBox owner)
        {
            _owner = owner;
        }

        public int Count => _owner._checkedStates.Count;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public int this[int index] => Snapshot()[index];

        public bool Contains(int index)
        {
            return _owner._checkedStates.ContainsKey(index);
        }

        public int IndexOf(int index)
        {
            return Snapshot().IndexOf(index);
        }

        public void CopyTo(Array array, int index)
        {
            ((ICollection)Snapshot()).CopyTo(array, index);
        }

        public IEnumerator<int> GetEnumerator()
        {
            return Snapshot().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private List<int> Snapshot()
        {
            return _owner._checkedStates.Keys.OrderBy(static value => value).ToList();
        }
    }

    public sealed class CheckedItemCollection : IReadOnlyList<object>, ICollection
    {
        private readonly CheckedListBox _owner;

        internal CheckedItemCollection(CheckedListBox owner)
        {
            _owner = owner;
        }

        public int Count => _owner._checkedStates.Count;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public object this[int index] => Snapshot()[index];

        public bool Contains(object item)
        {
            return Snapshot().Contains(item);
        }

        public int IndexOf(object item)
        {
            return Snapshot().IndexOf(item);
        }

        public void CopyTo(Array array, int index)
        {
            ((ICollection)Snapshot()).CopyTo(array, index);
        }

        public IEnumerator<object> GetEnumerator()
        {
            return Snapshot().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private List<object> Snapshot()
        {
            var items = new List<object>(_owner._checkedStates.Count);
            foreach (int index in _owner._checkedStates.Keys.OrderBy(static value => value))
            {
                if (index >= 0 && index < _owner.Items.Count)
                {
                    items.Add(_owner.Items[index]);
                }
            }

            return items;
        }
    }
}

public class ComboBox : ListBox
{
    private bool _droppedDown;

    public AutoCompleteMode AutoCompleteMode { get; set; }

    public AutoCompleteSource AutoCompleteSource { get; set; }

    public bool DroppedDown
    {
        get => _droppedDown;
        set
        {
            if (_droppedDown == value)
            {
                return;
            }

            _droppedDown = value;
            Invalidate();
            Parent?.Invalidate();
        }
    }

    public ComboBoxStyle DropDownStyle { get; set; }

    public bool FormattingEnabled { get; set; }

    public bool Sorted { get; set; }

    public int SelectionLength { get; set; }

    public string SelectedText { get; set; } = string.Empty;

    public override int SelectedIndex
    {
        get => base.SelectedIndex;
        set
        {
            base.SelectedIndex = value;
        }
    }

    public void SelectAll()
    {
        SelectedText = Text;
        SelectionLength = Text.Length;
    }
}

public class TabControl : Control
{
    private int _selectedIndex = -1;
    private bool _syncingControls;
    private bool _syncingTabPages;

    public TabControl()
    {
        TabPages = new TabPageCollection(this);
    }

    public TabPageCollection TabPages { get; }

    public bool RightToLeftLayout { get; set; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= TabPages.Count)
            {
                _selectedIndex = -1;
                return;
            }

            _selectedIndex = value;
        }
    }

    public TabPage? SelectedTab
    {
        get => SelectedIndex >= 0 && SelectedIndex < TabPages.Count ? TabPages[SelectedIndex] : null;
        set => SelectedIndex = value != null ? TabPages.IndexOf(value) : -1;
    }

    internal void RegisterControlTabPage(TabPage page, int controlIndex)
    {
        if (_syncingTabPages || TabPages.Contains(page))
        {
            return;
        }

        _syncingControls = true;
        try
        {
            TabPages.Insert(Math.Clamp(controlIndex, 0, TabPages.Count), page);
        }
        finally
        {
            _syncingControls = false;
        }
    }

    internal void UnregisterControlTabPage(TabPage page)
    {
        if (_syncingTabPages || !TabPages.Contains(page))
        {
            return;
        }

        _syncingControls = true;
        try
        {
            TabPages.Remove(page);
        }
        finally
        {
            _syncingControls = false;
        }
    }

    internal void UnregisterControlTabPages(IEnumerable<TabPage> pages)
    {
        foreach (TabPage page in pages)
        {
            UnregisterControlTabPage(page);
        }
    }

    internal void MoveControlTabPage(TabPage page, int controlIndex)
    {
        if (_syncingTabPages)
        {
            return;
        }

        int pageIndex = TabPages.IndexOf(page);
        if (pageIndex >= 0)
        {
            TabPages.MoveItem(pageIndex, controlIndex);
        }
    }

    public sealed class TabPageCollection : Collection<TabPage>
    {
        private readonly TabControl _owner;

        internal TabPageCollection(TabControl owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, TabPage item)
        {
            base.InsertItem(index, item);
            if (!_owner._syncingControls && !_owner.Controls.Contains(item))
            {
                _owner._syncingTabPages = true;
                try
                {
                    _owner.Controls.Add(item);
                    _owner.Controls.SetChildIndex(item, index);
                }
                finally
                {
                    _owner._syncingTabPages = false;
                }
            }

            if (_owner.SelectedIndex == -1)
            {
                _owner.SelectedIndex = 0;
            }

            _owner.Invalidate();
        }

        protected override void RemoveItem(int index)
        {
            TabPage page = this[index];
            base.RemoveItem(index);
            if (!_owner._syncingControls)
            {
                _owner._syncingTabPages = true;
                try
                {
                    _owner.Controls.Remove(page);
                }
                finally
                {
                    _owner._syncingTabPages = false;
                }
            }

            if (_owner.SelectedIndex >= Count)
            {
                _owner.SelectedIndex = Count - 1;
            }

            _owner.Invalidate();
        }

        protected override void ClearItems()
        {
            if (!_owner._syncingControls)
            {
                _owner._syncingTabPages = true;
                try
                {
                    foreach (TabPage page in this)
                    {
                        _owner.Controls.Remove(page);
                    }
                }
                finally
                {
                    _owner._syncingTabPages = false;
                }
            }

            base.ClearItems();
            _owner.SelectedIndex = -1;
            _owner.Invalidate();
        }

        internal void MoveItem(int oldIndex, int requestedIndex)
        {
            if (Count <= 1)
            {
                return;
            }

            int newIndex = Math.Clamp(requestedIndex, 0, Count - 1);
            if (oldIndex == newIndex)
            {
                return;
            }

            TabPage? selectedPage = _owner.SelectedTab;
            TabPage page = this[oldIndex];
            base.RemoveItem(oldIndex);
            base.InsertItem(newIndex, page);
            _owner.SelectedIndex = selectedPage != null ? IndexOf(selectedPage) : -1;
            _owner.Invalidate();
        }

        public void AddRange(TabPage[] pages)
        {
            foreach (TabPage page in pages)
            {
                Add(page);
            }
        }
    }
}

public class TabPage : Panel
{
    public TabPage()
    {
    }

    public TabPage(string text)
    {
        Text = text;
    }

    public string ToolTipText { get; set; } = string.Empty;

    public bool UseVisualStyleBackColor { get; set; }
}

public class DataGridView : Control, ISupportInitialize
{
    public event DataGridViewCellEventHandler? CellValueChanged;
    public event DataGridViewDataErrorEventHandler? DataError;
    public event DataGridViewEditingControlShowingEventHandler? EditingControlShowing;
    public event DataGridViewRowsAddedEventHandler? RowsAdded;
    public event DataGridViewRowsRemovedEventHandler? RowsRemoved;

    public bool AllowUserToAddRows { get; set; } = true;

    public bool AllowUserToDeleteRows { get; set; } = true;

    public bool AllowUserToResizeRows { get; set; } = true;

    public DataGridViewColumnHeadersHeightSizeMode ColumnHeadersHeightSizeMode { get; set; }

    public bool MultiSelect { get; set; } = true;

    public bool ShowEditingIcon { get; set; } = true;

    public DataGridViewColumnCollection Columns { get; }

    public DataGridViewRowCollection Rows { get; }

    public DataGridViewEditMode EditMode { get; set; }

    public DataGridViewCell? CurrentCell { get; set; }

    public int RowHeadersWidth { get; set; } = 41;

    public DataGridViewRowHeadersWidthSizeMode RowHeadersWidthSizeMode { get; set; }

    public DataGridView()
    {
        Columns = new DataGridViewColumnCollection(this);
        Rows = new DataGridViewRowCollection(this);
    }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }

    public bool EndEdit()
    {
        if (CurrentCell != null)
        {
            OnCellValueChanged(new DataGridViewCellEventArgs(CurrentCell.ColumnIndex, CurrentCell.RowIndex));
        }

        return true;
    }

    internal void EnsureRowCells(DataGridViewRow row)
    {
        while (row.Cells.Count < Columns.Count)
        {
            row.Cells.Add(CreateCellForColumn(Columns[row.Cells.Count]));
        }
    }

    internal DataGridViewCell CreateCellForColumn(DataGridViewColumn column)
    {
        return column.CreateCell();
    }

    internal void OnCellValueChanged(DataGridViewCellEventArgs e)
    {
        CellValueChanged?.Invoke(this, e);
    }

    internal void OnDataError(DataGridViewDataErrorEventArgs e)
    {
        DataError?.Invoke(this, e);
    }

    internal void OnEditingControlShowing(DataGridViewEditingControlShowingEventArgs e)
    {
        EditingControlShowing?.Invoke(this, e);
    }

    internal void OnRowsAdded(DataGridViewRowsAddedEventArgs e)
    {
        RowsAdded?.Invoke(this, e);
        Invalidate();
    }

    internal void OnRowsRemoved(DataGridViewRowsRemovedEventArgs e)
    {
        RowsRemoved?.Invoke(this, e);
        Invalidate();
    }

    public sealed class DataGridViewColumnCollection : Collection<DataGridViewColumn>
    {
        private readonly DataGridView _owner;

        internal DataGridViewColumnCollection(DataGridView owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, DataGridViewColumn item)
        {
            item.SetOwner(_owner, index);
            base.InsertItem(index, item);
            Reindex();
            foreach (DataGridViewRow row in _owner.Rows)
            {
                _owner.EnsureRowCells(row);
            }
        }

        protected override void RemoveItem(int index)
        {
            this[index].SetOwner(null, -1);
            base.RemoveItem(index);
            Reindex();
        }

        protected override void ClearItems()
        {
            foreach (DataGridViewColumn column in this)
            {
                column.SetOwner(null, -1);
            }

            base.ClearItems();
        }

        public void AddRange(DataGridViewColumn[] columns)
        {
            foreach (DataGridViewColumn column in columns)
            {
                Add(column);
            }
        }

        private void Reindex()
        {
            for (int i = 0; i < Count; i++)
            {
                this[i].SetOwner(_owner, i);
            }
        }
    }

    public sealed class DataGridViewRowCollection : Collection<DataGridViewRow>
    {
        private readonly DataGridView _owner;

        internal DataGridViewRowCollection(DataGridView owner)
        {
            _owner = owner;
        }

        public new int Add(DataGridViewRow row)
        {
            base.Add(row);
            return Count - 1;
        }

        public int Add()
        {
            return Add(new DataGridViewRow());
        }

        public int Add(params object?[] values)
        {
            var row = new DataGridViewRow();
            for (int i = 0; i < values.Length; i++)
            {
                DataGridViewCell cell = i < _owner.Columns.Count
                    ? _owner.CreateCellForColumn(_owner.Columns[i])
                    : new DataGridViewTextBoxCell();
                cell.Value = values[i];
                row.Cells.Add(cell);
            }

            return Add(row);
        }

        protected override void InsertItem(int index, DataGridViewRow item)
        {
            item.SetOwner(_owner, index);
            _owner.EnsureRowCells(item);
            base.InsertItem(index, item);
            Reindex();
            _owner.OnRowsAdded(new DataGridViewRowsAddedEventArgs(index, 1));
        }

        protected override void RemoveItem(int index)
        {
            this[index].SetOwner(null, -1);
            base.RemoveItem(index);
            Reindex();
            _owner.OnRowsRemoved(new DataGridViewRowsRemovedEventArgs(index, 1));
        }

        protected override void ClearItems()
        {
            int count = Count;
            foreach (DataGridViewRow row in this)
            {
                row.SetOwner(null, -1);
            }

            base.ClearItems();
            if (count > 0)
            {
                _owner.OnRowsRemoved(new DataGridViewRowsRemovedEventArgs(0, count));
            }
        }

        private void Reindex()
        {
            for (int i = 0; i < Count; i++)
            {
                this[i].SetOwner(_owner, i);
            }
        }
    }
}

public class DataGridViewColumn : Component
{
    private DataGridView? _owner;

    public DataGridViewAutoSizeColumnMode AutoSizeMode { get; set; }

    public string HeaderText { get; set; } = string.Empty;

    public int Index { get; private set; } = -1;

    public string Name { get; set; } = string.Empty;

    public bool ReadOnly { get; set; }

    public object? Tag { get; set; }

    public int Width { get; set; } = 100;

    internal virtual DataGridViewCell CreateCell()
    {
        return new DataGridViewTextBoxCell();
    }

    internal void SetOwner(DataGridView? owner, int index)
    {
        _owner = owner;
        Index = index;
    }
}

public class DataGridViewTextBoxColumn : DataGridViewColumn
{
    internal override DataGridViewCell CreateCell()
    {
        return new DataGridViewTextBoxCell();
    }
}

public class DataGridViewComboBoxColumn : DataGridViewColumn
{
    internal override DataGridViewCell CreateCell()
    {
        return new DataGridViewComboBoxCell();
    }
}

public class DataGridViewRow
{
    private DataGridView? _owner;

    public DataGridViewCellCollection Cells { get; }

    public DataGridView? DataGridView => _owner;

    public int Index { get; private set; } = -1;

    public object? Tag { get; set; }

    public DataGridViewRow()
    {
        Cells = new DataGridViewCellCollection(this);
    }

    internal void SetOwner(DataGridView? owner, int index)
    {
        _owner = owner;
        Index = index;
        for (int i = 0; i < Cells.Count; i++)
        {
            Cells[i].SetOwner(owner, this, index, i);
        }
    }
}

public sealed class DataGridViewCellCollection : Collection<DataGridViewCell>
{
    private readonly DataGridViewRow _owner;

    internal DataGridViewCellCollection(DataGridViewRow owner)
    {
        _owner = owner;
    }

    protected override void InsertItem(int index, DataGridViewCell item)
    {
        item.SetOwner(_owner.DataGridView, _owner, _owner.Index, index);
        base.InsertItem(index, item);
        Reindex();
    }

    protected override void RemoveItem(int index)
    {
        this[index].SetOwner(null, null, -1, -1);
        base.RemoveItem(index);
        Reindex();
    }

    protected override void ClearItems()
    {
        foreach (DataGridViewCell cell in this)
        {
            cell.SetOwner(null, null, -1, -1);
        }

        base.ClearItems();
    }

    private void Reindex()
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].SetOwner(_owner.DataGridView, _owner, _owner.Index, i);
        }
    }
}

public class DataGridViewCell
{
    public int ColumnIndex { get; private set; } = -1;

    public DataGridView? DataGridView { get; private set; }

    public DataGridViewRow? OwningRow { get; private set; }

    public int RowIndex { get; private set; } = -1;

    public object? Value { get; set; }

    internal void SetOwner(DataGridView? dataGridView, DataGridViewRow? row, int rowIndex, int columnIndex)
    {
        DataGridView = dataGridView;
        OwningRow = row;
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
    }
}

public class DataGridViewTextBoxCell : DataGridViewCell
{
}

public class DataGridViewComboBoxCell : DataGridViewCell
{
    public IList Items { get; } = new ArrayList();
}

public class TableLayoutPanel : Panel
{
}

public class FlowLayoutPanel : Panel
{
}

public class PictureBox : Control, ISupportInitialize
{
    public BorderStyle BorderStyle { get; set; }

    public Image? Image { get; set; }

    public PictureBoxSizeMode SizeMode { get; set; }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }
}

public class ProgressBar : Control
{
    public int Maximum { get; set; } = 100;

    public int Minimum { get; set; }

    public int Step { get; set; } = 10;

    public ProgressBarStyle Style { get; set; }

    public int Value { get; set; }

    public bool RightToLeftLayout { get; set; }

    public void PerformStep()
    {
        Value = Math.Max(Minimum, Math.Min(Maximum, Value + Step));
    }
}

public class NumericUpDown : Control, ISupportInitialize
{
    private decimal _value;

    public decimal DecimalPlaces { get; set; }

    public decimal Increment { get; set; } = 1;

    public decimal Maximum { get; set; } = 100;

    public decimal Minimum { get; set; }

    public decimal Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public event EventHandler? ValueChanged;

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }
}

public class TrackBar : Control, ISupportInitialize
{
    public event EventHandler? Scroll;

    public int Maximum { get; set; } = 10;

    public int Minimum { get; set; }

    public int SmallChange { get; set; } = 1;

    public int TickFrequency { get; set; } = 1;

    public int Value { get; set; }

    public bool RightToLeftLayout { get; set; }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }

    public void RaiseScroll()
    {
        Scroll?.Invoke(this, EventArgs.Empty);
    }
}

public class LinkLabel : Label
{
    public event LinkLabelLinkClickedEventHandler? LinkClicked;

    protected virtual void OnLinkClicked(LinkLabelLinkClickedEventArgs e)
    {
        LinkClicked?.Invoke(this, e);
    }
}

public class PropertyGrid : Control
{
    public event PropertyValueChangedEventHandler? PropertyValueChanged;
    public event SelectedGridItemChangedEventHandler? SelectedGridItemChanged;
    public event EventHandler? SelectedObjectsChanged;

    private readonly List<PropertyGridDisplayRow> _displayRows = new();
    private object? _selectedObject;
    private object?[]? _selectedObjects;
    private PropertySort _propertySort = PropertySort.CategorizedAlphabetical;

    public object? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (ReferenceEquals(_selectedObject, value) && _selectedObjects == null)
            {
                return;
            }

            _selectedObject = value;
            _selectedObjects = null;
            RebuildDisplayRows();
            SelectedObjectsChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public object?[]? SelectedObjects
    {
        get => _selectedObjects;
        set
        {
            _selectedObjects = value;
            _selectedObject = value != null && value.Length > 0 ? value[0] : null;
            RebuildDisplayRows();
            SelectedObjectsChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public GridItem? SelectedGridItem { get; set; }

    public bool HelpVisible { get; set; } = true;

    public PropertySort PropertySort
    {
        get => _propertySort;
        set
        {
            if (_propertySort == value)
            {
                return;
            }

            _propertySort = value;
            RebuildDisplayRows();
            Invalidate();
        }
    }

    public PropertyTabCollection PropertyTabs { get; } = new();

    public bool ToolbarVisible { get; set; } = true;

    public IReadOnlyList<PropertyGridDisplayRow> DisplayRows => _displayRows;

    public int SelectedObjectCount => _selectedObjects?.Length ?? (_selectedObject != null ? 1 : 0);

    public void ResetSelectedProperty()
    {
    }

    public override void Refresh()
    {
        RebuildDisplayRows();
        base.Refresh();
    }

    protected virtual void OnPropertyValueChanged(PropertyValueChangedEventArgs e)
    {
        PropertyValueChanged?.Invoke(this, e);
    }

    protected virtual void OnSelectedGridItemChanged(SelectedGridItemChangedEventArgs e)
    {
        SelectedGridItemChanged?.Invoke(this, e);
    }

    private void RebuildDisplayRows()
    {
        _displayRows.Clear();
        if (_selectedObject == null)
        {
            SelectedGridItem = null;
            return;
        }

        var descriptors = TypeDescriptor.GetProperties(_selectedObject)
            .Cast<PropertyDescriptor>()
            .Where(static descriptor => descriptor.IsBrowsable)
            .ToList();

        if (_propertySort == PropertySort.Alphabetical || _propertySort == PropertySort.CategorizedAlphabetical)
        {
            descriptors.Sort(static (x, y) => string.Compare(x.DisplayName, y.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        }

        string? lastCategory = null;
        bool showCategories = _propertySort == PropertySort.Categorized || _propertySort == PropertySort.CategorizedAlphabetical;
        foreach (PropertyDescriptor descriptor in descriptors)
        {
            string category = descriptor.Category ?? string.Empty;
            if (showCategories && !string.Equals(lastCategory, category, StringComparison.Ordinal))
            {
                _displayRows.Add(PropertyGridDisplayRow.CreateCategory(category));
                lastCategory = category;
            }

            object? value = null;
            bool valueAvailable = true;
            try
            {
                value = descriptor.GetValue(_selectedObject);
            }
            catch
            {
                valueAvailable = false;
            }

            _displayRows.Add(PropertyGridDisplayRow.CreateProperty(
                descriptor.DisplayName,
                valueAvailable ? FormatPropertyValue(value) : string.Empty,
                descriptor.Description ?? string.Empty,
                category,
                descriptor));
        }

        PropertyGridDisplayRow? firstProperty = _displayRows.FirstOrDefault(static row => !row.IsCategory);
        SelectedGridItem = firstProperty != null
            ? new GridItem
            {
                Label = firstProperty.Label,
                Value = firstProperty.ValueText,
                PropertyDescriptor = firstProperty.PropertyDescriptor
            }
            : null;
    }

    private static string FormatPropertyValue(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }
}

public sealed class PropertyTabCollection : Collection<Type>
{
    public void AddTabType(Type propertyTabType, System.ComponentModel.PropertyTabScope tabScope)
    {
        Add(propertyTabType);
    }
}

public sealed class PropertyGridDisplayRow
{
    private PropertyGridDisplayRow(
        bool isCategory,
        string label,
        string valueText,
        string description,
        string category,
        PropertyDescriptor? propertyDescriptor)
    {
        IsCategory = isCategory;
        Label = label;
        ValueText = valueText;
        Description = description;
        Category = category;
        PropertyDescriptor = propertyDescriptor;
    }

    public bool IsCategory { get; }

    public string Label { get; }

    public string ValueText { get; }

    public string Description { get; }

    public string Category { get; }

    public PropertyDescriptor? PropertyDescriptor { get; }

    public static PropertyGridDisplayRow CreateCategory(string category)
    {
        return new PropertyGridDisplayRow(true, string.IsNullOrEmpty(category) ? "Misc" : category, string.Empty, string.Empty, category, null);
    }

    public static PropertyGridDisplayRow CreateProperty(
        string label,
        string valueText,
        string description,
        string category,
        PropertyDescriptor propertyDescriptor)
    {
        return new PropertyGridDisplayRow(false, label, valueText, description, category, propertyDescriptor);
    }
}

public class WebBrowser : Control
{
    public event EventHandler? CanGoBackChanged;
    public event EventHandler? CanGoForwardChanged;
    public event EventHandler? DocumentTitleChanged;
    public event WebBrowserNavigatingEventHandler? Navigating;
    public event WebBrowserNavigatedEventHandler? Navigated;
    public event WebBrowserDocumentCompletedEventHandler? DocumentCompleted;
    public event EventHandler? StatusTextChanged;

    private Uri? _url;
    private string _documentTitle = string.Empty;
    private string _statusText = string.Empty;

    protected object ActiveXInstance { get; } = new object();

    public Uri? Url
    {
        get => _url;
        set
        {
            if (value != null)
            {
                var navigating = new WebBrowserNavigatingEventArgs(value);
                Navigating?.Invoke(this, navigating);
                if (navigating.Cancel)
                {
                    return;
                }
            }

            _url = value;
            if (value != null)
            {
                Navigated?.Invoke(this, new WebBrowserNavigatedEventArgs(value));
                DocumentCompleted?.Invoke(this, new WebBrowserDocumentCompletedEventArgs(value));
            }
        }
    }

    public string DocumentTitle
    {
        get => _documentTitle;
        set
        {
            if (_documentTitle == value)
            {
                return;
            }

            _documentTitle = value;
            DocumentTitleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            StatusTextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool CanGoBack { get; set; }

    public bool CanGoForward { get; set; }

    public void Navigate(string urlString)
    {
        Url = new Uri(urlString);
    }

    public void Navigate(Uri url)
    {
        Url = url;
    }

    public void GoBack()
    {
    }

    public void GoForward()
    {
    }

    public void Stop()
    {
    }

    public void Refresh(WebBrowserRefreshOption opt)
    {
        Refresh();
    }

    protected virtual void CreateSink()
    {
    }

    protected virtual void DetachSink()
    {
    }
}

public class ToolStrip : Control
{
    public ToolStripItemCollection Items { get; } = new();

    public bool CanOverflow { get; set; } = true;

    public ToolStripGripStyle GripStyle { get; set; }

    public bool ShowItemToolTips { get; set; } = true;

    public bool Stretch { get; set; } = true;
}

public class MenuStrip : ToolStrip
{
}

public class StatusStrip : ToolStrip
{
}

public class ContextMenuStrip : ToolStrip
{
    public static event EventHandler<ContextMenuStripShowRequestedEventArgs>? ShowRequested;

    public ContextMenuStrip()
    {
    }

    public ContextMenuStrip(IContainer container)
    {
        container?.Add(this);
    }

    public event CancelEventHandler? Opening;
    public event EventHandler? Opened;
    public event EventHandler? Closed;

    public Control? SourceControl { get; private set; }

    public Point ShowPosition { get; private set; }

    public void Show(Control control, Point position)
    {
        SourceControl = control;
        ShowPosition = position;

        var opening = new CancelEventArgs();
        Opening?.Invoke(this, opening);
        if (!opening.Cancel)
        {
            Visible = true;
            Opened?.Invoke(this, EventArgs.Empty);
            var requested = new ContextMenuStripShowRequestedEventArgs(this, control, position);
            ShowRequested?.Invoke(this, requested);
        }
    }

    public void Close()
    {
        if (Visible)
        {
            Visible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class ContextMenuStripShowRequestedEventArgs : EventArgs
{
    public ContextMenuStripShowRequestedEventArgs(ContextMenuStrip contextMenuStrip, Control control, Point position)
    {
        ContextMenuStrip = contextMenuStrip;
        Control = control;
        Position = position;
    }

    public ContextMenuStrip ContextMenuStrip { get; }

    public Control Control { get; }

    public Point Position { get; }

    public bool Handled { get; set; }
}

public class ToolStripDropDown : ToolStrip
{
}

public class ToolStripItem : Component
{
    public event EventHandler? Click;
    public event EventHandler? TextChanged;

    public bool Available { get; set; } = true;

    public bool AutoSize { get; set; } = true;

    public ToolStripItemDisplayStyle DisplayStyle { get; set; } = ToolStripItemDisplayStyle.ImageAndText;

    public virtual bool Enabled { get; set; } = true;

    public Image? Image { get; set; }

    public ToolStripItemImageScaling ImageScaling { get; set; } = ToolStripItemImageScaling.SizeToFit;

    public string? ImageKey { get; set; }

    public Color ImageTransparentColor { get; set; } = Color.Empty;

    public string Name { get; set; } = string.Empty;

    public RightToLeft RightToLeft { get; set; }

    public Size Size { get; set; }

    public object? Tag { get; set; }

    public bool Selected { get; set; }

    public string ToolTipText { get; set; } = string.Empty;

    public virtual bool Visible { get; set; } = true;

    public int Width { get; set; }

    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public virtual void PerformClick()
    {
        OnClick(EventArgs.Empty);
    }

    public virtual bool Focus()
    {
        return true;
    }

    protected virtual void OnClick(EventArgs e)
    {
        Click?.Invoke(this, e);
    }
}

public class ToolStripMenuItem : ToolStripItem
{
    private bool _checked;
    private CheckState _checkState;

    public ToolStripMenuItem()
    {
    }

    public ToolStripMenuItem(string text)
    {
        Text = text;
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            _checkState = value ? CheckState.Checked : CheckState.Unchecked;
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            _checkState = value;
            Checked = value == CheckState.Checked;
        }
    }

    public bool CheckOnClick { get; set; }

    public ToolStripDropDown DropDown { get; } = new();

    public ToolStripItemCollection DropDownItems { get; } = new();

    public string ShortcutKeyDisplayString { get; set; } = string.Empty;

    public Keys ShortcutKeys { get; set; }

    public void ShowDropDown()
    {
        OnDropDownShow(EventArgs.Empty);
        DropDown.Visible = true;
    }

    protected virtual void OnDropDownShow(EventArgs e)
    {
    }
}

public class ToolStripButton : ToolStripItem
{
    public bool Checked { get; set; }

    public bool CheckOnClick { get; set; }
}

public class ToolStripDropDownButton : ToolStripItem
{
    public ToolStripDropDown DropDown { get; } = new();

    public ToolStripItemCollection DropDownItems { get; } = new();

    public void ShowDropDown()
    {
        OnDropDownShow(EventArgs.Empty);
        DropDown.Visible = true;
    }

    protected virtual void OnDropDownShow(EventArgs e)
    {
    }
}

public class ToolStripSplitButton : ToolStripDropDownButton
{
    public event EventHandler? ButtonClick;

    public void PerformButtonClick()
    {
        OnButtonClick(EventArgs.Empty);
    }

    protected virtual void OnButtonClick(EventArgs e)
    {
        ButtonClick?.Invoke(this, e);
    }
}

public class ToolStripLabel : ToolStripItem
{
}

public class ToolStripControlHost : ToolStripItem
{
    public ToolStripControlHost(Control control)
    {
        Control = control;
    }

    public Control Control { get; }
}

public class ToolStripComboBox : ToolStripItem
{
    public event KeyEventHandler? KeyDown;
    public event EventHandler? SelectedIndexChanged;

    public ComboBox ComboBox { get; } = new();

    public AutoCompleteMode AutoCompleteMode
    {
        get => ComboBox.AutoCompleteMode;
        set => ComboBox.AutoCompleteMode = value;
    }

    public AutoCompleteSource AutoCompleteSource
    {
        get => ComboBox.AutoCompleteSource;
        set => ComboBox.AutoCompleteSource = value;
    }

    public FlatStyle FlatStyle { get; set; }

    public ListBox.ListBoxObjectCollection Items => ComboBox.Items;

    public int SelectedIndex
    {
        get => ComboBox.SelectedIndex;
        set
        {
            if (ComboBox.SelectedIndex == value)
            {
                return;
            }

            ComboBox.SelectedIndex = value;
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedItem => ComboBox.SelectedItem;

    protected virtual void OnKeyDown(KeyEventArgs e)
    {
        KeyDown?.Invoke(this, e);
    }
}

public class ToolStripProgressBar : ToolStripControlHost
{
    public ToolStripProgressBar()
        : base(new ProgressBar())
    {
    }

    private ProgressBar ProgressBar => (ProgressBar)Control;

    public int Maximum
    {
        get => ProgressBar.Maximum;
        set => ProgressBar.Maximum = value;
    }

    public int Minimum
    {
        get => ProgressBar.Minimum;
        set => ProgressBar.Minimum = value;
    }

    public ProgressBarStyle Style
    {
        get => ProgressBar.Style;
        set => ProgressBar.Style = value;
    }

    public int Step
    {
        get => ProgressBar.Step;
        set => ProgressBar.Step = value;
    }

    public int Value
    {
        get => ProgressBar.Value;
        set => ProgressBar.Value = value;
    }

    public void PerformStep()
    {
        ProgressBar.PerformStep();
    }
}

public class ToolStripTextBox : ToolStripControlHost
{
    public ToolStripTextBox()
        : base(new TextBox())
    {
    }

    private TextBox TextBox => (TextBox)Control;

    public override bool Enabled
    {
        get => TextBox.Enabled;
        set => TextBox.Enabled = value;
    }

    public override bool Visible
    {
        get => TextBox.Visible;
        set => TextBox.Visible = value;
    }

    public string? SelectedText
    {
        get => TextBox.SelectedText;
        set => TextBox.SelectedText = value;
    }

    public int SelectionLength
    {
        get => TextBox.SelectionLength;
        set => TextBox.SelectionLength = value;
    }

    public int SelectionStart
    {
        get => TextBox.SelectionStart;
        set => TextBox.SelectionStart = value;
    }

    public override bool Focus()
    {
        return TextBox.Focus();
    }
}

public class ToolStripSeparator : ToolStripItem
{
}

public class ToolStripItemCollection : Collection<ToolStripItem>
{
    public ToolStripItem Add(string text)
    {
        var item = new ToolStripMenuItem(text);
        Add(item);
        return item;
    }

    public void AddRange(ToolStripItem[] items)
    {
        foreach (ToolStripItem item in items)
        {
            Add(item);
        }
    }

    public ToolStripItem Add(string text, Image? image, EventHandler? onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            Image = image
        };
        if (onClick != null)
        {
            item.Click += onClick;
        }

        Add(item);
        return item;
    }
}

public abstract class FileDialog : Component
{
    public bool AddExtension { get; set; } = true;

    public bool CheckFileExists { get; set; }

    public bool CheckPathExists { get; set; } = true;

    public string DefaultExt { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Filter { get; set; } = string.Empty;

    public int FilterIndex { get; set; } = 1;

    public string InitialDirectory { get; set; } = string.Empty;

    public bool RestoreDirectory { get; set; }

    public string Title { get; set; } = string.Empty;

    protected virtual string DialogKind => "OpenFile";

    protected virtual bool AllowMultipleSelection => false;

    public virtual DialogResult ShowDialog()
    {
        PortableFileDialogResult? result = PortableWinFormsDialogService.ShowFileDialog(
            DialogKind,
            Title,
            InitialDirectory,
            suggestedItemName: FileName,
            defaultExtension: DefaultExt,
            Filter,
            FilterIndex,
            AllowMultipleSelection);
        if (result == null || result.SelectedPathCount == 0 || string.IsNullOrEmpty(result.SelectedPath))
        {
            return DialogResult.Cancel;
        }

        SetSelectedPaths(result.SelectedPaths);
        return DialogResult.OK;
    }

    public virtual DialogResult ShowDialog(IWin32Window owner)
    {
        return ShowDialog();
    }

    protected virtual void SetSelectedPaths(ReadOnlySpan<string> selectedPaths)
    {
        FileName = selectedPaths[0];
    }
}

public class OpenFileDialog : FileDialog
{
    protected override string DialogKind => "OpenFile";

    protected override bool AllowMultipleSelection => Multiselect;

    public bool Multiselect { get; set; }

    public string[] FileNames { get; set; } = Array.Empty<string>();

    protected override void SetSelectedPaths(ReadOnlySpan<string> selectedPaths)
    {
        int selectedPathCount = Multiselect ? selectedPaths.Length : 1;
        FileNames = selectedPaths[..selectedPathCount].ToArray();
        FileName = FileNames[0];
    }
}

public class SaveFileDialog : FileDialog
{
    protected override string DialogKind => "SaveFile";

    public bool OverwritePrompt { get; set; } = true;
}

public class PrintDialog : Component
{
    public bool AllowSomePages { get; set; }

    public PrintDocument? Document { get; set; }

    public DialogResult ShowDialog()
    {
        return DialogResult.Cancel;
    }

    public DialogResult ShowDialog(IWin32Window owner)
    {
        return ShowDialog();
    }
}

public class PrintPreviewDialog : Form
{
    public PrintDocument? Document { get; set; }

    public bool TopMost { get; set; }

    public void Show(IWin32Window owner)
    {
        Show();
    }
}

public class FolderBrowserDialog : Component
{
    public string Description { get; set; } = string.Empty;

    public Environment.SpecialFolder RootFolder { get; set; } = Environment.SpecialFolder.Desktop;

    public string SelectedPath { get; set; } = string.Empty;

    public bool ShowNewFolderButton { get; set; } = true;

    public DialogResult ShowDialog()
    {
        PortableFileDialogResult? result = PortableWinFormsDialogService.ShowFileDialog(
            "PickFolder",
            Description,
            SelectedPath,
            suggestedItemName: string.Empty,
            defaultExtension: string.Empty,
            filter: string.Empty,
            filterIndex: 1);
        if (result == null || string.IsNullOrEmpty(result.SelectedPath))
        {
            return DialogResult.Cancel;
        }

        SelectedPath = result.SelectedPath;
        return DialogResult.OK;
    }

    public DialogResult ShowDialog(IWin32Window owner)
    {
        return ShowDialog();
    }
}

public class ColorDialog : Component
{
    public Color Color { get; set; }

    public int[] CustomColors { get; set; } = Array.Empty<int>();

    public DialogResult ShowDialog()
    {
        return RunDialog(IntPtr.Zero) ? DialogResult.OK : DialogResult.Cancel;
    }

    public DialogResult ShowDialog(IWin32Window owner)
    {
        return ShowDialog();
    }

    protected virtual bool RunDialog(IntPtr hwndOwner)
    {
        int? selectedArgb = PortableWinFormsColorDialogService.ShowColorDialog(Color.ToArgb(), CustomColors);
        if (!selectedArgb.HasValue)
        {
            return false;
        }

        Color = Color.FromArgb(selectedArgb.Value);
        return true;
    }
}

public sealed class ToolTip : Component
{
    private readonly Dictionary<Control, string> _toolTips = new();

    public bool Active { get; set; } = true;

    public void SetToolTip(Control control, string? caption)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (string.IsNullOrEmpty(caption))
        {
            _toolTips.Remove(control);
        }
        else
        {
            _toolTips[control] = caption;
        }
    }

    public string GetToolTip(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _toolTips.TryGetValue(control, out string? value) ? value : string.Empty;
    }
}

public sealed class ErrorProvider : Component, IExtenderProvider, ISupportInitialize
{
    private readonly Dictionary<Control, string> _errors = new();
    private readonly Dictionary<Control, ErrorIconAlignment> _iconAlignments = new();
    private readonly Dictionary<Control, int> _iconPadding = new();

    public ErrorProvider()
    {
    }

    public ErrorProvider(ContainerControl parentControl)
    {
        ContainerControl = parentControl;
    }

    public ErrorProvider(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Add(this);
    }

    public int BlinkRate { get; set; } = 250;

    public ErrorBlinkStyle BlinkStyle { get; set; } = ErrorBlinkStyle.BlinkIfDifferentError;

    public ContainerControl? ContainerControl { get; set; }

    public string? DataMember { get; set; }

    public object? DataSource { get; set; }

    public Icon? Icon { get; set; }

    public RightToLeft RightToLeft { get; set; } = RightToLeft.Inherit;

    public bool CanExtend(object extendee)
    {
        return extendee is Control;
    }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }

    public string GetError(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _errors.TryGetValue(control, out string? value) ? value : string.Empty;
    }

    public ErrorIconAlignment GetIconAlignment(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _iconAlignments.TryGetValue(control, out ErrorIconAlignment value) ? value : ErrorIconAlignment.MiddleRight;
    }

    public int GetIconPadding(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _iconPadding.TryGetValue(control, out int value) ? value : 0;
    }

    public void SetError(Control control, string? value)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (string.IsNullOrEmpty(value))
        {
            _errors.Remove(control);
        }
        else
        {
            _errors[control] = value;
        }
    }

    public void SetIconAlignment(Control control, ErrorIconAlignment value)
    {
        ArgumentNullException.ThrowIfNull(control);
        _iconAlignments[control] = value;
    }

    public void SetIconPadding(Control control, int padding)
    {
        ArgumentNullException.ThrowIfNull(control);
        _iconPadding[control] = padding;
    }

    public void Clear()
    {
        _errors.Clear();
    }
}

public class AxHost : Control
{
    public sealed class ConnectionPointCookie
    {
        public ConnectionPointCookie(object? source, object? sink, Type eventInterface)
        {
            EventInterface = eventInterface;
        }

        public Type EventInterface { get; }

        public void Disconnect()
        {
        }
    }
}

public class FontDialog : Component
{
    public Font? Font { get; set; } = SystemFonts.DefaultFont;

    public bool ShowEffects { get; set; } = true;

    public bool ShowColor { get; set; }

    public int MinSize { get; set; }

    public int MaxSize { get; set; }

    public DialogResult ShowDialog()
    {
        return RunDialog(IntPtr.Zero) ? DialogResult.OK : DialogResult.Cancel;
    }

    public DialogResult ShowDialog(IWin32Window owner)
    {
        return ShowDialog();
    }

    protected virtual bool RunDialog(IntPtr hwndOwner)
    {
        Font initialFont = Font ?? SystemFonts.DefaultFont;
        var request = new PortableFontDialogRequest(
            initialFont.Name,
            initialFont.Size,
            (int)initialFont.Style,
            initialFont.Unit.ToString(),
            ShowEffects,
            ShowColor,
            MinSize,
            MaxSize);

        PortableFontDialogResult? result = PortableWinFormsFontDialogService.ShowFontDialog(request);
        if (result == null)
        {
            return false;
        }

        Font = new Font(
            result.FamilyName,
            result.Size,
            (FontStyle)result.Style,
            ParseGraphicsUnit(result.Unit),
            initialFont.GdiCharSet,
            initialFont.GdiVerticalFont);
        return true;
    }

    private static GraphicsUnit ParseGraphicsUnit(string unit)
    {
        return Enum.TryParse(unit, ignoreCase: true, out GraphicsUnit parsed)
            ? parsed
            : GraphicsUnit.Point;
    }
}

[DefaultProperty(nameof(Interval))]
[DefaultEvent(nameof(Tick))]
[ToolboxItemFilter("System.Windows.Forms")]
public class Timer : Component
{
    private readonly object _gate = new();
    private IDisposable? _registration;
    private long _registrationVersion;
    private bool _disposed;
    private bool _enabled;
    private int _interval = 100;

    public Timer()
    {
    }

    public Timer(IContainer container)
        : this()
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Add(this);
    }

    public event EventHandler? Tick;

    [DefaultValue(false)]
    public virtual bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
        set
        {
            IDisposable? registration = null;
            bool restart = false;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed && value, this);
                if (_disposed)
                    return;
                if (_enabled == value)
                    return;

                _enabled = value;
                _registrationVersion++;
                registration = _registration;
                _registration = null;
                restart = value;
            }

            registration?.Dispose();
            if (restart && !DesignMode)
                RegisterTimer();
        }
    }

    [DefaultValue(100)]
    public int Interval
    {
        get
        {
            lock (_gate)
            {
                return _interval;
            }
        }
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Timer interval must be greater than zero.");

            IDisposable? registration = null;
            bool restart = false;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_interval == value)
                    return;

                _interval = value;
                if (_enabled)
                {
                    _registrationVersion++;
                    registration = _registration;
                    _registration = null;
                    restart = true;
                }
            }

            registration?.Dispose();
            if (restart && !DesignMode)
                RegisterTimer();
        }
    }

    [Bindable(true)]
    [DefaultValue(null)]
    [Localizable(false)]
    [TypeConverter(typeof(StringConverter))]
    public object? Tag { get; set; }

    public void Start()
    {
        Enabled = true;
    }

    public void Stop()
    {
        Enabled = false;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void RaiseTick()
    {
        lock (_gate)
        {
            if (!_enabled || _disposed)
                return;
        }

        OnTick(EventArgs.Empty);
    }

    public override string ToString()
    {
        return base.ToString() + ", Interval: " + Interval.ToString(CultureInfo.CurrentCulture);
    }

    protected virtual void OnTick(EventArgs e)
    {
        Tick?.Invoke(this, e);
    }

    protected override void Dispose(bool disposing)
    {
        IDisposable? registration = null;
        if (disposing)
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _enabled = false;
                    _registrationVersion++;
                    registration = _registration;
                    _registration = null;
                }
            }

            registration?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RegisterTimer()
    {
        int interval;
        long version;
        lock (_gate)
        {
            if (!_enabled || _disposed || _registration is not null)
                return;

            interval = _interval;
            version = _registrationVersion;
        }

        IDisposable registration;
        try
        {
            registration = Application.RegisterTimer(interval, OnRegisteredTimerTick);
        }
        catch
        {
            lock (_gate)
            {
                if (_enabled
                    && !_disposed
                    && _registration is null
                    && _registrationVersion == version)
                {
                    _enabled = false;
                    _registrationVersion++;
                }
            }

            throw;
        }

        bool retainRegistration;
        lock (_gate)
        {
            retainRegistration = _enabled
                && !_disposed
                && _registration is null
                && _registrationVersion == version;
            if (retainRegistration)
                _registration = registration;
        }

        if (!retainRegistration)
            registration.Dispose();
    }

    private void OnRegisteredTimerTick()
    {
        try
        {
            RaiseTick();
        }
        catch (Exception exception)
        {
            Application.OnThreadException(exception);
        }
    }
}

public class ListView : Control
{
    private const int BorderInset = 1;
    private const int HeaderHeight = 20;
    private const int RowHeight = 18;
    private const int SmallIconRowHeight = 22;
    private const int ListCellWidth = 140;
    private const int LargeIconCellWidth = 96;
    private const int LargeIconCellHeight = 72;
    private const int TileCellWidth = 160;
    private const int TileCellHeight = 48;
    private ImageList? _largeImageList;
    private ImageList? _smallImageList;
    private View _view;
    private int _updateCount;
    private bool _invalidatePending;
    private int _verticalScrollOffset;
    private bool _syncingCheckedItems;
    private bool _syncingSelection;

    public event LabelEditEventHandler? AfterLabelEdit;
    public event ColumnClickEventHandler? ColumnClick;
    public event ItemCheckEventHandler? ItemCheck;
    public event EventHandler? ItemActivate;
    public event EventHandler? SelectedIndexChanged;

    public ColumnHeaderCollection Columns { get; } = new();

    public ListViewGroupCollection Groups { get; } = new();

    public ListViewItemCollection Items { get; }

    public CheckedListViewItemCollection CheckedItems { get; }

    public CheckedIndexCollection CheckedIndices { get; }

    public SelectedListViewItemCollection SelectedItems { get; }

    public IComparer? ListViewItemSorter { get; set; }

    public ImageList? LargeImageList
    {
        get => _largeImageList;
        set
        {
            if (ReferenceEquals(_largeImageList, value))
            {
                return;
            }

            if (_largeImageList != null)
            {
                _largeImageList.Changed -= OnImageListChanged;
            }

            _largeImageList = value;
            if (_largeImageList != null)
            {
                _largeImageList.Changed += OnImageListChanged;
            }

            ClampVerticalScrollOffset();
            Invalidate();
        }
    }

    public ImageList? SmallImageList
    {
        get => _smallImageList;
        set
        {
            if (ReferenceEquals(_smallImageList, value))
            {
                return;
            }

            if (_smallImageList != null)
            {
                _smallImageList.Changed -= OnImageListChanged;
            }

            _smallImageList = value;
            if (_smallImageList != null)
            {
                _smallImageList.Changed += OnImageListChanged;
            }

            ClampVerticalScrollOffset();
            Invalidate();
        }
    }

    public bool AllowColumnReorder { get; set; }

    public ListViewAlignment Alignment { get; set; }

    public BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;

    public bool HideSelection { get; set; } = true;

    public bool HotTracking { get; set; }

    public bool CheckBoxes { get; set; }

    public bool FullRowSelect { get; set; }

    public bool GridLines { get; set; }

    public ColumnHeaderStyle HeaderStyle { get; set; } = ColumnHeaderStyle.Clickable;

    public bool LabelEdit { get; set; }

    public bool MultiSelect { get; set; } = true;

    public SortOrder Sorting { get; set; }

    public bool UseCompatibleStateImageBehavior { get; set; }

    public View View
    {
        get => _view;
        set
        {
            if (value is < View.LargeIcon or > View.Tile)
            {
                throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(View));
            }

            if (_view == value)
            {
                return;
            }

            _view = value;
            _verticalScrollOffset = 0;
            Invalidate();
        }
    }

    public ListView()
    {
        Items = new ListViewItemCollection(this);
        CheckedItems = new CheckedListViewItemCollection(this);
        CheckedIndices = new CheckedIndexCollection(this);
        SelectedItems = new SelectedListViewItemCollection(this);
    }

    public void BeginUpdate()
    {
        if (_updateCount < int.MaxValue)
        {
            _updateCount++;
        }
    }

    public void EndUpdate()
    {
        if (_updateCount == 0)
        {
            return;
        }

        _updateCount--;
        if (_updateCount == 0 && _invalidatePending)
        {
            _invalidatePending = false;
            base.Invalidate();
        }
    }

    public override void Invalidate()
    {
        if (_updateCount > 0)
        {
            _invalidatePending = true;
            return;
        }

        base.Invalidate();
    }

    public override void Invalidate(Rectangle rc)
    {
        Invalidate();
    }

    public ListViewItem? GetItemAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= ClientSize.Width || y >= ClientSize.Height)
        {
            return null;
        }

        int index = GetItemIndexAt(x, y);
        return index >= 0 && index < Items.Count ? Items[index] : null;
    }

    public Rectangle GetItemRect(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        int contentWidth = GetContentWidth();
        int contentTop = GetItemsTop();
        return View switch
        {
            View.LargeIcon => GetGridItemRect(index, contentWidth, contentTop, GetLargeIconCellWidth(), GetLargeIconCellHeight()),
            View.Tile => GetGridItemRect(index, contentWidth, contentTop, GetTileCellWidth(), GetTileCellHeight()),
            View.SmallIcon => new Rectangle(BorderInset, contentTop + (index * GetSmallIconRowHeight()) - _verticalScrollOffset, contentWidth, GetSmallIconRowHeight()),
            View.List => GetListItemRect(index, contentTop),
            _ => new Rectangle(BorderInset, contentTop + (index * GetDetailsRowHeight()) - _verticalScrollOffset, contentWidth, GetDetailsRowHeight())
        };
    }

    public void EnsureVisible(int index)
    {
        Rectangle itemBounds = GetItemRect(index);
        if (View == View.List)
        {
            int viewportLeft = BorderInset;
            int viewportRight = Math.Max(viewportLeft, ClientSize.Width - BorderInset);
            int nextHorizontalOffset = _verticalScrollOffset;
            if (itemBounds.Left < viewportLeft)
            {
                nextHorizontalOffset -= viewportLeft - itemBounds.Left;
            }
            else if (itemBounds.Right > viewportRight)
            {
                nextHorizontalOffset += itemBounds.Right - viewportRight;
            }

            SetVerticalScrollOffset(nextHorizontalOffset);
            return;
        }

        int viewportTop = GetItemsTop();
        int viewportBottom = Math.Max(viewportTop, ClientSize.Height - BorderInset);
        int nextOffset = _verticalScrollOffset;

        if (itemBounds.Top < viewportTop)
        {
            nextOffset -= viewportTop - itemBounds.Top;
        }
        else if (itemBounds.Bottom > viewportBottom)
        {
            nextOffset += itemBounds.Bottom - viewportBottom;
        }

        SetVerticalScrollOffset(nextOffset);
    }

    public void Sort()
    {
        if (ListViewItemSorter != null)
        {
            Items.Sort(ListViewItemSorter);
        }
    }

    protected virtual void OnColumnClick(ColumnClickEventArgs e)
    {
        ColumnClick?.Invoke(this, e);
    }

    protected virtual void OnItemCheck(ItemCheckEventArgs e)
    {
        ItemCheck?.Invoke(this, e);
    }

    public void RaiseColumnClick(int column)
    {
        if (column < 0 || column >= Columns.Count)
        {
            return;
        }

        OnColumnClick(new ColumnClickEventArgs(column));
        Invalidate();
    }

    public bool TryRaiseColumnClickAt(int x, int y)
    {
        if (x < BorderInset
            || y < BorderInset
            || y >= BorderInset + HeaderHeight
            || x >= ClientSize.Width - BorderInset
            || HeaderStyle != ColumnHeaderStyle.Clickable
            || View != View.Details)
        {
            return false;
        }

        int currentX = BorderInset;
        for (int columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
        {
            int width = Columns[columnIndex].Width > 0 ? Columns[columnIndex].Width : 120;
            if (x >= currentX && x < currentX + width)
            {
                RaiseColumnClick(columnIndex);
                return true;
            }

            currentX += width;
        }

        return false;
    }

    protected virtual void OnItemActivate(EventArgs e)
    {
        ItemActivate?.Invoke(this, e);
    }

    protected virtual void OnAfterLabelEdit(LabelEditEventArgs e)
    {
        AfterLabelEdit?.Invoke(this, e);
    }

    protected virtual void OnSelectedIndexChanged(EventArgs e)
    {
        SelectedIndexChanged?.Invoke(this, e);
    }

    internal void SetItemSelected(ListViewItem item, bool selected, bool raiseEvent)
    {
        if (item.Owner != null && !ReferenceEquals(item.Owner, this))
        {
            return;
        }

        if (item.SelectedCore == selected)
        {
            return;
        }

        bool changed = false;
        if (selected && !MultiSelect)
        {
            foreach (ListViewItem selectedItem in SelectedItems.ToArray())
            {
                if (!ReferenceEquals(selectedItem, item))
                {
                    SetItemSelected(selectedItem, false, false);
                    changed = true;
                }
            }
        }

        item.SetSelectedCore(selected);
        _syncingSelection = true;
        try
        {
            if (selected)
            {
                if (!SelectedItems.Contains(item))
                {
                    SelectedItems.Add(item);
                }
            }
            else
            {
                SelectedItems.Remove(item);
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        changed = true;
        Invalidate();
        if (changed && raiseEvent)
        {
            OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    internal void SetItemChecked(ListViewItem item, bool value, bool raiseEvent)
    {
        if (item.Owner != null && !ReferenceEquals(item.Owner, this))
        {
            return;
        }

        if (item.CheckedCore == value)
        {
            return;
        }

        bool finalValue = value;
        if (raiseEvent && item.Owner != null)
        {
            int index = Items.IndexOf(item);
            if (index >= 0)
            {
                var eventArgs = new ItemCheckEventArgs(
                    index,
                    value ? CheckState.Checked : CheckState.Unchecked,
                    item.CheckedCore ? CheckState.Checked : CheckState.Unchecked);
                OnItemCheck(eventArgs);
                finalValue = eventArgs.NewValue != CheckState.Unchecked;
            }
        }

        if (item.CheckedCore == finalValue)
        {
            return;
        }

        item.SetCheckedCore(finalValue);
        _syncingCheckedItems = true;
        try
        {
            if (finalValue)
            {
                if (!CheckedItems.Contains(item))
                {
                    CheckedItems.Add(item);
                }
            }
            else
            {
                CheckedItems.Remove(item);
            }
        }
        finally
        {
            _syncingCheckedItems = false;
        }

        Invalidate();
    }

    public void RaiseItemActivate()
    {
        OnItemActivate(EventArgs.Empty);
    }

    public bool TryActivateItemAt(int x, int y)
    {
        ListViewItem? item = GetItemAt(x, y);
        if (item == null)
        {
            return false;
        }

        if (!item.Selected)
        {
            item.Selected = true;
        }

        RaiseItemActivate();
        return true;
    }

    public bool TryToggleItemCheckAt(int x, int y)
    {
        if (!CheckBoxes)
        {
            return false;
        }

        ListViewItem? item = GetItemAt(x, y);
        if (item == null)
        {
            return false;
        }

        int index = Items.IndexOf(item);
        Rectangle itemBounds = GetItemRect(index);
        Rectangle checkBounds = GetCheckBoxBounds(itemBounds);
        if (!checkBounds.Contains(x, y))
        {
            return false;
        }

        if (!item.Selected)
        {
            item.Selected = true;
        }

        item.Checked = !item.Checked;
        return true;
    }

    internal void AttachItem(ListViewItem item)
    {
        item.SetOwner(this);
        if (item.CheckedCore && !CheckedItems.Contains(item))
        {
            _syncingCheckedItems = true;
            try
            {
                CheckedItems.Add(item);
            }
            finally
            {
                _syncingCheckedItems = false;
            }
        }

        if (item.SelectedCore && !SelectedItems.Contains(item))
        {
            _syncingSelection = true;
            try
            {
                SelectedItems.Add(item);
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        ClampVerticalScrollOffset();
    }

    internal void DetachItem(ListViewItem item)
    {
        if (SelectedItems.Contains(item))
        {
            _syncingSelection = true;
            try
            {
                SelectedItems.Remove(item);
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        if (CheckedItems.Contains(item))
        {
            _syncingCheckedItems = true;
            try
            {
                CheckedItems.Remove(item);
            }
            finally
            {
                _syncingCheckedItems = false;
            }
        }

        item.SetSelectedCore(false);
        item.SetOwner(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Items.Count == 0)
        {
            return;
        }

        int currentIndex = SelectedItems.Count > 0 ? Items.IndexOf(SelectedItems[0]) : -1;
        int targetIndex = GetNavigationTargetIndex(currentIndex, e.KeyCode);
        if (targetIndex < 0)
        {
            if (e.KeyCode == Keys.Space && CheckBoxes && currentIndex >= 0)
            {
                Items[currentIndex].Checked = !Items[currentIndex].Checked;
                e.Handled = true;
            }

            return;
        }

        SelectOnly(targetIndex);
        EnsureVisible(targetIndex);
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Delta == 0)
        {
            return;
        }

        int wheelSteps = Math.Max(1, Math.Abs(e.Delta) / 120);
        int direction = e.Delta > 0 ? -1 : 1;
        int lineHeight = View switch
        {
            View.LargeIcon => GetLargeIconCellHeight(),
            View.Tile => GetTileCellHeight(),
            View.List => GetListCellWidth(),
            View.SmallIcon => GetSmallIconRowHeight(),
            _ => GetDetailsRowHeight()
        };
        int scrollLines = View == View.List ? 1 : Math.Max(1, SystemInformation.MouseWheelScrollLines);
        SetVerticalScrollOffset(_verticalScrollOffset + (direction * wheelSteps * scrollLines * lineHeight));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        ClampVerticalScrollOffset();
        base.OnSizeChanged(e);
    }

    private Rectangle GetCheckBoxBounds(Rectangle itemBounds)
    {
        int top = View == View.LargeIcon
            ? itemBounds.Top + 4
            : itemBounds.Top + Math.Max(0, (itemBounds.Height - 12) / 2);
        return new Rectangle(itemBounds.Left + 4, top, 12, 12);
    }

    private int GetItemIndexAt(int x, int y)
    {
        int contentTop = GetItemsTop();
        if (y < contentTop)
        {
            return -1;
        }

        int contentWidth = GetContentWidth();
        if (x < BorderInset || x >= BorderInset + contentWidth)
        {
            return -1;
        }

        if (View == View.List)
        {
            int listRowHeight = GetSmallIconRowHeight();
            int row = (y - contentTop) / listRowHeight;
            int rowsPerColumn = GetListRowsPerColumn();
            if (row < 0 || row >= rowsPerColumn)
            {
                return -1;
            }

            int scrolledX = x - BorderInset + _verticalScrollOffset;
            if (scrolledX < 0)
            {
                return -1;
            }

            int column = scrolledX / GetListCellWidth();
            int index = (column * rowsPerColumn) + row;
            return index < Items.Count ? index : -1;
        }

        int scrolledY = y - contentTop + _verticalScrollOffset;
        if (scrolledY < 0)
        {
            return -1;
        }

        if (View is View.LargeIcon or View.Tile)
        {
            int cellWidth = View == View.LargeIcon ? GetLargeIconCellWidth() : GetTileCellWidth();
            int cellHeight = View == View.LargeIcon ? GetLargeIconCellHeight() : GetTileCellHeight();
            int columns = GetGridColumnCount(contentWidth, cellWidth);
            int column = (x - BorderInset) / cellWidth;
            int row = scrolledY / cellHeight;
            if (column >= columns)
            {
                return -1;
            }

            int index = (row * columns) + column;
            return index < Items.Count ? index : -1;
        }

        int rowHeight = View == View.SmallIcon ? GetSmallIconRowHeight() : GetDetailsRowHeight();
        int rowIndex = scrolledY / rowHeight;
        return rowIndex < Items.Count ? rowIndex : -1;
    }

    private Rectangle GetGridItemRect(int index, int contentWidth, int contentTop, int cellWidth, int cellHeight)
    {
        int columns = GetGridColumnCount(contentWidth, cellWidth);
        int column = index % columns;
        int row = index / columns;
        int x = BorderInset + (column * cellWidth);
        int width = Math.Max(0, Math.Min(cellWidth, BorderInset + contentWidth - x));
        return new Rectangle(x, contentTop + (row * cellHeight) - _verticalScrollOffset, width, cellHeight);
    }

    private Rectangle GetListItemRect(int index, int contentTop)
    {
        int rowsPerColumn = GetListRowsPerColumn();
        int column = index / rowsPerColumn;
        int row = index % rowsPerColumn;
        int cellWidth = GetListCellWidth();
        return new Rectangle(
            BorderInset + (column * cellWidth) - _verticalScrollOffset,
            contentTop + (row * GetSmallIconRowHeight()),
            cellWidth,
            GetSmallIconRowHeight());
    }

    private int GetNavigationTargetIndex(int currentIndex, Keys keyCode)
    {
        if (keyCode == Keys.Home)
        {
            return 0;
        }

        if (keyCode == Keys.End)
        {
            return Items.Count - 1;
        }

        if (currentIndex < 0)
        {
            return keyCode is Keys.Down or Keys.Right or Keys.PageDown ? 0 : -1;
        }

        int delta;
        if (View is View.LargeIcon or View.Tile)
        {
            int cellWidth = View == View.LargeIcon ? GetLargeIconCellWidth() : GetTileCellWidth();
            int columns = GetGridColumnCount(GetContentWidth(), cellWidth);
            int visibleRows = Math.Max(1, (ClientSize.Height - GetItemsTop()) / (View == View.LargeIcon ? GetLargeIconCellHeight() : GetTileCellHeight()));
            delta = keyCode switch
            {
                Keys.Left => -1,
                Keys.Right => 1,
                Keys.Up => -columns,
                Keys.Down => columns,
                Keys.PageUp => -(columns * visibleRows),
                Keys.PageDown => columns * visibleRows,
                _ => 0
            };
        }
        else if (View == View.List)
        {
            int rowsPerColumn = GetListRowsPerColumn();
            int currentRow = currentIndex % rowsPerColumn;
            return keyCode switch
            {
                Keys.Left => Math.Clamp(currentIndex - rowsPerColumn, 0, Items.Count - 1),
                Keys.Right => Math.Clamp(currentIndex + rowsPerColumn, 0, Items.Count - 1),
                Keys.Up => currentRow > 0 ? currentIndex - 1 : currentIndex,
                Keys.Down => currentRow + 1 < rowsPerColumn && currentIndex + 1 < Items.Count
                    ? currentIndex + 1
                    : currentIndex,
                Keys.PageUp => Math.Clamp(currentIndex - rowsPerColumn, 0, Items.Count - 1),
                Keys.PageDown => Math.Clamp(currentIndex + rowsPerColumn, 0, Items.Count - 1),
                _ => -1
            };
        }
        else
        {
            int rowHeight = View == View.SmallIcon ? GetSmallIconRowHeight() : GetDetailsRowHeight();
            int visibleRows = Math.Max(1, (ClientSize.Height - GetItemsTop()) / rowHeight);
            delta = keyCode switch
            {
                Keys.Left or Keys.Up => -1,
                Keys.Right or Keys.Down => 1,
                Keys.PageUp => -visibleRows,
                Keys.PageDown => visibleRows,
                _ => 0
            };
        }

        return delta == 0 ? -1 : Math.Clamp(currentIndex + delta, 0, Items.Count - 1);
    }

    private void SelectOnly(int index)
    {
        ListViewItem target = Items[index];
        bool changed = false;
        foreach (ListViewItem selectedItem in SelectedItems.ToArray())
        {
            if (!ReferenceEquals(selectedItem, target))
            {
                SetItemSelected(selectedItem, false, false);
                changed = true;
            }
        }

        if (!target.SelectedCore)
        {
            SetItemSelected(target, true, false);
            changed = true;
        }

        if (changed)
        {
            OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    private int GetItemsTop()
    {
        return BorderInset + (View == View.Details && HeaderStyle != ColumnHeaderStyle.None ? HeaderHeight : 0);
    }

    private int GetContentWidth()
    {
        return Math.Max(0, ClientSize.Width - (BorderInset * 2));
    }

    private int GetDetailsRowHeight()
    {
        return Math.Max(RowHeight, (_smallImageList?.ImageSize.Height ?? 0) + 2);
    }

    private int GetSmallIconRowHeight()
    {
        return Math.Max(SmallIconRowHeight, (_smallImageList?.ImageSize.Height ?? 0) + 4);
    }

    private int GetListCellWidth()
    {
        return Math.Max(ListCellWidth, (_smallImageList?.ImageSize.Width ?? 0) + 100);
    }

    private int GetListRowsPerColumn()
    {
        int viewportHeight = Math.Max(0, ClientSize.Height - BorderInset - GetItemsTop());
        return Math.Max(1, viewportHeight / GetSmallIconRowHeight());
    }

    private int GetLargeIconCellWidth()
    {
        return Math.Max(LargeIconCellWidth, (_largeImageList?.ImageSize.Width ?? 0) + 16);
    }

    private int GetLargeIconCellHeight()
    {
        return Math.Max(LargeIconCellHeight, (_largeImageList?.ImageSize.Height ?? 0) + 30);
    }

    private int GetTileCellWidth()
    {
        return Math.Max(TileCellWidth, (_largeImageList?.ImageSize.Width ?? 0) + 96);
    }

    private int GetTileCellHeight()
    {
        return Math.Max(TileCellHeight, (_largeImageList?.ImageSize.Height ?? 0) + 8);
    }

    private static int GetGridColumnCount(int contentWidth, int cellWidth)
    {
        return Math.Max(1, contentWidth / Math.Max(1, cellWidth));
    }

    private int GetContentHeight()
    {
        int itemsHeight;
        if (View is View.LargeIcon or View.Tile)
        {
            int cellWidth = View == View.LargeIcon ? GetLargeIconCellWidth() : GetTileCellWidth();
            int cellHeight = View == View.LargeIcon ? GetLargeIconCellHeight() : GetTileCellHeight();
            int columns = GetGridColumnCount(GetContentWidth(), cellWidth);
            itemsHeight = ((Items.Count + columns - 1) / columns) * cellHeight;
        }
        else
        {
            int rowHeight = View is View.SmallIcon or View.List ? GetSmallIconRowHeight() : GetDetailsRowHeight();
            itemsHeight = Items.Count * rowHeight;
        }

        return (GetItemsTop() - BorderInset) + itemsHeight;
    }

    private int GetMaximumVerticalScrollOffset()
    {
        if (View == View.List)
        {
            int rowsPerColumn = GetListRowsPerColumn();
            int columns = (Items.Count + rowsPerColumn - 1) / rowsPerColumn;
            int contentWidth = columns * GetListCellWidth();
            return Math.Max(0, contentWidth - GetContentWidth());
        }

        int viewportHeight = Math.Max(0, ClientSize.Height - (BorderInset * 2));
        return Math.Max(0, GetContentHeight() - viewportHeight);
    }

    private void ClampVerticalScrollOffset()
    {
        _verticalScrollOffset = Math.Clamp(_verticalScrollOffset, 0, GetMaximumVerticalScrollOffset());
    }

    private void SetVerticalScrollOffset(int value)
    {
        int next = Math.Clamp(value, 0, GetMaximumVerticalScrollOffset());
        if (_verticalScrollOffset == next)
        {
            return;
        }

        _verticalScrollOffset = next;
        Invalidate();
    }

    private void OnImageListChanged(object? sender, EventArgs e)
    {
        ClampVerticalScrollOffset();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_largeImageList != null)
            {
                _largeImageList.Changed -= OnImageListChanged;
            }

            if (_smallImageList != null)
            {
                _smallImageList.Changed -= OnImageListChanged;
            }
        }

        base.Dispose(disposing);
    }

    public sealed class ColumnHeaderCollection : Collection<ColumnHeader>
    {
        public ColumnHeader Add(string text, int width, HorizontalAlignment textAlign)
        {
            var columnHeader = new ColumnHeader
            {
                Text = text,
                Width = width,
                TextAlign = textAlign
            };
            Add(columnHeader);
            return columnHeader;
        }

        public void AddRange(ColumnHeader[] values)
        {
            foreach (ColumnHeader value in values)
            {
                Add(value);
            }
        }
    }

    public sealed class ListViewGroupCollection : Collection<ListViewGroup>
    {
        public ListViewGroup? this[string key]
        {
            get
            {
                foreach (ListViewGroup group in this)
                {
                    if (string.Equals(group.Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return group;
                    }
                }

                return null;
            }
        }

        public void AddRange(ListViewGroup[] groups)
        {
            foreach (ListViewGroup group in groups)
            {
                Add(group);
            }
        }
    }

    public sealed class ListViewItemCollection : Collection<ListViewItem>
    {
        private readonly ListView _owner;

        internal ListViewItemCollection(ListView owner)
        {
            _owner = owner;
        }

        public new ListViewItem Add(ListViewItem item)
        {
            base.Add(item);
            return item;
        }

        public ListViewItem Add(string text)
        {
            var item = new ListViewItem(text);
            Add(item);
            return item;
        }

        public ListViewItem Add(string text, int imageIndex)
        {
            var item = new ListViewItem(text)
            {
                ImageIndex = imageIndex
            };
            Add(item);
            return item;
        }

        public void AddRange(ListViewItem[] values)
        {
            foreach (ListViewItem value in values)
            {
                Add(value);
            }
        }

        internal void Sort(IComparer comparer)
        {
            var items = new List<ListViewItem>(this);
            items.Sort((x, y) => comparer.Compare(x, y));
            ClearItems();
            foreach (var item in items)
            {
                Add(item);
            }
        }

        protected override void InsertItem(int index, ListViewItem item)
        {
            base.InsertItem(index, item);
            _owner.AttachItem(item);
            _owner.Invalidate();
        }

        protected override void RemoveItem(int index)
        {
            ListViewItem item = this[index];
            _owner.DetachItem(item);
            base.RemoveItem(index);
            _owner.ClampVerticalScrollOffset();
            _owner.Invalidate();
        }

        protected override void ClearItems()
        {
            foreach (ListViewItem item in this)
            {
                _owner.DetachItem(item);
            }

            base.ClearItems();
            _owner.ClampVerticalScrollOffset();
            _owner.Invalidate();
        }
    }

    public sealed class SelectedListViewItemCollection : Collection<ListViewItem>
    {
        private readonly ListView _owner;

        internal SelectedListViewItemCollection(ListView owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, ListViewItem item)
        {
            if (_owner._syncingSelection)
            {
                base.InsertItem(index, item);
                return;
            }

            _owner.SetItemSelected(item, true, true);
        }

        protected override void RemoveItem(int index)
        {
            if (_owner._syncingSelection)
            {
                base.RemoveItem(index);
                return;
            }

            _owner.SetItemSelected(this[index], false, true);
        }

        protected override void ClearItems()
        {
            if (_owner._syncingSelection)
            {
                base.ClearItems();
                return;
            }

            foreach (ListViewItem item in this.ToArray())
            {
                _owner.SetItemSelected(item, false, false);
            }

            _owner.OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    public sealed class CheckedListViewItemCollection : Collection<ListViewItem>
    {
        private readonly ListView _owner;

        internal CheckedListViewItemCollection(ListView owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, ListViewItem item)
        {
            if (_owner._syncingCheckedItems)
            {
                base.InsertItem(index, item);
                return;
            }

            _owner.SetItemChecked(item, true, true);
        }

        protected override void RemoveItem(int index)
        {
            if (_owner._syncingCheckedItems)
            {
                base.RemoveItem(index);
                return;
            }

            _owner.SetItemChecked(this[index], false, true);
        }

        protected override void ClearItems()
        {
            if (_owner._syncingCheckedItems)
            {
                base.ClearItems();
                return;
            }

            foreach (ListViewItem item in this.ToArray())
            {
                _owner.SetItemChecked(item, false, false);
            }
        }
    }

    public sealed class CheckedIndexCollection : IReadOnlyList<int>, ICollection
    {
        private readonly ListView _owner;

        internal CheckedIndexCollection(ListView owner)
        {
            _owner = owner;
        }

        public int Count => _owner.CheckedItems.Count;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public int this[int index] => Snapshot()[index];

        public bool Contains(int index)
        {
            return index >= 0
                && index < _owner.Items.Count
                && _owner.Items[index].Checked;
        }

        public int IndexOf(int index)
        {
            return Snapshot().IndexOf(index);
        }

        public void CopyTo(Array array, int index)
        {
            ((ICollection)Snapshot()).CopyTo(array, index);
        }

        public IEnumerator<int> GetEnumerator()
        {
            return Snapshot().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private List<int> Snapshot()
        {
            var indices = new List<int>();
            for (int i = 0; i < _owner.Items.Count; i++)
            {
                if (_owner.Items[i].Checked)
                {
                    indices.Add(i);
                }
            }

            return indices;
        }
    }
}

public class TreeView : Control
{
    private const int BorderInset = 1;
    private const int ContentTop = 3;
    private const int DefaultRowHeight = 18;
    private const int IndentWidth = 14;
    private const int GlyphSlotWidth = 12;
    private const int ImageGap = 3;
    private ImageList? _imageList;
    private TreeNode? _selectedNode;
    private int _updateCount;
    private bool _invalidatePending;
    private int _verticalScrollOffset;
    private bool _fullRowSelect;
    private int _imageIndex = -1;
    private int _selectedImageIndex = -1;
    private TreeNode? _notifyingCheckedNode;

    public event TreeViewCancelEventHandler? BeforeExpand;
    public event TreeViewEventHandler? AfterExpand;
    public event TreeViewCancelEventHandler? BeforeCollapse;
    public event TreeViewEventHandler? AfterCollapse;
    public event TreeViewCancelEventHandler? BeforeSelect;
    public event TreeViewEventHandler? AfterCheck;
    public event TreeViewEventHandler? AfterSelect;
    public event NodeLabelEditEventHandler? AfterLabelEdit;
    public event DrawTreeNodeEventHandler? DrawNode;
    public event ItemDragEventHandler? ItemDrag;

    public TreeNodeCollection Nodes { get; }

    public static new Font DefaultFont => SystemFonts.DefaultFont;

    public TreeNode? SelectedNode
    {
        get => _selectedNode;
        set => SelectNode(value, TreeViewAction.Unknown);
    }

    public bool Sorted { get; set; }

    public IComparer? TreeViewNodeSorter { get; set; }

    public ImageList? ImageList
    {
        get => _imageList;
        set
        {
            if (ReferenceEquals(_imageList, value))
            {
                return;
            }

            if (_imageList != null)
            {
                _imageList.Changed -= OnImageListChanged;
            }

            _imageList = value;
            if (_imageList != null)
            {
                _imageList.Changed += OnImageListChanged;
            }

            Invalidate();
        }
    }

    public BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;

    public int ImageIndex
    {
        get => _imageIndex;
        set
        {
            if (_imageIndex == value)
            {
                return;
            }

            _imageIndex = value;
            Invalidate();
        }
    }

    public int SelectedImageIndex
    {
        get => _selectedImageIndex;
        set
        {
            if (_selectedImageIndex == value)
            {
                return;
            }

            _selectedImageIndex = value;
            Invalidate();
        }
    }

    public bool LabelEdit { get; set; }

    public bool HideSelection { get; set; } = true;

    public bool FullRowSelect
    {
        get => _fullRowSelect;
        set
        {
            if (_fullRowSelect == value)
            {
                return;
            }

            _fullRowSelect = value;
            Invalidate();
        }
    }

    public TreeViewDrawMode DrawMode { get; set; }

    public TreeView()
    {
        Nodes = new TreeNodeCollection(this, null);
    }

    public virtual TreeNode? GetNodeAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= ClientSize.Width || y >= ClientSize.Height)
        {
            return null;
        }

        TreeNodeLayoutEnumerator layouts = GetVisibleNodeLayouts().GetEnumerator();
        while (layouts.MoveNext())
        {
            TreeNodeLayout layout = layouts.Current;
            if (layout.RowBounds.Contains(x, y))
            {
                return layout.Node;
            }
        }

        return null;
    }

    public virtual TreeNode? GetNodeAt(Point pt)
    {
        return GetNodeAt(pt.X, pt.Y);
    }

    public virtual void BeginUpdate()
    {
        if (_updateCount < int.MaxValue)
        {
            _updateCount++;
        }
    }

    public virtual void EndUpdate()
    {
        if (_updateCount == 0)
        {
            return;
        }

        _updateCount--;
        if (_updateCount == 0 && _invalidatePending)
        {
            _invalidatePending = false;
            ClampVerticalScrollOffset();
            base.Invalidate();
        }
    }

    public override void Invalidate()
    {
        if (_updateCount > 0)
        {
            _invalidatePending = true;
            return;
        }

        ClampVerticalScrollOffset();
        base.Invalidate();
    }

    public override void Invalidate(Rectangle rc)
    {
        Invalidate();
    }

    public TreeNodeLayoutEnumerable GetVisibleNodeLayouts()
    {
        return new TreeNodeLayoutEnumerable(this);
    }

    public bool TryGetNodeLayout(TreeNode node, out TreeNodeLayout layout)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!ReferenceEquals(node.TreeView, this))
        {
            layout = default;
            return false;
        }

        TreeNodeLayoutEnumerator layouts = GetVisibleNodeLayouts().GetEnumerator();
        while (layouts.MoveNext())
        {
            if (ReferenceEquals(layouts.Current.Node, node))
            {
                layout = layouts.Current;
                return true;
            }
        }

        layout = default;
        return false;
    }

    public bool TryToggleExpansionAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= ClientSize.Width || y >= ClientSize.Height)
        {
            return false;
        }

        TreeNodeLayoutEnumerator layouts = GetVisibleNodeLayouts().GetEnumerator();
        while (layouts.MoveNext())
        {
            TreeNodeLayout layout = layouts.Current;
            if (!layout.GlyphBounds.IsEmpty && layout.GlyphBounds.Contains(x, y))
            {
                layout.Node.Toggle();
                return true;
            }
        }

        return false;
    }

    internal void EnsureNodeVisible(TreeNode node)
    {
        if (!ReferenceEquals(node.TreeView, this))
        {
            return;
        }

        ExpandAncestors(node.Parent);
        if (!TryGetNodeLayout(node, out TreeNodeLayout layout))
        {
            return;
        }

        int viewportTop = ContentTop;
        int viewportBottom = Math.Max(viewportTop, ClientSize.Height - BorderInset);
        int nextOffset = _verticalScrollOffset;
        if (layout.RowBounds.Top < viewportTop)
        {
            nextOffset -= viewportTop - layout.RowBounds.Top;
        }
        else if (layout.RowBounds.Bottom > viewportBottom)
        {
            nextOffset += layout.RowBounds.Bottom - viewportBottom;
        }

        SetVerticalScrollOffset(nextOffset);
    }

    public void ExpandAll()
    {
        BeginUpdate();
        try
        {
            foreach (TreeNode node in Nodes)
            {
                node.ExpandAll();
            }
        }
        finally
        {
            EndUpdate();
        }
    }

    public void CollapseAll()
    {
        BeginUpdate();
        try
        {
            foreach (TreeNode node in Nodes)
            {
                node.Collapse(false);
            }
        }
        finally
        {
            EndUpdate();
        }
    }

    public new virtual void Sort()
    {
        if (TreeViewNodeSorter == null)
        {
            return;
        }

        Nodes.Sort(TreeViewNodeSorter);
    }

    internal void AttachNode(TreeNode node)
    {
        node.SetTreeView(this);
    }

    protected virtual void OnBeforeExpand(TreeViewCancelEventArgs e)
    {
        BeforeExpand?.Invoke(this, e);
    }

    protected virtual void OnAfterExpand(TreeViewEventArgs e)
    {
        AfterExpand?.Invoke(this, e);
    }

    protected virtual void OnBeforeCollapse(TreeViewCancelEventArgs e)
    {
        BeforeCollapse?.Invoke(this, e);
    }

    protected virtual void OnAfterCollapse(TreeViewEventArgs e)
    {
        AfterCollapse?.Invoke(this, e);
    }

    protected virtual void OnBeforeSelect(TreeViewCancelEventArgs e)
    {
        BeforeSelect?.Invoke(this, e);
    }

    protected virtual void OnAfterCheck(TreeViewEventArgs e)
    {
        AfterCheck?.Invoke(this, e);
    }

    protected virtual void OnAfterSelect(TreeViewEventArgs e)
    {
        AfterSelect?.Invoke(this, e);
    }

    protected virtual void OnAfterLabelEdit(NodeLabelEditEventArgs e)
    {
        AfterLabelEdit?.Invoke(this, e);
    }

    protected virtual void OnDrawNode(DrawTreeNodeEventArgs e)
    {
        DrawNode?.Invoke(this, e);
    }

    public void RaiseDrawNode(DrawTreeNodeEventArgs e)
    {
        OnDrawNode(e);
    }

    protected virtual void OnItemDrag(ItemDragEventArgs e)
    {
        ItemDrag?.Invoke(this, e);
    }

    internal bool RaiseBeforeExpand(TreeViewCancelEventArgs e)
    {
        OnBeforeExpand(e);
        return !e.Cancel;
    }

    internal void RaiseAfterExpand(TreeViewEventArgs e)
    {
        OnAfterExpand(e);
    }

    internal bool RaiseBeforeCollapse(TreeViewCancelEventArgs e)
    {
        OnBeforeCollapse(e);
        return !e.Cancel;
    }

    internal void RaiseAfterCollapse(TreeViewEventArgs e)
    {
        OnAfterCollapse(e);
    }

    internal void NotifyNodeChecked(TreeNode node)
    {
        Invalidate();
        if (ReferenceEquals(_notifyingCheckedNode, node))
        {
            return;
        }

        TreeNode? previousNotifyingNode = _notifyingCheckedNode;
        _notifyingCheckedNode = node;
        try
        {
            OnAfterCheck(new TreeViewEventArgs(node, TreeViewAction.Unknown));
        }
        finally
        {
            _notifyingCheckedNode = previousNotifyingNode;
        }
    }

    internal void ClearSelectionForRemovedNode(TreeNode node)
    {
        if (_selectedNode == null || !IsNodeOrDescendant(node, _selectedNode))
        {
            return;
        }

        _selectedNode = null;
        Invalidate();
    }

    internal TreeNodeLayout CreateNodeLayout(TreeNode node, int depth, int visibleIndex)
    {
        int rowHeight = GetRowHeight();
        int rowTop = ContentTop + (visibleIndex * rowHeight) - _verticalScrollOffset;
        int clientRight = Math.Max(BorderInset, ClientSize.Width - BorderInset);
        var rowBounds = new Rectangle(
            BorderInset,
            rowTop,
            Math.Max(0, clientRight - BorderInset),
            rowHeight);

        int glyphLeft = 4 + (depth * IndentWidth);
        var ownerDrawBounds = new Rectangle(
            glyphLeft,
            rowTop,
            Math.Max(0, clientRight - glyphLeft),
            rowHeight);
        Rectangle glyphBounds = node.Nodes.Count > 0
            ? new Rectangle(glyphLeft, rowTop, GlyphSlotWidth, rowHeight)
            : Rectangle.Empty;

        int contentLeft = glyphLeft + GlyphSlotWidth;
        Rectangle imageBounds = Rectangle.Empty;
        if (HasNodeImage(node))
        {
            Size imageSize = _imageList!.ImageSize;
            int imageTop = rowTop + Math.Max(0, (rowHeight - imageSize.Height) / 2);
            imageBounds = new Rectangle(contentLeft, imageTop, imageSize.Width, imageSize.Height);
            contentLeft += imageSize.Width + ImageGap;
        }

        var textBounds = new Rectangle(
            contentLeft,
            rowTop,
            Math.Min(
                Math.Max(0, clientRight - contentLeft),
                EstimateNodeLabelWidth(node.Text)),
            rowHeight);
        Rectangle selectionBounds = FullRowSelect ? rowBounds : textBounds;
        node.Bounds = textBounds;
        return new TreeNodeLayout(
            node,
            visibleIndex,
            depth,
            rowBounds,
            ownerDrawBounds,
            glyphBounds,
            imageBounds,
            textBounds,
            selectionBounds);
    }

    private static bool IsNodeOrDescendant(TreeNode root, TreeNode candidate)
    {
        for (TreeNode? current = candidate; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        TreeNode? target = null;
        bool handled = true;
        switch (e.KeyCode)
        {
            case Keys.Home:
                target = GetFirstVisibleNode();
                break;
            case Keys.End:
                target = GetLastVisibleNode();
                break;
            case Keys.Up:
                target = _selectedNode == null ? GetLastVisibleNode() : GetPreviousVisibleNode(_selectedNode);
                break;
            case Keys.Down:
                target = _selectedNode == null ? GetFirstVisibleNode() : GetNextVisibleNode(_selectedNode);
                break;
            case Keys.Right:
                if (_selectedNode == null)
                {
                    target = GetFirstVisibleNode();
                }
                else if (_selectedNode.Nodes.Count > 0 && !_selectedNode.IsExpanded)
                {
                    _selectedNode.Expand();
                    _selectedNode.EnsureVisible();
                }
                else if (_selectedNode.IsExpanded)
                {
                    target = FindFirstVisibleNode(_selectedNode.Nodes, 0);
                }
                break;
            case Keys.Left:
                if (_selectedNode?.IsExpanded == true && _selectedNode.Nodes.Count > 0)
                {
                    _selectedNode.Collapse();
                    _selectedNode.EnsureVisible();
                }
                else
                {
                    target = _selectedNode?.Parent;
                }
                break;
            default:
                handled = false;
                break;
        }

        if (!handled)
        {
            return;
        }

        if (target != null)
        {
            SelectNode(target, TreeViewAction.ByKeyboard);
        }

        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Delta == 0)
        {
            return;
        }

        int wheelSteps = Math.Max(1, Math.Abs(e.Delta) / 120);
        int direction = e.Delta > 0 ? -1 : 1;
        int scrollLines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        SetVerticalScrollOffset(_verticalScrollOffset + (direction * wheelSteps * scrollLines * GetRowHeight()));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        ClampVerticalScrollOffset();
        base.OnSizeChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _imageList != null)
        {
            _imageList.Changed -= OnImageListChanged;
        }

        base.Dispose(disposing);
    }

    private void SelectNode(TreeNode? value, TreeViewAction action)
    {
        if (ReferenceEquals(_selectedNode, value))
        {
            value?.EnsureVisible();
            return;
        }

        if (value != null && !ReferenceEquals(value.TreeView, this))
        {
            return;
        }

        if (value != null)
        {
            var cancelEventArgs = new TreeViewCancelEventArgs(value, false, action);
            OnBeforeSelect(cancelEventArgs);
            if (cancelEventArgs.Cancel)
            {
                return;
            }
        }

        _selectedNode = value;
        value?.EnsureVisible();
        Invalidate();
        if (value != null)
        {
            OnAfterSelect(new TreeViewEventArgs(value, action));
        }
    }

    private static void ExpandAncestors(TreeNode? node)
    {
        if (node == null)
        {
            return;
        }

        ExpandAncestors(node.Parent);
        node.Expand();
    }

    private TreeNode? GetFirstVisibleNode()
    {
        return FindFirstVisibleNode(Nodes, 0);
    }

    private TreeNode? GetLastVisibleNode()
    {
        TreeNode? last = null;
        TreeNodeLayoutEnumerator layouts = GetVisibleNodeLayouts().GetEnumerator();
        while (layouts.MoveNext())
        {
            last = layouts.Current.Node;
        }

        return last;
    }

    private TreeNode? GetPreviousVisibleNode(TreeNode node)
    {
        TreeNode? previous = null;
        TreeNodeLayoutEnumerator layouts = GetVisibleNodeLayouts().GetEnumerator();
        while (layouts.MoveNext())
        {
            if (ReferenceEquals(layouts.Current.Node, node))
            {
                return previous;
            }

            previous = layouts.Current.Node;
        }

        return null;
    }

    private TreeNode? GetNextVisibleNode(TreeNode node)
    {
        bool found = false;
        TreeNodeLayoutEnumerator layouts = GetVisibleNodeLayouts().GetEnumerator();
        while (layouts.MoveNext())
        {
            if (found)
            {
                return layouts.Current.Node;
            }

            found = ReferenceEquals(layouts.Current.Node, node);
        }

        return null;
    }

    private static TreeNode? FindFirstVisibleNode(TreeNodeCollection nodes, int startIndex)
    {
        for (int index = Math.Max(0, startIndex); index < nodes.Count; index++)
        {
            if (nodes[index].IsVisible)
            {
                return nodes[index];
            }
        }

        return null;
    }

    private int EstimateNodeLabelWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        float fontPixels = Math.Max(1f, Font.SizeInPoints * (96f / 72f));
        int width = (int)MathF.Ceiling(text.Length * fontPixels * 0.62f) + 4;
        return Math.Max(1, width);
    }

    private bool HasNodeImage(TreeNode node)
    {
        if (_imageList == null || _imageList.Images.Count == 0)
        {
            return false;
        }

        bool selected = ReferenceEquals(_selectedNode, node);
        string key = selected ? node.SelectedImageKey : node.ImageKey;
        if (!string.IsNullOrEmpty(key) && _imageList.Images.ContainsKey(key))
        {
            return true;
        }

        int index = selected ? node.SelectedImageIndex : node.ImageIndex;
        if (index < 0)
        {
            index = selected ? SelectedImageIndex : ImageIndex;
        }

        return index >= 0 && index < _imageList.Images.Count;
    }

    private int GetRowHeight()
    {
        return Math.Max(DefaultRowHeight, (_imageList?.ImageSize.Height ?? 0) + 2);
    }

    private int GetVisibleNodeCount()
    {
        int count = 0;
        CountVisibleNodes(Nodes, ref count);
        return count;
    }

    private static void CountVisibleNodes(TreeNodeCollection nodes, ref int count)
    {
        foreach (TreeNode node in nodes)
        {
            if (!node.IsVisible)
            {
                continue;
            }

            count++;
            if (node.IsExpanded)
            {
                CountVisibleNodes(node.Nodes, ref count);
            }
        }
    }

    private int GetMaximumVerticalScrollOffset()
    {
        int contentBottom = ContentTop + (GetVisibleNodeCount() * GetRowHeight());
        int viewportBottom = Math.Max(ContentTop, ClientSize.Height - BorderInset);
        return Math.Max(0, contentBottom - viewportBottom);
    }

    private void ClampVerticalScrollOffset()
    {
        _verticalScrollOffset = Math.Clamp(_verticalScrollOffset, 0, GetMaximumVerticalScrollOffset());
    }

    private void SetVerticalScrollOffset(int value)
    {
        int next = Math.Clamp(value, 0, GetMaximumVerticalScrollOffset());
        if (_verticalScrollOffset == next)
        {
            return;
        }

        _verticalScrollOffset = next;
        Invalidate();
    }

    private void OnImageListChanged(object? sender, EventArgs e)
    {
        Invalidate();
    }
}

public readonly struct TreeNodeLayout
{
    public TreeNodeLayout(
        TreeNode node,
        int visibleIndex,
        int depth,
        Rectangle rowBounds,
        Rectangle ownerDrawBounds,
        Rectangle glyphBounds,
        Rectangle imageBounds,
        Rectangle textBounds,
        Rectangle selectionBounds)
    {
        Node = node;
        VisibleIndex = visibleIndex;
        Depth = depth;
        RowBounds = rowBounds;
        OwnerDrawBounds = ownerDrawBounds;
        GlyphBounds = glyphBounds;
        ImageBounds = imageBounds;
        TextBounds = textBounds;
        SelectionBounds = selectionBounds;
    }

    public TreeNode Node { get; }

    public int VisibleIndex { get; }

    public int Depth { get; }

    public Rectangle RowBounds { get; }

    public Rectangle OwnerDrawBounds { get; }

    public Rectangle GlyphBounds { get; }

    public Rectangle ImageBounds { get; }

    public Rectangle TextBounds { get; }

    public Rectangle SelectionBounds { get; }
}

public readonly struct TreeNodeLayoutEnumerable
{
    private readonly TreeView _owner;

    internal TreeNodeLayoutEnumerable(TreeView owner)
    {
        _owner = owner;
    }

    public TreeNodeLayoutEnumerator GetEnumerator()
    {
        return new TreeNodeLayoutEnumerator(_owner);
    }
}

[InlineArray(8)]
internal struct TreeNodeTraversalFrameBuffer
{
    private TreeNodeTraversalFrame _element0;
}

internal struct TreeNodeTraversalFrame
{
    public TreeNodeTraversalFrame(TreeNodeCollection nodes, int depth)
    {
        Nodes = nodes;
        NextIndex = 0;
        Depth = depth;
    }

    public TreeNodeCollection Nodes;

    public int NextIndex;

    public int Depth;
}

public struct TreeNodeLayoutEnumerator
{
    private const int InlineFrameCapacity = 8;
    private readonly TreeView _owner;
    private TreeNodeTraversalFrameBuffer _inlineFrames;
    private TreeNodeTraversalFrame[]? _overflowFrames;
    private int _frameCount;
    private int _visibleIndex;
    private bool _initialized;

    internal TreeNodeLayoutEnumerator(TreeView owner)
    {
        _owner = owner;
        _inlineFrames = default;
        _overflowFrames = null;
        _frameCount = 0;
        _visibleIndex = -1;
        _initialized = false;
        CollectionAccessCount = 0;
        Current = default;
    }

    public TreeNodeLayout Current { get; private set; }

    public int CollectionAccessCount { get; private set; }

    public bool MoveNext()
    {
        if (!_initialized)
        {
            _initialized = true;
            PushFrame(_owner.Nodes, 0);
        }

        while (_frameCount > 0)
        {
            int frameIndex = _frameCount - 1;
            TreeNodeTraversalFrame frame = GetFrame(frameIndex);
            if (frame.NextIndex >= frame.Nodes.Count)
            {
                _frameCount--;
                continue;
            }

            TreeNode node = frame.Nodes[frame.NextIndex++];
            SetFrame(frameIndex, frame);
            CollectionAccessCount++;
            if (!node.IsVisible)
            {
                continue;
            }

            int depth = frame.Depth;
            if (node.IsExpanded && node.Nodes.Count > 0)
            {
                PushFrame(node.Nodes, depth + 1);
            }

            _visibleIndex++;
            Current = _owner.CreateNodeLayout(node, depth, _visibleIndex);
            return true;
        }

        return false;
    }

    private void PushFrame(TreeNodeCollection nodes, int depth)
    {
        int frameIndex = _frameCount++;
        if (frameIndex >= InlineFrameCapacity)
        {
            int overflowIndex = frameIndex - InlineFrameCapacity;
            if (_overflowFrames == null)
            {
                _overflowFrames = new TreeNodeTraversalFrame[4];
            }
            else if (overflowIndex >= _overflowFrames.Length)
            {
                Array.Resize(ref _overflowFrames, _overflowFrames.Length * 2);
            }
        }

        SetFrame(frameIndex, new TreeNodeTraversalFrame(nodes, depth));
    }

    private TreeNodeTraversalFrame GetFrame(int frameIndex)
    {
        if (frameIndex < InlineFrameCapacity)
        {
            return _inlineFrames[frameIndex];
        }

        return _overflowFrames![frameIndex - InlineFrameCapacity];
    }

    private void SetFrame(int frameIndex, TreeNodeTraversalFrame frame)
    {
        if (frameIndex < InlineFrameCapacity)
        {
            _inlineFrames[frameIndex] = frame;
            return;
        }

        _overflowFrames![frameIndex - InlineFrameCapacity] = frame;
    }
}

public class TreeNode
{
    private TreeView? _treeView;
    private bool _checked;
    private bool _isVisible = true;
    private int _imageIndex = -1;
    private string _imageKey = string.Empty;
    private int _selectedImageIndex = -1;
    private string _selectedImageKey = string.Empty;
    private string _text = string.Empty;

    public TreeNode()
    {
        Nodes = new TreeNodeCollection(null, this);
    }

    public TreeNode(string text)
        : this()
    {
        Text = text;
    }

    public TreeNode(string text, int imageIndex, int selectedImageIndex)
        : this(text)
    {
        ImageIndex = imageIndex;
        SelectedImageIndex = selectedImageIndex;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            TreeView?.NotifyNodeChecked(this);
        }
    }

    public bool IsEditing { get; private set; }

    public bool IsExpanded { get; private set; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }

            _isVisible = value;
            TreeView?.Invalidate();
        }
    }

    public int ImageIndex
    {
        get => _imageIndex;
        set
        {
            if (_imageIndex == value && (value < 0 || _imageKey.Length == 0))
            {
                return;
            }

            _imageIndex = value;
            if (value >= 0)
            {
                _imageKey = string.Empty;
            }

            TreeView?.Invalidate();
        }
    }

    public string ImageKey
    {
        get => _imageKey;
        set
        {
            string next = value ?? string.Empty;
            if (_imageKey == next && (next.Length == 0 || _imageIndex == -1))
            {
                return;
            }

            _imageKey = next;
            if (next.Length > 0)
            {
                _imageIndex = -1;
            }

            TreeView?.Invalidate();
        }
    }

    public int SelectedImageIndex
    {
        get => _selectedImageIndex;
        set
        {
            if (_selectedImageIndex == value && (value < 0 || _selectedImageKey.Length == 0))
            {
                return;
            }

            _selectedImageIndex = value;
            if (value >= 0)
            {
                _selectedImageKey = string.Empty;
            }

            TreeView?.Invalidate();
        }
    }

    public string SelectedImageKey
    {
        get => _selectedImageKey;
        set
        {
            string next = value ?? string.Empty;
            if (_selectedImageKey == next && (next.Length == 0 || _selectedImageIndex == -1))
            {
                return;
            }

            _selectedImageKey = next;
            if (next.Length > 0)
            {
                _selectedImageIndex = -1;
            }

            TreeView?.Invalidate();
        }
    }

    public ContextMenuStrip? ContextMenuStrip { get; set; }

    public TreeNodeCollection Nodes { get; }

    public string Name { get; set; } = string.Empty;

    public TreeNode? Parent { get; internal set; }

    public string Text
    {
        get => _text;
        set
        {
            string next = value ?? string.Empty;
            if (_text == next)
            {
                return;
            }

            _text = next;
            TreeView?.Invalidate();
        }
    }

    public object? Tag { get; set; }

    public TreeView? TreeView => _treeView ?? Parent?.TreeView;

    public Rectangle Bounds { get; set; }

    public string FullPath => Parent != null && !string.IsNullOrEmpty(Parent.FullPath)
        ? Parent.FullPath + "\\" + Text
        : Text;

    public void BeginEdit()
    {
        IsEditing = true;
    }

    public void EndEdit(bool cancel)
    {
        IsEditing = false;
    }

    public void EnsureVisible()
    {
        TreeView?.EnsureNodeVisible(this);
    }

    public void Expand()
    {
        if (IsExpanded)
        {
            return;
        }

        TreeView? treeView = TreeView;
        if (treeView != null)
        {
            var e = new TreeViewCancelEventArgs(this, false, TreeViewAction.Expand);
            if (!treeView.RaiseBeforeExpand(e))
            {
                return;
            }
        }

        IsExpanded = true;
        treeView?.Invalidate();
        treeView?.RaiseAfterExpand(new TreeViewEventArgs(this, TreeViewAction.Expand));
    }

    public void ExpandAll()
    {
        Expand();
        foreach (TreeNode node in Nodes)
        {
            node.ExpandAll();
        }
    }

    public void Collapse()
    {
        if (!IsExpanded)
        {
            return;
        }

        TreeView? treeView = TreeView;
        if (treeView != null)
        {
            var e = new TreeViewCancelEventArgs(this, false, TreeViewAction.Collapse);
            if (!treeView.RaiseBeforeCollapse(e))
            {
                return;
            }
        }

        IsExpanded = false;
        treeView?.Invalidate();
        treeView?.RaiseAfterCollapse(new TreeViewEventArgs(this, TreeViewAction.Collapse));
    }

    public void Collapse(bool ignoreChildren)
    {
        if (!ignoreChildren)
        {
            foreach (TreeNode node in Nodes)
            {
                node.Collapse(false);
            }
        }

        Collapse();
    }

    public void Toggle()
    {
        if (IsExpanded)
        {
            Collapse();
        }
        else
        {
            Expand();
        }
    }

    public void Remove()
    {
        if (Parent != null)
        {
            Parent.Nodes.Remove(this);
            return;
        }

        TreeView?.Nodes.Remove(this);
    }

    internal void SetTreeView(TreeView? treeView)
    {
        _treeView = treeView;
        foreach (TreeNode node in Nodes)
        {
            node.SetTreeView(treeView);
        }
    }
}

public class TreeNodeCollection : Collection<TreeNode>
{
    private readonly TreeView? _treeView;
    private readonly TreeNode? _parent;

    internal TreeNodeCollection(TreeView? treeView, TreeNode? parent)
    {
        _treeView = treeView;
        _parent = parent;
    }

    public new int Add(TreeNode node)
    {
        base.Add(node);
        return Count - 1;
    }

    public TreeNode Add(string text)
    {
        var node = new TreeNode(text);
        Add(node);
        return node;
    }

    public void AddRange(TreeNode[] nodes)
    {
        foreach (TreeNode node in nodes)
        {
            Add(node);
        }
    }

    public void CopyTo(TreeNode[] array, int arrayIndex)
    {
        for (int i = 0; i < Count; i++)
        {
            array[arrayIndex + i] = this[i];
        }
    }

    internal void Sort(IComparer comparer)
    {
        var nodes = new List<TreeNode>(this);
        nodes.Sort((x, y) => comparer.Compare(x, y));
        ClearItems();
        foreach (TreeNode node in nodes)
        {
            Add(node);
        }
    }

    protected override void InsertItem(int index, TreeNode item)
    {
        item.Parent = _parent;
        item.SetTreeView(_treeView ?? _parent?.TreeView);
        base.InsertItem(index, item);
        item.TreeView?.Invalidate();
    }

    protected override void RemoveItem(int index)
    {
        TreeNode item = this[index];
        TreeView? treeView = item.TreeView;
        treeView?.ClearSelectionForRemovedNode(item);
        item.Parent = null;
        item.SetTreeView(null);
        base.RemoveItem(index);
        treeView?.Invalidate();
    }

    protected override void ClearItems()
    {
        TreeView? treeView = _treeView ?? _parent?.TreeView;
        foreach (TreeNode node in this)
        {
            treeView?.ClearSelectionForRemovedNode(node);
            node.Parent = null;
            node.SetTreeView(null);
        }

        base.ClearItems();
        treeView?.Invalidate();
    }
}

public class ColumnHeader
{
    public string? ImageKey { get; set; }

    public string Text { get; set; } = string.Empty;

    public HorizontalAlignment TextAlign { get; set; }

    public int Width { get; set; }
}

public class ListViewGroup
{
    public ListViewGroup()
    {
    }

    public ListViewGroup(string header, HorizontalAlignment headerAlignment)
    {
        Header = header;
        HeaderAlignment = headerAlignment;
    }

    public ListViewGroup(string key, string headerText)
    {
        Name = key;
        Header = headerText;
    }

    public string Header { get; set; } = string.Empty;

    public HorizontalAlignment HeaderAlignment { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class ListViewItem
{
    private bool _checked;
    private bool _isEditing;
    private bool _selected;
    private int _imageIndex = -1;
    private string _text = string.Empty;
    private ListView? _owner;

    public ListViewItem()
    {
    }

    public ListViewItem(string text)
    {
        Text = text;
        SubItems.Add(new ListViewSubItem(this, text));
    }

    public ListViewItem(string text, int imageIndex)
        : this(text)
    {
        ImageIndex = imageIndex;
    }

    public ListViewItem(string[] items)
    {
        if (items.Length > 0)
        {
            Text = items[0];
        }

        foreach (string item in items)
        {
            SubItems.Add(new ListViewSubItem(this, item));
        }
    }

    public ListViewItem(string[] items, int imageIndex)
        : this(items)
    {
        ImageIndex = imageIndex;
    }

    public ListViewSubItemCollection SubItems { get; } = new();

    public string Text
    {
        get => _text;
        set
        {
            string next = value ?? string.Empty;
            if (string.Equals(_text, next, StringComparison.Ordinal))
            {
                return;
            }

            _text = next;
            _owner?.Invalidate();
        }
    }

    public object? Tag { get; set; }

    public int ImageIndex
    {
        get => _imageIndex;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, -1);

            if (_imageIndex == value)
            {
                return;
            }

            _imageIndex = value;
            _owner?.Invalidate();
        }
    }

    public ListViewGroup? Group { get; set; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_owner != null)
            {
                _owner.SetItemSelected(this, value, true);
            }
            else
            {
                _selected = value;
            }
        }
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_owner != null)
            {
                _owner.SetItemChecked(this, value, true);
            }
            else
            {
                _checked = value;
            }
        }
    }

    internal ListView? Owner => _owner;

    internal bool CheckedCore => _checked;

    internal bool SelectedCore => _selected;

    public void BeginEdit()
    {
        _isEditing = true;
    }

    public void EnsureVisible()
    {
        int index = _owner?.Items.IndexOf(this) ?? -1;
        if (index >= 0)
        {
            _owner!.EnsureVisible(index);
        }
    }

    internal void SetOwner(ListView? owner)
    {
        _owner = owner;
    }

    internal void SetCheckedCore(bool value)
    {
        _checked = value;
    }

    internal void SetSelectedCore(bool selected)
    {
        _selected = selected;
    }

    public sealed class ListViewSubItem
    {
        public ListViewSubItem()
        {
        }

        public ListViewSubItem(ListViewItem? owner, string text)
        {
            Owner = owner;
            Text = text;
        }

        public ListViewItem? Owner { get; }

        public string Text { get; set; } = string.Empty;
    }

    public sealed class ListViewSubItemCollection : Collection<ListViewSubItem>
    {
        public int Add(string text)
        {
            Add(new ListViewSubItem(null!, text));
            return Count - 1;
        }
    }
}

public sealed class ImageList : Component
{
    private Size _imageSize = new(16, 16);

    public ImageList()
    {
        Images = new ImageCollection(OnChanged);
    }

    public ImageList(IContainer container)
        : this()
    {
        container?.Add(this);
    }

    internal event EventHandler? Changed;

    public ImageCollection Images { get; }

    public ColorDepth ColorDepth { get; set; }

    public ImageListStreamer? ImageStream { get; set; }

    public Size ImageSize
    {
        get => _imageSize;
        set
        {
            if (value.Width <= 0 || value.Height <= 0)
            {
                throw new ArgumentException("ImageSize dimensions must be positive.", nameof(value));
            }

            if (_imageSize == value)
            {
                return;
            }

            _imageSize = value;
            OnChanged();
        }
    }

    public Color TransparentColor { get; set; } = Color.Transparent;

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public sealed class ImageCollection
    {
        private readonly List<Image> _images = new();
        private readonly List<string> _keys = new();
        private readonly Action _changed;

        internal ImageCollection(Action changed)
        {
            _changed = changed;
        }

        public int Count => _images.Count;

        public bool Empty => _images.Count == 0;

        public ICollection Keys => _keys.AsReadOnly();

        public Image this[int index]
        {
            get => _images[index];
            set
            {
                _images[index] = value;
                _changed();
            }
        }

        public Image? this[string key]
        {
            get
            {
                int index = IndexOfKey(key);
                return index >= 0 ? _images[index] : null;
            }
        }

        public int Add(Icon icon)
        {
            return Add(icon.ToBitmap());
        }

        public int Add(Image image)
        {
            _images.Add(image);
            _keys.Add(string.Empty);
            _changed();
            return _images.Count - 1;
        }

        public void Add(string key, Icon icon)
        {
            Add(key, icon.ToBitmap());
        }

        public void Add(string key, Image image)
        {
            int index = Add(image);
            SetKeyName(index, key);
        }

        public void AddRange(Image[] images)
        {
            if (images == null)
            {
                return;
            }

            foreach (Image image in images)
            {
                Add(image);
            }
        }

        public void Clear()
        {
            if (_images.Count == 0)
            {
                return;
            }

            _images.Clear();
            _keys.Clear();
            _changed();
        }

        public bool ContainsKey(string key)
        {
            return IndexOfKey(key) >= 0;
        }

        public int IndexOf(Image image)
        {
            return _images.IndexOf(image);
        }

        public int IndexOfKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return -1;
            }

            for (int i = 0; i < _keys.Count; i++)
            {
                if (string.Equals(_keys[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        public void Remove(Image image)
        {
            int index = IndexOf(image);
            if (index >= 0)
            {
                RemoveAt(index);
            }
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _images.Count)
            {
                return;
            }

            _images.RemoveAt(index);
            _keys.RemoveAt(index);
            _changed();
        }

        public void RemoveByKey(string key)
        {
            int index = IndexOfKey(key);
            if (index >= 0)
            {
                RemoveAt(index);
            }
        }

        public void SetKeyName(int index, string name)
        {
            if (index < 0 || index >= _keys.Count)
            {
                return;
            }

            _keys[index] = name ?? string.Empty;
            _changed();
        }
    }
}

public sealed class ImageListStreamer
{
}

public class NativeWindow
{
    public IntPtr Handle { get; private set; }

    public void AssignHandle(IntPtr handle)
    {
        Handle = handle;
    }

    public void ReleaseHandle()
    {
        Handle = IntPtr.Zero;
    }

    protected virtual void WndProc(ref Message m)
    {
    }

    protected virtual void OnThreadException(Exception e)
    {
    }
}

public interface IDataObject
{
    object? GetData(string format);

    object? GetData(Type format);

    object? GetData(string format, bool autoConvert);

    bool GetDataPresent(string format);

    bool GetDataPresent(Type format);

    bool GetDataPresent(string format, bool autoConvert);

    string[] GetFormats()
    {
        return Array.Empty<string>();
    }

    string[] GetFormats(bool autoConvert)
    {
        return GetFormats();
    }
}

public class DataObject : IDataObject
{
    private readonly Dictionary<string, object?> _data = new(StringComparer.Ordinal);

    public DataObject()
    {
    }

    public DataObject(object data)
    {
        SetData(data);
    }

    public DataObject(string format, object? data)
    {
        SetData(format, data);
    }

    public object? GetData(string format)
    {
        return _data.TryGetValue(format, out var value) ? value : null;
    }

    public object? GetData(Type format)
    {
        return GetData(format.FullName ?? format.Name);
    }

    public object? GetData(string format, bool autoConvert)
    {
        return GetData(format);
    }

    public bool GetDataPresent(string format)
    {
        return _data.ContainsKey(format);
    }

    public bool GetDataPresent(Type format)
    {
        return GetDataPresent(format.FullName ?? format.Name);
    }

    public bool GetDataPresent(string format, bool autoConvert)
    {
        return GetDataPresent(format);
    }

    public string[] GetFormats()
    {
        return _data.Keys.ToArray();
    }

    public string[] GetFormats(bool autoConvert)
    {
        return GetFormats();
    }

    public void SetData(string format, object? data)
    {
        _data[format] = data;
    }

    public void SetData(Type format, object? data)
    {
        SetData(format.FullName ?? format.Name, data);
    }

    public void SetData(object? data)
    {
        if (data != null)
        {
            if (data is string text)
            {
                SetData(DataFormats.Text, text);
                SetData(DataFormats.UnicodeText, text);
                SetData(DataFormats.StringFormat, text);
            }

            SetData(data.GetType(), data);
        }
    }
}

public static class DataFormats
{
    public const string FileDrop = "FileDrop";
    public const string StringFormat = "String";
    public const string Text = "Text";
    public const string UnicodeText = "UnicodeText";
}

public static class Clipboard
{
    public static void Clear()
    {
        PortableWinFormsClipboardService.Clear();
    }

    public static void SetDataObject(object data)
    {
        PortableWinFormsClipboardService.SetDataObject(data);
    }

    public static IDataObject? GetDataObject()
    {
        return PortableWinFormsClipboardService.GetDataObject();
    }

    public static void SetText(string text)
    {
        PortableWinFormsClipboardService.SetText(text);
    }

    public static string GetText()
    {
        return PortableWinFormsClipboardService.GetText();
    }

    public static bool ContainsText()
    {
        return PortableWinFormsClipboardService.ContainsText();
    }
}

public static class Cursors
{
    public static Cursor Default { get; } = new Cursor();
    public static Cursor WaitCursor { get; } = new Cursor();
}

public static class Application
{
    private static readonly List<IMessageFilter> s_messageFilters = new();
    private static IWinFormsApplicationHost? s_applicationHost;

    public static string StartupPath => AppContext.BaseDirectory;

    public static event EventHandler? Idle;
    public static event ThreadExceptionEventHandler? ThreadException;

    public static void OnThreadException(Exception e)
    {
        ThreadException?.Invoke(typeof(Application), new ThreadExceptionEventArgs(e));
    }

    public static ApartmentState OleRequired()
    {
        return ApartmentState.STA;
    }

    public static void SetCompatibleTextRenderingDefault(bool defaultValue)
    {
    }

    public static void AddMessageFilter(IMessageFilter value)
    {
        if (!s_messageFilters.Contains(value))
        {
            s_messageFilters.Add(value);
        }
    }

    public static void RemoveMessageFilter(IMessageFilter value)
    {
        s_messageFilters.Remove(value);
    }

    public static bool FilterMessage(ref Message message)
    {
        foreach (IMessageFilter filter in s_messageFilters.ToArray())
        {
            if (filter.PreFilterMessage(ref message))
            {
                return true;
            }
        }

        return false;
    }

    public static void RaiseIdle(EventArgs e)
    {
        Idle?.Invoke(typeof(Application), e);
    }

    public static void RegisterPortableApplicationHost(IWinFormsApplicationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Volatile.Write(ref s_applicationHost, host);
    }

    internal static IWinFormsDispatcherHost? GetDispatcherHost()
    {
        return Volatile.Read(ref s_applicationHost) as IWinFormsDispatcherHost;
    }

    internal static DragDropEffects DoDragDrop(
        Control source,
        IDataObject data,
        DragDropEffects allowedEffects)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(data);

        return Volatile.Read(ref s_applicationHost) is IWinFormsDragDropHost dragDropHost
            ? dragDropHost.DoDragDrop(source, data, allowedEffects)
            : DragDropEffects.None;
    }

    internal static bool TryPointToScreen(
        Control control,
        Point point,
        out Point screenPoint)
    {
        if (Volatile.Read(ref s_applicationHost) is IWinFormsCoordinateHost coordinateHost)
        {
            return coordinateHost.TryPointToScreen(control, point, out screenPoint);
        }

        screenPoint = default;
        return false;
    }

    internal static bool TryPointToClient(
        Control control,
        Point point,
        out Point clientPoint)
    {
        if (Volatile.Read(ref s_applicationHost) is IWinFormsCoordinateHost coordinateHost)
        {
            return coordinateHost.TryPointToClient(control, point, out clientPoint);
        }

        clientPoint = default;
        return false;
    }

    internal static bool TryCreateGraphics(
        Control control,
        out Graphics graphics)
    {
        if (Volatile.Read(ref s_applicationHost) is IWinFormsGraphicsHost graphicsHost
            && graphicsHost.TryCreateGraphics(control, out graphics))
        {
            return true;
        }

        graphics = null!;
        return false;
    }

    public static void Run()
    {
    }

    public static void Run(Form mainForm)
    {
        ArgumentNullException.ThrowIfNull(mainForm);
        IWinFormsApplicationHost? applicationHost = Volatile.Read(ref s_applicationHost);
        if (applicationHost != null)
        {
            applicationHost.Run(mainForm);
            return;
        }

        mainForm.Show();
    }

    public static void ExitThread()
    {
        Volatile.Read(ref s_applicationHost)?.ExitThread();
    }

    internal static bool TryShowDialog(Form form, IWin32Window? owner, out DialogResult result)
    {
        IWinFormsApplicationHost? applicationHost = Volatile.Read(ref s_applicationHost);
        if (applicationHost == null)
        {
            result = default;
            return false;
        }

        result = applicationHost.ShowDialog(form, owner);
        return true;
    }

    internal static void RequestDialogCompletion(Form form)
    {
        if (Volatile.Read(ref s_applicationHost) is IWinFormsModalDialogHost modalDialogHost)
        {
            modalDialogHost.RequestDialogCompletion(form);
        }
    }

    internal static IDisposable RegisterTimer(int intervalMilliseconds, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (Volatile.Read(ref s_applicationHost) is not IWinFormsTimerHost timerHost)
        {
            throw new InvalidOperationException(
                "The registered WinForms application host does not support UI timers.");
        }

        return timerHost.RegisterTimer(intervalMilliseconds, callback);
    }
}

public static class MessageBox
{
    public static DialogResult Show(string? text)
    {
        return ShowCore(
            owner: null,
            text,
            caption: string.Empty,
            MessageBoxButtons.OK,
            MessageBoxIcon.None,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.None);
    }

    public static DialogResult Show(string? text, string? caption)
    {
        return ShowCore(
            owner: null,
            text,
            caption,
            MessageBoxButtons.OK,
            MessageBoxIcon.None,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.None);
    }

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons)
    {
        return ShowCore(
            owner: null,
            text,
            caption,
            buttons,
            MessageBoxIcon.None,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.None);
    }

    public static DialogResult Show(
        string? text,
        string? caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon)
    {
        return ShowCore(
            owner: null,
            text,
            caption,
            buttons,
            icon,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.None);
    }

    public static DialogResult Show(
        string? text,
        string? caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
    {
        return ShowCore(
            owner: null,
            text,
            caption,
            buttons,
            icon,
            defaultButton,
            MessageBoxOptions.None);
    }

    public static DialogResult Show(
        string? text,
        string? caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton,
        MessageBoxOptions options)
    {
        return ShowCore(owner: null, text, caption, buttons, icon, defaultButton, options);
    }

    public static DialogResult Show(IWin32Window? owner, string? text)
    {
        return ShowCore(
            owner,
            text,
            caption: string.Empty,
            MessageBoxButtons.OK,
            MessageBoxIcon.None,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.None);
    }

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption)
    {
        return ShowCore(
            owner,
            text,
            caption,
            MessageBoxButtons.OK,
            MessageBoxIcon.None,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.None);
    }

    public static DialogResult Show(
        IWin32Window? owner,
        string? text,
        string? caption,
        MessageBoxButtons buttons)
    {
        return ShowCore(
            owner,
            text,
            caption,
            buttons,
            MessageBoxIcon.None,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.None);
    }

    public static DialogResult Show(
        IWin32Window? owner,
        string? text,
        string? caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon)
    {
        return ShowCore(
            owner,
            text,
            caption,
            buttons,
            icon,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.None);
    }

    public static DialogResult Show(
        IWin32Window? owner,
        string? text,
        string? caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
    {
        return ShowCore(
            owner,
            text,
            caption,
            buttons,
            icon,
            defaultButton,
            MessageBoxOptions.None);
    }

    public static DialogResult Show(
        IWin32Window? owner,
        string? text,
        string? caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton,
        MessageBoxOptions options)
    {
        return ShowCore(owner, text, caption, buttons, icon, defaultButton, options);
    }

    private static DialogResult ShowCore(
        IWin32Window? owner,
        string? text,
        string? caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton,
        MessageBoxOptions options)
    {
        ValidateButtons(buttons);
        ValidateIcon(icon);
        ValidateDefaultButton(defaultButton);

        if (owner is not null
            && (options & (MessageBoxOptions.ServiceNotification | MessageBoxOptions.DefaultDesktopOnly)) != 0)
        {
            throw new ArgumentException(
                "A message box shown with ServiceNotification or DefaultDesktopOnly cannot have an owner.",
                nameof(options));
        }

        DialogResult fallbackResult = GetFallbackResult(buttons, defaultButton);
        return PortableWinFormsMessageBoxService.Show(
            owner,
            text,
            caption,
            buttons,
            icon,
            options,
            fallbackResult);
    }

    private static DialogResult GetFallbackResult(
        MessageBoxButtons buttons,
        MessageBoxDefaultButton defaultButton)
    {
        int defaultIndex = defaultButton switch
        {
            MessageBoxDefaultButton.Button2 => 1,
            MessageBoxDefaultButton.Button3 => 2,
            MessageBoxDefaultButton.Button4 => 3,
            _ => 0
        };

        return buttons switch
        {
            MessageBoxButtons.OK => DialogResult.OK,
            MessageBoxButtons.OKCancel when defaultIndex == 1 => DialogResult.Cancel,
            MessageBoxButtons.OKCancel => DialogResult.OK,
            MessageBoxButtons.AbortRetryIgnore when defaultIndex == 1 => DialogResult.Retry,
            MessageBoxButtons.AbortRetryIgnore when defaultIndex == 2 => DialogResult.Ignore,
            MessageBoxButtons.AbortRetryIgnore => DialogResult.Abort,
            MessageBoxButtons.YesNoCancel when defaultIndex == 1 => DialogResult.No,
            MessageBoxButtons.YesNoCancel when defaultIndex == 2 => DialogResult.Cancel,
            MessageBoxButtons.YesNoCancel => DialogResult.Yes,
            MessageBoxButtons.YesNo when defaultIndex == 1 => DialogResult.No,
            MessageBoxButtons.YesNo => DialogResult.Yes,
            MessageBoxButtons.RetryCancel when defaultIndex == 1 => DialogResult.Cancel,
            MessageBoxButtons.RetryCancel => DialogResult.Retry,
            MessageBoxButtons.CancelTryContinue when defaultIndex == 1 => DialogResult.TryAgain,
            MessageBoxButtons.CancelTryContinue when defaultIndex == 2 => DialogResult.Continue,
            MessageBoxButtons.CancelTryContinue => DialogResult.Cancel,
            _ => DialogResult.OK
        };
    }

    private static void ValidateButtons(MessageBoxButtons buttons)
    {
        if (buttons is not MessageBoxButtons.OK
            and not MessageBoxButtons.OKCancel
            and not MessageBoxButtons.AbortRetryIgnore
            and not MessageBoxButtons.YesNoCancel
            and not MessageBoxButtons.YesNo
            and not MessageBoxButtons.RetryCancel
            and not MessageBoxButtons.CancelTryContinue)
        {
            throw new InvalidEnumArgumentException(nameof(buttons), (int)buttons, typeof(MessageBoxButtons));
        }
    }

    private static void ValidateIcon(MessageBoxIcon icon)
    {
        if (icon is not MessageBoxIcon.None
            and not MessageBoxIcon.Hand
            and not MessageBoxIcon.Question
            and not MessageBoxIcon.Exclamation
            and not MessageBoxIcon.Asterisk)
        {
            throw new InvalidEnumArgumentException(nameof(icon), (int)icon, typeof(MessageBoxIcon));
        }
    }

    private static void ValidateDefaultButton(MessageBoxDefaultButton defaultButton)
    {
        if (defaultButton is not MessageBoxDefaultButton.Button1
            and not MessageBoxDefaultButton.Button2
            and not MessageBoxDefaultButton.Button3
            and not MessageBoxDefaultButton.Button4)
        {
            throw new InvalidEnumArgumentException(
                nameof(defaultButton),
                (int)defaultButton,
                typeof(MessageBoxDefaultButton));
        }
    }
}

public static class ControlPaint
{
    public static void DrawBorder3D(Graphics graphics, Rectangle rectangle, Border3DStyle style)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return;
        }

        if (style is not Border3DStyle.RaisedInner and not Border3DStyle.Sunken)
        {
            throw new InvalidEnumArgumentException(nameof(style), (int)style, typeof(Border3DStyle));
        }

        Color topLeftColor = style == Border3DStyle.Sunken
            ? SystemColors.ControlDark
            : SystemColors.ControlLightLight;
        Color bottomRightColor = style == Border3DStyle.Sunken
            ? SystemColors.ControlLightLight
            : SystemColors.ControlDark;

        int left = rectangle.Left;
        int top = rectangle.Top;
        int right = rectangle.Right - 1;
        int bottom = rectangle.Bottom - 1;
        using var topLeftPen = new Pen(topLeftColor);
        using var bottomRightPen = new Pen(bottomRightColor);
        graphics.DrawLine(topLeftPen, left, bottom, left, top);
        graphics.DrawLine(topLeftPen, left, top, right, top);
        graphics.DrawLine(bottomRightPen, right, top, right, bottom);
        graphics.DrawLine(bottomRightPen, right, bottom, left, bottom);
    }

    public static Color Light(Color baseColor)
    {
        return Color.FromArgb(
            baseColor.A,
            Math.Min(255, baseColor.R + ((255 - baseColor.R) / 2)),
            Math.Min(255, baseColor.G + ((255 - baseColor.G) / 2)),
            Math.Min(255, baseColor.B + ((255 - baseColor.B) / 2)));
    }
}

public static class SystemInformation
{
    public static BootMode BootMode => BootMode.Normal;

    public static Size DragSize => new(4, 4);

    public static Font MenuFont => SystemFonts.MenuFont;

    public static int MouseWheelScrollLines => 3;

    public static bool TerminalServerSession => false;

    public static int VerticalScrollBarWidth => 17;
}

public delegate void ColumnClickEventHandler(object sender, ColumnClickEventArgs e);

public delegate void LabelEditEventHandler(object sender, LabelEditEventArgs e);

public delegate void DragEventHandler(object sender, DragEventArgs e);

public delegate void MouseEventHandler(object sender, MouseEventArgs e);

public delegate void PaintEventHandler(object sender, PaintEventArgs e);

public delegate void DrawItemEventHandler(object sender, DrawItemEventArgs e);

public delegate void MeasureItemEventHandler(object sender, MeasureItemEventArgs e);

public delegate void ItemCheckEventHandler(object sender, ItemCheckEventArgs e);

public delegate void ScrollEventHandler(object sender, ScrollEventArgs e);

public delegate void KeyPressEventHandler(object sender, KeyPressEventArgs e);

public delegate void KeyEventHandler(object sender, KeyEventArgs e);

public delegate void PreviewKeyDownEventHandler(object sender, PreviewKeyDownEventArgs e);

public delegate void FormClosingEventHandler(object sender, FormClosingEventArgs e);

public delegate void FormClosedEventHandler(object sender, FormClosedEventArgs e);

public delegate void WebBrowserNavigatedEventHandler(object sender, WebBrowserNavigatedEventArgs e);

public delegate void WebBrowserDocumentCompletedEventHandler(object sender, WebBrowserDocumentCompletedEventArgs e);

public delegate void WebBrowserNavigatingEventHandler(object sender, WebBrowserNavigatingEventArgs e);

public delegate void PropertyValueChangedEventHandler(object sender, PropertyValueChangedEventArgs e);

public delegate void SelectedGridItemChangedEventHandler(object sender, SelectedGridItemChangedEventArgs e);

public delegate void LinkLabelLinkClickedEventHandler(object sender, LinkLabelLinkClickedEventArgs e);

public delegate void DataGridViewCellEventHandler(object sender, DataGridViewCellEventArgs e);

public delegate void DataGridViewDataErrorEventHandler(object sender, DataGridViewDataErrorEventArgs e);

public interface IMessageFilter
{
    bool PreFilterMessage(ref Message m);
}

public delegate void DataGridViewEditingControlShowingEventHandler(object sender, DataGridViewEditingControlShowingEventArgs e);

public delegate void DataGridViewRowsAddedEventHandler(object sender, DataGridViewRowsAddedEventArgs e);

public delegate void DataGridViewRowsRemovedEventHandler(object sender, DataGridViewRowsRemovedEventArgs e);

public delegate void TreeViewCancelEventHandler(object sender, TreeViewCancelEventArgs e);

public delegate void TreeViewEventHandler(object sender, TreeViewEventArgs e);

public delegate void NodeLabelEditEventHandler(object sender, NodeLabelEditEventArgs e);

public delegate void DrawTreeNodeEventHandler(object sender, DrawTreeNodeEventArgs e);

public delegate void ItemDragEventHandler(object sender, ItemDragEventArgs e);

public enum ImeMode
{
    NoControl = 0,
    On = 1,
    Off = 2,
    Disable = 3
}

public enum DialogResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Abort = 3,
    Retry = 4,
    Ignore = 5,
    Yes = 6,
    No = 7,
    TryAgain = 10,
    Continue = 11
}

public enum MessageBoxButtons
{
    OK = 0,
    OKCancel = 1,
    AbortRetryIgnore = 2,
    YesNoCancel = 3,
    YesNo = 4,
    RetryCancel = 5,
    CancelTryContinue = 6
}

public enum MessageBoxIcon
{
    None = 0,
    Hand = 16,
    Question = 32,
    Exclamation = 48,
    Asterisk = 64,
    Stop = Hand,
    Error = Hand,
    Warning = Exclamation,
    Information = Asterisk
}

public enum MessageBoxDefaultButton
{
    Button1 = 0,
    Button2 = 256,
    Button3 = 512,
    Button4 = 768
}

[Flags]
public enum MessageBoxOptions
{
    None = 0,
    DefaultDesktopOnly = 0x20000,
    RightAlign = 0x80000,
    RtlReading = 0x100000,
    ServiceNotification = 0x200000
}

[Flags]
public enum Keys
{
    None = 0,
    KeyCode = 0xFFFF,
    Return = 13,
    Enter = Return,
    Tab = 9,
    ShiftKey = 16,
    ControlKey = 17,
    Menu = 18,
    CapsLock = 20,
    Escape = 27,
    Space = 32,
    PageUp = 33,
    PageDown = 34,
    End = 35,
    Home = 36,
    Left = 37,
    Right = 39,
    Insert = 45,
    Back = 8,
    Backspace = Back,
    Delete = 46,
    D0 = 48,
    D1 = 49,
    D2 = 50,
    D3 = 51,
    D4 = 52,
    D5 = 53,
    D6 = 54,
    D7 = 55,
    D8 = 56,
    D9 = 57,
    A = 65,
    B = 66,
    C = 67,
    D = 68,
    E = 69,
    F = 70,
    G = 71,
    H = 72,
    I = 73,
    J = 74,
    K = 75,
    L = 76,
    M = 77,
    N = 78,
    O = 79,
    P = 80,
    Q = 81,
    R = 82,
    S = 83,
    T = 84,
    U = 85,
    V = 86,
    W = 87,
    X = 88,
    Y = 89,
    Z = 90,
    NumPad0 = 96,
    NumPad1 = 97,
    NumPad2 = 98,
    NumPad3 = 99,
    NumPad4 = 100,
    NumPad5 = 101,
    NumPad6 = 102,
    NumPad7 = 103,
    NumPad8 = 104,
    NumPad9 = 105,
    Multiply = 106,
    Add = 107,
    Separator = 108,
    Subtract = 109,
    Decimal = 110,
    Divide = 111,
    F1 = 112,
    F2 = 113,
    F3 = 114,
    F4 = 115,
    F5 = 116,
    F6 = 117,
    F7 = 118,
    F8 = 119,
    F9 = 120,
    F10 = 121,
    F11 = 122,
    F12 = 123,
    OemSemicolon = 186,
    Oemplus = 187,
    Oemcomma = 188,
    OemMinus = 189,
    OemPeriod = 190,
    OemQuestion = 191,
    Oemtilde = 192,
    OemOpenBrackets = 219,
    OemPipe = 220,
    OemCloseBrackets = 221,
    OemQuotes = 222,
    Up = 38,
    Down = 40,
    Shift = 0x10000,
    Control = 0x20000,
    Alt = 0x40000,
    Modifiers = unchecked((int)0xFFFF0000)
}

public struct Message
{
    public IntPtr HWnd;
    public int Msg;
    public IntPtr WParam;
    public IntPtr LParam;
    public IntPtr Result;
}

public enum Border3DStyle
{
    RaisedInner = 4,
    Sunken = 2
}

public enum BorderStyle
{
    None = 0,
    FixedSingle = 1,
    Fixed3D = 2
}

public enum ErrorBlinkStyle
{
    BlinkIfDifferentError = 0,
    AlwaysBlink = 1,
    NeverBlink = 2
}

public enum ErrorIconAlignment
{
    TopLeft = 0,
    TopRight = 1,
    MiddleLeft = 2,
    MiddleRight = 3,
    BottomLeft = 4,
    BottomRight = 5
}

public class AmbientProperties
{
    public Color BackColor { get; set; } = SystemColors.Control;
    public Cursor? Cursor { get; set; }
    public Font Font { get; set; } = SystemFonts.DefaultFont;
    public Color ForeColor { get; set; } = SystemColors.ControlText;
}

public sealed class ControlBindingsCollection : Collection<Binding>
{
    public ControlBindingsCollection(Control control)
    {
        Control = control;
    }

    public Control Control { get; }

    public Binding Add(string propertyName, object dataSource, string dataMember)
    {
        var binding = new Binding(propertyName, dataSource, dataMember);
        Add(binding);
        return binding;
    }
}

public sealed class Binding
{
    public Binding(string propertyName, object dataSource, string dataMember)
    {
        PropertyName = propertyName;
        DataSource = dataSource;
        DataMember = dataMember;
    }

    public object DataSource { get; }

    public string DataMember { get; }

    public string PropertyName { get; }
}

[Flags]
public enum AnchorStyles
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8
}

public enum Appearance
{
    Normal = 0,
    Button = 1
}

public enum AutoCompleteMode
{
    None = 0,
    Suggest = 1,
    Append = 2,
    SuggestAppend = 3
}

public enum AutoCompleteSource
{
    None = 0,
    HistoryList = 1,
    ListItems = 256,
    AllUrl = 6
}

public enum ToolStripItemDisplayStyle
{
    None = 0,
    Text = 1,
    Image = 2,
    ImageAndText = 3
}

public enum ToolStripItemImageScaling
{
    None = 0,
    SizeToFit = 1
}

public enum ToolStripGripStyle
{
    Hidden = 0,
    Visible = 1
}

public enum WebBrowserRefreshOption
{
    Normal = 0,
    IfExpired = 1,
    Completely = 3
}

public enum View
{
    LargeIcon = 0,
    Details = 1,
    SmallIcon = 2,
    List = 3,
    Tile = 4
}

public enum SortOrder
{
    None = 0,
    Ascending = 1,
    Descending = 2
}

public enum ProgressBarStyle
{
    Blocks = 0,
    Continuous = 1,
    Marquee = 2
}

public enum ScrollBars
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Both = 3
}

public enum RightToLeft
{
    No = 0,
    Yes = 1,
    Inherit = 2
}

public enum BootMode
{
    Normal = 0,
    FailSafe = 1,
    FailSafeWithNetwork = 2
}

public enum PictureBoxSizeMode
{
    Normal = 0,
    StretchImage = 1,
    AutoSize = 2,
    CenterImage = 3,
    Zoom = 4
}

public enum AutoScaleMode
{
    None = 0,
    Font = 1,
    Dpi = 2,
    Inherit = 3
}

public enum CheckState
{
    Unchecked = 0,
    Checked = 1,
    Indeterminate = 2
}

public enum SelectionMode
{
    None = 0,
    One = 1,
    MultiSimple = 2,
    MultiExtended = 3
}

public enum ComboBoxStyle
{
    DropDown = 1,
    Simple = 0,
    DropDownList = 2
}

public enum DrawMode
{
    Normal = 0,
    OwnerDrawFixed = 1,
    OwnerDrawVariable = 2
}

[Flags]
public enum DrawItemState
{
    None = 0,
    Selected = 1,
    Grayed = 2,
    Disabled = 4,
    Checked = 8,
    Focus = 16,
    Default = 32,
    HotLight = 64,
    Inactive = 128,
    NoAccelerator = 256,
    NoFocusRect = 512,
    ComboBoxEdit = 4096
}

[Flags]
public enum ControlStyles
{
    UserPaint = 0x2,
    AllPaintingInWmPaint = 0x2000,
    OptimizedDoubleBuffer = 0x20000,
    CacheText = 0x4000
}

public enum DockStyle
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 3,
    Right = 4,
    Fill = 5
}

[Flags]
public enum BoundsSpecified
{
    None = 0,
    X = 1,
    Y = 2,
    Width = 4,
    Height = 8,
    Location = X | Y,
    Size = Width | Height,
    All = Location | Size
}

public enum FlatStyle
{
    Flat = 0,
    Popup = 1,
    Standard = 2,
    System = 3
}

public enum FormBorderStyle
{
    None = 0,
    FixedSingle = 1,
    Fixed3D = 2,
    FixedDialog = 3,
    Sizable = 4,
    FixedToolWindow = 5,
    SizableToolWindow = 6
}

public enum FormStartPosition
{
    Manual = 0,
    CenterScreen = 1,
    WindowsDefaultLocation = 2,
    WindowsDefaultBounds = 3,
    CenterParent = 4
}

public enum FormWindowState
{
    Normal = 0,
    Minimized = 1,
    Maximized = 2
}

public enum HorizontalAlignment
{
    Left = 0,
    Right = 1,
    Center = 2
}

public enum ColumnHeaderStyle
{
    None = 0,
    Nonclickable = 1,
    Clickable = 2
}

public enum ListViewAlignment
{
    Default = 0,
    Left = 1,
    Top = 2,
    SnapToGrid = 5
}

public enum PropertySort
{
    NoSort = 0,
    Alphabetical = 1,
    Categorized = 2,
    CategorizedAlphabetical = 3
}

public enum DataGridViewAutoSizeColumnMode
{
    NotSet = 0,
    None = 1,
    ColumnHeader = 2,
    AllCellsExceptHeader = 4,
    AllCells = 6,
    DisplayedCellsExceptHeader = 8,
    DisplayedCells = 10,
    Fill = 16
}

public enum DataGridViewColumnHeadersHeightSizeMode
{
    EnableResizing = 0,
    DisableResizing = 1,
    AutoSize = 2
}

public enum DataGridViewRowHeadersWidthSizeMode
{
    EnableResizing = 0,
    DisableResizing = 1,
    AutoSizeToAllHeaders = 2,
    AutoSizeToDisplayedHeaders = 3,
    AutoSizeToFirstHeader = 4
}

public enum DataGridViewEditMode
{
    EditOnEnter = 0,
    EditOnKeystroke = 1,
    EditOnKeystrokeOrF2 = 2,
    EditOnF2 = 3,
    EditProgrammatically = 4
}

public enum CloseReason
{
    None = 0,
    WindowsShutDown = 1,
    MdiFormClosing = 2,
    UserClosing = 3,
    TaskManagerClosing = 4,
    FormOwnerClosing = 5,
    ApplicationExitCall = 6
}

public enum Orientation
{
    Horizontal = 0,
    Vertical = 1
}

[Flags]
public enum DragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    Scroll = unchecked((int)0x80000000),
    All = Copy | Move | Link | Scroll
}

public enum MouseButtons
{
    None = 0,
    Left = 0x100000,
    Right = 0x200000,
    Middle = 0x400000
}

public enum ScrollEventType
{
    SmallDecrement,
    SmallIncrement,
    LargeDecrement,
    LargeIncrement,
    ThumbPosition,
    ThumbTrack,
    First,
    Last,
    EndScroll
}

public enum ScrollOrientation
{
    HorizontalScroll = 0,
    VerticalScroll = 1
}

public enum ColorDepth
{
    Depth8Bit = 8,
    Depth16Bit = 16,
    Depth24Bit = 24,
    Depth32Bit = 32
}

public enum TreeViewAction
{
    Unknown = 0,
    ByKeyboard = 1,
    ByMouse = 2,
    Collapse = 3,
    Expand = 4
}

public enum TreeViewDrawMode
{
    Normal = 0,
    OwnerDrawText = 1,
    OwnerDrawAll = 2
}

[Flags]
public enum TreeNodeStates
{
    Default = 0,
    Checked = 1,
    Selected = 2,
    Grayed = 4,
    Hot = 8,
    Focused = 16
}

public class ControlEventArgs : EventArgs
{
    public ControlEventArgs(Control control)
    {
        Control = control ?? throw new ArgumentNullException(nameof(control));
    }

    public Control Control { get; }
}

public sealed class ColumnClickEventArgs : EventArgs
{
    public ColumnClickEventArgs(int column)
    {
        Column = column;
    }

    public int Column { get; }
}

public sealed class ItemCheckEventArgs : EventArgs
{
    public ItemCheckEventArgs(int index, CheckState newValue, CheckState currentValue)
    {
        Index = index;
        NewValue = newValue;
        CurrentValue = currentValue;
    }

    public int Index { get; }

    public CheckState NewValue { get; set; }

    public CheckState CurrentValue { get; }
}

public class LabelEditEventArgs : EventArgs
{
    public LabelEditEventArgs(int item, string? label)
    {
        Item = item;
        Label = label;
    }

    public bool CancelEdit { get; set; }

    public int Item { get; }

    public string? Label { get; }
}

public class DragEventArgs : EventArgs
{
    public DragEventArgs(IDataObject data, int keyState, int x, int y, DragDropEffects allowedEffect, DragDropEffects effect)
    {
        Data = data;
        KeyState = keyState;
        X = x;
        Y = y;
        AllowedEffect = allowedEffect;
        Effect = effect;
    }

    public DragDropEffects AllowedEffect { get; }

    public IDataObject Data { get; }

    public DragDropEffects Effect { get; set; }

    public int KeyState { get; }

    public int X { get; }

    public int Y { get; }
}

public class MouseEventArgs : EventArgs
{
    public MouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta)
    {
        Button = button;
        Clicks = clicks;
        X = x;
        Y = y;
        Delta = delta;
    }

    public MouseButtons Button { get; }

    public int Clicks { get; }

    public int Delta { get; }

    public Point Location => new(X, Y);

    public int X { get; }

    public int Y { get; }
}

public class KeyPressEventArgs : EventArgs
{
    public KeyPressEventArgs(char keyChar)
    {
        KeyChar = keyChar;
    }

    public bool Handled { get; set; }

    public char KeyChar { get; set; }
}

public class KeyEventArgs : EventArgs
{
    private bool _suppressKeyPress;

    public KeyEventArgs(Keys keyData)
    {
        KeyData = keyData;
    }

    public bool Handled { get; set; }

    public virtual bool Alt => (KeyData & Keys.Alt) == Keys.Alt;

    public bool Control => (KeyData & Keys.Control) == Keys.Control;

    public bool SuppressKeyPress
    {
        get => _suppressKeyPress;
        set
        {
            _suppressKeyPress = value;
            Handled = value;
        }
    }

    public Keys KeyData { get; }

    public Keys KeyCode
    {
        get
        {
            Keys keyCode = KeyData & Keys.KeyCode;
            return Enum.IsDefined(keyCode) ? keyCode : Keys.None;
        }
    }

    public int KeyValue => (int)(KeyData & Keys.KeyCode);

    public Keys Modifiers => KeyData & Keys.Modifiers;

    public virtual bool Shift => (KeyData & Keys.Shift) == Keys.Shift;
}

public class FormClosingEventArgs : CancelEventArgs
{
    public FormClosingEventArgs(CloseReason closeReason, bool cancel)
        : base(cancel)
    {
        CloseReason = closeReason;
    }

    public CloseReason CloseReason { get; }
}

public class FormClosedEventArgs : EventArgs
{
    public FormClosedEventArgs(CloseReason closeReason)
    {
        CloseReason = closeReason;
    }

    public CloseReason CloseReason { get; }
}

public class WebBrowserNavigatedEventArgs : EventArgs
{
    public WebBrowserNavigatedEventArgs(Uri url)
    {
        Url = url;
    }

    public Uri Url { get; }
}

public class WebBrowserDocumentCompletedEventArgs : EventArgs
{
    public WebBrowserDocumentCompletedEventArgs(Uri url)
    {
        Url = url;
    }

    public Uri Url { get; }
}

public class WebBrowserNavigatingEventArgs : CancelEventArgs
{
    public WebBrowserNavigatingEventArgs(Uri url)
    {
        Url = url;
    }

    public Uri Url { get; }

    public string TargetFrameName { get; set; } = string.Empty;
}

public class PropertyValueChangedEventArgs : EventArgs
{
    public PropertyValueChangedEventArgs(GridItem? changedItem, object? oldValue)
    {
        ChangedItem = changedItem;
        OldValue = oldValue;
    }

    public GridItem? ChangedItem { get; }

    public object? OldValue { get; }
}

public class SelectedGridItemChangedEventArgs : EventArgs
{
    public SelectedGridItemChangedEventArgs(GridItem? oldSelection, GridItem? newSelection)
    {
        OldSelection = oldSelection;
        NewSelection = newSelection;
    }

    public GridItem? NewSelection { get; }

    public GridItem? OldSelection { get; }
}

public class DataGridViewCellEventArgs : EventArgs
{
    public DataGridViewCellEventArgs(int columnIndex, int rowIndex)
    {
        ColumnIndex = columnIndex;
        RowIndex = rowIndex;
    }

    public int ColumnIndex { get; }

    public int RowIndex { get; }
}

public class DataGridViewDataErrorEventArgs : DataGridViewCellEventArgs
{
    public DataGridViewDataErrorEventArgs(Exception? exception, int columnIndex, int rowIndex)
        : base(columnIndex, rowIndex)
    {
        Exception = exception;
    }

    public Exception? Exception { get; }

    public bool ThrowException { get; set; }
}

public class DataGridViewEditingControlShowingEventArgs : EventArgs
{
    public DataGridViewEditingControlShowingEventArgs(Control control)
    {
        Control = control;
    }

    public Control Control { get; }
}

public class DataGridViewRowsAddedEventArgs : EventArgs
{
    public DataGridViewRowsAddedEventArgs(int rowIndex, int rowCount)
    {
        RowIndex = rowIndex;
        RowCount = rowCount;
    }

    public int RowIndex { get; }

    public int RowCount { get; }
}

public class DataGridViewRowsRemovedEventArgs : EventArgs
{
    public DataGridViewRowsRemovedEventArgs(int rowIndex, int rowCount)
    {
        RowIndex = rowIndex;
        RowCount = rowCount;
    }

    public int RowIndex { get; }

    public int RowCount { get; }
}

public class LinkLabelLinkClickedEventArgs : EventArgs
{
    public LinkLabelLinkClickedEventArgs(object? link)
    {
        Link = link;
    }

    public object? Link { get; }
}

public class GridItem
{
    public string Label { get; set; } = string.Empty;

    public object? Value { get; set; }

    public PropertyDescriptor? PropertyDescriptor { get; set; }
}

public class PaintEventArgs : EventArgs
{
    public PaintEventArgs(Graphics graphics, Rectangle clipRectangle)
    {
        Graphics = graphics;
        ClipRectangle = clipRectangle;
    }

    public Rectangle ClipRectangle { get; }

    public Graphics Graphics { get; }
}

public class DrawItemEventArgs : EventArgs
{
    public DrawItemEventArgs(Graphics graphics, Font font, Rectangle bounds, int index, DrawItemState state)
    {
        Graphics = graphics;
        Font = font;
        Bounds = bounds;
        Index = index;
        State = state;
    }

    public Rectangle Bounds { get; }

    public Font Font { get; }

    public Graphics Graphics { get; }

    public int Index { get; }

    public DrawItemState State { get; }

    public void DrawBackground()
    {
        Brush background = (State & DrawItemState.Selected) != 0
            ? SystemBrushes.Highlight
            : (State & DrawItemState.Disabled) != 0
                ? SystemBrushes.Control
                : SystemBrushes.Window;

        Graphics.FillRectangle(background, Bounds);
    }

    public void DrawFocusRectangle()
    {
        if ((State & DrawItemState.Focus) == 0 || (State & DrawItemState.NoFocusRect) != 0)
        {
            return;
        }

        Rectangle focusBounds = Bounds;
        if (focusBounds.Width <= 1 || focusBounds.Height <= 1)
        {
            return;
        }

        focusBounds.Inflate(-1, -1);
        Graphics.DrawRectangle(SystemPens.WindowText, focusBounds);
    }
}

public readonly struct Padding
{
    public static Padding Empty { get; } = new Padding(0);

    public Padding(int all)
    {
        Left = all;
        Top = all;
        Right = all;
        Bottom = all;
    }

    public Padding(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Left { get; }

    public int Top { get; }

    public int Right { get; }

    public int Bottom { get; }
}

public sealed class DockPaddingEdges
{
    public int All
    {
        set
        {
            Left = value;
            Top = value;
            Right = value;
            Bottom = value;
        }
    }

    public int Left { get; set; }

    public int Top { get; set; }

    public int Right { get; set; }

    public int Bottom { get; set; }
}

public class MeasureItemEventArgs : EventArgs
{
    public MeasureItemEventArgs(Graphics graphics, int index)
    {
        Graphics = graphics;
        Index = index;
    }

    public Graphics Graphics { get; }

    public int Index { get; }

    public int ItemHeight { get; set; }

    public int ItemWidth { get; set; }
}

public class TreeViewCancelEventArgs : CancelEventArgs
{
    public TreeViewCancelEventArgs(TreeNode? node, bool cancel, TreeViewAction action)
        : base(cancel)
    {
        Node = node;
        Action = action;
    }

    public TreeViewAction Action { get; }

    public TreeNode? Node { get; }
}

public class TreeViewEventArgs : EventArgs
{
    public TreeViewEventArgs(TreeNode? node)
        : this(node, TreeViewAction.Unknown)
    {
    }

    public TreeViewEventArgs(TreeNode? node, TreeViewAction action)
    {
        Node = node;
        Action = action;
    }

    public TreeViewAction Action { get; }

    public TreeNode? Node { get; }
}

public class NodeLabelEditEventArgs : EventArgs
{
    public NodeLabelEditEventArgs(TreeNode? node, string? label)
    {
        Node = node;
        Label = label;
    }

    public bool CancelEdit { get; set; }

    public string? Label { get; }

    public TreeNode? Node { get; }
}

public class DrawTreeNodeEventArgs : EventArgs
{
    public DrawTreeNodeEventArgs(Graphics graphics, TreeNode? node, Rectangle bounds)
    {
        Graphics = graphics;
        Node = node;
        Bounds = bounds;
    }

    public Rectangle Bounds { get; }

    public Graphics Graphics { get; }

    public TreeNode? Node { get; }

    public bool DrawDefault { get; set; }

    public TreeNodeStates State { get; set; }
}

public class ItemDragEventArgs : EventArgs
{
    public ItemDragEventArgs(MouseButtons button, object? item)
    {
        Button = button;
        Item = item;
    }

    public MouseButtons Button { get; }

    public object? Item { get; }
}

public class PreviewKeyDownEventArgs : EventArgs
{
    public PreviewKeyDownEventArgs(Keys keyData)
    {
        KeyData = keyData;
    }

    public Keys KeyData { get; }

    public Keys KeyCode => KeyData & ~(Keys.Control | Keys.Shift);

    public Keys Modifiers => KeyData & (Keys.Control | Keys.Shift);
}

public class ScrollEventArgs : EventArgs
{
    public ScrollEventArgs(ScrollEventType type, int oldValue, int newValue)
    {
        Type = type;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public ScrollEventArgs(
        ScrollEventType type,
        int oldValue,
        int newValue,
        ScrollOrientation scrollOrientation)
        : this(type, oldValue, newValue)
    {
        ScrollOrientation = scrollOrientation;
    }

    public int NewValue { get; }

    public int OldValue { get; }

    public ScrollOrientation ScrollOrientation { get; }

    public ScrollEventType Type { get; }
}
