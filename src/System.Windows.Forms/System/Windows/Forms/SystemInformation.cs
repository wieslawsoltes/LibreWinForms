// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Drawing;
#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;
#endif
#if !LIBREWINFORMS_PROGPU_DRAWING
using System.Drawing.Interop;
using System.Runtime.CompilerServices;
#endif
#if !LIBREWINFORMS_PORTABLE
using System.Runtime.InteropServices;
#endif
#if !LIBREWINFORMS_PORTABLE
using Windows.Win32.System.StationsAndDesktops;
using Windows.Win32.UI.Accessibility;
#endif
#if !LIBREWINFORMS_PORTABLE
using static Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX;
using static Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_ACTION;
#endif

namespace System.Windows.Forms;

/// <summary>
///  Provides information about the operating system.
/// </summary>
public static class SystemInformation
{
#if !LIBREWINFORMS_PORTABLE
    private static bool s_checkMultiMonitorSupport;
    private static bool s_multiMonitorSupport;
#endif

#if !LIBREWINFORMS_PORTABLE
    private static HWINSTA s_processWinStation;
    private static bool s_isUserInteractive;
#endif

    private static PowerStatus? s_powerStatus;

    /// <summary>
    ///  Gets a value indicating whether the user has enabled full window drag.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool DragFullWindows => PortableSystemSettings.DragFullWindows;
#else
    public static bool DragFullWindows
        => PInvokeCore.SystemParametersInfoBool(SPI_GETDRAGFULLWINDOWS);
#endif

    /// <summary>
    ///  Gets a value indicating whether the user has selected to run in high contrast.
    /// </summary>
    public static bool HighContrast
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return LibrePlatform.IsRegistered
                && LibrePlatform.Current.SystemSettings.HighContrast;
#else
            HIGHCONTRASTW data = default;
            return PInvokeCore.SystemParametersInfo(ref data)
                && data.dwFlags.HasFlag(HIGHCONTRASTW_FLAGS.HCF_HIGHCONTRASTON);
#endif
        }
    }

    /// <summary>
    ///  Gets the number of lines to scroll when the mouse wheel is rotated.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int MouseWheelScrollLines => PortableSystemSettings.MouseWheelScrollLines;
#else
    public static int MouseWheelScrollLines
        => PInvokeCore.SystemParametersInfoInt(SPI_GETWHEELSCROLLLINES);
#endif

    /// <summary>
    ///  Gets the dimensions of the primary display monitor in pixels.
    /// </summary>
    public static Size PrimaryMonitorSize
#if LIBREWINFORMS_PORTABLE
        => Screen.PrimaryScreen?.Bounds.Size ?? Size.Empty;
#else
        => GetSize(SM_CXSCREEN, SM_CYSCREEN);
#endif

    /// <summary>
    ///  Gets the width of the vertical scroll bar in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int VerticalScrollBarWidth => PortableSystemSettings.VerticalScrollBarWidth;
#else
    public static int VerticalScrollBarWidth => PInvokeCore.GetSystemMetrics(SM_CXVSCROLL);
#endif

    /// <summary>
    ///  Gets the width of the vertical scroll bar in pixels.
    /// </summary>
    public static int GetVerticalScrollBarWidthForDpi(int dpi)
#if LIBREWINFORMS_PORTABLE
        => ScaleHelper.ScaleToDpi(PortableSystemSettings.VerticalScrollBarWidth, dpi);
#else
        => ScaleHelper.IsThreadPerMonitorV2Aware
            ? PInvoke.GetCurrentSystemMetrics(SM_CXVSCROLL, (uint)dpi)
            : PInvokeCore.GetSystemMetrics(SM_CXVSCROLL);
#endif

    /// <summary>
    ///  Gets the height of the horizontal scroll bar in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int HorizontalScrollBarHeight => PortableSystemSettings.HorizontalScrollBarHeight;
#else
    public static int HorizontalScrollBarHeight => PInvokeCore.GetSystemMetrics(SM_CYHSCROLL);
#endif

    /// <summary>
    ///  Gets the height of the horizontal scroll bar in pixels.
    /// </summary>
    public static int GetHorizontalScrollBarHeightForDpi(int dpi)
#if LIBREWINFORMS_PORTABLE
        => ScaleHelper.ScaleToDpi(PortableSystemSettings.HorizontalScrollBarHeight, dpi);
#else
        => ScaleHelper.IsThreadPerMonitorV2Aware
            ? PInvoke.GetCurrentSystemMetrics(SM_CYHSCROLL, (uint)dpi)
            : PInvokeCore.GetSystemMetrics(SM_CYHSCROLL);
#endif

    /// <summary>
    ///  Gets the height of the normal caption area of a window in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int CaptionHeight => PortableSystemSettings.CaptionHeight;
#else
    public static int CaptionHeight => PInvokeCore.GetSystemMetrics(SM_CYCAPTION);
#endif

    /// <summary>
    ///  Gets the width and height of a window border in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size BorderSize => GetPortableSize(PortableSystemSettings.BorderSize);
#else
    public static Size BorderSize => GetSize(SM_CXBORDER, SM_CYBORDER);
#endif

    /// <summary>
    ///  Gets the width and height of a window border in pixels.
    /// </summary>
    public static Size GetBorderSizeForDpi(int dpi)
    {
#if LIBREWINFORMS_PORTABLE
        return ScaleHelper.ScaleToDpi(GetPortableSize(PortableSystemSettings.BorderSize), dpi);
#else
        return ScaleHelper.IsThreadPerMonitorV2Aware
            ? new(PInvoke.GetCurrentSystemMetrics(SM_CXBORDER, (uint)dpi),
                PInvoke.GetCurrentSystemMetrics(SM_CYBORDER, (uint)dpi))
            : BorderSize;
#endif
    }

    /// <summary>
    ///  Gets the thickness in pixels, of the border for a window that has a caption and is not resizable.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size FixedFrameBorderSize => GetPortableSize(PortableSystemSettings.FixedFrameBorderSize);
