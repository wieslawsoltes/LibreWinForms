// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using System.Runtime.ExceptionServices;
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public unsafe partial class Control
{
    [ThreadStatic]
    private static Keys s_portableModifierKeys;

    [ThreadStatic]
    private static HashSet<Keys>? s_portableKeysDown;

    [ThreadStatic]
    private static MouseButtons s_portableMouseButtons;

    [ThreadStatic]
    private static Control?[]? s_portableButtonOwners;

    [ThreadStatic]
    private static Point s_portableMousePosition;

    [ThreadStatic]
    private static Control? s_portablePointerRoot;

    [ThreadStatic]
    private static Control? s_portableHoverRoot;

    private Control? _portableFocusedControl;
    private Control? _portableHoveredControl;
    private LibreHandle _portableHoveredControlHandle;
    private LibreHandle _portableHoverWindowHandle;
    private Control? _portableCapturedControl;
    private Control? _portablePressedControl;
    private MouseButtons _portablePressedButton;
    private PortableNativeClick? _portableNativePress;
    private PortableNativeClick? _portableCompletedNativeClick;
    private uint _portablePointerPressVersion;
    private uint _portablePointerInputVersion;
    private uint _portablePointerCaptureVersion;
    private bool _portableWindowFocused;
    private bool _portableControlFocusNotified;
    private uint _portableWindowFocusVersion;
    private uint _portableFocusInputVersion;
    private bool _portableSuppressKeyPress;
    private uint _portableTextInputVersion;
    private uint _portableCanceledTextInputVersion;
    private LibreCursorShape? _portableAppliedCursorShape;

    internal void DispatchPortableInput(in LibreInputEvent inputEvent)
    {
        Control root = GetPortableTopLevelControl();
        if (inputEvent.Kind == LibreInputEventKind.PointerLeave)
        {
            // Retire before callbacks and never mutate their replacement hover.
            // Leaving the native view does not release a held drag/capture.
            root._portablePointerInputVersion++;
            if (ReferenceEquals(s_portableHoverRoot, root))
                s_portableHoverRoot = null;
            root.RetirePortableHover();
            return;
        }

        if (inputEvent.Kind == LibreInputEventKind.PointerCancel)
        {
            root.CancelPortablePointerInput(out Exception? failure);
            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
            return;
        }

        if (inputEvent.Kind == LibreInputEventKind.FocusGained)
            root._portableFocusInputVersion++;
        if (inputEvent.Kind == LibreInputEventKind.FocusLost)
        {
            uint focusVersion = root._portableWindowFocusVersion;
            uint focusInputVersion = ++root._portableFocusInputVersion;
            // Disabled/nonactivating popup windows lose pointer input without
            // ever owning keyboard focus. Retire their own pointer state, but
            // never clear another window's keys, buttons or replacement input.
            bool current = root.CancelPortablePointerInput(out Exception? failure);
            if (current && root._portableWindowFocusVersion == focusVersion
                && root._portableFocusInputVersion == focusInputVersion && root._portableWindowFocused)
            {
                try
                {
                    s_portablePointerRoot = root;
                    s_portableModifierKeys = ToKeys(inputEvent.Modifiers);
                    root.TrackPortableMenuKeyInput(inputEvent);
                    root.SetPortableWindowFocus(focused: false);
                }
                catch (Exception exception)
                {
                    if (failure is null)
                        failure = exception;
                    else
                        failure.Data["PortablePointerFocusCleanup"] = exception;
                }
            }

            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
            return;
        }

        s_portablePointerRoot = root;
        s_portableModifierKeys = ToKeys(inputEvent.Modifiers);
        root.TrackPortableMenuKeyInput(inputEvent);

        switch (inputEvent.Kind)
        {
            case LibreInputEventKind.FocusGained:
                root.SetPortableWindowFocus(focused: true);
                break;
            case LibreInputEventKind.KeyDown:
                root._portableSuppressKeyPress = false;
                bool firstKeyDown = s_portableKeysDown?.Contains(ToKeys(inputEvent.Key)) != true;
                SetPortableKeyState(inputEvent.Key, isDown: true);
                root.DispatchPortableKey(inputEvent.Key, PInvokeCore.WM_KEYDOWN, firstKeyDown);
                break;
            case LibreInputEventKind.KeyUp:
                SetPortableKeyState(inputEvent.Key, isDown: false);
                try
                {
                    root.DispatchPortableKey(inputEvent.Key, PInvokeCore.WM_KEYUP);
                }
                finally
                {
                    root._portableSuppressKeyPress = false;
                }

                break;
            case LibreInputEventKind.TextInput:
                root.DispatchPortableText(inputEvent.Text);
                break;
            case LibreInputEventKind.SystemTextInput:
                root.DispatchPortableText(inputEvent.Text, systemCharacter: true);
                break;
            case LibreInputEventKind.PointerMove:
            case LibreInputEventKind.PointerDown:
            case LibreInputEventKind.PointerUp:
            case LibreInputEventKind.PointerWheel:
                root.DispatchPortablePointer(inputEvent);
                break;
        }
    }

    private static void SetPortableKeyState(LibreKey key, bool isDown)
    {
        Keys keyCode = ToKeys(key);
        if (keyCode == Keys.None)
        {
            return;
        }

        if (isDown)
        {
            (s_portableKeysDown ??= []).Add(keyCode);
        }
        else
        {
            s_portableKeysDown?.Remove(keyCode);
        }
    }

    internal void CancelPortableCapture(bool updateCursor = true)
    {
        Control root = GetPortableTopLevelControl();
        Control? captured = root.RetirePortableCapture();
        captured?.OnMouseCaptureChanged(EventArgs.Empty);
        if (updateCursor)
            root.RefreshPortableCursor();
    }

    private Control? RetirePortableCapture()
    {
        Control root = this;
        Control? captured = root._portableCapturedControl;
        root._portablePointerInputVersion++;
        root._portablePointerPressVersion++;
        root._portablePointerCaptureVersion++;
        root._portableCapturedControl = null;
        root._portablePressedControl = null;
        root._portablePressedButton = MouseButtons.None;
        root._portableNativePress = null;
        root._portableCompletedNativeClick = null;
        if (s_portableButtonOwners is { } owners)
        {
            for (int index = 0; index < owners.Length; index++)
            {
                if (ReferenceEquals(owners[index], root))
                    root.SetPortableButtonState(PortableButtonAt(index), isDown: false);
            }
        }

        return captured;
    }

    private bool CancelPortablePointerInput(out Exception? failure)
    {
        LibreHandle windowHandle = _window.PortableHandle;
        Control? previous = TakePortableHover(out LibreHandle previousHandle, out LibreHandle previousWindowHandle);
        if (ReferenceEquals(s_portableHoverRoot, this))
            s_portableHoverRoot = null;
        Control? captured = RetirePortableCapture();
        uint inputVersion = _portablePointerInputVersion;
        failure = null;
        try
        {
            captured?.OnMouseCaptureChanged(EventArgs.Empty);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            if (_portablePointerInputVersion == inputVersion && _window.PortableHandle == windowHandle
                && s_portableHoverRoot is null)
                NotifyPortableHoverLeave(previous, previousHandle, previousWindowHandle);
        }
        catch (Exception exception)
        {
            if (failure is null)
                failure = exception;
            else
                failure.Data["PortablePointerLeaveCleanup"] = exception;
        }

        return _portablePointerInputVersion == inputVersion && _window.PortableHandle == windowHandle
            && !IsDisposed && !Disposing && IsHandleCreated;
    }

    private static MouseButtons PortableButtonAt(int index) => index switch
    {
        0 => MouseButtons.Left,
        1 => MouseButtons.Right,
        2 => MouseButtons.Middle,
        3 => MouseButtons.XButton1,
        4 => MouseButtons.XButton2,
        _ => MouseButtons.None,
    };

    private void SetPortableButtonState(MouseButtons button, bool isDown)
    {
        int index = button switch
        {
            MouseButtons.Left => 0,
            MouseButtons.Right => 1,
            MouseButtons.Middle => 2,
            MouseButtons.XButton1 => 3,
            MouseButtons.XButton2 => 4,
            _ => -1,
        };
        if (index < 0)
            return;
        if (isDown)
        {
            (s_portableButtonOwners ??= new Control?[5])[index] = this;
            s_portableMouseButtons |= button;
        }
        else
        {
            if (s_portableButtonOwners is { } owners)
                owners[index] = null;
            s_portableMouseButtons &= ~button;
        }
    }

    internal void SetPortableWindowFocus(bool focused)
    {
        if (focused && (!Visible || !IsHandleCreated || IsDisposed || Disposing))
        {
            return;
        }

        if (_portableWindowFocused == focused)
        {
            return;
        }

        if (!focused)
        {
            _portablePendingMenuKey = Keys.None;
            _portableSuppressKeyPress = false;
            if (this is not Form owner || owner.IsPortableActivationOwner)
            {
                s_portableModifierKeys = Keys.None;
                s_portableKeysDown?.Clear();
            }
        }

        _portableWindowFocused = focused;
        uint version = ++_portableWindowFocusVersion;
        Control? losingFocus = !focused && _portableControlFocusNotified ? _portableFocusedControl : null;
        if (!focused)
        {
            _portableControlFocusNotified = false;
            NotifyPortableHostedFocusLifetime();
        }

        if (this is Form form)
        {
            form.UpdatePortableActivation(focused);
        }

        // Canonical activation/focus callbacks can hide, dispose or transfer
        // ownership. Never complete the superseded focus transition afterward.
        if (version != _portableWindowFocusVersion)
        {
            return;
        }

        if (focused)
        {
            NotifyPortableControlFocus();
        }
        else if (losingFocus is not null)
        {
            losingFocus.InvokeLostFocus(losingFocus, EventArgs.Empty);
        }
    }

    private void NotifyPortableControlFocus()
    {
        if (!_portableWindowFocused || _portableControlFocusNotified || IsDisposed || Disposing || !Visible)
        {
            return;
        }

        Control target = _portableFocusedControl ?? this;
        _portableFocusedControl = target;
        _portableControlFocusNotified = true;
        target.InvokeGotFocus(target, EventArgs.Empty);
    }

    private void SetPortableFocus(Control target)
    {
        if (_portableFocusedControl == target)
        {
            NotifyPortableControlFocus();
            return;
        }

        Control? previous = _portableFocusedControl;
        bool previousNotified = _portableControlFocusNotified;
        _portableFocusedControl = target;
        _portableControlFocusNotified = false;
        if (_portableWindowFocused)
        {
            if (previousNotified)
            {
                previous?.InvokeLostFocus(previous, EventArgs.Empty);
            }

            if (_portableFocusedControl == target)
            {
                NotifyPortableControlFocus();
            }
        }
    }

    private void DispatchPortableKey(LibreKey key, uint messageId, bool firstKeyDown = false)
    {
        Keys keyCode = ToKeys(key);
        if (keyCode == Keys.None)
        {
            return;
        }

        Control target = _portableFocusedControl ?? this;
        Keys pendingMenuKey = _portablePendingMenuKey;
        LibreHandle pendingMenuWindow = _portablePendingMenuWindow;
        if (messageId == PInvokeCore.WM_KEYUP)
            _portablePendingMenuKey = Keys.None;
        // A previous key callback can retire the focused editor. Reading Handle
        // must not recreate that recipient for the later release/repeat event.
        if (IsDisposed || Disposing || !IsHandleCreated
            || target.IsDisposed || target.Disposing || !target.IsHandleCreated)
            return;
        Message message = Message.Create(target.Handle, (int)messageId, (nint)(int)keyCode, 0);
        uint inputVersion = _portableMenuInputVersion;
        uint focusVersion = _portableWindowFocusVersion;
        LibreHandle windowHandle = _window.PortableHandle;
        ToolStripDropDown.PortableMenuKeyRelease? menuRelease =
            messageId == PInvokeCore.WM_KEYUP && pendingMenuKey == keyCode && pendingMenuWindow == windowHandle
                && IsPortableBareMenuKey(keyCode) && this is Form receivingOwner
                ? new(receivingOwner) : null;
        bool handled = DispatchPortableKeyboardMessage(target, ref message);
        if (handled && inputVersion == _portableMenuInputVersion)
            _portablePendingMenuKey = Keys.None;
        if (!handled && inputVersion == _portableMenuInputVersion && focusVersion == _portableWindowFocusVersion
            && !IsDisposed && !Disposing && IsHandleCreated && _window.PortableHandle == windowHandle
            && _portableWindowFocused && this is Form { IsPortableActivationOwner: true })
        {
            if (messageId == PInvokeCore.WM_KEYDOWN && firstKeyDown && IsPortableBareMenuKey(keyCode))
            {
                _portablePendingMenuKey = keyCode;
                _portablePendingMenuWindow = windowHandle;
            }
            else
                menuRelease?.Process();
        }
    }

    private bool DispatchPortableKeyboardMessage(Control target, ref Message message)
    {
        uint inputVersion = _portableMenuInputVersion;
        uint textVersion = _portableTextInputVersion;
        if (Application.FilterMessage(ref message))
        {
            return true;
        }

        // The native menu filter redirects keyboard messages without moving
        // focus. Resolve after caller filters, which can close or replace a menu.
        ToolStrip? menu = ToolStripDropDown.GetPortableKeyboardTarget(this);
        nint menuHandle = menu?.Handle ?? 0;
        if (menu is not null)
        {
            target = menu is ToolStripDropDown dropDown ? dropDown.GetPortableKeyboardInputTarget() : menu;
            message.HWnd = target.Handle;
            if (message.MsgInternal == PInvokeCore.WM_KEYDOWN || message.MsgInternal == PInvokeCore.WM_KEYUP)
                target._portableSuppressKeyPress = false;
        }

        if (target.IsDisposed || target.Disposing)
            return true;
        nint handle = target.Handle;
        bool processed = PreProcessControlMessageInternal(target, ref message)
            == PreProcessControlState.MessageProcessed;
        bool handled = true;
        if (!processed && !target.IsDisposed && !target.Disposing
            && (menu is null || (ReferenceEquals(ToolStripDropDown.GetPortableKeyboardTarget(this), menu)
                && menu.IsHandleCreated && menu.Handle == menuHandle
                && target.IsHandleCreated && target.Handle == handle
                && (menu is not ToolStripDropDown currentDropDown || ReferenceEquals(currentDropDown.GetPortableKeyboardInputTarget(), target)))))
        {
            handled = target.ProcessPortableKeyMessage(ref message);
        }

        if (menu is not null && (target._portableSuppressKeyPress
            || (processed && message.MsgInternal == PInvokeCore.WM_KEYDOWN)
            || !ReferenceEquals(ToolStripDropDown.GetPortableKeyboardTarget(this), menu)
            || !menu.IsHandleCreated || menu.Handle != menuHandle
            || (menu is ToolStripDropDown hostedMenu && !ReferenceEquals(hostedMenu.GetPortableKeyboardInputTarget(), target))
            || !target.IsHandleCreated || target.Handle != handle))
        {
            // Typed backends may deliver a translated character after a
            // consumed key or a mnemonic that closed the menu. It must not
            // leak into the still-focused owner editor in the same key cycle.
            // Retiring a recipient cancels the current text packet as well.
            // A standalone character callback has no future KeyUp, however,
            // so it must not leave key-cycle suppression on the owner forever.
            _portableCanceledTextInputVersion = textVersion;
            if (inputVersion == _portableMenuInputVersion
                && (message.MsgInternal == PInvokeCore.WM_KEYDOWN || message.MsgInternal == PInvokeCore.WM_KEYUP
                    || s_portableKeysDown is { Count: > 0 }))
                _portableSuppressKeyPress = true;
        }

        return processed || handled;
    }

    private void DispatchPortableText(string? text, bool systemCharacter = false)
    {
        if (string.IsNullOrEmpty(text) || _portableSuppressKeyPress)
        {
            return;
        }

        Control target = _portableFocusedControl ?? this;
        uint textVersion = ++_portableTextInputVersion;
        foreach (char character in text)
        {
            if (target.IsDisposed || target.Disposing || _portableSuppressKeyPress
                || textVersion != _portableTextInputVersion || textVersion == _portableCanceledTextInputVersion)
            {
                break;
            }

            target.ProcessPortableCharacter(character, systemCharacter);
        }
    }

    internal void ProcessPortableCharacter(char character, bool systemCharacter = false)
    {
        Message message = Message.Create(Handle,
            (int)(systemCharacter ? PInvokeCore.WM_SYSCHAR : PInvokeCore.WM_CHAR), character, 0);
        GetPortableFocusRoot().DispatchPortableKeyboardMessage(this, ref message);
    }

    // The managed event/preprocessing path precedes the platform edit-control
    // default operation, just as WmKeyChar precedes DefWndProc on Windows.
    internal bool ProcessPortableKeyMessage(ref Message message)
    {
        bool handled = ProcessKeyMessage(ref message);
        if (!IsDisposed)
        {
            ProcessPortableTranslatedKey(ref message);
            if (!handled && !IsDisposed)
            {
                ProcessPortableDefaultKeyMessage(ref message);
            }
        }

        return handled || IsDisposed || Disposing;
    }

    internal virtual void ProcessPortableTranslatedKey(ref Message message) { }

    internal virtual void ProcessPortableDefaultKeyMessage(ref Message message) { }

    internal bool IsPortableKeyPressSuppressed
        => GetPortableFocusRoot()._portableSuppressKeyPress;

    internal void SuppressPortableKeyPress()
        => GetPortableFocusRoot()._portableSuppressKeyPress = true;

    private void DispatchPortablePointer(in LibreInputEvent inputEvent)
    {
        uint inputVersion = ++_portablePointerInputVersion;
        LibreHandle receivingHandle = _window.PortableHandle;
        if (!IsCurrentPortablePointerInput(inputVersion, receivingHandle))
            return;

        PortableNativeClick? previousClick = null;
        if (inputEvent.Kind == LibreInputEventKind.PointerDown)
        {
            // A new press claims the history before hover/focus callbacks can
            // pump a replacement press. Native counts belong to the whole view,
            // so only this exact logical target can continue its completed pair.
            previousClick = _portableCompletedNativeClick;
            _portableCompletedNativeClick = null;
            _portableNativePress = null;
        }

        // Physical release remains authoritative even when a hover callback
        // retires its recipient before source MouseUp can be delivered.
        if (inputEvent.Kind == LibreInputEventKind.PointerUp)
            SetPortableButtonState(ToMouseButtons(inputEvent.Button), isDown: false);

        Point rootPosition = new(inputEvent.Position.X, inputEvent.Position.Y);
        s_portableMousePosition = PointToScreen(rootPosition);

        if (!ReferenceEquals(s_portableHoverRoot, this))
        {
            Control? previousRoot = s_portableHoverRoot;
            // Claim the new window before public leave callbacks. Nested input
            // owns its replacement even if it returns to the previous window.
            s_portableHoverRoot = this;
            if (previousRoot is not null)
            {
                previousRoot._portablePointerInputVersion++;
                previousRoot.RetirePortableHover();
            }

            if (!IsCurrentPortablePointerInput(inputVersion, receivingHandle))
                return;
        }

        if (inputEvent.Kind == LibreInputEventKind.PointerDown
            && inputEvent.Button is LibrePointerButton.Primary or LibrePointerButton.Secondary or LibrePointerButton.Middle)
        {
            ToolStripDropDown.ProcessPortablePointerDown(PortableHitTest(rootPosition), s_portableMousePosition);
            // Closing callbacks may dispose or recreate the receiving window.
            // Never deliver an old native event into its replacement generation.
            if (!IsCurrentPortablePointerInput(inputVersion, receivingHandle))
                return;
        }

        Control? hit = PortableHitTest(rootPosition);
        bool clientHit = hit is not null && hit.PortableClientRectangle.Contains(hit.PointToClient(s_portableMousePosition));
        Control? capturedBeforeHover = _portableCapturedControl;
        LibreHandle capturedHandle = capturedBeforeHover?._window.PortableHandle ?? default;
        uint captureVersion = _portablePointerCaptureVersion;
        if (!UpdatePortableHover(clientHit ? hit : null, inputVersion, receivingHandle))
        {
            RetireInvalidPortablePointerCapture(capturedBeforeHover, capturedHandle, captureVersion, inputVersion, receivingHandle);
            return;
        }

        if (capturedBeforeHover is not null && ReferenceEquals(_portableCapturedControl, capturedBeforeHover)
            && !IsCurrentPortablePointerTarget(capturedBeforeHover, capturedHandle))
        {
            RetireInvalidPortablePointerCapture(capturedBeforeHover, capturedHandle, captureVersion, inputVersion, receivingHandle);
            return;
        }

        Control? target = _portableCapturedControl ?? hit;
        LibreHandle targetHandle = target?._window.PortableHandle ?? default;
        captureVersion = _portablePointerCaptureVersion;
        if (target is not null && !IsCurrentPortablePointerTarget(target, targetHandle))
        {
            RetireInvalidPortablePointerCapture(target, targetHandle, captureVersion, inputVersion, receivingHandle);
            return;
        }

        RefreshPortableCursor();
        if (!IsCurrentPortablePointerInput(inputVersion, receivingHandle))
            return;
        if (target is null || (_portableCapturedControl is null && !clientHit))
        {
            return;
        }

        if (!IsCurrentPortablePointerTarget(target, targetHandle))
        {
            RetireInvalidPortablePointerCapture(target, targetHandle, captureVersion, inputVersion, receivingHandle);
            return;
        }

        Point location = target.PointToClient(s_portableMousePosition);
        MouseButtons button = ToMouseButtons(inputEvent.Button);
        switch (inputEvent.Kind)
        {
            case LibreInputEventKind.PointerMove:
                MouseEventArgs move = new(s_portableMouseButtons, 0, location.X, location.Y, 0);
                PortablePointerDispatchContext moveContext = new(this, target);
                if (!target.GetStyle(ControlStyles.UserMouse)
                    && (!target.ProcessPortableMouseMoveDefault(move, moveContext) || !moveContext.IsCurrent))
                    return;
                target.OnMouseMove(move);
                break;
            case LibreInputEventKind.PointerDown:
                SetPortableButtonState(button, isDown: true);
                if (button == MouseButtons.Left && target.GetStyle(ControlStyles.Selectable))
                {
                    target.Focus();
                    // GotFocus can close the popup or replace either source
                    // handle. Do not finish this press in a retired control.
                    if (!IsCurrentPortablePointerInput(inputVersion, receivingHandle)
                        || !IsCurrentPortablePointerTarget(target, targetHandle)
                        || _portablePointerCaptureVersion != captureVersion)
                    {
                        if (_portablePointerInputVersion == inputVersion && _portablePointerCaptureVersion == captureVersion
                            && ReferenceEquals(s_portablePointerRoot, this))
                            SetPortableButtonState(button, isDown: false);
                        return;
                    }
                }

                _portableCapturedControl = target;
                _portablePointerCaptureVersion++;
                _portablePressedControl = target;
                _portablePressedButton = button;
                _portablePointerPressVersion++;
                int clicks = 1;
                if (IsNativePointerButton(inputEvent, LibreNativePointerKind.Down, out var nativeDown))
                {
                    if (nativeDown.ClickCount > 0 && previousClick is { Clicks: 1, NativeCount: > 0 and < int.MaxValue }
                        && nativeDown.ClickCount == previousClick.NativeCount + 1
                        && previousClick.Matches(this, target, button))
                        clicks = 2;
                    _portableNativePress = new(this, target, button, nativeDown.ClickCount, clicks);
                }

                PortableNativeClick? nativePress = _portableNativePress;
                uint pressVersion = _portablePointerPressVersion;
                try
                {
                    MouseEventArgs down = new(button, clicks, location.X, location.Y, 0);
                    PortablePointerDispatchContext downContext = new(this, target);
                    // Like WmMouseDown/WmMouseMove's DefWndProc, edit-control
                    // defaults precede even a derived override's first line.
                    if (!target.GetStyle(ControlStyles.UserMouse)
                        && (!target.ProcessPortableMouseDownDefault(down, downContext) || !downContext.IsCurrent))
                        return;
                    target.OnMouseDown(down);
                    // The native WndProc samples this style after MouseDown,
                    // not at the later release. A callback may change it, but
                    // cannot classify a newer reentrant press on this one's behalf.
                    if (nativePress is not null && _portablePointerPressVersion == pressVersion
                        && ReferenceEquals(_portableNativePress, nativePress)
                        && nativePress.Matches(this, target, button))
                        nativePress.StandardDoubleClickEnabled = target.GetStyle(ControlStyles.StandardDoubleClick);
                }
                catch
                {
                    if (_portablePointerPressVersion == pressVersion && ReferenceEquals(_portableNativePress, nativePress))
                        _portableNativePress = null;
                    throw;
                }

                break;
            case LibreInputEventKind.PointerUp:
                DispatchPortableMouseUp(target, hit, button, location, inputEvent);
                break;
            case LibreInputEventKind.PointerWheel:
                DispatchPortableMouseWheel(target, s_portableMousePosition, inputEvent.Delta.Y);
                break;
        }
    }

    private void DispatchPortableMouseUp(Control target, Control? hit, MouseButtons button, Point location,
        in LibreInputEvent inputEvent)
    {
        LibreHandle receivingHandle = _window.PortableHandle;
        nint targetHandle = target.Handle;
        uint pressVersion = _portablePointerPressVersion;
        Control? captured = _portableCapturedControl;
        uint captureVersion = _portablePointerCaptureVersion;
        Exception? callbackError = null;
        PortableNativeClick? nativePress = _portableNativePress;
        _portableNativePress = null;
        _portableCompletedNativeClick = null;
        bool nativeUp = IsNativePointerButton(inputEvent, LibreNativePointerKind.Up, out _);
        bool nativeRelease = nativeUp && nativePress is not null && nativePress.Matches(this, target, button);
        bool? nativeDoubleClick = nativeUp ? nativeRelease && nativePress!.Clicks == 2 : null;
        bool completedNativeClick = false;

        bool IsCurrentRelease() => _portablePointerPressVersion == pressVersion
            && !IsDisposed && !Disposing && IsHandleCreated && _window.PortableHandle == receivingHandle
            && !target.IsDisposed && !target.Disposing && target.IsHandleCreated && target.Handle == targetHandle;

        try
        {
            if (button == MouseButtons.Right)
            {
                // WmMouseUp's native default processing sends WM_CONTEXTMENU
                // before Click/MouseClick/MouseUp. Enter the actual source
                // procedure so TextBox/DataGridView/custom control policy and
                // parent default processing remain authoritative.
                Message context = Message.Create(targetHandle, (int)PInvokeCore.WM_CONTEXTMENU,
                    targetHandle, (nint)(LPARAM)s_portableMousePosition);
                target.WndProc(ref context);
            }

            if (!IsCurrentRelease())
                return;

            bool eligibleClick = target == _portablePressedControl
                && button == _portablePressedButton
                && hit == target
                && !target.ValidationCancelled;
            bool nativeClickEligible = nativeRelease && eligibleClick && target.PortableClientRectangle.Contains(location);
            bool fireClick = (nativeUp ? nativeClickEligible : eligibleClick) && target.GetStyle(ControlStyles.StandardClick);
            if (fireClick)
            {
                if (nativeDoubleClick == true && nativePress!.StandardDoubleClickEnabled)
                {
                    MouseEventArgs clickEvent = new(button, 2, location.X, location.Y, 0);
                    target.OnDoubleClick(clickEvent);
                    if (!IsCurrentRelease())
                        return;
                    target.OnMouseDoubleClick(clickEvent);
                }
                else
                {
                    MouseEventArgs clickEvent = new(button, 1, location.X, location.Y, 0);
                    target.OnClick(clickEvent);
                    if (!IsCurrentRelease())
                        return;
                    target.OnMouseClick(clickEvent);
                }
            }

            if (IsCurrentRelease())
            {
                // Up retains the original canonical count of one. TextBoxBase
                // owns its own click notifications inside this virtual dispatch.
                target.InvokePortableMouseUp(new MouseEventArgs(button, 1, location.X, location.Y, 0),
                    nativeDoubleClick, nativeClickEligible, nativeUp ? IsCurrentRelease : null);
                completedNativeClick = nativeClickEligible && nativePress!.NativeCount > 0
                    && !target.ValidationCancelled;
            }
        }
        catch (Exception error)
        {
            callbackError = error;
            throw;
        }
        finally
        {
            // A callback can start another press, even on the same control.
            // Retire only this release's state, never its replacement.
            if (_portablePointerPressVersion == pressVersion)
            {
                if (IsCurrentRelease())
                    target.SetState(States.ValidationCancelled, false);
                _portablePressedControl = null;
                _portablePressedButton = MouseButtons.None;
                _portableNativePress = null;
                if (_portablePointerCaptureVersion == captureVersion && ReferenceEquals(_portableCapturedControl, captured))
                {
                    _portableCapturedControl = null;
                    _portablePointerCaptureVersion++;
                }

                try
                {
                    if (!IsDisposed && !Disposing && IsHandleCreated && _window.PortableHandle == receivingHandle)
                        RefreshPortableCursor();
                }
                catch (Exception cleanupError) when (callbackError is not null)
                {
                    callbackError.Data["PortableMouseUpCleanupError"] = cleanupError;
                }
            }
        }

        // Publication follows all callbacks and cleanup. A nested press, failed
        // release, retired source or replacement target cannot seed a later pair.
        if (completedNativeClick && IsCurrentRelease() && nativePress!.Matches(this, target, button)
            && IsCurrentPortablePointerTarget(target, target._window.PortableHandle))
            _portableCompletedNativeClick = nativePress;
    }

    internal virtual void InvokePortableMouseUp(MouseEventArgs e, bool? nativeDoubleClick,
        bool nativeClickEligible, Func<bool>? isCurrentRelease)
        => OnMouseUp(e);

    internal virtual bool ProcessPortableMouseDownDefault(MouseEventArgs e, in PortablePointerDispatchContext context)
        => true;

    internal virtual bool ProcessPortableMouseMoveDefault(MouseEventArgs e, in PortablePointerDispatchContext context)
        => true;

    // Value-owned callback scope: checking it never creates a handle or borrows
    // a delegate whose closure could accidentally follow a replacement press.
    internal readonly struct PortablePointerDispatchContext
    {
        private readonly Control _source;
        private readonly Control _target;
        private readonly LibreHandle _sourceHandle;
        private readonly LibreHandle _targetHandle;
        private readonly uint _inputVersion;
        private readonly uint _pressVersion;
        private readonly uint _captureVersion;

        internal PortablePointerDispatchContext(Control source, Control target)
        {
            _source = source;
            _target = target;
            _sourceHandle = source._window.PortableHandle;
            _targetHandle = target._window.PortableHandle;
            _inputVersion = source._portablePointerInputVersion;
            _pressVersion = source._portablePointerPressVersion;
            _captureVersion = source._portablePointerCaptureVersion;
        }

        internal bool IsCurrent => IsCurrentPress && _source._portablePointerInputVersion == _inputVersion;

        internal bool IsCurrentPress => _source._portablePointerPressVersion == _pressVersion
            && _source._portablePointerCaptureVersion == _captureVersion
            && _source.IsCurrentPortablePointerInput(_source._portablePointerInputVersion, _sourceHandle)
            && _target.IsHandleCreated && _source.IsCurrentPortablePointerTarget(_target, _targetHandle);
    }

    private static bool IsNativePointerButton(in LibreInputEvent input, LibreNativePointerKind kind,
        out LibreNativePointerMetadata native)
    {
        native = input.NativePointer.GetValueOrDefault();
        return input.NativePointer.HasValue && native.Kind == kind && native.ClickCount >= 0
            && input.Button is >= LibrePointerButton.Primary and <= LibrePointerButton.XButton2
            && native.Button == (int)input.Button - 1;
    }

    private sealed class PortableNativeClick
    {
        private readonly WeakReference<Control> _target;
        private readonly LibreHandle _sourceHandle;
        private readonly LibreHandle _targetHandle;
        private readonly MouseButtons _button;

        internal int NativeCount { get; }
        internal int Clicks { get; }
        internal bool StandardDoubleClickEnabled { get; set; }

        internal PortableNativeClick(Control source, Control target, MouseButtons button, int nativeCount, int clicks)
        {
            _target = new(target);
            _sourceHandle = source._window.PortableHandle;
            _targetHandle = target._window.PortableHandle;
            _button = button;
            NativeCount = nativeCount;
            Clicks = clicks;
        }

        internal bool Matches(Control source, Control target, MouseButtons button)
            => _sourceHandle == source._window.PortableHandle && _targetHandle == target._window.PortableHandle
                && _button == button && _target.TryGetTarget(out Control? previous) && ReferenceEquals(previous, target);
    }

    private static void DispatchPortableMouseWheel(Control target, Point screenPosition, int delta)
    {
        for (Control? current = target; current is not null; current = current.ParentInternal)
        {
            Point location = current.PointToClient(screenPosition);
            HandledMouseEventArgs args = new(MouseButtons.None, 0, location.X, location.Y, delta);
            current.OnMouseWheel(args);
            if (args.Handled)
            {
                return;
            }
        }
    }

    private bool IsCurrentPortablePointerInput(uint version, LibreHandle receivingHandle)
        => _portablePointerInputVersion == version && ReferenceEquals(s_portablePointerRoot, this)
            && !IsDisposed && !Disposing && Visible && IsHandleCreated
            && _window.PortableHandle == receivingHandle;

    private bool IsCurrentPortablePointerTarget(Control target, LibreHandle targetHandle)
        => !target.IsDisposed && !target.Disposing && target.Visible && target.Enabled
            && target._window.PortableHandle == targetHandle
            && ReferenceEquals(target.GetPortableTopLevelControl(), this);

    private void RetireInvalidPortablePointerCapture(Control? target, LibreHandle targetHandle,
        uint captureVersion, uint inputVersion, LibreHandle receivingHandle)
    {
        if (target is not null && _portablePointerCaptureVersion == captureVersion
            && ReferenceEquals(_portableCapturedControl, target)
            && IsCurrentPortablePointerInput(inputVersion, receivingHandle)
            && !IsCurrentPortablePointerTarget(target, targetHandle))
            CancelPortableCapture(updateCursor: false);
    }

    private bool UpdatePortableHover(Control? target, uint inputVersion, LibreHandle receivingHandle)
    {
        LibreHandle targetHandle = target?._window.PortableHandle ?? default;
        if (_portableHoveredControl == target && _portableHoveredControlHandle == targetHandle
            && _portableHoverWindowHandle == receivingHandle)
        {
            return true;
        }

        RetirePortableHover();
        if (!IsCurrentPortablePointerInput(inputVersion, receivingHandle)
            || (target is not null && !IsCurrentPortablePointerTarget(target, targetHandle)))
            return false;

        _portableHoveredControl = target;
        _portableHoveredControlHandle = targetHandle;
        _portableHoverWindowHandle = receivingHandle;
        target?.OnMouseEnter(EventArgs.Empty);
        if (!IsCurrentPortablePointerInput(inputVersion, receivingHandle))
            return false;
        if (target is not null && !IsCurrentPortablePointerTarget(target, targetHandle))
        {
            // Do not retain a disposed, reparented or replaced target. A nested
            // input generation was already excluded above and owns its state.
            _portableHoveredControl = null;
            _portableHoveredControlHandle = default;
            _portableHoverWindowHandle = default;
            return false;
        }

        return true;
    }

    private void RetirePortableHover()
    {
        Control? previous = TakePortableHover(out LibreHandle previousHandle, out LibreHandle previousWindowHandle);
        NotifyPortableHoverLeave(previous, previousHandle, previousWindowHandle);
    }

    private Control? TakePortableHover(out LibreHandle previousHandle, out LibreHandle previousWindowHandle)
    {
        Control? previous = _portableHoveredControl;
        previousHandle = _portableHoveredControlHandle;
        previousWindowHandle = _portableHoverWindowHandle;
        // Clear first for same-window and cross-window transitions alike. A
        // retired native handle must not send leave into its replacement.
        _portableHoveredControl = null;
        _portableHoveredControlHandle = default;
        _portableHoverWindowHandle = default;
        return previous;
    }

    private void NotifyPortableHoverLeave(Control? previous, LibreHandle previousHandle, LibreHandle previousWindowHandle)
    {
        if (previous is not null && previousWindowHandle == _window.PortableHandle
            && !IsDisposed && !Disposing && IsHandleCreated
            && !previous.IsDisposed && !previous.Disposing
            && previous._window.PortableHandle == previousHandle
            && ReferenceEquals(previous.GetPortableTopLevelControl(), this))
            previous.OnMouseLeave(EventArgs.Empty);
    }

    private void RefreshPortableCursor(bool force = false)
    {
        Control root = GetPortableTopLevelControl();
        Control? target = root._portableCapturedControl ?? root._portableHoveredControl;
        Cursor cursor = target?.Cursor ?? Cursors.Default;
        LibreCursorShape shape = cursor.PortableShape;
        if (!force && root._portableAppliedCursorShape == shape)
        {
            return;
        }

        root._window.SetPortableCursor(shape);
        root._portableAppliedCursorShape = shape;
        Cursor.SetPortableCurrentFromInput(cursor);
    }

    internal static void ApplyPortableCursorOverride(Cursor? cursor)
    {
        if (cursor is null || s_portablePointerRoot is null)
        {
            return;
        }

        LibreCursorShape shape = cursor.PortableShape;
        s_portablePointerRoot._window.SetPortableCursor(shape);
        s_portablePointerRoot._portableAppliedCursorShape = shape;
    }

    internal static void ApplyPortableCursorVisibility(bool visible)
    {
        s_portablePointerRoot?._window.SetPortableCursorVisible(visible);
    }

    private Control? PortableHitTest(Point position)
    {
        Padding ownInsets = PortableNonClientInsets;
        Rectangle window = new(-ownInsets.Left, -ownInsets.Top, Width, Height);
        if (!Visible || !Enabled || !window.Contains(position))
        {
            return null;
        }

        // A source border blocks underlying controls but is not a client mouse
        // event. Only an existing capture continues receiving outside-client input.
        if (!PortableClientRectangle.Contains(position)) return this;

        if (ChildControls is { } children)
        {
            for (int index = 0; index < children.Count; index++)
            {
                Control child = children[index];
                Padding insets = child.PortableNonClientInsets;
                Control? hit = child.PortableHitTest(new Point(
                    position.X - child._x - insets.Left, position.Y - child._y - insets.Top));
                if (hit is not null)
                {
                    return hit;
                }
            }
        }

        return this;
    }

    private Point PortableClientOriginOnScreen()
    {
        int x = 0;
        int y = 0;
        for (Control? current = this; current is not null; current = current.ParentInternal)
        {
            Padding insets = current.PortableNonClientInsets;
            x = checked(x + current._x + insets.Left);
            y = checked(y + current._y + insets.Top);
        }

        return new Point(x, y);
    }

    private bool PortableContainsFocus()
    {
        Control root = GetPortableFocusRoot();
        if (!root._portableWindowFocused || root._portableFocusedControl is not { } focused)
        {
            return false;
        }

        for (Control? current = focused; current is not null; current = current.ParentInternal)
        {
            if (current == this)
            {
                return true;
            }
        }

        return false;
    }

    private static Keys ToKeys(LibreInputModifiers modifiers)
    {
        Keys keys = Keys.None;
        if (modifiers.HasFlag(LibreInputModifiers.Shift)) keys |= Keys.Shift;
        if (modifiers.HasFlag(LibreInputModifiers.Control)) keys |= Keys.Control;
        if (modifiers.HasFlag(LibreInputModifiers.Alt)) keys |= Keys.Alt;
        return keys;
    }

    private static Keys ToKeys(LibreKey key)
    {
        if (key is >= LibreKey.D0 and <= LibreKey.D9)
        {
            return Keys.D0 + (key - LibreKey.D0);
        }

        if (key is >= LibreKey.A and <= LibreKey.Z)
        {
            return Keys.A + (key - LibreKey.A);
        }

        if (key is >= LibreKey.F1 and <= LibreKey.F24)
        {
            return Keys.F1 + (key - LibreKey.F1);
        }

        if (key is >= LibreKey.NumPad0 and <= LibreKey.NumPad9)
        {
            return Keys.NumPad0 + (key - LibreKey.NumPad0);
        }

        return key switch
        {
            LibreKey.Space => Keys.Space,
            LibreKey.Apostrophe => Keys.OemQuotes,
            LibreKey.Comma => Keys.Oemcomma,
            LibreKey.Minus => Keys.OemMinus,
            LibreKey.Period => Keys.OemPeriod,
            LibreKey.Slash => Keys.OemQuestion,
            LibreKey.Semicolon => Keys.OemSemicolon,
            LibreKey.Equal => Keys.Oemplus,
            LibreKey.LeftBracket => Keys.OemOpenBrackets,
            LibreKey.Backslash => Keys.OemPipe,
            LibreKey.RightBracket => Keys.OemCloseBrackets,
            LibreKey.GraveAccent => Keys.Oemtilde,
            LibreKey.Escape => Keys.Escape,
            LibreKey.Enter or LibreKey.NumPadEnter => Keys.Enter,
            LibreKey.Tab => Keys.Tab,
            LibreKey.Backspace => Keys.Back,
            LibreKey.Insert => Keys.Insert,
            LibreKey.Delete => Keys.Delete,
            LibreKey.Right => Keys.Right,
            LibreKey.Left => Keys.Left,
            LibreKey.Down => Keys.Down,
            LibreKey.Up => Keys.Up,
            LibreKey.PageUp => Keys.PageUp,
            LibreKey.PageDown => Keys.PageDown,
            LibreKey.Home => Keys.Home,
            LibreKey.End => Keys.End,
            LibreKey.CapsLock => Keys.CapsLock,
            LibreKey.ScrollLock => Keys.Scroll,
            LibreKey.NumLock => Keys.NumLock,
            LibreKey.PrintScreen => Keys.PrintScreen,
            LibreKey.Pause => Keys.Pause,
            LibreKey.NumPadDecimal => Keys.Decimal,
            LibreKey.NumPadDivide => Keys.Divide,
            LibreKey.NumPadMultiply => Keys.Multiply,
            LibreKey.NumPadSubtract => Keys.Subtract,
            LibreKey.NumPadAdd => Keys.Add,
            LibreKey.NumPadEqual => Keys.Oemplus,
            LibreKey.LeftShift => Keys.LShiftKey,
            LibreKey.LeftControl => Keys.LControlKey,
            LibreKey.LeftAlt => Keys.LMenu,
            LibreKey.LeftMeta => Keys.LWin,
            LibreKey.RightShift => Keys.RShiftKey,
            LibreKey.RightControl => Keys.RControlKey,
            LibreKey.RightAlt => Keys.RMenu,
            LibreKey.RightMeta => Keys.RWin,
            LibreKey.Menu => Keys.Apps,
            _ => Keys.None,
        };
    }

    private static MouseButtons ToMouseButtons(LibrePointerButton button) => button switch
    {
        LibrePointerButton.Primary => MouseButtons.Left,
        LibrePointerButton.Secondary => MouseButtons.Right,
        LibrePointerButton.Middle => MouseButtons.Middle,
        LibrePointerButton.XButton1 => MouseButtons.XButton1,
        LibrePointerButton.XButton2 => MouseButtons.XButton2,
        _ => MouseButtons.None,
    };
}
#endif
