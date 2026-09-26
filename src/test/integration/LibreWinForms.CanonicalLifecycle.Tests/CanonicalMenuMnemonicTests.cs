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
    [InlineData("&File", "f")]
    [InlineData("File", "f")]
    [InlineData("&Édit", "é")]
    public void NativeSystemCharactersEnterTheCanonicalMenuMnemonicPath(string caption, string character)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = new();
            ToolStripMenuItem item = new(caption);
            ToolStripItem command = item.DropDownItems.Add("Command");
            bar.Items.Add(item);
            owner.Controls.Add(bar);
            owner.MainMenuStrip = bar;
            int clicked = 0;
            command.Click += (_, _) => clicked++;
            SendMenuKey(platform, owner, LibreKey.LeftAlt, down: true);
            SendNativeCharacter(platform, owner, character, system: true, LibreInputModifiers.Alt);
            item.DropDown.Visible.Should().BeTrue();
            command.Selected.Should().BeTrue();
            SendMenuKey(platform, owner, LibreKey.LeftAlt, down: false);
            item.DropDown.Visible.Should().BeTrue("the mnemonic canceled bare-Alt activation");
            SendDropdownKey(platform, owner, LibreKey.Enter);
            clicked.Should().Be(1);
            editor.Text.Should().BeEmpty();
            editor.Focused.Should().BeTrue();
            platform.LastActivatedWindow.IsNull.Should().BeTrue();
        });
    }

    [Fact]
    public void NativeDuplicateMenuMnemonicsCycleWithoutNativeMenuHooks()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = new();
            ToolStripMenuItem first = new("&File");
            ToolStripMenuItem second = new("&Format");
            first.DropDownItems.Add("First command");
            second.DropDownItems.Add("Second command");
            bar.Items.Add(first);
            bar.Items.Add(second);
            owner.Controls.Add(bar);
            owner.MainMenuStrip = bar;
            SendNativeCharacter(platform, owner, "f", system: true, LibreInputModifiers.Alt);
            first.Selected.Should().BeTrue();
            first.DropDown.Visible.Should().BeFalse();
            SendNativeCharacter(platform, owner, "f", system: true, LibreInputModifiers.Alt);
            second.Selected.Should().BeTrue();
            second.DropDown.Visible.Should().BeFalse();
            SendDropdownKey(platform, owner, LibreKey.Down);
            second.DropDown.Visible.Should().BeTrue();
            second.DropDownItems[0].Selected.Should().BeTrue();
            editor.Focused.Should().BeTrue();
            platform.LastActivatedWindow.IsNull.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(LibreInputModifiers.Alt)]
    [InlineData(LibreInputModifiers.Control | LibreInputModifiers.Alt)]
    public void OrdinaryNativeTextRemainsTextWithOptionOrAltGrModifiers(LibreInputModifiers modifiers)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = new();
            ToolStripMenuItem item = new("&Édit");
            item.DropDownItems.Add("Command");
            bar.Items.Add(item);
            owner.Controls.Add(bar);
            SendNativeCharacter(platform, owner, "é", system: false, modifiers);
            editor.Text.Should().Be("é");
            item.DropDown.Visible.Should().BeFalse();
            item.Selected.Should().BeFalse();
        });
    }

    [Fact]
    public void UnmatchedSystemCharacterRaisesKeyPressWithoutEditingTheOwner()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            List<char> characters = [];
            editor.KeyPress += (_, e) => characters.Add(e.KeyChar);
            SendNativeCharacter(platform, owner, "z", system: true, LibreInputModifiers.Alt);
            characters.Should().Equal('z');
            editor.Text.Should().BeEmpty();
        });
    }

    [Fact]
    public void NativeSystemCharacterFiltersRunBeforeCanonicalMnemonicSelection()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = new();
            ToolStripMenuItem item = new("&File");
            item.DropDownItems.Add("Command");
            bar.Items.Add(item);
            owner.Controls.Add(bar);
            int filtered = 0;
            CallbackKeyboardFilter filter = new(message =>
            {
                if (message.Msg != 0x106) return false; // WM_SYSCHAR
                filtered++;
                return true;
            });
            Application.AddMessageFilter(filter);
            try { SendNativeCharacter(platform, owner, "f", system: true, LibreInputModifiers.Alt); }
            finally { Application.RemoveMessageFilter(filter); }
            filtered.Should().Be(1);
            item.DropDown.Visible.Should().BeFalse();
            editor.Text.Should().BeEmpty();
        });
    }

    private static void SendNativeCharacter(HeadlessPlatform platform, Form owner, string text,
        bool system, LibreInputModifiers modifiers)
        => platform.SendControlInput(owner, new LibreInputEvent(
            system ? LibreInputEventKind.SystemTextInput : LibreInputEventKind.TextInput,
            1, modifiers, LibreKey.Unknown, text, default, default, LibrePointerButton.None));
}
