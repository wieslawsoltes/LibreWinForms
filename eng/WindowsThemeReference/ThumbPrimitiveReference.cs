// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms.VisualStyles;

internal static class ThumbPrimitiveReference
{
    // Separate diagnostic: the original 120-request theme inventory is unchanged.
    internal static int Run(string output, PixelOffsetMode pixelOffset = PixelOffsetMode.Default)
    {
        if (pixelOffset is not (PixelOffsetMode.Default or PixelOffsetMode.Half))
            throw new ArgumentOutOfRangeException(nameof(pixelOffset));
        if (!OperatingSystem.IsWindows()
            || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
            throw new InvalidOperationException("Disposable hosted Windows reference only.");
        string root = Path.GetFullPath(output);
        string temporary = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP")!);
        if (output != root || !root.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(root) || File.Exists(root))
            throw new InvalidOperationException("Evidence must be a new absolute RUNNER_TEMP directory.");
        foreach (Type type in new[] { typeof(Form), typeof(Bitmap) })
            if (!type.Assembly.Location.Contains("Microsoft.WindowsDesktop.App", StringComparison.Ordinal))
                throw new InvalidOperationException("Original Microsoft framework required.");
        if (!Application.SetHighDpiMode(HighDpiMode.PerMonitorV2))
            throw new InvalidOperationException("Original physical DPI policy unavailable.");
        Application.EnableVisualStyles();
        if (!VisualStyleRenderer.IsSupported) throw new InvalidOperationException("Native themes unavailable.");
        Directory.CreateDirectory(root);
        Stopwatch clock = Stopwatch.StartNew();
        List<object> cases = new();
        int imageCount = 0;
        long bytes = 0;
        for (int part = 2; part <= 3; part++)
        for (int state = 1; state <= 3; state++)
        foreach (Color background in new[] { Color.White, SystemColors.Control })
        {
            CheckBudget();
            bool horizontal = part == 2;
            int width = horizontal ? 36 : 17, height = horizontal ? 17 : 36;
            float thickness = state == 1 ? 2 : 6;
            RectangleF pill = horizontal
                ? new(0, (height + 1 - thickness) / 2, width, thickness)
                : new((width + 1 - thickness) / 2, 0, thickness, height);
            VisualStyleElement element = VisualStyleElement.CreateElement("SCROLLBAR", part, state);
            if (!VisualStyleRenderer.IsElementDefined(element))
                throw new InvalidOperationException($"Original thumb part/state undefined: {part}/{state}");
            VisualStyleRenderer theme = new(element);
            using Bitmap native = new(width, height, PixelFormat.Format32bppArgb);
            native.SetResolution(96, 96);
            using (Graphics graphics = Graphics.FromImage(native))
            {
                graphics.Clear(background);
                theme.DrawBackground(graphics, new Rectangle(0, 0, width, height));
                if (theme.LastHResult < 0) throw new InvalidOperationException("Original native theme draw failed.");
            }

            object nativeImage = Save(native, $"{cases.Count:D2}-theme.png");
            List<object> candidates = new();
            foreach (SmoothingMode mode in new[] { SmoothingMode.AntiAlias, SmoothingMode.HighQuality })
            {
                CheckBudget();
                using Bitmap candidate = new(width, height, PixelFormat.Format32bppArgb);
                candidate.SetResolution(96, 96);
                PixelOffsetMode actualPixelOffset;
                using (Graphics graphics = Graphics.FromImage(candidate))
                using (SolidBrush body = new(Color.FromArgb(240, 240, 240)))
                using (SolidBrush edge = new(Color.White))
                using (SolidBrush foreground = new(Color.FromArgb(133, 133, 133)))
                {
                    graphics.Clear(background);
                    graphics.SmoothingMode = mode;
                    if (pixelOffset != PixelOffsetMode.Default) graphics.PixelOffsetMode = pixelOffset;
                    actualPixelOffset = graphics.PixelOffsetMode;
                    graphics.FillRectangle(body, 0, 0, width, height);
                    graphics.FillRectangle(edge, 0, 0, horizontal ? width : 1, horizontal ? 1 : height);
                    graphics.FillRoundedRectangle(foreground, pill, new SizeF(thickness, thickness));
                }

                List<object> differences = new();
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int expected = native.GetPixel(x, y).ToArgb(), actual = candidate.GetPixel(x, y).ToArgb();
                    if (expected != actual) differences.Add(new { x, y, expected, actual });
                }

                candidates.Add(new { smoothing = mode.ToString(), pixelOffset = actualPixelOffset.ToString(),
                    image = Save(candidate, $"{cases.Count:D2}-{mode}.png"),
                    differentPixels = differences.Count, differences });
            }

            cases.Add(new { part, state, width, height, background = background.ToArgb(),
                pill = new[] { pill.X, pill.Y, pill.Width, pill.Height }, thickness, nativeImage, candidates });
        }

        if (cases.Count != 12 || imageCount != 36) throw new InvalidOperationException("Incomplete primitive inventory.");
        var receipt = new { schema = "windows-thumb-primitive-reference-v1", qualified = false,
            injectedInput = false, diagnosticOnly = true, runtime = Environment.Version.ToString(),
            os = Environment.OSVersion.VersionString, formsAssembly = typeof(Form).Assembly.Location,
            drawingAssembly = typeof(Bitmap).Assembly.Location, dpi = 96, pixelOffset = pixelOffset.ToString(),
            theme = VisualStyleInformation.DisplayName, scheme = VisualStyleInformation.ColorScheme,
            size = VisualStyleInformation.Size, elapsedSeconds = clock.Elapsed.TotalSeconds, bytes, imageCount, cases };
        File.WriteAllText(Path.Combine(root, "reference.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        return 0; // Successful observation records differences; it does not qualify a candidate.

        void CheckBudget()
        {
            if (clock.Elapsed.TotalSeconds > 45 || imageCount > 36 || bytes > 16 * 1024 * 1024)
                throw new TimeoutException("Primitive observation budget exceeded.");
        }

        object Save(Bitmap bitmap, string file)
        {
            CheckBudget();
            if (imageCount >= 36) throw new InvalidOperationException("Primitive image count exceeded.");
            string path = Path.Combine(root, file);
            bitmap.Save(path, ImageFormat.Png);
            imageCount++;
            bytes += new FileInfo(path).Length;
            CheckBudget();
            return new { file, sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) };
        }
    }
}
