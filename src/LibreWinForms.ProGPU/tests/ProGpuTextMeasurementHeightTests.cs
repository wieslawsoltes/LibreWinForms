// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuTextMeasurementHeightTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(19)]
    [InlineData(64)]
    public void MeasureTextExtendsTheRectangleForEveryLine(int proposedHeight)
    {
        ProGpuTextRendererService service = new();
        using Bitmap bitmap = new(1, 1);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using Font font = new(FontFamily.GenericSansSerif, 24, FontStyle.Regular, GraphicsUnit.Pixel);
        (string Text, LibreTextFormat Format)[] cases =
        [
            ("&File", LibreTextFormat.HorizontalCenter | LibreTextFormat.VerticalCenter | LibreTextFormat.HidePrefix),
            ("First line\nSecond line", LibreTextFormat.NoPrefix),
            ("Alpha Beta Gamma Delta", LibreTextFormat.WordBreak),
            ("Alpha Beta Gamma Delta", LibreTextFormat.WordBreak | LibreTextFormat.TextBoxControl),
            ("Alpha Beta Gamma Delta", LibreTextFormat.WordBreak | LibreTextFormat.WordEllipsis),
            ("Single line", LibreTextFormat.SingleLine),
        ];

        foreach ((string text, LibreTextFormat format) in cases)
        {
            foreach (int width in new[] { 60, 400 })
            {
                Size unconstrained = service.MeasureText(graphics, text, font,
                    new Size(width, int.MaxValue), format);
                Size constrained = service.MeasureText(graphics, text, font,
                    new Size(width, proposedHeight), format);
                constrained.Should().Be(unconstrained,
                    "CALCRECT extends the bottom instead of fitting or trimming to the proposed height ({0}, {1})",
                    text, format);
                unconstrained.Height.Should().BeGreaterThan(0);
            }
        }
    }

    [Fact]
    public void MeasurementStillUsesTheProposedWidthForWrapping()
    {
        ProGpuTextRendererService service = new();
        using Font font = new(FontFamily.GenericSansSerif, 24, FontStyle.Regular, GraphicsUnit.Pixel);
        const string text = "Alpha Beta Gamma Delta";
        const LibreTextFormat format = LibreTextFormat.WordBreak | LibreTextFormat.NoPadding;
        Size narrow = service.MeasureText(null, text, font, new Size(100, 1), format);
        Size wide = service.MeasureText(null, text, font, new Size(600, 1), format);
        narrow.Height.Should().BeGreaterThan(wide.Height);
        narrow.Width.Should().BeLessThan(wide.Width);
        service.MeasureText(null, string.Empty, font, new Size(100, 1), format).Should().Be(Size.Empty);
        font.Size.Should().Be(24);
        font.Unit.Should().Be(GraphicsUnit.Pixel);
    }
}
