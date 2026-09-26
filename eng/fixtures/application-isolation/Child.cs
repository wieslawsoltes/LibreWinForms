// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.ApplicationIsolation;
using LibreWinForms.Platform;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (Convert.ToHexString(typeof(Font).Assembly.GetName().GetPublicKeyToken()!) != "C29C9752855EE183")
            throw new InvalidOperationException("The child did not load canonical ProGPU Drawing.");
        if (!LibrePlatform.IsRegistered)
            throw new InvalidOperationException("The ordinary installed SDK did not register its actual backend.");
        try
        {
            switch (args[0])
            {
                case "verify":
                    VerifyControls(args[1]);
                    PortableApplication.Complete(true, args[1]);
                    return 0;
                case "decision":
                    PortableApplication.Complete(false, null);
                    return 0;
                case "nonzero":
                    PortableApplication.Complete(true, "not success");
                    return 23;
                case "missing":
                    return 0;
                case "malformed":
                    File.WriteAllText(ResultPath, "{\"version\":\"wrong type\"}");
                    return 0;
                case "oversized":
                    File.WriteAllBytes(ResultPath, new byte[32769]);
                    return 0;
                case "duplicate":
                    PortableApplication.Complete(true, "first");
                    try { PortableApplication.Complete(false, "overwrite"); }
                    catch (IOException) { return 0; }
                    throw new InvalidOperationException("A second completion overwrote the first result.");
                case "value-limit":
                    try { PortableApplication.Complete(true, new string('x', 4097)); }
                    catch (ArgumentOutOfRangeException)
                    {
                        PortableApplication.Complete(true, "bounded");
                        return 0;
                    }
                    throw new InvalidOperationException("The result text limit was not enforced.");
                case "wait":
                    Thread.Sleep(TimeSpan.FromSeconds(30));
                    return 0;
                default:
                    throw new ArgumentException("Unknown fixture mode.");
            }
        }
        finally
        {
            LibrePlatform.Current.Dispose();
        }
    }

    private static string ResultPath => Environment.GetEnvironmentVariable("LIBREWINFORMS_APPLICATION_RESULT_PATH")
        ?? throw new InvalidOperationException("Missing owned result location.");

    private static void VerifyControls(string text)
    {
        // Real shipped types and source state; no portable-shaped test doubles.
        using Font font = new(FontFamily.GenericSansSerif, 11.0f);
        using Bitmap image = new(2, 2);
        image.SetPixel(1, 0, Color.FromArgb(255, 17, 29, 43));
        using Control control = new() { Text = text, Font = font, BackgroundImage = image };
        if (control.Text != text || !ReferenceEquals(control.Font, font)
            || !ReferenceEquals(control.BackgroundImage, image) || image.GetPixel(1, 0).ToArgb() != Color.FromArgb(255, 17, 29, 43).ToArgb()
            || Control.DefaultFont is null)
            throw new InvalidOperationException("Canonical child control/drawing ownership did not remain intact.");
    }
}
