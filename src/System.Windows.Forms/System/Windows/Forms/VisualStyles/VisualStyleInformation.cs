// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;
#endif

namespace System.Windows.Forms.VisualStyles;

/// <summary>
///  Provides information about the current visual style.
///
///  NOTE:
///
///  1) These properties (except SupportByOS, which is always meaningful) are meaningful only
///  if visual styles are supported and have currently been applied by the user.
///  2) A subset of these use VisualStyleRenderer objects, so they are
///  not meaningful unless VisualStyleRenderer.IsSupported is true.
/// </summary>
public static class VisualStyleInformation
{
    // Make this per-thread, so that different threads can safely use these methods.
    [ThreadStatic]
    private static VisualStyleRenderer? t_visualStyleRenderer;

    /// <summary>
    ///  Used to find whether visual styles are supported by the current OS. Same as
    ///  using the OSFeature class to see if themes are supported.
    ///  This is always supported on platforms that .NET Core supports.
    /// </summary>
    public static bool IsSupportedByOS => true;

    /// <summary>
    ///  Returns true if a visual style has currently been applied by the user, else false.
    /// </summary>
    public static bool IsEnabledByUser
#if LIBREWINFORMS_PORTABLE
        => LibrePlatform.IsRegistered && LibrePlatform.Current.VisualStyles.IsEnabled;
#else
        => PInvoke.IsAppThemed();
#endif

    internal static unsafe string ThemeFilename
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return LibrePlatform.IsRegistered
                ? LibrePlatform.Current.VisualStyles.ThemeFilename
                : string.Empty;
#else
            if (IsEnabledByUser)
            {
                Span<char> filename = stackalloc char[512];
                fixed (char* pFilename = filename)
                {
                    PInvoke.GetCurrentThemeName(pFilename, filename.Length, null, 0, null, 0);
                }

                return filename.SliceAtFirstNull().ToString();
            }

            return string.Empty;
#endif
        }
    }

    /// <summary>
    ///  The current visual style's color scheme name.
    /// </summary>
    public static unsafe string ColorScheme
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return LibrePlatform.IsRegistered
                ? LibrePlatform.Current.VisualStyles.ColorScheme
                : string.Empty;
#else
            if (IsEnabledByUser)
            {
                Span<char> colorScheme = stackalloc char[512];
                fixed (char* pColorScheme = colorScheme)
                {
                    PInvoke.GetCurrentThemeName(null, 0, pColorScheme, colorScheme.Length, null, 0);
                }

                return colorScheme.SliceAtFirstNull().ToString();
            }

            return string.Empty;
#endif
        }
    }

    /// <summary>
    ///  The current visual style's size name.
    /// </summary>
    public static unsafe string Size
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return LibrePlatform.IsRegistered
                ? LibrePlatform.Current.VisualStyles.ThemeSize
                : string.Empty;
#else
            if (IsEnabledByUser)
            {
                Span<char> size = stackalloc char[512];
                fixed (char* pSize = size)
                {
                    PInvoke.GetCurrentThemeName(null, 0, null, 0, pSize, size.Length);
                }

                return size.SliceAtFirstNull().ToString();
            }

            return string.Empty;
#endif
        }
    }

    /// <summary>
    ///  The current visual style's display name.
    /// </summary>
    public static unsafe string DisplayName
#if LIBREWINFORMS_PORTABLE
        => LibrePlatform.IsRegistered ? LibrePlatform.Current.VisualStyles.DisplayName : string.Empty;
#else
        => IsEnabledByUser
        ? PInvoke.GetThemeDocumentationProperty(ThemeFilename, "DisplayName")
        : string.Empty;
#endif

    /// <summary>
    ///  The current visual style's company.
    /// </summary>
    public static string Company
#if LIBREWINFORMS_PORTABLE
        => LibrePlatform.IsRegistered ? LibrePlatform.Current.VisualStyles.Company : string.Empty;
#else
        => IsEnabledByUser
        ? PInvoke.GetThemeDocumentationProperty(ThemeFilename, "Company")
        : string.Empty;
#endif

    /// <summary>
    ///  The name of the current visual style's author.
    /// </summary>
    public static string Author
#if LIBREWINFORMS_PORTABLE
        => LibrePlatform.IsRegistered ? LibrePlatform.Current.VisualStyles.Author : string.Empty;
