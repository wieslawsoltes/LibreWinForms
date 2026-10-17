// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableFontDeviceAutoScaleRetainsSelectedFontAndReferenceDpi()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = PrepareFontAutoScalePlatform(2, HighDpiMode.SystemAware);
        using Font font = new(FontFamily.GenericSansSerif, 9);
        using FontAutoScaleForm form = CreateFontAutoScaleForm(font);
        int calls = 0;
        platform.FontDeviceMetric = (selected, dpi) =>
        {
            selected.Should().BeSameAs(font); dpi.Should().Be(192); calls++;
            return 123;
        };
        try
        {
            form.OverrideLineMetric(1);
            form.CurrentAutoScaleDimensions.Height.Should().Be(123);
            form.CurrentAutoScaleDimensions.Height.Should().Be(123);
            calls.Should().Be(1);
            platform.LastMeasuredDpi.Should().Be(192);
            font.Size.Should().Be(9);
        }
        finally { platform.FontDeviceMetric = null; }
    }

    [Fact]
    public void PortableFontDeviceAutoScaleUnavailablePreservesLineMetric()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = PrepareFontAutoScalePlatform(1.5, HighDpiMode.SystemAware);
        using Font font = new(FontFamily.GenericSansSerif, 9);
        using FontAutoScaleForm form = CreateFontAutoScaleForm(font);
        platform.FontDeviceMetric = (_, dpi) => { dpi.Should().Be(144); return null; };
        try { form.CurrentAutoScaleDimensions.Height.Should().Be((int)Math.Ceiling(font.GetHeight(144))); }
        finally { platform.FontDeviceMetric = null; }
    }

    [Fact]
    public void PortableFontDeviceAutoScaleProviderFailureCannotBecomeFallback()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = PrepareFontAutoScalePlatform(1, HighDpiMode.SystemAware);
        using Font font = new(FontFamily.GenericSansSerif, 9);
        using FontAutoScaleForm form = CreateFontAutoScaleForm(font);
        var failure = new InvalidDataException("Original malformed font metadata");
        platform.FontDeviceMetric = (_, _) => throw failure;
        try
        {
            Action read = () => _ = form.CurrentAutoScaleDimensions;
            read.Should().Throw<InvalidDataException>().Which.Should().BeSameAs(failure);
        }
        finally { platform.FontDeviceMetric = null; }
    }

    private sealed partial class HeadlessPlatform : ILibreFontAutoScaleMetrics
    {
        internal Func<Font, int, int?>? FontDeviceMetric { get; set; }
        public bool TryGetFontAutoScaleHeight(Font font, int referenceDpi, out int height)
        {
            int? value = FontDeviceMetric?.Invoke(font, referenceDpi);
            height = value.GetValueOrDefault();
            return value.HasValue;
        }
    }
}
