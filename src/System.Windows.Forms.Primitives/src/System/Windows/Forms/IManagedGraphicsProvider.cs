// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PROGPU_DRAWING
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
///  Exposes the original managed drawing frame without converting it to a Win32 graphics provider.
/// </summary>
internal interface IManagedGraphicsProvider
{
    /// <summary>
    ///  Borrows the event's graphics, retaining its existing lazy-creation and ownership policy.
    ///  The caller must not dispose the returned object.
    /// </summary>
    Graphics? GetGraphics(bool createIfNeeded);
}
#endif
