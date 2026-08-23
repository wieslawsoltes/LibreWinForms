// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;
#endif

namespace System.Windows.Forms;

/// <summary>
///  Provides a low-level encapsulation of a window handle and a window procedure. The class automatically
///  manages window class creation and registration.
/// </summary>
public unsafe partial class NativeWindow : MarshalByRefObject, IWin32Window, IHandle<HWND>
{
#if DEBUG
    private static BooleanSwitch AlwaysUseNormalWndProc { get; }
        = new("AlwaysUseNormalWndProc", "Skips checking for the debugger when choosing the debuggable WndProc handler");
#endif

    private const int InitializedFlags = 0x01;
    private const int UseDebuggableWndProc = 0x04;

    // Do we have any active HWNDs?
    [ThreadStatic]
    private static bool t_anyHandleCreated;
    private static bool s_anyHandleCreatedInApp;

    [ThreadStatic]
    private static byte t_wndProcFlags;

    [ThreadStatic]
    private static byte t_userSetProcFlags;
    private static byte s_userSetProcFlagsForApp;

    // Need to Store Table of Ids and Handles
    private static short s_globalID = 1;
    private static readonly Dictionary<HWND, GCHandle> s_windowHandles = [];
    private static readonly Dictionary<short, HWND> s_windowIds = [];
    private static readonly Lock s_internalSyncObject = new();
#if LIBREWINFORMS_PORTABLE
#pragma warning disable CA1823 // The shared source retains the Win32 class-creation lock for Windows builds.
#endif
    private static readonly Lock s_createWindowSyncObject = new();
#if LIBREWINFORMS_PORTABLE
#pragma warning restore CA1823
#endif

    private readonly Lock _lock = new();

#if LIBREWINFORMS_PORTABLE
#pragma warning disable CS0649, CS0414, IDE0044 // Win32 subclassing state remains compiled for the shared Windows source path.
#endif
    // Our window procedure delegate
    private WNDPROC? _windowProc;

    // The native handle for our delegate
    private void* _windowProcHandle;

    // The native handle for Windows' default window procedure
    private static IntPtr s_defaultWindowProc;

    private void* _priorWindowProcHandle;
    private bool _suppressedGC;
    private bool _ownHandle;
#if LIBREWINFORMS_PORTABLE
#pragma warning restore CS0649, CS0414, IDE0044
#endif
#if LIBREWINFORMS_PORTABLE
    private LibreHandle _portableHandle;
    private ILibreWindow? _portableWindow;
    private LibreWindowCoordinateMode _portableCoordinateMode;
    private double _portablePresentationScale = 1.0;
    private WINDOW_STYLE _portableStyle;
    private WINDOW_EX_STYLE _portableExtendedStyle;

    internal LibreHandle PortableHandle => _portableHandle;
#endif
    private NativeWindow? _nextWindow;
    private readonly WeakReference<NativeWindow> _weakThisPtr;

    static NativeWindow()
    {
        AppDomain.CurrentDomain.ProcessExit += OnShutdown;
    }

    public NativeWindow()
    {
        _weakThisPtr = new(this);
    }

    /// <summary>
    ///  Cache window DpiContext awareness information that helps to create handle with right context at the later time.
    /// </summary>
    internal DPI_AWARENESS_CONTEXT DpiAwarenessContext { get; } =
#if LIBREWINFORMS_PORTABLE
        ScaleHelper.GetThreadHighDpiMode() switch
        {
            HighDpiMode.PerMonitorV2 => DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2,
            HighDpiMode.PerMonitor => DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE,
            HighDpiMode.SystemAware => DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_SYSTEM_AWARE,
            _ => DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_UNAWARE,
        };
#else
        PInvoke.GetThreadDpiAwarenessContextInternal();
#endif

    /// <summary>
    ///  Override's the base object's finalize method.
    /// </summary>
    ~NativeWindow()
    {
        ForceExitMessageLoop();
    }

    /// <summary>
    ///  This was factored into another function so the finalizer in control that releases the window
    ///  can perform the exact same code without further changes. If you make changes to the finalizer,
    ///  change this method -- try not to change NativeWindow's finalizer.
    /// </summary>
    internal unsafe void ForceExitMessageLoop()
    {
#if LIBREWINFORMS_PORTABLE
        if (HWND.IsNull)
        {
            return;
        }

        try
        {
            LibrePlatform.Current.Dispatcher.Post(DestroyHandle);
        }
        catch (InvalidOperationException)
        {
            ReleasePortableHandle(disposeWindow: false);
        }

        return;
#else
        HWND handle;
        bool ownedHandle;

        lock (_lock)
        {
            handle = HWND;
            ownedHandle = _ownHandle;
        }

        if (!handle.IsNull)
        {
            // Now, before we set handle to zero and finish the finalizer, let's send
            // a WM_NULL to the window. Why?  Because if the main ui thread is INSIDE
            // the wndproc for this control during our unsubclass, then we could AV
            // when control finally reaches us.
            if (PInvoke.IsWindow(handle))
            {
                uint id = PInvokeCore.GetWindowThreadProcessId(handle, out _);
                Application.ThreadContext? context = Application.ThreadContext.FromId(id);
                nint threadHandle = context is null ? 0 : context.Handle;

                if (threadHandle != 0)
                {
                    uint exitCode;
                    PInvoke.GetExitCodeThread((HANDLE)threadHandle, &exitCode);
                    if (!AppDomain.CurrentDomain.IsFinalizingForUnload() && (NTSTATUS)exitCode == NTSTATUS.STATUS_PENDING)
                    {
                        PInvoke.SendMessageTimeout(
                            handle,
                            (uint)RegisteredMessage.WM_UIUNSUBCLASS,
                            default,
                            default,
                            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG,
                            100,
                            null);
                    }
                }
            }

            if (!HWND.IsNull)
            {
                // If the dest thread is gone, it should be safe to unsubclass here.
                ReleaseHandle(true);
            }
        }

        if (!handle.IsNull && ownedHandle)
        {
            // If we owned the handle, post a WM_CLOSE to get rid of it.
            PInvokeCore.PostMessage(handle, PInvokeCore.WM_CLOSE);
        }
#endif
    }

