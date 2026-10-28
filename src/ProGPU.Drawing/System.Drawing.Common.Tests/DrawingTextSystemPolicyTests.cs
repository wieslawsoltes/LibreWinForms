using System.Drawing;
using System.Drawing.Text;
using ProGPU.Scene;
using ProGPU.SystemDrawing;
using Xunit;

namespace System.Drawing.Tests;

public sealed class DrawingTextSystemPolicyTests
{
    [Fact]
    public void NativeQueriesPreserveRawWordsAndIndependentFailures()
    {
        var calls = new List<uint>();
        bool Query(uint action, out uint value)
        {
            calls.Add(action);
            value = action switch { 0x004A => uint.MaxValue, 0x200A => 99, 0x200C => 7, _ => 0x80000000 };
            return action != 0x200A;
        }
        var snapshot = WindowsTextSystemSettings.Capture(Query);
        Assert.Equal(new uint[] { 0x004A, 0x200A, 0x200C, 0x2012 }, calls);
        Assert.Equal(new DrawingTextSystemSettings(uint.MaxValue, null, 7, 0x80000000), snapshot);
        var policy = DrawingGdiTextPolicy.FromSystemSnapshot(DrawingGdiFontQuality.Proof, snapshot);
        Assert.Equal(snapshot, policy.SystemSnapshot);
        Assert.Null(policy.SystemSettings);
    }

