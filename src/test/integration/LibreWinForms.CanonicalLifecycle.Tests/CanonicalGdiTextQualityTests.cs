// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using ProGPU.SystemDrawing;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void CanonicalGdiRequestedQualityUsesOriginalMapping()
    {
        if (RunDpiCaseInNewProcess()) return;
        var probe = new GdiQualityProbe();
        var platform = new HeadlessPlatform(autoCloseWindows: false, textRenderer: probe);
        LibrePlatform.Register(platform.Services);
        var context = new global::ProGPU.Scene.DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(FontFamily.GenericSansSerif, 13);
        foreach ((TextRenderingHint hint, DrawingGdiFontQuality quality) in new[]
        {
            (TextRenderingHint.SystemDefault, DrawingGdiFontQuality.Default),
            (TextRenderingHint.SingleBitPerPixel, DrawingGdiFontQuality.Draft),
            (TextRenderingHint.SingleBitPerPixelGridFit, DrawingGdiFontQuality.Proof),
            (TextRenderingHint.AntiAlias, DrawingGdiFontQuality.Antialiased),
            (TextRenderingHint.AntiAliasGridFit, DrawingGdiFontQuality.Antialiased),
            (TextRenderingHint.ClearTypeGridFit, DrawingGdiFontQuality.ClearType),
        })
        {
            probe.Policies.Clear();
            graphics.TextRenderingHint = hint;
            TextRenderer.DrawText(graphics, "Name", font, new Rectangle(0, 0, 200, 60), Color.Black);
            platform.RejectGdiSettingsReads = true;
            try
            {
                Assert.Equal(new Size(17, 19), TextRenderer.MeasureText(graphics, "Name", font));
            }
            finally { platform.RejectGdiSettingsReads = false; }
            Assert.Equal(0, probe.LegacyCalls);
            Assert.Equal(2, probe.Policies.Count);
            Assert.All(probe.Policies, policy => Assert.Equal(quality, policy.RequestedQuality));
            Assert.Equal(new DrawingGdiSystemSettings(false, 1, 1700), probe.Policies[0].SystemSettings);
            Assert.Null(probe.Policies[1].SystemSettings);
            Assert.Equal(hint, graphics.TextRenderingHint);
        }
    }

    [Fact]
    public void CanonicalGdiLegacyServiceReceivesOnlyOriginalCalls()
    {
        if (RunDpiCaseInNewProcess()) return;
        var probe = new LegacyGdiProbe();
        var platform = new HeadlessPlatform(autoCloseWindows: false, textRenderer: probe);
        LibrePlatform.Register(platform.Services);
        platform.RejectGdiSettingsReads = true;
        try
        {
            var context = new global::ProGPU.Scene.DrawingContext();
            using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
            using Font font = new(FontFamily.GenericSansSerif, 13);
            TextRenderer.DrawText(graphics, "Name", font, Point.Empty, Color.Black);
            Assert.Equal(new Size(17, 19), TextRenderer.MeasureText(graphics, "Name", font));
            Assert.Equal(2, probe.LegacyCalls);
        }
        finally { platform.RejectGdiSettingsReads = false; }
    }

    [Fact]
    public void CanonicalGdiCompatibilityDefaultsRemainUnknown()
    {
        if (RunDpiCaseInNewProcess()) return;
        var probe = new GdiQualityProbe();
        var platform = new HeadlessPlatform(autoCloseWindows: false);
        var services = new LibrePlatformServices(platform, platform, platform.Handles, platform, platform, platform,
            UnsupportedLibreDesktopCaptureService.Instance, UnsupportedLibreNativeFontInteropService.Instance,
            UnsupportedLibreNativeGraphicsInteropService.Instance, platform, DefaultLibreSystemSettingsService.Instance, probe);
        LibrePlatform.Register(services);
        var context = new global::ProGPU.Scene.DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(FontFamily.GenericSansSerif, 13);
        TextRenderer.DrawText(graphics, "Name", font, Point.Empty, Color.Black);
        Assert.Null(Assert.Single(probe.Policies).SystemSettings);
    }

    [Fact]
    public void TypedRetainedGdiCapturesSettingsOnlyAtPaintAndRejectsAfterDisposal()
    {
        var renderer = new ProGpuTextRendererService();
        var context = new global::ProGPU.Scene.DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(FontFamily.GenericSansSerif, 13);
        const LibreTextFormat format = LibreTextFormat.NoPadding | LibreTextFormat.NoPrefix | LibreTextFormat.SingleLine;
        int reads = 0;
        DrawingGdiSystemSettings? Capture() { reads++; return new(false, reads, 1600 + reads); }
        using ILibreTextLayout layout = renderer.CreateLayout(graphics, "Name", font, new Size(200, 60), format,
            DrawingGdiFontQuality.Draft, Capture);
        _ = layout.ContentSize;
        _ = layout.GetCaret(2);
        Assert.Equal(0, reads);
        Assert.Throws<ArgumentNullException>(() => layout.Draw(null!, PointF.Empty, Color.Black));
        Assert.Equal(0, reads);
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        layout.Draw(graphics, PointF.Empty, Color.Black);
        layout.Draw(graphics, PointF.Empty, Color.Black);
        Assert.Equal(2, reads);
        layout.Dispose();
        Assert.Throws<ObjectDisposedException>(() => layout.Draw(graphics, PointF.Empty, Color.Black));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void TypedRetainedInvalidRequestNeverReadsSettingsOrSource()
    {
        int reads = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProGpuTextRendererService().CreateLayout(
            null!, null!, null!, default, default, (DrawingGdiFontQuality)99,
            () => { reads++; return null; }));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void CanonicalRetainedGdiRequestIsCapturedBeforePaintSettings()
    {
        if (RunDpiCaseInNewProcess()) return;
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        var probe = new GdiLayoutProbe();
        var context = new global::ProGPU.Scene.DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(FontFamily.GenericSansSerif, 13);
        graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        platform.RejectGdiSettingsReads = true;
        ILibreTextLayout layout;
        try
        {
            // Exercise the actual canonical internal entry without changing the
            // product's public API or requiring a native editor/window.
            var create = typeof(TextRenderer).GetMethod("CreatePortableTextLayout",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(create);
            layout = (ILibreTextLayout)create.Invoke(null, [probe, graphics, "Name", font, new Size(200, 60),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine])!;
        }
        finally { platform.RejectGdiSettingsReads = false; }
        using (layout)
        {
            Assert.Equal(DrawingGdiFontQuality.Proof, probe.Request);
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            layout.Draw(graphics, PointF.Empty, Color.Black);
            Assert.Equal(DrawingGdiFontQuality.Proof, probe.Request);
            Assert.Equal(new DrawingGdiSystemSettings(false, 1, 1700), probe.LastPaintSettings);
        }
    }

    [Fact]
    public void TypedRetainedSettingsCallbackCannotPaintADisposedGeneration()
    {
        var renderer = new ProGpuTextRendererService();
        var context = new global::ProGPU.Scene.DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(FontFamily.GenericSansSerif, 13);
        const LibreTextFormat format = LibreTextFormat.NoPadding | LibreTextFormat.NoPrefix | LibreTextFormat.SingleLine;
        ILibreTextLayout? layout = null;
        layout = renderer.CreateLayout(graphics, "Name", font, new Size(200, 60), format,
            DrawingGdiFontQuality.Proof, () => { layout!.Dispose(); return null; });
        int before = context.Commands.Count;
        Assert.Throws<ObjectDisposedException>(() => layout.Draw(graphics, PointF.Empty, Color.Black));
        Assert.Equal(before, context.Commands.Count);
    }

    private class LegacyGdiProbe : ILibreTextRendererService
    {
        public int LegacyCalls { get; private set; }
        public void DrawText(Graphics graphics, string text, Font? font, Rectangle bounds, Color foreColor,
            Color backColor, LibreTextFormat format) => LegacyCalls++;
        public Size MeasureText(Graphics? graphics, string text, Font? font, Size proposedSize, LibreTextFormat format)
        { LegacyCalls++; return new(17, 19); }
    }

    private sealed class GdiQualityProbe : LegacyGdiProbe, ILibreGdiTextRendererService
    {
        public List<DrawingGdiTextPolicy> Policies { get; } = [];
        public void DrawText(Graphics graphics, string text, Font? font, Rectangle bounds, Color foreColor,
            Color backColor, LibreTextFormat format, DrawingGdiTextPolicy policy) => Policies.Add(policy);
        public Size MeasureText(Graphics? graphics, string text, Font? font, Size proposedSize,
            LibreTextFormat format, DrawingGdiTextPolicy policy)
        { Policies.Add(policy); return new(17, 19); }
    }

    private sealed class GdiLayoutProbe : ILibreGdiTextLayoutService
    {
        public DrawingGdiFontQuality? Request { get; private set; }
        public DrawingGdiSystemSettings? LastPaintSettings { get; private set; }
        public ILibreTextLayout CreateLayout(Graphics graphics, string text, Font font, Size layoutSize, LibreTextFormat format)
            => throw new InvalidOperationException("Typed layout dispatch required.");
        public ILibreTextLayout CreateLayout(Graphics graphics, string text, Font font, Size layoutSize,
            LibreTextFormat format, DrawingGdiFontQuality requestedQuality, Func<DrawingGdiSystemSettings?> capturePaintSettings)
        {
            Request = requestedQuality;
            return new ProGpuTextRendererService().CreateLayout(graphics, text, font, layoutSize, format,
                requestedQuality, () => LastPaintSettings = capturePaintSettings());
        }
    }

    private sealed partial class HeadlessPlatform
    {
        internal bool RejectGdiSettingsReads { get; set; }
        private T ReadGdiSetting<T>(T value) => RejectGdiSettingsReads
            ? throw new InvalidOperationException("Legacy service must not read new GDI settings.") : value;
    }
}