    /// <summary>
    ///  Indicates whether a window handle was created &amp; is being tracked.
    /// </summary>
    internal static bool AnyHandleCreated => t_anyHandleCreated;

    /// <summary>
    ///  Gets the handle for this window.
    /// </summary>
    public IntPtr Handle => HWND;

    internal HWND HWND { get; private set; }

    HWND IHandle<HWND>.Handle => HWND;

    /// <summary>
    ///  This returns the prior NativeWindow created with the same native handle, if any.
    /// </summary>
    internal NativeWindow? PreviousWindow { get; private set; }

    /// <summary>
    ///  Address of the Windows default WNDPROC (DefWindowProcW).
    /// </summary>
    internal static unsafe nint DefaultWindowProc
    {
        get
        {
            var hModule = PInvoke.GetModuleHandle(Libraries.User32);
            fixed (byte* ptr = "DefWindowProcW\0"u8)
            {
                if (s_defaultWindowProc == 0)
                {
                    // Cache the default windows procedure address
                    s_defaultWindowProc = PInvoke.GetProcAddress(hModule, (PCSTR)ptr);

                    if (s_defaultWindowProc == 0)
                    {
                        throw new Win32Exception();
                    }
                }
            }

            return s_defaultWindowProc;
        }
    }

    private static int WndProcFlags
    {
        get
        {
            // Upcast for easy bit masking...
            int intWndProcFlags = t_wndProcFlags;

            // Check to see if a debugger is installed. If there is, then use DebuggableCallback instead;
            // this callback has no try/catch around it so exceptions go to the debugger.

            if (intWndProcFlags == 0)
            {
                if (t_userSetProcFlags != 0)
                {
                    intWndProcFlags = t_userSetProcFlags;
                }
                else if (s_userSetProcFlagsForApp != 0)
                {
                    intWndProcFlags = s_userSetProcFlagsForApp;
                }
                else if (!Application.CustomThreadExceptionHandlerAttached)
                {
                    if (Debugger.IsAttached)
                    {
                        intWndProcFlags |= UseDebuggableWndProc;
                    }
                }

#if DEBUG
                if (AlwaysUseNormalWndProc.Enabled)
                {
                    intWndProcFlags &= ~UseDebuggableWndProc;
                }
#endif
                intWndProcFlags |= InitializedFlags;
                t_wndProcFlags = (byte)intWndProcFlags;
            }

            return intWndProcFlags;
        }
    }

    internal static bool WndProcShouldBeDebuggable
        => (WndProcFlags & UseDebuggableWndProc) != 0;

    /// <summary>
    ///  Inserts an entry into this hashtable.
    /// </summary>
    private static void AddWindowToTable(HWND handle, NativeWindow window)
    {
        Debug.Assert(!handle.IsNull, "Should never insert a zero handle into the hash");

        lock (s_internalSyncObject)
        {
            t_anyHandleCreated = true;
            s_anyHandleCreatedInApp = true;

            GCHandle root = GCHandle.Alloc(window, GCHandleType.Weak);

            if (s_windowHandles.TryGetValue(handle, out GCHandle oldRoot))
            {
                // This handle exists with another NativeWindow, replace it and
                // hook up the previous and next window pointers so we can get
                // back to the right window.

                if (oldRoot.IsAllocated)
                {
                    if (oldRoot.Target is NativeWindow target)
                    {
                        window.PreviousWindow = target;
                        Debug.Assert(
                            window.PreviousWindow._nextWindow is null,
                            "Last window in chain should have null next ptr");
                        window.PreviousWindow._nextWindow = window;
                    }

                    oldRoot.Free();
                }
            }

            s_windowHandles[handle] = root;
        }
    }

    /// <summary>
    ///  Creates and applies a unique identifier to the given window <paramref name="handle"/>.
    /// </summary>
    /// <returns>
    ///  The identifier given to the window.
    /// </returns>
    internal static short CreateWindowId(IHandle<HWND> handle)
    {
        short id = s_globalID++;
        s_windowIds[id] = handle.Handle;

        // Set the Window ID
#if !LIBREWINFORMS_PORTABLE
        PInvokeCore.SetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_ID, id);
#endif

