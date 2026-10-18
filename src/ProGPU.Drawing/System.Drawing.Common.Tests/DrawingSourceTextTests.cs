using System.Drawing.Text;
using System.Numerics;
using ProGPU.Scene;
using ProGPU.SystemDrawing;
using ProGPU.Text;
using Xunit;

namespace System.Drawing.Tests;

public sealed class DrawingSourceTextTests
{
    private static StringFormat Format() => new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
        Trimming = StringTrimming.None,
        HotkeyPrefix = HotkeyPrefix.None
    };

    [Theory]
    [InlineData(9, GraphicsUnit.Point)]
    [InlineData(12, GraphicsUnit.Pixel)]
    public void ExplicitSourcePaintMeasurementAndCaretRowsShareOneExactRecord(float size, GraphicsUnit unit)
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(new TtfFont(FontDeviceAutoScaleTests.WithAuthoredVdmx()), size, unit: unit);
        using StringFormat format = Format();
        format.SetDigitSubstitution(0, StringDigitSubstitute.None);
        using var brush = new SolidBrush(Color.Black);
        float ordinary = font.GetHeight(graphics);
        Assert.True(DrawingSourceText.TryGetLineMetrics(graphics, font, out var metrics));
        Assert.Equal(15, metrics.Height);
        var bounds = new SizeF(100, 100);
        Assert.Equal(45, DrawingSourceText.Measure(graphics, "A\n\nA", font, bounds, format, metrics).Height);
        DrawingTextLayout retained = DrawingSourceText.CreateLayout(graphics, "A\n\nA", font, bounds, format, metrics);
        Assert.Equal(45, retained.ContentSize.Height);
        Assert.Equal(new Vector2(0, 30), retained.GetCaretStop(3).Position);
        Assert.Equal(15, retained.GetCaretStop(3).Height);
        DrawingSourceText.Draw(graphics, "A\n\nA", font, brush, new RectangleF(PointF.Empty, bounds), format, metrics);
        var direct = Assert.Single(context.Commands.Where(c => c.Type == RenderCommandType.DrawGlyphRun));
        Assert.Equal(new[] { 12f, 42f }, direct.GlyphPositions!.Select(p => p.Y));
        int count = context.Commands.Count;
        retained.Draw(graphics, brush, PointF.Empty);
        var replay = Assert.Single(context.Commands.Skip(count).Where(c => c.Type == RenderCommandType.DrawGlyphRun));
        Assert.Equal(direct.GlyphIndices, replay.GlyphIndices);
        Assert.Equal(direct.GlyphPositions, replay.GlyphPositions);
        Assert.Equal(ordinary, font.GetHeight(graphics));
        Assert.Equal(ordinary * 3, graphics.MeasureString("A\n\nA", font, bounds, format).Height, 4);
    }

    [Fact]
    public void OriginalHeightLimitUsesSourceRowsDuringTrimming()
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(new TtfFont(FontDeviceAutoScaleTests.WithAuthoredVdmx()), 12, unit: GraphicsUnit.Pixel);
        using StringFormat format = Format();
        format.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.LineLimit;
        using var brush = new SolidBrush(Color.Black);
        Assert.True(DrawingSourceText.TryGetLineMetrics(graphics, font, out var metrics));
        DrawingSourceText.Draw(graphics, "A\nA\nA", font, brush, new RectangleF(0, 0, 100, 30), format, metrics);
        var command = Assert.Single(context.Commands.Where(c => c.Type == RenderCommandType.DrawGlyphRun));
        Assert.Equal(new[] { 12f, 27f }, command.GlyphPositions!.Select(p => p.Y));
    }

    [Fact]
    public void FractionalAndMissingEmNeverUseNearestOrRoundedLineRecords()
    {
        using Graphics graphics = Graphics.FromProGpuDrawingContext(new DrawingContext());
        var face = new TtfFont(FontDeviceAutoScaleTests.WithAuthoredVdmx());
        foreach (float size in new[] { 12.5f, 13f })
        {
            using Font font = new(face, size, unit: GraphicsUnit.Pixel);
            Assert.False(DrawingSourceText.TryGetLineMetrics(graphics, font, out var metrics));
            Assert.Null(metrics);
        }
    }

    [Fact]
    public void EmptySourceRetainsItsCaretBandAndMismatchCannotPublishDrawCommands()
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        var face = new TtfFont(FontDeviceAutoScaleTests.WithAuthoredVdmx());
        using Font font = new(face, 12, unit: GraphicsUnit.Pixel);
        using Font wrong = new(face, 18, unit: GraphicsUnit.Pixel);
        using StringFormat format = Format();
        format.SetDigitSubstitution(0, StringDigitSubstitute.None);
        using var brush = new SolidBrush(Color.Black);
        Assert.True(DrawingSourceText.TryGetLineMetrics(graphics, font, out var metrics));
        DrawingTextLayout empty = DrawingSourceText.CreateLayout(graphics, "", font, new SizeF(100, 100), format, metrics);
        Assert.Equal(SizeF.Empty, empty.ContentSize);
        Assert.Equal(15, empty.GetCaretStop(0).Height);
        Assert.Throws<ArgumentException>(() => DrawingSourceText.Draw(graphics, "A", wrong, brush,
            new RectangleF(0, 0, 100, 100), format, metrics));
        Assert.Empty(context.Commands);
        graphics.Dispose();
        Assert.Throws<ArgumentException>(() => DrawingSourceText.TryGetLineMetrics(graphics, font, out _));
    }

    [Theory]
    [InlineData(144, 144, true, 25)]
    [InlineData(192, 192, true, 32)]
    [InlineData(144, 96, false, 0)]
    public void SourceFrameUsesActualSquareDeviceDpiWithoutBorrowingAnotherAxis(float dpiX, float dpiY, bool available, int height)
    {
        using Graphics graphics = Graphics.FromProGpuDrawingContext(new DrawingContext(),
            new RectangleF(0, 0, 100, 100), Matrix4x4.Identity, dpiX, dpiY);
        using Font font = new(new TtfFont(FontDeviceAutoScaleTests.WithAuthoredVdmx()), 9);
        Assert.Equal(available, DrawingSourceText.TryGetLineMetrics(graphics, font, out var metrics));
        if (available) Assert.Equal(height, metrics!.Height);
        else Assert.Null(metrics);
    }
}
