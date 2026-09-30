// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    // Source-seam fixtures only. These literal spans/hits/ranges were recorded
    // in the FxvEWdBQ stock EDIT receipt; this adapter is not a word classifier.
    [Fact]
    public void PortableEditWordBoundary_LeadingHitZeroRetainsOriginalContentRange()
        => CheckObservedSpaceWordDrag(0, 2, 9);

    [Fact]
    public void PortableEditWordBoundary_LeadingSpaceDragCanCollapseThenRestore()
        => CheckObservedSpaceWordDrag(1, 0, 2);

    [Fact]
    public void PortableEditWordBoundary_ExactBoundaryDownUsesStrictPreviousWord()
        => CheckObservedSpaceWordDrag(2, 0, 2);

    [Fact]
    public void PortableEditWordBoundary_InteriorDownAndReversalUseOriginalRange()
        => CheckObservedSpaceWordDrag(3, 2, 9);

    private static void CheckObservedSpaceWordDrag(int hit, int start, int end,
        [CallerMemberName] string method = "")
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "  alpha  beta  ", [0, 2, 9, 15], 2);
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, hit);
            input.Click(point, 1);
            int notifications = 0;
            editor.BeforeMouseDown = e =>
            {
                Assert.Equal(2, e.Clicks);
                AssertWordRange(editor, start, end);
                notifications++;
            };
            input.Down(point, 2);
            Assert.Equal(1, notifications);
            AssertWordRange(editor, start, end);
            input.Drag(ObservedWordPoint(probe, 13));
            AssertWordRange(editor, start, 15);
            input.Drag(ObservedWordPoint(probe, 0));
            AssertWordRange(editor, 2, end);
            input.Drag(point);
            AssertWordRange(editor, start, end);
            input.Drag(ObservedWordPoint(probe, 13));
            AssertWordRange(editor, start, 15);
            Assert.Equal(1, probe.BoundaryQueries);
        }, method);

    [Fact]
    public void PortableEditWordBoundary_TabAfterSpacesRetainsItsSeparateObservedSpan()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "one\t\ttwo \tthree ", [0, 5, 9, 10, 16]);
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, 10);
            input.Click(point, 1);
            input.Down(point, 2);
            AssertWordRange(editor, 9, 10);
            input.Drag(ObservedWordPoint(probe, 11));
            AssertWordRange(editor, 9, 16);
            input.Drag(point);
            AssertWordRange(editor, 9, 10);
        });

    [Fact]
    public void PortableEditWordBoundary_AsciiPunctuationStaysInItsObservedWord()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "alpha,beta.gamma! (tail) - end ", [0, 18, 25, 27, 31]);
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, 7);
            input.Click(point, 1);
            input.Down(point, 2);
            AssertWordRange(editor, 0, 18);
        });

    [Fact]
    public void PortableEditWordBoundary_ActualRowStartAdmitsItsCurrentBoundary()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "alpha\r\nbeta\r\n\r\ngamma ", [0, 5, 7, 11, 13, 15, 21], multiline: true);
            RetainedLayoutProbe layout = probe.Layouts.Last();
            Assert.Equal(7, layout.GetRowSourceStart(1));
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, 7);
            input.Click(point, 1);
            input.Down(point, 2);
            AssertWordRange(editor, 7, 11);
        });

    [Fact]
    public void PortableEditWordBoundary_LongWordIsNotClampedToTheRetainedRow()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            editor.Width = 100;
            PrepareObservedWords(editor, probe, "abcdefghijklmnopqrstuvwxyz0123456789 ", [0, 37], multiline: true);
            RetainedLayoutProbe layout = probe.Layouts.Last();
            Assert.True(layout.RowCount > 1);
            int rowStart = layout.GetRowSourceStart(1);
            Assert.InRange(rowStart, 1, 36);
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, rowStart);
            input.Click(point, 1);
            input.Down(point, 2);
            AssertWordRange(editor, 0, 37);
        });

    [Fact]
    public void PortableEditWordBoundary_LeftDragKeepsSignedAnchorForFollowingShiftKey()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "  alpha  beta  ", [0, 2, 9, 15], 2);
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, 10);
            input.Click(point, 1);
            input.Down(point, 2);
            input.Drag(ObservedWordPoint(probe, 3));
            AssertWordRange(editor, 2, 15);
            input.Up(ObservedWordPoint(probe, 3), 2);
            SendRetainedKey(platform, owner, LibreKey.Right, LibreInputModifiers.Shift);
            AssertWordRange(editor, 3, 15);
        });

    [Fact]
    public void PortableEditWordBoundary_SameRangeHandlerOverrideRetiresWordDrag()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "  alpha  beta  ", [0, 2, 9, 15], 2);
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, 3);
            input.Click(point, 1);
            int callbacks = 0;
            editor.MouseDown += (_, _) => { callbacks++; editor.Select(2, 7); };
            input.Down(point, 2);
            Assert.Equal(1, callbacks);
            input.Drag(ObservedWordPoint(probe, 13));
            AssertWordRange(editor, 2, 9);
            Assert.True(editor.Capture);
        });

    [Fact]
    public void PortableEditWordBoundary_MoveHandlerOverrideSurvivesReversal()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "  alpha  beta  ", [0, 2, 9, 15], 2);
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, 3);
            input.Click(point, 1);
            input.Down(point, 2);
            int callbacks = 0;
            editor.MouseMove += (_, _) =>
            {
                if (++callbacks != 1) return;
                AssertWordRange(editor, 2, 15);
                editor.Select(4, 1);
            };
            input.Drag(ObservedWordPoint(probe, 13));
            input.Drag(ObservedWordPoint(probe, 0));
            Assert.Equal(2, callbacks);
            AssertWordRange(editor, 4, 5);
        });

    [Fact]
    public void PortableEditWordBoundary_ReadOnlySelectionDoesNotEditText()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "  alpha  beta  ", [0, 2, 9, 15], 2);
            editor.ReadOnly = true;
            int edits = 0;
            editor.TextChanged += (_, _) => edits++;
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, 3);
            input.Click(point, 1);
            input.Down(point, 2);
            AssertWordRange(editor, 2, 9);
            input.Drag(ObservedWordPoint(probe, 13));
            AssertWordRange(editor, 2, 15);
            Assert.Equal(0, edits);
        });

    [Fact]
    public void PortableEditWordBoundary_ProviderWithoutCapabilityKeepsCharacterSelection()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            editor.Text = "alpha beta";
            editor.Record();
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = TextPointerCaretPoint(probe.Layouts.Last(), 3);
            input.Click(point, 1);
            input.Down(point, 2);
            AssertWordRange(editor, 3, 3);
            input.Drag(TextPointerCaretPoint(probe.Layouts.Last(), 6));
            AssertWordRange(editor, 3, 6);
        });
    }

    [Fact]
    public void PortableEditWordBoundary_DeclaredButMissingLayoutRejectsBeforeSelection()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            probe.OmitBoundaryCapability = true;
            PrepareObservedWords(editor, probe, "  alpha  beta  ", [0, 2, 9, 15], 2);
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, 3);
            input.Click(point, 1);
            Assert.Throws<InvalidOperationException>(() => input.Down(point, 2));
            AssertWordRange(editor, 3, 3);
            Assert.Equal(0, probe.BoundaryQueries);
        });

    [Fact]
    public void PortableEditWordBoundary_MissingEndRejectsAtomically()
        => CheckRejectedWordSnapshot([0, 2, 9], 2);

    [Fact]
    public void PortableEditWordBoundary_DuplicatePositionRejectsAtomically()
        => CheckRejectedWordSnapshot([0, 2, 2, 15], 2);

    [Fact]
    public void PortableEditWordBoundary_NonBoundaryLeadingContentRejectsAtomically()
        => CheckRejectedWordSnapshot([0, 2, 9, 15], 3);

    private static void CheckRejectedWordSnapshot(int[] positions, int leading,
        [CallerMemberName] string method = "")
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "  alpha  beta  ", positions, leading);
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, 3);
            input.Click(point, 1);
            int notifications = 0;
            editor.MouseDown += (_, _) => notifications++;
            Assert.Throws<InvalidOperationException>(() => input.Down(point, 2));
            AssertWordRange(editor, 3, 3);
            Assert.Equal(0, notifications);
            Assert.Equal(1, probe.BoundaryQueries);
        }, method);

    [Fact]
    public void PortableEditWordBoundary_BoundaryCallbackCancelRejectsOldDown()
        => CheckWordSnapshotReentry("cancel");

    [Fact]
    public void PortableEditWordBoundary_BoundaryCallbackTextReplacementRejectsOldDown()
        => CheckWordSnapshotReentry("text");

    [Fact]
    public void PortableEditWordBoundary_BoundaryCallbackNestedPressKeepsNewDrag()
        => CheckWordSnapshotReentry("nested");

    private static void CheckWordSnapshotReentry(string change, [CallerMemberName] string method = "")
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "  alpha  beta  ", [0, 2, 9, 15], 2);
            Point point = ObservedWordPoint(probe, 3);
            Point replacement = ObservedWordPoint(probe, 10);
            using PasswordWordPointer input = new(platform, owner, editor);
            input.Click(point, 1);
            int notifications = 0;
            editor.MouseDown += (_, _) => notifications++;
            probe.AfterBoundaryQuery = () =>
            {
                probe.AfterBoundaryQuery = null;
                if (change == "nested") input.Down(replacement, 0);
                else
                {
                    if (change == "cancel") input.Cancel();
                    else editor.Text = "replacement";
                    editor.Select(1, 2);
                }
            };
            input.Down(point, 2);
            Assert.Equal(change == "nested" ? 1 : 0, notifications);
            if (change == "nested")
            {
                input.Drag(ObservedWordPoint(probe, 13));
                AssertWordRange(editor, 10, 13);
            }
            else AssertWordRange(editor, 1, 3);
            Assert.Equal(1, probe.BoundaryQueries);
        }, method);

    [Fact]
    public void PortableEditWordBoundary_SelectionCallbackNestedPressSurvivesOldCommit()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "  alpha  beta  ", [0, 2, 9, 15], 2);
            Point point = ObservedWordPoint(probe, 3);
            Point replacement = ObservedWordPoint(probe, 10);
            using PasswordWordPointer input = new(platform, owner, editor);
            input.Click(point, 1);
            bool entered = false;
            int notifications = 0;
            editor.MouseDown += (_, _) => notifications++;
            editor.Invalidated += (_, _) =>
            {
                if (entered || editor.SelectionLength != 7) return;
                entered = true;
                input.Down(replacement, 0);
            };
            input.Down(point, 2);
            Assert.True(entered);
            Assert.Equal(1, notifications);
            input.Drag(ObservedWordPoint(probe, 13));
            AssertWordRange(editor, 10, 13);
        });

    [Fact]
    public void PortableEditWordBoundary_CaptureReplacementRetiresOldWordDrag()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "  alpha  beta  ", [0, 2, 9, 15], 2);
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, 3);
            input.Click(point, 1);
            input.Down(point, 2);
            editor.Capture = false;
            editor.Capture = true;
            editor.Select(4, 1);
            input.Drag(ObservedWordPoint(probe, 13));
            AssertWordRange(editor, 4, 5);
        });

    [Fact]
    public void PortableEditWordBoundary_NegativeRowCountRejectsBeforeSelection()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "  alpha  beta  ", [0, 2, 9, 15], 2);
            using PasswordWordPointer input = new(platform, owner, editor);
            Point point = ObservedWordPoint(probe, 3);
            input.Click(point, 1);
            probe.ReportedRowCount = -1;
            Assert.Throws<InvalidOperationException>(() => input.Down(point, 2));
            AssertWordRange(editor, 3, 3);
        });

    [Fact]
    public void PortableEditWordBoundary_MoveHitCallbackNestedPressRetiresOldNotification()
        => RunObservedWordEditor((platform, owner, editor, probe) =>
        {
            PrepareObservedWords(editor, probe, "  alpha  beta  ", [0, 2, 9, 15], 2);
            Point point = ObservedWordPoint(probe, 3);
            Point replacement = ObservedWordPoint(probe, 10);
            Point end = ObservedWordPoint(probe, 13);
            using PasswordWordPointer input = new(platform, owner, editor);
            input.Click(point, 1);
            input.Down(point, 2);
            RetainedLayoutProbe layout = probe.Layouts.Last();
            int downs = 0, moves = 0;
            editor.MouseDown += (_, _) => downs++;
            editor.MouseMove += (_, _) => moves++;
            layout.AfterHitTest = () =>
            {
                layout.AfterHitTest = null;
                input.Down(replacement, 0);
            };
            input.Drag(end);
            Assert.Equal(1, downs);
            Assert.Equal(0, moves);
            AssertWordRange(editor, 10, 10);
            input.Drag(end);
            Assert.Equal(1, moves);
            AssertWordRange(editor, 10, 13);
        });

    private static void RunObservedWordEditor(Action<HeadlessPlatform, Form, RetainedEditor, ObservedEditWordRendererProbe> action,
        [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        const string marker = "LIBREWINFORMS_TEST_RETAINED_TEXT";
        string? previous = Environment.GetEnvironmentVariable(marker);
        Environment.SetEnvironmentVariable(marker, "edit-words");
        try
        {
            RunRetainedEditor((platform, owner, editor, probe)
                => action(platform, owner, editor, Assert.IsType<ObservedEditWordRendererProbe>(probe)));
        }
        finally { Environment.SetEnvironmentVariable(marker, previous); }
    }

    private static void PrepareObservedWords(RetainedEditor editor, ObservedEditWordRendererProbe probe,
        string text, int[] positions, int leading = 0, bool multiline = false)
    {
        probe.Boundaries = new(positions, leading);
        editor.Multiline = multiline;
        editor.Text = text;
        editor.Select(0, 0);
        Assert.True(editor.Focus());
        editor.Record();
        Assert.Equal(text, probe.LastText);
    }

    private static Point ObservedWordPoint(RetainedTextRendererProbe probe, int hit)
    {
        RetainedLayoutProbe layout = probe.Layouts.Last();
        Point point = TextPointerCaretPoint(layout, hit);
        Assert.Equal(hit, layout.HitTest(new(point.X - 1, point.Y - 1)).TextPosition);
        return point;
    }

    private static void AssertWordRange(TextBox editor, int start, int end)
    {
        Assert.Equal(start, editor.SelectionStart);
        Assert.Equal(end - start, editor.SelectionLength);
    }

    private sealed class ObservedEditWordRendererProbe : RetainedTextRendererProbe, ILibreEditWordBoundaryService
    {
        internal LibreEditWordBoundaries Boundaries { get; set; } = new(new[] { 0 }, 0);
        internal bool OmitBoundaryCapability { get; set; }
        internal int? ReportedRowCount { get; set; }
        internal int BoundaryQueries { get; set; }
        internal Action? AfterBoundaryQuery { get; set; }

        protected override RetainedLayoutProbe CreateLayoutProbe(ILibreTextLayout layout, string text)
            => OmitBoundaryCapability ? base.CreateLayoutProbe(layout, text)
                : new ObservedEditWordLayoutProbe(layout, this,
                    new(Boundaries.Positions.ToArray(), Boundaries.LeadingContentStart));
    }

    private sealed class ObservedEditWordLayoutProbe(ILibreTextLayout layout,
        ObservedEditWordRendererProbe owner, LibreEditWordBoundaries boundaries)
        : RetainedLayoutProbe(layout), ILibreEditWordBoundaryLayout
    {
        int ILibreTextSourceGeometry.RowCount => owner.ReportedRowCount ?? RowCount;

        public LibreEditWordBoundaries GetWordBoundaries()
        {
            owner.BoundaryQueries++;
            owner.AfterBoundaryQuery?.Invoke();
            return boundaries;
        }
    }
}
