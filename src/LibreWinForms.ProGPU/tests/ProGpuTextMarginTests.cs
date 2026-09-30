// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuTextMarginTests
{
    private const LibreTextFormat Plain = LibreTextFormat.NoPrefix | LibreTextFormat.SingleLine;

    [Theory]
    [InlineData(12, GraphicsUnit.Pixel, 96)]
    [InlineData(12, GraphicsUnit.Pixel, 192)]
    [InlineData(24, GraphicsUnit.Pixel, 192)]
    [InlineData(12, GraphicsUnit.Point, 96)]
    [InlineData(12, GraphicsUnit.Point, 192)]
    public void MeasurementUsesRealizedHeightAndPaddingPrecedence(float size, GraphicsUnit unit, float dpi)
    {
        var service = new ProGpuTextRendererService();
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetResolution(dpi, dpi);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using Font font = new(FontFamily.GenericSansSerif, size, FontStyle.Regular, unit);
        int height = (int)MathF.Ceiling(font.GetHeight(graphics));
        Size proposed = new(int.MaxValue, int.MaxValue);
        Size bare = service.MeasureText(graphics, "Agjy 012345", font, proposed, Plain | LibreTextFormat.NoPadding);
        int normal = (int)Math.Ceiling(height / 6f) + (int)Math.Ceiling(height / 4f);
        int extra = (int)Math.Ceiling(height / 3f) + (int)Math.Ceiling(height / 6f * 2.5f);
        Size padded = service.MeasureText(graphics, "Agjy 012345", font, proposed, Plain);
        padded.Should().Be(new Size(bare.Width + normal, bare.Height));
        Size both = service.MeasureText(graphics, "Agjy 012345", font, proposed, Plain | LibreTextFormat.LeftAndRightPadding);
        both.Should().Be(new Size(bare.Width + extra, bare.Height));
        service.MeasureText(graphics, "Agjy 012345", font, proposed,
            Plain | LibreTextFormat.LeftAndRightPadding | LibreTextFormat.NoPadding).Should().Be(both);
        font.Size.Should().Be(size);
        font.Unit.Should().Be(unit);
    }

    [Theory]
    [InlineData(LibreTextFormat.Default)]
    [InlineData(LibreTextFormat.HorizontalCenter)]
    [InlineData(LibreTextFormat.Right)]
    [InlineData(LibreTextFormat.RightToLeft)]
    [InlineData(LibreTextFormat.RightToLeft | LibreTextFormat.Right)]
    [InlineData(LibreTextFormat.LeftAndRightPadding)]
    public void DrawUsesTheSameAsymmetricContentRectangle(LibreTextFormat flags)
    {
        var service = new ProGpuTextRendererService();
        using Font font = new(FontFamily.GenericSansSerif, 24, FontStyle.Italic, GraphicsUnit.Pixel);
        using var actual = new Bitmap(220, 70);
        using var expected = new Bitmap(220, 70);
        using (Graphics graphics = Graphics.FromImage(actual))
        using (Graphics reference = Graphics.FromImage(expected))
        {
            int height = (int)MathF.Ceiling(font.GetHeight(graphics));
            int factor = flags.HasFlag(LibreTextFormat.LeftAndRightPadding) ? 2 : 1;
            int left = (int)Math.Ceiling(height / 6f * factor);
            int right = (int)Math.Ceiling(height / 6f * (factor + 0.5f));
            Rectangle bounds = new(10, 10, 195, 50);
            service.DrawText(graphics, "Ab fj", font, bounds, Color.Black, Color.Empty, Plain | flags);
            Rectangle content = new(bounds.X + left, bounds.Y, bounds.Width - left - right, bounds.Height);
            reference.SetClip(bounds);
            service.DrawText(reference, "Ab fj", font, content, Color.Black, Color.Empty,
                Plain | (flags & ~LibreTextFormat.LeftAndRightPadding) | LibreTextFormat.NoPadding | LibreTextFormat.NoClipping);
        }

        int ink = 0;
        for (int y = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                Color pixel = actual.GetPixel(x, y);
                pixel.ToArgb().Should().Be(expected.GetPixel(x, y).ToArgb(), $"pixel ({x}, {y}) must use the padded content frame");
                if (pixel.A != 0)
                {
                    ink++;
                }
            }
        }

        ink.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrappingSubtractsMarginsBeforeLayout(bool extra)
    {
        var service = new ProGpuTextRendererService();
        using var bitmap = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using Font font = new(FontFamily.GenericSansSerif, 24, FontStyle.Regular, GraphicsUnit.Pixel);
        int height = (int)MathF.Ceiling(font.GetHeight(graphics));
        int factor = extra ? 2 : 1;
        int total = (int)Math.Ceiling(height / 6f * factor) + (int)Math.Ceiling(height / 6f * (factor + 0.5f));
        const string text = "Alpha beta gamma delta epsilon zeta";
        const LibreTextFormat flags = LibreTextFormat.WordBreak | LibreTextFormat.NoPrefix;
        Size bare = service.MeasureText(graphics, text, font, new Size(90, int.MaxValue), flags | LibreTextFormat.NoPadding);
        Size padded = service.MeasureText(graphics, text, font, new Size(90 + total, int.MaxValue),
            flags | (extra ? LibreTextFormat.LeftAndRightPadding : LibreTextFormat.Default));
        padded.Should().Be(new Size(bare.Width + total, bare.Height));
        padded.Height.Should().BeGreaterThan(height);
    }

    [Fact]
    public void ExhaustedPaddingKeepsBackgroundWithoutUnboundedText()
    {
        var service = new ProGpuTextRendererService();
        using var bitmap = new Bitmap(80, 60);
        using Font font = new(FontFamily.GenericSansSerif, 24, FontStyle.Regular, GraphicsUnit.Pixel);
        Rectangle bounds = new(10, 10, 1, 40);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            service.DrawText(graphics, "Overflow", font, bounds, Color.Black, Color.Yellow, Plain);
        }

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                Color pixel = bitmap.GetPixel(x, y);
                if (bounds.Contains(x, y))
                {
                    pixel.ToArgb().Should().Be(Color.Yellow.ToArgb());
                }
                else
                {
                    pixel.A.Should().Be(0);
                }
            }
        }
    }

    [Fact]
    public void EmptyTextDoesNotAcquireMargins()
    {
        var service = new ProGpuTextRendererService();
        foreach (LibreTextFormat padding in new[] { LibreTextFormat.Default, LibreTextFormat.NoPadding, LibreTextFormat.LeftAndRightPadding })
        {
            service.MeasureText(null, string.Empty, null, new Size(100, 100), Plain | padding).Should().Be(Size.Empty);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallerClipAndTransformSurviveDrawing(bool noClipping)
    {
        var service = new ProGpuTextRendererService();
        using var bitmap = new Bitmap(150, 80);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using Font font = new(FontFamily.GenericSansSerif, 24, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.TranslateTransform(3, 4);
        RectangleF originalClip = new(4, 5, 130, 60);
        graphics.SetClip(originalClip);
        float[] transform = graphics.Transform.Elements;
        service.DrawText(graphics, "Long text exceeding the caller bounds", font, new Rectangle(10, 10, 50, 40),
            Color.Black, Color.Empty, Plain | (noClipping ? LibreTextFormat.NoClipping : LibreTextFormat.Default));
        graphics.ClipBounds.Should().Be(originalClip);
        graphics.Transform.Elements.Should().Equal(transform);
    }
}
