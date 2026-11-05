// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using LibreWinForms.Platform;
using ProGPU.Scene;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuDrawingDpiTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(float.Epsilon)]
    [InlineData(float.NaN)]
    public void InvalidRasterTargetDoesNotClearExistingFallbackPaint(float scale)
    {
        var fallback = new DrawingVisual();
        using (Graphics existing = Graphics.FromProGpuDrawingContext(fallback.Context))
            existing.FillRectangle(Brushes.Red, 1, 2, 3, 4);
        RenderCommand[] original = fallback.Context.Commands.ToArray();
        Assert.NotEmpty(original);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProGpuRetainedPaintFrame(
            new ContainerVisual(), fallback, new DrawingVisual(), new ContainerVisual(),
            new DrawingVisual(), [], new(0, 0, 100, 100), new(0, 0, 100, 100), 96, scale));
        Assert.Equal(original, fallback.Context.Commands);
    }

    [Theory]
    [InlineData(96f, 1f)]
    [InlineData(96f, 2f)]
    [InlineData(192f, 1f)]
    public void RetainedFrameAndEveryLayerShareActualTargetMeasurementAndGlyphSize(float dpi, float rasterScale)
    {
        var root = new ContainerVisual();
        var fallback = new DrawingVisual();
        var transient = new DrawingVisual();
        var adorners = new ContainerVisual();
        var reversible = new DrawingVisual();
        root.AddChild(fallback);
        root.AddTopmostChild(transient);
        root.AddTopmostChild(adorners);
        root.AddTopmostChild(reversible);
        var layers = new Dictionary<LibreHandle, DrawingVisual>();
        var frame = new ProGpuRetainedPaintFrame(root, fallback, transient, adorners, reversible,
            layers, new(0, 0, 1000, 1000), new(0, 0, 1000, 1000), dpi, rasterScale);
        using Font font = new(FontFamily.GenericSansSerif, 9f);
        var renderer = new ProGpuTextRendererService();
        var handle = new LibreHandle((nint)1, LibreHandleKind.LogicalControl);
        Size measured;
        try
        {
            using ILibrePaintLayer layer = frame.OpenLayer(handle, new(10, 20, 500, 500), new(10, 20, 500, 500));
            Graphics graphics = Assert.IsType<Graphics>(layer.Graphics);
            Assert.Equal(dpi, frame.Graphics.DpiY);
            Assert.Equal(dpi, graphics.DpiY);
            measured = renderer.MeasureText(graphics, "Alpha", font, new(1000, 1000), LibreTextFormat.NoPadding | LibreTextFormat.SingleLine);
            Assert.Equal(renderer.MeasureText(frame.Graphics, "Alpha", font, new(1000, 1000), LibreTextFormat.NoPadding | LibreTextFormat.SingleLine), measured);
            renderer.DrawText(graphics, "Alpha", font, new(0, 0, 500, 500), Color.Black, Color.Empty, LibreTextFormat.NoPadding | LibreTextFormat.SingleLine);
        }
        finally
        {
            frame.Complete();
        }

        Assert.True(measured.Height > 0);
        Assert.Equal(9f * dpi / 72f, Assert.Single(layers[handle].Context.Commands, c => c.Type == RenderCommandType.DrawGlyphRun).FontSize);
        Assert.Equal(9f, font.Size);
    }
}
