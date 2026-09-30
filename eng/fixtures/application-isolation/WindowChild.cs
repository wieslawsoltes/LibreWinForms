// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.ApplicationIsolation;
using LibreWinForms.Platform;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1 || !LibrePlatform.IsRegistered)
            throw new InvalidOperationException("The ordinary installed SDK and primitive argument are required.");
        if (Convert.ToHexString(typeof(Font).Assembly.GetName().GetPublicKeyToken()!) != "C29C9752855EE183")
            throw new InvalidOperationException("The child must own canonical ProGPU Drawing.");

        // Match the existing installed-package visible smoke's platform deadlines.
        TimeSpan timeout = TimeSpan.FromSeconds(OperatingSystem.IsWindows() ? 120 : 60);
        using System.Threading.Timer watchdog = new(_ => Environment.Exit(124), null, timeout, Timeout.InfiniteTimeSpan);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        string text = args[0];
        try
        {
            VerifyWindow(text);
        }
        finally
        {
            LibrePlatform.Current.Dispose();
        }

        // Publish only after normal closure and disposal, never a UI/Drawing object.
        PortableApplication.Complete(true, text);
    }

    private static void VerifyWindow(string text)
    {
        // Exercise the three system-font getters from issue #23 inside the child.
        using Font defaultFont = SystemFonts.DefaultFont;
        using Font menuFont = SystemFonts.MenuFont;
        using Font messageFont = SystemFonts.MessageBoxFont;
        using Bitmap image = new(2, 2);
        Color ink = Color.FromArgb(255, 17, 29, 43);
        image.SetPixel(1, 0, ink);
        using Form form = new() { Text = "Isolated portable window" };
        form.SuspendLayout();
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ClientSize = new Size(520, 220);
        form.StartPosition = FormStartPosition.CenterScreen;
        Font[] fonts = [defaultFont, menuFont, messageFont];
        int[] paints = new int[fonts.Length];
        Label[] labels = new Label[fonts.Length];
        for (int index = 0; index < fonts.Length; index++)
        {
            int capturedIndex = index;
            labels[index] = new Label
            {
                Text = text,
                Font = fonts[index],
                Location = new Point(20, 20 + index * 45),
                Size = new Size(460, 32)
            };
            labels[index].Paint += (_, _) => paints[capturedIndex]++;
            form.Controls.Add(labels[index]);
        }

        PictureBox picture = new()
        {
            Image = image,
            Location = new Point(20, 160),
            Size = new Size(32, 32),
            SizeMode = PictureBoxSizeMode.StretchImage
        };
        int imagePaints = 0;
        picture.Paint += (_, _) => imagePaints++;
        form.Controls.Add(picture);
        form.ResumeLayout(false);
        form.PerformLayout();
        bool shown = false;
        bool closed = false;
        int formPaints = 0;
        form.Paint += (_, _) => formPaints++;
        form.FormClosed += (_, _) => closed = true;
        form.Shown += (_, _) =>
        {
            shown = true;
            form.Invalidate(true);
            form.Update();
            // Let the native visible lifecycle return through its dispatcher before closing.
            _ = form.BeginInvoke((Action)(() =>
            {
                if (!form.IsHandleCreated || !form.Visible || formPaints == 0 || imagePaints == 0
                    || paints.Any(count => count == 0) || labels.Any(label => !label.IsHandleCreated)
                    || labels.Where((label, index) => !ReferenceEquals(label.Font, fonts[index]) || label.Text != text).Any()
                    || !ReferenceEquals(picture.Image, image) || image.GetPixel(1, 0).ToArgb() != ink.ToArgb())
                    throw new InvalidOperationException("The child did not paint its actual source-owned fonts and image.");
                Console.WriteLine($"Isolated window source paint: form={formPaints}, labels={string.Join(',', paints)}, image={imagePaints}, dpi={form.DeviceDpi}");
                form.Close();
            }));
        };

        Application.Run(form);
        if (!shown || !closed || !form.IsDisposed)
            throw new InvalidOperationException("The isolated window did not close normally.");
        // Paint/closure is a native-window lifecycle contract, not pixel or input parity.
    }
}
