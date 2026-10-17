// Read-only Windows GDI font-metric diagnostic; never application qualification.
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class FontMetricsDiagnostic
{
    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FaceName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct TextMetric
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveCharWidth, MaxCharWidth;
        public int Weight, Overhang, DigitizedAspectX, DigitizedAspectY;
        public char FirstChar, LastChar, DefaultChar, BreakChar;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, CharSet;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Extent { public int Width, Height; }
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern nint CreateCompatibleDC(nint source);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern nint CreateFontIndirectW(in LogFont font);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool GetTextMetricsW(nint dc, out TextMetric metrics);
    [DllImport("gdi32.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetTextFaceW(nint dc, int count, StringBuilder name);
    [DllImport("gdi32.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool GetTextExtentPoint32W(nint dc, string text, int count, out Extent size);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern uint GetFontData(nint dc, uint table, uint offset, [Out] byte[]? bytes, uint size);

    private static byte[] ReadFont(nint dc, uint table, int limit)
    {
        uint size = GetFontData(dc, table, 0, null, 0);
        if (size == uint.MaxValue || size == 0 || size > limit) throw new InvalidOperationException("Invalid font data size");
        byte[] bytes = new byte[checked((int)size)];
        if (GetFontData(dc, table, 0, bytes, size) != size) throw new InvalidOperationException("Font data read changed");
        return bytes;
    }
    private static uint Tag(string value) => (uint)value[0] | (uint)value[1] << 8 | (uint)value[2] << 16 | (uint)value[3] << 24;
    private static void Check(bool value, string operation)
    {
        if (!value) throw new InvalidOperationException(operation);
    }
    public static void Main()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var cases = new List<object>();
        nint dc = CreateCompatibleDC(0);
        Check(dc != 0, "CreateCompatibleDC");
        try
        {
            foreach (string family in new[] { "Segoe UI", "Arial", "Consolas" })
            foreach (float points in new[] { 8.25f, 9f, 10f, 12f, 16f })
            foreach (int dpi in new[] { 96, 144, 192 })
            {
                // Exact upstream FontCache.Data logical-height construction.
                var descriptor = new LogFont { FaceName = family, Height = -(int)Math.Ceiling(dpi * points / 72),
                    Weight = 400, CharSet = 1, OutPrecision = 4 };
                nint font = CreateFontIndirectW(in descriptor);
                Check(font != 0, "CreateFontIndirectW");
                nint previous = SelectObject(dc, font);
                Check(previous != 0 && previous != -1, "SelectObject");
                try
                {
                    Check(GetTextMetricsW(dc, out TextMetric metrics), "GetTextMetricsW");
                    var actual = new StringBuilder(256);
                    Check(GetTextFaceW(dc, actual.Capacity, actual) > 0, "GetTextFaceW");
                    Check(GetTextExtentPoint32W(dc, alphabet, alphabet.Length, out Extent extent), "GetTextExtentPoint32W");
                    var tables = new Dictionary<string, string>();
                    foreach (string table in new[] { "head", "hhea", "OS/2" })
                        tables.Add(table, Convert.ToHexString(ReadFont(dc, Tag(table), 4096)));
                    cases.Add(new { family, points, dpi, descriptor, actualFace = actual.ToString(), metrics,
                        alphabetExtent = extent, sourceAverageWidth = (int)Math.Round(extent.Width / (float)alphabet.Length),
                        fontSha256 = Convert.ToHexString(SHA256.HashData(ReadFont(dc, 0, 64 * 1024 * 1024))), tables });
                }
                finally
                {
                    Check(SelectObject(dc, previous) != 0, "Restore original font");
                    Check(DeleteObject(font), "DeleteObject");
                }
            }
        }
        finally { Check(DeleteDC(dc), "DeleteDC"); }
        Console.WriteLine(JsonSerializer.Serialize(new { diagnosticOnly = true, qualified = false,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), alphabet, cases },
            new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
    }
}
