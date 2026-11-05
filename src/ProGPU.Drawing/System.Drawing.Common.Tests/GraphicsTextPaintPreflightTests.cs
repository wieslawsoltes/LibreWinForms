using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Text;
using System.Numerics;
using ProGPU.Scene;
using ProGPU.SystemDrawing;
using ProGPU.Text;
using Xunit;

namespace System.Drawing.Tests;

public sealed class GraphicsTextPaintPreflightTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MalformedFontCannotPublishEvenAnEmptyLayoutClip(bool retained, bool clipped)
    {
        using var font = new Font(FontWithGasp(0x8000), 13, unit: GraphicsUnit.Pixel);
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context,
            new RectangleF(0, 0, 300, 80), Matrix4x4.Identity, 96, 96, 1f);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Trimming = StringTrimming.None,
            FormatFlags = StringFormatFlags.NoWrap | (clipped ? 0 : StringFormatFlags.NoClip)
        };
        format.SetDigitSubstitution(0, StringDigitSubstitute.None);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        DrawingTextLayout? layout = retained
            ? DrawingTextLayout.Create(graphics, "Name", font, new SizeF(200, 40), format) : null;
        Assert.Empty(context.Commands);
        Assert.Throws<InvalidDataException>(() =>
        {
            if (layout is not null) layout.Draw(graphics, Brushes.Black, new PointF(8, 8));
            else graphics.DrawString("Name", font, Brushes.Black, new RectangleF(8, 8, 200, 40), format);
        });
        Assert.Empty(context.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessfulPaintRetainsClipGlyphDecorationOrder(bool retained)
    {
        using var font = new Font(FontFamily.GenericSansSerif, 13, FontStyle.Underline, GraphicsUnit.Pixel);
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using var format = new StringFormat { Trimming = StringTrimming.None };
        format.SetDigitSubstitution(0, StringDigitSubstitute.None);
        var bounds = new RectangleF(8, 8, 200, 40);
        if (retained)
            DrawingTextLayout.Create(graphics, "Name", font, bounds.Size, format)
                .Draw(graphics, Brushes.Black, bounds.Location);
        else
            graphics.DrawString("Name", font, Brushes.Black, bounds, format);
        var commands = context.Commands.ToArray();
        Assert.Equal(RenderCommandType.PushClip, commands[0].Type);
        Assert.Equal(RenderCommandType.PopClip, commands[^1].Type);
        int glyph = Array.FindIndex(commands, c => c.Type == RenderCommandType.DrawGlyphRun);
        int decoration = Array.FindIndex(commands, c => c.Type == RenderCommandType.DrawRect);
        Assert.True(glyph > 0 && decoration > glyph && decoration < commands.Length - 1);
        Assert.Equal(1, commands.Count(c => c.Type == RenderCommandType.PushClip));
        Assert.Equal(1, commands.Count(c => c.Type == RenderCommandType.PopClip));
    }

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
