// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Imaging;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuTextRendererServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceGdiDrawDoesNotInheritOrdinarySingleBitCoverage(bool retained)
    {
        var service = new ProGpuTextRendererService();
        var context = new global::ProGPU.Scene.DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(FontFamily.GenericSansSerif, 13);
        const LibreTextFormat format = LibreTextFormat.SingleLine | LibreTextFormat.NoPrefix | LibreTextFormat.NoPadding;
        using var layout = retained ? service.CreateLayout(graphics, "Name 123", font, new Size(200, 60), format) : null;
        foreach (System.Drawing.Text.TextRenderingHint hint in new[]
        {
            System.Drawing.Text.TextRenderingHint.SingleBitPerPixel,
            System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit
        })
        {
            graphics.TextRenderingHint = hint;
            int start = context.Commands.Count;
            if (layout is not null) layout.Draw(graphics, PointF.Empty, Color.Black);
            else service.DrawText(graphics, "Name 123", font, new Rectangle(0, 0, 200, 60), Color.Black, Color.Empty, format);
            var runs = context.Commands.Skip(start).Where(c => c.Type == global::ProGPU.Scene.RenderCommandType.DrawGlyphRun).ToArray();
            runs.Should().NotBeEmpty();
            foreach (var run in runs)
                run.TextRenderingMode.Should().Be(global::ProGPU.Scene.TextRenderingMode.Grayscale);
            graphics.TextRenderingHint.Should().Be(hint);
        }
    }

    [Fact]
    public void TextBoxContentFlagsProduceRealInkWithinTheCallerClip()
    {
        var service = new ProGpuTextRendererService();
        using var target = new Bitmap(160, 60, PixelFormat.Format32bppArgb);
        using Font font = new(FontFamily.GenericSansSerif, 18);
        Rectangle clip = new(10, 8, 50, 35);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            graphics.SetClip(clip);
            service.DrawText(
                graphics,
                "Alice and more source text",
                font,
                new Rectangle(10, 8, 140, 45),
                Color.Navy,
                Color.Empty,
                LibreTextFormat.TextBoxControl | LibreTextFormat.NoPrefix
                    | LibreTextFormat.NoPadding | LibreTextFormat.SingleLine);
        }

        int ink = 0;
        for (int y = 0; y < target.Height; y++)
        {
            for (int x = 0; x < target.Width; x++)
            {
                byte alpha = target.GetPixel(x, y).A;
                if (clip.Contains(x, y))
                {
                    if (alpha != 0)
                    {
                        ink++;
                    }
                }
                else
                {
                    alpha.Should().Be(0, "text may not escape the retained source clip");
                }
            }
        }

        ink.Should().BeGreaterThan(0, "the real ProGPU text renderer must draw the editor content");
    }

    [Fact]
    public void DrawTextUsesManagedGraphicsAndColorsBackground()
    {
        var service = new ProGpuTextRendererService();
        using var target = new Bitmap(120, 40, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            service.DrawText(
                graphics,
                "Libre",
                SystemFonts.DefaultFont,
                new Rectangle(10, 5, 100, 30),
                Color.Black,
                Color.Yellow,
                LibreTextFormat.HorizontalCenter
                    | LibreTextFormat.VerticalCenter
                    | LibreTextFormat.SingleLine);
        }

        target.GetPixel(11, 6).ToArgb().Should().Be(Color.Yellow.ToArgb());
        Enumerable.Range(0, target.Width * target.Height)
            .Select(index => target.GetPixel(index % target.Width, index / target.Width).ToArgb())
            .Should().Contain(pixel => pixel != Color.Transparent.ToArgb() && pixel != Color.Yellow.ToArgb());
    }

    [Fact]
    public void MeasureTextSupportsCallerGraphicsAndHeadlessMeasurement()
    {
        var service = new ProGpuTextRendererService();
        using var target = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(target);

        Size withGraphics = service.MeasureText(
            graphics,
            "Libre WinForms portable text",
            SystemFonts.DefaultFont,
            new Size(80, 200),
            LibreTextFormat.WordBreak | LibreTextFormat.NoPadding);
        Size headless = service.MeasureText(
            graphics: null,
            "Libre",
            SystemFonts.DefaultFont,
            new Size(int.MaxValue, int.MaxValue),
            LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);

        withGraphics.Width.Should().BeInRange(1, 80);
        withGraphics.Height.Should().BeGreaterThan(0);
        headless.Width.Should().BeGreaterThan(0);
        headless.Height.Should().BeGreaterThan(0);
    }
}
