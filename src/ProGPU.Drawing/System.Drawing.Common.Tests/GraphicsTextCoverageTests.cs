using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ProGPU.Scene;
using ProGPU.SystemDrawing;
using Xunit;

namespace System.Drawing.Tests;

public sealed class GraphicsTextCoverageTests
{
    [Theory]
    [InlineData(TextRenderingHint.SingleBitPerPixel, false)]
    [InlineData(TextRenderingHint.SingleBitPerPixel, true)]
    [InlineData(TextRenderingHint.SingleBitPerPixelGridFit, false)]
    [InlineData(TextRenderingHint.SingleBitPerPixelGridFit, true)]
    public void ExplicitSingleBitDrawUsesBinaryCoverage(TextRenderingHint hint, bool retained)
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 12);
        using var brush = new SolidBrush(Color.Black);
        using StringFormat format = MakeFormat();
        graphics.TextRenderingHint = hint;
        if (retained)
            DrawingTextLayout.Create(graphics, "Name 123", font, new SizeF(200, 60), format)
                .Draw(graphics, brush, PointF.Empty);
        else
            graphics.DrawString("Name 123", font, brush, new RectangleF(0, 0, 200, 60), format);
        var runs = context.Commands.Where(c => c.Type == RenderCommandType.DrawGlyphRun).ToArray();
        Assert.NotEmpty(runs);
        Assert.All(runs, command => Assert.Equal(TextRenderingMode.Aliased, command.TextRenderingMode));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SourceGdiPolicyStaysIndependentOfLineMetricsAndLaterGraphicsState(bool retained, bool deviceMetrics)
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(new ProGPU.Text.TtfFont(FontDeviceAutoScaleTests.WithAuthoredVdmx()),
            12, unit: GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.Black);
        using StringFormat format = MakeFormat();
        ProGPU.Text.SourceDeviceLineMetrics? metrics = null;
        if (deviceMetrics) Assert.True(DrawingSourceText.TryGetLineMetrics(graphics, font, out metrics));
        var size = new SizeF(200, 60);
        DrawingTextLayout? layout = retained ? DrawingGdiText.CreateLayout(graphics, "Name 123", font, size, format, metrics) : null;
        foreach (TextRenderingHint hint in Enum.GetValues<TextRenderingHint>())
        {
            graphics.TextRenderingHint = hint;
            int start = context.Commands.Count;
            if (layout is not null) layout.Draw(graphics, brush, PointF.Empty);
            else DrawingGdiText.Draw(graphics, "Name 123", font, brush, new RectangleF(PointF.Empty, size), format, metrics);
            var source = context.Commands.Skip(start).Where(c => c.Type == RenderCommandType.DrawGlyphRun).ToArray();
            Assert.NotEmpty(source);
            // This preserves the old GDI paint route while its independent
            // font-quality/system/coverage policy is implemented and qualified.
            Assert.All(source, c => Assert.Equal(TextRenderingMode.Grayscale, c.TextRenderingMode));
            Assert.Equal(hint, graphics.TextRenderingHint);
            start = context.Commands.Count;
            graphics.DrawString("Name 123", font, brush, new RectangleF(PointF.Empty, size), format);
            var ordinary = context.Commands.Skip(start).Where(c => c.Type == RenderCommandType.DrawGlyphRun).ToArray();
            Assert.NotEmpty(ordinary);
            TextRenderingMode expected = hint is TextRenderingHint.SingleBitPerPixel or TextRenderingHint.SingleBitPerPixelGridFit
                ? TextRenderingMode.Aliased : TextRenderingMode.Grayscale;
            Assert.All(ordinary, c => Assert.Equal(expected, c.TextRenderingMode));
        }
    }

    [Fact]
    public void RetainedOrdinaryLayoutReadsCoverageAtPaintWithoutChangingGlyphPlacement()
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 12);
        using var brush = new SolidBrush(Color.Black);
        using StringFormat format = MakeFormat();
        var layout = DrawingTextLayout.Create(graphics, "Name 123", font, new SizeF(200, 60), format);
        graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixel;
        layout.Draw(graphics, brush, PointF.Empty);
        var first = context.Commands.Where(c => c.Type == RenderCommandType.DrawGlyphRun).ToArray();
        int start = context.Commands.Count;
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
        layout.Draw(graphics, brush, PointF.Empty);
        var second = context.Commands.Skip(start).Where(c => c.Type == RenderCommandType.DrawGlyphRun).ToArray();
        Assert.NotEmpty(first);
        Assert.Equal(first.Length, second.Length);
        for (int i = 0; i < first.Length; i++)
        {
            Assert.Equal(TextRenderingMode.Aliased, first[i].TextRenderingMode);
            Assert.Equal(TextRenderingMode.Grayscale, second[i].TextRenderingMode);
            Assert.Same(first[i].GlyphPositions, second[i].GlyphPositions);
            Assert.Same(first[i].GlyphIndices, second[i].GlyphIndices);
            Assert.Equal(first[i].Position, second[i].Position);
        }
    }

    [Theory]
    [InlineData(TextRenderingHint.SingleBitPerPixel, false)]
    [InlineData(TextRenderingHint.SingleBitPerPixel, true)]
    [InlineData(TextRenderingHint.SingleBitPerPixelGridFit, false)]
    [InlineData(TextRenderingHint.SingleBitPerPixelGridFit, true)]
    public void OpaqueSingleBitPaintContainsOnlyForegroundAndBackground(TextRenderingHint hint, bool retained)
    {
        using var bitmap = new Bitmap(200, 60);
        using var font = new Font(FontFamily.GenericSansSerif, 13);
        using var brush = new SolidBrush(Color.Black);
        using StringFormat format = MakeFormat();
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = hint;
            if (retained)
                DrawingTextLayout.Create(graphics, "Name Age 123", font, new SizeF(180, 40), format)
                    .Draw(graphics, brush, new PointF(8, 8));
            else graphics.DrawString("Name Age 123", font, brush, new RectangleF(8, 8, 180, 40), format);
        }
        // Inspect the complete original image once instead of submitting one
        // GPU readback per pixel. Keep every alpha/color/ink assertion below.
        int[] pixels = new int[bitmap.Width * bitmap.Height];
        BitmapData locked = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), pixels, y * bitmap.Width, bitmap.Width);
        }
        finally { bitmap.UnlockBits(locked); }
        foreach (int index in new[] { 0, bitmap.Width - 1, pixels.Length - bitmap.Width, pixels.Length - 1,
                     Array.FindIndex(pixels, value => value == Color.Black.ToArgb()) })
        {
            Assert.InRange(index, 0, pixels.Length - 1);
            Assert.Equal(bitmap.GetPixel(index % bitmap.Width, index / bitmap.Width).ToArgb(), pixels[index]);
        }
        int ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            Color pixel = Color.FromArgb(pixels[y * bitmap.Width + x]);
            Assert.Equal(255, pixel.A);
            Assert.True(pixel.ToArgb() == Color.White.ToArgb() || pixel.ToArgb() == Color.Black.ToArgb(),
                $"Single-bit coverage produced an intermediate pixel at ({x},{y}): {pixel}.");
            if (pixel.ToArgb() == Color.Black.ToArgb()) ink++;
        }
        Assert.True(ink > 0);
    }

    private static StringFormat MakeFormat()
    {
        var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None,
            HotkeyPrefix = HotkeyPrefix.None
        };
        format.SetDigitSubstitution(0, StringDigitSubstitute.None);
        return format;
    }
}
