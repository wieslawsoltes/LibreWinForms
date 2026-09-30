// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData("a\r\nb", 3, true, "ab", 1)]
    [InlineData("a\r\nb", 1, false, "ab", 1)]
    [InlineData("a\r\n\r\nb", 5, true, "a\r\nb", 3)]
    [InlineData("a\r\n\r\nb", 1, false, "a\r\nb", 1)]
    [InlineData("\r\n", 2, true, "", 0)]
    [InlineData("\r\n", 0, false, "", 0)]
    [InlineData("a\rb", 2, true, "ab", 1)]
    [InlineData("a\rb", 1, false, "ab", 1)]
    [InlineData("a\nb", 2, true, "ab", 1)]
    [InlineData("a\nb", 1, false, "ab", 1)]
    public void PortableNewlineDeletionRemovesOneCompleteBreak(
        string source, int caret, bool backwards, string expected, int expectedCaret)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using TextBox editor = new() { Multiline = true, Text = source };
        owner.Controls.Add(editor);
        owner.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.Select(caret, 0);
        int changes = 0;
        int modified = 0;
        int presses = 0;
        editor.TextChanged += (_, _) => changes++;
        editor.ModifiedChanged += (_, _) => modified++;
        editor.KeyPress += (_, _) => presses++;

        LibreKey key = backwards ? LibreKey.Backspace : LibreKey.Delete;
        platform.SendInput(LibreInputEventKind.KeyDown, key: key);
        if (backwards) platform.SendInput(LibreInputEventKind.TextInput, text: "\b");
        platform.SendInput(LibreInputEventKind.KeyUp, key: key);

        editor.Text.Should().Be(expected);
        editor.SelectionStart.Should().Be(expectedCaret);
        editor.SelectionLength.Should().Be(0);
        editor.Modified.Should().BeTrue();
        changes.Should().Be(1);
        modified.Should().Be(1);
        presses.Should().Be(backwards ? 1 : 0, "the duplicate host Backspace must remain suppressed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableNewlineDeletionKeepsExplicitUtf16Selection(bool backwards)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using TextBox editor = new() { Multiline = true, Text = "a\r\nb" };
        owner.Controls.Add(editor);
        owner.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.Select(1, 1);
        LibreKey key = backwards ? LibreKey.Backspace : LibreKey.Delete;
        platform.SendInput(LibreInputEventKind.KeyDown, key: key);
        platform.SendInput(LibreInputEventKind.KeyUp, key: key);
        editor.Text.Should().Be("a\nb", "an explicit source selection is not expanded by collapsed-caret deletion");
        editor.SelectionStart.Should().Be(1);
        editor.SelectionLength.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableNewlineDeletionHonorsReadOnlyAndSuppression(bool backwards)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using TextBox editor = new() { Multiline = true, Text = "a\r\nb", ReadOnly = true };
        owner.Controls.Add(editor);
        owner.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        int caret = backwards ? 3 : 1;
        editor.Select(caret, 0);
        int changes = 0;
        editor.TextChanged += (_, _) => changes++;
        LibreKey key = backwards ? LibreKey.Backspace : LibreKey.Delete;
        platform.SendInput(LibreInputEventKind.KeyDown, key: key);
        platform.SendInput(LibreInputEventKind.KeyUp, key: key);
        editor.Text.Should().Be("a\r\nb");
        editor.SelectionStart.Should().Be(caret);

        editor.ReadOnly = false;
        editor.KeyDown += (_, e) => e.SuppressKeyPress = true;
        platform.SendInput(LibreInputEventKind.KeyDown, key: key);
        platform.SendInput(LibreInputEventKind.KeyUp, key: key);
        editor.Text.Should().Be("a\r\nb");
        editor.SelectionStart.Should().Be(caret);
        changes.Should().Be(0);
        editor.Modified.Should().BeFalse();
    }
}
