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
    public void PortableRetainedRowNavigationKeepsPreferredXAcrossShortRows()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            editor.Text = "aaaa\r\nx\r\naaaa";
            editor.Select(3, 0);
            editor.Record();
            int layouts = probe.Layouts.Count;
            int changes = 0;
            editor.TextChanged += (_, _) => changes++;
            foreach (bool readOnly in new[] { false, true })
            {
                editor.ReadOnly = readOnly;
                SendRetainedKey(platform, owner, LibreKey.Down, LibreInputModifiers.None);
                editor.SelectionStart.Should().Be(7);
                editor.Record();
                SendRetainedKey(platform, owner, LibreKey.Down, LibreInputModifiers.None);
                editor.SelectionStart.Should().Be(12);
                SendRetainedKey(platform, owner, LibreKey.Up, LibreInputModifiers.None);
                editor.SelectionStart.Should().Be(7);
                SendRetainedKey(platform, owner, LibreKey.Up, LibreInputModifiers.None);
                editor.SelectionStart.Should().Be(3);
            }

            editor.SelectionLength.Should().Be(0);
            editor.Modified.Should().BeFalse();
            changes.Should().Be(0);
            probe.Layouts.Count.Should().Be(layouts);
        });
    }

    [Fact]
    public void PortableRetainedRowNavigationPreservesShiftAnchorAcrossEmptyRows()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, _) =>
        {
            editor.Text = "aaaa\r\n\r\naaaa";
            editor.Select(3, 0);
            SendRetainedKey(platform, owner, LibreKey.Down, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(3);
            editor.SelectionLength.Should().Be(3);
            SendRetainedKey(platform, owner, LibreKey.Down, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(3);
            editor.SelectionLength.Should().Be(8);
            SendRetainedKey(platform, owner, LibreKey.Up, LibreInputModifiers.Shift);
            editor.SelectionLength.Should().Be(3);
            SendRetainedKey(platform, owner, LibreKey.Up, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(3);
            editor.SelectionLength.Should().Be(0);
        });
    }

    [Fact]
    public void PortableRetainedRowNavigationHomeEndUsesWrappedRowsAndAffinity()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            editor.Text = "aa aa aa";
            editor.Record();
            float rowWidth = probe.Layouts.Last().GetCaret(3).Position.X;
            editor.ClientSize = new Size((int)MathF.Ceiling(rowWidth) + 1, 100);
            editor.Select(4, 0);
            SendRetainedKey(platform, owner, LibreKey.Home, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(3);
            SendRetainedKey(platform, owner, LibreKey.End, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(6);
            SendRetainedKey(platform, owner, LibreKey.Home, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(3);
            editor.SelectionLength.Should().Be(3);
            SendRetainedKey(platform, owner, LibreKey.End, LibreInputModifiers.Shift);
            editor.SelectionStart.Should().Be(6);
            editor.SelectionLength.Should().Be(0);
            SendRetainedKey(platform, owner, LibreKey.Home, LibreInputModifiers.Control);
            editor.SelectionStart.Should().Be(0);
            SendRetainedKey(platform, owner, LibreKey.End, LibreInputModifiers.Control);
            editor.SelectionStart.Should().Be(8);
        });
    }

    [Fact]
    public void PortableRetainedRowNavigationExternalAndHorizontalSelectionsResetPreferredX()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, _) =>
        {
            editor.Text = "aaaa\r\nx\r\naaaa";
            editor.Select(3, 0);
            SendRetainedKey(platform, owner, LibreKey.Down, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(7);
            SendRetainedKey(platform, owner, LibreKey.Left, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(6);
            SendRetainedKey(platform, owner, LibreKey.Down, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(9);
            editor.Select(0, 0);
            SendRetainedKey(platform, owner, LibreKey.Down, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(6);
        });
    }

    [Fact]
    public void PortableRetainedRowNavigationHonorsPublicKeyHandlersAndDisposal()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, probe) =>
        {
            editor.Text = "abc\r\ndef";
            editor.Select(1, 0);
            editor.Record();
            KeyEventHandler handled = (_, e) => e.Handled = true;
            editor.KeyDown += handled;
            foreach (LibreKey key in new[] { LibreKey.Down, LibreKey.Home, LibreKey.End })
                SendRetainedKey(platform, owner, key, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(1);
            editor.KeyDown -= handled;
            editor.KeyDown += (_, _) => editor.Dispose();
            int layouts = probe.Layouts.Count;
            SendRetainedKey(platform, owner, LibreKey.Down, LibreInputModifiers.None);
            editor.IsDisposed.Should().BeTrue();
            probe.Layouts.Count.Should().Be(layouts);
        });
    }

    [Fact]
    public void PortableRetainedRowNavigationUsesBlankAndTrailingRowBoundaries()
    {
        if (RunDpiCaseInNewProcess(retainedTextLayout: true)) return;
        RunRetainedEditor((platform, owner, editor, _) =>
        {
            editor.Text = "ab\r\n\r\n";
            editor.Select(1, 0);
            SendRetainedKey(platform, owner, LibreKey.Down, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(4);
            SendRetainedKey(platform, owner, LibreKey.Home, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(4);
            SendRetainedKey(platform, owner, LibreKey.End, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(4);
            SendRetainedKey(platform, owner, LibreKey.Down, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(6);
            SendRetainedKey(platform, owner, LibreKey.Down, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(6);
            SendRetainedKey(platform, owner, LibreKey.Home, LibreInputModifiers.None);
            editor.SelectionStart.Should().Be(6);
        });
    }
}
