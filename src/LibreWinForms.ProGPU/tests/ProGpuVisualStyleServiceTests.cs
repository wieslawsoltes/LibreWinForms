// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Imaging;
using FluentAssertions;
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
    }
}
