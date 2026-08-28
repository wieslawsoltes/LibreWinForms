// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.Sdk.SourceFirstSmoke;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!LibrePlatform.IsRegistered)
        {
            throw new InvalidOperationException("The source-first SDK did not register the ProGPU platform backend.");
        }

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        ApplicationConfiguration.Initialize();

        using Bitmap bitmap = new(64, 64);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (Pen pen = new(Color.Black))
        {
            graphics.DrawCurve(
                pen,
                [new Point(0, 0), new Point(16, 32), new Point(48, 16), new Point(63, 63)]);
        }

        using Form form = new()
        {
            ClientSize = new Size(320, 180),
            Text = "Canonical LibreWinForms SDK smoke"
        };
        form.Controls.Add(new Button { Text = "Source-built", AutoSize = true });

        LibrePlatform.Current.Dispose();
        return 0;
    }
}
