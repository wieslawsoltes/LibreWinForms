// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms;

public sealed partial class Application
{
    internal static readonly Type s_typeOfModalApplicationContext = typeof(ModalApplicationContext);

    private class ModalApplicationContext : ApplicationContext
    {
        private ThreadContext? _parentWindowContext;

        private delegate void ThreadWindowCallback(ThreadContext context, bool onlyWinForms);

        public ModalApplicationContext(Form modalForm) : base(modalForm)
        {
        }

        public void DisableThreadWindows(bool disable, bool onlyWinForms)
        {
            Control? parentControl = null;

            // Get ahold of the parent HWND -- if it's a different thread we need to do the disable
            // over there too. Note we only do this if we're parented by a Windows Forms parent.

            if (MainForm is not null && MainForm.IsHandleCreated)
            {
#if LIBREWINFORMS_PORTABLE
                // The registered portable backend owns one UI dispatcher. Same-dispatcher owners are
                // already covered by ThreadWindows; retain the managed owner here so a future
                // multi-dispatcher backend can marshal the matching callback without inspecting HWNDs.
                parentControl = MainForm.OwnerInternal;
                _parentWindowContext = null;
#else
                // Get ahold of the parenting control
                HWND parentHandle = (HWND)PInvokeCore.GetWindowLong(MainForm, WINDOW_LONG_PTR_INDEX.GWL_HWNDPARENT);

                parentControl = Control.FromHandle(parentHandle);

                _parentWindowContext = parentControl is not null && parentControl.InvokeRequired
                    ? GetContextForHandle(parentControl)
                    : null;
#endif
            }

            // If we got a thread context, that means our parent is in a different thread, make the call on that thread.
            if (_parentWindowContext is not null)
            {
                // In case we've already torn down, ask the context for this.
                parentControl ??= _parentWindowContext.ApplicationContext!.MainForm;

                parentControl!.Invoke(
                    disable ? new ThreadWindowCallback(DisableThreadWindowsCallback) : new ThreadWindowCallback(EnableThreadWindowsCallback),
                    [_parentWindowContext, onlyWinForms]);
            }
        }

        private void DisableThreadWindowsCallback(ThreadContext context, bool onlyWinForms)
            => context.DisableWindowsForModalLoop(onlyWinForms, this);

        private void EnableThreadWindowsCallback(ThreadContext context, bool onlyWinForms)
            => context.EnableWindowsForModalLoop(onlyWinForms, this);

        protected override void ExitThreadCore()
        {
            // do nothing... modal dialogs exit by setting dialog result
        }
    }
}
