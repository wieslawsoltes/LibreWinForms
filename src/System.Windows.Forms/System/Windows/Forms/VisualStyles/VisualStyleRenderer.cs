// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
#if !LIBREWINFORMS_PROGPU_DRAWING
using System.Drawing.Interop;
#endif
#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;
#endif
#if !LIBREWINFORMS_PORTABLE
using Microsoft.Win32;
#endif

namespace System.Windows.Forms.VisualStyles;

/// <summary>
///  This class provides full feature parity with UxTheme API.
/// </summary>
public sealed class VisualStyleRenderer : IHandle<HTHEME>
{
    private HRESULT _lastHResult;
#if !LIBREWINFORMS_PORTABLE
    private const int NumberOfPossibleClasses = VisualStyleElement.Count; // used as size for themeHandles

    [ThreadStatic]
    private static Dictionary<string, ThemeHandle>? t_themeHandles; // per-thread cache of ThemeHandle objects.

    [ThreadStatic]
    private static long t_threadCacheVersion;

    private static long s_globalCacheVersion;
#endif

    static VisualStyleRenderer()
    {
#if !LIBREWINFORMS_PORTABLE
        SystemEvents.UserPreferenceChanging += OnUserPreferenceChanging;
#endif
    }

    /// <summary>
    ///  Check if visual styles is supported for client area.
    /// </summary>
    private static bool AreClientAreaVisualStylesSupported
    {
        get
        {
#if LIBREWINFORMS_PORTABLE
            return Application.UseVisualStyles
                && PortableVisualStyles.IsEnabled
                && (Application.VisualStyleState & VisualStyleState.ClientAreaEnabled) == VisualStyleState.ClientAreaEnabled;
#else
            return (VisualStyleInformation.IsEnabledByUser &&
               ((Application.VisualStyleState & VisualStyleState.ClientAreaEnabled) == VisualStyleState.ClientAreaEnabled));
#endif
        }
    }

#if LIBREWINFORMS_PORTABLE
    private static ILibreVisualStyleService PortableVisualStyles
        => LibrePlatform.IsRegistered
            ? LibrePlatform.Current.VisualStyles
            : UnsupportedLibreVisualStyleService.Instance;
#endif

    /// <summary>
    ///  Returns true if visual styles are 1) supported by the OS 2) enabled in the client area
    ///  and 3) currently applied to this application. Otherwise, it returns false. Note that
    ///  if it returns false, attempting to instantiate/use objects of this class
    ///  will result in exceptions being thrown.
    /// </summary>
    public static bool IsSupported
    {
        get
        {
            bool supported = AreClientAreaVisualStylesSupported;

#if !LIBREWINFORMS_PORTABLE
            if (supported)
            {
                // In some cases, this check isn't enough, since the theme handle creation
                // could fail for some other reason. Try creating a theme handle here - if successful, return true,
                // else return false.
                IntPtr hTheme = GetHandle("BUTTON", false); // Button is an arbitrary choice.
                supported = hTheme != IntPtr.Zero;
            }
#endif

            return supported;
        }
    }

    /// <summary>
    ///  Returns true if the element is defined by the current visual style, else false.
    ///  Note:
    ///  1) Throws an exception if IsSupported is false, since it is illegal to call it in that case.
    ///  2) The underlying API does not validate states. So if you pass in invalid state values,
    ///   we might still return true. When you use an invalid state to render, you get the default
    ///   state instead.
    /// </summary>
    public static bool IsElementDefined(VisualStyleElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return IsCombinationDefined(element.ClassName, element.Part);
    }

    internal static bool IsCombinationDefined(string className, int part)
    {
#if LIBREWINFORMS_PORTABLE
        if (!IsSupported)
        {
            throw new InvalidOperationException(SR.VisualStyleNotActive);
        }

        return PortableVisualStyles.IsElementDefined(className, part);
#else
        bool result = false;

        if (!IsSupported)
        {
            throw new InvalidOperationException(VisualStyleInformation.IsEnabledByUser
                ? SR.VisualStylesDisabledInClientArea
                : SR.VisualStyleNotActive);
        }

        HTHEME hTheme = GetHandle(className, false);

        if (!hTheme.IsNull)
        {
            // IsThemePartDefined doesn't work for part = 0, although there are valid parts numbered 0. We
            // allow these explicitly here.
            result = part == 0 || (bool)PInvoke.IsThemePartDefined(hTheme, part, 0);
        }

        // If the combo isn't defined, check the validity of our theme handle cache.
        if (!result)
        {
            using PInvoke.OpenThemeDataScope handle = new(HWND.Null, className);

            if (!handle.IsNull)
            {
                result = PInvoke.IsThemePartDefined(handle, part, 0);
            }

            // If we did, in fact get a new correct theme handle, our cache is out of date -- update it now.
            if (result)
            {
                RefreshCache();
            }
        }

        return result;
#endif
    }

    /// <summary>
    ///  Constructor takes a VisualStyleElement.
    /// </summary>
    public VisualStyleRenderer(VisualStyleElement element) : this(element.ClassName, element.Part, element.State)
    {
    }

    /// <summary>
    ///  Constructor takes weakly typed parameters - left for extensibility (using classes, parts or states
    ///  not defined in the VisualStyleElement class.)
    /// </summary>
    public VisualStyleRenderer(string className, int part, int state)
    {
        ArgumentNullException.ThrowIfNull(className);

        if (!IsCombinationDefined(className, part))
            throw new ArgumentException(SR.VisualStylesInvalidCombination);

        Class = className;
        Part = part;
        State = state;
    }

    /// <summary>
    ///  Returns the current _class. Use SetParameters to set.
    /// </summary>
    public string Class { get; private set; }

    /// <summary>
    ///  Returns the current part. Use SetParameters to set.
    /// </summary>
    public int Part { get; private set; }

    /// <summary>
    ///  Returns the current state. Use SetParameters to set.
    /// </summary>
    public int State { get; private set; }

    /// <summary>
    ///  Returns the underlying HTheme handle.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   NOTE: The handle gets invalidated when the theme changes or the user disables theming. When that
    ///   happens, the user should requery this property to get the correct handle. To know when the
    ///   theme changed, hook on to SystemEvents.UserPreferenceChanged and look for ThemeChanged.
    ///   category.
    ///  </para>
    /// </remarks>
    public IntPtr Handle
#if LIBREWINFORMS_PORTABLE
        => throw new PlatformNotSupportedException(
            "HTHEME export requires the explicit Windows UxTheme adapter.");
#else
        => !IsSupported
            ? throw new InvalidOperationException(VisualStyleInformation.IsEnabledByUser
                ? SR.VisualStylesDisabledInClientArea
                : SR.VisualStyleNotActive)
            : (nint)GetHandle(Class);
#endif

    HTHEME IHandle<HTHEME>.Handle => (HTHEME)Handle;

    internal HTHEME HTHEME => (HTHEME)Handle;

