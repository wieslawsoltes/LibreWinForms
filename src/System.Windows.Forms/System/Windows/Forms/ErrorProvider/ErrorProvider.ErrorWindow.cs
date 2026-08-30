// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;
#endif
#if !LIBREWINFORMS_PROGPU_DRAWING
using Windows.Win32.Graphics.GdiPlus;
#endif

namespace System.Windows.Forms;

public partial class ErrorProvider
{
    /// <summary>
    ///  There is one ErrorWindow for each control parent. It is parented to the
    ///  control parent. The window's region is made up of the regions from icons
    ///  of all child icons. The window's size is the enclosing rectangle for all
    ///  the regions. A tooltip window is created as a child of this window. The
    ///  rectangle associated with each error icon being displayed is added as a
    ///  tool to the tooltip window.
    /// </summary>
    internal partial class ErrorWindow : NativeWindow
    {
        private AccessibleObject? _accessibleObject;
        private readonly List<ControlItem> _items = [];
        private readonly Control _parent;
        private readonly ErrorProvider _provider;
        private Rectangle _windowBounds;
        private Timer? _timer;
        private NativeWindow? _tipWindow;
#if LIBREWINFORMS_PORTABLE
        private static long s_nextAdornerId;
        private readonly LibreAdornerId _adornerId = new(Interlocked.Increment(ref s_nextAdornerId));
        private ToolTip? _portableToolTip;
        private Timer? _portableToolTipTimer;
        private Action? _portableToolTipTimerAction;
        private ControlItem? _portableHoverItem;
        private bool _portableToolTipVisible;
#endif

        /// <summary>
        ///  Construct an error window for this provider and control parent.
        /// </summary>
        public ErrorWindow(ErrorProvider provider, Control parent)
        {
            _provider = provider;
            _parent = parent;
#if LIBREWINFORMS_PORTABLE
            _parent.MouseMove += OnPortableParentMouseMove;
            _parent.MouseLeave += OnPortableParentMouseLeave;
            _parent.MouseDown += OnPortableParentMouseDown;
#endif
        }

        /// <summary>
        ///  The Accessibility Object for this ErrorProvider
        /// </summary>
        internal AccessibleObject AccessibilityObject => _accessibleObject ??= CreateAccessibilityInstance();

        /// <summary>
        ///  This is called when a control would like to show an error icon.
        /// </summary>
        public void Add(ControlItem item)
        {
            _items.Add(item);
            if (!EnsureCreated())
            {
                return;
            }

            if (_tipWindow is not null)
            {
                ToolInfoWrapper<ErrorWindow> toolInfo = new(this, item.Id, TOOLTIP_FLAGS.TTF_SUBCLASS, item.Error);
                toolInfo.SendMessage(_tipWindow, PInvoke.TTM_ADDTOOLW);
            }

            Update(timerCaused: false);
        }

        internal List<ControlItem> ControlItems => _items;

        /// <summary>
        ///  Constructs the new instance of the accessibility object for this ErrorProvider. Subclasses
        ///  should not call base.CreateAccessibilityObject.
        /// </summary>
        private ErrorWindowAccessibleObject CreateAccessibilityInstance() => new(this);

        /// <summary>
        ///  Called to get rid of any resources the Object may have.
        /// </summary>
        public void Dispose()
        {
#if LIBREWINFORMS_PORTABLE
            _parent.MouseMove -= OnPortableParentMouseMove;
            _parent.MouseLeave -= OnPortableParentMouseLeave;
            _parent.MouseDown -= OnPortableParentMouseDown;
#endif
            EnsureDestroyed();
        }

