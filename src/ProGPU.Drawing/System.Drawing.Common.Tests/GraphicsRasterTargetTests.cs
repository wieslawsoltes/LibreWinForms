using ProGPU.Scene;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Numerics;
using Xunit;

namespace System.Drawing.Tests;

public sealed class GraphicsRasterTargetTests
{
    [Theory]
    [InlineData(96f, 96f, 2f)]
    [InlineData(192f, 192f, 1f)]
    [InlineData(144f, 192f, 1.25f)]
    public void ExplicitRasterTargetDoesNotChangeLayoutPlacementOrSavedHostState(
        float dpiX, float dpiY, float rasterScale)
    {
        using var fonts = new PrivateFontCollection();
        fonts.AddFontFile(Path.Combine(AppContext.BaseDirectory, "Fonts", "Inter-Regular.ttf"));
        using FontFamily family = Assert.Single(fonts.Families);
        using var font = new Font(family, 9f, FontStyle.Regular, GraphicsUnit.Point);
        using var format = StringFormat.GenericTypographic;
        var bounds = new RectangleF(0, 0, 1000, 1000);
        Matrix4x4 transform = Matrix4x4.CreateTranslation(7, 11, 0);
        var original = new DrawingContext();
        var explicitTarget = new DrawingContext();
        using Graphics legacy = Graphics.FromProGpuDrawingContext(original, bounds, transform, dpiX, dpiY);
        using Graphics target = Graphics.FromProGpuDrawingContext(explicitTarget, bounds, transform,
            dpiX, dpiY, rasterScale);
        GraphicsState saved = target.Save();
        target.ScaleTransform(2, 3);
        GraphicsContainer container = target.BeginContainer();
        Assert.Equal(rasterScale, target.GlyphRasterDpiScale);
        target.EndContainer(container);
        target.Restore(saved);
        Assert.Null(legacy.GlyphRasterDpiScale);
        Assert.Equal(rasterScale, target.GlyphRasterDpiScale);
        Assert.Equal(dpiX, target.DpiX);
        Assert.Equal(dpiY, target.DpiY);
        Assert.Equal(legacy.MeasureString("Alpha", font), target.MeasureString("Alpha", font));
        legacy.DrawString("Alpha", font, Brushes.Black, bounds, format);
        target.DrawString("Alpha", font, Brushes.Black, bounds, format);
        RenderCommand expected = Assert.Single(original.Commands, c => c.Type == RenderCommandType.DrawGlyphRun);
        RenderCommand actual = Assert.Single(explicitTarget.Commands, c => c.Type == RenderCommandType.DrawGlyphRun);
        Assert.Equal(expected.FontSize, actual.FontSize);
        Assert.Equal(expected.Transform, actual.Transform);
        Assert.Equal(expected.Position, actual.Position);
        Assert.Equal(expected.GlyphIndices, actual.GlyphIndices);
        Assert.Equal(expected.GlyphPositions, actual.GlyphPositions);
    }

    [Fact]
    public void BitmapRasterScaleIsOneIndependentlyOfItsResolutionMetadata()
    {
        using var bitmap = new Bitmap(10, 10);
        bitmap.SetResolution(144, 192);
        using Graphics image = Graphics.FromImage(bitmap);
        Assert.Equal(1f, image.GlyphRasterDpiScale);
        Assert.Equal(144f, image.DpiX);
        Assert.Equal(192f, image.DpiY);
        bitmap.SetResolution(96, 96);
        Assert.Equal(1f, image.GlyphRasterDpiScale);
        Assert.Equal(192f, image.DpiY);
        using Graphics unknown = Graphics.FromProGpuDrawingContext(new DrawingContext(),
            new RectangleF(0, 0, 10, 10), Matrix4x4.CreateScale(2, 2, 1));
        Assert.Null(unknown.GlyphRasterDpiScale);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.Epsilon)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidTargetRejectsBeforeRecorderOrCallbackOwnership(float scale)
    {
        var context = new DrawingContext();
        int callbacks = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.FromProGpuDrawingContext(context,
            new RectangleF(0, 0, 100, 100), Matrix4x4.Identity, 96, 96, scale,
            flushed: _ => callbacks++, completed: () => callbacks++));
        Assert.Empty(context.Commands);
        Assert.Equal(0, callbacks);
    }
}