        return id;
    }

    /// <summary>
    ///  Assigns a handle to this <see cref="NativeWindow"/> instance.
    /// </summary>
    public void AssignHandle(IntPtr handle) => AssignHandle((HWND)handle, assignUniqueID: true);

    internal unsafe void AssignHandle(HWND hwnd, bool assignUniqueID)
    {
#if LIBREWINFORMS_PORTABLE
        _ = assignUniqueID;
        lock (_lock)
        {
            CheckReleased();
            Debug.Assert(!hwnd.IsNull);
            HWND = hwnd;
            _portableHandle = new LibreHandle((nint)hwnd, LibreHandleKind.LogicalControl);
            AddWindowToTable(hwnd, this);
            OnHandleChange();
        }
#else
        lock (_lock)
        {
            CheckReleased();
            Debug.Assert(!hwnd.IsNull);

            HWND = hwnd;

            _priorWindowProcHandle = (void*)PInvokeCore.GetWindowLong(this, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC);
            Debug.Assert(_priorWindowProcHandle is not null);

            _windowProc = new WNDPROC(Callback);

            AddWindowToTable(hwnd, this);

            // Set the NativeWindow window procedure delegate and get back the native pointer for it.
            PInvokeCore.SetWindowLong(this, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC, _windowProc);
            _windowProcHandle = (void*)PInvokeCore.GetWindowLong(this, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC);

            // This shouldn't be possible.
            Debug.Assert(_priorWindowProcHandle != _windowProcHandle, "Uh oh! Subclassed ourselves!!!");

            if (assignUniqueID
                && ((WINDOW_STYLE)(uint)PInvokeCore.GetWindowLong(this, WINDOW_LONG_PTR_INDEX.GWL_STYLE)).HasFlag(WINDOW_STYLE.WS_CHILD)
                && PInvokeCore.GetWindowLong(this, WINDOW_LONG_PTR_INDEX.GWL_ID) == 0)
            {
                PInvokeCore.SetWindowLong(this, WINDOW_LONG_PTR_INDEX.GWL_ID, hwnd);
            }

            if (_suppressedGC)
            {
                GC.ReRegisterForFinalize(this);
                _suppressedGC = false;
            }

            OnHandleChange();
        }
#endif
    }

    /// <summary>
    ///  Window message callback method. Control arrives here when a window
    ///  message is sent to this Window. This method packages the window message
    ///  in a Message object and invokes the wndProc() method. A WM_NCDESTROY
    ///  message automatically causes the releaseHandle() method to be called.
    /// </summary>
    private LRESULT Callback(HWND hWnd, uint msg, WPARAM wparam, LPARAM lparam)
    {
        // Note: if you change this code be sure to change the
        // corresponding code in DebuggableCallback below!

        Message m = Message.Create(hWnd, msg, wparam, lparam);

        try
        {
            if (_weakThisPtr.TryGetTarget(out _))
            {
                WndProc(ref m);
            }
            else
            {
                DefWndProc(ref m);
            }
        }
        catch (Exception e)
        {
            if (WndProcShouldBeDebuggable)
            {
                throw;
            }

            OnThreadException(e);
        }
        finally
        {
            if (msg == PInvokeCore.WM_NCDESTROY)
            {
                ReleaseHandle(handleValid: false);
            }

            if (msg == RegisteredMessage.WM_UIUNSUBCLASS)
            {
                ReleaseHandle(handleValid: true);
            }
        }

        return m.ResultInternal;
    }

    /// <summary>
    ///  Raises an exception if the window handle is not zero.
    /// </summary>
    private void CheckReleased()
    {
        if (Handle != 0)
        {
            throw new InvalidOperationException(SR.HandleAlreadyExists);
        }
    }

    /// <summary>
    ///  Creates a window handle for this window.
    /// </summary>
    public virtual unsafe void CreateHandle(CreateParams cp)
    {
#if LIBREWINFORMS_PORTABLE
        ArgumentNullException.ThrowIfNull(cp);
        lock (_lock)
        {
            CheckReleased();
            LibrePlatformServices services = LibrePlatform.Current;
            _portableStyle = (WINDOW_STYLE)(uint)cp.Style;
            _portableExtendedStyle = (WINDOW_EX_STYLE)(uint)cp.ExStyle;
            if (this is Control.ControlNativeWindow controlWindow && controlWindow.GetControl() is Form)
            {
                LibreWindowOptions options = LibreWindowOptions.None;
                LibreWindowBorder border = ResolvePortableBorder(_portableStyle);
                if (border != LibreWindowBorder.Hidden) options |= LibreWindowOptions.Decorated;
                if (border == LibreWindowBorder.Resizable) options |= LibreWindowOptions.Resizable;
                if (_portableStyle.HasFlag(WINDOW_STYLE.WS_VISIBLE)) options |= LibreWindowOptions.Visible;
                if (_portableExtendedStyle.HasFlag(WINDOW_EX_STYLE.WS_EX_TOPMOST)) options |= LibreWindowOptions.TopMost;
                if (_portableExtendedStyle.HasFlag(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW)) options |= LibreWindowOptions.ToolWindow;
                LibreWindowState initialState = _portableStyle.HasFlag(WINDOW_STYLE.WS_MAXIMIZE)
                    ? LibreWindowState.Maximized
                    : _portableStyle.HasFlag(WINDOW_STYLE.WS_MINIMIZE)
                        ? LibreWindowState.Minimized
                        : LibreWindowState.Normal;

                LibreHandle owner = default;
                if (cp.Parent != 0 && FromHandle(cp.Parent) is { } parent)
                {
                    owner = parent._portableHandle;
                }

                LibreRectangle requestedBounds = new(
                    cp.X == PInvoke.CW_USEDEFAULT ? 100 : cp.X,
                    cp.Y == PInvoke.CW_USEDEFAULT ? 100 : cp.Y,
                    Math.Max(1, cp.Width),
                    Math.Max(1, cp.Height));
                LibreWindowCoordinateMode coordinateMode = ScaleHelper.IsThreadPerMonitorV2Aware
                    ? LibreWindowCoordinateMode.DevicePixels
                    : LibreWindowCoordinateMode.Logical;
                double initialDpiScale = coordinateMode == LibreWindowCoordinateMode.DevicePixels
                    ? services.Monitors.GetNearest(requestedBounds).DpiScale
                    : 1.0;
                if (coordinateMode == LibreWindowCoordinateMode.DevicePixels)
                {
                    // Canonical top-level autoscaling changes size but deliberately keeps
                    // Location unchanged. Create the native window at those eventual managed
                    // device bounds so initialization does not visibly reposition it.
                    LibreRectangle scaledSize = LibreWindowCoordinates.ToManaged(
                        new LibreRectangle(0, 0, requestedBounds.Width, requestedBounds.Height),
                        coordinateMode,
                        initialDpiScale,
                        initialDpiScale);
                    requestedBounds = new LibreRectangle(
                        requestedBounds.X,
                        requestedBounds.Y,
                        scaledSize.Width,
                        scaledSize.Height);
                }

                LibreWindowCreateOptions createOptions = new(
                    cp.Caption ?? string.Empty,
                    requestedBounds,
                    options,
                    owner,
                    coordinateMode,
                    initialDpiScale,
                    initialState);
                _portableWindow = services.Windows.Create(createOptions, new PortableWindowEvents(this));
                _portableHandle = _portableWindow.Handle;
                _portableCoordinateMode = _portableWindow.CoordinateMode;
                _portablePresentationScale = _portableWindow.DpiScale;
            }
            else
            {
                _portableHandle = services.Handles.Allocate(this, LibreHandleKind.LogicalControl);
            }

            HWND = (HWND)_portableHandle.Value;
            AddWindowToTable(HWND, this);
            _ownHandle = true;
            OnHandleChange();
        }

        return;
#else
        lock (_lock)
        {
            CheckReleased();
            WindowClass windowClass = WindowClass.FindOrCreate(cp.ClassName, (WNDCLASS_STYLES)cp.ClassStyle);
            lock (s_createWindowSyncObject)
            {
                // The CLR will sometimes pump messages while we're waiting on the lock.
                // If a message comes through (say a WM_ACTIVATE for the parent) which
                // causes the handle to be created, we can try to create the handle twice
                // for NativeWindow. Check the handle again to avoid this.
                if (!HWND.IsNull)
                {
                    return;
                }

                HWND createResult = HWND.Null;
                int lastWin32Error = 0;

                NativeWindow? prevTargetWindow = windowClass._targetWindow;
                try
                {
                    windowClass._targetWindow = this;

                    // Parking window dpi awareness context need to match with dpi awareness context of control being
                    // parented to this parking window. Otherwise, reparenting of control will fail.
                    using (ScaleHelper.EnterDpiAwarenessScope(DpiAwarenessContext, DPI_HOSTING_BEHAVIOR.DPI_HOSTING_BEHAVIOR_MIXED))
                    {
                        HINSTANCE modHandle = PInvoke.GetModuleHandle((PCWSTR)null);
                        // Older versions of Windows AV rather than returning E_OUTOFMEMORY.
                        // Catch this and then we re-throw an out of memory error.
                        try
                        {
                            // CreateWindowEx throws if WindowText is greater than the max
                            // length of a 16 bit int (32767).
                            // If it exceeds the max, we should take the substring....
                            if (cp.Caption is not null && cp.Caption.Length > short.MaxValue)
                            {
                                cp.Caption = cp.Caption[..short.MaxValue];
                            }

                            createResult = PInvoke.CreateWindowEx(
                                (WINDOW_EX_STYLE)cp.ExStyle,
                                windowClass._windowClassName,
                                cp.Caption,
                                (WINDOW_STYLE)cp.Style,
                                cp.X,
                                cp.Y,
                                cp.Width,
                                cp.Height,
                                (HWND)cp.Parent,
                                HMENU.Null,
                                modHandle,
                                cp.Param);

                            lastWin32Error = Marshal.GetLastWin32Error();
                        }
                        catch (NullReferenceException e)
                        {
                            throw new OutOfMemoryException(SR.ErrorCreatingHandle, e);
                        }
                    }
                }
                finally
                {
                    windowClass._targetWindow = prevTargetWindow;
                }

                Debug.WriteLineIf(
                    CoreSwitches.PerfTrack.Enabled,
                    $"Handle created of type '{cp.ClassName}' with caption '{cp.Caption}' from NativeWindow of type '{GetType().FullName}'");

                if (createResult.IsNull)
                {
                    throw new Win32Exception(lastWin32Error, SR.ErrorCreatingHandle);
                }

#if DEBUG
                if (OsVersion.IsWindows10_18030rGreater())
                {
                    // In a mixed DPI hosting environment, the DPI settings for child windows can be determined by
                    // either the parent window or the thread hosting it,
                    // based on the window properties and the behavior of the thread. For additional information,
                    // please refer to
                    // https://microsoft.visualstudio.com/OS/_git/os.2020?path=/clientcore/windows/Core/ntuser/kernel/windows/createw.cxx&version=GBofficial/main&line=881&lineEnd=882&lineStartColumn=1&lineEndColumn=1&lineStyle=plain&_a=contents
                    DPI_AWARENESS_CONTEXT controlHandleDpiContext = PInvoke.GetWindowDpiAwarenessContext(HWND);
                    Debug.Assert(DpiAwarenessContext.IsEquivalent(controlHandleDpiContext),
                        $"Control's expected DpiAwarenessContext - {DpiAwarenessContext} is different from the DpiAwarenessContext on the Handle created for the control - {controlHandleDpiContext}");
                }
#endif
                _ownHandle = true;
            }
        }
#endif
    }

    /// <summary>
    ///  Invokes the default window procedure associated with this Window. It is
    ///  an error to call this method when the Handle property is zero.
    /// </summary>
    public void DefWndProc(ref Message m)
    {
#if LIBREWINFORMS_PORTABLE
        m.ResultInternal = default;
        return;
#else
        if (PreviousWindow is null)
        {
            if (_priorWindowProcHandle == null)
            {
                Debug.Fail($"Can't find a default window procedure for message {m} on class {GetType().Name}");

                // At this point, there isn't much we can do. There's a small chance the following
                // line will allow the rest of the program to run, but don't get your hopes up.
                m.ResultInternal = PInvokeCore.DefWindowProc(m.HWND, (uint)m.Msg, m.WParamInternal, m.LParamInternal);
                return;
            }

            m.ResultInternal = PInvokeCore.CallWindowProc(
                _priorWindowProcHandle,
                m.HWND,
                (uint)m.Msg,
                m.WParamInternal,
                m.LParamInternal);
        }
        else
        {
            m.ResultInternal = PreviousWindow.Callback(m.HWND, m.MsgInternal, m.WParamInternal, m.LParamInternal);
        }
#endif
    }

    /// <summary>
    ///  Destroys the handle associated with this window.
    /// </summary>
    public virtual void DestroyHandle()
    {
#if LIBREWINFORMS_PORTABLE
        ReleasePortableHandle(disposeWindow: true);
#else
        lock (_lock)
        {
            if (!HWND.IsNull)
            {
                if (!PInvoke.DestroyWindow(HWND))
                {
                    UnSubclass();

                    // Now post a close and let it do whatever it needs to do on its own.
                    PInvokeCore.PostMessage(this, PInvokeCore.WM_CLOSE);
                }

                HWND = HWND.Null;
                _ownHandle = false;
            }

            // Now that we have disposed, there is no need to finalize us any more.
            GC.SuppressFinalize(this);
            _suppressedGC = true;
        }
#endif
    }

    /// <summary>
    ///  Retrieves the window associated with the specified <paramref name="handle"/>.
    /// </summary>
    public static NativeWindow? FromHandle(IntPtr handle)
        => handle != IntPtr.Zero ? GetWindowFromTable((HWND)handle) : null;

    /// <summary>
    ///  Returns the native window for the given handle, or null if the handle is not in our hash table.
    /// </summary>
    private static NativeWindow? GetWindowFromTable(HWND handle) =>
        s_windowHandles.TryGetValue(handle, out GCHandle value) && value.IsAllocated
            ? (NativeWindow?)value.Target
            : null;

    /// <summary>
    ///  Returns the handle from the given <paramref name="id"/> if found, otherwise returns
    ///  <see cref="IntPtr.Zero"/>.
    /// </summary>
    internal static HWND GetHandleFromWindowId(short id)
    {
        if (!s_windowIds.TryGetValue(id, out HWND handle))
        {
            handle = HWND.Null;
        }

        return handle;
    }

    /// <summary>
    ///  Specifies a notification method that is called when the handle for a
    ///  window is changed.
    /// </summary>
    protected virtual void OnHandleChange()
    {
    }

    /// <summary>
    ///  On class load, we connect an event to Application to let us know when
    ///  the process or domain terminates. When this happens, we attempt to
    ///  clear our window class cache. We cannot destroy windows (because we don't
    ///  have access to their thread), and we cannot unregister window classes
    ///  (because the classes are in use by the windows we can't destroy). Instead,
    ///  we move the class and window procs to DefWndProc
    /// </summary>
