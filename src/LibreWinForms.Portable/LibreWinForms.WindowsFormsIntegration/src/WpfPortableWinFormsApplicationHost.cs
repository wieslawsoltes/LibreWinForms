using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace System.Windows.Forms.Integration;

internal sealed class WpfPortableWinFormsApplicationHost : Forms.IWinFormsApplicationHost
{
    private readonly object _gate = new();
    private readonly Dictionary<Forms.Form, Window> _windows = new();

    public static WpfPortableWinFormsApplicationHost Instance { get; } = new();

    public void Run(Forms.Form mainForm)
    {
        ArgumentNullException.ThrowIfNull(mainForm);

        WpfApplication? application = WpfApplication.Current;
        bool ownsApplication = application == null;
        application ??= new WpfApplication();

        Window window = CreateWindow(mainForm, owner: null, modal: false);
        if (ownsApplication)
        {
            application.Run(window);
        }
        else
        {
            window.ShowDialog();
        }
    }

    public Forms.DialogResult ShowDialog(Forms.Form form, Forms.IWin32Window? owner)
    {
        ArgumentNullException.ThrowIfNull(form);

        Window? ownerWindow = ResolveOwnerWindow(owner);

        Window window = CreateWindow(form, ownerWindow, modal: true);
        window.ShowDialog();
        return form.DialogResult;
    }

    private Window? ResolveOwnerWindow(Forms.IWin32Window? owner)
    {
        if (owner == null)
        {
            return null;
        }

        if (owner is Forms.Form ownerForm)
        {
            lock (_gate)
            {
                if (_windows.TryGetValue(ownerForm, out Window? ownerWindow))
                {
                    return ownerWindow;
                }
            }
        }

        IntPtr ownerHandle = owner.Handle;
        if (ownerHandle == IntPtr.Zero)
        {
            return null;
        }

        HwndSource? source = HwndSource.FromHwnd(ownerHandle);
        if (source?.RootVisual is Window sourceWindow)
        {
            return sourceWindow;
        }

        return source?.RootVisual is DependencyObject rootVisual
            ? Window.GetWindow(rootVisual)
            : null;
    }

    public void ExitThread()
    {
        WpfApplication? application = WpfApplication.Current;
        if (application == null)
        {
            return;
        }

        InvokeOnDispatcher(application.Dispatcher, application.Shutdown);
    }

    private Window CreateWindow(Forms.Form form, Window? owner, bool modal)
    {
        var host = new WindowsFormsHost
        {
            Child = form
        };

        var window = new Window
        {
            Title = GetWindowTitle(form),
            Width = GetInitialDimension(form.Width, 800),
            Height = GetInitialDimension(form.Height, 600),
            MinWidth = form.MinimumSize.Width > 0 ? form.MinimumSize.Width : 0,
            MinHeight = form.MinimumSize.Height > 0 ? form.MinimumSize.Height : 0,
            Content = host,
            ShowInTaskbar = form.ShowInTaskbar,
            ResizeMode = ToResizeMode(form),
            WindowStyle = form.FormBorderStyle == Forms.FormBorderStyle.None ? WindowStyle.None : WindowStyle.SingleBorderWindow,
            WindowStartupLocation = ToStartupLocation(form, owner, modal)
        };

        if (form.MaximumSize.Width > 0)
        {
            window.MaxWidth = form.MaximumSize.Width;
        }

        if (form.MaximumSize.Height > 0)
        {
            window.MaxHeight = form.MaximumSize.Height;
        }

        if (owner != null && modal)
        {
            window.Owner = owner;
        }

        window.WindowState = ToWindowState(form.WindowState);

        bool closingWindowFromForm = false;
        bool closingFormFromWindow = false;

        form.TextChanged += (_, _) => InvokeOnDispatcher(window.Dispatcher, () => window.Title = GetWindowTitle(form));
        form.FormClosed += (_, _) =>
        {
            lock (_gate)
            {
                _windows.Remove(form);
            }

            if (!closingFormFromWindow)
            {
                closingWindowFromForm = true;
                InvokeOnDispatcher(
                    window.Dispatcher,
                    () =>
                    {
                        if (window.IsVisible)
                        {
                            window.Close();
                        }
                    });
            }
        };

        window.Closing += (_, e) =>
        {
            if (closingWindowFromForm)
            {
                return;
            }

            closingFormFromWindow = true;
            if (!form.Close(Forms.CloseReason.UserClosing))
            {
                e.Cancel = true;
                closingFormFromWindow = false;
            }
        };

        window.Closed += (_, _) =>
        {
            lock (_gate)
            {
                _windows.Remove(form);
            }
        };

        lock (_gate)
        {
            _windows[form] = window;
        }

        window.Loaded += (_, _) => form.Show();
        return window;
    }

    private static void InvokeOnDispatcher(System.Windows.Threading.Dispatcher dispatcher, Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private static string GetWindowTitle(Forms.Form form)
    {
        return string.IsNullOrWhiteSpace(form.Text) ? form.Name : form.Text;
    }

    private static double GetInitialDimension(int value, int fallback)
    {
        return value > 0 ? value : fallback;
    }

    private static WindowStartupLocation ToStartupLocation(Forms.Form form, Window? owner, bool modal)
    {
        return form.StartPosition switch
        {
            Forms.FormStartPosition.CenterScreen => WindowStartupLocation.CenterScreen,
            Forms.FormStartPosition.CenterParent when owner != null && modal => WindowStartupLocation.CenterOwner,
            _ => WindowStartupLocation.Manual
        };
    }

    private static WindowState ToWindowState(Forms.FormWindowState state)
    {
        return state switch
        {
            Forms.FormWindowState.Minimized => WindowState.Minimized,
            Forms.FormWindowState.Maximized => WindowState.Maximized,
            _ => WindowState.Normal
        };
    }

    private static ResizeMode ToResizeMode(Forms.Form form)
    {
        if (!form.ControlBox)
        {
            return ResizeMode.NoResize;
        }

        return form.FormBorderStyle switch
        {
            Forms.FormBorderStyle.FixedSingle or
            Forms.FormBorderStyle.Fixed3D or
            Forms.FormBorderStyle.FixedDialog or
            Forms.FormBorderStyle.FixedToolWindow => form.MinimizeBox || form.MaximizeBox ? ResizeMode.CanMinimize : ResizeMode.NoResize,
            _ => ResizeMode.CanResize
        };
    }
}