#else
    public static Size FixedFrameBorderSize => GetSize(SM_CXFIXEDFRAME, SM_CYFIXEDFRAME);
#endif

    /// <summary>
    ///  Gets the height of the scroll box in a vertical scroll bar in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int VerticalScrollBarThumbHeight => PortableSystemSettings.VerticalScrollBarThumbHeight;
#else
    public static int VerticalScrollBarThumbHeight => PInvokeCore.GetSystemMetrics(SM_CYVTHUMB);
#endif

    /// <summary>
    ///  Gets the width of the scroll box in a horizontal scroll bar in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int HorizontalScrollBarThumbWidth => PortableSystemSettings.HorizontalScrollBarThumbWidth;
#else
    public static int HorizontalScrollBarThumbWidth => PInvokeCore.GetSystemMetrics(SM_CXHTHUMB);
#endif

    /// <summary>
    ///  Gets the default dimensions of an icon in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size IconSize => GetPortableSize(PortableSystemSettings.IconSize);
#else
    public static Size IconSize => GetSize(SM_CXICON, SM_CYICON);
#endif

    /// <summary>
    ///  Gets the dimensions of a cursor in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size CursorSize => GetPortableSize(PortableSystemSettings.CursorSize);
#else
    public static Size CursorSize => GetSize(SM_CXCURSOR, SM_CYCURSOR);
#endif

    /// <summary>
    ///  Gets the system's font for menus.
    /// </summary>
    public static Font MenuFont => GetMenuFontHelper(0, useDpi: false);

    /// <summary>
    ///  Gets the system's font for menus, scaled accordingly to an arbitrary DPI you provide.
    /// </summary>
    public static Font GetMenuFontForDpi(int dpi)
        => GetMenuFontHelper((uint)dpi, ScaleHelper.IsThreadPerMonitorV2Aware);

    private static unsafe Font GetMenuFontHelper(uint dpi, bool useDpi)
    {
#if LIBREWINFORMS_PORTABLE
        return PortableSystemSettings.GetMenuFont(checked((int)dpi));
#elif LIBREWINFORMS_PROGPU_DRAWING
        // The native ProGPU drawing configuration cannot import a Windows LOGFONT.
        // Preserve its managed fallback while portable hosts use the typed service above.
        return Control.DefaultFont;
#else
        // We can get the system's menu font through the NONCLIENTMETRICS structure
        // via SystemParametersInfo
        NONCLIENTMETRICSW data = default;

        bool result = useDpi
            ? PInvokeCore.TrySystemParametersInfoForDpi(ref data, dpi)
            : PInvokeCore.SystemParametersInfo(ref data);

        if (result)
        {
            try
            {
                return Font.FromLogFont(Unsafe.AsRef<LOGFONT>((LOGFONT*)&data.lfMenuFont));
            }
            catch (ArgumentException)
            {
                // Font.FromLogFont throws ArgumentException when it finds
                // a font that is not TrueType. Default to standard control font.
            }
        }

        return Control.DefaultFont;
#endif
    }

    /// <summary>
    ///  Gets the height of a one line of a menu in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int MenuHeight => PortableSystemSettings.MenuHeight;
#else
    public static int MenuHeight => PInvokeCore.GetSystemMetrics(SM_CYMENU);
#endif

    /// <summary>
    ///  Returns the current system power status.
    /// </summary>
    public static PowerStatus PowerStatus => s_powerStatus ??= new PowerStatus();

    /// <summary>
    ///  Gets the size of the working area in pixels.
    /// </summary>
    public static Rectangle WorkingArea
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
#else
            RECT workingArea = default;
            PInvokeCore.SystemParametersInfo(SPI_GETWORKAREA, ref workingArea);
            return workingArea;
#endif
        }
    }

    /// <summary>
    ///  Gets the height, in pixels, of the Kanji window at the bottom of the screen
    ///  for double-byte (DBCS) character set versions of Windows.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int KanjiWindowHeight => PortableSystemSettings.KanjiWindowHeight;
#else
    public static int KanjiWindowHeight => PInvokeCore.GetSystemMetrics(SM_CYKANJIWINDOW);
#endif

    /// <summary>
    ///  Gets a value indicating whether the system has a mouse installed.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
#if LIBREWINFORMS_PORTABLE
    public static bool MousePresent => PortableSystemSettings.MousePresent;
#else
    public static bool MousePresent => PInvokeCore.GetSystemMetrics(SM_MOUSEPRESENT) != 0;
#endif

    /// <summary>
    ///  Gets the height in pixels, of the arrow bitmap on the vertical scroll bar.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int VerticalScrollBarArrowHeight => PortableSystemSettings.VerticalScrollBarArrowHeight;
#else
    public static int VerticalScrollBarArrowHeight => PInvokeCore.GetSystemMetrics(SM_CYVSCROLL);
#endif

    /// <summary>
    ///  Gets the height of the vertical scroll bar arrow bitmap in pixels.
    /// </summary>
    public static int VerticalScrollBarArrowHeightForDpi(int dpi)
#if LIBREWINFORMS_PORTABLE
        => ScaleHelper.ScaleToDpi(PortableSystemSettings.VerticalScrollBarArrowHeight, dpi);
#else
        => PInvoke.GetCurrentSystemMetrics(SM_CYVSCROLL, (uint)dpi);
#endif

    /// <summary>
    ///  Gets the width, in pixels, of the arrow bitmap on the horizontal scrollbar.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int HorizontalScrollBarArrowWidth => PortableSystemSettings.HorizontalScrollBarArrowWidth;
#else
    public static int HorizontalScrollBarArrowWidth => PInvokeCore.GetSystemMetrics(SM_CXHSCROLL);
