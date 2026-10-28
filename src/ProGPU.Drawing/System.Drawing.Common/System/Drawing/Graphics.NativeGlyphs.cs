using System.Drawing.Text;
using System.Numerics;
using ProGPU.Backend.Native;
using ProGPU.Scene;
using ProGPU.SystemDrawing;
using ProGPU.Text;

namespace System.Drawing;

public partial class Graphics
{
    private readonly record struct NativeDrawingGlyphPaint(
        DrawingNativeGlyphCache.Use? Use, Rect InkBounds, TextRenderingMode Coverage);

    private NativeDrawingGlyphPaint[]? PrepareNativeGlyphPaint(PreparedDrawingGlyphRun[] runs,
        FontStyle style, float fontSize, ProGPU.Vector.Brush brush, Vector2 origin,
        Matrix4x4 transform, bool sourceGdiText)
    {
        // GDI font quality is independent. Do not infer either its smoothing
        // preference or a physical target from ordinary Graphics layout DPI.
        if (sourceGdiText || GlyphRasterDpiScale is not { } scale || _dpiX != _dpiY ||
            TextRenderingHint is not (TextRenderingHint.SingleBitPerPixelGridFit or TextRenderingHint.AntiAliasGridFit) ||
            (style & (FontStyle.Bold | FontStyle.Italic)) != 0 ||
            brush is not ProGPU.Vector.SolidColorBrush || !IsNativeGlyphTranslation(transform))
            return null;
        float physicalEm = fontSize * scale;
        if (!float.IsFinite(physicalEm) || physicalEm < 1f || physicalEm > ushort.MaxValue ||
            physicalEm != MathF.Truncate(physicalEm))
            return null;
        ushort ppem = (ushort)physicalEm;
        uint em26_6 = (uint)ppem * 64;
        NativeFontOutlinePolicy?[] policies = new NativeFontOutlinePolicy?[runs.Length];
        TextRenderingMode[] coverages = new TextRenderingMode[runs.Length];
        bool any = false;
        // Complete every original font-table admission before creating a native
        // generation or publishing even the first command of a mixed-font draw.
        for (int index = 0; index < runs.Length; index++)
        {
            TtfFont font = runs[index].Font;
            if (font.HasColorGlyphs || font.HasBitmapGlyphs ||
                !font.TryGetStaticRasterizationRecommendations(ppem, out FontRasterizationRange range))
                continue;
            if (TextRenderingHint == TextRenderingHint.SingleBitPerPixelGridFit)
            {
                policies[index] = NativeFontOutlinePolicy.Monochrome;
                coverages[index] = TextRenderingMode.Aliased;
            }
            else if ((range.Recommendations & FontRasterizationRecommendations.GridFit) != 0)
            {
                bool gray = (range.Recommendations & FontRasterizationRecommendations.Grayscale) != 0;
                policies[index] = gray ? NativeFontOutlinePolicy.Normal : NativeFontOutlinePolicy.Monochrome;
                coverages[index] = gray ? TextRenderingMode.Grayscale : TextRenderingMode.Aliased;
            }
            any |= policies[index].HasValue;
        }
        if (!any) return null;

        NativeDrawingGlyphPaint[] prepared = new NativeDrawingGlyphPaint[runs.Length];
        try
        {
            for (int index = 0; index < runs.Length; index++)
            {
                if (policies[index] is not { } policy) continue;
                PreparedDrawingGlyphRun run = runs[index];
                DrawingNativeGlyphCache.Use use = DrawingNativeGlyphCache.Acquire(run.Font,
                    run.GlyphIndices, run.GlyphPositions, em26_6, scale, policy);
                prepared[index] = new(use, default, coverages[index]);
                if (!DrawingContext.TryGetHintedGlyphInkBounds(use.Geometry, origin, out Rect bounds, out _))
                    throw new NotSupportedException("The original native glyph ink cannot be represented in this source frame.");
                prepared[index] = new(use, bounds, coverages[index]);
            }
            return prepared;
        }
        catch (Exception failure)
        {
            ReleaseNativeGlyphPaint(prepared, failure);
            throw;
        }
    }

    private static void ReleaseNativeGlyphPaint(NativeDrawingGlyphPaint[]? prepared, Exception? primary)
    {
        if (prepared is null) return;
        Exception? firstCleanup = null;
        foreach (var paint in prepared)
        {
            try { paint.Use?.Dispose(); }
            catch (Exception cleanup)
            {
                firstCleanup ??= cleanup;
                if (primary is not null)
                {
                    try { primary.Data["NativeGlyphPaintCleanupFailure"] = cleanup; }
                    catch { /* Retain the original preparation/recording error. */ }
                }
            }
        }
        if (primary is null && firstCleanup is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstCleanup).Throw();
    }

    private bool UsesIntegerGlyphRasterOrigin(TtfFont font, FontStyle style,
        Matrix4x4 transform, bool sourceGdiText)
        => !sourceGdiText && TextRenderingHint == TextRenderingHint.SingleBitPerPixel &&
            GlyphRasterDpiScale is > 0f && _dpiX == _dpiY &&
            (style & (FontStyle.Bold | FontStyle.Italic)) == 0 &&
            !font.HasCffOutlines && !font.HasColorGlyphs && !font.HasBitmapGlyphs &&
            IsNativeGlyphTranslation(transform);

    private static bool IsNativeGlyphTranslation(Matrix4x4 matrix)
        => matrix.M11 == 1f && matrix.M22 == 1f && matrix.M33 == 1f && matrix.M44 == 1f &&
            matrix.M12 == 0f && matrix.M13 == 0f && matrix.M14 == 0f &&
            matrix.M21 == 0f && matrix.M23 == 0f && matrix.M24 == 0f &&
            matrix.M31 == 0f && matrix.M32 == 0f && matrix.M34 == 0f && matrix.M43 == 0f &&
            float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42);
}
