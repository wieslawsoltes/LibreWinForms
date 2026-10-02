// Independent Uniscribe observations beside real EDIT geometry. These APIs do
// not expose EDIT's internal shaping cache, selected fallback runs or renderer.
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

internal static partial class WordSelectionReference
{
    private sealed class ShapingDiagnostics
    {
        public string Schema => "native-edit-shaping-diagnostics-v1";
        public bool IndependentOfEdit => true;
        public bool EditRendererIdentityQualified => false;
        public bool EditFallbackIdentityQualified => false;
        public string FontPolicy => "selected-HFONT-only; no fallback attempted";
        // The captured Arial hdmx records distinguish these sizes. The verifier
        // still requires actual ScriptPlace odd/even advances, not table claims.
        public int[] RequestedPixelSizes => [19, 21];
        public List<Dictionary<string, object?>> Cases { get; } = [];
        public Dictionary<string, byte[]> FontBytesBySha256 { get; } = [];
        public List<string> Errors { get; } = [];
        public bool Completed { get; set; }
        public bool HostShown { get; set; }
        public bool OwnedWindowOnly => true;
        public bool DesktopQualified => false;
        public bool PhysicalInputQualified => false;
        public object? Identity { get; set; }
        public long ElapsedMilliseconds { get; set; }
    }

