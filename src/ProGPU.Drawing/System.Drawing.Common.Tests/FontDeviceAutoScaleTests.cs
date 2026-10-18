using System.Buffers.Binary;
using ProGPU.SystemDrawing;
using ProGPU.Text;
using Xunit;

namespace System.Drawing.Tests;

public sealed class FontDeviceAutoScaleTests
{
    [Theory]
    [InlineData(9f, 96, 15)]
    [InlineData(8.25f, 96, 13)]
    [InlineData(9f, 144, 25)]
    [InlineData(9f, 192, 32)]
    public void ExactDeviceRecordUsesSourceEmRoundingWithoutChangingDrawingMetrics(float points, int dpi, int expected)
    {
        var face = new TtfFont(WithAuthoredVdmx());
        using Font font = new(face, points);
        Assert.Same(face, font.TtfFont);
        float original = font.GetHeight(dpi);
        Assert.True(DrawingFontMetrics.TryGetAutoScaleHeight(font, dpi, out int height));
        Assert.Equal(expected, height);
        Assert.Equal(original, font.GetHeight(dpi));
        Assert.Equal(points, font.Size);
    }

    [Fact]
    public void MissingSizeAndPixelUnitDoNotInventDeviceMetrics()
    {
        var face = new TtfFont(WithAuthoredVdmx());
        using Font missing = new(face, 9.5f);
        Assert.False(DrawingFontMetrics.TryGetAutoScaleHeight(missing, 96, out int height));
        Assert.Equal(0, height);
        using Font pixel = new(face, 12, FontStyle.Regular, GraphicsUnit.Pixel);
        Assert.False(DrawingFontMetrics.TryGetAutoScaleHeight(pixel, 192, out height));
        Assert.Equal(0, height);
    }

    [Fact]
    public void MalformedRecordIsNotAnUnavailableMetric()
    {
        var face = new TtfFont(WithAuthoredVdmx(malformed: true));
        using Font font = new(face, 9);
        Assert.Throws<InvalidDataException>(() => DrawingFontMetrics.TryGetAutoScaleHeight(font, 96, out _));
    }

    internal static byte[] WithAuthoredVdmx(bool malformed = false) => SourceDeviceFontFixture.Create(malformed);
}
