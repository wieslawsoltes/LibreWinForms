// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Text.Json;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class WindowsThemeBitmapSourceTests
{
    public static bool IsHostedWindows => OperatingSystem.IsWindows()
        && Environment.GetEnvironmentVariable("LIBREWINFORMS_NATIVE_THEME_TEST") == "1";

    [Fact(Skip = "Requires the original hosted Windows theme and explicit no-input validation.", SkipUnless = nameof(IsHostedWindows))]
    public void LiveNativeHorizontalThumbMatchesAllOriginalFramesThroughDefaultService()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WindowsThemeThumbAssets");
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "receipt.json")));
        int compared = 0;
        foreach (JsonElement item in receipt.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (item.GetProperty("part").GetInt32() != 2) continue;
            JsonElement expected = item.GetProperty("expected");
            using Bitmap original = new(Path.Combine(directory, expected.GetProperty("file").GetString()!));
            using Bitmap actual = new(original.Width, original.Height);
            actual.SetResolution(96, 96);
            int state = item.GetProperty("state").GetInt32();
            using (Graphics graphics = Graphics.FromImage(actual))
            {
                graphics.Clear(Color.FromArgb(expected.GetProperty("background").GetInt32()));
                Assert.True(WindowsThemeBitmapSource.TryDraw(graphics, "SCROLLBAR", 2, state,
                    new Rectangle(Point.Empty, actual.Size), null));
                // Repaint through the public default to verify integration,
                // rather than accepting a component that is never selected.
                graphics.Clear(Color.FromArgb(expected.GetProperty("background").GetInt32()));
                new ProGpuVisualStyleService().DrawBackground(graphics, "SCROLLBAR", 2, state,
                    new Rectangle(Point.Empty, actual.Size), null);
            }

            for (int y = 0; y < original.Height; y++)
            for (int x = 0; x < original.Width; x++)
                Assert.Equal(original.GetPixel(x, y).ToArgb(), actual.GetPixel(x, y).ToArgb());
            compared++;
        }

        Assert.Equal(8, compared);
    }

    [Fact]
    public void NativeOpaqueRgbKeepsChannelOrderRowsAndIndependentLifetime()
    {
        byte[] pixels = [0, 0, 255, 0, 0, 255, 0, 127, 255, 0, 0, 23, 255, 255, 255, 255];
        using Bitmap image = WindowsThemeBitmapSource.CreateOpaqueImage(2, 2, pixels);
        Array.Clear(pixels);
        Assert.Equal(Color.Red.ToArgb(), image.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), image.GetPixel(1, 0).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), image.GetPixel(0, 1).ToArgb());
        Assert.Equal(Color.White.ToArgb(), image.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void NativeOpaqueRgbRejectsPartialOrOverflowingImages()
    {
        Assert.Throws<ArgumentException>(() => WindowsThemeBitmapSource.CreateOpaqueImage(2, 2, new byte[15]));
        Assert.Throws<ArgumentException>(() => WindowsThemeBitmapSource.CreateOpaqueImage(int.MaxValue, 2, new byte[16]));
        Assert.Throws<ArgumentException>(() => WindowsThemeBitmapSource.CreateOpaqueImage(0, 1, []));
    }

    [Fact]
    public void NativeThumbDoesNotAdmitAnotherPartOrDpiOrMutateCallerPaint()
    {
        using Bitmap target = new(40, 20);
        target.SetResolution(144, 144);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Magenta);
            Assert.False(WindowsThemeBitmapSource.TryDraw(graphics, "SCROLLBAR", 2, 1, new(0, 0, 36, 17), null));
        }

        target.SetResolution(96, 96);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            Assert.False(WindowsThemeBitmapSource.TryDraw(graphics, "SCROLLBAR", 3, 1, new(0, 0, 17, 36), null));
            Assert.False(WindowsThemeBitmapSource.TryDraw(graphics, "SCROLLBAR", 2, 5, new(0, 0, 36, 17), null));
            Assert.False(WindowsThemeBitmapSource.TryDraw(graphics, "HEADER", 2, 1, new(0, 0, 36, 17), null));
        }

        for (int y = 0; y < target.Height; y++)
        for (int x = 0; x < target.Width; x++) Assert.Equal(Color.Magenta.ToArgb(), target.GetPixel(x, y).ToArgb());
    }
}
