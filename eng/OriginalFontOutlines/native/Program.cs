using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ProGPU.Backend.Native;

internal static class Program
{
    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception failure) { Console.Error.WriteLine(failure); return 1; }
    }

    private static void Run(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 2 ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
            throw new InvalidOperationException("Disposable hosted Windows only; no input or desktop windows.");
        string temporary = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP")!);
        string output = Path.GetFullPath(args[0]), referencePath = Path.GetFullPath(args[1]);
        if (output != args[0] || referencePath != args[1] ||
            !output.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !referencePath.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(output) || File.Exists(output))
            throw new InvalidOperationException("Fresh owned output and same-run reference are required.");
        using JsonDocument reference = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(referencePath, "reference.json")));
        JsonElement source = reference.RootElement;
        if (source.GetProperty("cases").GetArrayLength() != 72 ||
            source.GetProperty("inputEvents").GetInt32() != 0 || source.GetProperty("desktopWindows").GetInt32() != 0 ||
            source.GetProperty("fonts").GetArrayLength() != 2)
            throw new InvalidOperationException("Complete original source capture required.");
        Directory.CreateDirectory(output);
        Stopwatch clock = Stopwatch.StartNew();
        List<object> fonts = new();
        foreach (JsonElement font in source.GetProperty("fonts").EnumerateArray())
        {
            string hash = font.GetProperty("sha256").GetString()!;
            if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c)))
                throw new InvalidOperationException("Invalid source font hash.");
            byte[] bytes = File.ReadAllBytes(Path.Combine(temporary, "text-raster-font-inputs", hash + ".ttf"));
            if (bytes.Length != font.GetProperty("bytes").GetInt32() ||
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != hash)
                throw new InvalidOperationException("Original font bytes changed.");
            JsonElement original = font.GetProperty("outlines");
            uint[] glyphs = original.GetProperty("glyphIndices").EnumerateArray().Select(x => x.GetUInt32()).ToArray();
            if (glyphs.Length != 32) throw new InvalidOperationException("Original glyph slot inventory changed.");
            byte[] logFont = Convert.FromBase64String(original.GetProperty("selectedLogFontBase64").GetString()!);
            if (logFont.Length != 92) throw new InvalidOperationException("Invalid original LOGFONT.");
            int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(logFont);
            int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(logFont.AsSpan(4));
            if (height is >= 0 or < -1024 || width != 0)
                throw new InvalidOperationException("The original negative-em, default-width font frame is required.");
            uint em = checked((uint)-height * 64);
            using NativeTextShapingContext context = new(bytes);
            List<object> captures = new();
            foreach (NativeFontHintInterpreter interpreter in Enum.GetValues<NativeFontHintInterpreter>())
            foreach (NativeFontOutlinePolicy policy in Enum.GetValues<NativeFontOutlinePolicy>())
            {
                if (clock.Elapsed.TotalSeconds >= 45) throw new TimeoutException("Font observation budget exceeded.");
                using NativeHintedFontBatch batch = context.CaptureOutlineBatch(0, glyphs, em, em, interpreter, policy);
                var counts = batch.Counts;
                if (counts.Glyphs != glyphs.Length || counts.Points > 1 << 20 || counts.Contours > 1 << 18)
                    throw new InvalidOperationException("Native capture exceeded original bounded inventory.");
                var descriptors = new NativeHintedGlyph[counts.Glyphs];
                var points = new NativeHintedPoint[counts.Points];
                var tags = new byte[counts.Points];
                var contours = new int[counts.Contours];
                batch.CopyTo(descriptors, points, tags, contours);
                for (int i = 0; i < glyphs.Length; i++)
                    if (descriptors[i].GlyphIndex != glyphs[i]) throw new InvalidOperationException("Source glyph order changed.");
                captures.Add(new { interpreter = interpreter.ToString(), policy = policy.ToString(),
                    em26_6 = em, counts, descriptors, points, tagsBase64 = Convert.ToBase64String(tags), contours });
            }
            fonts.Add(new { font = font.GetProperty("native").GetString(), sha256 = hash, captures });
        }
        if (clock.Elapsed.TotalSeconds >= 45) throw new TimeoutException("Font observation budget exceeded.");
        File.WriteAllText(Path.Combine(output, "native-outlines.json"), JsonSerializer.Serialize(new
        {
            schema = "native-font-outline-observation-v1", qualified = false, inputEvents = 0, desktopWindows = 0,
            runtime = Environment.Version.ToString(), architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            elapsedSeconds = clock.Elapsed.TotalSeconds, fonts,
            limitation = "Compares explicit font execution policies, without selecting GDI/GDI+ defaults or claiming pixel parity."
        }, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
    }
}
