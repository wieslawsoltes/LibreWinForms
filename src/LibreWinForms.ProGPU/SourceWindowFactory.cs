// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using ProGPU.Backend;
using Silk.NET.Windowing;

namespace LibreWinForms.ProGPU;

internal static class SourceWindowFactory
{
    internal static bool UsesOwnedCocoaPopup(bool isMacOS, LibreWindowOptions options)
        => isMacOS && options.HasFlag(LibreWindowOptions.Popup);

    internal static IWindow Create(bool ownedCocoaPopup, WindowOptions options, Action wakeHost)
        => Create(ownedCocoaPopup, options, wakeHost,
            static options => Silk.NET.Windowing.Window.Create(options),
            static (options, wake) => NativePopupWindow.CreateCocoaPopupWindow(options, wake));

    // Typed factory seam: tests record the actual selected options without
    // creating native windows, input contexts or GPU devices.
    internal static IWindow Create(bool ownedCocoaPopup, WindowOptions options, Action wakeHost,
        Func<WindowOptions, IWindow> ordinaryFactory,
        Func<WindowOptions, Action, IWindow> ownedFactory)
    {
        ArgumentNullException.ThrowIfNull(wakeHost);
        if (!ownedCocoaPopup)
            return ordinaryFactory(options);

        // These fields belong to the source host, not the native popup. Source
        // title metadata stays in SilkLibreWindow; display/owner binding happens
        // only through NativePopupAdmission after hidden creation.
        options = options with
        {
            IsVisible = false,
            Title = string.Empty,
            API = GraphicsAPI.None,
            IsContextControlDisabled = true,
            WindowState = WindowState.Normal,
            WindowBorder = WindowBorder.Hidden,
        };
        return ownedFactory(options, wakeHost);
    }
}
