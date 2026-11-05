using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Xunit;

namespace System.Drawing.Tests;

public sealed class GraphicsTextRenderingStateTests
{
    [Fact]
    public void NewGraphicsRetainsSystemSelectedTextQuality()
    {
        using var bitmap = new Bitmap(8, 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        Assert.Equal(TextRenderingHint.SystemDefault, graphics.TextRenderingHint);
    }

    [Theory]
    [InlineData(TextRenderingHint.SystemDefault)]
    [InlineData(TextRenderingHint.SingleBitPerPixelGridFit)]
    [InlineData(TextRenderingHint.SingleBitPerPixel)]
    [InlineData(TextRenderingHint.AntiAliasGridFit)]
    [InlineData(TextRenderingHint.AntiAlias)]
    [InlineData(TextRenderingHint.ClearTypeGridFit)]
    public void SelectedQualitySurvivesSaveAndNestedContainer(TextRenderingHint requested)
    {
        using var bitmap = new Bitmap(8, 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = requested;
        GraphicsState saved = graphics.Save();
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
        GraphicsContainer outer = graphics.BeginContainer();
        Assert.Equal(TextRenderingHint.SystemDefault, graphics.TextRenderingHint);
        graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixel;
        GraphicsContainer inner = graphics.BeginContainer();
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.EndContainer(inner);
        Assert.Equal(TextRenderingHint.SingleBitPerPixel, graphics.TextRenderingHint);
        graphics.EndContainer(outer);
        Assert.Equal(TextRenderingHint.AntiAlias, graphics.TextRenderingHint);
        graphics.Restore(saved);
        Assert.Equal(requested, graphics.TextRenderingHint);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void UndefinedQualityCannotReplaceTheSelectedState(int value)
    {
        using var bitmap = new Bitmap(8, 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Assert.Throws<InvalidEnumArgumentException>("value",
            () => graphics.TextRenderingHint = (TextRenderingHint)value);
        Assert.Equal(TextRenderingHint.AntiAliasGridFit, graphics.TextRenderingHint);
    }

    [Fact]
    public void RetiredGraphicsRejectsBothReadingAndChangingTextQuality()
    {
        using var bitmap = new Bitmap(8, 8);
        Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Dispose();
        Assert.Throws<ArgumentException>(() => graphics.TextRenderingHint);
        Assert.Throws<ArgumentException>(() => graphics.TextRenderingHint = TextRenderingHint.SystemDefault);
    }
}
