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
    [InlineData(false)]
    [InlineData(true)]
    public void PortableUndoClipboardReplacementTogglesExactUtf16Text(bool multiline)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Multiline = multiline, Text = "A🙂B", CharacterCasing = CharacterCasing.Upper };
        editor.Select(3, -2);
        Clipboard.SetText("xy");
        editor.Paste();
        editor.Text.Should().Be("AXYB");
        editor.CanUndo.Should().BeTrue();
        editor.MaxLength = 1;
        int changes = 0;
        editor.TextChanged += (_, _) => changes++;
        editor.Undo();
        editor.Text.Should().Be("A🙂B", "undo must not apply current user input limits or casing");
        editor.SelectionStart.Should().Be(1);
        editor.SelectionLength.Should().Be(2);
        editor.CanUndo.Should().BeTrue();
        editor.Undo();
        editor.Text.Should().Be("AXYB");
        editor.SelectionStart.Should().Be(1);
        editor.SelectionLength.Should().Be(2);
        changes.Should().Be(2);
        editor.ClearUndo();
        editor.CanUndo.Should().BeFalse();
        editor.Undo();
        editor.Text.Should().Be("AXYB");
        changes.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableUndoCutRestoresOriginalSelectionWithoutChangingClipboard(bool multiline)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Multiline = multiline, Text = "A🙂B" };
        editor.Select(1, 2);
        editor.Cut();
        editor.Text.Should().Be("AB");
        Clipboard.SetText("external clipboard");
        editor.CanUndo.Should().BeTrue();
        editor.Undo();
        editor.Text.Should().Be("A🙂B");
        editor.SelectedText.Should().Be("🙂");
        Clipboard.GetText().Should().Be("external clipboard");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableUndoTypingCoalescesUntilExplicitCaretChange(bool multiline)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using TextBox editor = new() { Multiline = multiline, Text = "seed" };
        owner.Controls.Add(editor);
        owner.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.Select(4, 0);
        platform.SendInput(LibreInputEventKind.TextInput, text: "a🙂b");
        editor.Text.Should().Be("seeda🙂b");
        editor.CanUndo.Should().BeTrue();
        editor.Undo();
        editor.Text.Should().Be("seed");
        editor.Undo();
        editor.Text.Should().Be("seeda🙂b");
        editor.Select(0, 0);
        platform.SendInput(LibreInputEventKind.TextInput, text: "x");
        editor.Undo();
        editor.Text.Should().Be("seeda🙂b", "a caret move separates the next typing group");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableUndoDeletionRetainsAdjacentCrlfAndSurrogateGroups(bool backwards)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using TextBox editor = new() { Multiline = true, Text = "A🙂\r\nB" };
        owner.Controls.Add(editor);
        owner.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.Select(backwards ? 5 : 1, 0);
        LibreKey key = backwards ? LibreKey.Backspace : LibreKey.Delete;
        for (int index = 0; index < 2; index++)
        {
            platform.SendInput(LibreInputEventKind.KeyDown, key: key);
            platform.SendInput(LibreInputEventKind.KeyUp, key: key);
        }

        editor.Text.Should().Be("AB");
        editor.CanUndo.Should().BeTrue();
        editor.Undo();
        editor.Text.Should().Be("A🙂\r\nB");
        editor.SelectedText.Should().Be("🙂\r\n");
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("text")]
    [InlineData("selection")]
    [InlineData("recreate")]
    public void PortableUndoIsClearedByProgrammaticReplacementAndHandleLifetime(string operation)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using UndoEditor editor = new() { Text = "before" };
        editor.SelectAll();
        Clipboard.SetText("after");
        editor.Paste();
        editor.CanUndo.Should().BeTrue();
        switch (operation)
        {
            case "clear": editor.ClearUndo(); break;
            case "text": editor.Text = "programmatic"; break;
            case "selection": editor.SelectedText = "programmatic"; break;
            case "recreate": editor.Recreate(); break;
        }

        string current = editor.Text;
        editor.CanUndo.Should().BeFalse();
        editor.Undo();
        editor.Text.Should().Be(current);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PortableUndoShortcutHonorsReadOnlyAndShortcutsEnabled(bool readOnly, bool disabled)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using TextBox editor = new() { Text = "before" };
        owner.Controls.Add(editor);
        owner.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.SelectAll();
        Clipboard.SetText("after");
        editor.Paste();
        editor.ReadOnly = readOnly;
        editor.ShortcutsEnabled = !disabled;
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Z, modifiers: LibreInputModifiers.Control);
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.Z, modifiers: LibreInputModifiers.Control);
        editor.Text.Should().Be(readOnly || disabled ? "after" : "before");
    }

    [Fact]
    public void PortableUndoEmptyAndMaskedControlsKeepCanonicalHandleAndDispatchBehavior()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "seed" };
        editor.CanUndo.Should().BeFalse();
        editor.ClearUndo();
        editor.IsHandleCreated.Should().BeFalse();
        editor.Undo();
        editor.IsHandleCreated.Should().BeTrue();
        editor.Text.Should().Be("seed");
        editor.Modified.Should().BeFalse();
        using MaskedTextBox masked = new("000") { Text = "123" };
        TextBoxBase baseEditor = masked;
        baseEditor.Undo();
        baseEditor.CanUndo.Should().BeFalse();
        masked.Text.Should().Be("123");
    }

    private sealed class UndoEditor : TextBox
    {
        internal void Recreate() => RecreateHandle();
    }
}
