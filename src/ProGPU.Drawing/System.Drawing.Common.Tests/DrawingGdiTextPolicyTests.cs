using System.Drawing;
using System.Drawing.Text;
using ProGPU.Scene;
using ProGPU.SystemDrawing;
using Xunit;

namespace System.Drawing.Tests;

public sealed class DrawingGdiTextPolicyTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void InvalidRequestRejectsBeforeSourceAccess(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawingGdiTextPolicy((DrawingGdiFontQuality)value, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => DrawingGdiText.CreateLayout(null!, null!, null!, default,
            null!, null, (DrawingGdiFontQuality)value));
    }

    [Fact]
    public void RawSettingsAndRequestedIdentityRemainIndependent()
    {
        var settings = new DrawingGdiSystemSettings(false, -42, int.MaxValue);
        var policy = new DrawingGdiTextPolicy(DrawingGdiFontQuality.Proof, settings);
        Assert.Equal(settings, policy.SystemSettings);
        Assert.Null(policy.SystemSettings?.FontSmoothingOrientation);
        Assert.NotEqual(policy, new DrawingGdiTextPolicy(DrawingGdiFontQuality.Draft, settings));
        Assert.NotEqual(policy, new DrawingGdiTextPolicy(DrawingGdiFontQuality.Proof, null));
    }

    [Fact]
    public void RetainedRequestSurvivesCallerMutationAndFontDisposalWithoutLayoutChanges()
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(FontFamily.GenericSansSerif, 13);
        using var format = new StringFormat(StringFormat.GenericTypographic);
        format.SetDigitSubstitution(0, StringDigitSubstitute.None);
        var captured = DrawingGdiText.CreateLayout(graphics, "Name 123", font, new SizeF(200, 60),
            format, null, DrawingGdiFontQuality.Proof);
        // Existing explicit default literals must keep binding to the original
        // optional metrics parameter, without ambiguous new overloads.
        var legacy = DrawingGdiText.CreateLayout(graphics, "Name 123", font, new SizeF(200, 60), format, default);
        Assert.Equal(legacy.ContentSize, captured.ContentSize);
        Assert.Equal(legacy.GetCaretStop(3), captured.GetCaretStop(3));
        Assert.Null(legacy.RequestedGdiQuality);
        font.Dispose();
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var brush = new SolidBrush(Color.Black);
        captured.Draw(graphics, brush, PointF.Empty, new DrawingGdiSystemSettings(true, 2, 1700));
        captured.Draw(graphics, brush, PointF.Empty, null);
        Assert.Equal(DrawingGdiFontQuality.Proof, captured.RequestedGdiQuality);
        Assert.Equal(TextRenderingHint.ClearTypeGridFit, graphics.TextRenderingHint);
        Assert.Contains(context.Commands, c => c.Type == RenderCommandType.DrawGlyphRun);
        Assert.All(context.Commands.Where(c => c.Type == RenderCommandType.DrawGlyphRun),
            c => Assert.Equal(TextRenderingMode.Grayscale, c.TextRenderingMode));
        Assert.Throws<InvalidOperationException>(() => legacy.Draw(graphics, brush, PointF.Empty, null));
    }

    [Fact]
    public void LegacyDefaultLiteralDrawKeepsItsOriginalMetricsOverload()
    {
        var context = new DrawingContext();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(context);
        using Font font = new(FontFamily.GenericSansSerif, 13);
        using var format = new StringFormat(StringFormat.GenericTypographic);
        DrawingGdiText.Draw(graphics, "Name", font, Brushes.Black, new RectangleF(0, 0, 200, 60), format, default);
        Assert.Contains(context.Commands, command => command.Type == RenderCommandType.DrawGlyphRun);
    }
}