#else
        => IsEnabledByUser
        ? PInvoke.GetThemeDocumentationProperty(ThemeFilename, "Author")
        : string.Empty;
#endif

    /// <summary>
    ///  The current visual style's copyright information.
    /// </summary>
    public static string Copyright
#if LIBREWINFORMS_PORTABLE
        => LibrePlatform.IsRegistered ? LibrePlatform.Current.VisualStyles.Copyright : string.Empty;
#else
        => IsEnabledByUser
        ? PInvoke.GetThemeDocumentationProperty(ThemeFilename, "Copyright")
        : string.Empty;
#endif

    /// <summary>
    ///  The current visual style's url.
    /// </summary>
    public static string Url
#if LIBREWINFORMS_PORTABLE
        => LibrePlatform.IsRegistered ? LibrePlatform.Current.VisualStyles.Url : string.Empty;
#else
        => IsEnabledByUser
        ? PInvoke.GetThemeDocumentationProperty(ThemeFilename, "Url")
        : string.Empty;
#endif

    /// <summary>
    ///  The current visual style's version.
    /// </summary>
    public static string Version
#if LIBREWINFORMS_PORTABLE
        => LibrePlatform.IsRegistered ? LibrePlatform.Current.VisualStyles.Version : string.Empty;
#else
        => IsEnabledByUser
        ? PInvoke.GetThemeDocumentationProperty(ThemeFilename, "Version")
        : string.Empty;
#endif

    /// <summary>
    ///  The current visual style's description.
    /// </summary>
    public static string Description
#if LIBREWINFORMS_PORTABLE
        => LibrePlatform.IsRegistered ? LibrePlatform.Current.VisualStyles.Description : string.Empty;
#else
        => IsEnabledByUser
        ? PInvoke.GetThemeDocumentationProperty(ThemeFilename, "Description")
        : string.Empty;
#endif

    /// <summary>
    ///  Returns true if the current theme supports flat menus, else false.
    /// </summary>
    public static bool SupportsFlatMenus
#if LIBREWINFORMS_PORTABLE
        => Application.RenderWithVisualStyles
        && LibrePlatform.IsRegistered
        && LibrePlatform.Current.VisualStyles.SupportsFlatMenus;
#else
        =>
        Application.RenderWithVisualStyles
        && PInvoke.GetThemeSysBool(
            SetParameters(VisualStyleElement.Window.Caption.Active).HTHEME,
            THEME_PROPERTY_SYMBOL_ID.TMT_FLATMENUS);
#endif

    /// <summary>
    ///  The minimum color depth supported by the current visual style.
    /// </summary>
    public static int MinimumColorDepth
    {
        get
        {
            if (!Application.UseVisualStyles)
            {
                return 0;
            }

#if LIBREWINFORMS_PORTABLE
            return LibrePlatform.IsRegistered
                ? LibrePlatform.Current.VisualStyles.MinimumColorDepth
                : 0;
#else
            PInvoke.GetThemeSysInt(
                SetParameters(VisualStyleElement.Window.Caption.Active).HTHEME,
                THEME_PROPERTY_SYMBOL_ID.TMT_MINCOLORDEPTH,
                out int depth);

            return depth;
#endif
        }
    }

    /// <summary>
    ///  Border Color that Windows renders for controls like TextBox and ComboBox.
    /// </summary>
    public static Color TextControlBorder => Application.RenderWithVisualStyles
        ? SetParameters(VisualStyleElement.TextBox.TextEdit.Normal).GetColor(ColorProperty.BorderColor)
        : SystemColors.WindowFrame;

    /// <summary>
    ///  This is the color buttons and tab pages are highlighted with when they are moused over on themed OS.
    /// </summary>
    public static Color ControlHighlightHot => Application.RenderWithVisualStyles
        ? SetParameters(VisualStyleElement.Button.PushButton.Normal).GetColor(ColorProperty.AccentColorHint)
        : SystemColors.ButtonHighlight;

    private static VisualStyleRenderer SetParameters(VisualStyleElement element)
    {
        if (t_visualStyleRenderer is null)
        {
            t_visualStyleRenderer = new VisualStyleRenderer(element);
        }
        else
        {
            t_visualStyleRenderer.SetParameters(element);
        }

        return t_visualStyleRenderer;
    }
}