#pragma warning disable SYSLIB0004 // Type or member is obsolete
    [PrePrepareMethod]
#pragma warning restore SYSLIB0004
    private static void OnShutdown(object? sender, EventArgs e)
    {
        // If we still have windows allocated, we must sling them to userDefWindowProc
        // or else they will AV if they get a message after the managed code has been
        // removed. In debug builds, we assert and give the "ToString" of the native
        // window. In retail we just detach the window proc and let it go. Note that
        // we cannot call DestroyWindow because this API will fail if called from
        // an incorrect thread.

        if (s_windowHandles.Count > 0)
        {
#if LIBREWINFORMS_PORTABLE
            GCHandle[] windowHandles = [.. s_windowHandles.Values];
            foreach (GCHandle gcHandle in windowHandles)
            {
                if (gcHandle.IsAllocated && gcHandle.Target is NativeWindow window)
                {
                    window.ReleasePortableHandle(disposeWindow: false);
                }
            }

            s_windowHandles.Clear();
#else
            Debug.Assert(DefaultWindowProc != IntPtr.Zero, "We have active windows but no user window proc?");

            lock (s_internalSyncObject)
            {
                foreach ((HWND handle, GCHandle gcHandle) in s_windowHandles)
                {
                    if (!handle.IsNull && handle != (HWND)(-1))
                    {
                        PInvokeCore.SetWindowLong(handle, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC, DefaultWindowProc);
                        PInvokeCore.SetClassLong(handle, GET_CLASS_LONG_INDEX.GCL_WNDPROC, DefaultWindowProc);
                        PInvokeCore.PostMessage(handle, PInvokeCore.WM_CLOSE);

                        // Fish out the Window object, if it is valid, and NULL the handle pointer. This
                        // way the rest of WinForms won't think the handle is still valid here.
                        if (gcHandle.IsAllocated)
                        {
                            NativeWindow? w = (NativeWindow?)gcHandle.Target;
                            w?.HWND = HWND.Null;
                        }
                    }
                }

                s_windowHandles.Clear();
            }
#endif
        }
    }

    /// <summary>
    ///  When overridden in a derived class, manages an unhandled thread exception.
    /// </summary>
    protected virtual void OnThreadException(Exception e)
    {
    }

    /// <summary>
    ///  Releases the handle associated with this window.
    /// </summary>
    public virtual void ReleaseHandle()
    {
        ReleaseHandle(true);
    }

    /// <summary>
    ///  Releases the handle associated with this window.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   If <paramref name="handleValid"/> is true, this will unsubclass the window as
    ///   well. <paramref name="handleValid"/> should be false if we are releasing in
    ///   response to a WM_DESTROY. Unsubclassing during this message can cause problems
    ///   with Windows theme manager and it's not needed anyway.
    ///  </para>
    /// </remarks>
    private void ReleaseHandle(bool handleValid)
    {
#if LIBREWINFORMS_PORTABLE
        _ = handleValid;
        ReleasePortableHandle(disposeWindow: false);
        return;
#else
        if (HWND.IsNull)
        {
            return;
        }

        lock (_lock)
        {
            if (HWND.IsNull)
            {
                return;
            }

            if (handleValid)
            {
                UnSubclass();
            }

            RemoveWindowFromDictionary(HWND, this);

            if (_ownHandle)
            {
                _ownHandle = false;
            }

            HWND = HWND.Null;

            if (_weakThisPtr.TryGetTarget(out _))
            {
                // We're not already finalizing.
                OnHandleChange();

                // Now that we have disposed, there is no need to finalize any more.
                GC.SuppressFinalize(this);
                _suppressedGC = true;
            }
        }
#endif
    }

