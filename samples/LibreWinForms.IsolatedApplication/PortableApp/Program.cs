// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.ApplicationIsolation;
using LibreWinForms.Platform;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool accepted = false;
        string? value = null;
        try
        {
            using Form form = new() { Text = "Isolated portable application", ClientSize = new Size(400, 130) };
            using TextBox editor = new() { Text = args.FirstOrDefault() ?? "Child-owned text", Width = 350, Location = new Point(20, 20) };
            using Button accept = new() { Text = "Return text", Location = new Point(20, 65), AutoSize = true };
            accept.Click += (_, _) => { accepted = true; value = editor.Text; form.Close(); };
            form.Controls.Add(editor);
            form.Controls.Add(accept);
            Application.Run(form);
        }
        finally
        {
            LibrePlatform.Current.Dispose();
        }

        // No Font, Image, Control, HWND or delegate leaves this process.
        PortableApplication.Complete(accepted, value);
    }
}
