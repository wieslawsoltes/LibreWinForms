// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuSingleLineHeightTests
{
    public static IEnumerable<object[]> ShortLines()
    {
        foreach (LibreTextFormat alignment in new[] { LibreTextFormat.Default, LibreTextFormat.VerticalCenter, LibreTextFormat.Bottom })
        {
            foreach (LibreTextFormat padding in new[] { LibreTextFormat.Default, LibreTextFormat.NoPadding, LibreTextFormat.LeftAndRightPadding })
            {
                foreach (LibreTextFormat trimming in new[] { LibreTextFormat.Default, LibreTextFormat.EndEllipsis, LibreTextFormat.WordEllipsis })
                {
                    yield return new object[] { alignment, padding, trimming };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ShortLines))]
    public void ShortSingleLineRetainsTheClippedFullLine(LibreTextFormat alignment, LibreTextFormat padding, LibreTextFormat trimming)
    {
        var service = new ProGpuTextRendererService();
        using Font font = new(FontFamily.GenericSansSerif, 24, FontStyle.Regular, GraphicsUnit.Pixel);
        using var actual = new Bitmap(180, 90);
        using var expected = new Bitmap(180, 90);
        Rectangle bounds = new(20, 35, 140, 18);
        LibreTextFormat flags = LibreTextFormat.SingleLine | LibreTextFormat.NoPrefix | alignment | padding | trimming;
        using (Graphics graphics = Graphics.FromImage(actual))
        using (Graphics reference = Graphics.FromImage(expected))
        {
            float lineHeight = font.GetHeight(graphics);
            lineHeight.Should().BeGreaterThan(bounds.Height);
            service.DrawText(graphics, "alice", font, bounds, Color.Black, Color.Empty, flags);

            // Independently draw the full-height line, crop it to the original
            // viewport, and move the crop relative to its top/center/bottom.
            int height = (int)MathF.Ceiling(lineHeight);
            int factor = padding == LibreTextFormat.LeftAndRightPadding ? 2 : 1;
            int left = padding == LibreTextFormat.NoPadding ? 0 : (int)Math.Ceiling(height / 6f * factor);
            int right = padding == LibreTextFormat.NoPadding ? 0 : (int)Math.Ceiling(height / 6f * (factor + 0.5f));
            float offset = alignment == LibreTextFormat.Bottom ? bounds.Height - lineHeight
                : alignment == LibreTextFormat.VerticalCenter ? (bounds.Height - lineHeight) / 2 : 0;
            reference.SetClip(bounds);
            using var format = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.NoClip);
            using var brush = new SolidBrush(Color.Black);
            reference.DrawString("alice", font, brush,
                new RectangleF(bounds.X + left, bounds.Y + offset, bounds.Width - left - right, lineHeight), format);
        }

        AssertClippedPixels(actual, expected, bounds);
    }

    [Theory]
    [InlineData(LibreTextFormat.Default)]
    [InlineData(LibreTextFormat.EndEllipsis)]
    [InlineData(LibreTextFormat.WordEllipsis)]
    [InlineData(LibreTextFormat.PathEllipsis)]
    public void SingleLineMeasurementDoesNotTruncateToTheProposedHeight(LibreTextFormat trimming)
    {
        var service = new ProGpuTextRendererService();
        using var bitmap = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using Font font = new(FontFamily.GenericSansSerif, 24, FontStyle.Regular, GraphicsUnit.Pixel);
        LibreTextFormat flags = LibreTextFormat.SingleLine | LibreTextFormat.NoPrefix | trimming;
        Size full = service.MeasureText(graphics, "alice", font, new Size(140, int.MaxValue), flags);
        full.Height.Should().BeGreaterThan(18);
        foreach (int height in new[] { 1, 18, 40 })
        {
            service.MeasureText(graphics, "alice", font, new Size(140, height), flags).Should().Be(full);
        }
    }

    [Theory]
    [InlineData(LibreTextFormat.EndEllipsis, StringTrimming.EllipsisCharacter)]
    [InlineData(LibreTextFormat.WordEllipsis, StringTrimming.EllipsisWord)]
    [InlineData(LibreTextFormat.PathEllipsis, StringTrimming.EllipsisPath)]
    public void ShortLineKeepsHorizontalEllipsisAndTheCallerClip(LibreTextFormat trimming, StringTrimming drawingTrimming)
    {
        var service = new ProGpuTextRendererService();
        using Font font = new(FontFamily.GenericSansSerif, 24, FontStyle.Regular, GraphicsUnit.Pixel);
        using var actual = new Bitmap(180, 90);
        using var expected = new Bitmap(180, 90);
        Rectangle bounds = new(20, 35, 80, 18);
        const string text = "C:\\alpha\\beta\\longfilename.txt";
        using (Graphics graphics = Graphics.FromImage(actual))
        using (Graphics reference = Graphics.FromImage(expected))
        {
            service.DrawText(graphics, text, font, bounds, Color.Black, Color.Empty,
                LibreTextFormat.SingleLine | LibreTextFormat.NoPrefix | LibreTextFormat.NoPadding | trimming);
            reference.SetClip(bounds);
            using var format = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.NoClip)
            {
                Trimming = drawingTrimming,
            };
            using var brush = new SolidBrush(Color.Black);
            reference.DrawString(text, font, brush,
                new RectangleF(bounds.X, bounds.Y, bounds.Width, font.GetHeight(reference)), format);
        }

        AssertClippedPixels(actual, expected, bounds);
    }

    private static void AssertClippedPixels(Bitmap actual, Bitmap expected, Rectangle bounds)
    {
        // Read each complete real GPU bitmap once; GetPixel would read back
        // the whole texture again for every compared pixel.
        byte[] pixels = ReadPixels(actual);
        pixels.Should().Equal(ReadPixels(expected), "every clipped full-line pixel must agree");
        int ink = 0;
        for (int y = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                if (pixels[((y * actual.Width) + x) * 4 + 3] != 0)
                {
                    bounds.Contains(x, y).Should().BeTrue("the original caller viewport remains authoritative");
                    ink++;
                }
            }
        }

        ink.Should().BeGreaterThan(0, "a short cell clips text instead of trimming away its entire line");
    }

    private static byte[] ReadPixels(Bitmap bitmap)
    {
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = checked(bitmap.Width * 4);
            byte[] pixels = new byte[checked(rowBytes * bitmap.Height)];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);
            }

            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