#if LIBREWINFORMS_PORTABLE
    internal bool PortableEnabled => _portableWindow?.Enabled ?? true;

    internal LibreWindowCoordinateMode PortableCoordinateMode => _portableCoordinateMode;

    internal double PortablePresentationScale => _portablePresentationScale;

    internal LibreRectangle PortableBounds => _portableWindow?.Bounds ?? default;

    internal WINDOW_STYLE PortableStyle
    {
        get => _portableStyle;
        set
        {
            _portableStyle = value;
            if (_portableWindow is { } window)
            {
                window.Border = ResolvePortableBorder(value);
            }
        }
    }

    internal WINDOW_EX_STYLE PortableExtendedStyle
    {
        get => _portableExtendedStyle;
        set => _portableExtendedStyle = value;
    }

    internal void SetPortableEnabled(bool enabled)
    {
        if (_portableWindow is { } window)
        {
            window.Enabled = enabled;
        }
    }

    internal void SetPortableTitle(string title)
    {
        if (_portableWindow is { } window)
        {
            window.Title = title;
        }
    }

    internal void SetPortableState(LibreWindowState state)
    {
        if (_portableWindow is { } window)
        {
            window.State = state;
        }
    }

    internal void SetPortableTopMost(bool topMost)
    {
        if (_portableWindow is { } window)
        {
            window.TopMost = topMost;
        }
    }

    private static LibreWindowBorder ResolvePortableBorder(WINDOW_STYLE style)
        => style.HasFlag(WINDOW_STYLE.WS_THICKFRAME)
            ? LibreWindowBorder.Resizable
            : style.HasFlag(WINDOW_STYLE.WS_BORDER)
                ? LibreWindowBorder.Fixed
                : LibreWindowBorder.Hidden;

    internal void SetPortableOwner(NativeWindow? owner)
    {
        if (_portableWindow is { } window)
        {
            window.Owner = owner?._portableHandle ?? default;
        }
    }

    internal void ActivatePortable()
    {
        _portableWindow?.Activate();
    }

    internal void SetPortableVisibility(bool visible)
    {
        if (_portableWindow is null)
        {
            return;
        }

        if (visible)
        {
            _portableWindow.Show();
        }
        else
        {
            _portableWindow.Hide();
        }
    }

    internal void SetPortableBounds(LibreRectangle bounds)
    {
        if (_portableWindow is { } window)
        {
            window.Bounds = bounds;
        }
    }

    internal void SetPortableIcons(IReadOnlyList<LibreWindowIcon> icons)
    {
        if (_portableWindow is { } window)
        {
            window.SetIcons(icons);
        }
    }

    internal void InvalidatePortable(LibreRectangle? dirtyRectangle)
    {
        if (_portableWindow is null)
        {
            return;
        }

        ILibrePaintService painting = LibrePlatform.Current.Painting;
        if (dirtyRectangle is { } dirty)
        {
            painting.Invalidate(_portableHandle, dirty);
        }
        else
        {
            painting.InvalidateAll(_portableHandle);
        }
    }

    internal System.Drawing.Graphics CreateGraphicsPortable(
        LibrePoint origin,
        LibreRectangle clipRectangle)
    {
        if (HWND.IsNull)
        {
            throw new InvalidOperationException("A Graphics cannot be created before the portable handle exists.");
        }

        return LibrePlatform.Current.Painting.CreateGraphics(
            _portableHandle,
            origin,
            clipRectangle);
    }

    internal void PresentPortable()
    {
        if (_portableWindow is not null)
        {
            LibrePlatform.Current.Painting.Present(_portableHandle);
        }
    }

    internal void DispatchPortableMessage(uint message)
    {
        if (HWND.IsNull)
        {
            return;
        }

        Message managedMessage = Message.Create(HWND, message, default, default);
        WndProc(ref managedMessage);
    }

    private void ReleasePortableHandle(bool disposeWindow)
    {
        ILibreWindow? window;
        lock (_lock)
        {
            if (HWND.IsNull)
            {
                return;
            }

            HWND oldHandle = HWND;
            window = _portableWindow;
            _portableWindow = null;
            RemoveWindowFromDictionary(oldHandle, this);
            if (window is null)
            {
                LibrePlatform.Current.Handles.Release(_portableHandle);
            }

            _portableHandle = default;
            _portableCoordinateMode = LibreWindowCoordinateMode.Logical;
            _portablePresentationScale = 1.0;
            _portableStyle = default;
            _portableExtendedStyle = default;
            HWND = HWND.Null;
            _ownHandle = false;
            OnHandleChange();
            GC.SuppressFinalize(this);
            _suppressedGC = true;
        }

        if (disposeWindow)
        {
            window?.Dispose();
        }
    }

    private sealed class PortableWindowEvents : ILibreWindowEvents
    {
        private readonly NativeWindow _owner;

        internal PortableWindowEvents(NativeWindow owner) => _owner = owner;

        public bool Closing()
        {
            _owner.DispatchPortableMessage(PInvokeCore.WM_CLOSE);
            return _owner.HWND.IsNull;
        }

        public void Closed() => _owner.ReleasePortableHandle(disposeWindow: false);

        public void BoundsChanged(LibreRectangle bounds)
        {
            if (_owner is Control.ControlNativeWindow controlWindow && controlWindow.GetControl() is { } control)
            {
                control.UpdatePortableBounds(bounds);
            }
        }

        public void StateChanged(LibreWindowState state)
        {
            if (_owner is Control.ControlNativeWindow controlWindow && controlWindow.GetControl() is Form form)
            {
                form.UpdatePortableWindowState(state);
            }
        }

        public void PresentationScaleChanged(double scale)
        {
            if (!double.IsFinite(scale) || scale <= 0.0 || scale > 8.0)
            {
                return;
            }

            _owner._portablePresentationScale = scale;
            if (_owner is Control.ControlNativeWindow controlWindow && controlWindow.GetControl() is { } control)
            {
                control.UpdatePortablePresentationScale(scale);
            }
        }

        public void PaintRequested(ILibrePaintFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (_owner is Control.ControlNativeWindow controlWindow && controlWindow.GetControl() is { } control)
            {
                control.PaintPortableFrame(frame);
            }
        }

        public void Input(in LibreInputEvent inputEvent)
        {
            if (_owner is Control.ControlNativeWindow controlWindow && controlWindow.GetControl() is { } control)
            {
                control.DispatchPortableInput(inputEvent);
            }
        }
    }
