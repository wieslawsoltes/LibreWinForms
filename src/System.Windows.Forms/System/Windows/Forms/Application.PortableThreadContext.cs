// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;
using Microsoft.Office;

namespace System.Windows.Forms;

public sealed partial class Application
{
    /// <summary>Routes canonical WinForms loop semantics through the registered portable dispatcher.</summary>
    internal sealed class PortableThreadContext : ThreadContext
    {
        protected override bool? GetMessageLoopInternal(bool mustBeActive, int loopCount)
            => loopCount > 0 ? true : null;

        protected override bool RunMessageLoop(msoloop reason, bool fullModal)
        {
            _ = fullModal;
            ILibreDispatcher dispatcher = LibrePlatform.Current.Dispatcher;
            switch (reason)
            {
                case msoloop.Main:
                    dispatcher.Run(CancellationToken.None);
                    break;
                case msoloop.ModalAlert:
                case msoloop.ModalForm:
                    dispatcher.RunNested(
                        () => CurrentForm is { } form && !form.CheckCloseDialog(closingOnly: false),
                        CancellationToken.None);
                    break;
                case msoloop.DoEvents:
                case msoloop.DoEventsModal:
                    dispatcher.PumpOnce();
                    break;
                default:
                    throw new PlatformNotSupportedException($"Portable WinForms does not support message-loop reason {reason} yet.");
            }

            return true;
        }
    }
}
#endif
