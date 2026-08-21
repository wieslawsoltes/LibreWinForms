// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PROGPU_DRAWING
using Windows.Win32.System.Ole;

namespace System.Drawing;

/// <summary>
/// Explicit boundary for Windows-only System.Drawing interop that has no
/// portable ProGPU representation. Calls fail visibly until a Windows adapter
/// supplies the corresponding native handle conversion.
/// </summary>
internal static class ProGpuDrawingInteropExtensions
{
    public static LOGFONTW ToLogicalFont(this Font font) =>
        throw NativeInteropUnavailable("LOGFONT export");

    public static LOGFONTW ToLogicalFont(this Font font, Graphics graphics) =>
        throw NativeInteropUnavailable("LOGFONT export");

    public static object CreateIPictureRCW(this Image image) =>
        throw NativeInteropUnavailable("OLE IPicture export");

    public static IPictureDisp.Interface CreateIPictureDispRCW(this Image image) =>
        throw NativeInteropUnavailable("OLE IPictureDisp export");

    public static object CreateIPictureRCW(this Icon icon, bool copy) =>
        throw NativeInteropUnavailable("OLE IPicture export");

    public static PICTDESC CreatePICTDESC(this Icon icon, bool copy) =>
        throw NativeInteropUnavailable("OLE PICTDESC export");

    public static PICTDESC CreatePICTDESC(this Bitmap bitmap) =>
        throw NativeInteropUnavailable("OLE PICTDESC export");

    private static PlatformNotSupportedException NativeInteropUnavailable(string operation) =>
        new($"{operation} requires the explicit Windows System.Drawing adapter.");
}
#endif