    /// <summary>
    ///  Used to set a new VisualStyleElement on this VisualStyleRenderer instance.
    /// </summary>
    public void SetParameters(VisualStyleElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        SetParameters(element.ClassName, element.Part, element.State);
    }

    /// <summary>
    ///  Used to set the _class, part and state that the VisualStyleRenderer object references.
    ///  These parameters cannot be set individually.
    ///  This method is present for extensibility.
    /// </summary>
    public void SetParameters(string className, int part, int state)
    {
        if (!IsCombinationDefined(className, part))
            throw new ArgumentException(SR.VisualStylesInvalidCombination);

        Class = className;
        Part = part;
        State = state;
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public void DrawBackground(IDeviceContext dc, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(dc);

#if LIBREWINFORMS_PORTABLE
        if (bounds.Width < 0 || bounds.Height < 0)
        {
            return;
        }

        PortableVisualStyles.DrawBackground(GetPortableGraphics(dc), Class, Part, State, bounds, null);
        _lastHResult = default;
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        DrawBackground(hdc, bounds, HWND.Null);
#endif
    }

    internal unsafe void DrawBackground(HDC dc, Rectangle bounds, HWND hwnd = default)
    {
        if (bounds.Width < 0 || bounds.Height < 0)
        {
            return;
        }

        if (!hwnd.IsNull)
        {
            using var htheme = OpenThemeData(hwnd, Class);
            _lastHResult = PInvoke.DrawThemeBackground(htheme, dc, Part, State, bounds, null);
        }
        else
        {
            _lastHResult = PInvoke.DrawThemeBackground(HTHEME, dc, Part, State, bounds, null);
        }
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public void DrawBackground(IDeviceContext dc, Rectangle bounds, Rectangle clipRectangle)
    {
        ArgumentNullException.ThrowIfNull(dc);

#if LIBREWINFORMS_PORTABLE
        if (bounds.Width < 0 || bounds.Height < 0 || clipRectangle.Width < 0 || clipRectangle.Height < 0)
        {
            return;
        }

        PortableVisualStyles.DrawBackground(GetPortableGraphics(dc), Class, Part, State, bounds, clipRectangle);
        _lastHResult = default;
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        DrawBackground(hdc, bounds, clipRectangle, HWND.Null);
#endif
    }

    internal unsafe void DrawBackground(HDC dc, Rectangle bounds, Rectangle clipRectangle, HWND hwnd)
    {
        if (bounds.Width < 0 || bounds.Height < 0 || clipRectangle.Width < 0 || clipRectangle.Height < 0)
            return;

        if (!hwnd.IsNull)
        {
            using var htheme = OpenThemeData(hwnd, Class);
            _lastHResult = PInvoke.DrawThemeBackground(htheme, dc, Part, State, bounds, clipRectangle);
        }
        else
        {
            _lastHResult = PInvoke.DrawThemeBackground(HTHEME, dc, Part, State, bounds, clipRectangle);
        }
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public Rectangle DrawEdge(IDeviceContext dc, Rectangle bounds, Edges edges, EdgeStyle style, EdgeEffects effects)
    {
        ArgumentNullException.ThrowIfNull(dc);

        SourceGenerated.EnumValidator.Validate(edges, nameof(edges));
        SourceGenerated.EnumValidator.Validate(style, nameof(style));
        SourceGenerated.EnumValidator.Validate(effects, nameof(effects));

#if LIBREWINFORMS_PORTABLE
        Rectangle contentBounds = PortableVisualStyles.DrawEdge(
            GetPortableGraphics(dc),
            Class,
            Part,
            State,
            bounds,
            GetPortableEdges(edges),
            GetPortableEdgeStyle(style),
            GetPortableEdgeEffects(effects));
        _lastHResult = default;
        return contentBounds;
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        return DrawEdge(hdc, bounds, edges, style, effects);
#endif
    }

    internal unsafe Rectangle DrawEdge(HDC dc, Rectangle bounds, Edges edges, EdgeStyle style, EdgeEffects effects)
    {
        SourceGenerated.EnumValidator.Validate(edges, nameof(edges));
        SourceGenerated.EnumValidator.Validate(style, nameof(style));
        SourceGenerated.EnumValidator.Validate(effects, nameof(effects));

        RECT contentRect;
        _lastHResult = PInvoke.DrawThemeEdge(
            HTHEME,
            dc,
            Part,
            State,
            bounds,
            (DRAWEDGE_FLAGS)style,
            (DRAW_EDGE_FLAGS)edges | (DRAW_EDGE_FLAGS)effects | DRAW_EDGE_FLAGS.BF_ADJUST,
            out contentRect);

        return contentRect;
    }

    /// <summary>
    ///  [See win32 equivalent.]
    ///  This method uses Graphics.DrawImage as a backup if themed drawing does not work.
    /// </summary>
    public void DrawImage(Graphics g, Rectangle bounds, Image image)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(image);

        if (bounds.Width < 0 || bounds.Height < 0)
            return;

        g.DrawImage(image, bounds);
    }

    /// <summary>
    ///  [See win32 equivalent.]
    ///  This method uses Graphics.DrawImage as a backup if themed drawing does not work.
    /// </summary>
    public void DrawImage(Graphics g, Rectangle bounds, ImageList imageList, int imageIndex)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(imageList);

        ArgumentOutOfRangeException.ThrowIfNegative(imageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(imageIndex, imageList.Images.Count);

        if (bounds.Width < 0 || bounds.Height < 0)
            return;

        // DrawThemeIcon currently seems to do nothing, but still return S_OK. As a workaround,
        // we call DrawImage on the graphics object itself for now.
        g.DrawImage(imageList.Images[imageIndex], bounds);
    }

    /// <summary>
    ///  Given a graphics object and bounds to draw in, this method effectively asks the passed in
    ///  control's parent to draw itself in there (it sends WM_ERASEBKGND &amp; WM_PRINTCLIENT messages
    ///  to the parent).
    /// </summary>
    public void DrawParentBackground(IDeviceContext dc, Rectangle bounds, Control childControl)
    {
        ArgumentNullException.ThrowIfNull(dc);
        ArgumentNullException.ThrowIfNull(childControl);

        if (bounds.Width < 0 || bounds.Height < 0)
        {
            return;
        }

#if LIBREWINFORMS_PORTABLE
        childControl.DrawPortableParentBackground(GetPortableGraphics(dc), bounds);
        _lastHResult = default;
#else
        if (childControl.IsHandleCreated)
        {
            using DeviceContextHdcScope hdc = dc.ToHdcScope();
            _lastHResult = PInvoke.DrawThemeParentBackground(childControl.HWND, hdc, bounds);
        }
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public void DrawText(IDeviceContext dc, Rectangle bounds, string? textToDraw)
    {
        DrawText(dc, bounds, textToDraw, false);
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public void DrawText(IDeviceContext dc, Rectangle bounds, string? textToDraw, bool drawDisabled)
    {
        DrawText(dc, bounds, textToDraw, drawDisabled, TextFormatFlags.HorizontalCenter);
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public void DrawText(IDeviceContext dc, Rectangle bounds, string? textToDraw, bool drawDisabled, TextFormatFlags flags)
    {
        ArgumentNullException.ThrowIfNull(dc);

#if LIBREWINFORMS_PORTABLE
        if (bounds.Width < 0 || bounds.Height < 0 || string.IsNullOrEmpty(textToDraw))
        {
            return;
        }

        PortableVisualStyles.DrawText(
            GetPortableGraphics(dc),
            Class,
            Part,
            State,
            bounds,
            textToDraw,
            drawDisabled,
            GetPortableTextFormat(flags));
        _lastHResult = default;
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        DrawText(hdc, bounds, textToDraw, drawDisabled, flags);
#endif
    }

    internal void DrawText(HDC dc, Rectangle bounds, string? textToDraw, bool drawDisabled, TextFormatFlags flags)
    {
        if (bounds.Width < 0 || bounds.Height < 0)
        {
            return;
        }

        if (!string.IsNullOrEmpty(textToDraw))
        {
            uint disableFlag = drawDisabled ? 0x1u : 0u;
            _lastHResult = PInvoke.DrawThemeText(
                HTHEME,
                dc,
                Part,
                State,
                textToDraw,
                textToDraw.Length,
                (DRAW_TEXT_FORMAT)flags,
                disableFlag,
                bounds);
        }
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public Rectangle GetBackgroundContentRectangle(IDeviceContext dc, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(dc);

#if LIBREWINFORMS_PORTABLE
        if (bounds.Width < 0 || bounds.Height < 0)
        {
            return Rectangle.Empty;
        }

        _lastHResult = default;
        return PortableVisualStyles.GetBackgroundContentRectangle(Class, Part, State, bounds);
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        return GetBackgroundContentRectangle(hdc, bounds);
#endif
    }

    internal Rectangle GetBackgroundContentRectangle(HDC dc, Rectangle bounds)
    {
        if (bounds.Width < 0 || bounds.Height < 0)
        {
            return Rectangle.Empty;
        }

        _lastHResult = PInvoke.GetThemeBackgroundContentRect(HTHEME, dc, Part, State, bounds, out RECT rect);
        return rect;
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public Rectangle GetBackgroundExtent(IDeviceContext dc, Rectangle contentBounds)
    {
        ArgumentNullException.ThrowIfNull(dc);

        if (contentBounds.Width < 0 || contentBounds.Height < 0)
        {
            return Rectangle.Empty;
        }

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.GetBackgroundExtent(Class, Part, State, contentBounds);
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        _lastHResult = PInvoke.GetThemeBackgroundExtent(HTHEME, hdc, Part, State, contentBounds, out RECT extents);
        return extents;
#endif
    }

    /// <summary>
    ///  Computes the region for a regular or partially transparent background that is bounded by a specified
    ///  rectangle. Return null if the region cannot be created.
    ///  [See win32 equivalent.]
    /// </summary>
    public unsafe Region? GetBackgroundRegion(IDeviceContext dc, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(dc);

        if (bounds.Width < 0 || bounds.Height < 0)
        {
            return null;
        }

#if LIBREWINFORMS_PORTABLE
        Region? region = PortableVisualStyles.GetBackgroundRegion(Class, Part, State, bounds);
        _lastHResult = default;
        return region;
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        HRGN hrgn;
        _lastHResult = PInvoke.GetThemeBackgroundRegion(HTHEME, hdc, Part, State, bounds, out hrgn);

        // GetThemeBackgroundRegion returns a null hRegion if it fails to create one, it could be because the bounding
        // box is too big. For more info see code in %xpsrc%\shell\themes\uxtheme\imagefile.cpp if you have an enlistment to it.

        if (hrgn.IsNull)
        {
            return null;
        }

        // From the GDI+ sources it doesn't appear as if they take ownership of the hRegion, so this is safe to do.
        // We need to DeleteObject in order to not leak.
        Region region = Region.FromHrgn(hrgn);
        PInvokeCore.DeleteObject(hrgn);
        return region;
#endif
    }

#if LIBREWINFORMS_PORTABLE
    private static Graphics GetPortableGraphics(IDeviceContext deviceContext)
        => deviceContext as Graphics
            ?? throw new PlatformNotSupportedException(
                "Portable visual-style drawing requires a managed System.Drawing.Graphics recorder.");

    private static LibreVisualStyleSizeType GetPortableSizeType(ThemeSizeType type)
        => type switch
        {
            ThemeSizeType.Minimum => LibreVisualStyleSizeType.Minimum,
            ThemeSizeType.True => LibreVisualStyleSizeType.True,
            ThemeSizeType.Draw => LibreVisualStyleSizeType.Draw,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

    private static LibreVisualStyleEdges GetPortableEdges(Edges edges)
    {
        LibreVisualStyleEdges portable = LibreVisualStyleEdges.None;
        if (edges.HasFlag(Edges.Left))
            portable |= LibreVisualStyleEdges.Left;
        if (edges.HasFlag(Edges.Top))
            portable |= LibreVisualStyleEdges.Top;
        if (edges.HasFlag(Edges.Right))
            portable |= LibreVisualStyleEdges.Right;
        if (edges.HasFlag(Edges.Bottom))
            portable |= LibreVisualStyleEdges.Bottom;
        if (edges.HasFlag(Edges.Diagonal))
            portable |= LibreVisualStyleEdges.Diagonal;
        return portable;
    }

    private static LibreVisualStyleEdgeStyle GetPortableEdgeStyle(EdgeStyle style)
        => style switch
        {
            EdgeStyle.Raised => LibreVisualStyleEdgeStyle.Raised,
            EdgeStyle.Sunken => LibreVisualStyleEdgeStyle.Sunken,
            EdgeStyle.Etched => LibreVisualStyleEdgeStyle.Etched,
            EdgeStyle.Bump => LibreVisualStyleEdgeStyle.Bump,
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        };

    private static LibreVisualStyleEdgeEffects GetPortableEdgeEffects(EdgeEffects effects)
    {
        LibreVisualStyleEdgeEffects portable = LibreVisualStyleEdgeEffects.None;
        if (effects.HasFlag(EdgeEffects.FillInterior))
            portable |= LibreVisualStyleEdgeEffects.FillInterior;
        if (effects.HasFlag(EdgeEffects.Flat))
            portable |= LibreVisualStyleEdgeEffects.Flat;
        if (effects.HasFlag(EdgeEffects.Soft))
            portable |= LibreVisualStyleEdgeEffects.Soft;
        if (effects.HasFlag(EdgeEffects.Mono))
            portable |= LibreVisualStyleEdgeEffects.Mono;
        return portable;
    }

    private static LibreVisualStyleTextFormat GetPortableTextFormat(TextFormatFlags flags)
    {
#pragma warning disable CS0618 // ModifyString is obsolete and deliberately rejected.
        const TextFormatFlags unsupported = TextFormatFlags.ExternalLeading
            | TextFormatFlags.Internal
            | TextFormatFlags.ModifyString
            | TextFormatFlags.NoFullWidthCharacterBreak
            | TextFormatFlags.PrefixOnly
            | TextFormatFlags.TextBoxControl;
#pragma warning restore CS0618
        const TextFormatFlags accepted = TextFormatFlags.Bottom
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.ExpandTabs
            | TextFormatFlags.HidePrefix
            | TextFormatFlags.HorizontalCenter
            | TextFormatFlags.NoClipping
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.PathEllipsis
            | TextFormatFlags.Right
            | TextFormatFlags.RightToLeft
            | TextFormatFlags.SingleLine
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.WordBreak
            | TextFormatFlags.WordEllipsis
            | TextFormatFlags.PreserveGraphicsClipping
            | TextFormatFlags.PreserveGraphicsTranslateTransform
            | TextFormatFlags.NoPadding
            | TextFormatFlags.LeftAndRightPadding;
        TextFormatFlags rejected = (flags & unsupported) | (flags & ~accepted);
        if (rejected != 0)
        {
            throw new PlatformNotSupportedException(
                $"Portable visual-style text flags '{rejected}' are not implemented.");
        }

        LibreVisualStyleTextFormat portable = LibreVisualStyleTextFormat.Default;
        if (flags.HasFlag(TextFormatFlags.HorizontalCenter))
            portable |= LibreVisualStyleTextFormat.HorizontalCenter;
        if (flags.HasFlag(TextFormatFlags.Right))
            portable |= LibreVisualStyleTextFormat.Right;
        if (flags.HasFlag(TextFormatFlags.VerticalCenter))
            portable |= LibreVisualStyleTextFormat.VerticalCenter;
        if (flags.HasFlag(TextFormatFlags.Bottom))
            portable |= LibreVisualStyleTextFormat.Bottom;
        if (flags.HasFlag(TextFormatFlags.SingleLine))
            portable |= LibreVisualStyleTextFormat.SingleLine;
        if (flags.HasFlag(TextFormatFlags.WordBreak))
            portable |= LibreVisualStyleTextFormat.WordBreak;
        if (flags.HasFlag(TextFormatFlags.EndEllipsis))
            portable |= LibreVisualStyleTextFormat.EndEllipsis;
        if (flags.HasFlag(TextFormatFlags.PathEllipsis))
            portable |= LibreVisualStyleTextFormat.PathEllipsis;
        if (flags.HasFlag(TextFormatFlags.WordEllipsis))
            portable |= LibreVisualStyleTextFormat.WordEllipsis;
        if (flags.HasFlag(TextFormatFlags.RightToLeft))
            portable |= LibreVisualStyleTextFormat.RightToLeft;
        if (flags.HasFlag(TextFormatFlags.NoClipping))
            portable |= LibreVisualStyleTextFormat.NoClipping;
        if (flags.HasFlag(TextFormatFlags.ExpandTabs))
            portable |= LibreVisualStyleTextFormat.ExpandTabs;
        if (flags.HasFlag(TextFormatFlags.NoPrefix))
            portable |= LibreVisualStyleTextFormat.NoPrefix;
        if (flags.HasFlag(TextFormatFlags.HidePrefix))
            portable |= LibreVisualStyleTextFormat.HidePrefix;
        if (flags.HasFlag(TextFormatFlags.NoPadding))
            portable |= LibreVisualStyleTextFormat.NoPadding;
        if (flags.HasFlag(TextFormatFlags.LeftAndRightPadding))
            portable |= LibreVisualStyleTextFormat.LeftAndRightPadding;
        return portable;
    }

    private static LibreVisualStyleHitTestOptions GetPortableHitTestOptions(HitTestOptions options)
    {
        const HitTestOptions accepted = HitTestOptions.FixedBorder
            | HitTestOptions.Caption
            | HitTestOptions.ResizingBorder
            | HitTestOptions.SizingTemplate
            | HitTestOptions.SystemSizingMargins;
        if ((options & ~accepted) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        LibreVisualStyleHitTestOptions portable = LibreVisualStyleHitTestOptions.None;
        if (options.HasFlag(HitTestOptions.FixedBorder))
            portable |= LibreVisualStyleHitTestOptions.FixedBorder;
        if (options.HasFlag(HitTestOptions.Caption))
            portable |= LibreVisualStyleHitTestOptions.Caption;
        if (options.HasFlag(HitTestOptions.ResizingBorderLeft))
            portable |= LibreVisualStyleHitTestOptions.ResizingBorderLeft;
        if (options.HasFlag(HitTestOptions.ResizingBorderTop))
            portable |= LibreVisualStyleHitTestOptions.ResizingBorderTop;
        if (options.HasFlag(HitTestOptions.ResizingBorderRight))
            portable |= LibreVisualStyleHitTestOptions.ResizingBorderRight;
        if (options.HasFlag(HitTestOptions.ResizingBorderBottom))
            portable |= LibreVisualStyleHitTestOptions.ResizingBorderBottom;
        if (options.HasFlag(HitTestOptions.SizingTemplate))
            portable |= LibreVisualStyleHitTestOptions.SizingTemplate;
        if (options.HasFlag(HitTestOptions.SystemSizingMargins))
            portable |= LibreVisualStyleHitTestOptions.SystemSizingMargins;
        return portable;
    }

    private static HitTestCode GetHitTestCode(LibreVisualStyleHitTestCode code)
        => code switch
        {
            LibreVisualStyleHitTestCode.Nowhere => HitTestCode.Nowhere,
            LibreVisualStyleHitTestCode.Client => HitTestCode.Client,
            LibreVisualStyleHitTestCode.Left => HitTestCode.Left,
            LibreVisualStyleHitTestCode.Right => HitTestCode.Right,
            LibreVisualStyleHitTestCode.Top => HitTestCode.Top,
            LibreVisualStyleHitTestCode.Bottom => HitTestCode.Bottom,
            LibreVisualStyleHitTestCode.TopLeft => HitTestCode.TopLeft,
            LibreVisualStyleHitTestCode.TopRight => HitTestCode.TopRight,
            LibreVisualStyleHitTestCode.BottomLeft => HitTestCode.BottomLeft,
            LibreVisualStyleHitTestCode.BottomRight => HitTestCode.BottomRight,
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };

    private static TextMetrics GetTextMetrics(LibreVisualStyleTextMetrics metrics) => new()
    {
        Height = metrics.Height,
        Ascent = metrics.Ascent,
        Descent = metrics.Descent,
        InternalLeading = metrics.InternalLeading,
        ExternalLeading = metrics.ExternalLeading,
        AverageCharWidth = metrics.AverageCharWidth,
        MaxCharWidth = metrics.MaxCharWidth,
        Weight = metrics.Weight,
        Overhang = metrics.Overhang,
        DigitizedAspectX = metrics.DigitizedAspectX,
        DigitizedAspectY = metrics.DigitizedAspectY,
        FirstChar = metrics.FirstChar,
        LastChar = metrics.LastChar,
        DefaultChar = metrics.DefaultChar,
        BreakChar = metrics.BreakChar,
        Italic = metrics.Italic,
        Underlined = metrics.Underlined,
        StruckOut = metrics.StruckOut,
        PitchAndFamily = GetTextPitchAndFamily(metrics.PitchAndFamily),
        CharSet = GetTextCharacterSet(metrics.CharacterSet),
    };

    private static TextMetricsPitchAndFamilyValues GetTextPitchAndFamily(
        LibreVisualStyleTextPitchAndFamily value)
    {
        const LibreVisualStyleTextPitchAndFamily accepted = LibreVisualStyleTextPitchAndFamily.FixedPitch
            | LibreVisualStyleTextPitchAndFamily.Vector
            | LibreVisualStyleTextPitchAndFamily.TrueType
            | LibreVisualStyleTextPitchAndFamily.Device;
        if ((value & ~accepted) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        TextMetricsPitchAndFamilyValues result = default;
        if (value.HasFlag(LibreVisualStyleTextPitchAndFamily.FixedPitch))
            result |= TextMetricsPitchAndFamilyValues.FixedPitch;
        if (value.HasFlag(LibreVisualStyleTextPitchAndFamily.Vector))
            result |= TextMetricsPitchAndFamilyValues.Vector;
        if (value.HasFlag(LibreVisualStyleTextPitchAndFamily.TrueType))
            result |= TextMetricsPitchAndFamilyValues.TrueType;
        if (value.HasFlag(LibreVisualStyleTextPitchAndFamily.Device))
            result |= TextMetricsPitchAndFamilyValues.Device;
        return result;
    }

    private static TextMetricsCharacterSet GetTextCharacterSet(LibreVisualStyleTextCharacterSet value)
        => value switch
        {
            LibreVisualStyleTextCharacterSet.Ansi => TextMetricsCharacterSet.Ansi,
            LibreVisualStyleTextCharacterSet.Default => TextMetricsCharacterSet.Default,
            LibreVisualStyleTextCharacterSet.Symbol => TextMetricsCharacterSet.Symbol,
            LibreVisualStyleTextCharacterSet.Mac => TextMetricsCharacterSet.Mac,
            LibreVisualStyleTextCharacterSet.ShiftJis => TextMetricsCharacterSet.ShiftJis,
            LibreVisualStyleTextCharacterSet.Hangul => TextMetricsCharacterSet.Hangul,
            LibreVisualStyleTextCharacterSet.Johab => TextMetricsCharacterSet.Johab,
            LibreVisualStyleTextCharacterSet.Gb2312 => TextMetricsCharacterSet.Gb2312,
            LibreVisualStyleTextCharacterSet.ChineseBig5 => TextMetricsCharacterSet.ChineseBig5,
            LibreVisualStyleTextCharacterSet.Greek => TextMetricsCharacterSet.Greek,
            LibreVisualStyleTextCharacterSet.Turkish => TextMetricsCharacterSet.Turkish,
            LibreVisualStyleTextCharacterSet.Vietnamese => TextMetricsCharacterSet.Vietnamese,
            LibreVisualStyleTextCharacterSet.Hebrew => TextMetricsCharacterSet.Hebrew,
            LibreVisualStyleTextCharacterSet.Arabic => TextMetricsCharacterSet.Arabic,
            LibreVisualStyleTextCharacterSet.Baltic => TextMetricsCharacterSet.Baltic,
            LibreVisualStyleTextCharacterSet.Russian => TextMetricsCharacterSet.Russian,
            LibreVisualStyleTextCharacterSet.Thai => TextMetricsCharacterSet.Thai,
            LibreVisualStyleTextCharacterSet.EastEurope => TextMetricsCharacterSet.EastEurope,
            LibreVisualStyleTextCharacterSet.Oem => TextMetricsCharacterSet.Oem,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static LibreVisualStyleColorProperty GetPortableColorProperty(ColorProperty property)
        => property switch
        {
            ColorProperty.BorderColor => LibreVisualStyleColorProperty.Border,
            ColorProperty.FillColor => LibreVisualStyleColorProperty.Fill,
            ColorProperty.TextColor => LibreVisualStyleColorProperty.Text,
            ColorProperty.AccentColorHint => LibreVisualStyleColorProperty.Accent,
            _ => throw new PlatformNotSupportedException(
                $"Portable visual-style color property '{property}' is not implemented."),
        };

    private static LibreVisualStyleIntegerProperty GetPortableIntegerProperty(IntegerProperty property)
        => property switch
        {
            IntegerProperty.ProgressChunkSize => LibreVisualStyleIntegerProperty.ProgressChunkSize,
            IntegerProperty.ProgressSpaceSize => LibreVisualStyleIntegerProperty.ProgressSpaceSize,
            _ => throw new PlatformNotSupportedException(
                $"Portable visual-style integer property '{property}' is not implemented."),
        };

    private static LibreVisualStyleBooleanProperty GetPortableBooleanProperty(BooleanProperty property)
        => property switch
        {
            BooleanProperty.Transparent => LibreVisualStyleBooleanProperty.Transparent,
            BooleanProperty.AutoSize => LibreVisualStyleBooleanProperty.AutoSize,
            BooleanProperty.BorderOnly => LibreVisualStyleBooleanProperty.BorderOnly,
            BooleanProperty.Composited => LibreVisualStyleBooleanProperty.Composited,
            BooleanProperty.BackgroundFill => LibreVisualStyleBooleanProperty.BackgroundFill,
            BooleanProperty.GlyphTransparent => LibreVisualStyleBooleanProperty.GlyphTransparent,
            BooleanProperty.GlyphOnly => LibreVisualStyleBooleanProperty.GlyphOnly,
            BooleanProperty.AlwaysShowSizingBar => LibreVisualStyleBooleanProperty.AlwaysShowSizingBar,
            BooleanProperty.MirrorImage => LibreVisualStyleBooleanProperty.MirrorImage,
            BooleanProperty.UniformSizing => LibreVisualStyleBooleanProperty.UniformSizing,
            BooleanProperty.IntegralSizing => LibreVisualStyleBooleanProperty.IntegralSizing,
            BooleanProperty.SourceGrow => LibreVisualStyleBooleanProperty.SourceGrow,
            BooleanProperty.SourceShrink => LibreVisualStyleBooleanProperty.SourceShrink,
            _ => throw new PlatformNotSupportedException(
                $"Portable visual-style Boolean property '{property}' is not implemented."),
        };

    private static LibreVisualStyleEnumProperty GetPortableEnumProperty(EnumProperty property)
        => property switch
        {
            EnumProperty.BackgroundType => LibreVisualStyleEnumProperty.BackgroundType,
            EnumProperty.BorderType => LibreVisualStyleEnumProperty.BorderType,
            EnumProperty.FillType => LibreVisualStyleEnumProperty.FillType,
            EnumProperty.SizingType => LibreVisualStyleEnumProperty.SizingType,
            EnumProperty.HorizontalAlignment => LibreVisualStyleEnumProperty.HorizontalAlignment,
            EnumProperty.ContentAlignment => LibreVisualStyleEnumProperty.ContentAlignment,
            EnumProperty.VerticalAlignment => LibreVisualStyleEnumProperty.VerticalAlignment,
            EnumProperty.OffsetType => LibreVisualStyleEnumProperty.OffsetType,
            EnumProperty.IconEffect => LibreVisualStyleEnumProperty.IconEffect,
            EnumProperty.TextShadowType => LibreVisualStyleEnumProperty.TextShadowType,
            EnumProperty.ImageLayout => LibreVisualStyleEnumProperty.ImageLayout,
            EnumProperty.GlyphType => LibreVisualStyleEnumProperty.GlyphType,
            EnumProperty.ImageSelectType => LibreVisualStyleEnumProperty.ImageSelectType,
            EnumProperty.GlyphFontSizingType => LibreVisualStyleEnumProperty.GlyphFontSizingType,
            EnumProperty.TrueSizeScalingType => LibreVisualStyleEnumProperty.TrueSizeScalingType,
            _ => throw new PlatformNotSupportedException(
                $"Portable visual-style enum property '{property}' is not implemented."),
        };

    private static LibreVisualStyleFilenameProperty GetPortableFilenameProperty(FilenameProperty property)
        => property switch
        {
            FilenameProperty.ImageFile => LibreVisualStyleFilenameProperty.ImageFile,
            FilenameProperty.ImageFile1 => LibreVisualStyleFilenameProperty.ImageFile1,
            FilenameProperty.ImageFile2 => LibreVisualStyleFilenameProperty.ImageFile2,
            FilenameProperty.ImageFile3 => LibreVisualStyleFilenameProperty.ImageFile3,
            FilenameProperty.ImageFile4 => LibreVisualStyleFilenameProperty.ImageFile4,
            FilenameProperty.ImageFile5 => LibreVisualStyleFilenameProperty.ImageFile5,
            FilenameProperty.StockImageFile => LibreVisualStyleFilenameProperty.StockImageFile,
            FilenameProperty.GlyphImageFile => LibreVisualStyleFilenameProperty.GlyphImageFile,
            _ => throw new PlatformNotSupportedException(
                $"Portable visual-style filename property '{property}' is not implemented."),
        };

    private static LibreVisualStyleStringProperty GetPortableStringProperty(StringProperty property)
        => property switch
        {
            StringProperty.Text => LibreVisualStyleStringProperty.Text,
            _ => throw new PlatformNotSupportedException(
                $"Portable visual-style string property '{property}' is not implemented."),
        };

    private static LibreVisualStyleFontProperty GetPortableFontProperty(FontProperty property)
        => property switch
        {
            FontProperty.TextFont => LibreVisualStyleFontProperty.Text,
            FontProperty.GlyphFont => LibreVisualStyleFontProperty.Glyph,
            _ => throw new PlatformNotSupportedException(
                $"Portable visual-style font property '{property}' is not implemented."),
        };

    private static LibreVisualStyleMarginProperty GetPortableMarginProperty(MarginProperty property)
        => property switch
        {
            MarginProperty.SizingMargins => LibreVisualStyleMarginProperty.Sizing,
            MarginProperty.ContentMargins => LibreVisualStyleMarginProperty.Content,
            MarginProperty.CaptionMargins => LibreVisualStyleMarginProperty.Caption,
            _ => throw new PlatformNotSupportedException(
                $"Portable visual-style margin property '{property}' is not implemented."),
        };

    private static LibreVisualStylePointProperty GetPortablePointProperty(PointProperty property)
        => property switch
        {
            PointProperty.Offset => LibreVisualStylePointProperty.Offset,
            PointProperty.TextShadowOffset => LibreVisualStylePointProperty.TextShadowOffset,
            PointProperty.MinSize => LibreVisualStylePointProperty.MinimumSize,
            PointProperty.MinSize1 => LibreVisualStylePointProperty.MinimumSize1,
            PointProperty.MinSize2 => LibreVisualStylePointProperty.MinimumSize2,
            PointProperty.MinSize3 => LibreVisualStylePointProperty.MinimumSize3,
            PointProperty.MinSize4 => LibreVisualStylePointProperty.MinimumSize4,
            PointProperty.MinSize5 => LibreVisualStylePointProperty.MinimumSize5,
            _ => throw new PlatformNotSupportedException(
                $"Portable visual-style point property '{property}' is not implemented."),
        };
#endif

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public bool GetBoolean(BooleanProperty prop)
    {
        SourceGenerated.EnumValidator.Validate(prop, nameof(prop));

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.GetBoolean(Class, Part, State, GetPortableBooleanProperty(prop));
#else
        _lastHResult = PInvoke.GetThemeBool(HTHEME, Part, State, (THEME_PROPERTY_SYMBOL_ID)prop, out BOOL value);
        return value;
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public Color GetColor(ColorProperty prop)
    {
        // Valid values are 0xed9 to 0xeef
        SourceGenerated.EnumValidator.Validate(prop, nameof(prop));

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.GetColor(Class, Part, State, GetPortableColorProperty(prop));
#else
        _lastHResult = PInvoke.GetThemeColor(HTHEME, Part, State, (THEME_PROPERTY_SYMBOL_ID)prop, out COLORREF color);
        return color;
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public int GetEnumValue(EnumProperty prop)
    {
        // Valid values are 0xfa1 to 0xfaf
        SourceGenerated.EnumValidator.Validate(prop, nameof(prop));

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.GetEnumValue(Class, Part, State, GetPortableEnumProperty(prop));
#else
        _lastHResult = PInvoke.GetThemeEnumValue(HTHEME, Part, State, (THEME_PROPERTY_SYMBOL_ID)prop, out int value);
        return value;
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public unsafe string GetFilename(FilenameProperty prop)
    {
        // Valid values are 0xbb9 to 0xbc0
        SourceGenerated.EnumValidator.Validate(prop, nameof(prop));

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.GetFilename(Class, Part, State, GetPortableFilenameProperty(prop));
#else
        Span<char> filename = stackalloc char[512];
        fixed (char* pFilename = filename)
        {
            _lastHResult = PInvoke.GetThemeFilename(HTHEME, Part, State, (THEME_PROPERTY_SYMBOL_ID)prop, pFilename, filename.Length);
        }

        return filename.SliceAtFirstNull().ToString();
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    ///  Returns null if the returned font was not true type, since GDI+ does not support it.
    /// </summary>
    public Font? GetFont(IDeviceContext dc, FontProperty prop)
    {
        ArgumentNullException.ThrowIfNull(dc);

        SourceGenerated.EnumValidator.Validate(prop, nameof(prop));

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.GetFont(Class, Part, State, GetPortableFontProperty(prop));
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        _lastHResult = PInvoke.GetThemeFont(this, hdc, Part, State, (int)prop, out LOGFONT logfont);

        // Check for a failed HR.
        if (!_lastHResult.Succeeded)
        {
            return null;
        }

        try
        {
            return Font.FromLogFont(logfont);
        }
        catch (Exception e) when (!e.IsCriticalException())
        {
            // Looks like the font was not true type
            return null;
        }
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public int GetInteger(IntegerProperty prop)
    {
        // Valid values are 0x961 to 0x978
        SourceGenerated.EnumValidator.Validate(prop, nameof(prop));

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.GetInteger(Class, Part, State, GetPortableIntegerProperty(prop));
#else
        _lastHResult = PInvoke.GetThemeInt(HTHEME, Part, State, (THEME_PROPERTY_SYMBOL_ID)prop, out int value);
        return value;
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public Size GetPartSize(IDeviceContext dc, ThemeSizeType type)
    {
        ArgumentNullException.ThrowIfNull(dc);

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.GetPartSize(Class, Part, State, null, GetPortableSizeType(type));
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        return GetPartSize(hdc, type, HWND.Null);
#endif
    }

    internal unsafe Size GetPartSize(HDC dc, ThemeSizeType type, HWND hwnd = default)
    {
        // Valid values are 0x0 to 0x2
        SourceGenerated.EnumValidator.Validate(type, nameof(type));

        if (!hwnd.IsNull && ScaleHelper.IsThreadPerMonitorV2Aware)
        {
            using var htheme = OpenThemeData(hwnd, Class);
            _lastHResult = PInvoke.GetThemePartSize(htheme, dc, Part, State, null, (THEMESIZE)type, out SIZE dpiSize);
            return dpiSize;
        }

        _lastHResult = PInvoke.GetThemePartSize(HTHEME, dc, Part, State, null, (THEMESIZE)type, out SIZE size);
        return size;
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public unsafe Size GetPartSize(IDeviceContext dc, Rectangle bounds, ThemeSizeType type)
    {
        ArgumentNullException.ThrowIfNull(dc);

        // Valid values are 0x0 to 0x2
        SourceGenerated.EnumValidator.Validate(type, nameof(type));

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.GetPartSize(Class, Part, State, bounds, GetPortableSizeType(type));
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        _lastHResult = PInvoke.GetThemePartSize(HTHEME, hdc, Part, State, bounds, (THEMESIZE)type, out SIZE size);
        return size;
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public Point GetPoint(PointProperty prop)
    {
        // valid values are 0xd49 to 0xd50
        SourceGenerated.EnumValidator.Validate(prop, nameof(prop));

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.GetPoint(Class, Part, State, GetPortablePointProperty(prop));
#else
        _lastHResult = PInvoke.GetThemePosition(HTHEME, Part, State, (THEME_PROPERTY_SYMBOL_ID)prop, out Point point);
        return point;
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public unsafe Padding GetMargins(IDeviceContext dc, MarginProperty prop)
    {
        ArgumentNullException.ThrowIfNull(dc);

        // Valid values are 0xe11 to 0xe13
        SourceGenerated.EnumValidator.Validate(prop, nameof(prop));

#if LIBREWINFORMS_PORTABLE
        LibreVisualStyleMargins margins = PortableVisualStyles.GetMargins(
            Class,
            Part,
            State,
            GetPortableMarginProperty(prop));
        _lastHResult = default;
        return new Padding(margins.Left, margins.Top, margins.Right, margins.Bottom);
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        _lastHResult = PInvoke.GetThemeMargins(HTHEME, hdc, Part, State, (THEME_PROPERTY_SYMBOL_ID)prop, null, out MARGINS margins);

        return new Padding(margins.cxLeftWidth, margins.cyTopHeight, margins.cxRightWidth, margins.cyBottomHeight);
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public unsafe string GetString(StringProperty prop)
    {
        // Valid values are 0xc81 to 0xc81
        SourceGenerated.EnumValidator.Validate(prop, nameof(prop));

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.GetString(Class, Part, State, GetPortableStringProperty(prop));
#else
        Span<char> aString = stackalloc char[512];
        fixed (char* pString = aString)
        {
            _lastHResult = PInvoke.GetThemeString(HTHEME, Part, State, (int)prop, pString, aString.Length);
        }

        return aString.SliceAtFirstNull().ToString();
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public unsafe Rectangle GetTextExtent(IDeviceContext dc, string textToDraw, TextFormatFlags flags)
    {
        ArgumentNullException.ThrowIfNull(dc);
        textToDraw.ThrowIfNullOrEmpty();

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.MeasureText(
            GetPortableGraphics(dc),
            Class,
            Part,
            State,
            bounds: null,
            textToDraw,
            GetPortableTextFormat(flags));
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        _lastHResult = PInvoke.GetThemeTextExtent(
            HTHEME,
            hdc,
            Part,
            State,
            textToDraw,
            textToDraw.Length,
            (DRAW_TEXT_FORMAT)flags,
            null,
            out RECT rect);

        return rect;
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public unsafe Rectangle GetTextExtent(IDeviceContext dc, Rectangle bounds, string textToDraw, TextFormatFlags flags)
    {
        ArgumentNullException.ThrowIfNull(dc);
        textToDraw.ThrowIfNullOrEmpty();

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.MeasureText(
            GetPortableGraphics(dc),
            Class,
            Part,
            State,
            bounds,
            textToDraw,
            GetPortableTextFormat(flags));
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        _lastHResult = PInvoke.GetThemeTextExtent(
            HTHEME,
            hdc,
            Part,
            State,
            textToDraw,
            textToDraw.Length,
            (DRAW_TEXT_FORMAT)flags,
            bounds,
            out RECT rect);

        return rect;
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public TextMetrics GetTextMetrics(IDeviceContext dc)
    {
        ArgumentNullException.ThrowIfNull(dc);

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return GetTextMetrics(PortableVisualStyles.GetTextMetrics(GetPortableGraphics(dc), Class, Part, State));
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        _lastHResult = PInvoke.GetThemeTextMetrics(HTHEME, hdc, Part, State, out TEXTMETRICW tm);
        return TextMetrics.FromTEXTMETRICW(tm);
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public HitTestCode HitTestBackground(IDeviceContext dc, Rectangle backgroundRectangle, Point pt, HitTestOptions options)
    {
        ArgumentNullException.ThrowIfNull(dc);

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return GetHitTestCode(PortableVisualStyles.HitTestBackground(
            GetPortableGraphics(dc),
            Class,
            Part,
            State,
            backgroundRectangle,
            region: null,
            pt,
            GetPortableHitTestOptions(options)));
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        _lastHResult = PInvoke.HitTestThemeBackground(
            HTHEME,
            hdc,
            Part,
            State,
            (HIT_TEST_BACKGROUND_OPTIONS)options,
            backgroundRectangle,
            HRGN.Null,
            pt,
            out ushort code);

        return (HitTestCode)code;
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public HitTestCode HitTestBackground(Graphics g, Rectangle backgroundRectangle, Region region, Point pt, HitTestOptions options)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(region);

#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return GetHitTestCode(PortableVisualStyles.HitTestBackground(
            g,
            Class,
            Part,
            State,
            backgroundRectangle,
            region,
            pt,
            GetPortableHitTestOptions(options)));
#else
        IntPtr hRgn = region.GetHrgn(g);
        return HitTestBackground(g, backgroundRectangle, hRgn, pt, options);
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public HitTestCode HitTestBackground(IDeviceContext dc, Rectangle backgroundRectangle, IntPtr hRgn, Point pt, HitTestOptions options)
    {
        ArgumentNullException.ThrowIfNull(dc);

#if LIBREWINFORMS_PORTABLE
        if (hRgn != IntPtr.Zero)
        {
            throw new PlatformNotSupportedException(
                "Portable visual-style hit testing does not accept a native HRGN. Use the managed Region overload.");
        }

        _lastHResult = default;
        return GetHitTestCode(PortableVisualStyles.HitTestBackground(
            GetPortableGraphics(dc),
            Class,
            Part,
            State,
            backgroundRectangle,
            region: null,
            pt,
            GetPortableHitTestOptions(options)));
#else
        using DeviceContextHdcScope hdc = dc.ToHdcScope();
        _lastHResult = PInvoke.HitTestThemeBackground(
            HTHEME,
            hdc,
            Part,
            State,
            (HIT_TEST_BACKGROUND_OPTIONS)options,
            backgroundRectangle,
            (HRGN)hRgn,
            pt,
            out ushort code);

        return (HitTestCode)code;
#endif
    }

    /// <summary>
    ///  [See win32 equivalent.]
    /// </summary>
    public bool IsBackgroundPartiallyTransparent()
    {
#if LIBREWINFORMS_PORTABLE
        _lastHResult = default;
        return PortableVisualStyles.IsBackgroundPartiallyTransparent(Class, Part, State);
#else
        return PInvoke.IsThemeBackgroundPartiallyTransparent(HTHEME, Part, State);
#endif
    }

    /// <summary>
    ///  This is similar to GetLastError in Win32. It returns the last HRESULT returned from a native call
    ///  into theme apis. We eat the errors and let the user handle any errors that occurred.
    /// </summary>
    public int LastHResult => (int)_lastHResult;

#if !LIBREWINFORMS_PORTABLE
    /// <summary>
    ///  Handles the ThemeChanged event. Basically, we need to ensure all per-thread theme handle
    ///  caches are refreshed.
    /// </summary>
    private static void OnUserPreferenceChanging(object sender, UserPreferenceChangingEventArgs ea)
    {
        if (ea.Category == UserPreferenceCategory.VisualStyle)
        {
            // Let all threads know their cached handles are no longer valid;
            // cache refresh will happen at next handle access.
            // Note that if the theme changes 2^sizeof(long) times before a thread uses
            // its handle, this whole version check won't work, but it is unlikely to happen.

            // this is not ideal.
            s_globalCacheVersion++;
        }
    }

    /// <summary>
    ///  Refreshes this thread's theme handle cache.
    /// </summary>
    private static void RefreshCache()
    {
        if (t_themeHandles is null)
        {
            return;
        }

        string[] classNames = new string[t_themeHandles.Keys.Count];
        t_themeHandles.Keys.CopyTo(classNames, 0);

        foreach (string className in classNames)
        {
            ThemeHandle? themeHandle = t_themeHandles[className];
            themeHandle?.Dispose();

            // We don't call IsSupported here, since that could cause RefreshCache to be called again,
            // leading to stack overflow.
            if (AreClientAreaVisualStylesSupported)
            {
                themeHandle = ThemeHandle.Create(className, false);
                if (themeHandle is not null)
                {
                    t_themeHandles[className] = themeHandle;
                }
            }
        }
    }

    private static HTHEME GetHandle(string className)
    {
        return GetHandle(className, true);
    }

    /// <summary>
    ///  Retrieves a theme handle for the given class from the handle cache. If its not
    ///  present in the cache, it creates a new object and stores it there.
    /// </summary>
    private static HTHEME GetHandle(string className, bool throwExceptionOnFail)
    {
        t_themeHandles ??= new(NumberOfPossibleClasses);
        if (t_threadCacheVersion != s_globalCacheVersion)
        {
            RefreshCache();
            t_threadCacheVersion = s_globalCacheVersion;
        }

        if (!t_themeHandles.TryGetValue(className, out ThemeHandle? themeHandle))
        {
            // See if it is already in cache
            themeHandle = ThemeHandle.Create(className, throwExceptionOnFail);
            if (themeHandle is null)
            {
                return HTHEME.Null;
            }

            t_themeHandles[className] = themeHandle;
        }

        return themeHandle.Handle;
    }
#endif

    private static PInvoke.OpenThemeDataScope OpenThemeData(HWND hwnd, string classList)
    {
        PInvoke.OpenThemeDataScope htheme = new(hwnd, classList);
        return htheme.IsNull ? throw new InvalidOperationException(SR.VisualStyleHandleCreationFailed) : htheme;
    }

#if !LIBREWINFORMS_PORTABLE
    // This wrapper class is needed for safely cleaning up TLS cache of handles.
    private class ThemeHandle : IDisposable, IHandle<HTHEME>
    {
        private ThemeHandle(HTHEME hTheme)
        {
            Handle = hTheme;
        }

        public HTHEME Handle { get; private set; }

        public static ThemeHandle? Create(string className, bool throwExceptionOnFail)
        {
            return Create(className, throwExceptionOnFail, HWND.Null);
        }

        internal static ThemeHandle? Create(string className, bool throwExceptionOnFail, HWND hWndRef)
        {
            // HThemes require an HWND when display scaling is different between monitors.
            HTHEME hTheme = PInvoke.OpenThemeData(hWndRef, className);

            return hTheme.IsNull
                ? throwExceptionOnFail ? throw new InvalidOperationException(SR.VisualStyleHandleCreationFailed) : null
                : new ThemeHandle(hTheme);
        }

        public void Dispose()
        {
            if (!Handle.IsNull)
            {
                PInvoke.CloseThemeData(Handle);
                Handle = HTHEME.Null;
            }

            GC.SuppressFinalize(this);
        }

        ~ThemeHandle() => Dispose();
    }
#endif
}