    private static int RunShapingDiagnostics(string path, Stopwatch budget, object? identity, long remainingReceiptBytes)
    {
        using FileStream file = new(Path.GetFullPath(path), FileMode.CreateNew, FileAccess.Write);
        var result = new ShapingDiagnostics { Identity = identity };
        try
        {
            using Form form = new()
            {
                AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(520, 180),
                StartPosition = FormStartPosition.Manual, Location = new Point(16, 16),
                ShowInTaskbar = false, Text = "Owned EDIT independent shaping reference"
            };
            form.Show();
            Application.DoEvents();
            result.HostShown = form.Visible;
            if (!result.HostShown) throw new InvalidOperationException("Diagnostic host was not shown.");
            foreach (int size in result.RequestedPixelSizes)
            foreach (GeometryInput original in GeometryInputs().Where(input => input.FontFamily is not null))
            {
                CheckBudget(budget);
                GeometryInput input = original with { FontPixels = size };
                using Font font = new(input.FontFamily!, input.FontPixels, FontStyle.Regular, GraphicsUnit.Pixel);
                if (!string.Equals(font.Name, input.FontFamily, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The requested diagnostic font is unavailable.");
                using Probe editor = new()
                {
                    AutoSize = false, BorderStyle = BorderStyle.FixedSingle, Bounds = new Rectangle(10, 10, 300, 60),
                    RightToLeft = input.Source.RightToLeft ? RightToLeft.Yes : RightToLeft.No,
                    Font = font, Text = input.Source.Text
                };
                form.Controls.Add(editor);
                nint handle = editor.Handle;
                form.Activate();
                bool focused = editor.Focus();
                Application.DoEvents();
                RequireGeometryOwner(editor, handle, input.Source.Text);
                if (!focused) throw new InvalidOperationException("Diagnostic EDIT focus is unavailable.");
                ObserveShaping(editor, handle, input, budget, result);
            }
            if (result.Cases.Count != 8) throw new InvalidOperationException("Incomplete shaping diagnostic case inventory.");
            result.Completed = result.Errors.Count == 0;
        }
        catch (Exception error)
        {
            result.Errors.Add(error.ToString());
            Console.Error.WriteLine(error);
        }
        result.ElapsedMilliseconds = budget.ElapsedMilliseconds;
        using var bounded = new GeometryReceiptStream(file, budget, remainingReceiptBytes);
        // Preserve the combined original byte budget; no repeated indentation
        // around every diagnostic hit is needed to retain the original values.
        JsonSerializer.Serialize(bounded, result, new JsonSerializerOptions { IncludeFields = true });
        bounded.Flush();
        return result.Completed ? 0 : 1;
    }

    private static void ObserveShaping(Probe editor, nint handle, GeometryInput input, Stopwatch budget,
        ShapingDiagnostics result)
    {
        var entry = new Dictionary<string, object?>
        {
            ["name"] = input.Source.Name, ["requestedPixelSize"] = input.FontPixels,
            ["requestedText"] = input.Source.Text, ["requestedUtf16"] = Utf16(input.Source.Text),
            ["rightToLeft"] = input.Source.RightToLeft,
            ["requestedSeams"] = new[] { input.First, input.Middle, input.Last },
            ["primaryCase"] = input.Source.Name, ["samePrimaryWindow"] = false, ["handle"] = handle.ToInt64(),
            ["nativeClass"] = ClassName(handle), ["deviceDpi"] = editor.DeviceDpi,
            ["clientSize"] = editor.ClientSize,
            ["systemColors"] = new[] { 5, 8, 13, 14, 15, 18 }.Select(index => new { index, colorRef = GetSysColor(index) }).ToArray(),
            ["completed"] = false
        };
        result.Cases.Add(entry);
        try
        {
            CheckBudget(budget);
            RequireGeometryOwner(editor, handle, input.Source.Text);
            // The new owned EDIT/HFONT supplies both independent shaping and
            // real control geometry. Same input is not the primary HWND/cache.
            entry["observations"] = ReadShapingSelections(editor, handle, input, budget);
            entry["ownedCaretSamples"] = 22;
            ReadShapingRuns(editor, handle, input, budget, result, entry);
            RequireGeometryOwner(editor, handle, input.Source.Text);
            entry["completed"] = true;
        }
        catch (Exception error)
        {
            entry["error"] = error.ToString();
            result.Errors.Add(error.ToString());
            Console.Error.WriteLine(error);
        }
    }

    private static List<object> ReadShapingSelections(Probe editor, nint handle, GeometryInput input, Stopwatch budget)
    {
        var observations = new List<object>();
        foreach (int position in new[] { 0, input.First, input.Middle, input.Last, input.Source.Text.Length })
            Select("collapsed-" + position, position, position);
        foreach ((int a, int b) in new[] { (input.First, input.Middle), (input.Middle, input.Last), (input.First, input.Last) })
        {
            Select($"forward-{a}-{b}", a, b);
            Select($"reverse-{b}-{a}", b, a);
        }
        return observations;

        void Select(string name, int requestedAnchor, int requestedActive)
        {
            CheckBudget(budget);
            RequireGeometryOwner(editor, handle, input.Source.Text);
            long response = SendMessageW(handle, SetSelection, requestedAnchor, requestedActive).ToInt64();
            GeometryState before = ReadGeometryState(editor, handle, input.Source.Text);
            long scroll = SendMessageW(handle, ScrollCaret, 0, 0).ToInt64();
            GeometryState after = ReadGeometryState(editor, handle, input.Source.Text);
            GeometryPixels pixels = ReadGeometryPixels(editor, handle, input.Source.Text);
            observations.Add(new { name, request = new { kind = "set-selection", requestedAnchor, requestedActive, result = response },
                beforeScroll = before, scrollCaretResult = scroll, afterScroll = after, pixels });
            if (!before.Caret.Available || !after.Caret.Available || !pixels.RgbIndependentOfClear)
                throw new InvalidOperationException("Diagnostic owned caret/complete print is unavailable.");
        }
    }

    private static void ReadShapingRuns(Probe editor, nint handle, GeometryInput input, Stopwatch budget,
        ShapingDiagnostics receipt, Dictionary<string, object?> entry)
    {
        if (Marshal.SizeOf<ShapingAnalysis>() != 4 || Marshal.SizeOf<ScriptItem>() != 8 ||
            Marshal.SizeOf<ShapingFontProperties>() != 16 || Marshal.SizeOf<ShapingTextMetric>() != 60 ||
            Marshal.SizeOf<ShapingOffset>() != 8 || Marshal.SizeOf<ShapingAbc>() != 12)
            throw new InvalidOperationException("Unexpected Uniscribe/GDI diagnostic ABI.");
        nint font = SendMessageW(handle, GetFont, 0, 0);
        if (font == 0 || GetObjectW(font, Marshal.SizeOf<GeometryLogFont>(), out GeometryLogFont descriptor) != Marshal.SizeOf<GeometryLogFont>())
            throw new InvalidOperationException("Diagnostic borrowed EDIT HFONT is unavailable.");
        nint dc = GetDC(handle), previous = 0, cache = 0;
        if (dc == 0) throw new Win32Exception();
        Exception? primary = null;
        try
        {
            previous = SelectObject(dc, font);
            if (previous == 0 || previous == -1 || GetCurrentObject(dc, 6) != font) throw new Win32Exception();
            char[] face = new char[256];
            int faceLength = GetTextFaceW(dc, face.Length, face);
            int terminator = Array.IndexOf(face, '\0');
            if (faceLength <= 0 || faceLength >= face.Length || terminator <= 0) throw new Win32Exception();
            uint byteCount = GetFontData(dc, 0, 0, null, 0);
            if (byteCount is 0 or > 32 * 1024 * 1024) throw new InvalidOperationException("Diagnostic selected font exceeds its byte bound.");
            byte[] bytes = new byte[checked((int)byteCount)];
            if (GetFontData(dc, 0, 0, bytes, byteCount) != byteCount || !GetTextMetricsW(dc, out ShapingTextMetric metrics))
                throw new Win32Exception();
            int mapMode = GetMapMode(dc);
            if (mapMode != 1) throw new InvalidOperationException("Diagnostic HDC must use MM_TEXT logical units.");
            string hash = GeometryHash(bytes);
            if (receipt.FontBytesBySha256.TryGetValue(hash, out byte[]? existing) && !bytes.AsSpan().SequenceEqual(existing))
                throw new InvalidOperationException("Diagnostic font hash collision.");
            receipt.FontBytesBySha256.TryAdd(hash, bytes);
            entry["font"] = new { borrowedHandle = font.ToInt64(), descriptor, selectedFace = new string(face, 0, terminator),
                faceApi = "GetTextFaceW", byteCount, sha256 = hash, metrics, mapMode,
                realizedEmHeight = metrics.Height - metrics.InternalLeading,
                managed = new { editor.Font.Name, editor.Font.Size, unit = editor.Font.Unit.ToString(), style = editor.Font.Style.ToString() },
                source = "WM_GETFONT selected into GetDC(EDIT); GetFontData on that same HDC" };
            var properties = new ShapingFontProperties { ByteSize = Marshal.SizeOf<ShapingFontProperties>() };
            RequireShapingResult(entry, "fontPropertiesHResult", ScriptGetFontProperties(dc, ref cache, ref properties));
            entry["fontProperties"] = properties;
            string text = input.Source.Text;
            uint control = 0;
            ushort state = input.Source.RightToLeft ? (ushort)1 : (ushort)0;
            var items = new ScriptItem[text.Length + 2];
            RequireShapingResult(entry, "itemizeHResult", ScriptItemize(text, text.Length, text.Length + 1, in control, in state, items, out int count));
            if (count <= 0 || count > text.Length || items[0].Start != 0 || items[count].Start != text.Length)
                throw new InvalidOperationException("Invalid independent shaping partition.");
            entry["control"] = control;
            entry["initialState"] = state;
            RequireShapingResult(entry, "scriptPropertiesHResult", ScriptGetProperties(out nint propertyTable, out int propertyCount));
            if (propertyTable == 0 || propertyCount <= 0) throw new InvalidOperationException("Script properties are unavailable.");
            var runs = new List<Dictionary<string, object?>>();
            entry["runs"] = runs;
            for (int index = 0; index < count; index++)
            {
                CheckBudget(budget);
                int start = items[index].Start, end = items[index + 1].Start;
                if (start < 0 || end <= start || end > text.Length) throw new InvalidOperationException("Invalid run source bounds.");
                string source = text[start..end];
                var analysis = new ShapingAnalysis { Flags = items[index].Analysis, State = items[index].State };
                var run = new Dictionary<string, object?> { ["start"] = start, ["end"] = end,
                    ["inputAnalysis"] = analysis, ["sourceUtf16"] = Utf16(source) };
                runs.Add(run);
                int script = analysis.Flags & 0x3FF;
                if (script >= propertyCount) throw new InvalidOperationException("Script property index is invalid.");
                nint scriptProperties = Marshal.ReadIntPtr(propertyTable, checked(script * IntPtr.Size));
                if (scriptProperties == 0) throw new InvalidOperationException("Script properties are missing.");
                run["rawPropertiesFirst"] = unchecked((uint)Marshal.ReadInt32(scriptProperties));
                run["rawPropertiesSecond"] = unchecked((uint)Marshal.ReadInt32(scriptProperties, 4));
                int capacity = checked((source.Length * 3 + 1) / 2 + 16);
                var glyphs = new ushort[capacity];
                var clusters = new ushort[source.Length];
                var attributes = new ushort[capacity]; // SCRIPT_VISATTR is one WORD, not managed bool fields.
                int shape = ScriptShape(dc, ref cache, source, source.Length, capacity, ref analysis,
                    glyphs, clusters, attributes, out int glyphCount);
                run["shapeHResult"] = shape;
                // Failed calls have undefined output: never publish those arrays.
                if (shape != 0) throw new InvalidOperationException($"Selected-font ScriptShape failed: 0x{unchecked((uint)shape):X8}; no fallback attempted.");
                if (glyphCount <= 0 || glyphCount > capacity || (analysis.Flags & 0x8000) != 0 || clusters.Any(value => value >= glyphCount))
                    throw new InvalidOperationException("Shaping did not return valid font glyph indices/clusters.");
                glyphs = glyphs[..glyphCount];
                attributes = attributes[..glyphCount];
                run["shapeAnalysis"] = analysis;
                run["glyphs"] = glyphs;
                run["logClusters"] = clusters;
                run["visualAttributes"] = attributes;
                int[] missing = Enumerable.Range(0, glyphCount).Where(i => glyphs[i] == properties.Default).ToArray();
                run["missingGlyphIndices"] = missing;
                if (missing.Length != 0) throw new InvalidOperationException("Selected font requires fallback; EDIT fallback face remains unknown.");
                var advances = new int[glyphCount];
                var offsets = new ShapingOffset[glyphCount];
                RequireShapingResult(run, "placeHResult", ScriptPlace(dc, ref cache, glyphs, glyphCount, attributes,
                    ref analysis, advances, offsets, out ShapingAbc abc));
                int width = advances.Sum();
                if (width <= 0 || width > MaximumGeometryWidth || advances.Any(value => value < 0))
                    throw new InvalidOperationException("Diagnostic run advance is outside the bounded scan.");
                run["placeAnalysis"] = analysis;
                run["advances"] = advances;
                run["offsets"] = offsets;
                run["abc"] = abc;
                run["advanceWidth"] = width;
                var logicalWidths = new int[source.Length];
                RequireShapingResult(run, "logicalWidthsHResult", ScriptGetLogicalWidths(in analysis, source.Length,
                    glyphCount, advances, clusters, attributes, logicalWidths));
                run["logicalWidths"] = logicalWidths;
                var edges = new List<object>();
                run["edges"] = edges;
                for (int cp = 0; cp < source.Length; cp++)
                foreach (bool trailing in new[] { false, true })
                {
                    CheckBudget(budget);
                    int hr = ScriptCPtoX(cp, trailing, source.Length, glyphCount, clusters, attributes, advances, in analysis, out int x);
                    edges.Add(new { cp, sourceBoundary = start + cp + (trailing ? 1 : 0), trailing, hResult = hr, x = hr == 0 ? (int?)x : null });
                    if (hr != 0) throw new InvalidOperationException("ScriptCPtoX failed.");
                }
                var hits = new List<object>();
                run["hits"] = hits;
                for (int x = -1; x <= width + 1; x++)
                {
                    CheckBudget(budget);
                    int hr = ScriptXtoCP(x, source.Length, glyphCount, clusters, attributes, advances, in analysis, out int cp, out int trailing);
                    hits.Add(new { x, hResult = hr, cp = hr == 0 ? (int?)cp : null,
                        trailing = hr == 0 ? (int?)trailing : null }); // Native trailing is a distance, NOT bool.
                    if (hr != 0) throw new InvalidOperationException("ScriptXtoCP failed.");
                }
                if (input.First >= start && input.Last <= end)
                {
                    int firstGlyph = clusters[input.First - start];
                    int[] sourceOwners = Enumerable.Range(0, source.Length).Where(i => clusters[i] == firstGlyph).Select(i => start + i).ToArray();
                    if (clusters[input.Middle - start] != firstGlyph || !sourceOwners.SequenceEqual(Enumerable.Range(input.First, input.Last - input.First)))
                        throw new InvalidOperationException("The requested lam-alef control did not form a shared cluster.");
                    int nextGlyph = clusters.Where(value => value > firstGlyph).Select(value => (int)value).DefaultIfEmpty(glyphCount).Min();
                    int clusterWidth = advances[firstGlyph..nextGlyph].Sum();
                    run["targetCluster"] = new { sourceStart = input.First, sourceEnd = input.Last,
                        firstGlyph, glyphEnd = nextGlyph, advanceWidth = clusterWidth, parity = clusterWidth & 1 };
                }
            }
            byte[] levels = items.Take(count).Select(item => (byte)(item.State & 31)).ToArray();
            var visualToLogical = new int[count];
            var logicalToVisual = new int[count];
            RequireShapingResult(entry, "layoutHResult", ScriptLayout(count, levels, visualToLogical, logicalToVisual));
            entry["levels"] = levels.Select(value => (int)value).ToArray();
            entry["visualToLogical"] = visualToLogical;
            entry["logicalToVisual"] = logicalToVisual;
            entry["coordinateFrame"] = "Each run uses its own left origin with original ScriptItemize flags; no EDIT alignment or affinity is inferred.";
            if (GetCurrentObject(dc, 6) != font || SendMessageW(handle, GetFont, 0, 0) != font)
                throw new InvalidOperationException("Selected diagnostic/source font changed.");
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            int free = cache == 0 ? 0 : ScriptFreeCache(ref cache);
            entry["freeCacheHResult"] = free;
            nint restored = previous == 0 || previous == -1 ? 0 : SelectObject(dc, previous);
            bool restoreOk = previous == 0 || previous == -1 || (restored != 0 && restored != -1);
            bool releaseOk = ReleaseDC(handle, dc) == 1;
            entry["fontRestored"] = restoreOk;
            entry["dcReleased"] = releaseOk;
            if (free != 0 || !restoreOk || !releaseOk) GeometryCleanupFailure(primary, "Diagnostic cache/font/DC retirement failed.");
        }
    }

    private static void RequireShapingResult(Dictionary<string, object?> receipt, string name, int result)
    {
        receipt[name] = result;
        if (result != 0) throw new InvalidOperationException($"{name}: 0x{unchecked((uint)result):X8}.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct ShapingAnalysis { public ushort Flags, State; }
    [StructLayout(LayoutKind.Sequential)] private struct ShapingOffset { public int Du, Dv; }
    [StructLayout(LayoutKind.Sequential)] private struct ShapingAbc { public int A; public uint B; public int C; }
    [StructLayout(LayoutKind.Sequential)] private struct ShapingFontProperties
    { public int ByteSize; public ushort Blank, Default, Invalid, Kashida; public int KashidaWidth; }
    [StructLayout(LayoutKind.Sequential)] private struct ShapingTextMetric
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AverageWidth, MaximumWidth,
            Weight, Overhang, DigitizedAspectX, DigitizedAspectY;
        public ushort FirstChar, LastChar, DefaultChar, BreakChar;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, CharSet;
    }
    [DllImport("gdi32", ExactSpelling = true)] private static extern nint GetCurrentObject(nint dc, uint type);
    [DllImport("gdi32", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetTextFaceW(nint dc, int capacity, [Out] char[] face);
    [DllImport("gdi32", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextMetricsW(nint dc, out ShapingTextMetric metrics);
    [DllImport("gdi32", ExactSpelling = true)] private static extern int GetMapMode(nint dc);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("usp10.dll", ExactSpelling = true)]
    private static extern int ScriptGetFontProperties(nint dc, ref nint cache, ref ShapingFontProperties properties);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("usp10.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ScriptShape(nint dc, ref nint cache, [MarshalAs(UnmanagedType.LPWStr)] string text,
        int chars, int capacity, ref ShapingAnalysis analysis, [Out] ushort[] glyphs, [Out] ushort[] clusters,
        [Out] ushort[] attributes, out int glyphCount);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("usp10.dll", ExactSpelling = true)]
    private static extern int ScriptPlace(nint dc, ref nint cache, ushort[] glyphs, int count, ushort[] attributes,
        ref ShapingAnalysis analysis, [Out] int[] advances, [Out] ShapingOffset[] offsets, out ShapingAbc abc);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("usp10.dll", ExactSpelling = true)]
    private static extern int ScriptCPtoX(int cp, [MarshalAs(UnmanagedType.Bool)] bool trailing, int chars, int glyphs,
        ushort[] clusters, ushort[] attributes, int[] advances, in ShapingAnalysis analysis, out int x);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("usp10.dll", ExactSpelling = true)]
    private static extern int ScriptXtoCP(int x, int chars, int glyphs, ushort[] clusters, ushort[] attributes,
        int[] advances, in ShapingAnalysis analysis, out int cp, out int trailing);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("usp10.dll", ExactSpelling = true)]
    private static extern int ScriptGetLogicalWidths(in ShapingAnalysis analysis, int chars, int glyphs,
        int[] advances, ushort[] clusters, ushort[] attributes, [Out] int[] widths);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("usp10.dll", ExactSpelling = true)]
    private static extern int ScriptLayout(int count, byte[] levels, [Out] int[] visualToLogical, [Out] int[] logicalToVisual);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("usp10.dll", ExactSpelling = true)]
    private static extern int ScriptFreeCache(ref nint cache);
}
