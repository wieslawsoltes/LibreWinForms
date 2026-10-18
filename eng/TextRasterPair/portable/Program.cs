using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibreWinForms.ProGPU;
using LibreWinForms.Platform;
using ProGPU.Text;

internal static class Program
{
    private const string Text = "Name Age 123 Hello Windows Forms";
    private const int Width = 384, Height = 64;

    [STAThread]
    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Run(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1) || args.Length != 2 ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
            throw new InvalidOperationException("Disposable hosted Windows only; no input or desktop windows.");
        string root = Path.GetFullPath(args[0]);
        string temporary = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP")!);
        if (root != args[0] || !root.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(root) || File.Exists(root))
            throw new InvalidOperationException("A new absolute RUNNER_TEMP evidence directory is required.");
        foreach (Type type in new[] { typeof(ProGpuTextRendererService), typeof(Graphics) })
            if (!Path.GetFullPath(type.Assembly.Location).StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase) ||
                type.Assembly.Location.Contains("Microsoft.WindowsDesktop.App", StringComparison.Ordinal))
                throw new InvalidOperationException("Private portable package assemblies required.");
        string referenceRoot = Path.GetFullPath(args[1]);
        if (referenceRoot != args[1] || !referenceRoot.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Original same-run reference input required.");
        using JsonDocument reference = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(referenceRoot, "reference.json")));
        if (reference.RootElement.GetProperty("cases").GetArrayLength() != 72 ||
            reference.RootElement.GetProperty("inputEvents").GetInt32() != 0 ||
            reference.RootElement.GetProperty("desktopWindows").GetInt32() != 0)
            throw new InvalidOperationException("Complete no-input reference required.");
        var service = new ProGpuTextRendererService();
        Directory.CreateDirectory(root);
        Stopwatch clock = Stopwatch.StartNew();
        ValidateBulkCapture();
        List<object> cases = new(), fonts = new();
        long bytes = 0;
        foreach (var specification in new[] { (Name: "Microsoft Sans Serif", Points: 9.75f), (Name: "Segoe UI", Points: 9f) })
        {
            JsonElement originalFont = reference.RootElement.GetProperty("fonts").EnumerateArray().Single(f =>
                f.GetProperty("requested").GetString() == specification.Name && f.GetProperty("points").GetSingle() == specification.Points);
            string hash = originalFont.GetProperty("sha256").GetString()!;
            if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c)))
                throw new InvalidOperationException("Invalid original font identity.");
            byte[] fontBytes = File.ReadAllBytes(Path.Combine(temporary, "text-raster-font-inputs", hash + ".ttf"));
            if (fontBytes.Length != originalFont.GetProperty("bytes").GetInt32() || Hash(fontBytes) != hash)
                throw new InvalidOperationException("Original font bytes changed.");
            var face = new TtfFont(fontBytes);
            using Font imported = new(face, specification.Points, FontStyle.Regular, GraphicsUnit.Point);
            using Font font = new(imported.FontFamily, specification.Points, FontStyle.Regular, GraphicsUnit.Point, 0);
            if (font.Name != specification.Name) throw new InvalidOperationException("Requested original font was substituted.");
            fonts.Add(new { requested = specification.Name, managed = font.Name, points = font.SizeInPoints,
                charset = font.GdiCharSet, bytes = fontBytes.Length, sha256 = Hash(fontBytes) });
            foreach (TextRenderingHint hint in Enum.GetValues<TextRenderingHint>())
            foreach (int contrast in new[] { 0, 4, 12 })
            foreach (string api in new[] { "GdiPlusDrawString", "GdiTextRenderer" })
            {
                Budget();
                using Bitmap bitmap = new(Width, Height, PixelFormat.Format32bppArgb);
                bitmap.SetResolution(96, 96);
                object state;
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    graphics.TextRenderingHint = hint;
                    graphics.TextContrast = contrast;
                    if (api == "GdiPlusDrawString")
                    {
                        using StringFormat format = new(StringFormat.GenericTypographic);
                        format.FormatFlags |= StringFormatFlags.NoWrap;
                        graphics.DrawString(Text, font, Brushes.Black, new RectangleF(8, 8, Width - 16, Height - 16), format);
                    }
                    else
                        service.DrawText(graphics, Text, font, new Rectangle(8, 8, Width - 16, Height - 16),
                            Color.Black, Color.White, LibreTextFormat.NoPadding | LibreTextFormat.NoPrefix | LibreTextFormat.SingleLine);
                    state = new { hint = graphics.TextRenderingHint.ToString(), contrast = graphics.TextContrast,
                        dpiX = graphics.DpiX, dpiY = graphics.DpiY };
                }
                byte[] rgba = ReadStraightRgba(bitmap);
                int colored = 0, intermediate = 0, ink = 0;
                int minX = Width, minY = Height, maxX = -1, maxY = -1;
                for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int offset = (y * Width + x) * 4;
                    Color color = Color.FromArgb(rgba[offset + 3], rgba[offset], rgba[offset + 1], rgba[offset + 2]);
                    if (color.R != color.G || color.G != color.B) colored++;
                    if ((color.R > 0 && color.R < 255) || (color.G > 0 && color.G < 255) || (color.B > 0 && color.B < 255)) intermediate++;
                    if (color.R != 255 || color.G != 255 || color.B != 255)
                    { ink++; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                }
                if (ink == 0) throw new InvalidOperationException("Original draw produced no ink.");
                string name = $"{cases.Count:D2}-{api}-{hint}-{contrast}.png";
                bitmap.Save(Path.Combine(root, name), ImageFormat.Png);
                byte[] png = File.ReadAllBytes(Path.Combine(root, name));
                bytes += png.Length;
                cases.Add(new { font = specification.Name, points = specification.Points, api, requestedHint = hint.ToString(),
                    requestedContrast = contrast, actual = state, colored, intermediate, ink,
                    inkBounds = new[] { minX, minY, maxX + 1, maxY + 1 }, rgbaSha256 = Hash(rgba),
                    image = new { name, bytes = png.Length, sha256 = Hash(png) } });
            }
        }
        Budget();
        if (cases.Count != 72) throw new InvalidOperationException("Incomplete original 72-case inventory.");
        var smoothing = reference.RootElement.GetProperty("smoothing").Clone();
        var result = new { schema = "portable-source-adapter-text-raster-v1", qualified = false, inputEvents = 0,
            desktopWindows = 0, text = Text, width = Width, height = Height, dpi = 96, fonts, smoothing, cases,
            runtime = Environment.Version.ToString(), os = Environment.OSVersion.VersionString,
            adapterEntry = "ProGpuTextRendererService.DrawText (canonical TextRenderer dispatch not exercised)",
            smoothingSource = "Original reference observation only; not injected into portable policy",
            pixelCapture = "One read-only Format32bppArgb LockBits per image; complete straight RGBA, no tolerance",
            backendAssembly = typeof(ProGpuTextRendererService).Assembly.Location, drawingAssembly = typeof(Graphics).Assembly.Location,
            elapsedSeconds = clock.Elapsed.TotalSeconds, pngBytes = bytes };
        File.WriteAllText(Path.Combine(root, "reference.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        void Budget()
        {
            if (clock.Elapsed.TotalSeconds > 45 || cases.Count > 72 || bytes > 16 * 1024 * 1024)
                throw new TimeoutException("Portable comparison budget exceeded.");
        }
    }

    private static byte[] ReadStraightRgba(Bitmap bitmap)
    {
        byte[] result = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Width != bitmap.Width || data.Height != bitmap.Height ||
                data.PixelFormat != PixelFormat.Format32bppArgb || data.Scan0 == IntPtr.Zero ||
                Math.Abs((long)data.Stride) < bitmap.Width * 4L)
                throw new InvalidOperationException("Unexpected complete straight-ARGB capture layout.");
            byte[] row = new byte[checked(bitmap.Width * 4)];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, checked(y * data.Stride)), row, 0, row.Length);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int source = x * 4, destination = (y * bitmap.Width + x) * 4;
                    result[destination] = row[source + 2];
                    result[destination + 1] = row[source + 1];
                    result[destination + 2] = row[source];
                    result[destination + 3] = row[source + 3];
                }
            }
        }
        finally { bitmap.UnlockBits(data); }
        return result;
    }

    private static void ValidateBulkCapture()
    {
        using Bitmap probe = new(17, 17, PixelFormat.Format32bppArgb);
        for (int y = 0; y < probe.Height; y++)
        for (int x = 0; x < probe.Width; x++)
        {
            int index = y * probe.Width + x;
            probe.SetPixel(x, y, Color.FromArgb(index % 256, index * 37 % 256, index * 71 % 256, index * 113 % 256));
        }
        byte[] captured = ReadStraightRgba(probe);
        for (int y = 0; y < probe.Height; y++)
        for (int x = 0; x < probe.Width; x++)
        {
            Color actual = probe.GetPixel(x, y);
            int offset = (y * probe.Width + x) * 4;
            if (captured[offset] != actual.R || captured[offset + 1] != actual.G ||
                captured[offset + 2] != actual.B || captured[offset + 3] != actual.A)
                throw new InvalidOperationException("Bulk capture changed original GetPixel channel values.");
        }
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
