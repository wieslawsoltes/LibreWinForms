// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
///  Owns a <see cref="Graphics"/> suitable for managed layout and measurement.
/// </summary>
/// <remarks>
///  <para>
///   Windows keeps the upstream cached screen-device-context behavior. Portable builds use an
///   in-memory ProGPU surface so layout never requires a native display device context.
///  </para>
/// </remarks>
internal ref struct LayoutGraphicsScope
{
#if LIBREWINFORMS_PORTABLE
    private Bitmap? _surface;
#else
    private readonly GdiCache.ScreenGraphicsScope _screen;
#endif

    public Graphics Graphics { get; }

    public LayoutGraphicsScope()
    {
#if LIBREWINFORMS_PORTABLE
        _surface = new Bitmap(1, 1);
        try
        {
            Graphics = Graphics.FromImage(_surface);
        }
        catch
        {
            _surface.Dispose();
            _surface = null;
            throw;
        }
#else
        _screen = GdiCache.GetScreenDCGraphics();
        Graphics = _screen.Graphics;
#endif
    }

#if LIBREWINFORMS_PORTABLE
    public void Dispose()
#else
    public readonly void Dispose()
#endif
    {
#if LIBREWINFORMS_PORTABLE
        Graphics.Dispose();
        _surface?.Dispose();
        _surface = null;
#else
        _screen.Dispose();
#endif
    }
}
