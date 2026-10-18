using System.Drawing;
using System.Drawing.Tests;
using LibreWinForms.Platform;
using ProGPU.Scene;
using ProGPU.Text;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuSourceDeviceTextTests
{
    [Theory]
    [InlineData(LibreTextFormat.Default, 35, 47)]
    [InlineData(LibreTextFormat.VerticalCenter, 31, 43)]
    [InlineData(LibreTextFormat.Bottom, 27, 39)]
    public void ShortSourceViewportRetainsExactFullLineAlignment(LibreTextFormat alignment, float originY, float baselineY)
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(new TtfFont(SourceDeviceFontFixture.Create()), 12, unit: GraphicsUnit.Pixel);
        var service = new ProGpuTextRendererService();
        service.DrawText(graphics, "A", font, new Rectangle(20, 35, 100, 7), Color.Black, Color.Empty,
            LibreTextFormat.SingleLine | LibreTextFormat.NoPadding | LibreTextFormat.NoPrefix | alignment);
        var run = Assert.Single(context.Commands, c => c.Type == RenderCommandType.DrawGlyphRun);
        Assert.Equal(originY, run.Position.Y); // Original fixture row15, viewport7, ascent12.
        Assert.Equal(baselineY, run.Position.Y + Assert.Single(run.GlyphPositions!).Y);
    }

    [Theory]
    [InlineData(9, GraphicsUnit.Point)]
    [InlineData(12, GraphicsUnit.Pixel)]
    public void SourceServiceKeepsPaintMeasureAndEditorOnTheExactDeviceRows(float size, GraphicsUnit unit)
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(new TtfFont(SourceDeviceFontFixture.Create()), size, unit: unit);
        float ordinaryHeight = font.GetHeight(graphics);
        var service = new ProGpuTextRendererService();
        const LibreTextFormat flags = LibreTextFormat.NoPadding | LibreTextFormat.NoPrefix | LibreTextFormat.WordBreak;
        Size measured = service.MeasureText(graphics, "A\nA", font, new Size(100, 1), flags);
        Assert.Equal(30, measured.Height); // DT_CALCRECT retains both original source rows.
        using ILibreTextLayout editor = service.CreateLayout(graphics, "A\nA", font, new Size(100, 100), flags);
        Assert.Equal(30, editor.ContentSize.Height);
        Assert.Equal(15, editor.GetCaret(2).Position.Y);
        Assert.Equal(15, editor.GetCaret(2).Height);
        service.DrawText(graphics, "A\nA", font, new Rectangle(0, 0, 100, 100), Color.Black, Color.Empty, flags);
        var glyphs = Assert.Single(context.Commands, c => c.Type == RenderCommandType.DrawGlyphRun);
        Assert.Equal(new[] { 12f, 27f }, glyphs.GlyphPositions!.Select(p => p.Y));
        Assert.Equal(ordinaryHeight, font.GetHeight(graphics));
        Size single = service.MeasureText(graphics, "A", font, new Size(100, 100),
            LibreTextFormat.SingleLine | LibreTextFormat.NoPrefix | LibreTextFormat.NoPadding);
        Size padded = service.MeasureText(graphics, "A", font, new Size(100, 100),
            LibreTextFormat.SingleLine | LibreTextFormat.NoPrefix);
        Size extra = service.MeasureText(graphics, "A", font, new Size(100, 100),
            LibreTextFormat.SingleLine | LibreTextFormat.NoPrefix | LibreTextFormat.LeftAndRightPadding);
        Assert.Equal(new Size(single.Width + 7, 15), padded); // Original height15: margins3+4.
        Assert.Equal(new Size(single.Width + 12, 15), extra); // Extra margins5+7.
    }
}
