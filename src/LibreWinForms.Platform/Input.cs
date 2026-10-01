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
    SystemTextInput,
    // Keep existing wire values stable. These retire pointer state only;
    // neither event is a keyboard-focus loss or a synthetic mouse release.
    PointerLeave,
    PointerCancel,
    // Lossless source scrolling is distinct from legacy integer MouseWheel.
    PointerScroll,
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

/// <summary>
/// A backend-neutral key identity. Values are intentionally not tied to
/// Win32 virtual keys or to any windowing/input library enum.
/// </summary>
public enum LibreKey
{
    Unknown,
    Space,
    Apostrophe,
    Comma,
    Minus,
    Period,
    Slash,
    D0,
    D1,
    D2,
    D3,
    D4,
    D5,
    D6,
    D7,
    D8,
    D9,
    Semicolon,
    Equal,
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    LeftBracket,
    Backslash,
    RightBracket,
    GraveAccent,
    Escape,
    Enter,
    Tab,
    Backspace,
    Insert,
    Delete,
    Right,
    Left,
    Down,
    Up,
    PageUp,
    PageDown,
    Home,
    End,
    CapsLock,
    ScrollLock,
    NumLock,
    PrintScreen,
    Pause,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    F13,
    F14,
    F15,
    F16,
    F17,
    F18,
    F19,
    F20,
    F21,
    F22,
    F23,
    F24,
    F25,
    NumPad0,
    NumPad1,
    NumPad2,
    NumPad3,
    NumPad4,
    NumPad5,
    NumPad6,
    NumPad7,
    NumPad8,
    NumPad9,
    NumPadDecimal,
    NumPadDivide,
    NumPadMultiply,
    NumPadSubtract,
    NumPadAdd,
    NumPadEnter,
    NumPadEqual,
    LeftShift,
    LeftControl,
    LeftAlt,
    LeftMeta,
    RightShift,
    RightControl,
    RightAlt,
    RightMeta,
    Menu,
}

/// <summary>A normalized input event delivered by a platform window.</summary>
public readonly record struct LibreInputEvent(
    LibreInputEventKind Kind,
    long Timestamp,
    LibreInputModifiers Modifiers,
    LibreKey Key,
    string? Text,
    LibrePoint Position,
    LibrePoint Delta,
    LibrePointerButton Button)
{
    /// <summary>
    /// Optional original native pointer metadata. This is not a wheel or click
    /// policy; canonical coordinates and shortcut modifiers remain separate.
    /// </summary>
    public LibreNativePointerMetadata? NativePointer { get; init; }

    public LibreNativeScrollMetadata? NativeScroll { get; init; }
}

public enum LibreNativePointerKind { Move, Drag, Down, Up, Enter, Leave, Cancel, Scroll }

[Flags]
public enum LibreNativePointerModifiers
{
    None = 0, Shift = 1, Control = 2, Alt = 4, Meta = 8,
    CapsLock = 16, NumericPad = 32, Help = 64, Function = 128,
}

/// <summary>
/// Original native view-point coordinates, seconds on the provider's clock,
/// zero-based native button (-1 for non-button events), click count and flags.
/// Retaining these values does not synthesize canonical double-click behavior.
/// </summary>
public readonly record struct LibreNativePointerMetadata(
    LibreNativePointerKind Kind, double X, double Y, double Timestamp,
    int Button, int ClickCount, LibreNativePointerModifiers Modifiers);

public enum LibreNativeScrollUnit { Lines, Points }
public enum LibreNativeScrollProtocol { Unspecified, AppKit }

/// <summary>One provider subscription's identity, independent of native handles.</summary>
public sealed class LibreNativeScrollStream { }

/// <summary>
/// Original signed native scroll quantities and phase protocol. PointScale maps
/// native view points to the source coordinate frame; line counts are unscaled.
/// Stream and Generation bind fractional state to the original live input owner.
/// </summary>
public readonly record struct LibreNativeScrollMetadata(
    double X, double Y, LibreNativeScrollUnit Unit, LibreNativeScrollProtocol Protocol,
    uint Phase, uint MomentumPhase, double PointScale, LibreNativeScrollStream Stream, ulong Generation)
{
    /// <summary>Validates quantities and the explicitly declared native phase protocol.</summary>
    public void Validate()
    {
        if (Stream is null || !double.IsFinite(X) || !double.IsFinite(Y)
            || !double.IsFinite(PointScale) || PointScale <= 0
            || Unit is < LibreNativeScrollUnit.Lines or > LibreNativeScrollUnit.Points
            || Protocol is < LibreNativeScrollProtocol.Unspecified or > LibreNativeScrollProtocol.AppKit)
            throw new ArgumentException("Invalid native scroll metadata.");
        if (Protocol == LibreNativeScrollProtocol.Unspecified && (Phase != 0 || MomentumPhase != 0))
            throw new PlatformNotSupportedException("Native scroll phases require an explicit protocol.");
        // The AppKit SDK declares these individual values. Bitwise combinations,
        // simultaneous normal/momentum and momentum MayBegin have no contract.
        if (Phase is not (0 or 1 or 2 or 4 or 8 or 16 or 32)
            || MomentumPhase is not (0 or 1 or 2 or 4 or 8 or 16)
            || (Phase != 0 && MomentumPhase != 0))
            throw new ArgumentException("Invalid AppKit scroll phases.");
    }
}
