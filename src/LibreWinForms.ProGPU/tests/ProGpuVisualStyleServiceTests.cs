// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Imaging;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuVisualStyleServiceTests
{
    [Fact]
    public void DrawBackgroundUsesManagedGraphicsAndHonorsClip()
    {
        var service = new ProGpuVisualStyleService();
        using var target = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            service.DrawBackground(
                graphics,
                "BUTTON",
                part: 1,
                state: 1,
                new Rectangle(1, 1, 6, 6),
                new Rectangle(4, 0, 4, 8));
        }

        target.GetPixel(2, 3).ToArgb().Should().Be(0);
        target.GetPixel(5, 3).ToArgb().Should().Be(Color.FromArgb(255, 250, 250, 250).ToArgb());
        target.GetPixel(6, 3).A.Should().Be(byte.MaxValue);
        target.GetPixel(6, 3).ToArgb().Should().NotBe(Color.FromArgb(255, 250, 250, 250).ToArgb());
    }

    [Fact]
    public void BackgroundRegionIsManagedAndCallerOwned()
    {
        var service = new ProGpuVisualStyleService();

        service.IsEnabled.Should().BeTrue();
        service.IsElementDefined("BUTTON", 1).Should().BeTrue();
        using Region? region = service.GetBackgroundRegion("BUTTON", 1, 1, new Rectangle(1, 2, 4, 5));

        region.Should().NotBeNull();
        region!.IsVisible(2, 3).Should().BeTrue();
        region.IsVisible(0, 0).Should().BeFalse();
        service.GetBackgroundContentRectangle("BUTTON", 1, 1, new Rectangle(0, 0, 20, 12))
            .Should().Be(new Rectangle(3, 3, 14, 6));
        service.GetPartSize("BUTTON", 1, 1, null, LibreVisualStyleSizeType.True)
            .Should().Be(new Size(75, 23));
        service.GetPartSize("BUTTON", 3, 1, null, LibreVisualStyleSizeType.True)
            .Should().Be(new Size(13, 13));
        service.GetColor("BUTTON", 1, 1, LibreVisualStyleColorProperty.Text).ToArgb()
            .Should().Be(Color.Black.ToArgb());
        service.GetInteger("PROGRESS", 3, 1, LibreVisualStyleIntegerProperty.ProgressChunkSize)
            .Should().Be(6);
        service.GetBoolean("BUTTON", 1, 1, LibreVisualStyleBooleanProperty.BackgroundFill)
            .Should().BeTrue();
        service.GetBoolean("BUTTON", 1, 1, LibreVisualStyleBooleanProperty.Transparent)
            .Should().BeFalse();
        service.GetEnumValue("BUTTON", 1, 1, LibreVisualStyleEnumProperty.BackgroundType)
            .Should().Be(1);
        service.GetEnumValue("BUTTON", 1, 1, LibreVisualStyleEnumProperty.FillType)
            .Should().Be(0);
        service.GetFilename("BUTTON", 1, 1, LibreVisualStyleFilenameProperty.ImageFile)
            .Should().BeEmpty();
        service.GetString("BUTTON", 1, 1, LibreVisualStyleStringProperty.Text)
            .Should().BeEmpty();
        using Font? themeFont = service.GetFont("BUTTON", 1, 1, LibreVisualStyleFontProperty.Text);
        themeFont.Should().NotBeNull();
        themeFont.Should().NotBeSameAs(SystemFonts.DefaultFont);
        themeFont!.Name.Should().Be(SystemFonts.DefaultFont.Name);
        service.GetMargins("BUTTON", 1, 1, LibreVisualStyleMarginProperty.Content)
            .Should().Be(new LibreVisualStyleMargins(3, 3, 3, 3));
        service.GetPoint("BUTTON", 1, 1, LibreVisualStylePointProperty.Offset)
            .Should().Be(Point.Empty);
        service.GetPoint("BUTTON", 1, 1, LibreVisualStylePointProperty.MinimumSize)
            .Should().Be(new Point(75, 23));
        service.IsBackgroundPartiallyTransparent("BUTTON", 1, 1).Should().BeFalse();
    }

    [Fact]
    public void DrawEdgeUsesManagedGraphicsAndReturnsAdjustedContentBounds()
    {
        var service = new ProGpuVisualStyleService();
        using var target = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
        Rectangle contentBounds;
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            contentBounds = service.DrawEdge(
                graphics,
                "TRACKBAR",
                part: 1,
                state: 1,
                new Rectangle(1, 1, 6, 6),
                LibreVisualStyleEdges.Left | LibreVisualStyleEdges.Top,
                LibreVisualStyleEdgeStyle.Raised,
                LibreVisualStyleEdgeEffects.None);
        }

        contentBounds.Should().Be(new Rectangle(2, 2, 5, 5));
        target.GetPixel(1, 3).A.Should().BeGreaterThan(0);
        target.GetPixel(3, 1).A.Should().BeGreaterThan(0);
        target.GetPixel(3, 3).A.Should().Be(0);
    }

    [Fact]
    public void DrawTextUsesManagedGraphicsAndTypedFormatting()
    {
        var service = new ProGpuVisualStyleService();
        using var target = new Bitmap(120, 40, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            service.DrawText(
                graphics,
                "BUTTON",
                part: 1,
                state: 1,
                new Rectangle(0, 0, 120, 40),
                "Libre",
                disabled: false,
                LibreVisualStyleTextFormat.HorizontalCenter
                    | LibreVisualStyleTextFormat.VerticalCenter
                    | LibreVisualStyleTextFormat.SingleLine);
        }

        Enumerable.Range(0, target.Width * target.Height)
            .Select(index => target.GetPixel(index % target.Width, index / target.Width).A)
            .Should().Contain(alpha => alpha > 0);

        using Graphics measureGraphics = Graphics.FromImage(target);
        Rectangle extent = service.MeasureText(
            measureGraphics,
            "BUTTON",
            part: 1,
            state: 1,
            new Rectangle(10, 5, 100, 30),
            "Libre",
            LibreVisualStyleTextFormat.HorizontalCenter | LibreVisualStyleTextFormat.VerticalCenter);
        extent.Width.Should().BeGreaterThan(0);
        extent.Height.Should().BeGreaterThan(0);
        extent.Left.Should().BeGreaterThan(10);
        extent.Top.Should().BeGreaterThanOrEqualTo(5);

        Rectangle hitBounds = new(10, 10, 40, 20);
        service.HitTestBackground(
            measureGraphics,
            "BUTTON",
            part: 1,
            state: 1,
            hitBounds,
            region: null,
            new Point(10, 10),
            LibreVisualStyleHitTestOptions.ResizingBorder)
            .Should().Be(LibreVisualStyleHitTestCode.TopLeft);
        service.HitTestBackground(
            measureGraphics,
            "BUTTON",
            part: 1,
            state: 1,
            hitBounds,
            region: null,
            new Point(30, 20),
            LibreVisualStyleHitTestOptions.ResizingBorder)
            .Should().Be(LibreVisualStyleHitTestCode.Client);
        using var hitRegion = new Region(new Rectangle(20, 15, 10, 10));
        service.HitTestBackground(
            measureGraphics,
            "BUTTON",
            part: 1,
            state: 1,
            hitBounds,
            hitRegion,
            new Point(12, 12),
            LibreVisualStyleHitTestOptions.ResizingBorder)
            .Should().Be(LibreVisualStyleHitTestCode.Nowhere);
    }
}