    [Fact]
    public void LegacySettingsKeepTheirOriginalValuesAndUnknownOrientation()
    {
        var legacy = new DrawingGdiSystemSettings(false, -42, int.MaxValue);
        var policy = new DrawingGdiTextPolicy(DrawingGdiFontQuality.Draft, legacy);
        Assert.Equal(legacy, policy.SystemSettings);
        Assert.Equal(new DrawingTextSystemSettings(0, unchecked((uint)-42), int.MaxValue, null), policy.SystemSnapshot);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DrawingGdiTextPolicy.FromSystemSnapshot((DrawingGdiFontQuality)99, null));
    }

    [Theory]
    [InlineData(TextRenderingHint.SystemDefault, 1)]
    [InlineData(TextRenderingHint.ClearTypeGridFit, 1)]
    [InlineData(TextRenderingHint.AntiAlias, 0)]
    [InlineData(TextRenderingHint.SingleBitPerPixel, 0)]
    public void ActualPaintCapturesOnceForWholeRunAndMeasurementNeverReads(TextRenderingHint hint, int expected)
    {
        var context = new DrawingContext();
        using var graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 13);
        using var format = Format();
        int calls = 0;
        DrawingTextSystemPolicy.Bind(graphics, () => { calls++; return new(1, 2, 1700, 0); });
        graphics.TextRenderingHint = hint;
        graphics.TextContrast = 9;
        _ = graphics.MeasureString("A whole source run", font);
        var layout = DrawingTextLayout.Create(graphics, "A whole source run", font, new SizeF(250, 50), format);
        _ = layout.ContentSize;
        Assert.Equal(0, calls);
        layout.Draw(graphics, Brushes.Black, PointF.Empty);
        Assert.Equal(expected, calls);
        Assert.All(context.Commands.Where(c => c.Type == RenderCommandType.DrawGlyphRun), c =>
            Assert.Equal(hint == TextRenderingHint.SingleBitPerPixel ? TextRenderingMode.Aliased : TextRenderingMode.Grayscale,
                c.TextRenderingMode));
        context.Clear();
    }

    [Fact]
    public void GdiAndExplicitUnknownDoNotBorrowGdiPlusPolicy()
    {
        var context = new DrawingContext();
        using var graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 13);
        using var format = Format();
        int calls = 0;
        DrawingTextSystemPolicy.Bind(graphics, () => { calls++; return null; });
        DrawingGdiText.Draw(graphics, "Name", font, Brushes.Black, new RectangleF(0, 0, 200, 50), format,
            null, DrawingGdiTextPolicy.FromSystemSnapshot(DrawingGdiFontQuality.ClearType, new(0, null, null, 99)));
        Assert.Equal(0, calls);
        DrawingGdiPlusTextPolicy policy = graphics.CaptureGdiPlusTextPolicy();
        Assert.Equal(TextRenderingHint.SystemDefault, policy.Hint);
        Assert.Null(policy.SystemSettings);
        Assert.Equal(1, calls);
        context.Clear();
    }

    [Theory]
    [InlineData(0)] // Disposal.
    [InlineData(1)] // Provider replacement.
    [InlineData(2)] // Source hint mutation.
    [InlineData(3)] // Source contrast mutation.
    [InlineData(4)] // Recursive capture.
    public void ReentrantCaptureCannotPublishAnObsoleteObservation(int change)
    {
        var context = new DrawingContext();
        using var graphics = Graphics.FromProGpuDrawingContext(context);
        DrawingTextSystemPolicy.Bind(graphics, () =>
        {
            switch (change)
            {
                case 0: graphics.Dispose(); break;
                case 1: DrawingTextSystemPolicy.Bind(graphics, () => null); break;
                case 2: graphics.TextRenderingHint = TextRenderingHint.AntiAlias; break;
                case 3: graphics.TextContrast = 8; break;
                case 4: _ = graphics.CaptureGdiPlusTextPolicy(); break;
            }
            return new(1, 2, 1700, 1);
        });
        if (change == 0) Assert.Throws<ArgumentException>(() => graphics.CaptureGdiPlusTextPolicy());
        else Assert.Throws<InvalidOperationException>(() => graphics.CaptureGdiPlusTextPolicy());
        Assert.Empty(context.Commands);
    }

    [Fact]
    public void RetainedGdiSnapshotKeepsRequestAndFontOwnerWithoutReadingGdiPlusProvider()
    {
        var context = new DrawingContext();
        using var graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 13);
        using var format = Format();
        var layout = DrawingGdiText.CreateLayout(graphics, "Name", font, new SizeF(200, 50), format,
            null, DrawingGdiFontQuality.Proof);
        font.Dispose();
        DrawingTextSystemPolicy.Bind(graphics, () => throw new InvalidOperationException("GDI+ provider must not run"));
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        layout.DrawWithSystemSnapshot(graphics, Brushes.Black, PointF.Empty, new(null, 2, null, 1));
        Assert.Equal(DrawingGdiFontQuality.Proof, layout.RequestedGdiQuality);
        Assert.Contains(context.Commands, c => c.Type == RenderCommandType.DrawGlyphRun);
        context.Clear();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedObservationPrecedesClipsAndPreservesOriginalException(bool retained)
    {
        var context = new DrawingContext();
        using var graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 13);
        using var format = Format();
        format.FormatFlags &= ~StringFormatFlags.NoClip;
        var layout = DrawingTextLayout.Create(graphics, "Clipped paint", font, new SizeF(120, 40), format);
        var failure = new InvalidOperationException("original observation failure");
        DrawingTextSystemPolicy.Bind(graphics, () => throw failure);
        void Paint()
        {
            if (retained) layout.Draw(graphics, Brushes.Black, PointF.Empty);
            else graphics.DrawString("Clipped paint", font, Brushes.Black, new RectangleF(0, 0, 120, 40), format);
        }
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(Paint));
        Assert.Empty(context.Commands);
        DrawingTextSystemPolicy.Bind(graphics, () => null);
        Paint(); // A failed capture must clear its reentrancy guard.
        Assert.Contains(context.Commands, c => c.Type == RenderCommandType.DrawGlyphRun);
        context.Clear();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaintCapturesTransformAfterObservation(bool retained)
    {
        var context = new DrawingContext();
        using var graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 13);
        using var format = Format();
        format.FormatFlags &= ~StringFormatFlags.NoClip;
        var layout = DrawingTextLayout.Create(graphics, "Fresh frame", font, new SizeF(120, 40), format);
        int calls = 0;
        DrawingTextSystemPolicy.Bind(graphics, () => { calls++; graphics.TranslateTransform(7, 11); return null; });
        if (retained) layout.Draw(graphics, Brushes.Black, PointF.Empty);
        else graphics.DrawString("Fresh frame", font, Brushes.Black, new RectangleF(0, 0, 120, 40), format);
        Assert.Equal(1, calls);
        var glyphs = context.Commands.Where(c => c.Type == RenderCommandType.DrawGlyphRun).ToArray();
        Assert.NotEmpty(glyphs);
        Assert.All(glyphs, c => { Assert.Equal(7, c.Transform.M41); Assert.Equal(11, c.Transform.M42); });
        context.Clear();
    }

    [Fact]
    public void RecursivePaintCannotPublishEvenAfterHintMutation()
    {
        var context = new DrawingContext();
        using var graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 13);
        using var format = Format();
        format.FormatFlags &= ~StringFormatFlags.NoClip;
        DrawingTextSystemPolicy.Bind(graphics, () =>
        {
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
            graphics.DrawString("recursive", font, Brushes.Black, new RectangleF(0, 0, 120, 40), format);
            return null;
        });
        Assert.Throws<InvalidOperationException>(() =>
            graphics.DrawString("outer", font, Brushes.Black, new RectangleF(0, 0, 120, 40), format));
        Assert.Empty(context.Commands);
    }

    private static StringFormat Format()
    {
        var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Trimming = StringTrimming.None,
            FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap
        };
        format.SetDigitSubstitution(0, StringDigitSubstitute.None);
        return format;
    }
}
