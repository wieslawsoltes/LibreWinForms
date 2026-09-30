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
    public void DevicePixelTextMetricsUseDeclaredDpiBeforeAndAfterHandleCreation()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        platform.SetInitialPresentationScales(2, 1);
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        using Font font = new(FontFamily.GenericSansSerif, 9f);
        using DpiMetricForm form = new() { Font = font, AutoScaleMode = AutoScaleMode.None };
        using ListBox list = new() { Font = font };
        using ComboBox combo = new() { Font = font, DropDownStyle = ComboBoxStyle.DropDownList };
        form.Controls.Add(list);
        form.Controls.Add(combo);
        int expected = (int)Math.Ceiling(font.GetHeight(192f));
        form.MetricHeight.Should().Be(expected);
        list.GetItemHeight(0).Should().Be(expected);
        combo.ItemHeight.Should().Be(expected + 2);
        using DpiMetricControl detached = new() { Font = font };
        detached.MetricHeight.Should().Be(expected);
        _ = detached.Handle;
        detached.MetricHeight.Should().Be(expected, "a logical handle is not an actual 96-DPI native window");
        TextRenderer.MeasureText("target dpi text", font);
        platform.LastMeasuredDpi.Should().Be(192f);
        RunSystemDpiForm(platform, form, () =>
        {
            form.MetricHeight.Should().Be(expected);
            list.GetItemHeight(0).Should().Be(expected);
            combo.ItemHeight.Should().Be(expected + 2);
            font.Size.Should().Be(9f);
        });
    }

    [Fact]
    public void LogicalTextMetricsStay96DespiteNativePresentationScale()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 2));
        platform.SetInitialPresentationScales(2, 2);
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware).Should().BeTrue();
        using DpiMetricForm form = new() { AutoScaleMode = AutoScaleMode.None };
        int expected = form.Font.Height;
        form.MetricHeight.Should().Be(expected);
        TextRenderer.MeasureText("target dpi text", form.Font);
        platform.LastMeasuredDpi.Should().Be(96f);
        RunSystemDpiForm(platform, form, () =>
        {
            form.MetricHeight.Should().Be(expected);
            platform.SetPresentationScales(3, 3);
            form.MetricHeight.Should().Be(expected);
        });
    }

    [Fact]
    public void FontHeightCachePreservesSystemReferenceAcrossOwnerDpiAndHandleChanges()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        platform.SetInitialPresentationScales(2, 1);
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        using DpiMetricForm form = new() { AutoScaleMode = AutoScaleMode.None };
        using ListBox list = new();
        form.Controls.Add(list);
        _ = form.MetricHeight;
        RunSystemDpiForm(platform, form, () =>
        {
            form.MetricHeight.Should().Be((int)Math.Ceiling(form.Font.GetHeight(192f)));
            platform.SetPresentationScales(3, 1);
            form.MetricHeight.Should().Be((int)Math.Ceiling(form.Font.GetHeight(192f)));
            list.GetItemHeight(0).Should().Be((int)Math.Ceiling(list.Font.GetHeight(192f)));
            platform.SetInitialPresentationScales(1, 1);
            nint old = form.Handle;
            form.ReplaceHandle();
            form.Handle.Should().NotBe(old);
            form.MetricHeight.Should().Be((int)Math.Ceiling(form.Font.GetHeight(192f)));
            list.GetItemHeight(0).Should().Be((int)Math.Ceiling(list.Font.GetHeight(192f)));
            using Font replacement = new(FontFamily.GenericSansSerif, 18f);
            form.Font = replacement;
            form.MetricHeight.Should().Be((int)Math.Ceiling(replacement.GetHeight(192f)));
            list.GetItemHeight(0).Should().Be((int)Math.Ceiling(replacement.GetHeight(192f)));
        });
    }

    [Fact]
    public void PerMonitorFontHeightUsesCurrentCanonicalFontAfterTargetTransition()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        platform.SetInitialPresentationScales(2, 1);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2).Should().BeTrue();
        using Font font = new(FontFamily.GenericSansSerif, 9f);
        using DpiMetricForm form = new() { Font = font, AutoScaleMode = AutoScaleMode.None };
        using DpiMetricControl child = new() { Font = font };
        form.Controls.Add(child);
        _ = child.MetricHeight;
        RunSystemDpiForm(platform, form, () =>
        {
            child.MetricHeight.Should().Be((int)Math.Ceiling(child.Font.GetHeight(192f)));
            platform.SetPresentationScales(1, 1);
            child.MetricHeight.Should().Be((int)Math.Ceiling(child.Font.GetHeight(192f)));
            platform.SetPresentationScales(2, 1);
            child.MetricHeight.Should().Be((int)Math.Ceiling(child.Font.GetHeight(192f)));
        });
    }

    [Fact]
    public void ExplicitMeasurementGraphicsOverridesScreenDpi()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        using Bitmap target = new(1, 1);
        target.SetResolution(144f, 144f);
        using Graphics graphics = Graphics.FromImage(target);
        TextRenderer.MeasureText(graphics, "target dpi text", SystemFonts.DefaultFont);
        platform.LastMeasuredDpi.Should().Be(144f);
    }

    private sealed class DpiMetricForm : Form
    {
        internal int MetricHeight => FontHeight;
        internal void ReplaceHandle() => RecreateHandle();
    }

    private sealed class DpiMetricControl : Control
    {
        internal int MetricHeight => FontHeight;
    }
}
