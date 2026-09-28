// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.Design;
using LibreWinForms.Platform;
using ProGPU.Backend;

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
        WgpuContext? ownerContext = null;
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
        form.Paint += (_, _) =>
        {
            painted = true;
            ownerContext = WgpuContext.Current
                ?? throw new InvalidOperationException("Source paint has no current window rendering context.");
        };
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
                ValidateOwnedPopupWindows(form, ownerContext
                    ?? throw new InvalidOperationException("The owner has not painted before popup validation."));
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

    private static unsafe void ValidateOwnedPopupWindows(Form owner, WgpuContext ownerContext)
    {
        using ContextMenuStrip menu = new() { AutoClose = false };
        ToolStripMenuItem more = new("More");
        more.DropDownItems.Add("Nested command");
        more.DropDown.AutoClose = false;
        menu.Items.Add("Open");
        menu.Items.Add(more);
        bool rootPainted = false;
        bool childPainted = false;
        WgpuContext? rootContext = null;
        WgpuContext? childContext = null;
        int closed = 0;
        menu.Paint += (_, _) => { rootPainted = true; rootContext = WgpuContext.Current; };
        more.DropDown.Paint += (_, _) => { childPainted = true; childContext = WgpuContext.Current; };
        menu.Closed += (_, _) => closed++;
        more.DropDown.Closed += (_, _) => closed++;
        // Canonical dropdown handles can exist before source owner binding.
        // Native/input setup must not allocate independent renderer devices.
        WgpuContext[] existingContexts = WgpuContext.ActiveContexts.ToArray();
        int initialContexts = existingContexts.Length;
        _ = menu.Handle;
        _ = more.DropDown.Handle;
        if (WgpuContext.ActiveContexts.Count != initialContexts)
            throw new InvalidOperationException("Hidden unowned popup handles eagerly created rendering contexts.");

        // An explicit Graphics request before owner binding still works and
        // owns its standalone device until that native handle is retired.
        using (ContextMenuStrip unowned = new())
        {
            _ = unowned.Handle;
            if (WgpuContext.ActiveContexts.Count != initialContexts)
                throw new InvalidOperationException("Precreated popup unexpectedly initialized its renderer.");
            using Graphics graphics = unowned.CreateGraphics();
            WgpuContext[] newContexts = WgpuContext.ActiveContexts.Except(existingContexts).ToArray();
            if (WgpuContext.ActiveContexts.Count != initialContexts + 1
                || newContexts.Length != 1 || newContexts[0].Device == ownerContext.Device)
                throw new InvalidOperationException("Pre-owner Graphics did not initialize an independent renderer.");
        }
        if (WgpuContext.ActiveContexts.Count != initialContexts)
            throw new InvalidOperationException("Pre-owner Graphics left its rendering context alive after popup disposal.");

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
        VerifySharedPopupContext(ownerContext, rootContext);
        VerifySharedPopupContext(ownerContext, childContext);
        if (ReferenceEquals(rootContext, childContext) || rootContext!.Surface == childContext!.Surface
            || WgpuContext.ActiveContexts.Count != initialContexts + 2)
            throw new InvalidOperationException("Root and submenu did not retain independent rendering surfaces.");

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

        if (!rootContext.IsDisposed || !childContext.IsDisposed || ownerContext.IsDisposed
            || WgpuContext.ActiveContexts.Count != initialContexts)
            throw new InvalidOperationException("Popup retirement did not release its contexts independently of the owner.");
        owner.Show();
        owner.Refresh();
        if (ownerContext.IsDisposed || !ownerContext.IsInitialized)
            throw new InvalidOperationException("The owner cannot render after its shared popup surfaces retire.");

        Console.WriteLine("Installed canonical popup windows passed: deferred precreation, pre-owner Graphics, shared owner device, independent root/submenu surfaces, source paint, owner-hide release and owner repaint.");
    }

    private static unsafe void VerifySharedPopupContext(WgpuContext owner, WgpuContext? popup)
    {
        if (popup is null || ReferenceEquals(owner, popup) || popup.IsDisposed || !popup.IsInitialized
            || popup.Device != owner.Device || popup.Queue != owner.Queue
            || popup.Surface == null || popup.Surface == owner.Surface
            || !ReferenceEquals(popup.RenderLock, owner.RenderLock)
            || popup.SelectedDx12ShaderCompiler != owner.SelectedDx12ShaderCompiler
            || popup.ComputeLimits != owner.ComputeLimits)
            throw new InvalidOperationException("The popup did not inherit its live owner's device while retaining its own surface.");
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
