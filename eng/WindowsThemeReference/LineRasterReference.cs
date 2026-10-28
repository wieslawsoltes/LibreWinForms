// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;

internal static class LineRasterReference
{
    // Independent original GDI+ observations. No desktop windows or input.
    internal static int Run(string output)
    {
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
        Directory.CreateDirectory(root);
        Stopwatch clock = Stopwatch.StartNew();
        List<object> cases = new();
        long bytes = 0;
        // Original endpoints remain separate from the device transform and clip.
        var inputs = new[]
        {
            new { Name = "horizontal", X1 = 4f, Y1 = 8f, X2 = 25f, Y2 = 8f, Width = 1f, Sx = 1f, Sy = 1f, Tx = 0f, Ty = 0f, Clip = false },
            new { Name = "reverse-horizontal", X1 = 25f, Y1 = 8f, X2 = 4f, Y2 = 8f, Width = 1f, Sx = 1f, Sy = 1f, Tx = 0f, Ty = 0f, Clip = false },
            new { Name = "vertical", X1 = 8f, Y1 = 4f, X2 = 8f, Y2 = 25f, Width = 1f, Sx = 1f, Sy = 1f, Tx = 0f, Ty = 0f, Clip = false },
            new { Name = "diagonal", X1 = 4f, Y1 = 5f, X2 = 25f, Y2 = 21f, Width = 1f, Sx = 1f, Sy = 1f, Tx = 0f, Ty = 0f, Clip = false },
            new { Name = "wide", X1 = 4f, Y1 = 8f, X2 = 25f, Y2 = 8f, Width = 2f, Sx = 1f, Sy = 1f, Tx = 0f, Ty = 0f, Clip = false },
            new { Name = "fractional-translation", X1 = 4f, Y1 = 8f, X2 = 25f, Y2 = 8f, Width = 1f, Sx = 1f, Sy = 1f, Tx = .25f, Ty = .75f, Clip = false },
            new { Name = "nonuniform-scale", X1 = 2f, Y1 = 5f, X2 = 12f, Y2 = 5f, Width = 1f, Sx = 2f, Sy = 3f, Tx = 1f, Ty = 2f, Clip = false },
            new { Name = "device-clip", X1 = 4f, Y1 = 8f, X2 = 25f, Y2 = 8f, Width = 1f, Sx = 1f, Sy = 1f, Tx = 2f, Ty = 0f, Clip = true }
        };
        foreach (var input in inputs)
        foreach (SmoothingMode requested in new[] { SmoothingMode.Default, SmoothingMode.None, SmoothingMode.AntiAlias })
        foreach (PixelOffsetMode pixelOffset in new[] { PixelOffsetMode.Default, PixelOffsetMode.Half })
        {
            CheckBudget();
            using Bitmap bitmap = new(32, 32, PixelFormat.Format32bppArgb);
            bitmap.SetResolution(96, 96);
            SmoothingMode initial, actual;
            PixelOffsetMode actualPixelOffset;
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new(Color.FromArgb(100, 100, 100), input.Width))
            using (Matrix transform = new(input.Sx, 0, 0, input.Sy, input.Tx, input.Ty))
            {
                initial = graphics.SmoothingMode;
                graphics.Clear(Color.White);
                if (input.Clip) graphics.SetClip(new Rectangle(9, 6, 11, 5));
                graphics.Transform = transform;
                graphics.SmoothingMode = requested;
                graphics.PixelOffsetMode = pixelOffset;
                actual = graphics.SmoothingMode;
                actualPixelOffset = graphics.PixelOffsetMode;
                graphics.DrawLine(pen, input.X1, input.Y1, input.X2, input.Y2);
            }
            string name = $"{cases.Count:D2}-{input.Name}-{requested}-{pixelOffset}.png";
            string path = Path.Combine(root, name);
            bitmap.Save(path, ImageFormat.Png);
            byte[] encoded = File.ReadAllBytes(path);
            bytes += encoded.Length;
            cases.Add(new { input, requested = requested.ToString(), initial = initial.ToString(), actual = actual.ToString(),
                requestedPixelOffset = pixelOffset.ToString(), actualPixelOffset = actualPixelOffset.ToString(),
                image = new { name, bytes = encoded.Length, sha256 = Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant() } });
        }
        CheckBudget();
        if (cases.Count != 48) throw new InvalidOperationException("Incomplete original line inventory.");
        File.WriteAllText(Path.Combine(root, "line-raster-reference.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, runtime = Environment.Version.ToString(), operatingSystem = Environment.OSVersion.ToString(),
            framework = typeof(Graphics).Assembly.Location, elapsedSeconds = clock.Elapsed.TotalSeconds,
            inputEvents = 0, width = 32, height = 32, dpi = 96, cases
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;

        void CheckBudget()
        {
            if (clock.Elapsed.TotalSeconds > 45 || cases.Count > 48 || bytes > 16 * 1024 * 1024)
                throw new TimeoutException("Original line observation budget exceeded.");
        }
    }
}
