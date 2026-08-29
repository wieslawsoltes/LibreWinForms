using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using System.Windows.Forms.Integration;

namespace LibreWinForms.CanonicalWfiSmoke;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ProGpuPlatform.Register();
        WindowsFormsHost.EnableWindowsFormsInterop();

        RequireAssembly(typeof(System.Windows.Forms.Application), "System.Windows.Forms");
        RequireAssembly(typeof(WindowsFormsHost), "WindowsFormsIntegration");
        RequireAssembly(typeof(ProGpuPlatform), "LibreWinForms.ProGPU");

        using var panel = new System.Windows.Forms.Panel();
        panel.Controls.Add(new System.Windows.Forms.Button { Text = "Canonical WinForms" });

        var host = new WindowsFormsHost { Child = panel };
        if (!ReferenceEquals(host.Child, panel))
        {
            throw new InvalidOperationException("Canonical WindowsFormsHost did not retain its WinForms child.");
        }

        host.Child = null;
        Console.WriteLine("Canonical Forms, ProGPU backend, and source-built WindowsFormsIntegration loaded without the compatibility runtime.");
        LibrePlatform.Current.Dispose();
    }

    private static void RequireAssembly(Type type, string expectedName)
    {
        string? actualName = type.Assembly.GetName().Name;
        if (!string.Equals(actualName, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected {type.FullName} from {expectedName}, but resolved {actualName}.");
        }
    }
}