#endif

    /// <summary>
    ///  Gets the width of the horizontal scroll bar arrow bitmap in pixels.
    /// </summary>
    public static int GetHorizontalScrollBarArrowWidthForDpi(int dpi)
#if LIBREWINFORMS_PORTABLE
        => ScaleHelper.ScaleToDpi(PortableSystemSettings.HorizontalScrollBarArrowWidth, dpi);
#else
        => ScaleHelper.IsThreadPerMonitorV2Aware
            ? PInvoke.GetCurrentSystemMetrics(SM_CXHSCROLL, (uint)dpi)
            : PInvokeCore.GetSystemMetrics(SM_CXHSCROLL);
#endif

    /// <summary>
    ///  Gets a value indicating whether this is a debug version of the operating system.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool DebugOS => PortableSystemSettings.DebugOperatingSystem;
#else
    public static bool DebugOS => PInvokeCore.GetSystemMetrics(SM_DEBUG) != 0;
#endif

    /// <summary>
    ///  Gets a value indicating whether the functions of the left and right mouse
    ///  buttons have been swapped.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool MouseButtonsSwapped => PortableSystemSettings.MouseButtonsSwapped;
#else
    public static bool MouseButtonsSwapped => PInvokeCore.GetSystemMetrics(SM_SWAPBUTTON) != 0;
#endif

    /// <summary>
    ///  Gets the minimum allowable dimensions of a window in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size MinimumWindowSize => GetPortableSize(PortableSystemSettings.MinimumWindowSize);
#else
    public static Size MinimumWindowSize => GetSize(SM_CXMIN, SM_CYMIN);
#endif

    /// <summary>
    ///  Gets the dimensions in pixels, of a caption bar or title bar button.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size CaptionButtonSize => GetPortableSize(PortableSystemSettings.CaptionButtonSize);
#else
    public static Size CaptionButtonSize => GetSize(SM_CXSIZE, SM_CYSIZE);
#endif

    /// <summary>
    ///  Gets the thickness in pixels, of the border for a window that can be resized.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size FrameBorderSize => GetPortableSize(PortableSystemSettings.FrameBorderSize);
#else
    public static Size FrameBorderSize => GetSize(SM_CXFRAME, SM_CYFRAME);
#endif

    /// <summary>
    ///  Gets the system's default minimum tracking dimensions of a window in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size MinWindowTrackSize => GetPortableSize(PortableSystemSettings.MinWindowTrackSize);
#else
    public static Size MinWindowTrackSize => GetSize(SM_CXMINTRACK, SM_CYMINTRACK);
#endif

    /// <summary>
    ///  Gets the dimensions in pixels, of the area that the user must click within
    ///  for the system to consider the two clicks a double-click. The rectangle is
    ///  centered around the first click.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size DoubleClickSize => GetPortableSize(PortableSystemSettings.DoubleClickSize);
#else
    public static Size DoubleClickSize => GetSize(SM_CXDOUBLECLK, SM_CYDOUBLECLK);
#endif

    /// <summary>
    ///  Gets the maximum number of milliseconds allowed between mouse clicks for a double-click.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int DoubleClickTime => PortableSystemSettings.DoubleClickTime;
#else
    public static int DoubleClickTime => (int)PInvoke.GetDoubleClickTime();
#endif

    /// <summary>
    ///  Gets the dimensions in pixels, of the grid used to arrange icons in a large icon view.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size IconSpacingSize
        => new(PortableSystemSettings.IconHorizontalSpacing, PortableSystemSettings.IconVerticalSpacing);
#else
    public static Size IconSpacingSize => GetSize(SM_CXICONSPACING, SM_CYICONSPACING);
#endif

    /// <summary>
    ///  Gets a value indicating whether drop down menus should be right-aligned with the corresponding menu
    ///  bar item.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool RightAlignedMenus => PortableSystemSettings.RightAlignedMenus;
#else
    public static bool RightAlignedMenus => PInvokeCore.GetSystemMetrics(SM_MENUDROPALIGNMENT) != 0;
#endif

    /// <summary>
    ///  Gets a value indicating whether the Microsoft Windows for Pen computing extensions are installed.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool PenWindows => PortableSystemSettings.PenWindows;
#else
    public static bool PenWindows => PInvokeCore.GetSystemMetrics(SM_PENWINDOWS) != 0;
#endif

    /// <summary>
    ///  Gets a value indicating whether the operating system is capable of handling
    ///  double-byte (DBCS) characters.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool DbcsEnabled => PortableSystemSettings.DbcsEnabled;
#else
    public static bool DbcsEnabled => PInvokeCore.GetSystemMetrics(SM_DBCSENABLED) != 0;
#endif

    /// <summary>
    ///  Gets the number of buttons on mouse.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int MouseButtons => PortableSystemSettings.MouseButtons;
#else
    public static int MouseButtons => PInvokeCore.GetSystemMetrics(SM_CMOUSEBUTTONS);
#endif

    /// <summary>
    ///  Gets a value indicating whether security is present on this operating system.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool Secure => PortableSystemSettings.Secure;
#else
    public static bool Secure => PInvokeCore.GetSystemMetrics(SM_SECURE) != 0;
#endif

    /// <summary>
    ///  Gets the dimensions in pixels, of a 3-D border.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size Border3DSize => GetPortableSize(PortableSystemSettings.Border3DSize);
#else
    public static Size Border3DSize => GetSize(SM_CXEDGE, SM_CYEDGE);
#endif

    /// <summary>
    ///  Gets the dimensions in pixels, of the grid into which minimized windows will be placed.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size MinimizedWindowSpacingSize
        => GetPortableSize(PortableSystemSettings.MinimizedWindowSpacingSize);
