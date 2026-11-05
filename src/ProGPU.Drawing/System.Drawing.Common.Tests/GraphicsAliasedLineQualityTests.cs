using System.Drawing.Drawing2D;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ProGPU.Scene;
using Xunit;

namespace System.Drawing.Tests;

public sealed class GraphicsAliasedLineQualityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(42)]
    [InlineData(44)]
    // Independently matching wider/scaled controls must remain unchanged.
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(29)]
    [InlineData(41)]
    public void OriginalCompleteLineFramesMatch(int index)
    {
        using Stream manifest = Resource("reference.json");
        using JsonDocument document = JsonDocument.Parse(manifest);
        JsonElement record = document.RootElement.GetProperty("cases")[index];
        JsonElement input = record.GetProperty("input");
        JsonElement image = record.GetProperty("image");
        using Stream encoded = Resource(image.GetProperty("name").GetString()!);
        using var bytes = new MemoryStream();
        encoded.CopyTo(bytes);
        Assert.Equal(image.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        bytes.Position = 0;
        using var expected = new Bitmap(bytes);
        using var actual = new Bitmap(32, 32);
        actual.SetResolution(96, 96);
        using (Graphics graphics = Graphics.FromImage(actual))
        using (var pen = new Pen(Color.FromArgb(100, 100, 100), Value("Width")))
        using (var transform = new Matrix(Value("Sx"), 0, 0, Value("Sy"), Value("Tx"), Value("Ty")))
        {
            graphics.Clear(Color.White);
            if (input.GetProperty("Clip").GetBoolean()) graphics.SetClip(new Rectangle(9, 6, 11, 5));
            graphics.Transform = transform;
            graphics.SmoothingMode = Enum.Parse<SmoothingMode>(record.GetProperty("requested").GetString()!);
            graphics.PixelOffsetMode = Enum.Parse<PixelOffsetMode>(record.GetProperty("requestedPixelOffset").GetString()!);
            graphics.DrawLine(pen, Value("X1"), Value("Y1"), Value("X2"), Value("Y2"));
        }

        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
            Assert.True(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(),
                $"Original case{index}, pixel({x},{y}): expected{expected.GetPixel(x, y)}, actual{actual.GetPixel(x, y)}");

        float Value(string name) => input.GetProperty(name).GetSingle();
    }

    [Theory]
    [InlineData("antialias")]
    [InlineData("half-offset")]
    [InlineData("wide")]
    [InlineData("dash")]
    [InlineData("round-cap")]
    [InlineData("translucent")]
    [InlineData("scale")]
    [InlineData("fractional-translation")]
    [InlineData("other-dpi")]
    [InlineData("diagonal")]
    [InlineData("fractional-endpoint")]
    [InlineData("empty")]
    [InlineData("unrepresentable-end")]
    [InlineData("unrepresentable-translation")]
    [InlineData("non-solid")]
    [InlineData("end-cap")]
    [InlineData("inset")]
    public void UnqualifiedFramesKeepTheirOriginalStroke(string kind)
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context,
            new RectangleF(0, 0, 32, 32), Matrix4x4.Identity,
            dpiX: kind == "other-dpi" ? 120 : 96, dpiY: kind == "other-dpi" ? 120 : 96);
        using var pen = new Pen(Color.FromArgb(100, 100, 100));
        if (kind == "antialias") graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (kind == "half-offset") graphics.PixelOffsetMode = PixelOffsetMode.Half;
        if (kind == "wide") pen.Width = 2;
        if (kind == "dash") pen.DashStyle = DashStyle.Dash;
        if (kind == "round-cap") pen.StartCap = LineCap.Round;
        if (kind == "end-cap") pen.EndCap = LineCap.Square;
        if (kind == "inset") pen.Alignment = PenAlignment.Inset;
        if (kind == "non-solid")
        {
            using var brush = new LinearGradientBrush(new Point(0, 0), new Point(32, 0), Color.Black, Color.White);
            pen.Brush = brush;
        }
        if (kind == "translucent") pen.Color = Color.FromArgb(128, 100, 100, 100);
        if (kind == "scale") graphics.ScaleTransform(2, 3);
        if (kind == "fractional-translation") graphics.TranslateTransform(.25f, .75f);
        if (kind == "unrepresentable-translation") graphics.TranslateTransform(33_554_432, 0);
        float x1 = kind == "fractional-endpoint" ? 4.25f : 4;
        float x2 = kind == "empty" ? x1 : kind == "unrepresentable-end" ? 33_554_432 : 25;
        float y2 = kind == "diagonal" ? 19 : 8;
        graphics.DrawLine(pen, x1, 8, x2, y2);
        RenderCommand line = Assert.Single(context.Commands);
        Assert.Equal(RenderCommandType.DrawLine, line.Type);
        Assert.Equal(new Vector2(x1, 8), line.Position);
        Assert.Equal(new Vector2(x2, y2), line.Position2);
    }

    private static Stream Resource(string name) => typeof(GraphicsAliasedLineQualityTests).Assembly
        .GetManifestResourceStream("System.Drawing.Common.Tests.Fixtures.OriginalLines." + name)
        ?? throw new InvalidOperationException("Missing original line fixture: " + name);
}
