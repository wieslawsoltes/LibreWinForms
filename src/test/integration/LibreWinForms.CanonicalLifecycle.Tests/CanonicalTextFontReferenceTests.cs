// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.ProGPU;
using ProGPU.Scene;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void CanonicalTextFontInitial96To192OwnsMatchingSourceAndNativeBounds()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyInitialCanonicalFontAndBounds(1, 2, new Size(400, 300), new Size(800, 600), new Rectangle(40, 60, 200, 80), 24f);
    }

    [Fact]
    public void CanonicalTextFontInitial192To96OwnsMatchingSourceAndNativeBounds()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyInitialCanonicalFontAndBounds(2, 1, new Size(800, 600), new Size(400, 300), new Rectangle(20, 30, 100, 40), 12f);
    }

    private static void VerifyInitialCanonicalFontAndBounds(
        double systemScale, double windowScale, Size before, Size after, Rectangle childAfter, float em)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(
            new LibreWinForms.Platform.LibreMonitor("primary", new(-4000, 0, 2000, 1600), new(-4000, 0, 2000, 1500), systemScale, true)
            { NativeCoordinateScale = 1 },
            new LibreWinForms.Platform.LibreMonitor("target", new(0, 0, 2400, 1600), new(0, 0, 2400, 1500), windowScale, false)
            { NativeCoordinateScale = 1 });
        platform.SetInitialPresentationScales(windowScale, 1);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2).Should().BeTrue();
        platform.ActualTextRenderer = new ProGpuTextRendererService();
        using Font font = new(FontFamily.GenericSansSerif, 9f);
        using Form form = new() { Font = font, ShowIcon = false };
        form.SuspendLayout();
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = new Rectangle(10, 20, 400, 300);
        using Control child = new() { Bounds = new Rectangle(20, 30, 100, 40) };
        form.Controls.Add(child);
        form.ResumeLayout(true);
        form.DeviceDpi.Should().Be((int)(systemScale * 96));
        form.Size.Should().Be(before);
        RunSystemDpiForm(platform, form, () =>
        {
            form.DeviceDpi.Should().Be((int)(windowScale * 96));
            form.Bounds.Should().Be(new Rectangle(new Point(10, 20), after));
            child.Bounds.Should().Be(childAfter);
            platform.LastWindowOptions.InitialDpiScale.Should().Be(windowScale);
            platform.LastWindowOptions.Bounds.Should().Be(new LibreWinForms.Platform.LibreRectangle(10, 20, after.Width, after.Height));
            platform.LastNativeWindowBounds.Should().Be(new LibreWinForms.Platform.LibreRectangle(10, 20, after.Width, after.Height));
            AssertCanonicalRecordedFont(form.Font, (float)(windowScale * 96), em);
        });
    }

    [Fact]
    public void CanonicalTextFontPrimary192Window192TransitionsUseLiteralEmSizes()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyCanonicalTextFontTransitions(2, 2, 9f, 24f, 4.5f);
    }

    [Fact]
    public void CanonicalTextFontPrimary96Window192TransitionsUseLiteralEmSizes()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyCanonicalTextFontTransitions(1, 2, 18f, 12f, 9f);
    }

    [Fact]
    public void CanonicalTextFontPrimary192Window96TransitionsUseLiteralEmSizes()
    {
        if (RunDpiCaseInNewProcess()) return;
        VerifyCanonicalTextFontTransitions(2, 1, 4.5f, 24f, 4.5f);
    }

    private static void VerifyCanonicalTextFontTransitions(
        double systemScale, double windowScale, float initialFontSize, float preHandleEm, float at96FontSize)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(systemScale, 1));
        platform.SetInitialPresentationScales(windowScale, 1);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2).Should().BeTrue();
        platform.ActualTextRenderer = new ProGpuTextRendererService();
        using Font original = new(FontFamily.GenericSansSerif, 9f);
        using DpiMetricForm form = new() { Font = original, AutoScaleMode = AutoScaleMode.None };
        using DpiMetricControl child = new() { Font = original };
        form.Controls.Add(child);
        AssertCanonicalRecordedFont(child.Font, 192f, preHandleEm);
        child.Font.Size.Should().Be(9f);
        RunSystemDpiForm(platform, form, () =>
        {
            child.Font.Size.Should().Be(initialFontSize);
            AssertCanonicalRecordedFont(child.Font, (float)windowScale * 96f, windowScale == 2 ? 24f : 12f);
            platform.SetPresentationScales(1, 1);
            child.Font.Size.Should().Be(at96FontSize);
            AssertCanonicalRecordedFont(child.Font, 96f, 12f);
            platform.SetPresentationScales(2, 1);
            child.Font.Size.Should().Be(systemScale == 2 ? 9f : 18f);
            AssertCanonicalRecordedFont(child.Font, 192f, 24f);
            original.Size.Should().Be(9f);
            original.Unit.Should().Be(GraphicsUnit.Point);
        });
    }

    [Fact]
    public void CanonicalTextFontSystemAwareKeepsInitialReferenceWhileGraphicsChanges()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        platform.SetInitialPresentationScales(2, 1);
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        platform.ActualTextRenderer = new ProGpuTextRendererService();
        using Font font = new(FontFamily.GenericSansSerif, 9f);
        using DpiMetricForm form = new() { Font = font, AutoScaleMode = AutoScaleMode.None };
        RunSystemDpiForm(platform, form, () =>
        {
            AssertCanonicalRecordedFont(form.Font, 192f, 24f);
            platform.SetPresentationScales(1, 1);
            form.Font.Should().BeSameAs(font);
            AssertCanonicalRecordedFont(form.Font, 96f, 24f);
            platform.SetPresentationScales(3, 1);
            AssertCanonicalRecordedFont(form.Font, 288f, 24f);
        });
    }

    [Fact]
    public void CanonicalTextFontLogicalRemains12PixelsOn192DpiDisplay()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 2));
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware).Should().BeTrue();
        platform.ActualTextRenderer = new ProGpuTextRendererService();
        using Font font = new(FontFamily.GenericSansSerif, 9f);
        AssertCanonicalRecordedFont(font, 96f, 12f);
    }

    [Fact]
    public void CanonicalTextFontCallerGraphicsKeepsRawDrawingDpiButNotFontReference()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        platform.ActualTextRenderer = new ProGpuTextRendererService();
        using Font font = new(FontFamily.GenericSansSerif, 9f);
        AssertCanonicalRecordedFont(font, 144f, 24f);
        DrawingContext raw = new();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(raw, new(0, 0, 1000, 1000), Matrix4x4.Identity, 144f, 144f);
        using Brush brush = new SolidBrush(Color.Black);
        using StringFormat format = StringFormat.GenericTypographic;
        graphics.DrawString("Alpha", font, brush, new RectangleF(0, 0, 900, 900), format);
        raw.Commands.Single(c => c.Type == RenderCommandType.DrawGlyphRun).FontSize.Should().Be(18f);
        graphics.DpiY.Should().Be(144f);
    }

    [Fact]
    public void CanonicalTextFontPixelUnitsDoNotScaleWithSystemResolution()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        platform.ActualTextRenderer = new ProGpuTextRendererService();
        using Font font = new(FontFamily.GenericSansSerif, 12f, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel, 2, true);
        AssertCanonicalRecordedFont(font, 192f, 12f);
        using DpiMetricControl control = new() { Font = font };
        control.MetricHeight.Should().Be(font.Height);
    }

    [Fact]
    public void CanonicalTextFontFractionalPointEmAndLineHeightRoundSeparately()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        platform.ActualTextRenderer = new ProGpuTextRendererService();
        using Font font = new(FontFamily.GenericSansSerif, 9.1f);
        AssertCanonicalRecordedFont(font, 192f, 25f);
        using DpiMetricControl control = new() { Font = font };
        float lineRatio = (float)font.FontFamily.GetLineSpacing(font.Style) / font.FontFamily.GetEmHeight(font.Style);
        control.MetricHeight.Should().Be((int)Math.Ceiling(lineRatio * (9.1f * 192f / 72f)));
    }

    [Fact]
    public void CanonicalTextFontNullRetainsTheExistingBackendDefaultPolicy()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        platform.ActualTextRenderer = new ProGpuTextRendererService();
        DrawingContext context = new();
        DrawingContext direct = new();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context, new(0, 0, 1000, 1000), Matrix4x4.Identity, 144f, 144f);
        using Graphics other = Graphics.FromProGpuDrawingContext(direct, new(0, 0, 1000, 1000), Matrix4x4.Identity, 144f, 144f);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        const LibreWinForms.Platform.LibreTextFormat format = LibreWinForms.Platform.LibreTextFormat.NoPadding
            | LibreWinForms.Platform.LibreTextFormat.NoPrefix | LibreWinForms.Platform.LibreTextFormat.SingleLine;
        TextRenderer.DrawText(graphics, "Alpha", null, new Rectangle(0, 0, 900, 900), Color.Black, flags);
        platform.LastDrawnTextFont.Should().BeNull();
        ProGpuTextRendererService renderer = new();
        renderer.DrawText(other, "Alpha", null, new Rectangle(0, 0, 900, 900), Color.Black, Color.Empty, format);
        context.Commands.Single(c => c.Type == RenderCommandType.DrawGlyphRun).FontSize.Should()
            .Be(direct.Commands.Single(c => c.Type == RenderCommandType.DrawGlyphRun).FontSize);
        TextRenderer.MeasureText(graphics, "Alpha", null, new(900, 900), flags).Should()
            .Be(renderer.MeasureText(other, "Alpha", null, new(900, 900), format));
    }

    private static void AssertCanonicalRecordedFont(Font source, float targetDpi, float expectedEm)
    {
        float sourceSize = source.Size;
        GraphicsUnit sourceUnit = source.Unit;
        FontStyle sourceStyle = source.Style;
        byte sourceCharset = source.GdiCharSet;
        bool sourceVertical = source.GdiVerticalFont;
        DrawingContext context = new();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context, new(0, 0, 1000, 1000), Matrix4x4.Identity, targetDpi, targetDpi);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(graphics, "Alpha", source, new Rectangle(0, 0, 900, 900), Color.Black, flags);
        HeadlessPlatform platform = (HeadlessPlatform)LibreWinForms.Platform.LibrePlatform.Current.TextRenderer;
        AssertCanonicalProjectedFont(platform.LastDrawnTextFont!.Value, source, expectedEm);
        RenderCommand command = context.Commands.Single(c => c.Type == RenderCommandType.DrawGlyphRun);
        command.FontSize.Should().Be(expectedEm);
        command.Font.Should().NotBeNull();
        using Font expected = new(source.FontFamily, expectedEm, sourceStyle, GraphicsUnit.Pixel, sourceCharset, sourceVertical);
        ProGpuTextRendererService renderer = new();
        DrawingContext independentRecording = new();
        using Graphics independentGraphics = Graphics.FromProGpuDrawingContext(independentRecording, new RectangleF(0, 0, 1000, 1000));
        renderer.DrawText(independentGraphics, "Alpha", expected, new(0, 0, 900, 900), Color.Black, Color.Empty,
            LibreWinForms.Platform.LibreTextFormat.NoPadding | LibreWinForms.Platform.LibreTextFormat.NoPrefix | LibreWinForms.Platform.LibreTextFormat.SingleLine);
        command.Font.Should().BeSameAs(independentRecording.Commands.Single(c => c.Type == RenderCommandType.DrawGlyphRun).Font);
        Size independent = renderer.MeasureText(graphics, "Alpha", expected, new(900, 900),
            LibreWinForms.Platform.LibreTextFormat.NoPadding | LibreWinForms.Platform.LibreTextFormat.NoPrefix | LibreWinForms.Platform.LibreTextFormat.SingleLine);
        TextRenderer.MeasureText(graphics, "Alpha", source, new(900, 900), flags).Should().Be(independent);
        TextRenderer.MeasureText("Alpha", source, new(900, 900), flags).Should().Be(independent);
        graphics.DpiY.Should().Be(targetDpi);
        source.Size.Should().Be(sourceSize);
        source.Unit.Should().Be(sourceUnit);
        source.Style.Should().Be(sourceStyle);
        source.GdiCharSet.Should().Be(sourceCharset);
        source.GdiVerticalFont.Should().Be(sourceVertical);
    }

    private static void AssertCanonicalProjectedFont(CanonicalTextFontDescriptor actual, Font source, float em)
        => actual.Should().Be(new CanonicalTextFontDescriptor(source.FontFamily.Name,
            em, GraphicsUnit.Pixel, source.Style, source.GdiCharSet, source.GdiVerticalFont));

    private readonly record struct CanonicalTextFontDescriptor(
        string Family, float Size, GraphicsUnit Unit, FontStyle Style, byte Charset, bool Vertical)
    {
        internal static CanonicalTextFontDescriptor Capture(Font font)
            => new(font.FontFamily.Name, font.Size, font.Unit, font.Style, font.GdiCharSet, font.GdiVerticalFont);
    }
}