#endif

    private static void RemoveWindowFromDictionary(HWND hwnd, NativeWindow window)
    {
        Debug.Assert(!hwnd.IsNull);

        lock (s_internalSyncObject)
        {
            if (!s_windowHandles.TryGetValue(hwnd, out GCHandle root))
            {
                return;
            }

            // Connect the prior window directly to the next window (if any)
            window.PreviousWindow?._nextWindow = window._nextWindow;

            if (window._nextWindow is not null)
            {
                // Connect the next window to the prior window
                window._nextWindow._priorWindowProcHandle = window._priorWindowProcHandle;
                window._nextWindow.PreviousWindow = window.PreviousWindow;
            }

            if (window._nextWindow is null)
            {
                // We're the last NativeWindow for this HWND, remove the key or reassign
                // the value to the prior NativeWindow if it exists.

                if (root.IsAllocated)
                {
                    root.Free();
                }

                if (window.PreviousWindow is not null)
                {
                    s_windowHandles[hwnd] = GCHandle.Alloc(window.PreviousWindow, GCHandleType.Weak);
                }
                else
                {
                    s_windowHandles.Remove(hwnd);
                }
            }

            // Set our current window's links to null
            window._nextWindow = null;
            window.PreviousWindow = null;
        }
    }

    /// <summary>
    ///  Removes the given Window from the lookup table.
    /// </summary>
    internal static void RemoveWindowFromIDTable(short id) => s_windowIds.Remove(id);

    /// <summary>
    ///  This method can be used to modify the exception handling behavior of
    ///  NativeWindow. By default, NativeWindow will detect if an application
    ///  is running under a debugger, or is running on a machine with a debugger
    ///  installed. In this case, an unhandled exception in the NativeWindow's
    ///  WndProc method will remain unhandled so the debugger can trap it. If
    ///  there is no debugger installed NativeWindow will trap the exception
    ///  and route it to the Application class's unhandled exception filter.
    ///
    ///  You can control this behavior via a config file, or directly through
    ///  code using this method. Setting the unhandled exception mode does
    ///  not change the behavior of any NativeWindow objects that are currently
    ///  connected to window handles; it only affects new handle connections.
    ///
    ///  When threadScope is false, the application exception mode is set. The
    ///  application exception mode is used for all threads that have the Automatic mode.
    ///  Setting the application exception mode does not affect the setting of the current thread.
    ///
    ///  When threadScope is true, the thread exception mode is set. The thread
    ///  exception mode overrides the application exception mode if it's not Automatic.
    /// </summary>
    internal static void SetUnhandledExceptionModeInternal(UnhandledExceptionMode mode, bool threadScope)
    {
        if (!threadScope && s_anyHandleCreatedInApp)
        {
            throw new InvalidOperationException(SR.ApplicationCannotChangeApplicationExceptionMode);
        }

        if (threadScope && t_anyHandleCreated)
        {
            throw new InvalidOperationException(SR.ApplicationCannotChangeThreadExceptionMode);
        }

        switch (mode)
        {
            case UnhandledExceptionMode.Automatic:
                if (threadScope)
                {
                    t_userSetProcFlags = 0;
                }
                else
                {
                    s_userSetProcFlagsForApp = 0;
                }

                break;
            case UnhandledExceptionMode.ThrowException:
                if (threadScope)
                {
                    t_userSetProcFlags = UseDebuggableWndProc | InitializedFlags;
                }
                else
                {
                    s_userSetProcFlagsForApp = UseDebuggableWndProc | InitializedFlags;
                }

                break;
            case UnhandledExceptionMode.CatchException:
                if (threadScope)
                {
                    t_userSetProcFlags = InitializedFlags;
                }
                else
                {
                    s_userSetProcFlagsForApp = InitializedFlags;
                }

                break;
            default:
                throw new InvalidEnumArgumentException(nameof(mode), (int)mode, typeof(UnhandledExceptionMode));
        }
    }

    /// <summary>
    ///  Unsubclassing is a tricky business. We need to account for some border cases:
    ///
    ///   1) User has done multiple subclasses but has un-subclassed out of order.
    ///   2) User has done multiple subclasses but now our defWindowProc points to
    ///       a NativeWindow that has GC'd.
    ///   3) User releasing this handle but this NativeWindow is not the current
    ///       window proc.
    /// </summary>
    private unsafe void UnSubclass()
    {
        bool finalizing = !_weakThisPtr.TryGetTarget(out _);

        // Don't touch if the current window proc is not ours.
        void* currentWindowProc = (void*)PInvokeCore.GetWindowLong(this, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC);
        if (_windowProcHandle == currentWindowProc)
        {
            // The current window proc is ours

            if (PreviousWindow is null)
            {
                // This is the first NativeWindow registered for this HWND, just put back the prior handle we stashed away.
                PInvokeCore.SetWindowLong(this, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC, (nint)_priorWindowProcHandle);
            }
            else
            {
                if (finalizing)
                {
                    // Here, we are finalizing and defWindowProc is pointing to a managed object. We must assume
                    // that the object defWindowProc is pointing to is also finalizing. Why?  Because we're
                    // holding a ref to it, and it is holding a ref to us. The only way this cycle will
                    // finalize is if no one else is hanging onto it. So, we re-assign the window proc to
                    // userDefWindowProc.
                    PInvokeCore.SetWindowLong(this, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC, DefaultWindowProc);
                }
                else
                {
                    // Here we are not finalizing so we use the windowProc for our previous window. This may
                    // DIFFER from the value we are currently storing in defWindowProc because someone may
                    // have re-subclassed.
                    PInvokeCore.SetWindowLong(this, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC, PreviousWindow._windowProc!);
                }
            }
        }
        else
        {
            // The current window proc isn't one we registered.

            // Cutting the subclass chain anyway, even if we're not the last one in the chain. If the whole chain
            // is all managed NativeWindow classes it doesn't matter, if the chain is not, then someone didn't clean
            // up properly, too bad for them...

            // We will cut off the chain if we cannot unsubclass.
            // If we find previouswindow pointing to us, then we can let RemoveWindowFromTable reassign the
            // defwndproc pointers properly when this guy gets removed (thereby unsubclassing ourselves)

            if (_nextWindow is null || _nextWindow._priorWindowProcHandle != _windowProcHandle)
            {
                // we didn't find it... let's unhook anyway and cut the chain... this prevents crashes
                PInvokeCore.SetWindowLong(this, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC, DefaultWindowProc);
            }
        }
    }

    /// <summary>
    ///  Invokes the default window procedure associated with this window.
    /// </summary>
    protected virtual void WndProc(ref Message m)
    {
        switch (m.MsgInternal)
        {
            case PInvokeCore.WM_DPICHANGED_BEFOREPARENT:
                WmDpiChangedBeforeParent(ref m);
                m.ResultInternal = (LRESULT)0;
                break;

            case PInvokeCore.WM_DPICHANGED_AFTERPARENT:
                WmDpiChangedAfterParent(ref m);
                m.ResultInternal = (LRESULT)0;
                break;
        }

        DefWndProc(ref m);
    }

    protected virtual void WmDpiChangedAfterParent(ref Message m)
    { }

    protected virtual void WmDpiChangedBeforeParent(ref Message m)
    { }
}
