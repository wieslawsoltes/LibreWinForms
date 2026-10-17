// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms.VisualStyles;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--thumb-primitives")
                return ThumbPrimitiveReference.Run(args[1]);

            if (!OperatingSystem.IsWindows() || args.Length != 1 ||
                Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
                Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
                throw new InvalidOperationException("Reference execution requires a disposable hosted Windows runner.");
            string root = Path.GetFullPath(args[0]);
            string temporary = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP")!);
            if (args[0] != root || !root.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                Directory.Exists(root) || File.Exists(root))
                throw new InvalidOperationException("Evidence must be a new absolute directory under RUNNER_TEMP.");
            foreach (Type type in new[] { typeof(Form), typeof(Bitmap) })
                if (!type.Assembly.Location.Contains("Microsoft.WindowsDesktop.App", StringComparison.Ordinal))
                    throw new InvalidOperationException("The original Microsoft WindowsDesktop assemblies are required.");
            Directory.CreateDirectory(root);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            if (!VisualStyleRenderer.IsSupported) throw new InvalidOperationException("Native visual styles are unavailable.");
            Stopwatch elapsed = Stopwatch.StartNew();
            List<object> cases = new();
            int renderedCases = 0;
            long bytes = 0;
            foreach (var item in Inventory())
            foreach (Color background in new[] { Color.White, SystemColors.Control })
            {
                if (elapsed.Elapsed > TimeSpan.FromSeconds(45) || cases.Count >= 128)
                    throw new TimeoutException("Original reference count/time bound exceeded.");
                VisualStyleElement element = VisualStyleElement.CreateElement(item.Class, item.Part, item.State);
                if (!VisualStyleRenderer.IsElementDefined(element))
                {
                    cases.Add(new { item.Class, item.Part, item.State, item.Width, item.Height,
                        background = background.ToArgb(), defined = false, rendered = false });
                    continue;
                }
                VisualStyleRenderer renderer = new(element);
                using Bitmap bitmap = new(item.Width, item.Height, PixelFormat.Format32bppArgb);
                bitmap.SetResolution(96, 96);
                using Graphics graphics = Graphics.FromImage(bitmap);
                Rectangle bounds = new(0, 0, item.Width, item.Height);
                graphics.Clear(background);
                renderer.DrawBackground(graphics, bounds);
                int drawResult = renderer.LastHResult;
                if (drawResult < 0) throw new InvalidOperationException($"Native theme draw failed: {drawResult:X8}");
                string file = $"{cases.Count:D3}-{item.Class}-{item.Part}-{item.State}.png";
                string path = Path.Combine(root, file);
                bitmap.Save(path, ImageFormat.Png);
                bytes += new FileInfo(path).Length;
                if (bytes > 16 * 1024 * 1024) throw new InvalidOperationException("Reference image budget exceeded.");
                var colors = Enum.GetValues<ColorProperty>().Select(property => Observe(property.ToString(),
                    () => renderer.GetColor(property).ToArgb(), renderer)).ToArray();
                var enums = Enum.GetValues<EnumProperty>().Select(property => Observe(property.ToString(),
                    () => renderer.GetEnumValue(property), renderer)).ToArray();
                var integers = Enum.GetValues<IntegerProperty>().Select(property => Observe(property.ToString(),
                    () => renderer.GetInteger(property), renderer)).ToArray();
                renderedCases++;
                cases.Add(new { item.Class, item.Part, item.State, item.Width, item.Height, defined = true, rendered = true,
                    background = background.ToArgb(), targetDpi = new[] { graphics.DpiX, graphics.DpiY }, drawResult,
                    content = Observe("content", () => Rect(renderer.GetBackgroundContentRectangle(graphics, bounds)), renderer),
                    margins = Observe("contentMargins", () =>
                    {
                        Padding p = renderer.GetMargins(graphics, MarginProperty.ContentMargins);
                        return new[] { p.Left, p.Top, p.Right, p.Bottom };
                    }, renderer),
                    partSize = Observe("trueSize", () =>
                    {
                        Size s = renderer.GetPartSize(graphics, ThemeSizeType.True);
                        return new[] { s.Width, s.Height };
                    }, renderer),
                    colors, enums, integers, file, sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) });
            }
            if (cases.Count != 120) throw new InvalidOperationException("Incomplete original reference inventory.");
            var result = new { schema = "windows-theme-reference-v2", qualified = false, injectedInput = false,
                runtime = Environment.Version.ToString(), os = Environment.OSVersion.VersionString,
                formsAssembly = typeof(Form).Assembly.Location, drawingAssembly = typeof(Bitmap).Assembly.Location,
                theme = VisualStyleInformation.DisplayName, scheme = VisualStyleInformation.ColorScheme,
                size = VisualStyleInformation.Size, elapsedSeconds = elapsed.Elapsed.TotalSeconds, bytes, renderedCases, cases };
            File.WriteAllText(Path.Combine(root, "reference.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static int[] Rect(Rectangle value) => [value.X, value.Y, value.Width, value.Height];

    private static object Observe<T>(string property, Func<T> read, VisualStyleRenderer renderer)
    {
        try { T value = read(); return new { property, hresult = renderer.LastHResult, value }; }
        catch (Exception error) { return new { property, error = error.GetType().FullName, error.Message }; }
    }

    private static IEnumerable<(string Class, int Part, int State, int Width, int Height)> Inventory()
    {
        for (int part = 1; part <= 3; part++)
        for (int state = 1; state <= 3; state++) yield return ("HEADER", part, state, 100, 23);
        for (int state = 1; state <= 3; state++) yield return ("HEADER", 1, state, 11, 7);
        for (int state = 1; state <= 16; state++) yield return ("SCROLLBAR", 1, state, 17, 17);
        for (int part = 2; part <= 9; part++)
        for (int state = 1; state <= 4; state++)
        {
            bool vertical = part is 3 or 6 or 7 or 9;
            int length = part is >= 4 and <= 7 ? 120 : 36;
            yield return ("SCROLLBAR", part, state, vertical ? 17 : length, vertical ? length : 17);
        }
    }
}
