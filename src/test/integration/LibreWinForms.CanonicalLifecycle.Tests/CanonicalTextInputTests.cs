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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultDataGridViewEditsCommitsAndCancelsThroughPortableInput(bool useF2)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(634, 446), ShowIcon = false };
        using DataGridView grid = new() { Bounds = new Rectangle(26, 119, 577, 299) };
        // The ordinary Name column and default EditMode in dotnet/samples'
        // CSWinFormDataGridView/CustomDataGridViewColumn are not masked editors.
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", Width = 120 });
        grid.Rows.Add("seed");
        form.Controls.Add(grid);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        DataGridViewCell cell = grid.Rows[0].Cells[0];
        grid.CurrentCell = cell;
        grid.Focus().Should().BeTrue();
        grid.EditMode.Should().Be(DataGridViewEditMode.EditOnKeystrokeOrF2);

        LibreKey key = useF2 ? LibreKey.F2 : LibreKey.A;
        platform.SendInput(LibreInputEventKind.KeyDown, key: key);
        grid.IsCurrentCellInEditMode.Should().BeTrue();
        DataGridViewTextBoxEditingControl editor = grid.EditingControl
            .Should().BeOfType<DataGridViewTextBoxEditingControl>().Subject;
        editor.Focused.Should().BeTrue();
        editor.Parent.Should().BeSameAs(grid.EditingPanel);
        platform.SendInput(LibreInputEventKind.TextInput, text: "A🙂");
        platform.SendInput(LibreInputEventKind.KeyUp, key: key);
        string expected = useF2 ? "seedA🙂" : "A🙂";
        editor.Text.Should().Be(expected);
        editor.SelectionStart.Should().Be(expected.Length);
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Enter);
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.Enter);
        cell.Value.Should().Be(expected);
        grid.IsCurrentCellInEditMode.Should().BeFalse();

        grid.CurrentCell = cell;
        grid.Focus().Should().BeTrue();
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.F2);
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.F2);
        ((TextBox)grid.EditingControl!).SelectAll();
        platform.SendInput(LibreInputEventKind.TextInput, text: "discard");
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Escape);
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.Escape);
        cell.Value.Should().Be(expected);
        grid.IsCurrentCellInEditMode.Should().BeFalse();
    }

    [Fact]
    public void DefaultDataGridViewNewRowAcceptsPortableTyping()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using DataGridView grid = new() { Size = new Size(240, 120) };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name" });
        form.Controls.Add(grid);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        grid.CurrentCell = grid.Rows[grid.NewRowIndex].Cells[0];
        grid.Focus().Should().BeTrue();
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.A);
        platform.SendInput(LibreInputEventKind.TextInput, text: "Alice");
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.A);
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Enter);
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.Enter);
        grid.Rows[0].Cells[0].Value.Should().Be("Alice");
        grid.Rows[0].IsNewRow.Should().BeFalse();
        grid.Rows[grid.NewRowIndex].IsNewRow.Should().BeTrue();
    }

    [Fact]
    public void PortableTextInputRetainsUtf16SelectionAndCanonicalNotifications()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using TextBox editor = new() { Text = "A🙂B" };
        form.Controls.Add(editor);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.Select(1, 2);
        int changed = 0;
        int modified = 0;
        List<char> characters = [];
        editor.TextChanged += (_, _) => changed++;
        editor.ModifiedChanged += (_, _) => modified++;
        editor.KeyPress += (_, e) => characters.Add(e.KeyChar);

        platform.SendInput(LibreInputEventKind.TextInput, text: "é");
        editor.Text.Should().Be("AéB");
        editor.SelectionStart.Should().Be(2);
        editor.SelectionLength.Should().Be(0);
        editor.Modified.Should().BeTrue();
        changed.Should().Be(1);
        modified.Should().Be(1);
        characters.Should().Equal('é');

        editor.Select(1, 1);
        platform.SendInput(LibreInputEventKind.TextInput, text: "🙂");
        editor.Text.Should().Be("A🙂B");
        editor.SelectionStart.Should().Be(3);
        editor.TextLength.Should().Be(4);
        characters.Should().Equal('é', '\ud83d', '\ude42');
        modified.Should().Be(1);

        editor.Select(1, 2);
        editor.SelectedText = "xy";
        editor.Text.Should().Be("AxyB");
        editor.SelectionStart.Should().Be(3);
        editor.Modified.Should().BeFalse();
        platform.SendInput(LibreInputEventKind.TextInput, text: "!");
        editor.Modified.Should().BeTrue();
        editor.Text = "reset";
        editor.Modified.Should().BeFalse();
    }

    [Fact]
    public void PortableTextInputHonorsHandledChangedAndFilteredCharacters()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using TextBox editor = new() { Text = "seed" };
        form.Controls.Add(editor);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.SelectAll();
        int changed = 0;
        editor.TextChanged += (_, _) => changed++;
        KeyPressEventHandler reject = (_, e) => e.Handled = true;
        editor.KeyPress += reject;
        platform.SendInput(LibreInputEventKind.TextInput, text: "a");
        editor.Text.Should().Be("seed");
        editor.SelectionLength.Should().Be(4);
        changed.Should().Be(0);
        editor.KeyPress -= reject;

        PortableCharacterFilter filter = new();
        Application.AddMessageFilter(filter);
        try
        {
            platform.SendInput(LibreInputEventKind.TextInput, text: "a");
            editor.Text.Should().Be("seed");
            filter.Count.Should().Be(1);
            changed.Should().Be(0);
        }
        finally
        {
            Application.RemoveMessageFilter(filter);
        }

        editor.KeyPress += (_, e) => e.KeyChar = 'z';
        platform.SendInput(LibreInputEventKind.TextInput, text: "a");
        editor.Text.Should().Be("z");
        changed.Should().Be(1);
    }

    [Fact]
    public void PortableTextInputHonorsReadOnlyMaxLengthCasingAndSuppression()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using TextBox editor = new() { Text = "seed", ReadOnly = true, MaxLength = 3, CharacterCasing = CharacterCasing.Upper };
        form.Controls.Add(editor);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.SelectAll();
        platform.SendInput(LibreInputEventKind.TextInput, text: "abcd");
        editor.Text.Should().Be("seed");
        editor.ReadOnly = false;
        KeyEventHandler suppress = (_, e) => e.SuppressKeyPress = true;
        editor.KeyDown += suppress;
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.A);
        platform.SendInput(LibreInputEventKind.TextInput, text: "abcd");
        editor.Text.Should().Be("seed");
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.A);
        editor.KeyDown -= suppress;
        platform.SendInput(LibreInputEventKind.TextInput, text: "abcd");
        editor.Text.Should().Be("ABC");
        editor.SelectionStart.Should().Be(3);
        editor.SelectionLength.Should().Be(0);
        editor.SelectAll();
        editor.KeyDown += suppress;
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.A);
        platform.SendInput(LibreInputEventKind.FocusLost);
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.KeyDown -= suppress;
        platform.SendInput(LibreInputEventKind.TextInput, text: "x");
        editor.Text.Should().Be("X");
    }

    [Fact]
    public void PortableBackspaceAndDeletePreserveSurrogateSelectionIndices()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using TextBox editor = new() { Text = "a🙂b" };
        form.Controls.Add(editor);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.Select(3, 0);
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Backspace);
        platform.SendInput(LibreInputEventKind.TextInput, text: "\b");
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.Backspace);
        editor.Text.Should().Be("ab");
        editor.SelectionStart.Should().Be(1);
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Delete);
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.Delete);
        editor.Text.Should().Be("a");
        editor.SelectionStart.Should().Be(1);
    }

    [Fact]
    public void PortableMaskedTextInputUsesOriginalMaskProviderExactlyOnce()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using MaskedTextBox editor = new("L00000") { TextMaskFormat = MaskFormat.ExcludePromptAndLiterals };
        form.Controls.Add(editor);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.Select(0, 0);
        int rejected = 0;
        editor.MaskInputRejected += (_, _) => rejected++;
        platform.SendInput(LibreInputEventKind.TextInput, text: "A12x345");
        editor.Text.Should().Be("A12345");
        editor.MaskCompleted.Should().BeTrue();
        rejected.Should().Be(1);
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Backspace);
        platform.SendInput(LibreInputEventKind.TextInput, text: "\b");
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.Backspace);
        editor.Text.Should().Be("A1234");
        editor.MaskCompleted.Should().BeFalse();
    }

    [Fact]
    public void PortableTextInputStopsAfterItsSourceControlIsDisposed()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using TextBox editor = new();
        form.Controls.Add(editor);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        int changed = 0;
        editor.TextChanged += (_, _) =>
        {
            changed++;
            editor.Dispose();
        };
        platform.SendInput(LibreInputEventKind.TextInput, text: "abc");
        changed.Should().Be(1);
        editor.IsDisposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableBackspaceInvokesCanonicalKeyPressBeforeDefaultEdit(bool changeCharacter)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using TextBox editor = new() { Text = "abc" };
        form.Controls.Add(editor);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.Select(3, 0);
        List<string> events = [];
        editor.KeyDown += (_, _) => events.Add("down");
        editor.KeyPress += (_, e) =>
        {
            events.Add("press");
            editor.Text.Should().Be("abc");
            e.KeyChar.Should().Be('\b');
            if (changeCharacter)
            {
                e.KeyChar = '!';
            }
            else
            {
                e.Handled = true;
            }
        };
        editor.TextChanged += (_, _) => events.Add("text");
        editor.KeyUp += (_, _) => events.Add("up");
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Backspace);
        platform.SendInput(LibreInputEventKind.TextInput, text: "\b");
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.Backspace);
        editor.Text.Should().Be(changeCharacter ? "abc!" : "abc");
        events.Should().Equal(changeCharacter ? ["down", "press", "text", "up"] : ["down", "press", "up"]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PortableBackspaceRetainsReadOnlyAndHandledKeyDownNotifications(bool readOnly, bool handleDown)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false };
        using TextBox editor = new() { Text = "abc", ReadOnly = readOnly };
        form.Controls.Add(editor);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        editor.Select(3, 0);
        List<string> events = [];
        editor.KeyDown += (_, e) => { events.Add("down"); e.Handled = handleDown; };
        editor.KeyPress += (_, e) => { events.Add("press"); e.KeyChar.Should().Be('\b'); };
        editor.TextChanged += (_, _) => events.Add("text");
        editor.KeyUp += (_, _) => events.Add("up");
        platform.SendInput(LibreInputEventKind.KeyDown, key: LibreKey.Backspace);
        platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.Backspace);
        editor.Text.Should().Be(readOnly ? "abc" : "ab");
        events.Should().Equal(readOnly ? ["down", "press", "up"] : ["down", "press", "text", "up"]);
    }

    private sealed class PortableCharacterFilter : IMessageFilter
    {
        public int Count { get; private set; }
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg == 0x0102)
            {
                Count++;
                return true;
            }

            return false;
        }
    }
}
