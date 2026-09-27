// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableFontAutoScaleUsesSystemDpi192()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = PrepareFontAutoScalePlatform(2, HighDpiMode.SystemAware);
        using Font font = new(FontFamily.GenericSansSerif, 8.25f);
        using FontAutoScaleForm form = CreateFontAutoScaleForm(font);
        form.CurrentAutoScaleDimensions.Height.Should().Be((float)Math.Ceiling(font.GetHeight(192f)));
        platform.LastMeasuredDpi.Should().Be(192f);
        form.Font.Should().BeSameAs(font);
        font.Size.Should().Be(8.25f);
    }

    [Fact]
    public void PortableFontAutoScaleUsesFractionalReference144()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = PrepareFontAutoScalePlatform(1.5, HighDpiMode.SystemAware);
        using Font font = new(FontFamily.GenericSansSerif, 9.25f);
        using FontAutoScaleForm form = CreateFontAutoScaleForm(font);
        form.CurrentAutoScaleDimensions.Height.Should().Be((float)Math.Ceiling(font.GetHeight(144f)));
        platform.LastMeasuredDpi.Should().Be(144f);
    }

    [Fact]
    public void PortableFontAutoScaleLogicalCoordinatesStay96()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = PrepareFontAutoScalePlatform(2, HighDpiMode.DpiUnaware);
        using Font font = new(FontFamily.GenericSansSerif, 9.25f);
        using FontAutoScaleForm form = CreateFontAutoScaleForm(font);
        form.CurrentAutoScaleDimensions.Height.Should().Be(font.Height);
        platform.LastMeasuredDpi.Should().Be(96f);
    }

    [Fact]
    public void PortableFontAutoScalePixelFontIsNotScaledTwice()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = PrepareFontAutoScalePlatform(2, HighDpiMode.SystemAware);
        using Font font = new(FontFamily.GenericSansSerif, 15.25f, FontStyle.Regular, GraphicsUnit.Pixel);
        using FontAutoScaleForm form = CreateFontAutoScaleForm(font);
        font.GetHeight(192f).Should().Be(font.GetHeight(96f));
        form.CurrentAutoScaleDimensions.Height.Should().Be(font.Height);
        platform.LastMeasuredDpi.Should().Be(192f);
    }

    [Fact]
    public void PortableFontAutoScaleIgnoresControlMetricOverride()
    {
        if (RunDpiCaseInNewProcess()) return;
        PrepareFontAutoScalePlatform(2, HighDpiMode.SystemAware);
        using Font font = new(FontFamily.GenericSansSerif, 8.25f);
        using FontAutoScaleForm form = CreateFontAutoScaleForm(font);
        form.OverrideLineMetric(1);
        form.CurrentAutoScaleDimensions.Height.Should().Be((float)Math.Ceiling(font.GetHeight(192f)),
            "font autoscaling measures the selected font, not a derived control's layout override");
    }

    [Fact]
    public void PortableFontAutoScaleRecomputesAfterLiveFontChange()
    {
        if (RunDpiCaseInNewProcess()) return;
        PrepareFontAutoScalePlatform(1.5, HighDpiMode.SystemAware);
        using Font first = new(FontFamily.GenericSansSerif, 9.25f);
        using Font second = new(FontFamily.GenericSansSerif, 16.5f);
        using FontAutoScaleForm form = CreateFontAutoScaleForm(first);
        // Canonical ContainerControl invalidates its cached autoscale metrics
        // on FontChanged only after its handle exists. Preserve that policy.
        _ = form.Handle;
        form.CurrentAutoScaleDimensions.Height.Should().Be((float)Math.Ceiling(first.GetHeight(144f)));
        form.Font = second;
        form.CurrentAutoScaleDimensions.Height.Should().Be((float)Math.Ceiling(second.GetHeight(144f)));
        form.Font.Should().BeSameAs(second);
    }

    private static FontAutoScaleForm CreateFontAutoScaleForm(Font font)
    {
        FontAutoScaleForm form = new();
        form.SuspendLayout();
        form.Font = font;
        form.AutoScaleMode = AutoScaleMode.Font;
        return form;
    }

    private static HeadlessPlatform PrepareFontAutoScalePlatform(double scale, HighDpiMode mode)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        double framebufferScale = mode == HighDpiMode.DpiUnaware ? scale : 1;
        platform.SetMonitors(SystemDpiMonitor(scale, framebufferScale));
        platform.SetInitialPresentationScales(scale, framebufferScale);
        Application.SetHighDpiMode(mode).Should().BeTrue();
        return platform;
    }

    private sealed class FontAutoScaleForm : Form
    {
        internal void OverrideLineMetric(int height) => FontHeight = height;
    }
}
