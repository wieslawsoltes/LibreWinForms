// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

public enum LibreInputEventKind
{
    KeyDown,
    KeyUp,
    TextInput,
    PointerDown,
    PointerUp,
    PointerMove,
    PointerWheel,
    FocusGained,
    FocusLost,
}

[Flags]
public enum LibreInputModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Meta = 8,
}

public enum LibrePointerButton
{
    None,
    Primary,
    Secondary,
    Middle,
    XButton1,
    XButton2,
}

/// <summary>A normalized input event delivered by a platform window.</summary>
public readonly record struct LibreInputEvent(
    LibreInputEventKind Kind,
    long Timestamp,
    LibreInputModifiers Modifiers,
    int Key,
    string? Text,
    LibrePoint Position,
    LibrePoint Delta,
    LibrePointerButton Button);
