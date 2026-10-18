using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Numerics;
using System.Runtime.InteropServices;
using ProGPU.Scene;
using ProGPU.SystemDrawing;
using ProGPU.Text;
using Xunit;

namespace System.Drawing.Tests;

public sealed class GraphicsIntegerGlyphOriginTests
{
    [Theory]
    [InlineData(1f, false)]
    [InlineData(1f, true)]
    [InlineData(1.5f, false)]
    [InlineData(1.5f, true)]
    [InlineData(2f, false)]
    [InlineData(2f, true)]
    public void ExplicitRasterTargetSelectsOriginPolicyWithoutChangingLayout(float scale, bool retained)
    {
        using var font = Font();
        using var format = Format();
        var context = new DrawingContext();
        using var graphics = Target(context, scale);
        var bounds = new RectangleF(8.5f, 8.5f, 240, 60);
        var layout = DrawingTextLayout.Create(graphics, "Name 123", font, bounds.Size, format);
        SizeF measured = graphics.MeasureString("Name 123", font);
        foreach (var hint in new[] { TextRenderingHint.SingleBitPerPixel, TextRenderingHint.AntiAlias })
        {
            graphics.TextRenderingHint = hint;
            if (retained) layout.Draw(graphics, Brushes.Black, bounds.Location);
            else graphics.DrawString("Name 123", font, Brushes.Black, bounds, format);
        }
        var commands = context.Commands.Where(c => c.Type == RenderCommandType.DrawGlyphRun).ToArray();
        Assert.Equal(2, commands.Length);
        var first = commands[0];
        var second = commands[1];
        Assert.Equal(TextHintingMode.IntegerPixel, first.TextHintingMode);
        Assert.True(first.PreferGlyphAtlas);
        Assert.Equal(TextHintingMode.Auto, second.TextHintingMode);
        Assert.False(second.PreferGlyphAtlas);
        Assert.Equal(first.GlyphIndices, second.GlyphIndices);
        Assert.Equal(first.GlyphPositions, second.GlyphPositions);
        Assert.Equal(first.Position, second.Position);
        Assert.Equal(first.Transform, second.Transform);
        Assert.Equal(measured, graphics.MeasureString("Name 123", font));
        if (retained)
        {
            Assert.Same(first.GlyphIndices, second.GlyphIndices);
            Assert.Same(first.GlyphPositions, second.GlyphPositions);
        }
        context.Clear();
    }

    [Theory]
    [InlineData(0)] // Target raster scale is unknown.
    [InlineData(1)] // Independent layout axes.
    [InlineData(2)] // Nontranslation source frame.
    [InlineData(3)] // GDI source identity.
    [InlineData(4)] // Ordinary antialiasing.
    [InlineData(5)] // ClearType.
    [InlineData(6)] // Synthetic bold.
    [InlineData(7)] // Synthetic italic.
    public void IndependentSourcePoliciesKeepTheirExistingOriginRoute(int scenario)
    {
        using var font = Font(scenario == 6 ? FontStyle.Bold : scenario == 7 ? FontStyle.Italic : FontStyle.Regular);
        using var format = Format();
        var context = new DrawingContext();
        using var graphics = scenario == 0 ? Graphics.FromProGpuDrawingContext(context) :
            Graphics.FromProGpuDrawingContext(context, new(0, 0, 300, 80), Matrix4x4.Identity,
                scenario == 1 ? 144 : 96, 96, 1f);
        graphics.TextRenderingHint = scenario == 4 ? TextRenderingHint.AntiAlias :
            scenario == 5 ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.SingleBitPerPixel;
        if (scenario == 2) graphics.TransformElements = new Matrix3x2(1, float.Epsilon, 0, 1, 0, 0);
        var bounds = new RectangleF(8.5f, 8.5f, 240, 60);
        if (scenario == 3) DrawingGdiText.Draw(graphics, "Name 123", font, Brushes.Black, bounds, format);
        else graphics.DrawString("Name 123", font, Brushes.Black, bounds, format);
        var command = Assert.Single(context.Commands, c => c.Type == RenderCommandType.DrawGlyphRun);
        Assert.Equal(TextHintingMode.Auto, command.TextHintingMode);
        Assert.False(command.PreferGlyphAtlas);
        context.Clear();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteBitmapMatchesIndependentRoundedPositionReplay(bool retained)
    {
        using var font = Font();
        using var format = Format();
        using var actual = new Bitmap(280, 80);
        using var expected = new Bitmap(280, 80);
        using (var graphics = Graphics.FromImage(actual))
        using (var reference = Graphics.FromImage(expected))
        {
            graphics.Clear(Color.White);
            reference.Clear(Color.White);
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixel;
            var bounds = new RectangleF(8.5f, 8.5f, 240, 60);
            if (retained) DrawingTextLayout.Create(graphics, "Name 123", font, bounds.Size, format)
                    .Draw(graphics, Brushes.Black, bounds.Location);
            else graphics.DrawString("Name 123", font, Brushes.Black, bounds, format);
            var runs = graphics.DrawingContext.Commands.Where(c => c.Type == RenderCommandType.DrawGlyphRun).ToArray();
            Assert.NotEmpty(runs);
            foreach (var run in runs)
            {
                Assert.Equal(Matrix4x4.Identity, run.Transform);
                var positions = run.GlyphPositions!.Select(p => new Vector2(
                    MathF.Floor(p.X + run.Position.X + .5f),
                    MathF.Floor(p.Y + run.Position.Y + .5f))).ToArray();
                reference.DrawingContext.DrawGlyphRun(run.GlyphIndices!, positions, run.Font!, run.FontSize,
                    run.Brush!, Vector2.Zero, textRenderingMode: TextRenderingMode.Aliased,
                    textHintingMode: TextHintingMode.Auto, preferGlyphAtlas: true);
            }
        }
        int[] pixels = Pixels(actual);
        Assert.Equal(Pixels(expected), pixels);
        Assert.Contains(Color.Black.ToArgb(), pixels);
        Assert.All(pixels, p => Assert.True(p == Color.Black.ToArgb() || p == Color.White.ToArgb()));
    }

    private static int[] Pixels(Bitmap image)
    {
        int[] pixels = new int[image.Width * image.Height];
        var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < image.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * image.Width, image.Width);
        }
        finally { image.UnlockBits(data); }
        return pixels;
    }

    private static Font Font(FontStyle style = FontStyle.Regular) => new(
        new TtfFont(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "Inter-Regular.ttf"))),
        13, style, GraphicsUnit.Pixel);

    private static Graphics Target(DrawingContext context, float scale) => Graphics.FromProGpuDrawingContext(context,
        new RectangleF(0, 0, 300, 80), Matrix4x4.Identity, 96, 96, scale);

    private static StringFormat Format()
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