#else
    public static Size MinimizedWindowSpacingSize => GetSize(SM_CXMINSPACING, SM_CYMINSPACING);
#endif

    /// <summary>
    ///  Gets the recommended dimensions of a small icon in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size SmallIconSize => GetPortableSize(PortableSystemSettings.SmallIconSize);
#else
    public static Size SmallIconSize => GetSize(SM_CXSMICON, SM_CYSMICON);
#endif

    /// <summary>
    ///  Gets the height of a small caption in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int ToolWindowCaptionHeight => PortableSystemSettings.ToolWindowCaptionHeight;
#else
    public static int ToolWindowCaptionHeight => PInvokeCore.GetSystemMetrics(SM_CYSMCAPTION);
#endif

    /// <summary>
    ///  Gets the dimensions of small caption buttons in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size ToolWindowCaptionButtonSize
        => GetPortableSize(PortableSystemSettings.ToolWindowCaptionButtonSize);
#else
    public static Size ToolWindowCaptionButtonSize => GetSize(SM_CXSMSIZE, SM_CYSMSIZE);
#endif

    /// <summary>
    ///  Gets the dimensions in pixels, of menu bar buttons.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size MenuButtonSize => GetPortableSize(PortableSystemSettings.MenuButtonSize);
#else
    public static Size MenuButtonSize => GetSize(SM_CXMENUSIZE, SM_CYMENUSIZE);
#endif

    /// <summary>
    ///  Gets flags specifying how the system arranges minimized windows.
    /// </summary>
    public static ArrangeStartingPosition ArrangeStartingPosition
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            ArrangeStartingPosition position = PortableSystemSettings.MinimizedWindowStartPosition switch
            {
                LibreMinimizedWindowStartPosition.BottomRight => ArrangeStartingPosition.BottomRight,
                LibreMinimizedWindowStartPosition.TopLeft => ArrangeStartingPosition.TopLeft,
                LibreMinimizedWindowStartPosition.TopRight => ArrangeStartingPosition.TopRight,
                _ => ArrangeStartingPosition.BottomLeft,
            };

            return PortableSystemSettings.HideMinimizedWindows
                ? position | ArrangeStartingPosition.Hide
                : position;
#else
            ArrangeStartingPosition mask = ArrangeStartingPosition.BottomLeft
                | ArrangeStartingPosition.BottomRight
                | ArrangeStartingPosition.Hide
                | ArrangeStartingPosition.TopLeft
                | ArrangeStartingPosition.TopRight;
            int compoundValue = PInvokeCore.GetSystemMetrics(SM_ARRANGE);
            return mask & (ArrangeStartingPosition)compoundValue;
#endif
        }
    }

    /// <summary>
    ///  Gets flags specifying how the system arranges minimized windows.
    /// </summary>
    public static ArrangeDirection ArrangeDirection
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return PortableSystemSettings.MinimizedWindowDirection switch
            {
                LibreMinimizedWindowDirection.Right => ArrangeDirection.Right,
                LibreMinimizedWindowDirection.Up => ArrangeDirection.Up,
                LibreMinimizedWindowDirection.Down => ArrangeDirection.Down,
                _ => ArrangeDirection.Left,
            };
#else
            ArrangeDirection mask = ArrangeDirection.Down
                | ArrangeDirection.Left | ArrangeDirection.Right | ArrangeDirection.Up;
            int compoundValue = PInvokeCore.GetSystemMetrics(SM_ARRANGE);
            return mask & (ArrangeDirection)compoundValue;
#endif
        }
    }

    /// <summary>
    ///  Gets the dimensions in pixels, of a normal minimized window.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size MinimizedWindowSize => GetPortableSize(PortableSystemSettings.MinimizedWindowSize);
#else
    public static Size MinimizedWindowSize => GetSize(SM_CXMINIMIZED, SM_CYMINIMIZED);
#endif

    /// <summary>
    ///  Gets the default maximum dimensions in pixels, of a window that has a
    ///  caption and sizing borders.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size MaxWindowTrackSize => GetPortableSize(PortableSystemSettings.MaxWindowTrackSize);
#else
    public static Size MaxWindowTrackSize => GetSize(SM_CXMAXTRACK, SM_CYMAXTRACK);
#endif

    /// <summary>
    ///  Gets the default dimensions, in pixels, of a maximized top-left window on the
    ///  primary monitor.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size PrimaryMonitorMaximizedWindowSize
        => GetPortableSize(PortableSystemSettings.PrimaryMonitorMaximizedWindowSize);
#else
    public static Size PrimaryMonitorMaximizedWindowSize => GetSize(SM_CXMAXIMIZED, SM_CYMAXIMIZED);
#endif

    /// <summary>
    ///  Gets a value indicating whether this computer is connected to a network.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool Network => PortableSystemSettings.Network;

    public static bool TerminalServerSession => PortableSystemSettings.TerminalServerSession;
#else
    public static bool Network => (PInvokeCore.GetSystemMetrics(SM_NETWORK) & 0x00000001) != 0;

    public static bool TerminalServerSession => (PInvokeCore.GetSystemMetrics(SM_REMOTESESSION) & 0x00000001) != 0;
#endif

    /// <summary>
    ///  Gets a value that specifies how the system was started.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static BootMode BootMode => PortableSystemSettings.BootMode switch
    {
        LibreBootMode.FailSafe => BootMode.FailSafe,
        LibreBootMode.FailSafeWithNetwork => BootMode.FailSafeWithNetwork,
        _ => BootMode.Normal,
    };
#else
    public static BootMode BootMode => (BootMode)PInvokeCore.GetSystemMetrics(SM_CLEANBOOT);
#endif

    /// <summary>
    ///  Gets the dimensions in pixels, of the rectangle that a drag operation must
    ///  extend to be considered a drag. The rectangle is centered on a drag point.
    /// </summary>
    public static Size DragSize
