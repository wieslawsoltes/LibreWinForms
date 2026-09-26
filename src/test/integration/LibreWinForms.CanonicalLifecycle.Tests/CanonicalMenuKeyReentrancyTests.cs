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
    public void MenuKeyReleaseCannotActivateAMainMenuReplacedByUnhandledKeyUp()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip original = AddMenuKeyBar(owner);
            using MenuStrip replacement = new() { Dock = DockStyle.Bottom };
            ToolStripItem replacementItem = replacement.Items.Add("Replacement");
            owner.Controls.Add(replacement);
            int originalActivations = 0;
            int replacementActivations = 0;
            original.MenuActivate += (_, _) => originalActivations++;
            replacement.MenuActivate += (_, _) => replacementActivations++;
            owner.KeyPreview = true;
            bool replace = true;
            owner.KeyUp += (_, e) =>
            {
                if (replace && e.KeyCode == Keys.F10)
                {
                    replace = false;
                    owner.MainMenuStrip = replacement;
                }
            };

            SendDropdownKey(platform, owner, LibreKey.F10);
            replace.Should().BeFalse("the ordinary unhandled KeyUp callback ran");
            originalActivations.Should().Be(0);
            replacementActivations.Should().Be(0);
            original.Items[1].Selected.Should().BeFalse();
            replacementItem.Selected.Should().BeFalse();
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("x");

            SendDropdownKey(platform, owner, LibreKey.F10);
            replacementActivations.Should().Be(1, "a new key cycle can admit the replacement");
            replacementItem.Selected.Should().BeTrue();
        });
    }

    [Fact]
    public void MenuKeyReleaseCannotActivateTheBarAfterUnhandledKeyUpOpensADropdown()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = AddMenuKeyBar(owner);
            ToolStripMenuItem item = (ToolStripMenuItem)bar.Items[1];
            ToolStripItem command = item.DropDownItems.Add("Command");
            int clicked = 0;
            int activated = 0;
            command.Click += (_, _) => clicked++;
            bar.MenuActivate += (_, _) => activated++;
            owner.KeyPreview = true;
            bool open = true;
            owner.KeyUp += (_, e) =>
            {
                if (open && e.KeyCode == Keys.F10)
                {
                    open = false;
                    item.ShowDropDown();
                }
            };

            SendDropdownKey(platform, owner, LibreKey.F10);
            open.Should().BeFalse();
            item.DropDown.Visible.Should().BeTrue();
            activated.Should().Be(0, "the release belongs to the pre-callback menu state");
            SendDropdownKey(platform, owner, LibreKey.Down);
            command.Selected.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Enter);
            clicked.Should().Be(1);
            editor.Text.Should().BeEmpty();
            editor.Focused.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MenuKeyDeselectionCannotRetireAContinuationReplacedDuringInvalidation(bool replaceDuringPaint)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip original = AddMenuKeyBar(owner);
            using MenuStrip replacement = new() { Dock = DockStyle.Bottom };
            ToolStripMenuItem replacementItem = new("Replacement");
            replacementItem.DropDownItems.Add("Command");
            replacement.Items.Add(replacementItem);
            owner.Controls.Add(replacement);
            SendDropdownKey(platform, owner, LibreKey.F10);
            original.Items[1].Selected.Should().BeTrue();
            int replacements = 0;
            int replacementDeactivations = 0;
            bool armed = false;
            replacement.MenuDeactivate += (_, _) => replacementDeactivations++;

            void ReplaceContinuation()
            {
                if (!armed)
                    return;
                armed = false;
                replacements++;
                replacementItem.ShowDropDown();
                SendDropdownKey(platform, owner, LibreKey.Escape);
            }

            original.Paint += (_, _) =>
            {
                if (replaceDuringPaint)
                    ReplaceContinuation();
            };
            original.Invalidated += (_, _) =>
            {
                if (!armed || original.Items[1].Selected)
                    return;
                if (replaceDuringPaint)
                    original.Update();
                else
                    ReplaceContinuation();
            };
            SendMenuKey(platform, owner, LibreKey.F10, down: true);
            armed = true;
            SendMenuKey(platform, owner, LibreKey.F10, down: false);

            replacements.Should().Be(1);
            replacementDeactivations.Should().Be(0);
            replacementItem.Selected.Should().BeTrue();
            replacementItem.DropDown.Visible.Should().BeFalse();
            SendDropdownKey(platform, owner, LibreKey.Down);
            replacementItem.DropDown.Visible.Should().BeTrue();
            editor.Text.Should().BeEmpty();
            editor.Focused.Should().BeTrue();
        });
    }

    [Fact]
    public void MenuKeyDeactivationCannotClearTheSameStripReplacementContinuation()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = AddMenuKeyBar(owner);
            ToolStripMenuItem item = (ToolStripMenuItem)bar.Items[1];
            item.DropDownItems.Add("Command");
            int activations = 0;
            int deactivations = 0;
            bar.MenuActivate += (_, _) => activations++;
            SendDropdownKey(platform, owner, LibreKey.F10);
            item.Selected.Should().BeTrue();
            bool replace = true;
            bar.MenuDeactivate += (_, _) =>
            {
                deactivations++;
                if (replace)
                {
                    replace = false;
                    item.ShowDropDown();
                    SendDropdownKey(platform, owner, LibreKey.Escape);
                }
            };

            SendDropdownKey(platform, owner, LibreKey.F10);
            replace.Should().BeFalse();
            activations.Should().Be(2);
            deactivations.Should().Be(1);
            item.Selected.Should().BeTrue();
            item.DropDown.Visible.Should().BeFalse();
            SendDropdownKey(platform, owner, LibreKey.Down);
            item.DropDown.Visible.Should().BeTrue();
            editor.Text.Should().BeEmpty();
            editor.Focused.Should().BeTrue();
        });
    }

    [Fact]
    public void MenuKeyReleaseCannotActivateTheRecreatedOwnerWindow()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using RecreatingForm owner = new() { ShowIcon = false };
        using TextBox editor = new() { Bounds = new Rectangle(8, 60, 180, 40) };
        owner.Controls.Add(editor);
        owner.Shown += (_, _) => platform.Post(() =>
        {
            try
            {
                platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
                editor.Focus().Should().BeTrue();
                using MenuStrip bar = AddMenuKeyBar(owner);
                int activated = 0;
                bar.MenuActivate += (_, _) => activated++;
                SendMenuKey(platform, owner, LibreKey.F10, down: true);
                LibreHandle oldHandle = platform.GetWindowHandle(owner);
                owner.RecreatePortableHandle();
                platform.GetWindowHandle(owner).Should().NotBe(oldHandle);
                platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
                editor.Focus().Should().BeTrue();
                SendMenuKey(platform, owner, LibreKey.F10, down: false);
                activated.Should().Be(0);
                bar.Items[1].Selected.Should().BeFalse();

                SendDropdownKey(platform, owner, LibreKey.F10);
                activated.Should().Be(1, "the new window accepts an independent complete key cycle");
                bar.Items[1].Selected.Should().BeTrue();
            }
            finally
            {
                owner.Close();
            }
        });
        Application.Run(owner);
    }
}
