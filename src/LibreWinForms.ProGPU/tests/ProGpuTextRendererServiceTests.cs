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
