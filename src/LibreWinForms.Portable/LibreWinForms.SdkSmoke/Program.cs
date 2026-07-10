using System;
using System.Linq;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfWindow = System.Windows.Window;

namespace LibreWinForms.SdkSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--run-form", StringComparer.Ordinal))
        {
            return RunMainFormSmoke();
        }

        if (args.Contains("--run-dialog", StringComparer.Ordinal))
        {
            return RunOwnedDialogSmoke();
        }

        Console.WriteLine("LibreWinForms SDK smoke build loaded.");
        return 0;
    }

    private static int RunMainFormSmoke()
    {
        bool shown = false;
        bool closed = false;

        var form = new Forms.Form
        {
            Name = "LibreWinFormsSdkSmoke",
            Text = "LibreWinForms SDK Smoke",
            Width = 320,
            Height = 180,
            StartPosition = Forms.FormStartPosition.CenterScreen
        };

        using var closeTimer = new Timer(_ => form.Close(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        form.Shown += (_, _) =>
        {
            shown = true;
            closeTimer.Change(TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
        };

        form.FormClosed += (_, _) => closed = true;

        Forms.Application.Run(form);

        if (!shown || !closed)
        {
            Console.Error.WriteLine($"LibreWinForms SDK smoke failed shown={shown} closed={closed}");
            return 2;
        }

        Console.WriteLine("LibreWinForms SDK smoke result=Success host=WPF formShown=True formClosed=True");
        return 0;
    }

    private static int RunOwnedDialogSmoke()
    {
        bool ownerLoaded = false;
        bool dialogShown = false;
        bool dialogClosed = false;
        bool ownerLinked = false;
        Forms.DialogResult dialogResult = Forms.DialogResult.None;

        var application = new WpfApplication();
        var ownerWindow = new WpfWindow
        {
            Title = "LibreWinForms SDK Dialog Owner",
            Width = 480,
            Height = 300
        };

        ownerWindow.Loaded += (_, _) =>
        {
            ownerLoaded = true;
            ownerWindow.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    var dialog = new Forms.Form
                    {
                        Name = "LibreWinFormsSdkOwnedDialog",
                        Text = "LibreWinForms SDK Owned Dialog",
                        Width = 340,
                        Height = 200,
                        StartPosition = Forms.FormStartPosition.CenterParent
                    };

                    var closeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    closeTimer.Tick += (_, _) =>
                    {
                        closeTimer.Stop();
                        dialog.DialogResult = Forms.DialogResult.OK;
                        dialog.Close();
                    };

                    dialog.Shown += (_, _) =>
                    {
                        dialogShown = true;
                        ownerLinked = WpfApplication.Current.Windows
                            .Cast<WpfWindow>()
                            .Any(window => !ReferenceEquals(window, ownerWindow)
                                && ReferenceEquals(window.Owner, ownerWindow));
                        closeTimer.Start();
                    };
                    dialog.FormClosed += (_, _) => dialogClosed = true;

                    dialogResult = dialog.ShowDialog(new WpfWindowOwner(ownerWindow));
                    closeTimer.Stop();
                    ownerWindow.Close();
                }),
                DispatcherPriority.ApplicationIdle);
        };

        application.Run(ownerWindow);

        if (!ownerLoaded || !dialogShown || !dialogClosed || !ownerLinked || dialogResult != Forms.DialogResult.OK)
        {
            Console.Error.WriteLine(
                $"LibreWinForms SDK owned dialog smoke failed ownerLoaded={ownerLoaded} dialogShown={dialogShown} " +
                $"dialogClosed={dialogClosed} ownerLinked={ownerLinked} result={dialogResult}");
            return 3;
        }

        Console.WriteLine(
            "LibreWinForms SDK owned dialog smoke result=Success host=WPF ownerLoaded=True " +
            "dialogShown=True dialogClosed=True ownerLinked=True result=OK");
        return 0;
    }

    private sealed class WpfWindowOwner : Forms.IWin32Window
    {
        private readonly WpfWindow _window;

        public WpfWindowOwner(WpfWindow window)
        {
            _window = window;
        }

        public IntPtr Handle => new WindowInteropHelper(_window).Handle;
    }
}
