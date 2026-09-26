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
    public void PortableClipboardCopyCutPasteRetainUtf16SelectionAndNotifications()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "A🙂B" };
        editor.Select(1, 2);
        int changed = 0;
        int modified = 0;
        int characters = 0;
        editor.TextChanged += (_, _) => changed++;
        editor.ModifiedChanged += (_, _) => modified++;
        editor.KeyPress += (_, _) => characters++;

        editor.Copy();
        editor.IsHandleCreated.Should().BeTrue();
        Clipboard.GetText().Should().Be("🙂");
        editor.Text.Should().Be("A🙂B");
        editor.SelectionStart.Should().Be(1);
        editor.SelectionLength.Should().Be(2);
        changed.Should().Be(0);
        modified.Should().Be(0);

        editor.Cut();
        Clipboard.GetText().Should().Be("🙂");
        editor.Text.Should().Be("AB");
        editor.SelectionStart.Should().Be(1);
        editor.SelectionLength.Should().Be(0);
        editor.Modified.Should().BeTrue();
        changed.Should().Be(1);
        modified.Should().Be(1);

        editor.Paste();
        editor.Text.Should().Be("A🙂B");
        editor.SelectionStart.Should().Be(3);
        changed.Should().Be(2);
        modified.Should().Be(1);
        characters.Should().Be(0, "clipboard replacement is not a synthesized key press");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableClipboardReverseSelectionRetainsOrderedUtf16Range(bool createHandle)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "A🙂B" };
        if (createHandle)
        {
            _ = editor.Handle;
        }

        editor.Select(3, -2);
        editor.SelectionStart.Should().Be(1);
        editor.SelectionLength.Should().Be(2);
        editor.SelectedText.Should().Be("🙂");
        editor.IsHandleCreated.Should().Be(createHandle);
        editor.Copy();
        Clipboard.GetText().Should().Be("🙂");
        editor.Text.Should().Be("A🙂B");

        Clipboard.SetText("x");
        editor.Paste();
        editor.Text.Should().Be("AxB");
        editor.SelectionStart.Should().Be(2);
        editor.SelectionLength.Should().Be(0);

        editor.Text = "A🙂B";
        editor.Select(3, -2);
        editor.Cut();
        Clipboard.GetText().Should().Be("🙂");
        editor.Text.Should().Be("AB");
        editor.SelectionStart.Should().Be(1);
        editor.SelectionLength.Should().Be(0);
    }

    [Fact]
    public void PortableClipboardEmptySelectionAndReadOnlyRetainSourceAndClipboard()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "seed" };
        Clipboard.SetText("previous");
        int changed = 0;
        editor.TextChanged += (_, _) => changed++;
        editor.Copy();
        editor.Cut();
        editor.IsHandleCreated.Should().BeTrue();
        Clipboard.GetText().Should().Be("previous");
        editor.Text.Should().Be("seed");

        editor.ReadOnly = true;
        editor.SelectAll();
        editor.Copy();
        Clipboard.GetText().Should().Be("seed");
        Clipboard.SetText("replacement");
        editor.Cut();
        editor.Paste();
        Clipboard.GetText().Should().Be("replacement");
        editor.Text.Should().Be("seed");
        editor.SelectionLength.Should().Be(4);
        editor.Modified.Should().BeFalse();
        changed.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PortableClipboardPasswordCannotCopyOrCut(bool systemPassword, bool createHandleFirst)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "secret" };
        if (createHandleFirst)
        {
            _ = editor.Handle;
        }

        if (systemPassword)
        {
            editor.UseSystemPasswordChar = true;
        }
        else
        {
            editor.PasswordChar = '*';
        }

        editor.SelectAll();
        Clipboard.SetText("public");
        editor.Copy();
        editor.Cut();
        editor.IsHandleCreated.Should().BeTrue();
        Clipboard.GetText().Should().Be("public");
        editor.Text.Should().Be("secret");
        editor.Modified.Should().BeFalse();
        editor.Paste();
        editor.Text.Should().Be("public");
        editor.Modified.Should().BeTrue();
    }

    [Fact]
    public void PortableClipboardFailedWriteDoesNotDeleteSelection()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "selected" };
        editor.SelectAll();
        Clipboard.SetText("previous");
        platform.RejectClipboardWrites = true;
        int changed = 0;
        editor.TextChanged += (_, _) => changed++;
        editor.Cut();
        editor.Text.Should().Be("selected");
        editor.SelectedText.Should().Be("selected");
        Clipboard.GetText().Should().Be("previous");
        editor.Modified.Should().BeFalse();
        changed.Should().Be(0);
    }

    [Fact]
    public void PortableClipboardAbsentAndEmptyTextAreDifferent()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "selected" };
        editor.SelectAll();
        DataObject data = new();
        data.SetData("not-text", autoConvert: false, "other");
        Clipboard.SetDataObject(data);
        editor.Paste();
        editor.SelectedText.Should().Be("selected");

        data = new();
        data.SetData(DataFormats.UnicodeText, autoConvert: false, string.Empty);
        Clipboard.SetDataObject(data);
        editor.Paste();
        editor.Text.Should().BeEmpty();
        editor.Modified.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PortableClipboardUsesCanonicalTextFormatConversion(int formatIndex)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new();
        string format = formatIndex switch
        {
            0 => DataFormats.UnicodeText,
            1 => DataFormats.Text,
            _ => DataFormats.StringFormat,
        };
        DataObject data = new();
        data.SetData(format, autoConvert: true, "café🙂");
        Clipboard.SetDataObject(data);
        editor.Paste();
        editor.Text.Should().Be("café🙂");
    }

    [Theory]
    [InlineData(false, "a\r\nb", "a")]
    [InlineData(false, "a\nb", "a")]
    [InlineData(false, "a\0b", "a")]
    [InlineData(true, "a\r\nb", "a\r\nb")]
    [InlineData(true, "a\0b", "a")]
    public void PortableClipboardHonorsTextControlLineAndTerminatorPolicy(bool multiline, string clipboard, string expected)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Multiline = multiline };
        Clipboard.SetText(clipboard);
        editor.Paste();
        editor.Text.Should().Be(expected);
        editor.SelectionStart.Should().Be(expected.Length);
    }

    [Fact]
    public void PortableClipboardHonorsCasingAndUserMaxLength()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using TextBox editor = new() { Text = "abCD", MaxLength = 5, CharacterCasing = CharacterCasing.Upper };
        editor.Select(1, 2);
        Clipboard.SetText("xyzw");
        editor.Paste();
        editor.Text.Should().Be("aXYZD");
        editor.SelectionStart.Should().Be(4);
        editor.SelectedText = "programmatic";
        editor.Text.Should().Be("aXYZprogrammaticD", "programmatic selection replacement is not limited user input");
    }

    [Theory]
    [InlineData(LibreKey.C, LibreInputModifiers.Control, "seed", "seed")]
    [InlineData(LibreKey.X, LibreInputModifiers.Control, "", "seed")]
    [InlineData(LibreKey.Delete, LibreInputModifiers.Shift, "", "seed")]
    [InlineData(LibreKey.V, LibreInputModifiers.Control, "replacement", "replacement")]
    [InlineData(LibreKey.Insert, LibreInputModifiers.Shift, "replacement", "replacement")]
    public void PortableClipboardShortcutsUseActualFocusedControl(LibreKey key, LibreInputModifiers modifiers, string expectedText, string expectedClipboard)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using TextBox editor = new() { Text = "seed" };
        form.Controls.Add(editor);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.SelectAll();
        Clipboard.SetText("replacement");
        platform.SendInput(LibreInputEventKind.KeyDown, modifiers: modifiers, key: key);
        platform.SendInput(LibreInputEventKind.KeyUp, modifiers: modifiers, key: key);
        editor.Text.Should().Be(expectedText);
        Clipboard.GetText().Should().Be(expectedClipboard);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableClipboardShortcutsRespectDisableAndParentCommand(bool parentHandles)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using ClipboardCommandForm form = new() { ShowIcon = false, HandlePaste = parentHandles };
        using TextBox editor = new() { Text = "seed", ShortcutsEnabled = parentHandles };
        form.Controls.Add(editor);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.SelectAll();
        Clipboard.SetText("replacement");
        platform.SendInput(LibreInputEventKind.KeyDown, modifiers: LibreInputModifiers.Control, key: LibreKey.V);
        platform.SendInput(LibreInputEventKind.KeyUp, modifiers: LibreInputModifiers.Control, key: LibreKey.V);
        editor.Text.Should().Be("seed");
        form.PasteCommands.Should().Be(1);
        editor.Paste();
        editor.Text.Should().Be("replacement", "ShortcutsEnabled and parent command handling do not disable the public method");
    }

    [Fact]
    public void PortableClipboardMaskedTextBoxRetainsOriginalProviderHandlers()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using MaskedTextBox editor = new("00-00") { Text = "1234", CutCopyMaskFormat = MaskFormat.IncludeLiterals };
        editor.SelectAll();
        editor.Copy();
        Clipboard.GetText().Should().Be("12-34");
        Clipboard.SetText("5678");
        editor.Paste();
        editor.Text.Should().Be("56-78");
        editor.MaskCompleted.Should().BeTrue();
    }

    [Fact]
    public void PortableClipboardDataGridViewPasteCommitsThroughExistingEditor()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using DataGridView grid = new() { Size = new Size(240, 120) };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name" });
        grid.Rows.Add("seed");
        form.Controls.Add(grid);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.Focus().Should().BeTrue();
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.F2);
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.F2);
        TextBox editor = grid.EditingControl.Should().BeOfType<DataGridViewTextBoxEditingControl>().Subject;
        editor.SelectAll();
        Clipboard.SetText("Alice🙂");
        platform.SendInput(LibreInputEventKind.KeyDown, modifiers: LibreInputModifiers.Control, key: LibreKey.V);
        platform.SendInput(LibreInputEventKind.KeyUp, modifiers: LibreInputModifiers.Control, key: LibreKey.V);
        editor.Text.Should().Be("Alice🙂");
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Enter);
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.Enter);
        grid.Rows[0].Cells[0].Value.Should().Be("Alice🙂");
        grid.IsCurrentCellInEditMode.Should().BeFalse();
    }

    [Fact]
    public void PortableClipboardDoesNotFlattenRichTextIntoPlainText()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using RichTextBox editor = new();
        Action copy = editor.Copy;
        Action cut = editor.Cut;
        Action paste = editor.Paste;
        copy.Should().Throw<PlatformNotSupportedException>();
        cut.Should().Throw<PlatformNotSupportedException>();
        paste.Should().Throw<PlatformNotSupportedException>();
        editor.IsHandleCreated.Should().BeFalse();
    }

    private sealed class ClipboardCommandForm : Form
    {
        internal bool HandlePaste { get; init; }
        internal int PasteCommands { get; private set; }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.V))
            {
                PasteCommands++;
                if (HandlePaste)
                {
                    return true;
                }
            }

            return base.ProcessCmdKey(ref message, keyData);
        }
    }
}
