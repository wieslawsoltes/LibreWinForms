// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.Sdk.SourceFirstVisibleSmoke;

internal static class Program
{
    private const int WatchdogExitCode = 124;

    [STAThread]
    private static int Main()
    {
        using System.Threading.Timer watchdog = new(
            static _ => Environment.Exit(WatchdogExitCode),
            state: null,
            dueTime: TimeSpan.FromSeconds(30),
            period: Timeout.InfiniteTimeSpan);

        if (!LibrePlatform.IsRegistered)
        {
            throw new InvalidOperationException("The installed SDK did not register the ProGPU platform backend.");
        }

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();

        bool shown = false;
        bool painted = false;

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
        form.Paint += (_, _) => painted = true;
        form.Shown += (_, _) =>
        {
            shown = true;
            form.Invalidate();
            form.Update();
            form.BeginInvoke((Action)form.Close);
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
}
