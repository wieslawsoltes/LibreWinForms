// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("receipt, DPI mode, theme flag required");
        string output = Path.GetFullPath(args[0]);
        if (File.Exists(output)) throw new IOException("Receipt must be new.");
        HighDpiMode mode = Enum.Parse<HighDpiMode>(args[1]);
        bool themed = bool.Parse(args[2]);
        if (!Application.SetHighDpiMode(mode)) throw new InvalidOperationException("DPI policy rejected.");
        if (themed) Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string formsPath = typeof(Control).Assembly.Location;
        if (!formsPath.Contains("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Not the original Microsoft WinForms reference.");

        var metrics = new List<object>();
        foreach (uint dpi in new uint[] { 96, 120, 144, 192, 240, 288 })
        {
            RECT single = default, edge = default;
            Require(AdjustWindowRectExForDpi(ref single, 0x40800000, false, 0, dpi));
            Require(AdjustWindowRectExForDpi(ref edge, 0x40000000, false, 0x200, dpi));
            metrics.Add(new {
                dpi, border = new[] { GetSystemMetricsForDpi(5, dpi), GetSystemMetricsForDpi(6, dpi) },
                edge = new[] { GetSystemMetricsForDpi(45, dpi), GetSystemMetricsForDpi(46, dpi) },
                managedBorder = SystemInformation.GetBorderSizeForDpi((int)dpi),
                singleAdjust = Rect(single), edgeAdjust = Rect(edge)
            });
        }

        using Form form = new() { AutoScaleMode = AutoScaleMode.None, Location = new Point(100, 100),
            ClientSize = new Size(400, 300), ShowInTaskbar = false };
        _ = form.Handle;
        var controls = new List<object>();
        foreach (BorderStyle border in Enum.GetValues<BorderStyle>())
        foreach (bool multiline in new[] { false, true })
        {
            using TextBoxProbe editor = new() { AutoSize = false, BorderStyle = border, Multiline = multiline,
                Bounds = new Rectangle(10, 20, 120, 40), Text = "Alice gypq", Font = SystemFonts.DefaultFont };
            form.Controls.Add(editor);
            var beforeHandle = new { editor.IsHandleCreated, editor.ClientSize, scaled = editor.ScaledBounds() };
            nint hwnd = editor.Handle;
            Require(GetWindowRect(hwnd, out RECT window));
            Require(GetClientRect(hwnd, out RECT client));
            Point origin = Point.Empty;
            Require(ClientToScreen(hwnd, ref origin));
            RECT format = default;
            SendMessageRect(hwnd, 0x00B2, 0, ref format);
            int margins = (int)SendMessageW(hwnd, 0x00D4, 0, 0);
            var afterHandle = new { editor.ClientSize, scaled = editor.ScaledBounds() };
            var hits = new List<object>();
            foreach (Point point in new[] {
                new Point(window.left, window.top),
                new Point(window.left, (window.top + window.bottom) / 2),
                new Point(window.right - 1, (window.top + window.bottom) / 2),
                new Point((window.left + window.right) / 2, window.top),
                new Point((window.left + window.right) / 2, window.bottom - 1),
                origin, new Point(origin.X + 10, origin.Y + 10)
            })
            {
                hits.Add(new { point, result = (int)SendMessageW(hwnd, 0x0084, 0, Pack(point)) });
            }
            var originalBounds = editor.Bounds;
            var originalClient = editor.ClientSize;
            editor.ClientSize = new Size(70, 18);
            Require(GetClientRect(hwnd, out RECT requestedClient));
            var afterClientSize = new { editor.Bounds, editor.ClientSize, actual = Rect(requestedClient) };
            controls.Add(new { beforeHandle, afterHandle, afterClientSize, border = border.ToString(), multiline, dpi = editor.DeviceDpi,
                Bounds = originalBounds, ClientSize = originalClient, window = Rect(window), client = Rect(client),
                origin, sourceOrigin = editor.PointToScreen(Point.Empty), format = Rect(format),
                margins = new[] { margins & 0xffff, (margins >> 16) & 0xffff },
                firstCharacter = editor.GetPositionFromCharIndex(0),
                preferredHeight = editor.PreferredHeight, preferredSize = editor.GetPreferredSize(Size.Empty), hits });
        }

        var pixels = new List<object>();
        foreach (Border3DStyle style in Enum.GetValues<Border3DStyle>())
        {
            using Bitmap bitmap = new(8, 8, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Magenta);
                ControlPaint.DrawBorder3D(graphics, new Rectangle(2, 2, 4, 4), style, Border3DSide.All);
            }
            int[] values = Enumerable.Range(0, 64).Select(i => bitmap.GetPixel(i % 8, i / 8).ToArgb()).ToArray();
            pixels.Add(new { style = style.ToString(), width = 8, height = 8, argb = values });
        }
        var receipt = new { completed = true, desktopQualified = false,
            framework = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            forms = typeof(Control).Assembly.FullName, formsPath, mode = mode.ToString(), themed,
            metrics, controls, pixels,
            colors = new { dark = SystemColors.ControlDark.ToArgb(), darkDark = SystemColors.ControlDarkDark.ToArgb(),
                light = SystemColors.ControlLight.ToArgb(), lightLight = SystemColors.ControlLightLight.ToArgb(),
                control = SystemColors.Control.ToArgb(), windowFrame = SystemColors.WindowFrame.ToArgb() } };
        string json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true });
        using (var writer = new StreamWriter(File.Open(output, FileMode.CreateNew, FileAccess.Write))) writer.Write(json);
        Console.WriteLine($"{mode}/{themed} completed: {output}");
        return 0;
    }

    private sealed class TextBoxProbe : TextBox
    {
        internal Rectangle ScaledBounds() => GetScaledBounds(Bounds, new SizeF(2, 2), BoundsSpecified.All);
    }
    private static int[] Rect(RECT rect) => new[] { rect.left, rect.top, rect.right, rect.bottom };
    private static nint Pack(Point p) => unchecked((nint)((uint)(ushort)p.X | (uint)(ushort)p.Y << 16));
    private static void Require(bool value) { if (!value) throw new System.ComponentModel.Win32Exception(); }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [DllImport("user32", SetLastError = true)] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32", SetLastError = true)] private static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32", SetLastError = true)] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32", SetLastError = true)] private static extern bool AdjustWindowRectExForDpi(ref RECT rect, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32", EntryPoint = "SendMessageW")] private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32", EntryPoint = "SendMessageW")] private static extern nint SendMessageRect(nint hwnd, uint message, nint wParam, ref RECT rect);
}
