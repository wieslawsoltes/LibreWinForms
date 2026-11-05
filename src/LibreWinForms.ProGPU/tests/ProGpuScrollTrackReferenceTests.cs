// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuScrollTrackReferenceTests
{
    public static IEnumerable<object[]> OriginalCases => Enumerable.Range(0, 48).Select(index => new object[] { index });

    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void TrackAndGripperPaintMatchesCompleteOriginalMicrosoftFrame(int index)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WindowsScrollTrackReference");
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "receipt.json")));
        JsonElement cases = receipt.RootElement.GetProperty("cases");
        Assert.Equal(48, cases.GetArrayLength());
        JsonElement item = cases[index];
        int part = item.GetProperty("Part").GetInt32(), state = item.GetProperty("State").GetInt32();
        Assert.InRange(part, 4, 9);
        Assert.InRange(state, 1, 4);
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
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(9)]
    public void PaintRetainsCallerTransformAndIntersection(int part)
    {
        using Bitmap target = new(20, 20);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Magenta);
            graphics.TranslateTransform(3, 4);
            graphics.SetClip(new Rectangle(0, 0, 7, 5));
            new ProGpuVisualStyleService().DrawBackground(graphics, "scrollbar", part, 3,
                new Rectangle(1, 1, 8, 7), new Rectangle(1, 0, 4, 7));
            Assert.Equal(new RectangleF(0, 0, 7, 5), graphics.ClipBounds);
            using var marker = new SolidBrush(Color.Lime);
            graphics.FillRectangle(marker, 0, 0, 1, 1);
        }

        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 20; x++)
        {
            Color expected = x == 3 && y == 4 ? Color.Lime
                : part < 8 && x >= 4 && x <= 7 && y >= 5 && y <= 8
                    ? (part == 4 && y == 5) || (part == 6 && x == 4) ? Color.White : Color.FromArgb(240, 240, 240)
                    : Color.Magenta;
            Assert.Equal(expected.ToArgb(), target.GetPixel(x, y).ToArgb());
        }
    }
}
