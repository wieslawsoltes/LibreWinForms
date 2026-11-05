// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuHeaderReferenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void HeaderPaintMatchesOriginalMicrosoftFrame(int index)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WindowsHeaderReference");
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "receipt.json")));
        JsonElement cases = receipt.RootElement.GetProperty("cases");
        Assert.Equal(12, cases.GetArrayLength());
        JsonElement item = cases[index];
        string path = Path.Combine(directory, item.GetProperty("file").GetString()!);
        Assert.Equal(item.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        using Bitmap original = new(path);
        int width = item.GetProperty("Width").GetInt32(), height = item.GetProperty("Height").GetInt32();
        Assert.Equal(new Size(width, height), original.Size);
        Color background = Color.FromArgb(item.GetProperty("background").GetInt32());
        var service = new ProGpuVisualStyleService();
        using Bitmap painted = new(width, height);
        using (Graphics graphics = Graphics.FromImage(painted))
        {
            graphics.Clear(background);
            service.DrawBackground(graphics, "HEADER", 1, item.GetProperty("State").GetInt32(),
                new Rectangle(0, 0, width, height), null);
        }

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            Assert.Equal(original.GetPixel(x, y).ToArgb(), painted.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void HeaderPaintPreservesCallerTransformAndIntersectedClip()
    {
        var service = new ProGpuVisualStyleService();
        using Bitmap target = new(20, 20);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Magenta);
            graphics.TranslateTransform(3, 4);
            graphics.SetClip(new Rectangle(1, 1, 7, 5));
            service.DrawBackground(graphics, "HEADER", 1, 1, new Rectangle(0, 0, 8, 7), new Rectangle(4, 0, 4, 7));
            Assert.Equal(new RectangleF(1, 1, 7, 5), graphics.ClipBounds);
            using var marker = new SolidBrush(Color.Lime);
            graphics.FillRectangle(marker, 1, 1, 1, 1);
        }

        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 20; x++)
        {
            Color expected = x == 4 && y == 5 ? Color.Lime
                : x >= 7 && x <= 10 && y >= 5 && y <= 9
                    ? x == 10 ? Color.FromArgb(229, 229, 229) : Color.White
                    : Color.Magenta;
            Assert.Equal(expected.ToArgb(), target.GetPixel(x, y).ToArgb());
        }
    }
}
