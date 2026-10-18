using ProGPU.Scene;
using ProGPU.SystemDrawing;
using Xunit;

namespace System.Drawing.Tests;

public sealed class GraphicsTextFailureTests
{
    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    public void FailedTextRetiresOnlyItsOwnClip(bool sourceGdi, bool retained, int failingConversion)
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Underline);
        using var brush = new FailingBrush(failingConversion);
        using var format = new StringFormat { Trimming = StringTrimming.None };
        format.SetDigitSubstitution(0, StringDigitSubstitute.None);
        var outer = new Rect(0, 0, 200, 100);
        context.PushClip(outer);
        int start = context.Commands.Count;
        var bounds = new RectangleF(5, 5, 80, 40);
        Exception failure = Assert.Throws<InvalidOperationException>(() =>
        {
            if (retained)
            {
                var layout = sourceGdi
                    ? DrawingGdiText.CreateLayout(graphics, "Name", font, bounds.Size, format)
                    : DrawingTextLayout.Create(graphics, "Name", font, bounds.Size, format);
                layout.Draw(graphics, brush, bounds.Location);
            }
            else if (sourceGdi) DrawingGdiText.Draw(graphics, "Name", font, brush, bounds, format);
            else graphics.DrawString("Name", font, brush, bounds, format);
        });
        Assert.Same(brush.Failure, failure);
        var text = context.Commands.Skip(start).ToArray();
        Assert.Equal(1, text.Count(c => c.Type == RenderCommandType.PushClip));
        Assert.Equal(1, text.Count(c => c.Type == RenderCommandType.PopClip));
        Assert.Equal(RenderCommandType.PopClip, text[^1].Type);
        Assert.Equal(failingConversion == 2, text.Any(c => c.Type == RenderCommandType.DrawGlyphRun));
        using var good = new SolidBrush(Color.Red);
        graphics.FillRectangle(good, 160, 60, 10, 10);
        Assert.Equal(RenderCommandType.DrawRect, context.Commands[^1].Type);
        context.PopClip();
        Assert.Equal(2, context.Commands.Count(c => c.Type == RenderCommandType.PopClip));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void LaterPaintOutsideFailedTextBoundsRemainsVisible(bool sourceGdi, int failingConversion)
    {
        using var bitmap = new Bitmap(200, 100);
        using var font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Underline);
        using var bad = new FailingBrush(failingConversion);
        using var good = new SolidBrush(Color.Red);
        using var format = new StringFormat { Trimming = StringTrimming.None };
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            var bounds = new RectangleF(5, 5, 80, 40);
            Assert.Same(bad.Failure, Assert.Throws<InvalidOperationException>(() =>
            {
                if (sourceGdi) DrawingGdiText.Draw(graphics, "Name", font, bad, bounds, format);
                else graphics.DrawString("Name", font, bad, bounds, format);
            }));
            graphics.FillRectangle(good, 160, 60, 10, 10);
        }
        for (int y = 61; y < 69; y++)
        for (int x = 161; x < 169; x++)
            Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(x, y).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(150, 60).ToArgb());
    }

    private sealed class FailingBrush(int failingConversion) : Brush
    {
        private int _conversions;
        public InvalidOperationException Failure { get; } = new("Original text brush failure");
        public override object Clone() => new FailingBrush(failingConversion);
        internal override ProGPU.Vector.Brush ToProGpuBrush()
        {
            if (++_conversions == failingConversion) throw Failure;
            return new ProGPU.Vector.SolidColorBrush(new System.Numerics.Vector4(0, 0, 0, 1));
        }
    }
}
