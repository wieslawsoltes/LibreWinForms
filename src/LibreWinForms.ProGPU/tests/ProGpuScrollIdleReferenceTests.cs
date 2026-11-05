// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuScrollIdleReferenceTests
{
    public static IEnumerable<object[]> OriginalCases => Enumerable.Range(0, 12).Select(index => new object[] { index });

    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void IdlePaintMatchesCompleteOriginalMicrosoftFrame(int index)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WindowsScrollIdleReference");
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "receipt.json")));
        JsonElement cases = receipt.RootElement.GetProperty("cases");
        Assert.Equal(12, cases.GetArrayLength());
        JsonElement item = cases[index];
        int part = item.GetProperty("Part").GetInt32(), state = item.GetProperty("State").GetInt32();
        Assert.True((part == 1 && state is 1 or 5 or 9 or 13) || (part is 2 or 3 && state == 4));
        Assert.True(item.GetProperty("defined").GetBoolean());
        Assert.Equal(0, item.GetProperty("drawResult").GetInt32());
        string path = Path.Combine(directory, item.GetProperty("file").GetString()!);
        Assert.Equal(item.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        using Bitmap original = new(path);
        int width = item.GetProperty("Width").GetInt32(), height = item.GetProperty("Height").GetInt32();
        Assert.Equal(new Size(width, height), original.Size);
        using Bitmap painted = new(width, height);
        using (Graphics graphics = Graphics.FromImage(painted))
        {
            graphics.Clear(Color.FromArgb(item.GetProperty("background").GetInt32()));
            new ProGpuVisualStyleService().DrawBackground(graphics, "SCROLLBAR", part, state,
                new Rectangle(0, 0, width, height), null);
        }

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            Assert.Equal(original.GetPixel(x, y).ToArgb(), painted.GetPixel(x, y).ToArgb());
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 5, false)]
    [InlineData(1, 9, true)]
    [InlineData(1, 13, true)]
    [InlineData(2, 4, true)]
    [InlineData(3, 4, false)]
    public void PaintRetainsCallerTransformAndIntersection(int part, int state, bool horizontal)
    {
        using Bitmap target = new(20, 20);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Magenta);
            graphics.TranslateTransform(3, 4);
            graphics.SetClip(new Rectangle(0, 0, 7, 5));
            new ProGpuVisualStyleService().DrawBackground(graphics, "scrollbar", part, state,
                new Rectangle(1, 1, 8, 7), new Rectangle(1, 0, 4, 7));
            Assert.Equal(new RectangleF(0, 0, 7, 5), graphics.ClipBounds);
            using var marker = new SolidBrush(Color.Lime);
            graphics.FillRectangle(marker, 0, 0, 1, 1);
        }

        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 20; x++)
        {
            Color expected = x == 3 && y == 4 ? Color.Lime
                : x >= 4 && x <= 7 && y >= 5 && y <= 8
                    ? (horizontal && y == 5) || (!horizontal && x == 4) ? Color.White : Color.FromArgb(240, 240, 240)
                    : Color.Magenta;
            Assert.Equal(expected.ToArgb(), target.GetPixel(x, y).ToArgb());
        }
    }
}