#if LIBREWINFORMS_PORTABLE
        => GetPortableSize(PortableSystemSettings.DragSize);
#else
        => GetSize(SM_CXDRAG, SM_CYDRAG);
#endif

    /// <summary>
    ///  Gets a value indicating whether the user requires an application to present
    ///  information visually in situations where it would otherwise present the
    ///  information in audible form.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool ShowSounds => PortableSystemSettings.ShowSounds;
#else
    public static bool ShowSounds => PInvokeCore.GetSystemMetrics(SM_SHOWSOUNDS) != 0;
#endif

    /// <summary>
    ///  Gets the dimensions of the default size of a menu checkmark in pixels.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size MenuCheckSize => GetPortableSize(PortableSystemSettings.MenuCheckSize);
#else
    public static Size MenuCheckSize => GetSize(SM_CXMENUCHECK, SM_CYMENUCHECK);
#endif

    /// <summary>
    ///  Gets a value indicating whether the system is enabled for Hebrew and Arabic languages.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool MidEastEnabled => PortableSystemSettings.MidEastEnabled;
#else
    public static bool MidEastEnabled => PInvokeCore.GetSystemMetrics(SM_MIDEASTENABLED) != 0;
#endif

    internal static bool MultiMonitorSupport
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return LibrePlatform.Current.Monitors.GetMonitors().Count > 1;
#else
            if (!s_checkMultiMonitorSupport)
            {
                s_multiMonitorSupport = PInvokeCore.GetSystemMetrics(SM_CMONITORS) != 0;
                s_checkMultiMonitorSupport = true;
            }

            return s_multiMonitorSupport;
#endif
        }
    }

    /// <summary>
    ///  Gets a value indicating whether a mouse with a mouse wheel is installed.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   This was never really correct. All versions of Windows NT from 4.0 onward supported the mouse wheel
    ///   directly. This should have been a version check. Rather than change it and risk breaking apps we'll
    ///   keep it equivalent to <see cref="MouseWheelPresent"/>.
    ///  </para>
    /// </remarks>
#if LIBREWINFORMS_PORTABLE
    public static bool NativeMouseWheelSupport => PortableSystemSettings.MouseWheelPresent;
#else
    public static bool NativeMouseWheelSupport => PInvokeCore.GetSystemMetrics(SM_MOUSEWHEELPRESENT) != 0;
#endif

    /// <summary>
    ///  Gets a value indicating whether a mouse with a mouse wheel is installed.
    /// </summary>
    public static bool MouseWheelPresent => NativeMouseWheelSupport;

    /// <summary>
    ///  Gets the bounds of the virtual screen.
    /// </summary>
    public static Rectangle VirtualScreen
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            IReadOnlyList<LibreMonitor> monitors = LibrePlatform.Current.Monitors.GetMonitors();
            if (monitors.Count == 0)
            {
                return Rectangle.Empty;
            }

            long left = monitors[0].Bounds.X;
            long top = monitors[0].Bounds.Y;
            long right = (long)monitors[0].Bounds.X + monitors[0].Bounds.Width;
            long bottom = (long)monitors[0].Bounds.Y + monitors[0].Bounds.Height;
            for (int index = 1; index < monitors.Count; index++)
            {
                LibreRectangle bounds = monitors[index].Bounds;
                left = Math.Min(left, bounds.X);
                top = Math.Min(top, bounds.Y);
                right = Math.Max(right, (long)bounds.X + bounds.Width);
                bottom = Math.Max(bottom, (long)bounds.Y + bounds.Height);
            }

            return new Rectangle(
                checked((int)left),
                checked((int)top),
                checked((int)(right - left)),
                checked((int)(bottom - top)));
#else
            if (MultiMonitorSupport)
            {
                return new(PInvokeCore.GetSystemMetrics(SM_XVIRTUALSCREEN),
                    PInvokeCore.GetSystemMetrics(SM_YVIRTUALSCREEN),
                    PInvokeCore.GetSystemMetrics(SM_CXVIRTUALSCREEN),
                    PInvokeCore.GetSystemMetrics(SM_CYVIRTUALSCREEN));
            }

            Size size = PrimaryMonitorSize;
            return new Rectangle(0, 0, size.Width, size.Height);
#endif
        }
    }

    /// <summary>
    ///  Gets the number of display monitors on the desktop.
    /// </summary>
    public static int MonitorCount
#if LIBREWINFORMS_PORTABLE
        => LibrePlatform.Current.Monitors.GetMonitors().Count;
#else
        => MultiMonitorSupport ? PInvokeCore.GetSystemMetrics(SM_CMONITORS) : 1;
#endif

    /// <summary>
    ///  Gets a value indicating whether all the display monitors have the same color format.
    /// </summary>
    public static bool MonitorsSameDisplayFormat
#if LIBREWINFORMS_PORTABLE
    {
        get
        {
            IReadOnlyList<LibreMonitor> monitors = LibrePlatform.Current.Monitors.GetMonitors();
            if (monitors.Count < 2)
            {
                return true;
            }

            int bitsPerPixel = monitors[0].BitsPerPixel;
            for (int index = 1; index < monitors.Count; index++)
            {
                if (monitors[index].BitsPerPixel != bitsPerPixel)
                {
                    return false;
                }
            }

            return true;
        }
    }
#else
        => !MultiMonitorSupport || PInvokeCore.GetSystemMetrics(SM_SAMEDISPLAYFORMAT) != 0;
