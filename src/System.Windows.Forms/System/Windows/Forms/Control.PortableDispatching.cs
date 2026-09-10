// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public unsafe partial class Control
{
    private readonly ILibreDispatcher _portableDispatcher =
        Application.ThreadContext.FromCurrent().Dispatcher;

    internal ILibreDispatcher PortableDispatcher => _portableDispatcher;
}
#endif
