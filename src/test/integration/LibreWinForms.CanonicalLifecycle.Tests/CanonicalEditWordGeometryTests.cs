// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableEditWordBoundary_OriginalEmojiInteriorAnchorSurvivesSourceDispatchAndPaint()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            // Original Win11 EDIT inventory, not a modern grapheme inventory.
            PrepareObservedWords(editor, probe, "a\u2603\uFE0Fb\U0001F469\u200D\U0001F4BBc ", [0, 4, 7, 9, 11]);
            using PasswordWordPointer input = new(platform, owner, editor);
            var point = ObservedWordPoint(probe, 9);
            input.Click(point, 1);
            input.Down(point, 2);
            AssertWordRange(editor, 7, 9);
            var generation = probe.Layouts.Last();
            editor.Record();
            editor.ScrollToCaret();
            AssertWordRange(editor, 7, 9);
            Assert.Same(generation, probe.Layouts.Last());
            Assert.Equal(1, probe.BoundaryQueries);
            // Whole-cluster selection rectangles are NOT asserted to be the
            // native EDIT partial-cluster ink. That geometry is unqualified.
        });

    [Fact]
    public void PortableEditWordBoundary_InteriorActiveEndpointKeepsOriginalIndexThroughPaintAndScroll()
        => RunObservedWordEditor((_, _, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "a\u2603\uFE0Fb\U0001F469\u200D\U0001F4BBc ", [0, 4, 7, 9, 11]);
            editor.Select(4, 3);
            var generation = probe.Layouts.Last();
            Assert.Equal(7, generation.GetCaret(7).TextPosition);
            editor.Record();
            AssertWordRange(editor, 4, 7);
            editor.ScrollToCaret();
            AssertWordRange(editor, 4, 7);
            Assert.Same(generation, probe.Layouts.Last());
        });

    [Fact]
    public void PortableEditWordBoundary_ContextJoinerActiveEndpointKeepsOriginalSourceIndex()
        => RunObservedWordEditor((_, _, editor, probe) =>
        {
            // Server2025 contextual receipt is kept distinct from the original
            // Win11 inventory. This is a source/lifetime control, not a new
            // classifier or native geometry parity assertion.
            PrepareObservedWords(editor, probe, "x\U0001F469\u200D\U0001F4BBy ", [0, 1, 4, 6, 8]);
            editor.Select(1, 3);
            Assert.Equal(4, probe.Layouts.Last().GetCaret(4).TextPosition);
            editor.Record();
            AssertWordRange(editor, 1, 4);
            editor.ScrollToCaret();
            AssertWordRange(editor, 1, 4);
        });

    [Fact]
    public void PortableEditWordBoundary_SubstitutedCaretRejectsWithoutRewritingOriginalEmojiEndpoint()
        => CheckSubstitutedWordCaret("a\u2603\uFE0Fb\U0001F469\u200D\U0001F4BBc ", [0, 4, 7, 9, 11], 4, 7, 4);

    [Fact]
    public void PortableEditWordBoundary_SubstitutedCaretRejectsWithoutRewritingContextJoinerEndpoint()
        => CheckSubstitutedWordCaret("x\U0001F469\u200D\U0001F4BBy ", [0, 1, 4, 6, 8], 1, 4, 1);

    private static void CheckSubstitutedWordCaret(string source, int[] boundaries, int start, int end,
        int substitute, [System.Runtime.CompilerServices.CallerMemberName] string method = "")
        => RunObservedWordEditor((_, _, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, source, boundaries);
            editor.Select(start, end - start);
            var generation = probe.Layouts.Last();
            // Explicit provider-fault control, not an assertion that this font
            // generation actually lacks the original source stop.
            probe.SubstituteCaretPosition = substitute;
            Assert.Throws<NotSupportedException>(() => editor.Record());
            AssertWordRange(editor, start, end);
            Assert.Throws<NotSupportedException>(() => editor.ScrollToCaret());
            AssertWordRange(editor, start, end);
            Assert.Same(generation, probe.Layouts.Last());
        }, method);

    [Fact]
    public void PortableEditWordBoundary_UnfocusedFrameDoesNotInventAnInteriorCaret()
        => RunObservedWordEditor((_, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "a\u2603\uFE0Fb\U0001F469\u200D\U0001F4BBc ", [0, 4, 7, 9, 11]);
            editor.HideSelection = false;
            editor.Select(4, 3);
            using Button other = new();
            owner.Controls.Add(other);
            Assert.True(other.Focus());
            Assert.False(editor.Focused);
            editor.Record();
            AssertWordRange(editor, 4, 7);
            // No claim about partial-cluster selected ink or native caret
            // placement follows from preserving these source indices.
        });
}
