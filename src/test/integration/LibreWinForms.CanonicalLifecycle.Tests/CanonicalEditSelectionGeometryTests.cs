// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableEditSelectionGeometry_OriginalEmojiRangesShareOneRetainedCluster()
        => CheckOriginalEditSelectionGeometry("emoji", "a\u2603\uFE0Fb\U0001F469\u200D\U0001F4BBc ",
            [0, 4, 7, 9, 11], 4, 7, 9);

    [Fact]
    public void PortableEditSelectionGeometry_ContextJoinerRangesShareOneRetainedCluster()
        => CheckOriginalEditSelectionGeometry("joiner", "x\U0001F469\u200D\U0001F4BBy ",
            [0, 1, 4, 6, 8], 1, 4, 6);

    [Fact]
    public void PortableEditSelectionGeometry_OriginalCombiningRangesShareOneRetainedCluster()
        => CheckOriginalEditSelectionGeometry("combining", "go A\U0001F600 e\u0301 fin ",
            [0, 3, 4, 7, 10, 14], 7, 8, 9);

    private static void CheckOriginalEditSelectionGeometry(string family, string source, int[] boundaries,
        int start, int interior, int end, [CallerMemberName] string method = "")
        => RunObservedWordEditor((_, _, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, source, boundaries);
            RetainedLayoutProbe generation = probe.Layouts.Last();
            RectangleF[] first = generation.GetEditSelectionRectangles(start, interior - start).ToArray();
            RectangleF[] second = generation.GetEditSelectionRectangles(interior, end - interior).ToArray();
            RectangleF[] whole = generation.GetEditSelectionRectangles(start, end - start).ToArray();
            var interiorCaret = generation.GetEditCaret(interior);
            var endCaret = generation.GetEditCaret(end);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                family,
                source,
                sourceFont = editor.Font.Name,
                sourceFontSize = editor.Font.Size,
                sourceFontUnit = editor.Font.Unit.ToString(),
                realizedFontSize = probe.LastFontSize,
                realizedFontUnit = probe.LastFontUnit.ToString(),
                first,
                second,
                whole,
                interiorCaret,
                endCaret
            }));
            Assert.NotEmpty(whole);
            Assert.Equal(whole, first);
            Assert.Equal(whole, second);
            Assert.Equal(interior, interiorCaret.TextPosition);
            Assert.Equal(end, endCaret.TextPosition);
            Assert.Equal(endCaret.Position, interiorCaret.Position);
            Assert.Equal(endCaret.Height, interiorCaret.Height);
            Assert.Equal(generation.GetEditSourcePositionPoint(end), generation.GetEditSourcePositionPoint(interior));
            Assert.Equal(generation.GetSourcePositionPoint(start), generation.GetEditSourcePositionPoint(start));
            Assert.Equal(generation.GetSourcePositionPoint(end), generation.GetEditSourcePositionPoint(end));
            Assert.Equal(editor.GetPositionFromCharIndex(end), editor.GetPositionFromCharIndex(interior));
            foreach ((int firstPosition, int lastPosition) in new[]
            {
                (start, interior), (interior, end), (start, end)
            })
            {
                editor.Select(firstPosition, lastPosition - firstPosition);
                editor.Record();
                editor.ScrollToCaret();
                AssertWordRange(editor, firstPosition, lastPosition);
                Assert.Same(generation, probe.Layouts.Last());
            }

            // These are original-generation rectangle/caret relationships from
            // the Windows receipt, not a claim of matching font metrics, native
            // BGRX pixels or desktop input on this device-free source host.
        }, method);
}
