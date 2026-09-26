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
    public void MenuStripContinuationEndsOnOutsideEditorPointerWithoutEatingInput()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            editor.Top = 60;
            using MenuStrip bar = new();
            ToolStripMenuItem item = new("Menu");
            item.DropDownItems.Add("Command");
            bar.Items.Add(item);
            owner.Controls.Add(bar);
            owner.MainMenuStrip = bar;
            int deactivated = 0;
            bar.MenuDeactivate += (_, _) => deactivated++;
            item.ShowDropDown();
            SendDropdownKey(platform, owner, LibreKey.Escape);
            item.DropDown.Visible.Should().BeFalse();
            item.Selected.Should().BeTrue();
            int pressed = 0;
            editor.MouseDown += (_, _) => pressed++;
            Point point = owner.PointToClient(editor.PointToScreen(new Point(2, 2)));
            bar.ClientRectangle.Contains(bar.PointToClient(owner.PointToScreen(point))).Should().BeFalse();
            SendOutsideMenuPointer(platform, owner, point);
            pressed.Should().Be(1);
            deactivated.Should().Be(1);
            item.Selected.Should().BeFalse();
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("x");
            editor.Focused.Should().BeTrue();
        });
    }

    [Fact]
    public void MenuStripContinuationRetainsInsideClientPointerAndSubsequentNavigation()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            editor.Top = 60;
            using MenuStrip bar = new();
            ToolStripMenuItem item = new("Menu");
            item.DropDownItems.Add("Command");
            bar.Items.Add(item);
            owner.Controls.Add(bar);
            owner.MainMenuStrip = bar;
            int deactivated = 0;
            bar.MenuDeactivate += (_, _) => deactivated++;
            item.ShowDropDown();
            SendDropdownKey(platform, owner, LibreKey.Escape);
            Point inside = new(bar.ClientSize.Width - 2, bar.ClientSize.Height / 2);
            bar.ClientRectangle.Contains(inside).Should().BeTrue();
            item.Bounds.Contains(inside).Should().BeFalse();
            Point point = owner.PointToClient(bar.PointToScreen(inside));
            SendOutsideMenuPointer(platform, owner, point);
            deactivated.Should().Be(0);
            item.Selected.Should().BeTrue();
            SendDropdownKey(platform, owner, LibreKey.Down);
            item.DropDown.Visible.Should().BeTrue();
            editor.Text.Should().BeEmpty();
        });
    }

    [Fact]
    public void MenuStripOutsidePointerCannotRetireAContinuationReplacedByClosedCallback()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            editor.Top = 60;
            using MenuStrip bar = new();
            using MenuStrip replacement = new() { Dock = DockStyle.Bottom };
            ToolStripMenuItem item = new("Menu");
            ToolStripMenuItem replacementItem = new("&Replacement");
            item.DropDownItems.Add("Command");
            replacementItem.DropDownItems.Add("Replacement command");
            bar.Items.Add(item);
            replacement.Items.Add(replacementItem);
            owner.Controls.Add(bar);
            owner.Controls.Add(replacement);
            owner.MainMenuStrip = bar;
            item.ShowDropDown();
            SendDropdownKey(platform, owner, LibreKey.Escape);
            SendDropdownKey(platform, owner, LibreKey.Down);
            item.DropDown.Visible.Should().BeTrue();
            int replacementDeactivated = 0;
            replacement.MenuDeactivate += (_, _) => replacementDeactivated++;
            item.DropDown.Closed += (_, _) =>
            {
                replacementItem.ShowDropDown();
                SendDropdownKey(platform, owner, LibreKey.Escape);
            };
            int pressed = 0;
            editor.MouseDown += (_, _) => pressed++;
            Point point = owner.PointToClient(editor.PointToScreen(new Point(2, 2)));
            SendOutsideMenuPointer(platform, owner, point);
            pressed.Should().Be(1);
            replacementDeactivated.Should().Be(0);
            replacementItem.Selected.Should().BeTrue();
            SendDropdownText(platform, owner, "r");
            replacementItem.DropDown.Visible.Should().BeTrue();
            editor.Text.Should().BeEmpty("the callback's new continuation is not the outside press's retired lease");
        });
    }
}
