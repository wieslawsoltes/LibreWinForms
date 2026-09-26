// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public partial class Control
{
    private Keys _portablePendingMenuKey;
    private LibreHandle _portablePendingMenuWindow;
    private uint _portableMenuInputVersion;
    private LibreInputModifiers _portableMenuKeyModifiers;

    private void TrackPortableMenuKeyInput(in LibreInputEvent input)
    {
        _portableMenuKeyModifiers = input.Modifiers;
        // Cancellation precedes application filters and callbacks. An ignored
        // chord or mouse click must not turn a later Alt release into activation.
        if (input.Kind is LibreInputEventKind.KeyDown or LibreInputEventKind.KeyUp
            or LibreInputEventKind.TextInput or LibreInputEventKind.PointerDown
            or LibreInputEventKind.FocusLost)
        {
            _portableMenuInputVersion++;
            if (input.Kind != LibreInputEventKind.KeyUp
                && (input.Kind != LibreInputEventKind.KeyDown
                    || ToKeys(input.Key) != _portablePendingMenuKey
                    || !IsPortableBareMenuKey(ToKeys(input.Key))))
                _portablePendingMenuKey = Keys.None;
        }
    }

    private bool IsPortableBareMenuKey(Keys key)
        => key == Keys.F10 ? _portableMenuKeyModifiers == LibreInputModifiers.None
            : (key is Keys.LMenu or Keys.RMenu or Keys.Menu)
                && (_portableMenuKeyModifiers & ~LibreInputModifiers.Alt) == LibreInputModifiers.None;
}
#endif