#endif

    /// <summary>
    ///  Gets the computer name of the current system.
    /// </summary>
    public static string ComputerName => Environment.MachineName;

    /// <summary>
    ///  Gets the user's domain name.
    /// </summary>
    public static string UserDomainName => Environment.UserDomainName;

    /// <summary>
    ///  Gets a value indicating whether the current process is running in user interactive mode.
    /// </summary>
    public static unsafe bool UserInteractive
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return Environment.UserInteractive;
#else
            HWINSTA hwinsta = PInvoke.GetProcessWindowStation();
            if (!hwinsta.IsNull && s_processWinStation != hwinsta)
            {
                s_isUserInteractive = true;

                USEROBJECTFLAGS flags = default;
                if (PInvoke.GetUserObjectInformation(
                    (HANDLE)hwinsta.Value,
                    USER_OBJECT_INFORMATION_INDEX.UOI_FLAGS,
                    &flags,
                    (uint)sizeof(USEROBJECTFLAGS),
                    lpnLengthNeeded: null))
                {
                    if ((flags.dwFlags & PInvoke.WSF_VISIBLE) == 0)
                    {
                        s_isUserInteractive = false;
                    }
                }

                s_processWinStation = hwinsta;
            }

            return s_isUserInteractive;
#endif
        }
    }

    /// <summary>
    ///  Gets the user name for the current thread, that is, the name of the user currently logged onto
    ///  the system.
    /// </summary>
    public static string UserName => Environment.UserName;

    /// <summary>
    ///  Gets whether the drop shadow effect in enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsDropShadowEnabled => PortableSystemSettings.DropShadowEnabled;
#else
    public static bool IsDropShadowEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETDROPSHADOW);
#endif

    /// <summary>
    ///  Gets whether the native user menus have a flat menu appearance.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsFlatMenuEnabled => PortableSystemSettings.FlatMenuEnabled;
#else
    public static bool IsFlatMenuEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETFLATMENU);
#endif

    /// <summary>
    ///  Gets whether font smoothing is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsFontSmoothingEnabled => PortableSystemSettings.FontSmoothingEnabled;
#else
    public static bool IsFontSmoothingEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETFONTSMOOTHING);
#endif

    /// <summary>
    ///  Returns the ClearType smoothing contrast value.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int FontSmoothingContrast => PortableSystemSettings.FontSmoothingContrast;
#else
    public static int FontSmoothingContrast => PInvokeCore.SystemParametersInfoInt(SPI_GETFONTSMOOTHINGCONTRAST);
#endif

    /// <summary>
    ///  Returns a type of Font smoothing.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int FontSmoothingType => PortableSystemSettings.FontSmoothingType;
#else
    public static int FontSmoothingType => PInvokeCore.SystemParametersInfoInt(SPI_GETFONTSMOOTHINGTYPE);
#endif

    /// <summary>
    ///  Retrieves the width in pixels of an icon cell.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int IconHorizontalSpacing => PortableSystemSettings.IconHorizontalSpacing;
#else
    public static int IconHorizontalSpacing => PInvokeCore.SystemParametersInfoInt(SPI_ICONHORIZONTALSPACING);
#endif

    /// <summary>
    ///  Retrieves the height in pixels of an icon cell.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int IconVerticalSpacing => PortableSystemSettings.IconVerticalSpacing;
#else
    public static int IconVerticalSpacing => PInvokeCore.SystemParametersInfoInt(SPI_ICONVERTICALSPACING);
#endif

    /// <summary>
    ///  Gets whether icon title wrapping is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsIconTitleWrappingEnabled => PortableSystemSettings.IconTitleWrappingEnabled;
#else
    public static bool IsIconTitleWrappingEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETICONTITLEWRAP);
#endif

    /// <summary>
    ///  Gets whether menu access keys are underlined.
    /// </summary>
    public static bool MenuAccessKeysUnderlined
#if LIBREWINFORMS_PORTABLE
        => PortableSystemSettings.MenuAccessKeysUnderlined;
#else
        => PInvokeCore.SystemParametersInfoBool(SPI_GETKEYBOARDCUES);
#endif

    /// <summary>
    ///  Retrieves the Keyboard repeat delay setting, which is a value in the range
    ///  from 0 through 3. The actual delay associated with each value may vary
    ///  depending on the hardware.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int KeyboardDelay => PortableSystemSettings.KeyboardDelay;
#else
    public static int KeyboardDelay => PInvokeCore.SystemParametersInfoInt(SPI_GETKEYBOARDDELAY);
#endif

    /// <summary>
    ///  Gets whether the user relies on keyboard instead of mouse and wants
    ///  applications to display keyboard interfaces that would be otherwise hidden.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsKeyboardPreferred => PortableSystemSettings.KeyboardPreferred;
#else
    public static bool IsKeyboardPreferred => PInvokeCore.SystemParametersInfoBool(SPI_GETKEYBOARDPREF);
#endif

    /// <summary>
    ///  Retrieves the Keyboard repeat speed setting, which is a value in the range
    ///  from 0 through 31. The actual rate may vary depending on the hardware.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int KeyboardSpeed => PortableSystemSettings.KeyboardSpeed;
#else
    public static int KeyboardSpeed => PInvokeCore.SystemParametersInfoInt(SPI_GETKEYBOARDSPEED);
#endif

    /// <summary>
    ///  Gets the <see cref="Size"/> in pixels of the rectangle within which the mouse
    ///  pointer has to stay to be considered hovering.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static Size MouseHoverSize => GetPortableSize(PortableSystemSettings.MouseHoverSize);
#else
    public static Size MouseHoverSize
        => new(PInvokeCore.SystemParametersInfoInt(SPI_GETMOUSEHOVERWIDTH),
            PInvokeCore.SystemParametersInfoInt(SPI_GETMOUSEHOVERHEIGHT));
#endif

    /// <summary>
    ///  Gets the time, in milliseconds, that the mouse pointer has to stay in the hover
    ///  rectangle to be considered hovering.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int MouseHoverTime => PortableSystemSettings.MouseHoverTime;
#else
    public static int MouseHoverTime => PInvokeCore.SystemParametersInfoInt(SPI_GETMOUSEHOVERTIME);
