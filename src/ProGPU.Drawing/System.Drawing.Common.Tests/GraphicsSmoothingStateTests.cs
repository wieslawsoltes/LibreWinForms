using System.ComponentModel;
using System.Drawing.Drawing2D;
using Xunit;

namespace System.Drawing.Tests;

public sealed class GraphicsSmoothingStateTests
{
    [Fact]
    public void NewGraphicsUsesOriginalUnsmoothedDefault()
    {
        using var bitmap = new Bitmap(8, 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        Assert.Equal(SmoothingMode.None, graphics.SmoothingMode);
    }

    [Theory]
    [InlineData(SmoothingMode.Default, SmoothingMode.None)]
    [InlineData(SmoothingMode.HighSpeed, SmoothingMode.None)]
    [InlineData(SmoothingMode.None, SmoothingMode.None)]
    [InlineData(SmoothingMode.HighQuality, SmoothingMode.AntiAlias)]
    [InlineData(SmoothingMode.AntiAlias, SmoothingMode.AntiAlias)]
    public void OriginalAliasesNormalizeAndSurviveSavedContexts(SmoothingMode requested, SmoothingMode expected)
    {
        using var bitmap = new Bitmap(8, 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = requested;
        Assert.Equal(expected, graphics.SmoothingMode);
        GraphicsState state = graphics.Save();
        graphics.SmoothingMode = expected == SmoothingMode.None ? SmoothingMode.AntiAlias : SmoothingMode.None;
        graphics.Restore(state);
        Assert.Equal(expected, graphics.SmoothingMode);
        GraphicsContainer container = graphics.BeginContainer();
        Assert.Equal(SmoothingMode.None, graphics.SmoothingMode);
        graphics.EndContainer(container);
        Assert.Equal(expected, graphics.SmoothingMode);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(5)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void UndefinedValuesPreservePreviousState(int requested)
    {
        using var bitmap = new Bitmap(8, 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Assert.Throws<InvalidEnumArgumentException>("value", () => graphics.SmoothingMode = (SmoothingMode)requested);
        Assert.Equal(SmoothingMode.AntiAlias, graphics.SmoothingMode);
    }

    [Fact]
    public void InvalidSentinelPreservesPreviousState()
    {
        using var bitmap = new Bitmap(8, 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.None;
        Assert.Throws<ArgumentException>(() => graphics.SmoothingMode = SmoothingMode.Invalid);
        Assert.Equal(SmoothingMode.None, graphics.SmoothingMode);
    }

    [Fact]
    public void DisposedGraphicsRejectsStateAccess()
    {
        using var bitmap = new Bitmap(8, 8);
        Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Dispose();
        Assert.Throws<ArgumentException>(() => graphics.SmoothingMode);
        Assert.Throws<ArgumentException>(() => graphics.SmoothingMode = SmoothingMode.None);
    }
}
