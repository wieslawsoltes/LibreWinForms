using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Numerics;
using System.Runtime.InteropServices;
using ProGPU.Backend.Native;
using ProGPU.Scene;
using ProGPU.SystemDrawing;
using ProGPU.Text;
using Xunit;

namespace System.Drawing.Tests;

public sealed class GraphicsNativeGlyphTests
{
    [Theory]
    [InlineData(1, TextRenderingHint.AntiAliasGridFit, TextRenderingMode.Aliased)]
    [InlineData(3, TextRenderingHint.AntiAliasGridFit, TextRenderingMode.Grayscale)]
    [InlineData(5, TextRenderingHint.AntiAliasGridFit, TextRenderingMode.Aliased)]
    [InlineData(7, TextRenderingHint.AntiAliasGridFit, TextRenderingMode.Grayscale)]
    [InlineData(15, TextRenderingHint.AntiAliasGridFit, TextRenderingMode.Grayscale)]
    [InlineData(2, TextRenderingHint.SingleBitPerPixelGridFit, TextRenderingMode.Aliased)]
    [InlineData(15, TextRenderingHint.SingleBitPerPixelGridFit, TextRenderingMode.Aliased)]
    public void OwnedGridFitUsesOriginalFontRecommendationWithoutChangingPlacements(
        int flags, TextRenderingHint hint, TextRenderingMode coverage)
    {
        using var font = new Font(FontWithGasp(flags), 13, unit: GraphicsUnit.Pixel);
        var originalContext = new DrawingContext();
        var nativeContext = new DrawingContext();
        using Graphics original = Graphics.FromProGpuDrawingContext(originalContext);
        using Graphics native = Target(nativeContext);
        using StringFormat format = Format();
        original.TextRenderingHint = native.TextRenderingHint = hint;
        original.DrawString("Name 123", font, Brushes.Black, new RectangleF(8, 8, 200, 40), format);
        native.DrawString("Name 123", font, Brushes.Black, new RectangleF(8, 8, 200, 40), format);
        RenderCommand before = Assert.Single(originalContext.Commands);
        RenderCommand after = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawGlyphRun, before.Type);
        Assert.Equal(RenderCommandType.DrawHintedGlyphs, after.Type);
        Assert.Equal(coverage, after.TextRenderingMode);
        Assert.Equal(before.Position, after.Position);
        Assert.Equal(before.Transform, after.Transform);
        FontGlyphPlacement[] placements = after.HintedGlyphGeometry!.FontPlacements.ToArray();
        Assert.Equal(before.GlyphIndices!.Select(value => (uint)value), placements.Select(value => value.GlyphId));
        Assert.Equal(before.GlyphPositions, placements.Select(value => value.Position));
        Assert.Equal(original.MeasureString("Name 123", font), native.MeasureString("Name 123", font));
        originalContext.Clear();
        nativeContext.Clear();
    }

    [Fact]
    public void RetainedWarmPaintReusesTheOwnedGeometryAcrossOrigins()
    {
        using var font = new Font(FontWithGasp(1), 13, unit: GraphicsUnit.Pixel);
        var context = new DrawingContext();
        using Graphics graphics = Target(context);
        using StringFormat format = Format();
        format.Trimming = StringTrimming.None;
        format.SetDigitSubstitution(0, StringDigitSubstitute.None);
        DrawingTextLayout layout = DrawingTextLayout.Create(graphics, "Name 123", font, new SizeF(200, 40), format);
        graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        layout.Draw(graphics, Brushes.Black, new PointF(8, 8));
        RenderCommand first = Assert.Single(context.Commands);
        layout.Draw(graphics, Brushes.Black, new PointF(16, 16));
        RenderCommand second = context.Commands[1];
        Assert.Equal(RenderCommandType.DrawHintedGlyphs, first.Type);
        Assert.Same(first.HintedGlyphGeometry, second.HintedGlyphGeometry);
        Assert.Equal(new Vector2(8, 8), second.Position - first.Position);
        context.Clear();
    }

    [Theory]
    [InlineData(0)] // Unknown raster target.
    [InlineData(1)] // Fractional original device em.
    [InlineData(2)] // Source scale transform.
    [InlineData(3)] // Independent target axes.
    [InlineData(4)] // Explicit GDI source quality.
    [InlineData(5)] // Ordinary unfitted GDI+ antialiasing.
    public void IndependentSourceContractsDoNotSilentlyEnterNativeGridFit(int scenario)
    {
        using var font = new Font(FontWithGasp(1), scenario == 1 ? 13.5f : 13, unit: GraphicsUnit.Pixel);
        var context = new DrawingContext();
        using Graphics graphics = scenario == 0 ? Graphics.FromProGpuDrawingContext(context)
            : Graphics.FromProGpuDrawingContext(context, new(0, 0, 300, 80), Matrix4x4.Identity,
                scenario == 3 ? 144 : 96, 96, 1f);
        using StringFormat format = Format();
        graphics.TextRenderingHint = scenario == 5 ? TextRenderingHint.AntiAlias : TextRenderingHint.AntiAliasGridFit;
        if (scenario == 2) graphics.ScaleTransform(2, 2);
        var bounds = new RectangleF(8, 8, 200, 40);
        if (scenario == 4) DrawingGdiText.Draw(graphics, "Name", font, Brushes.Black, bounds, format);
        else graphics.DrawString("Name", font, Brushes.Black, bounds, format);
        Assert.Equal(RenderCommandType.DrawGlyphRun, Assert.Single(context.Commands).Type);
        context.Clear();
    }

    [Fact]
    public void MalformedOriginalRecommendationRejectsBeforeGlyphPublication()
    {
        using var font = new Font(FontWithGasp(0x8000), 13, unit: GraphicsUnit.Pixel);
        var context = new DrawingContext();
        using Graphics graphics = Target(context);
        using StringFormat format = Format();
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Assert.Throws<InvalidDataException>(() => graphics.DrawString("Name", font, Brushes.Black,
            new RectangleF(8, 8, 200, 40), format));
        Assert.Empty(context.Commands);
    }

    [Fact]
    public void MutableCallerArraysCannotRewriteAStoredNativeGeneration()
    {
        TtfFont font = FontWithGasp(1);
        ushort[] glyphs = [font.GetGlyphIndex('I')];
        Vector2[] positions = [new(0f, 0f)];
        using var first = DrawingNativeGlyphCache.Acquire(font, glyphs, positions, 13 * 64, 1,
            NativeFontOutlinePolicy.Monochrome);
        glyphs[0] = font.GetGlyphIndex('A');
        positions[0] = new(0.25f, 2f);
        using var changed = DrawingNativeGlyphCache.Acquire(font, glyphs, positions, 13 * 64, 1,
            NativeFontOutlinePolicy.Monochrome);
        Assert.NotSame(first.Geometry, changed.Geometry);
        Assert.Equal((uint)font.GetGlyphIndex('I'), first.Geometry.FontPlacements[0].GlyphId);
        Assert.Equal(Vector2.Zero, first.Geometry.FontPlacements[0].Position);
        Assert.Equal(new Vector2(0.25f, 2f), changed.Geometry.FontPlacements[0].Position);
    }

    [Fact]
    public void EvictionProtectsAnActiveCallerAndThenTheRecordedGpuOwner()
    {
        TtfFont font = FontWithGasp(1);
        ushort[] glyphs = [font.GetGlyphIndex('I')];
        Vector2[] positions = [Vector2.Zero];
        using var first = DrawingNativeGlyphCache.Acquire(font, glyphs, positions, 13 * 64, 1,
            NativeFontOutlinePolicy.Monochrome);
        HintedGlyphGeometry geometry = first.Geometry;
        for (uint em = 14; em < 31; em++)
        {
            using var next = DrawingNativeGlyphCache.Acquire(font, glyphs, positions, em * 64, 1,
                NativeFontOutlinePolicy.Monochrome);
        }
        Assert.False(geometry.IsDisposed);
        using var bitmap = new Bitmap(64, 48);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            Vector2 origin = new(8, 30);
            Assert.True(DrawingContext.TryGetHintedGlyphInkBounds(geometry, origin, out Rect ink, out bool hasInk));
            Assert.True(hasInk);
            graphics.DrawingContext.DrawHintedGlyphs(geometry, origin, ink,
                new ProGPU.Vector.SolidColorBrush(new Vector4(0, 0, 0, 1)), textRenderingMode: TextRenderingMode.Aliased);
        }
        first.Dispose();
        Assert.True(geometry.IsDisposed);
        BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, 64, 48), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] rgba = new byte[locked.Stride * locked.Height];
            Marshal.Copy(locked.Scan0, rgba, 0, rgba.Length);
            Assert.Contains((byte)0, rgba);
            for (int offset = 0; offset < rgba.Length; offset += 4)
            {
                Assert.Equal(255, rgba[offset + 3]);
                Assert.True(rgba[offset] is 0 or 255);
                Assert.Equal(rgba[offset], rgba[offset + 1]);
                Assert.Equal(rgba[offset], rgba[offset + 2]);
            }
        }
        finally { bitmap.UnlockBits(locked); }
    }

    private static Graphics Target(DrawingContext context) => Graphics.FromProGpuDrawingContext(context,
        new RectangleF(0, 0, 300, 80), Matrix4x4.Identity, 96, 96, 1f);

    private static StringFormat Format() => new(StringFormat.GenericTypographic)
    { FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap };

    private static TtfFont FontWithGasp(int flags)
    {
        // Authored policy controls over the owned static Inter fixture, not
        // replacement Microsoft pixels or a modified font in product paint.
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "Inter-Regular.ttf"));
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        int head = -1;
        bool replaced = false;
        for (int index = 0; index < count; index++)
        {
            int entry = 12 + index * 16;
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry));
            int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry + 8)));
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry + 12)));
            if (tag == 0x68656164) head = offset;
            if (tag != 0x67617370) continue;
            Assert.Equal(8, length);
            Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 2)));
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset + 6), checked((ushort)flags));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(entry + 4), Sum(bytes.AsSpan(offset, length)));
            replaced = true;
        }
        Assert.True(replaced && head >= 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(head + 8), 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(head + 8), unchecked(0xB1B0AFBAu - Sum(bytes)));
        return new TtfFont(bytes);
    }

    private static uint Sum(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        for (int index = 0; index < bytes.Length; index += 4)
        {
            uint word = 0;
            for (int byteIndex = 0; byteIndex < 4; byteIndex++)
                word = (word << 8) | (index + byteIndex < bytes.Length ? bytes[index + byteIndex] : 0u);
            sum = unchecked(sum + word);
        }
        return sum;
    }
}
