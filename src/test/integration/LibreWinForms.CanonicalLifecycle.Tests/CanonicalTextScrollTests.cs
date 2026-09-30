// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void PortableScrollToCaretUpdatesSingleLinePointerViewportBeforePainting()
        => CheckScrollPointerViewport(multiline: false);

    [Fact]
    public void PortableScrollToCaretUpdatesMultilinePointerViewportBeforePainting()
        => CheckScrollPointerViewport(multiline: true);

    private static void CheckScrollPointerViewport(bool multiline, [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Multiline = multiline;
            editor.WordWrap = false;
            editor.Width = 50;
            editor.Text = new string('W', 30);
            editor.Select(0, 0);
            editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            int generations = probe.Layouts.Count;
            editor.Select(editor.TextLength, 0);
            editor.ScrollToCaret();
            // No paint between the public call and hit testing: deferring the
            // viewport change until OnPaint would select near the source start.
            editor.Press(new Point(editor.ClientSize.Width - 2, 1 + (int)(layout.GetCaret(0).Height / 2)));
            editor.SelectionStart.Should().Be(editor.TextLength);
            editor.SelectionLength.Should().Be(0);
            editor.Capture = false;
            editor.Record();
            layout.LastOrigin.X.Should().BeLessThan(0);
            probe.Layouts.Count.Should().Be(generations);
        });
    }

    [Fact]
    public void PortableScrollToCaretRetainsBlankTrailingRowsAndExactSourceSelection()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Text = "first\r\n\r\nlast\r\n";
            editor.Height = 28;
            editor.Select(0, 0);
            editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            editor.Select(editor.TextLength, 0);
            editor.ScrollToCaret();
            editor.Record();
            LibreTextCaret caret = layout.GetCaret(editor.TextLength);
            float y = caret.Position.Y + layout.LastOrigin.Y;
            y.Should().BeGreaterThanOrEqualTo(1);
            (y + Math.Min(caret.Height, editor.ClientSize.Height - 2)).Should().BeApproximately(editor.ClientSize.Height - 1, .001f);
            editor.Text.Should().Be("first\r\n\r\nlast\r\n");
            editor.SelectionStart.Should().Be(editor.TextLength);
            editor.Select(0, 0);
            editor.ScrollToCaret();
            editor.Record();
            layout.LastOrigin.Y.Should().Be(1);
        });
    }

    [Fact]
    public void PortableScrollToCaretAlreadyVisibleDoesNotInvalidateOrRebuild()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Text = "visible";
            editor.Select(2, 0);
            editor.Record();
            int generations = probe.Layouts.Count, invalidations = 0, edits = 0;
            editor.Invalidated += (_, _) => invalidations++;
            editor.TextChanged += (_, _) => edits++;
            editor.ScrollToCaret();
            editor.ScrollToCaret();
            invalidations.Should().Be(0);
            edits.Should().Be(0);
            probe.Layouts.Count.Should().Be(generations);
            editor.SelectionStart.Should().Be(2);
        });
    }

    [Fact]
    public void PortableScrollToCaretUsesSignedActiveEndWithoutChangingSelection()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.WordWrap = false;
            editor.Width = 50;
            editor.Text = new string('W', 30);
            editor.Select(editor.TextLength, 0);
            editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            layout.LastOrigin.X.Should().BeLessThan(0);
            editor.Select(editor.TextLength, -editor.TextLength);
            editor.ScrollToCaret();
            editor.Record();
            layout.LastOrigin.X.Should().Be(1);
            editor.SelectionStart.Should().Be(0);
            editor.SelectionLength.Should().Be(editor.TextLength);
        });
    }

    [Fact]
    public void PortableScrollToCaretWorksReadOnlyWithoutEditing()
        => CheckReadOnlyScroll(password: false);

    [Fact]
    public void PortableScrollToCaretWorksReadOnlyWithoutExposingPasswordSource()
        => CheckReadOnlyScroll(password: true);

    private static void CheckReadOnlyScroll(bool password, [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Multiline = false;
            editor.Width = 50;
            editor.Text = new string('W', 30);
            editor.ReadOnly = true;
            if (password) editor.PasswordChar = '*';
            editor.Select(editor.TextLength, 0);
            int edits = 0;
            editor.TextChanged += (_, _) => edits++;
            editor.ScrollToCaret();
            editor.Record();
            probe.LastText.Should().Be(password ? new string('*', 30) : editor.Text);
            probe.Layouts.Last().LastOrigin.X.Should().BeLessThan(0);
            editor.SelectionStart.Should().Be(editor.TextLength);
            edits.Should().Be(0);
        });
    }

    [Fact]
    public void PortableScrollToCaretUnfocusedControlKeepsItsViewport()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, owner, editor, probe) =>
        {
            editor.WordWrap = false;
            editor.Width = 50;
            editor.Text = new string('W', 30);
            editor.Select(0, 0);
            editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            using TextBox other = new() { Bounds = new Rectangle(8, 100, 50, 20) };
            owner.Controls.Add(other);
            other.Focus().Should().BeTrue();
            editor.Focused.Should().BeFalse();
            editor.Select(editor.TextLength, 0);
            editor.ScrollToCaret();
            editor.Record();
            layout.LastOrigin.Should().Be(new PointF(1, 1));
        });
    }

    [Fact]
    public void PortableScrollToCaretDefersWithoutCreatingAHandleAndPreservesVirtualDispatch()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using ScrollMessageProbe editor = new() { Text = "source" };
        editor.ScrollToCaret();
        editor.IsHandleCreated.Should().BeFalse();
        editor.ScrollMessages.Should().Be(0);
        _ = editor.Handle;
        editor.ScrollMessages.Should().Be(1);
        editor.ScrollToCaret();
        editor.ScrollMessages.Should().Be(2);
    }

    [Fact]
    public void PortableScrollToCaretEmptyTextKeepsOriginalNoMessageBehavior()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using ScrollMessageProbe editor = new();
        _ = editor.Handle;
        editor.ScrollToCaret();
        editor.ScrollMessages.Should().Be(0);
    }

    [Fact]
    public void PortableScrollToCaretMaskedTypedApiRemainsItsOriginalNoOp()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using MaskedTextBox editor = new("00") { Text = "12" };
        editor.ScrollToCaret();
        editor.IsHandleCreated.Should().BeFalse();
        editor.Text.Should().Be("12");
    }

    [Fact]
    public void PortableScrollToCaretAllowsDisposalInVirtualMessageHandler()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using ScrollMessageProbe editor = new() { Text = "source", DisposeOnScroll = true };
        _ = editor.Handle;
        editor.ScrollToCaret();
        editor.ScrollMessages.Should().Be(1);
        editor.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void PortableScrollToCaretSupportsRightAlignedOverflow()
        => CheckAlignedScroll(HorizontalAlignment.Right, rtl: false);

    [Fact]
    public void PortableScrollToCaretSupportsCenteredOverflow()
        => CheckAlignedScroll(HorizontalAlignment.Center, rtl: false);

    [Fact]
    public void PortableScrollToCaretSupportsLeftAlignedRtlOverflow()
        => CheckAlignedScroll(HorizontalAlignment.Left, rtl: true);

    [Fact]
    public void PortableScrollToCaretSupportsRightAlignedRtlOverflow()
        => CheckAlignedScroll(HorizontalAlignment.Right, rtl: true);

    private static void CheckAlignedScroll(HorizontalAlignment alignment, bool rtl, [CallerMemberName] string method = "")
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true, method: method)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.Multiline = false;
            editor.Width = 50;
            editor.TextAlign = alignment;
            editor.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            editor.Text = rtl ? "שלום עולם שלום עולם שלום עולם" : new string('W', 30);
            foreach (int position in new[] { 0, editor.TextLength, 0 })
            {
                editor.Select(position, 0);
                editor.ScrollToCaret();
                editor.Record();
                RetainedLayoutProbe layout = probe.Layouts.Last();
                LibreTextCaret caret = layout.GetCaret(position);
                float x = caret.Position.X + layout.LastOrigin.X;
                x.Should().BeGreaterThanOrEqualTo(1);
                (x + Math.Min(Math.Max(1, SystemInformation.CaretWidth), editor.ClientSize.Width - 2))
                    .Should().BeLessThanOrEqualTo(editor.ClientSize.Width - 1);
                editor.SelectionStart.Should().Be(position);
            }
        });
    }

    [Fact]
    public void PortableScrollToCaretAcceptsAnEmptyViewport()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, _) =>
        {
            editor.Text = "source";
            editor.ClientSize = Size.Empty;
            editor.Select(editor.TextLength, 0);
            editor.ScrollToCaret();
            editor.Text.Should().Be("source");
            editor.SelectionStart.Should().Be(6);
        });
    }

    [Fact]
    public void PortableScrollToCaretInvalidationCanDisposeTheControl()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.WordWrap = false;
            editor.Width = 50;
            editor.Text = new string('W', 30);
            editor.Select(0, 0);
            editor.Record();
            RetainedLayoutProbe layout = probe.Layouts.Last();
            editor.Select(editor.TextLength, 0);
            editor.Invalidated += (_, _) => editor.Dispose();
            editor.ScrollToCaret();
            editor.IsDisposed.Should().BeTrue();
            layout.Disposed.Should().BeTrue();
        });
    }

    [Fact]
    public void PortableScrollToCaretInvalidationKeepsAReplacementLayout()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((_, _, editor, probe) =>
        {
            editor.WordWrap = false;
            editor.Width = 50;
            editor.Text = new string('W', 30);
            editor.Select(0, 0);
            editor.Record();
            RetainedLayoutProbe original = probe.Layouts.Last();
            editor.Select(editor.TextLength, 0);
            bool replaced = false;
            editor.Invalidated += (_, _) =>
            {
                if (replaced) return;
                replaced = true;
                editor.Text = "new";
                editor.Select(0, 0);
                editor.Record();
            };
            editor.ScrollToCaret();
            replaced.Should().BeTrue();
            original.Disposed.Should().BeTrue();
            probe.Layouts.Last().Disposed.Should().BeFalse();
            probe.Layouts.Last().LastOrigin.Should().Be(new PointF(1, 1));
            editor.Text.Should().Be("new");
            editor.SelectionStart.Should().Be(0);
        });
    }

    private sealed class ScrollMessageProbe : TextBox
    {
        internal int ScrollMessages { get; private set; }
        internal bool DisposeOnScroll { get; init; }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x00B7) // EM_SCROLLCARET
            {
                message.WParam.Should().Be(0);
                message.LParam.Should().Be(0);
                ScrollMessages++;
                if (DisposeOnScroll) Dispose();
                return;
            }

            base.WndProc(ref message);
        }
    }
}
