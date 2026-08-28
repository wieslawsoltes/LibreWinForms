// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Identifies the host setting families affected by a settings notification.</summary>
[Flags]
public enum LibreSystemSettingsChangeKind
{
    None = 0,
    Accessibility = 1 << 0,
    Color = 1 << 1,
    General = 1 << 2,
    Locale = 1 << 3,
    VisualStyle = 1 << 4,
    Window = 1 << 5,
    Display = 1 << 6,
    All = Accessibility | Color | General | Locale | VisualStyle | Window | Display,
}

/// <summary>Describes a typed host settings change without exposing Microsoft.Win32 event arguments.</summary>
public sealed class LibreSystemSettingsChangedEventArgs : EventArgs
{
    public LibreSystemSettingsChangedEventArgs(LibreSystemSettingsChangeKind kind)
    {
        if (kind == LibreSystemSettingsChangeKind.None || (kind & ~LibreSystemSettingsChangeKind.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
    }

    public LibreSystemSettingsChangeKind Kind { get; }

    public bool Includes(LibreSystemSettingsChangeKind kind) => (Kind & kind) != 0;
}

/// <summary>Supplies host system settings used by canonical managed controls.</summary>
public interface ILibreSystemSettingsService
{
    /// <summary>Raised when host appearance or metric settings have changed.</summary>
    event EventHandler<LibreSystemSettingsChangedEventArgs>? SettingsChanged;

    bool HighContrast { get; }

    LibreSize BorderSize { get; }

    LibreSize FixedFrameBorderSize { get; }

    LibreSize Border3DSize { get; }

    int VerticalScrollBarWidth { get; }

    int HorizontalScrollBarHeight { get; }

    int VerticalScrollBarArrowHeight { get; }

    int HorizontalScrollBarArrowWidth { get; }

    int VerticalScrollBarThumbHeight { get; }

    int HorizontalScrollBarThumbWidth { get; }

    LibreSize DragSize { get; }

    int MouseWheelScrollLines { get; }

    bool MenuAccessKeysUnderlined { get; }

    int KeyboardDelay { get; }

    bool KeyboardPreferred { get; }

    int KeyboardSpeed { get; }

    LibreSize MouseHoverSize { get; }

    int MouseHoverTime { get; }

    int MouseSpeed { get; }

    bool SnapToDefaultButton { get; }
}

/// <summary>Portable baseline used when a host does not expose OS system settings.</summary>
public sealed class DefaultLibreSystemSettingsService : ILibreSystemSettingsService
{
    public static DefaultLibreSystemSettingsService Instance { get; } = new();

    private DefaultLibreSystemSettingsService()
    {
    }

    public event EventHandler<LibreSystemSettingsChangedEventArgs>? SettingsChanged
    {
        add { }
        remove { }
    }

    public bool HighContrast => false;

    public LibreSize BorderSize => new(1, 1);

    public LibreSize FixedFrameBorderSize => new(3, 3);

    public LibreSize Border3DSize => new(2, 2);

    public int VerticalScrollBarWidth => 17;

    public int HorizontalScrollBarHeight => 17;

    public int VerticalScrollBarArrowHeight => 17;

    public int HorizontalScrollBarArrowWidth => 17;

    public int VerticalScrollBarThumbHeight => 17;

    public int HorizontalScrollBarThumbWidth => 17;

    public LibreSize DragSize => new(4, 4);

    public int MouseWheelScrollLines => 3;

    public bool MenuAccessKeysUnderlined => false;

    public int KeyboardDelay => 1;

    public bool KeyboardPreferred => false;

    public int KeyboardSpeed => 31;

    public LibreSize MouseHoverSize => new(4, 4);

    public int MouseHoverTime => 400;

    public int MouseSpeed => 10;

    public bool SnapToDefaultButton => false;
}
