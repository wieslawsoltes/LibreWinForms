// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.Design;
using LibreWinForms.Platform;

namespace LibreWinForms.Sdk.SourceFirstVisibleSmoke;

internal static class Program
{
    private const int WatchdogExitCode = 124;

    [STAThread]
    private static int Main()
    {
        TimeSpan watchdogTimeout = OperatingSystem.IsWindows()
            ? TimeSpan.FromSeconds(120)
            : TimeSpan.FromSeconds(60);
        using System.Threading.Timer watchdog = new(
            static _ => Environment.Exit(WatchdogExitCode),
            state: null,
            dueTime: watchdogTimeout,
            period: Timeout.InfiniteTimeSpan);

        if (!LibrePlatform.IsRegistered)
        {
            throw new InvalidOperationException("The installed SDK did not register the ProGPU platform backend.");
        }

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();

        using UserControl designRoot = new();
        IDesigner rootDesigner = TypeDescriptor.CreateDesigner(designRoot, typeof(IRootDesigner))
            ?? throw new InvalidOperationException("The canonical UserControl root designer could not be resolved.");
        if (rootDesigner is not IRootDesigner)
        {
            throw new InvalidOperationException(
                $"The canonical UserControl designer is not a root designer ({rootDesigner.GetType().FullName}).");
        }

        bool shown = false;
        bool painted = false;
        using ProgressBar progressBar = new()
        {
            Location = new Point(24, 72),
            Size = new Size(240, 24),
            Value = 50
        };

        using Form form = new()
        {
            ClientSize = new Size(480, 320),
            Text = "Canonical LibreWinForms package smoke"
        };
        form.Controls.Add(new Button
        {
            AutoSize = true,
            Location = new Point(24, 24),
            Text = "Source-built System.Windows.Forms"
        });
        form.Controls.Add(progressBar);
        form.Paint += (_, _) => painted = true;
        form.Shown += (_, _) =>
        {
            shown = true;
            if (!progressBar.IsHandleCreated)
            {
                throw new InvalidOperationException("The portable ProgressBar did not create its managed handle.");
            }

            form.Invalidate();
            form.Update();
            // Let the visible lifecycle return through the platform dispatcher.
            // Closing synchronously inside Shown can race native activation on a
            // cold hosted Windows runner and leave Application.Run waiting.
            _ = form.BeginInvoke((Action)(() =>
            {
                ValidateOwnedPopupWindows(form);
                form.Close();
            }));
        };

        try
        {
            Application.Run(form);

            if (!shown || !painted)
            {
                throw new InvalidOperationException(
                    $"The package-mode form did not complete its visible lifecycle (shown={shown}, painted={painted}).");
            }

            Console.WriteLine($"Visible canonical package smoke passed on {Environment.OSVersion.Platform}.");
            return 0;
        }
        finally
        {
            LibrePlatform.Current.Dispose();
        }
    }

    private static void ValidateOwnedPopupWindows(Form owner)
    {
        using ContextMenuStrip menu = new() { AutoClose = false };
        ToolStripMenuItem more = new("More");
        more.DropDownItems.Add("Nested command");
        more.DropDown.AutoClose = false;
        menu.Items.Add("Open");
        menu.Items.Add(more);
        bool rootPainted = false;
        bool childPainted = false;
        int closed = 0;
        menu.Paint += (_, _) => rootPainted = true;
        more.DropDown.Paint += (_, _) => childPainted = true;
        menu.Closed += (_, _) => closed++;
        more.DropDown.Closed += (_, _) => closed++;
        menu.Show(owner, new Point(24, 112));
        more.ShowDropDown();
        LibreHandle ownerHandle = new(owner.Handle, LibreHandleKind.Window);
        LibreHandle rootHandle = VerifyPopup(menu, ownerHandle);
        LibreHandle childHandle = VerifyPopup(more.DropDown, ownerHandle);
        if (rootHandle == childHandle)
            throw new InvalidOperationException("Root and submenu must own independent platform windows.");

        menu.Refresh();
        more.DropDown.Refresh();
        if (!rootPainted || !childPainted)
            throw new InvalidOperationException($"Native popup source paint did not execute (root={rootPainted}, child={childPainted}).");

        // Persistent menus do not auto-close. Native owner loss must still
        // release both surfaces while retaining their application-owned source.
        owner.Hide();
        if (menu.Visible || more.DropDown.Visible || menu.IsHandleCreated || more.DropDown.IsHandleCreated
            || menu.IsDisposed || more.DropDown.IsDisposed || closed != 2
            || LibrePlatform.Current.Handles.TryGet<ILibreWindow>(rootHandle, out _)
            || LibrePlatform.Current.Handles.TryGet<ILibreWindow>(childHandle, out _))
        {
            throw new InvalidOperationException($"Owner hide did not retire both persistent native popup windows (closed={closed}).");
        }

        Console.WriteLine("Installed canonical popup windows passed: independent owned root/submenu, source paint, owner-hide release.");
    }

    private static LibreHandle VerifyPopup(ToolStripDropDown popup, LibreHandle owner)
    {
        if (!popup.Visible || !popup.IsHandleCreated)
            throw new InvalidOperationException("The canonical popup is not visible with a live handle.");
        LibreHandle handle = new(popup.Handle, LibreHandleKind.Window);
        if (handle == owner || !LibrePlatform.Current.Handles.TryGet(handle, out ILibreWindow? window)
            || !window.Visible || window.Owner != owner || window.Border != LibreWindowBorder.Hidden
            || window.ShowInTaskbar || window.Bounds.Width <= 0 || window.Bounds.Height <= 0)
        {
            throw new InvalidOperationException("The source popup did not resolve to a distinct visible owned platform window.");
        }

        return handle;
    }
}
