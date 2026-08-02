using System;
using System.ComponentModel;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class ApplicationCompatibilityBehaviorTests
{
    public static void Run()
    {
        Forms.Application.EnableVisualStyles();
        Assert(Forms.Application.RenderWithVisualStyles, "EnableVisualStyles did not enable visual-style state.");

        bool changed = Forms.Application.SetHighDpiMode(Forms.HighDpiMode.SystemAware);
        Assert(changed, "SetHighDpiMode rejected a valid portable DPI mode.");
        Assert(
            Forms.Application.HighDpiMode == Forms.HighDpiMode.SystemAware,
            "SetHighDpiMode did not retain the requested portable DPI mode.");

        bool rejectedInvalid = false;
        try
        {
            Forms.Application.SetHighDpiMode((Forms.HighDpiMode)int.MaxValue);
        }
        catch (InvalidEnumArgumentException)
        {
            rejectedInvalid = true;
        }

        Assert(rejectedInvalid, "SetHighDpiMode accepted an undefined mode.");
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);

        Console.WriteLine("LibreWinForms application compatibility tests passed: visualStyles=1 dpiMode=PerMonitorV2.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
