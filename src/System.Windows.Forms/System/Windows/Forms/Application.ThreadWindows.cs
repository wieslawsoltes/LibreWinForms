// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms;

public sealed partial class Application
{
    /// <summary>
    ///  This class enables or disables all windows in the current thread. We use this to disable other windows on the
    ///  thread when a modal dialog is to be shown. It can also be used to dispose all windows in a thread, which we do
    ///  before returning from a message loop.
    /// </summary>
    private sealed class ThreadWindows
    {
#if LIBREWINFORMS_PORTABLE
        private readonly List<Form> _windows;
        private Form? _activeForm;
#else
        private readonly List<HWND> _windows;
        private HWND _activeHwnd;
        private HWND _focusedHwnd;
#endif
        internal ThreadWindows? _previousThreadWindows;
        private readonly bool _onlyWinForms = true;

        internal ThreadWindows(bool onlyWinForms)
        {
#if LIBREWINFORMS_PORTABLE
            _windows = new List<Form>(Application.OpenForms.Count);
            _onlyWinForms = onlyWinForms;
            foreach (Form form in Application.OpenForms)
            {
                if (form.IsHandleCreated
                    && form.Visible
                    && form.Enabled
                    && form.PortableWindowEnabled
                    && !form.InvokeRequired)
                {
                    _windows.Add(form);
                }
            }
#else
            _windows = new List<HWND>(16);
            _onlyWinForms = onlyWinForms;
            PInvokeCore.EnumCurrentThreadWindows(Callback);
#endif
        }

#if !LIBREWINFORMS_PORTABLE
        private BOOL Callback(HWND hwnd)
        {
            // We only do visible and enabled windows. Also, we only do top level windows.
            // Finally, we only include windows that are DNA windows, since other MSO components
            // will be responsible for disabling their own windows.
            if (PInvoke.IsWindowVisible(hwnd) && PInvoke.IsWindowEnabled(hwnd))
            {
                if (!_onlyWinForms || Control.FromHandle(hwnd) is not null)
                {
                    _windows.Add(hwnd);
                }
            }

            return true;
        }
#endif

        // Disposes all top-level Controls on this thread
        internal void Dispose()
        {
#if LIBREWINFORMS_PORTABLE
            foreach (Form form in _windows)
            {
                if (!form.IsDisposed)
                {
                    form.Dispose();
                }
            }
#else
            foreach (HWND hwnd in _windows)
            {
                if (PInvoke.IsWindow(hwnd))
                {
                    Control.FromHandle(hwnd)?.Dispose();
                }
            }
#endif
        }

        // Enables/disables all top-level Controls on this thread
        internal void Enable(bool enable)
        {
#if LIBREWINFORMS_PORTABLE
            if (!_onlyWinForms && !enable)
            {
                _activeForm = Form.ActiveForm;
            }

            foreach (Form form in _windows)
            {
                if (!form.IsDisposed && form.IsHandleCreated)
                {
                    form.SetPortableWindowEnabled(enable);
                }
            }

            if (!_onlyWinForms && enable && _activeForm is { IsDisposed: false, Visible: true } activeForm)
            {
                activeForm.Activate();
            }
#else
            if (!_onlyWinForms && !enable)
            {
                _activeHwnd = PInvoke.GetActiveWindow();
                Control? activatingControl = ThreadContext.FromCurrent().ActivatingControl;
                _focusedHwnd = activatingControl is not null ? activatingControl.HWND : PInvoke.GetFocus();
            }

            foreach (HWND hwnd in _windows)
            {
                if (PInvoke.IsWindow(hwnd))
                {
                    PInvoke.EnableWindow(hwnd, enable);
                }
            }

            // OpenFileDialog is not returning the focus the way other dialogs do.
            // Important that we re-activate the old window when we are closing
            // our modal dialog.
            //
            // edit mode forever with Excel application
            // But, DON'T change other people's state when we're simply
            // responding to external MSOCM events about modality. When we are,
            // we are created with a TRUE for onlyWinForms.
            if (!_onlyWinForms && enable)
            {
                if (!_activeHwnd.IsNull && PInvoke.IsWindow(_activeHwnd))
                {
                    PInvoke.SetActiveWindow(_activeHwnd);
                }

                if (!_focusedHwnd.IsNull && PInvoke.IsWindow(_focusedHwnd))
                {
                    PInvoke.SetFocus(_focusedHwnd);
                }
            }
#endif
        }
    }
}
