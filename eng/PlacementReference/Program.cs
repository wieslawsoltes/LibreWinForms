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
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
            !typeof(Graphics).Assembly.Location.Contains("Microsoft.WindowsDesktop.App", StringComparison.Ordinal))
            throw new InvalidOperationException("Disposable Windows original framework only.");
        string root = Path.GetFullPath(args[0]);
        string temporary = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP")!);
        if (root != args[0] || !root.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(root) || File.Exists(root) || Text.Length > 32)
            throw new InvalidOperationException("New bounded original output required.");
        Directory.CreateDirectory(root);
        Stopwatch clock = Stopwatch.StartNew();
        List<object> cases = new(), fonts = new();
        long pngBytes = 0;
        foreach (var specification in new[] { (Name: "Microsoft Sans Serif", Points: 9.75f), (Name: "Segoe UI", Points: 9f) })
        {
            using Font font = new(specification.Name, specification.Points, FontStyle.Regular, GraphicsUnit.Point, 0);
            if (font.Name != specification.Name) throw new InvalidOperationException("Original font substituted.");
            fonts.Add(ObserveFont(font));
            foreach (TextRenderingHint hint in Enum.GetValues<TextRenderingHint>())
            {
                Budget();
                using Bitmap whole = new(Width, Height, PixelFormat.Format32bppArgb);
                whole.SetResolution(96, 96);
                using Bitmap pieces = new(Width, Height, PixelFormat.Format32bppArgb);
                pieces.SetResolution(96, 96);
                using Graphics g = Graphics.FromImage(whole), separate = Graphics.FromImage(pieces);
                foreach (Graphics target in new[] { g, separate })
                { target.Clear(Color.White); target.TextRenderingHint = hint; target.TextContrast = 0; }
                using StringFormat format = new(StringFormat.GenericTypographic);
                format.FormatFlags |= StringFormatFlags.NoWrap;
                RectangleF layout = new(8, 8, Width - 16, Height - 16);
                // The whole draw remains the independent, previously captured original.
                g.DrawString(Text, font, Brushes.Black, layout, format);
                SizeF measured = g.MeasureString(Text, font, layout.Size, format);
                using StringFormat measureFormat = (StringFormat)format.Clone();
                measureFormat.SetMeasurableCharacterRanges(Enumerable.Range(0, Text.Length).Select(i => new CharacterRange(i, 1)).ToArray());
                Region[] regions = g.MeasureCharacterRanges(Text, font, layout, measureFormat);
                if (regions.Length != Text.Length) throw new InvalidOperationException("Incomplete source ranges.");
                List<object> positions = new();
                try
                {
                    for (int i = 0; i < Text.Length; i++)
                    {
                        RectangleF box = regions[i].GetBounds(g);
                        positions.Add(new { index = i, codepoint = (int)Text[i], x = box.X, y = box.Y, width = box.Width, height = box.Height });
                        // A diagnostic hypothesis only: range left is not assumed to be a glyph pen.
                        if (Text[i] != ' ')
                            separate.DrawString(Text[i].ToString(), font, Brushes.Black,
                                new RectangleF(box.X, layout.Y, Width - box.X, layout.Height), format);
                    }
                }
                finally { foreach (Region region in regions) region.Dispose(); }
                byte[] a = Rgba(whole), b = Rgba(pieces);
                int differentPixels = 0;
                for (int i = 0; i < a.Length; i += 4)
                    if (!a.AsSpan(i, 4).SequenceEqual(b.AsSpan(i, 4))) differentPixels++;
                string stem = $"{cases.Count:D2}-{hint}";
                var wholeImage = Save(whole, stem + "-whole.png");
                var piecesImage = Save(pieces, stem + "-range-left-pieces.png");
                cases.Add(new { font = specification.Name, points = font.SizeInPoints, hint = hint.ToString(), contrast = 0,
                    measured = new { width = measured.Width, height = measured.Height }, positions,
                    fontHeight = font.GetHeight(g), emHeight = font.FontFamily.GetEmHeight(FontStyle.Regular),
                    cellAscent = font.FontFamily.GetCellAscent(FontStyle.Regular), cellDescent = font.FontFamily.GetCellDescent(FontStyle.Regular),
                    lineSpacing = font.FontFamily.GetLineSpacing(FontStyle.Regular),
                    wholeRgbaSha256 = Hash(a), piecesRgbaSha256 = Hash(b), differentPixels, wholeImage, piecesImage });
            }
        }
        Budget();
        if (cases.Count != 12) throw new InvalidOperationException("Incomplete twelve-case inventory.");
        File.WriteAllText(Path.Combine(root, "reference.json"), JsonSerializer.Serialize(new {
            schema = "original-gdi-plus-character-placement-v1", qualified = false, inputEvents = 0, desktopWindows = 0,
            text = Text, width = Width, height = Height, dpi = 96, fonts, cases, pngBytes, elapsedSeconds = clock.Elapsed.TotalSeconds,
            scope = "Public character-range measurement and independent whole/single-character drawings; measured boxes are not asserted glyph origins."
        }, new JsonSerializerOptions { WriteIndented = true }));
        object Save(Bitmap bitmap, string name)
        {
            string path = Path.Combine(root, name); bitmap.Save(path, ImageFormat.Png);
            byte[] bytes = File.ReadAllBytes(path); pngBytes += bytes.Length;
            return new { name, bytes = bytes.Length, sha256 = Hash(bytes) };
        }
        void Budget()
        {
            if (clock.Elapsed.TotalSeconds > 45 || pngBytes > 16 * 1024 * 1024)
                throw new TimeoutException("Original observation budget exceeded.");
        }
    }

    private static byte[] Rgba(Bitmap image)
    {
        byte[] bytes = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            Color c = image.GetPixel(x, y); int offset = (y * Width + x) * 4;
            bytes[offset] = c.R; bytes[offset + 1] = c.G; bytes[offset + 2] = c.B; bytes[offset + 3] = c.A;
        }
        return bytes;
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