        /// <summary>
        ///  Make sure the error window is created, and the tooltip window is created.
        /// </summary>
        private unsafe bool EnsureCreated()
        {
#if LIBREWINFORMS_PORTABLE
            return _parent.IsHandleCreated;
#else
            if (Handle != 0)
            {
                return true;
            }

            if (!_parent.IsHandleCreated)
            {
                return false;
            }

            CreateParams cparams = new()
            {
                Caption = string.Empty,
                Style = (int)(WINDOW_STYLE.WS_VISIBLE | WINDOW_STYLE.WS_CHILD),
                ClassStyle = (int)WNDCLASS_STYLES.CS_DBLCLKS,
                X = 0,
                Y = 0,
                Width = 0,
                Height = 0,
                Parent = _parent.Handle
            };

            CreateHandle(cparams);

            PInvoke.InitCommonControlsEx(new INITCOMMONCONTROLSEX
            {
                dwSize = (uint)sizeof(INITCOMMONCONTROLSEX),
                dwICC = INITCOMMONCONTROLSEX_ICC.ICC_TAB_CLASSES
            });

            cparams = new()
            {
                Parent = Handle,
                ClassName = PInvoke.TOOLTIPS_CLASS,
                Style = (int)PInvoke.TTS_ALWAYSTIP
            };

            _tipWindow = new NativeWindow();
            _tipWindow.CreateHandle(cparams);

            PInvokeCore.SendMessage(
                _tipWindow,
                PInvoke.TTM_SETMAXTIPWIDTH,
                (WPARAM)0,
                (LPARAM)SystemInformation.MaxWindowTrackSize.Width);
            PInvoke.SetWindowPos(
                _tipWindow,
                HWND.HWND_TOP,
                0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
            PInvokeCore.SendMessage(_tipWindow, PInvoke.TTM_SETDELAYTIME, (WPARAM)PInvoke.TTDT_INITIAL);

            return true;
#endif
        }

        /// <summary>
        ///  Destroy the timer, toolwindow, and the error window itself.
        /// </summary>
        private void EnsureDestroyed()
        {
            _timer?.Dispose();
            _timer = null;

#if LIBREWINFORMS_PORTABLE
            HidePortableToolTip(updateIcon: false);
            _portableToolTipTimer?.Dispose();
            _portableToolTipTimer = null;
            _portableToolTip?.Dispose();
            _portableToolTip = null;
            Control root = _parent.TopLevelControl ?? _parent;
            if (root.IsHandleCreated)
            {
                LibrePlatform.Current.Adorners.Remove(root.PortableHandle, _adornerId);
            }

            _parent.Invalidate(true);
            return;
#else

            _tipWindow?.DestroyHandle();
            _tipWindow = null;

            // Hide the window and invalidate the parent to ensure that we leave no visual artifacts.
            // Given that we have an unusual region window, this is needed.
            PInvoke.SetWindowPos(
                this,
                HWND.HWND_TOP,
                _windowBounds.X,
                _windowBounds.Y,
                _windowBounds.Width,
                _windowBounds.Height,
                SET_WINDOW_POS_FLAGS.SWP_HIDEWINDOW | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE);
            _parent?.Invalidate(true);
            DestroyHandle();
#endif
        }

        private unsafe void MirrorDcIfNeeded(HDC hdc)
        {
            if (_parent.IsMirrored)
            {
                // Mirror the DC
                PInvokeCore.SetMapMode(hdc, HDC_MAP_MODE.MM_ANISOTROPIC);
                SIZE originalExtents = default;
                PInvoke.GetViewportExtEx(hdc, &originalExtents);
                PInvoke.SetViewportExtEx(hdc, -originalExtents.Width, originalExtents.Height, lpsz: null);
                Point originalOrigin = default;
                PInvokeCore.GetViewportOrgEx(hdc, &originalOrigin);
                PInvoke.SetViewportOrgEx(hdc, originalOrigin.X + _windowBounds.Width - 1, originalOrigin.Y, lppt: null);
            }
        }

        /// <summary>
        ///  This is called when the error window needs to paint. We paint each icon at its correct location.
        /// </summary>
        private unsafe void OnPaint()
        {
            using BeginPaintScope hdc = new(HWND);
            using SaveDcScope save = new(hdc);

            MirrorDcIfNeeded(hdc);

            for (int i = 0; i < _items.Count; i++)
            {
                ControlItem item = _items[i];
                Rectangle bounds = item.GetIconBounds(_provider.Region.Size);
                PInvokeCore.DrawIconEx(
                    hdc,
                    bounds.X - _windowBounds.X,
                    bounds.Y - _windowBounds.Y,
                    _provider.Region,
                    bounds.Width, bounds.Height);
            }
        }

        protected override void OnThreadException(Exception e)
        {
            Application.OnThreadException(e);
        }

        /// <summary>
        ///  This is called when an error icon is flashing, and the view needs to be updated.
        /// </summary>
        private void OnTimer(object? sender, EventArgs e)
        {
            int blinkPhase = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                blinkPhase += _items[i].BlinkPhase;
            }

            if (blinkPhase == 0 && _provider.BlinkStyle != ErrorBlinkStyle.AlwaysBlink)
            {
                Debug.Assert(_timer is not null);
                _timer.Stop();
            }

            Update(timerCaused: true);
        }

        private void OnToolTipVisibilityChanging(IntPtr id, bool toolTipShown)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Id == id)
                {
                    _items[i].ToolTipShown = toolTipShown;
                }
            }