#endif

    /// <summary>
    ///  Gets the current mouse speed.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int MouseSpeed => PortableSystemSettings.MouseSpeed;
#else
    public static int MouseSpeed => PInvokeCore.SystemParametersInfoInt(SPI_GETMOUSESPEED);
#endif

    /// <summary>
    ///  Determines whether the snap-to-default-button feature is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsSnapToDefaultEnabled => PortableSystemSettings.SnapToDefaultButton;
#else
    public static bool IsSnapToDefaultEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETSNAPTODEFBUTTON);
#endif

    /// <summary>
    ///  Determines whether the popup menus are left aligned or right aligned.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static LeftRightAlignment PopupMenuAlignment
        => PortableSystemSettings.PopupMenusLeftAligned
            ? LeftRightAlignment.Left : LeftRightAlignment.Right;
#else
    public static LeftRightAlignment PopupMenuAlignment
        => PInvokeCore.SystemParametersInfoBool(SPI_GETMENUDROPALIGNMENT)
            ? LeftRightAlignment.Left : LeftRightAlignment.Right;
#endif

    /// <summary>
    ///  Determines whether the menu fade animation feature is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsMenuFadeEnabled => PortableSystemSettings.MenuFadeEnabled;
#else
    public static bool IsMenuFadeEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETMENUFADE);
#endif

    /// <summary>
    ///  Indicates the time, in milliseconds, that the system waits before displaying
    ///  a shortcut menu.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int MenuShowDelay => PortableSystemSettings.MenuShowDelay;
#else
    public static int MenuShowDelay => PInvokeCore.SystemParametersInfoInt(SPI_GETMENUSHOWDELAY);
#endif

    /// <summary>
    ///  Indicates whether the slide open effect for combo boxes is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsComboBoxAnimationEnabled => PortableSystemSettings.ComboBoxAnimationEnabled;
#else
    public static bool IsComboBoxAnimationEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETCOMBOBOXANIMATION);
#endif

    /// <summary>
    ///  Indicates whether the gradient effect for windows title bars is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsTitleBarGradientEnabled => PortableSystemSettings.TitleBarGradientEnabled;
#else
    public static bool IsTitleBarGradientEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETGRADIENTCAPTIONS);
#endif

    /// <summary>
    ///  Indicates whether the hot tracking of user interface elements is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsHotTrackingEnabled => PortableSystemSettings.HotTrackingEnabled;
#else
    public static bool IsHotTrackingEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETHOTTRACKING);
#endif

    /// <summary>
    ///  Indicates whether the smooth scrolling effect for listbox is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsListBoxSmoothScrollingEnabled => PortableSystemSettings.ListBoxSmoothScrollingEnabled;
#else
    public static bool IsListBoxSmoothScrollingEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETLISTBOXSMOOTHSCROLLING);
#endif

    /// <summary>
    ///  Indicates whether the menu animation feature is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsMenuAnimationEnabled => PortableSystemSettings.MenuAnimationEnabled;
#else
    public static bool IsMenuAnimationEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETMENUANIMATION);
#endif

    /// <summary>
    ///  Indicates whether the selection fade effect is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsSelectionFadeEnabled => PortableSystemSettings.SelectionFadeEnabled;
#else
    public static bool IsSelectionFadeEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETSELECTIONFADE);
#endif

    /// <summary>
    ///  Indicates whether tool tip animation is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsToolTipAnimationEnabled => PortableSystemSettings.ToolTipAnimationEnabled;
#else
    public static bool IsToolTipAnimationEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETTOOLTIPANIMATION);
#endif

    /// <summary>
    ///  Indicates whether UI effects are enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool UIEffectsEnabled => PortableSystemSettings.UIEffectsEnabled;
#else
    public static bool UIEffectsEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETUIEFFECTS);
#endif

    /// <summary>
    ///  Indicates whether the windows tracking (activating the window the mouse in on) is ON or OFF.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsActiveWindowTrackingEnabled => PortableSystemSettings.ActiveWindowTrackingEnabled;
#else
    public static bool IsActiveWindowTrackingEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETACTIVEWINDOWTRACKING);
#endif

    /// <summary>
    ///  Retrieves the active window tracking delay in milliseconds.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int ActiveWindowTrackingDelay => PortableSystemSettings.ActiveWindowTrackingDelay;
#else
    public static int ActiveWindowTrackingDelay => PInvokeCore.SystemParametersInfoInt(SPI_GETACTIVEWNDTRKTIMEOUT);
#endif

    /// <summary>
    ///  Indicates whether windows minimize/restore animation is enabled.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static bool IsMinimizeRestoreAnimationEnabled
        => PortableSystemSettings.MinimizeRestoreAnimationEnabled;
#else
    public static bool IsMinimizeRestoreAnimationEnabled => PInvokeCore.SystemParametersInfoBool(SPI_GETANIMATION);
#endif

    /// <summary>
    ///  Retrieves the border multiplier factor that determines the width of a window's sizing border.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int BorderMultiplierFactor => PortableSystemSettings.BorderMultiplierFactor;
#else
    public static int BorderMultiplierFactor => PInvokeCore.SystemParametersInfoInt(SPI_GETBORDER);
#endif

    /// <summary>
    ///  Indicates the caret blink time.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int CaretBlinkTime => PortableSystemSettings.CaretBlinkTime;
#else
    public static int CaretBlinkTime => (int)PInvoke.GetCaretBlinkTime();
#endif

    /// <summary>
    ///  Indicates the caret width in edit controls.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int CaretWidth => PortableSystemSettings.CaretWidth;
#else
    public static int CaretWidth => PInvokeCore.SystemParametersInfoInt(SPI_GETCARETWIDTH);
