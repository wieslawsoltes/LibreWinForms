// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableTextGeometryEmptyMultilineRetainsOriginalResults()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Multiline = true };
        editor.IsHandleCreated.Should().BeFalse();
        editor.GetLineFromCharIndex(0).Should().Be(0);
        editor.IsHandleCreated.Should().BeTrue();
        editor.GetFirstCharIndexFromLine(0).Should().Be(0);
        editor.GetFirstCharIndexFromLine(1).Should().Be(-1);
        editor.GetFirstCharIndexOfCurrentLine().Should().Be(0);
        editor.GetCharIndexFromPosition(Point.Empty).Should().Be(0);
        editor.GetCharFromPosition(Point.Empty).Should().Be('\0');
    }

    [Fact]
    public void PortableTextGeometrySingleLineRetainsZeroLineResults()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "plain" };
        foreach (int index in new[] { -1, 0, 1, 50, int.MaxValue })
            editor.GetLineFromCharIndex(index).Should().Be(0);
        editor.GetFirstCharIndexFromLine(0).Should().Be(0);
        editor.GetFirstCharIndexFromLine(1).Should().Be(0);
        editor.GetFirstCharIndexOfCurrentLine().Should().Be(0);
    }

    [Fact]
    public void PortableTextGeometryInvalidArgumentsDoNotCreateAHandle()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "abc", Multiline = true };
        foreach (int index in new[] { -1, 3, int.MaxValue })
            editor.GetPositionFromCharIndex(index).Should().Be(Point.Empty);
        Action invalid = () => editor.GetFirstCharIndexFromLine(-1);
        invalid.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("lineNumber");
        editor.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void PortableTextGeometryPreservesVirtualMessagesAndPackedCoordinates()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using GeometryMessageEditor editor = new() { Text = "abcdef", Multiline = true };
        editor.GetCharIndexFromPosition(new(-7, 9)).Should().Be(2);
        editor.LastMessage.Should().Be(0xD7);
        editor.LastLParam.Should().Be((nint)0x0009FFF9);
        editor.GetPositionFromCharIndex(2).Should().Be(new Point(-3, -5));
        editor.LastWParam.Should().Be(2);
        editor.GetLineFromCharIndex(-1).Should().Be(7);
        editor.LastWParam.Should().Be(-1);
        editor.GetFirstCharIndexFromLine(3).Should().Be(4);
        editor.LastWParam.Should().Be(3);
        editor.GetFirstCharIndexOfCurrentLine().Should().Be(4);
        editor.LastWParam.Should().Be(-1);
        editor.IsHandleCreated.Should().BeTrue();
    }

    [Fact]
    public void PortableTextGeometryHardBreaksKeepEveryOriginalSourceIndex()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, _) =>
        {
            editor.Text = "a\r\n\r\nb\n";
            int[] starts = [0, 3, 5, 7];
            int[] rows = [0, 0, 0, 1, 1, 2, 2, 3];
            for (int row = 0; row < starts.Length; row++)
                editor.GetFirstCharIndexFromLine(row).Should().Be(starts[row]);
            for (int index = 0; index < rows.Length; index++)
                editor.GetLineFromCharIndex(index).Should().Be(rows[index]);
            editor.GetFirstCharIndexFromLine(4).Should().Be(-1);
            editor.GetLineFromCharIndex(int.MaxValue).Should().Be(3);
            editor.GetPositionFromCharIndex(1).Should().Be(editor.GetPositionFromCharIndex(2));
            editor.GetPositionFromCharIndex(3).Should().Be(editor.GetPositionFromCharIndex(4));
            editor.Text.Should().Be("a\r\n\r\nb\n");
        });
    }

    [Fact]
    public void PortableTextGeometryWrapBoundaryKeepsSourceRowAndCaretAffinityDistinct()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            editor.Width = 55;
            editor.Text = "one two three four five six";
            editor.Select(0, 0);
            editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            layout.RowCount.Should().BeGreaterThan(1);
            int boundary = layout.GetRowSourceStart(1);
            editor.GetFirstCharIndexFromLine(1).Should().Be(boundary);
            editor.GetLineFromCharIndex(boundary).Should().Be(1);
            SendRetainedKey(platform, owner, LibreKey.End, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(boundary);
            editor.GetLineFromCharIndex(-1).Should().Be(0);
            editor.GetFirstCharIndexOfCurrentLine().Should().Be(0);
            editor.GetLineFromCharIndex(boundary).Should().Be(1);
        });
    }

    [Fact]
    public void PortableTextGeometrySelectionBeginningAndSignedCaretRemainDistinct()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, _) =>
        {
            editor.Text = "a\r\nb\r\nc";
            editor.Select(0, editor.TextLength);
            editor.GetLineFromCharIndex(-1).Should().Be(0);
            editor.GetFirstCharIndexOfCurrentLine().Should().Be(6);
            editor.Select(editor.TextLength, -editor.TextLength);
            editor.GetLineFromCharIndex(-1).Should().Be(0);
            editor.GetFirstCharIndexOfCurrentLine().Should().Be(0);
            editor.SelectionStart.Should().Be(0);
            editor.SelectionLength.Should().Be(editor.TextLength);
        });
    }

    [Fact]
    public void PortableTextGeometryQueriesReuseDrawingDpiWithoutMutation()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Text = "alpha\r\nbeta";
            editor.Select(2, 0);
            editor.Record(144);
            RetainedLayoutProbe layout = probe.Layouts.Last();
            int count = probe.Layouts.Count, edits = 0, invalidations = 0;
            editor.TextChanged += (_, _) => edits++;
            editor.Invalidated += (_, _) => invalidations++;
            for (int index = 0; index < editor.TextLength; index++)
            {
                PointF point = layout.GetSourcePositionPoint(index);
                Point expected = Point.Truncate(new(point.X + layout.LastOrigin.X, point.Y + layout.LastOrigin.Y));
                editor.GetPositionFromCharIndex(index).Should().Be(expected);
                editor.GetLineFromCharIndex(index).Should().Be(layout.GetRowIndexFromTextPosition(index));
            }

            editor.GetCharIndexFromPosition(new(1, 1)).Should().BeGreaterThanOrEqualTo(0);
            probe.Layouts.Count.Should().Be(count);
            invalidations.Should().Be(0);
            edits.Should().Be(0);
            editor.SelectionStart.Should().Be(2);
            editor.Focused.Should().BeTrue();
        });
    }

    [Fact]
    public void PortableTextGeometryClusterInteriorsUseRealLeadingEdges()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, _) =>
        {
            editor.Text = "a\u0301\U0001F600b";
            editor.GetPositionFromCharIndex(1).Should().Be(editor.GetPositionFromCharIndex(0));
            editor.GetPositionFromCharIndex(3).Should().Be(editor.GetPositionFromCharIndex(2));
        });
    }

    [Fact]
    public void PortableTextGeometryPasswordQueriesNeverExposeSecretToProvider()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Multiline = false;
            editor.PasswordChar = '*';
            editor.Text = "secret";
            Point point = editor.GetPositionFromCharIndex(2);
            editor.GetCharIndexFromPosition(point).Should().Be(2);
            editor.GetCharFromPosition(point).Should().Be('c');
            probe.LastText.Should().Be("******");
        });
    }

    [Fact]
    public void PortableTextGeometryUsesThePublishedViewportBeforePainting()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.WordWrap = false;
            editor.Width = 60;
            editor.Text = new string('W', 30);
            editor.Select(0, 0);
            editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            editor.Select(editor.TextLength, 0);
            editor.ScrollToCaret();
            Point last = editor.GetPositionFromCharIndex(editor.TextLength - 1);
            last.X.Should().BeInRange(0, editor.ClientSize.Width - 1);
            editor.GetCharIndexFromPosition(last).Should().Be(editor.TextLength - 1);
            editor.GetPositionFromCharIndex(0).X.Should().BeLessThan(0);
            probe.Layouts.Last().Should().BeSameAs(layout);
        });
    }

    [Fact]
    public void PortableTextGeometryRtlAndAlignmentUseCapturedLeadingEdges()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Text = "abc אבג";
            editor.RightToLeft = RightToLeft.Yes;
            editor.TextAlign = HorizontalAlignment.Right;
            editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            for (int index = 0; index < editor.TextLength; index++)
            {
                PointF point = layout.GetSourcePositionPoint(index);
                editor.GetPositionFromCharIndex(index).Should().Be(Point.Truncate(
                    new PointF(point.X + layout.LastOrigin.X, point.Y + layout.LastOrigin.Y)));
            }
        });
    }

    [Fact]
    public void PortableTextGeometryOutsideClientRetainsPublicLastCharacterClamp()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "abc", Multiline = true };
        editor.GetCharIndexFromPosition(new(-1, -1)).Should().Be(2);
        editor.GetCharFromPosition(new(-1, -1)).Should().Be('c');
    }

    [Fact]
    public void PortableTextGeometryMissingProviderFailsExplicitly()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "abc", Multiline = true };
        Action query = () => editor.GetPositionFromCharIndex(1);
        query.Should().Throw<PlatformNotSupportedException>();
    }

    [Fact]
    public void PortableTextGeometryFarRightHitStaysOnTheActualShortRow()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.WordWrap = false;
            editor.Text = "a\r\nWWWWWWWWWWWWWWWWWWWW";
            editor.Select(0, 0);
            editor.Record();
            float height = probe.Layouts.Last().GetCaret(0).Height;
            editor.GetCharIndexFromPosition(new(100, (int)(height / 2))).Should().Be(1,
                "a point beyond the short first row belongs to its delimiter, not a longer adjacent row");
        });
    }

    private sealed class GeometryMessageEditor : TextBox
    {
        internal int LastMessage { get; private set; }
        internal nint LastWParam { get; private set; }
        internal nint LastLParam { get; private set; }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg is 0xD7 or 0xD6 or 0xC9 or 0xBB)
            {
                LastMessage = message.Msg;
                LastWParam = message.WParam;
                LastLParam = message.LParam;
                message.Result = message.Msg switch
                {
                    0xD7 => 0x00030002,
                    0xD6 => unchecked((int)0xFFFBFFFD),
                    0xC9 => 7,
                    _ => 4,
                };
                return;
            }

            base.WndProc(ref message);
        }
    }
}
