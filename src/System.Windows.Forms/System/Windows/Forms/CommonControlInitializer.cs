// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms;

/// <summary>
///  Initializes native Windows common-control classes when the Win32 backend is active.
///  Portable backends create and paint the corresponding managed controls themselves.
/// </summary>
internal static class CommonControlInitializer
{
    internal static void Initialize()
    {
#if !LIBREWINFORMS_PORTABLE
        PInvoke.InitCommonControls();
#endif
    }

    internal static unsafe void Initialize(INITCOMMONCONTROLSEX_ICC classes)
    {
#if !LIBREWINFORMS_PORTABLE
        PInvoke.InitCommonControlsEx(new INITCOMMONCONTROLSEX
        {
            dwSize = (uint)sizeof(INITCOMMONCONTROLSEX),
            dwICC = classes
        });
#endif
    }
}