#endif

    public static int MouseWheelScrollDelta => (int)PInvoke.WHEEL_DELTA;

    /// <summary>
    ///  The width of the left and right edges of the focus rectangle.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int VerticalFocusThickness => PortableSystemSettings.VerticalFocusThickness;
#else
    public static int VerticalFocusThickness => PInvokeCore.GetSystemMetrics(SM_CYFOCUSBORDER);
#endif

    /// <summary>
    ///  The width of the top and bottom edges of the focus rectangle.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int HorizontalFocusThickness => PortableSystemSettings.HorizontalFocusThickness;
#else
    public static int HorizontalFocusThickness => PInvokeCore.GetSystemMetrics(SM_CXFOCUSBORDER);
#endif

    /// <summary>
    ///  The height of the vertical sizing border around the perimeter of the window that can be resized.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int VerticalResizeBorderThickness => PortableSystemSettings.VerticalResizeBorderThickness;
#else
    public static int VerticalResizeBorderThickness => PInvokeCore.GetSystemMetrics(SM_CYSIZEFRAME);
#endif

    /// <summary>
    ///  The width of the horizontal sizing border around the perimeter of the window that can be resized.
    /// </summary>
#if LIBREWINFORMS_PORTABLE
    public static int HorizontalResizeBorderThickness => PortableSystemSettings.HorizontalResizeBorderThickness;
#else
    public static int HorizontalResizeBorderThickness => PInvokeCore.GetSystemMetrics(SM_CXSIZEFRAME);
#endif

    /// <summary>
    ///  The orientation of the screen in degrees.
    /// </summary>
    public static unsafe ScreenOrientation ScreenOrientation
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return PortableSystemSettings.ScreenOrientation switch
            {
                LibreScreenOrientation.Angle90 => ScreenOrientation.Angle90,
                LibreScreenOrientation.Angle180 => ScreenOrientation.Angle180,
                LibreScreenOrientation.Angle270 => ScreenOrientation.Angle270,
                _ => ScreenOrientation.Angle0,
            };
#else
            ScreenOrientation so = ScreenOrientation.Angle0;
            DEVMODEW dm = new()
            {
                dmSize = (ushort)sizeof(DEVMODEW),
            };

            PInvoke.EnumDisplaySettings(lpszDeviceName: null, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref dm);
            if ((dm.dmFields & DEVMODE_FIELD_FLAGS.DM_DISPLAYORIENTATION) > 0)
            {
                so = (ScreenOrientation)dm.Anonymous1.Anonymous2.dmDisplayOrientation;
            }

            return so;
#endif
        }
    }

    /// <summary>
    ///  The thickness, in pixels, of the sizing border.
    /// </summary>
    public static int SizingBorderWidth
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return PortableSystemSettings.SizingBorderWidth;
#else
            NONCLIENTMETRICSW data = default;
            return PInvokeCore.SystemParametersInfo(ref data)
                && data.iBorderWidth > 0 ? data.iBorderWidth : 0;
#endif
        }
    }

    /// <summary>
    ///  The <see cref="Size"/>, in pixels, of the small caption buttons.
    /// </summary>
    public static unsafe Size SmallCaptionButtonSize
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return GetPortableSize(PortableSystemSettings.SmallCaptionButtonSize);
#else
            NONCLIENTMETRICSW data = default;
            return PInvokeCore.SystemParametersInfo(ref data)
                && data.iSmCaptionHeight > 0 && data.iSmCaptionWidth > 0
                    ? new Size(data.iSmCaptionWidth, data.iSmCaptionHeight)
                    : Size.Empty;
#endif
        }
    }

    /// <summary>
    ///  The <see cref="Size"/>, in pixels, of the menu bar buttons.
    /// </summary>
    public static unsafe Size MenuBarButtonSize
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return GetPortableSize(PortableSystemSettings.MenuBarButtonSize);
#else
            NONCLIENTMETRICSW data = default;
            return PInvokeCore.SystemParametersInfo(ref data)
                && data.iMenuHeight > 0 && data.iMenuWidth > 0
                    ? new Size(data.iMenuWidth, data.iMenuHeight)
                    : Size.Empty;
#endif
        }
    }

    /// <summary>
    ///  Checks whether the current WinForms app is running on a secure desktop under a terminal
    ///  server session. This is the case when the TS session has been locked.
    ///  This method is useful when calling into GDI+ Graphics methods that modify the object's
    ///  state, these methods fail under a locked terminal session.
    /// </summary>
    internal static bool InLockedTerminalSession()
    {
#if LIBREWINFORMS_PORTABLE
        return TerminalServerSession && PortableSystemSettings.LockedTerminalSession;
#else
        if (TerminalServerSession)
        {
            // Try to open the input desktop. If it fails with access denied assume
            // the app is running on a secure desktop.
            HDESK desktop = PInvoke.OpenInputDesktop(0, false, DESKTOP_ACCESS_FLAGS.DESKTOP_SWITCHDESKTOP);
            if (desktop.IsNull)
            {
                return Marshal.GetLastWin32Error() == (int)WIN32_ERROR.ERROR_ACCESS_DENIED;
            }

            PInvoke.CloseDesktop(desktop);
        }

        return false;
#endif
    }

#if LIBREWINFORMS_PORTABLE
    private static ILibreSystemSettingsService PortableSystemSettings
        => LibrePlatform.IsRegistered
            ? LibrePlatform.Current.SystemSettings
            : DefaultLibreSystemSettingsService.Instance;

    private static Size GetPortableSize(LibreSize size) => new(size.Width, size.Height);
#endif

#if !LIBREWINFORMS_PORTABLE
    private static Size GetSize(SYSTEM_METRICS_INDEX x, SYSTEM_METRICS_INDEX y)
        => new(PInvokeCore.GetSystemMetrics(x), PInvokeCore.GetSystemMetrics(y));
#endif
}
