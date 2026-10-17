// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuThemeBitmapRendererTests
{
    public static IEnumerable<object[]> OriginalFrames => Enumerable.Range(0, 16).Select(i => new object[] { i });

    public static IEnumerable<object[]> OriginalArrowFrames => Enumerable.Range(0, 32).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(OriginalArrowFrames))]
    public void ThemeBitmapRecordsOriginalArrowResourcePair(int index)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WindowsThemeArrowAssets");
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "receipt.json")));
        JsonElement cases = receipt.RootElement.GetProperty("cases"), sources = receipt.RootElement.GetProperty("sources");
        Assert.Equal(32, cases.GetArrayLength());
        Assert.Equal(2, sources.GetArrayLength());
        string VerifiedPath(JsonElement descriptor)
        {
            string path = Path.Combine(directory, descriptor.GetProperty("file").GetString()!);
            Assert.Equal(descriptor.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            return path;
        }

        // These are immutable original source PNG resources, including actual
        // transparent coverage; no image is synthesized from reference pixels.
        using Bitmap background = new(VerifiedPath(sources[0]));
        using Bitmap selected = new(VerifiedPath(sources[1]));
        Assert.Equal(new Size(17, 340), background.Size);
        Assert.Equal(new Size(13, 260), selected.Size);
        JsonElement item = cases[index];
        int state = item.GetProperty("state").GetInt32();
        using Bitmap original = new(VerifiedPath(item));
        Assert.Equal(new Size(17, 17), original.Size);
        using Bitmap result = new(17, 17);
        using (Graphics graphics = Graphics.FromImage(result))
        {
            graphics.Clear(Color.FromArgb(item.GetProperty("background").GetInt32()));
            Assert.True(ProGpuThemeBitmapRenderer.TryDraw(graphics, background,
                new Rectangle(0, (state - 1) * 17, 17, 17), new(8, 8, 8, 8), new(0, 0, 17, 17), null));
            Assert.True(ProGpuThemeBitmapRenderer.TryDraw(graphics, selected,
                new Rectangle(0, (state - 1) * 13, 13, 13), default, new(2, 2, 13, 13), null));
        }

        background.Dispose();
        selected.Dispose();
        for (int y = 0; y < 17; y++)
        for (int x = 0; x < 17; x++)
            Assert.Equal(original.GetPixel(x, y).ToArgb(), result.GetPixel(x, y).ToArgb());
    }

    [Theory]
    [MemberData(nameof(OriginalFrames))]
    public void ThemeBitmapRetainsOriginalNativeThumbFrame(int index)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WindowsThemeThumbAssets");
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "receipt.json")));
        JsonElement cases = receipt.RootElement.GetProperty("cases");
        Assert.Equal(16, cases.GetArrayLength());
        JsonElement item = cases[index], asset = item.GetProperty("asset"), expected = item.GetProperty("expected");
        int width = asset.GetProperty("width").GetInt32(), height = -asset.GetProperty("signedHeight").GetInt32();
        byte[] data = File.ReadAllBytes(Path.Combine(directory, asset.GetProperty("file").GetString()!));
        Assert.Equal(asset.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
        Assert.Equal(width * 4, asset.GetProperty("stride").GetInt32());
        Assert.Equal(width * height * 4, data.Length);
        for (int i = 3; i < data.Length; i += 4) Assert.Equal(255, data[i]);
        using Bitmap image = new(width, height, PixelFormat.Format32bppArgb);
        BitmapData target = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++) Marshal.Copy(data, y * width * 4, target.Scan0 + y * target.Stride, width * 4);
        }
        finally { image.UnlockBits(target); }
        int count = item.GetProperty("imageCount").GetInt32();
        Assert.Equal(0, height % count);
        int frameHeight = height / count;
        JsonElement margin = item.GetProperty("margins");
        LibreVisualStyleMargins margins = new(margin[0].GetInt32(), margin[1].GetInt32(), margin[2].GetInt32(), margin[3].GetInt32());
        string originalPath = Path.Combine(directory, expected.GetProperty("file").GetString()!);
        Assert.Equal(expected.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(originalPath))));
        using Bitmap original = new(originalPath);
        using Bitmap result = new(original.Width, original.Height);
        using (Graphics graphics = Graphics.FromImage(result))
        {
            graphics.Clear(Color.FromArgb(expected.GetProperty("background").GetInt32()));
            Assert.True(ProGpuThemeBitmapRenderer.TryDraw(graphics, image,
                new Rectangle(0, (item.GetProperty("state").GetInt32() - 1) * frameHeight, width, frameHeight),
                margins, new Rectangle(Point.Empty, original.Size), null));
        }

        // The eventual live source owns and retires this bitmap after recording;
        // the retained Graphics commands must own their actual image content.
        image.Dispose();

        for (int y = 0; y < original.Height; y++)
        for (int x = 0; x < original.Width; x++)
            Assert.Equal(original.GetPixel(x, y).ToArgb(), result.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void ThemeBitmapPreservesCallerTransformClipAndSampling()
    {
        using Bitmap image = new(3, 3);
        using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.Lime);
        using Bitmap target = new(12, 12);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Magenta);
            graphics.TranslateTransform(2, 3);
            graphics.SetClip(new Rectangle(0, 0, 6, 6));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Assert.True(ProGpuThemeBitmapRenderer.TryDraw(graphics, image, new Rectangle(0, 0, 3, 3),
                new(1, 1, 1, 1), new Rectangle(0, 0, 9, 9), new Rectangle(1, 1, 3, 4)));
            Assert.Equal(InterpolationMode.HighQualityBicubic, graphics.InterpolationMode);
            Assert.Equal(new RectangleF(0, 0, 6, 6), graphics.ClipBounds);
            using SolidBrush marker = new(Color.Red);
            graphics.FillRectangle(marker, 0, 0, 1, 1);
        }

        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 12; x++)
            Assert.Equal((x == 2 && y == 3 ? Color.Red
                : x >= 3 && x < 6 && y >= 4 && y < 8 ? Color.Lime : Color.Magenta).ToArgb(), target.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void ThemeBitmapRejectsInvalidFrameBeforePaintAndLeavesBorderContractionSeparate()
    {
        using Bitmap image = new(3, 3);
        using Bitmap target = new(4, 4);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Magenta);
            Assert.Throws<ArgumentException>(() => ProGpuThemeBitmapRenderer.TryDraw(graphics, image,
                new Rectangle(0, 1, 3, 3), new(1, 1, 1, 1), new Rectangle(0, 0, 4, 4), null));
            Assert.Throws<ArgumentException>(() => ProGpuThemeBitmapRenderer.TryDraw(graphics, image,
                new Rectangle(0, 0, 3, 3), new(2, 1, 1, 1), new Rectangle(0, 0, 4, 4), null));
            Assert.False(ProGpuThemeBitmapRenderer.TryDraw(graphics, image,
                new Rectangle(0, 0, 3, 3), new(1, 1, 1, 1), new Rectangle(0, 0, 1, 4), null));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProGpuThemeBitmapRenderer.TryDraw(graphics, image,
                new Rectangle(0, 0, 3, 3), new(1, 1, 1, 1), new Rectangle(int.MaxValue, 0, 4, 4), null));
        }

        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++) Assert.Equal(Color.Magenta.ToArgb(), target.GetPixel(x, y).ToArgb());
    }
}
