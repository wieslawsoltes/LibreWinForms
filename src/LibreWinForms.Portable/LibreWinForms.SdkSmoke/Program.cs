using System;
using System.Linq;
using System.Threading;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SdkSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool runSmoke = args.Contains("--run-form", StringComparer.Ordinal);
        if (!runSmoke)
        {
            Console.WriteLine("LibreWinForms SDK smoke build loaded.");
            return 0;
        }

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
}
