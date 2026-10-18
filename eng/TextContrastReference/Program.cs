using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

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
        if (!OperatingSystem.IsWindows() || args.Length != 1 ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
            throw new InvalidOperationException("Disposable hosted Windows only; no input or desktop windows.");
        string root = Path.GetFullPath(args[0]);
        string temporary = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP")!);
        if (root != args[0] || !root.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(root) || File.Exists(root))
            throw new InvalidOperationException("A new absolute RUNNER_TEMP evidence directory is required.");
        foreach (Type type in new[] { typeof(Form), typeof(Graphics) })
            if (!type.Assembly.Location.Contains("Microsoft.WindowsDesktop.App", StringComparison.Ordinal))
                throw new InvalidOperationException("Original Microsoft framework required.");
        Directory.CreateDirectory(root);
        Stopwatch clock = Stopwatch.StartNew();
        List<object> cases = new(), fonts = new();
        long bytes = 0;
        foreach (var specification in new[] { (Name: "Microsoft Sans Serif", Points: 9.75f), (Name: "Segoe UI", Points: 9f) })
        {
            using Font font = new(specification.Name, specification.Points, FontStyle.Regular, GraphicsUnit.Point, 0);
            if (font.Name != specification.Name) throw new InvalidOperationException("Requested original font was substituted.");
            fonts.Add(ObserveFont(font));
            foreach (TextRenderingHint hint in Enum.GetValues<TextRenderingHint>())
            foreach (int contrast in Enumerable.Range(0, 13))
            foreach (var paint in new[] {
                (Name: "black-white", Foreground: Color.Black, Background: Color.White),
                (Name: "white-black", Foreground: Color.White, Background: Color.Black),
                (Name: "color-opaque", Foreground: Color.FromArgb(255, 31, 97, 211), Background: Color.FromArgb(255, 223, 157, 61)),
                (Name: "half-color-opaque", Foreground: Color.FromArgb(128, 31, 97, 211), Background: Color.FromArgb(255, 223, 157, 61)),
                (Name: "color-transparent", Foreground: Color.FromArgb(255, 31, 97, 211), Background: Color.FromArgb(0, 0, 0, 0)),
                (Name: "half-color-translucent", Foreground: Color.FromArgb(128, 31, 97, 211), Background: Color.FromArgb(96, 223, 157, 61)) })
            {
                Budget();
                using Bitmap bitmap = new(Width, Height, PixelFormat.Format32bppArgb);
                bitmap.SetResolution(96, 96);
                object state;
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(paint.Background);
                    graphics.TextRenderingHint = hint;
                    graphics.TextContrast = contrast;
                    using StringFormat format = new(StringFormat.GenericTypographic);
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    using SolidBrush brush = new(paint.Foreground);
                    graphics.DrawString(Text, font, brush, new RectangleF(8, 8, Width - 16, Height - 16), format);
                    state = new { hint = graphics.TextRenderingHint.ToString(), contrast = graphics.TextContrast,
                        dpiX = graphics.DpiX, dpiY = graphics.DpiY };
                }
                byte[] rgba = new byte[Width * Height * 4];
                int colored = 0, intermediate = 0, ink = 0;
                int minX = Width, minY = Height, maxX = -1, maxY = -1;
                for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    Color color = bitmap.GetPixel(x, y);
                    int offset = (y * Width + x) * 4;
                    rgba[offset] = color.R; rgba[offset + 1] = color.G; rgba[offset + 2] = color.B; rgba[offset + 3] = color.A;
                    if (color.R != color.G || color.G != color.B) colored++;
                    if ((color.R > 0 && color.R < 255) || (color.G > 0 && color.G < 255) || (color.B > 0 && color.B < 255)) intermediate++;
                    if (color.ToArgb() != paint.Background.ToArgb())
                    { ink++; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                }
                if (ink == 0) throw new InvalidOperationException("Original draw produced no ink.");
                string name = $"{cases.Count:D4}-{paint.Name}-{hint}-{contrast}.png";
                bitmap.Save(Path.Combine(root, name), ImageFormat.Png);
                byte[] png = File.ReadAllBytes(Path.Combine(root, name));
                bytes += png.Length;
                cases.Add(new { font = specification.Name, points = specification.Points, api = "GdiPlusDrawString", paint = paint.Name, foregroundArgb = paint.Foreground.ToArgb(), backgroundArgb = paint.Background.ToArgb(), requestedHint = hint.ToString(),
                    requestedContrast = contrast, actual = state, colored, intermediate, ink,
                    inkBounds = new[] { minX, minY, maxX + 1, maxY + 1 }, rgbaSha256 = Hash(rgba),
                    image = new { name, bytes = png.Length, sha256 = Hash(png) } });
            }
        }
        Budget();
        if (cases.Count != 936) throw new InvalidOperationException("Incomplete original 936-case contrast inventory.");
        var smoothing = new[] { 0x004Au, 0x200Au, 0x200Cu, 0x2012u }.Select(action =>
        {
            bool success = SystemParametersInfoW(action, 0, out uint value, 0);
            return new { action, success, value, error = success ? 0 : Marshal.GetLastWin32Error() };
        }).ToArray();
        var result = new { schema = "original-gdiplus-text-contrast-v1", qualified = false, inputEvents = 0,
            desktopWindows = 0, text = Text, width = Width, height = Height, dpi = 96, fonts, smoothing, cases,
            runtime = Environment.Version.ToString(), os = Environment.OSVersion.VersionString,
            formsAssembly = typeof(Form).Assembly.Location, drawingAssembly = typeof(Graphics).Assembly.Location,
            elapsedSeconds = clock.Elapsed.TotalSeconds, pngBytes = bytes };
        File.WriteAllText(Path.Combine(root, "reference.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        void Budget()
        {
            if (clock.Elapsed.TotalSeconds > 45 || cases.Count > 936 || bytes > 16 * 1024 * 1024)
                throw new TimeoutException("Original reference budget exceeded.");
        }
    }

    private static object ObserveFont(Font font)
    {
        nint dc = CreateCompatibleDC(0);
        if (dc == 0) throw new InvalidOperationException("Owned font DC creation failed.");
        nint selected = 0, old = 0;
        try
        {
            selected = font.ToHfont();
            old = SelectObject(dc, selected);
            if (old == 0 || old == -1) throw new InvalidOperationException("Owned font selection failed.");
            StringBuilder face = new(128);
            if (GetTextFaceW(dc, face.Capacity, face) == 0) throw new InvalidOperationException("Native font identity unavailable.");
            uint length = GetFontData(dc, 0, 0, null, 0);
            if (length == uint.MaxValue || length == 0 || length > 32 * 1024 * 1024)
                throw new InvalidOperationException("Invalid original font byte extent.");
            byte[] data = new byte[checked((int)length)];
            if (GetFontData(dc, 0, 0, data, length) != length) throw new InvalidOperationException("Incomplete original font bytes.");
            return new { requested = font.OriginalFontName, managed = font.Name, native = face.ToString(),
                points = font.SizeInPoints, charset = font.GdiCharSet, bytes = length, sha256 = Hash(data),
                gasp = ObserveFontTable(dc, "gasp") };
        }
        finally
        {
            bool restored = old == 0 || old == -1 || SelectObject(dc, old) != 0;
            bool dcReleased = DeleteDC(dc);
            bool fontReleased = selected == 0 || DeleteObject(selected);
            if (!restored || !fontReleased || !dcReleased) throw new InvalidOperationException("Owned font native cleanup failed.");
        }
    }

    // Read-only metadata from the same selected native face. An unavailable
    // query is recorded as its raw GDI result, never interpreted as an empty table.
    private static object ObserveFontTable(nint dc, string tag)
    {
        uint nativeTag = (uint)tag[0] | (uint)tag[1] << 8 | (uint)tag[2] << 16 | (uint)tag[3] << 24;
        uint length = GetFontData(dc, nativeTag, 0, null, 0);
        if (length == uint.MaxValue)
            return new { tag, queryResult = length, available = false };
        if (length == 0 || length > 16384)
            throw new InvalidOperationException("Original font policy table exceeds its metadata bound.");
        byte[] table = new byte[checked((int)length)];
        if (GetFontData(dc, nativeTag, 0, table, length) != length)
            throw new InvalidOperationException("Original font policy table changed during observation.");
        return new { tag, queryResult = length, available = true, sha256 = Hash(table),
            hex = Convert.ToHexString(table).ToLowerInvariant() };
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int GetTextFaceW(nint dc, int count, StringBuilder face);
    [DllImport("gdi32.dll")] private static extern uint GetFontData(nint dc, uint table, uint offset, [Out] byte[]? data, uint length);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, out uint value, uint flags);
}
