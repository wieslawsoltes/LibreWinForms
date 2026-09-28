// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public partial class Control
{
    // Native-style controls read window state, not the lazily initialized
    // managed ShowFocusCues/ShowKeyboardCues property cache.
    private uint _portableWindowUIState;

    internal bool ShowPortableNativeFocusCues
        => !IsHandleCreated || (_portableWindowUIState & PInvoke.UISF_HIDEFOCUS) == 0;

    private void ChangePortableUIState(uint action, uint flags)
    {
        Message message = Message.Create(Handle, (int)PInvokeCore.WM_CHANGEUISTATE,
            (nint)(action | flags << 16), 0);
        WndProc(ref message);
    }

    private bool TryProcessPortableUIState(ref Message message)
    {
        if (message.Msg == PInvokeCore.WM_QUERYUISTATE)
        {
            message.Result = (nint)_portableWindowUIState;
            return true;
        }

        if (message.MsgInternal is not PInvokeCore.WM_CHANGEUISTATE and not PInvokeCore.WM_UPDATEUISTATE)
        {
            return false;
        }

        uint action = message.WParamInternal.LOWORD;
        if (action is not PInvoke.UIS_SET and not PInvoke.UIS_CLEAR)
        {
            // UIS_INITIALIZE needs an authoritative last-input contract; do not
            // invent an input device from the platform or the source control.
            return false;
        }

        uint flags = (uint)message.WParamInternal.HIWORD
            & (PInvoke.UISF_HIDEFOCUS | PInvoke.UISF_HIDEACCEL | PInvoke.UISF_ACTIVE);
        uint next = action == PInvoke.UIS_SET
            ? _portableWindowUIState | flags
            : _portableWindowUIState & ~flags;
        message.Result = 0;

        if (message.Msg == PInvokeCore.WM_CHANGEUISTATE)
        {
            if (ParentInternal is { IsHandleCreated: true, IsDisposed: false, Disposing: false } parent)
            {
                Message forwarded = Message.Create(parent.Handle, message.Msg, message.WParam, message.LParam);
                parent.WndProc(ref forwarded);
            }
            else if (next != _portableWindowUIState)
            {
                Message update = Message.Create(Handle, (int)PInvokeCore.WM_UPDATEUISTATE,
                    message.WParam, message.LParam);
                WndProc(ref update);
            }

            return true;
        }

        if (next == _portableWindowUIState)
        {
            return true;
        }

        _portableWindowUIState = next;
        if (ChildControls is { Count: > 0 } children)
        {
            // WmUpdateUIState retains canonical cache/event ordering around this
            // default-procedure propagation. Callbacks may change the hierarchy.
            nint handle = Handle;
            (Control Child, nint Handle)[] snapshot = new (Control, nint)[children.Count];
            for (int i = 0; i < children.Count; i++)
            {
                Control child = children[i];
                snapshot[i] = (child, child.IsHandleCreated ? child.Handle : 0);
            }

            foreach ((Control child, nint childHandle) in snapshot)
            {
                if (!IsHandleCreated || Handle != handle || IsDisposed || Disposing)
                {
                    break;
                }

                if (childHandle != 0 && child.IsHandleCreated && child.Handle == childHandle
                    && !child.IsDisposed && !child.Disposing
                    && ReferenceEquals(child.ParentInternal, this))
                {
                    Message update = Message.Create(childHandle, (int)PInvokeCore.WM_UPDATEUISTATE,
                        message.WParam, message.LParam);
                    child.WndProc(ref update);
                }
            }
        }

        return true;
    }
}
#endif