#if DEBUG
            int shownTooltips = 0;
            for (int j = 0; j < _items.Count; j++)
            {
                if (_items[j].ToolTipShown)
                {
                    shownTooltips++;
                }
            }

            Debug.Assert(shownTooltips <= 1);
#endif
        }

        /// <summary>
        ///  This is called when a control no longer needs to display an error icon.
        /// </summary>
        public void Remove(ControlItem item)
        {
#if LIBREWINFORMS_PORTABLE
            if (ReferenceEquals(_portableHoverItem, item))
            {
                HidePortableToolTip(updateIcon: false);
                _portableHoverItem = null;
            }
#endif
            _items.Remove(item);

            if (_tipWindow is not null)
            {
                ToolInfoWrapper<ErrorWindow> info = new(this, item.Id);
                info.SendMessage(_tipWindow, PInvoke.TTM_DELTOOLW);
            }

            if (_items.Count == 0)
            {
                EnsureDestroyed();
            }
            else
            {
                Update(timerCaused: false);
            }
        }

        /// <summary>
        ///  Start the blinking process. The timer will fire until there are no more
        ///  icons that need to blink.
        /// </summary>
        public void StartBlinking()
        {
            if (_timer is null)
            {
                _timer = new Timer();
                _timer.Tick += OnTimer;
            }

            _timer.Interval = _provider.BlinkRate;
            _timer.Start();
            Update(timerCaused: false);
        }

        public void StopBlinking()
        {
            _timer?.Stop();
            Update(timerCaused: false);
        }

        /// <summary>
        ///  Move and size the error window, compute and set the window region, set the tooltip
        ///  rectangles and descriptions. This basically brings the error window up to date with
        ///  the internal data structures.
        /// </summary>
        public unsafe void Update(bool timerCaused)
        {
            IconRegion iconRegion = _provider.Region;
            Size size = iconRegion.Size;
#if LIBREWINFORMS_PORTABLE
            UpdatePortable(timerCaused, iconRegion, size);
#else
            _windowBounds = Rectangle.Empty;
            for (int i = 0; i < _items.Count; i++)
            {
                ControlItem item = _items[i];
                Rectangle iconBounds = item.GetIconBounds(size);
                _windowBounds = _windowBounds.IsEmpty ? iconBounds : Rectangle.Union(_windowBounds, iconBounds);
            }

            using Region windowRegion = new(new Rectangle(0, 0, 0, 0));

            for (int i = 0; i < _items.Count; i++)
            {
                ControlItem item = _items[i];
                Rectangle iconBounds = item.GetIconBounds(size);
                iconBounds.X -= _windowBounds.X;
                iconBounds.Y -= _windowBounds.Y;

                bool showIcon = true;
                if (!item.ToolTipShown)
                {
                    switch (_provider.BlinkStyle)
                    {
                        case ErrorBlinkStyle.NeverBlink:
                            // always show icon
                            break;
                        case ErrorBlinkStyle.BlinkIfDifferentError:
                            showIcon = (item.BlinkPhase == 0) || (item.BlinkPhase > 0 && (item.BlinkPhase & 1) == (i & 1));
                            break;
                        case ErrorBlinkStyle.AlwaysBlink:
                            showIcon = ((i & 1) == 0) == _provider.ShowIcon;
                            break;
                    }
                }

                if (showIcon)
                {
                    iconRegion.Region.Translate(iconBounds.X, iconBounds.Y);
                    windowRegion.Union(iconRegion.Region);
                    iconRegion.Region.Translate(-iconBounds.X, -iconBounds.Y);
                }

                if (_tipWindow is not null)
                {
                    TOOLTIP_FLAGS flags = TOOLTIP_FLAGS.TTF_SUBCLASS;
                    if (_provider.RightToLeft)
                    {
                        flags |= TOOLTIP_FLAGS.TTF_RTLREADING;
                    }

                    ToolInfoWrapper<ErrorWindow> toolInfo = new(this, item.Id, flags, item.Error, iconBounds);
                    toolInfo.SendMessage(_tipWindow, PInvoke.TTM_SETTOOLINFOW);
                }

                if (timerCaused && item.BlinkPhase > 0)
                {
                    item.BlinkPhase--;
                }
            }

            if (timerCaused)
            {
                _provider.ShowIcon = !_provider.ShowIcon;
            }

            using GetDcScope hdc = new(HWND);
            using SaveDcScope save = new(hdc);
            MirrorDcIfNeeded(hdc);

#if !LIBREWINFORMS_PROGPU_DRAWING
            using Graphics g = hdc.CreateGraphics();
            using RegionScope windowRegionHandle = windowRegion.GetRegionScope(g);
            if (PInvoke.SetWindowRgn(this, windowRegionHandle, fRedraw: true) != 0)
            {
                // The HWnd owns the region.
                windowRegionHandle.RelinquishOwnership();
            }
#endif

            PInvoke.SetWindowPos(
                this,
                HWND.HWND_TOP,
                _windowBounds.X,
                _windowBounds.Y,
                _windowBounds.Width,
                _windowBounds.Height,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

            PInvoke.InvalidateRect(this, lpRect: null, bErase: false);
#endif
        }

#if LIBREWINFORMS_PORTABLE
        private void UpdatePortable(bool timerCaused, IconRegion iconRegion, Size size)
        {
            _windowBounds = Rectangle.Empty;
            List<Rectangle> visible = [];
            for (int index = 0; index < _items.Count; index++)
            {
                ControlItem item = _items[index];
                Rectangle iconBounds = item.GetIconBounds(size);
                _windowBounds = _windowBounds.IsEmpty
                    ? iconBounds
                    : Rectangle.Union(_windowBounds, iconBounds);
                if (ShouldShowIcon(item, index))
                {
                    visible.Add(iconBounds);
                }

                if (timerCaused && item.BlinkPhase > 0)
                {
                    item.BlinkPhase--;
                }
            }

            if (timerCaused)
            {
                _provider.ShowIcon = !_provider.ShowIcon;
            }

            Control root = _parent.TopLevelControl ?? _parent;
            if (!root.IsHandleCreated)
            {
                return;
            }

            if (visible.Count == 0 || _windowBounds.Width <= 0 || _windowBounds.Height <= 0)
            {
                LibrePlatform.Current.Adorners.Remove(root.PortableHandle, _adornerId);
                return;
            }

            Point parentOrigin = root.PointToClient(_parent.PointToScreen(Point.Empty));
            Rectangle ownerBounds = _windowBounds;
            ownerBounds.Offset(parentOrigin);
            using Graphics graphics = LibrePlatform.Current.Adorners.CreateGraphics(
                root.PortableHandle,
                _adornerId,
                new LibreRectangle(ownerBounds.X, ownerBounds.Y, ownerBounds.Width, ownerBounds.Height),
                new LibreRectangle(ownerBounds.X, ownerBounds.Y, ownerBounds.Width, ownerBounds.Height));
            foreach (Rectangle iconBounds in visible)
            {
                graphics.DrawIcon(
                    iconRegion.Icon,
                    new Rectangle(
                        iconBounds.X - _windowBounds.X,
                        iconBounds.Y - _windowBounds.Y,
                        iconBounds.Width,
                        iconBounds.Height));
            }
        }

        private bool ShouldShowIcon(ControlItem item, int index)
        {
            if (item.ToolTipShown)
            {
                return true;
            }

            return _provider.BlinkStyle switch
            {
                ErrorBlinkStyle.NeverBlink => true,
                ErrorBlinkStyle.BlinkIfDifferentError => item.BlinkPhase == 0
                    || (item.BlinkPhase > 0 && (item.BlinkPhase & 1) == (index & 1)),
                ErrorBlinkStyle.AlwaysBlink => ((index & 1) == 0) == _provider.ShowIcon,
                _ => throw new InvalidOperationException($"Unknown error blink style: {_provider.BlinkStyle}."),
            };
        }

        private void OnPortableParentMouseMove(object? sender, MouseEventArgs e)
        {
            _ = sender;
            ControlItem? hoveredItem = null;
            for (int index = _items.Count - 1; index >= 0; index--)
            {
                ControlItem item = _items[index];
                if (ShouldShowIcon(item, index) && item.GetIconBounds(_provider.Region.Size).Contains(e.Location))
                {
                    hoveredItem = item;
                    break;
                }
            }

            if (ReferenceEquals(_portableHoverItem, hoveredItem))
            {
                return;
            }

            HidePortableToolTip(updateIcon: true);
            _portableHoverItem = hoveredItem;
            if (hoveredItem is null)
            {
                return;
            }

            ToolTip toolTip = EnsurePortableToolTip();
            StartPortableToolTipTimer(toolTip.InitialDelay, ShowPortableToolTip);
        }

        private void OnPortableParentMouseLeave(object? sender, EventArgs e)
        {
            _ = sender;
            _ = e;
            HidePortableToolTip(updateIcon: true);
            _portableHoverItem = null;
        }

        private void OnPortableParentMouseDown(object? sender, MouseEventArgs e)
        {
            _ = sender;
            _ = e;
            HidePortableToolTip(updateIcon: true);
            _portableHoverItem = null;
        }

        private ToolTip EnsurePortableToolTip()
            => _portableToolTip ??= new ToolTip
            {
                ShowAlways = true,
            };

        private void StartPortableToolTipTimer(int delay, Action action)
        {
            _portableToolTipTimer ??= CreateTimer();
            _portableToolTipTimer.Stop();
            _portableToolTipTimerAction = action;
            if (delay == 0)
            {
                OnPortableToolTipTimer(_portableToolTipTimer, EventArgs.Empty);
                return;
            }

            _portableToolTipTimer.Interval = delay;
            _portableToolTipTimer.Start();

            Timer CreateTimer()
            {
                Timer timer = new();
                timer.Tick += OnPortableToolTipTimer;
                return timer;
            }
        }

        private void OnPortableToolTipTimer(object? sender, EventArgs e)
        {
            _ = sender;
            _ = e;
            _portableToolTipTimer?.Stop();
            Action? action = _portableToolTipTimerAction;
            _portableToolTipTimerAction = null;
            action?.Invoke();
        }

        private void ShowPortableToolTip()
        {
            ControlItem? item = _portableHoverItem;
            int index = item is null ? -1 : _items.IndexOf(item);
            if (item is null || index < 0 || !ShouldShowIcon(item, index) || string.IsNullOrEmpty(item.Error))
            {
                return;
            }

            ToolTip toolTip = EnsurePortableToolTip();
            item.ToolTipShown = true;
            Update(timerCaused: false);
            Rectangle iconBounds = item.GetIconBounds(_provider.Region.Size);
            toolTip.Show(item.Error, _parent, iconBounds.Right + 2, iconBounds.Bottom + 2);
            _portableToolTipVisible = true;
            if (toolTip.AutoPopDelay > 0)
            {
                StartPortableToolTipTimer(toolTip.AutoPopDelay, () => HidePortableToolTip(updateIcon: true));
            }
        }

        private void HidePortableToolTip(bool updateIcon)
        {
            _portableToolTipTimer?.Stop();
            _portableToolTipTimerAction = null;
            if (_portableToolTipVisible && !_parent.IsDisposed)
            {
                _portableToolTip?.Hide(_parent);
            }

            _portableToolTipVisible = false;
            if (_portableHoverItem is { ToolTipShown: true } item)
            {
                item.ToolTipShown = false;
                if (updateIcon)
                {
                    Update(timerCaused: false);
                }
            }
        }
#endif

        /// <summary>
        ///  Handles the WM_GETOBJECT message. Used for accessibility.
        /// </summary>
        private void WmGetObject(ref Message m)
        {
            if (m.Msg == (int)PInvokeCore.WM_GETOBJECT && m.LParamInternal == PInvoke.UiaRootObjectId)
            {
                // If the requested object identifier is UiaRootObjectId,
                // we should return an UI Automation provider using the UiaReturnRawElementProvider function.
                m.ResultInternal = PInvoke.UiaReturnRawElementProvider(
                    this,
                    m.WParamInternal,
                    m.LParamInternal,
                    AccessibilityObject);

                return;
            }

            // Some accessible object requested that we don't care about, so do default message processing.
            DefWndProc(ref m);
        }

        /// <summary>
        ///  Called when the error window gets a windows message.
        /// </summary>
        protected override unsafe void WndProc(ref Message m)
        {
            switch (m.MsgInternal)
            {
                case PInvokeCore.WM_GETOBJECT:
                    WmGetObject(ref m);
                    break;
                case PInvokeCore.WM_NOTIFY:
                    NMHDR* nmhdr = (NMHDR*)(nint)m.LParamInternal;
                    if (nmhdr->code is PInvoke.TTN_SHOW or PInvoke.TTN_POP)
                    {
                        OnToolTipVisibilityChanging((nint)nmhdr->idFrom, nmhdr->code == PInvoke.TTN_SHOW);
                    }

                    break;
                case PInvokeCore.WM_ERASEBKGND:
                    break;
                case PInvokeCore.WM_PAINT:
                    OnPaint();
                    break;
                default:
                    base.WndProc(ref m);
                    break;
            }
        }

        protected override void WmDpiChangedBeforeParent(ref Message m)
        {
            base.WmDpiChangedBeforeParent(ref m);

            int currentDpi = (int)PInvoke.GetDpiForWindow(this);
            if (currentDpi == _parent.DeviceDpiInternal)
            {
                return;
            }

            double factor = ((double)currentDpi) / _parent.DeviceDpiInternal;
            Icon icon = _provider.Icon;
            _provider.CurrentDpi = currentDpi;
            _provider.Icon = new Icon(icon, (int)(icon.Width * factor), (int)(icon.Height * factor));
        }
    }
}
