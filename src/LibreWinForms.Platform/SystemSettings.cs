// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;

namespace LibreWinForms.Platform;

/// <summary>Identifies the host boot mode without exposing a WinForms enum.</summary>
public enum LibreBootMode
{
    Normal,
    FailSafe,
    FailSafeWithNetwork,
}

/// <summary>Identifies the host corner used to begin arranging minimized windows.</summary>
public enum LibreMinimizedWindowStartPosition
{
    BottomLeft,
    BottomRight,
    TopLeft,
    TopRight,
}

/// <summary>Identifies the host direction used to arrange minimized windows.</summary>
public enum LibreMinimizedWindowDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>Identifies the host display orientation without exposing a WinForms enum.</summary>
public enum LibreScreenOrientation
{
    Angle0,
    Angle90,
    Angle180,
    Angle270,
}

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

    /// <summary>Creates the host menu font for the requested DPI, or the default DPI when zero.</summary>
    Font GetMenuFont(int dpi);

    LibreSize BorderSize { get; }

    LibreSize FixedFrameBorderSize { get; }

    LibreSize Border3DSize { get; }

    int VerticalScrollBarWidth { get; }

    int HorizontalScrollBarHeight { get; }

    int CaptionHeight { get; }

    int MenuHeight { get; }

    LibreSize MinWindowTrackSize { get; }

    LibreSize IconSize { get; }

    LibreSize CursorSize { get; }

    LibreSize SmallIconSize { get; }

    LibreSize MinimumWindowSize { get; }

    LibreSize CaptionButtonSize { get; }

    LibreSize FrameBorderSize { get; }

    LibreSize MaxWindowTrackSize { get; }

    LibreSize PrimaryMonitorMaximizedWindowSize { get; }

    LibreSize MinimizedWindowSpacingSize { get; }

    int ToolWindowCaptionHeight { get; }

    LibreSize ToolWindowCaptionButtonSize { get; }

    LibreSize MenuButtonSize { get; }

    LibreSize MinimizedWindowSize { get; }

    int KanjiWindowHeight { get; }

    bool DebugOperatingSystem { get; }

    bool RightAlignedMenus { get; }

    bool PenWindows { get; }

    bool DbcsEnabled { get; }

    bool Secure { get; }

    bool Network { get; }

    bool TerminalServerSession { get; }

    LibreBootMode BootMode { get; }

    bool ShowSounds { get; }

    LibreSize MenuCheckSize { get; }

    bool MidEastEnabled { get; }

    LibreMinimizedWindowStartPosition MinimizedWindowStartPosition { get; }

    LibreMinimizedWindowDirection MinimizedWindowDirection { get; }

    bool HideMinimizedWindows { get; }

    LibreScreenOrientation ScreenOrientation { get; }

    int SizingBorderWidth { get; }

    LibreSize SmallCaptionButtonSize { get; }

    LibreSize MenuBarButtonSize { get; }

    bool LockedTerminalSession { get; }

    int VerticalScrollBarArrowHeight { get; }

    int HorizontalScrollBarArrowWidth { get; }

    int VerticalScrollBarThumbHeight { get; }

    int HorizontalScrollBarThumbWidth { get; }

    LibreSize DragSize { get; }

    bool MousePresent { get; }

    bool MouseButtonsSwapped { get; }

    int MouseButtons { get; }

    LibreSize DoubleClickSize { get; }

    int DoubleClickTime { get; }

    bool MouseWheelPresent { get; }

    int CaretBlinkTime { get; }

    int MouseWheelScrollLines { get; }

    bool MenuAccessKeysUnderlined { get; }

    int KeyboardDelay { get; }

    bool KeyboardPreferred { get; }

    int KeyboardSpeed { get; }

    LibreSize MouseHoverSize { get; }

    int MouseHoverTime { get; }

    int MouseSpeed { get; }

    bool SnapToDefaultButton { get; }

    bool DragFullWindows { get; }

    bool DropShadowEnabled { get; }

    bool FlatMenuEnabled { get; }

    bool PopupMenusLeftAligned { get; }

    bool MenuFadeEnabled { get; }

    int MenuShowDelay { get; }

    bool ComboBoxAnimationEnabled { get; }

    bool TitleBarGradientEnabled { get; }

    bool HotTrackingEnabled { get; }

    bool ListBoxSmoothScrollingEnabled { get; }

    bool MenuAnimationEnabled { get; }

    bool SelectionFadeEnabled { get; }

    bool ToolTipAnimationEnabled { get; }

    bool UIEffectsEnabled { get; }

    bool ActiveWindowTrackingEnabled { get; }

    int ActiveWindowTrackingDelay { get; }

    bool MinimizeRestoreAnimationEnabled { get; }

    int BorderMultiplierFactor { get; }

    int CaretWidth { get; }

    int VerticalFocusThickness { get; }

    int HorizontalFocusThickness { get; }

    int VerticalResizeBorderThickness { get; }

    int HorizontalResizeBorderThickness { get; }

    bool FontSmoothingEnabled { get; }

    int FontSmoothingContrast { get; }

    int FontSmoothingType { get; }

    int IconHorizontalSpacing { get; }

    int IconVerticalSpacing { get; }

    bool IconTitleWrappingEnabled { get; }
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

    public Font GetMenuFont(int dpi) => (Font)SystemFonts.MenuFont.Clone();

    public LibreSize BorderSize => new(1, 1);

    public LibreSize FixedFrameBorderSize => new(3, 3);

    public LibreSize Border3DSize => new(2, 2);

    public int VerticalScrollBarWidth => 17;

    public int HorizontalScrollBarHeight => 17;

    public int CaptionHeight => 23;

    public int MenuHeight => 19;

    public LibreSize MinWindowTrackSize => new(112, 27);

    public LibreSize IconSize => new(32, 32);

    public LibreSize CursorSize => new(32, 32);

    public LibreSize SmallIconSize => new(16, 16);

    public LibreSize MinimumWindowSize => new(112, 27);

    public LibreSize CaptionButtonSize => new(30, 30);

    public LibreSize FrameBorderSize => new(8, 8);

    public LibreSize MaxWindowTrackSize => new(1936, 1056);

    public LibreSize PrimaryMonitorMaximizedWindowSize => new(1936, 1056);

    public LibreSize MinimizedWindowSpacingSize => new(160, 28);

    public int ToolWindowCaptionHeight => 19;

    public LibreSize ToolWindowCaptionButtonSize => new(18, 18);

    public LibreSize MenuButtonSize => new(18, 18);

    public LibreSize MinimizedWindowSize => new(160, 28);

    public int KanjiWindowHeight => 0;

    public bool DebugOperatingSystem => false;

    public bool RightAlignedMenus => false;

    public bool PenWindows => false;

    public bool DbcsEnabled => false;

    public bool Secure => false;

    public bool Network => true;

    public bool TerminalServerSession => false;

    public LibreBootMode BootMode => LibreBootMode.Normal;

    public bool ShowSounds => false;

    public LibreSize MenuCheckSize => new(13, 13);

    public bool MidEastEnabled => false;

    public LibreMinimizedWindowStartPosition MinimizedWindowStartPosition
        => LibreMinimizedWindowStartPosition.BottomLeft;

    public LibreMinimizedWindowDirection MinimizedWindowDirection
        => LibreMinimizedWindowDirection.Left;

    public bool HideMinimizedWindows => false;

    public LibreScreenOrientation ScreenOrientation => LibreScreenOrientation.Angle0;

    public int SizingBorderWidth => 1;

    public LibreSize SmallCaptionButtonSize => new(18, 18);

    public LibreSize MenuBarButtonSize => new(18, 18);

    public bool LockedTerminalSession => false;

    public int VerticalScrollBarArrowHeight => 17;

    public int HorizontalScrollBarArrowWidth => 17;

    public int VerticalScrollBarThumbHeight => 17;

    public int HorizontalScrollBarThumbWidth => 17;

    public LibreSize DragSize => new(4, 4);

    public bool MousePresent => true;

    public bool MouseButtonsSwapped => false;

    public int MouseButtons => 3;

    public LibreSize DoubleClickSize => new(4, 4);

    public int DoubleClickTime => 500;

    public bool MouseWheelPresent => true;

    public int CaretBlinkTime => 530;

    public int MouseWheelScrollLines => 3;

    public bool MenuAccessKeysUnderlined => false;

    public int KeyboardDelay => 1;

    public bool KeyboardPreferred => false;

    public int KeyboardSpeed => 31;

    public LibreSize MouseHoverSize => new(4, 4);

    public int MouseHoverTime => 400;

    public int MouseSpeed => 10;

    public bool SnapToDefaultButton => false;

    public bool DragFullWindows => true;

    public bool DropShadowEnabled => true;

    public bool FlatMenuEnabled => true;

    public bool PopupMenusLeftAligned => true;

    public bool MenuFadeEnabled => true;

    public int MenuShowDelay => 400;

    public bool ComboBoxAnimationEnabled => true;

    public bool TitleBarGradientEnabled => true;

    public bool HotTrackingEnabled => true;

    public bool ListBoxSmoothScrollingEnabled => true;

    public bool MenuAnimationEnabled => true;

    public bool SelectionFadeEnabled => true;

    public bool ToolTipAnimationEnabled => true;

    public bool UIEffectsEnabled => true;

    public bool ActiveWindowTrackingEnabled => false;

    public int ActiveWindowTrackingDelay => 500;

    public bool MinimizeRestoreAnimationEnabled => true;

    public int BorderMultiplierFactor => 1;

    public int CaretWidth => 1;

    public int VerticalFocusThickness => 1;

    public int HorizontalFocusThickness => 1;

    public int VerticalResizeBorderThickness => 8;

    public int HorizontalResizeBorderThickness => 8;

    public bool FontSmoothingEnabled => true;

    public int FontSmoothingContrast => 1400;

    public int FontSmoothingType => 2;

    public int IconHorizontalSpacing => 75;

    public int IconVerticalSpacing => 75;

    public bool IconTitleWrappingEnabled => true;
}
