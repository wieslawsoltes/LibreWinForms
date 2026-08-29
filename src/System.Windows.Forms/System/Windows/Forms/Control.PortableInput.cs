// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
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
    private static Point s_portableMousePosition;

    private Control? _portableFocusedControl;
    private Control? _portableHoveredControl;
    private Control? _portableCapturedControl;
    private Control? _portablePressedControl;
    private MouseButtons _portablePressedButton;
    private bool _portableWindowFocused;
    private LibreCursorShape? _portableAppliedCursorShape;

    internal void DispatchPortableInput(in LibreInputEvent inputEvent)
    {
        Control root = GetPortableTopLevelControl();
        s_portableModifierKeys = ToKeys(inputEvent.Modifiers);

        switch (inputEvent.Kind)
        {
            case LibreInputEventKind.FocusGained:
                root.SetPortableWindowFocus(focused: true);
                break;
            case LibreInputEventKind.FocusLost:
                s_portableModifierKeys = Keys.None;
                s_portableKeysDown?.Clear();
                root.SetPortableWindowFocus(focused: false);
                break;
            case LibreInputEventKind.KeyDown:
                SetPortableKeyState(inputEvent.Key, isDown: true);
                root.DispatchPortableKey(inputEvent.Key, PInvokeCore.WM_KEYDOWN);
                break;
            case LibreInputEventKind.KeyUp:
                SetPortableKeyState(inputEvent.Key, isDown: false);
                root.DispatchPortableKey(inputEvent.Key, PInvokeCore.WM_KEYUP);
                break;
            case LibreInputEventKind.TextInput:
                root.DispatchPortableText(inputEvent.Text);
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

    internal void CancelPortableCapture()
    {
        Control root = GetPortableTopLevelControl();
        Control? captured = root._portableCapturedControl;
        root._portableCapturedControl = null;
        root._portablePressedControl = null;
        root._portablePressedButton = MouseButtons.None;
        s_portableMouseButtons = MouseButtons.None;
        captured?.OnMouseCaptureChanged(EventArgs.Empty);
        root.RefreshPortableCursor();
    }

    private void SetPortableWindowFocus(bool focused)
    {
        if (_portableWindowFocused == focused)
        {
            return;
        }

        _portableWindowFocused = focused;
        if (this is Form form)
        {
            Form.SetPortableActiveForm(focused ? form : null);
        }

        if (focused)
        {
            Control target = _portableFocusedControl ?? this;
            _portableFocusedControl = target;
            target.InvokeGotFocus(target, EventArgs.Empty);
        }
        else if (_portableFocusedControl is { } target)
        {
            target.InvokeLostFocus(target, EventArgs.Empty);
        }
    }

    private void SetPortableFocus(Control target)
    {
        if (_portableFocusedControl == target)
        {
            return;
        }

        Control? previous = _portableFocusedControl;
        _portableFocusedControl = target;
        if (_portableWindowFocused)
        {
            previous?.InvokeLostFocus(previous, EventArgs.Empty);
            target.InvokeGotFocus(target, EventArgs.Empty);
        }
    }

    private void DispatchPortableKey(LibreKey key, uint messageId)
    {
        Keys keyCode = ToKeys(key);
        if (keyCode == Keys.None)
        {
            return;
        }

        Control target = _portableFocusedControl ?? this;
        Message message = Message.Create(target.Handle, (int)messageId, (nint)(int)keyCode, 0);
        if (Application.FilterMessage(ref message))
        {
            return;
        }

        if (PreProcessControlMessageInternal(target, ref message) != PreProcessControlState.MessageProcessed)
        {
            target.ProcessKeyMessage(ref message);
        }
    }

    private void DispatchPortableText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Control target = _portableFocusedControl ?? this;
        foreach (char character in text)
        {
            Message message = Message.Create(target.Handle, (int)PInvokeCore.WM_CHAR, character, 0);
            if (Application.FilterMessage(ref message))
            {
                continue;
            }

            if (PreProcessControlMessageInternal(target, ref message) != PreProcessControlState.MessageProcessed)
            {
                target.ProcessKeyMessage(ref message);
            }
        }
    }

    private void DispatchPortablePointer(in LibreInputEvent inputEvent)
    {
        Point rootPosition = new(inputEvent.Position.X, inputEvent.Position.Y);
        s_portableMousePosition = PointToScreen(rootPosition);

        Control? hit = PortableHitTest(rootPosition);
        UpdatePortableHover(hit);
        Control? target = _portableCapturedControl ?? hit;
        RefreshPortableCursor();
        if (target is null)
        {
            return;
        }

        Point location = target.PointToClient(s_portableMousePosition);
        MouseButtons button = ToMouseButtons(inputEvent.Button);
        switch (inputEvent.Kind)
        {
            case LibreInputEventKind.PointerMove:
                target.OnMouseMove(new MouseEventArgs(s_portableMouseButtons, 0, location.X, location.Y, 0));
                break;
            case LibreInputEventKind.PointerDown:
                s_portableMouseButtons |= button;
                if (button == MouseButtons.Left && target.GetStyle(ControlStyles.Selectable))
                {
                    target.Focus();
                }

                _portableCapturedControl = target;
                _portablePressedControl = target;
                _portablePressedButton = button;
                target.OnMouseDown(new MouseEventArgs(button, 1, location.X, location.Y, 0));
                break;
            case LibreInputEventKind.PointerUp:
                s_portableMouseButtons &= ~button;
                bool fireClick = target == _portablePressedControl
                    && button == _portablePressedButton
                    && hit == target
                    && target.GetStyle(ControlStyles.StandardClick);
                if (fireClick)
                {
                    MouseEventArgs clickEvent = new(button, 1, location.X, location.Y, 0);
                    target.OnClick(clickEvent);
                    target.OnMouseClick(clickEvent);
                }

                target.OnMouseUp(new MouseEventArgs(button, 1, location.X, location.Y, 0));
                _portableCapturedControl = null;
                _portablePressedControl = null;
                _portablePressedButton = MouseButtons.None;
                RefreshPortableCursor();
                break;
            case LibreInputEventKind.PointerWheel:
                DispatchPortableMouseWheel(target, s_portableMousePosition, inputEvent.Delta.Y);
                break;
        }
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

    private void UpdatePortableHover(Control? target)
    {
        if (_portableHoveredControl == target)
        {
            return;
        }

        _portableHoveredControl?.OnMouseLeave(EventArgs.Empty);
        _portableHoveredControl = target;
        target?.OnMouseEnter(EventArgs.Empty);
    }

    private void RefreshPortableCursor(bool force = false)
    {
        Control root = GetPortableTopLevelControl();
        Control? target = root._portableCapturedControl ?? root._portableHoveredControl;
        LibreCursorShape shape = (target?.Cursor ?? Cursors.Default).PortableShape;
        if (!force && root._portableAppliedCursorShape == shape)
        {
            return;
        }

        root._window.SetPortableCursor(shape);
        root._portableAppliedCursorShape = shape;
    }

    private Control? PortableHitTest(Point position)
    {
        if (!Visible || !Enabled || !ClientRectangle.Contains(position))
        {
            return null;
        }

        if (ChildControls is { } children)
        {
            for (int index = 0; index < children.Count; index++)
            {
                Control child = children[index];
                Control? hit = child.PortableHitTest(new Point(position.X - child._x, position.Y - child._y));
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
            x = checked(x + current._x);
            y = checked(y + current._y);
        }

        return new Point(x, y);
    }

    private bool PortableContainsFocus()
    {
        Control root = GetPortableTopLevelControl();
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
