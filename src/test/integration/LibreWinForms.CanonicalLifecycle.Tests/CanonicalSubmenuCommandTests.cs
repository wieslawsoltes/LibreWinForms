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
    public void SubmenuCommandDismissesAncestorsAndRetainsSourceControlDuringClick(bool pointer)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using ContextMenuStrip root = new();
            ToolStripMenuItem first = new("First");
            ToolStripMenuItem second = new("Second");
            ToolStripItem command = second.DropDownItems.Add("Command");
            first.DropDownItems.Add(second);
            root.Items.Add(first);
            Dictionary<string, ToolStripDropDownCloseReason> closed = [];
            root.Closed += (_, e) => closed.Add("root", e.CloseReason);
            first.DropDown.Closed += (_, e) => closed.Add("middle", e.CloseReason);
            second.DropDown.Closed += (_, e) => closed.Add("leaf", e.CloseReason);
            Control? sourceAtClick = null;
            int clicks = 0;
            command.Click += (_, _) => { sourceAtClick = root.SourceControl; clicks++; };
            root.Show(editor, Point.Empty);
            first.ShowDropDown();
            second.ShowDropDown();

            if (pointer) ClickSubmenuCommand(platform, second.DropDown, command);
            else command.PerformClick();

            clicks.Should().Be(1);
            root.Visible.Should().BeFalse();
            first.DropDown.Visible.Should().BeFalse();
            second.DropDown.Visible.Should().BeFalse();
            closed.Should().HaveCount(3);
            closed["leaf"].Should().Be(ToolStripDropDownCloseReason.ItemClicked);
            closed["middle"].Should().Be(ToolStripDropDownCloseReason.AppFocusChange);
            closed["root"].Should().Be(ToolStripDropDownCloseReason.AppFocusChange);
            sourceAtClick.Should().BeSameAs(editor);
            editor.Focused.Should().BeTrue();
            platform.LastActivatedWindow.IsNull.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SubmenuCommandPreservesLeafVetoAncestorVetoAndPersistentRoot(int cancellation)
    {
        RunDropdownKeyboard((_, owner, editor) =>
        {
            using ContextMenuStrip root = new() { AutoClose = cancellation != 2 };
            ToolStripMenuItem more = new("More");
            ToolStripItem command = more.DropDownItems.Add("Command");
            root.Items.Add(more);
            if (cancellation == 0) more.DropDown.Closing += (_, e) => e.Cancel = true;
            if (cancellation == 1) root.Closing += (_, e) => e.Cancel = true;
            int clicks = 0;
            command.Click += (_, _) => clicks++;
            root.Show(editor, Point.Empty);
            more.ShowDropDown();

            command.PerformClick();

            clicks.Should().Be(1);
            root.Visible.Should().BeTrue();
            more.DropDown.Visible.Should().Be(cancellation == 0);
        });
    }

    [Fact]
    public void SubmenuCommandRetiresAncestorsBeforePropagatingTheCommandException()
    {
        RunDropdownKeyboard((_, owner, editor) =>
        {
            using ContextMenuStrip root = new();
            ToolStripMenuItem more = new("More");
            ToolStripItem command = more.DropDownItems.Add("Command");
            root.Items.Add(more);
            InvalidOperationException expected = new("command failure");
            command.Click += (_, _) => throw expected;
            root.Show(editor, Point.Empty);
            more.ShowDropDown();

            Action click = command.PerformClick;
            click.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);

            root.Visible.Should().BeFalse();
            more.DropDown.Visible.Should().BeFalse();
        });
    }

    [Fact]
    public void SubmenuCommandEndsItsMainMenuContinuationAndReturnsTextToTheEditor()
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = AddMenuKeyBar(owner);
            ToolStripMenuItem file = (ToolStripMenuItem)bar.Items[1];
            file.DropDownItems.Add("Command");
            SendDropdownKey(platform, owner, LibreKey.F10);
            SendDropdownKey(platform, owner, LibreKey.Down);
            file.DropDown.Visible.Should().BeTrue();

            file.DropDownItems[0].PerformClick();

            file.DropDown.Visible.Should().BeFalse();
            file.Selected.Should().BeFalse();
            SendDropdownText(platform, owner, "x");
            editor.Text.Should().Be("x");
            editor.Focused.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubmenuCommandCannotRetireAContinuationReplacedByClosedCallback(bool sameStrip)
    {
        RunDropdownKeyboard((platform, owner, editor) =>
        {
            using MenuStrip bar = AddMenuKeyBar(owner);
            using MenuStrip other = new() { Dock = DockStyle.Bottom };
            owner.Controls.Add(other);
            MenuStrip replacement = sameStrip ? bar : other;
            ToolStripMenuItem replacementItem = new("&Replacement");
            replacementItem.DropDownItems.Add("Replacement command");
            replacement.Items.Add(replacementItem);
            ToolStripMenuItem file = (ToolStripMenuItem)bar.Items[1];
            file.DropDownItems.Add("Command");
            SendDropdownKey(platform, owner, LibreKey.F10);
            SendDropdownKey(platform, owner, LibreKey.Down);
            int replacementDeactivated = 0;
            file.DropDown.Closed += (_, _) =>
            {
                replacementItem.ShowDropDown();
                SendDropdownKey(platform, owner, LibreKey.Escape);
                replacement.MenuDeactivate += (_, _) => replacementDeactivated++;
            };

            file.DropDownItems[0].PerformClick();

            replacementDeactivated.Should().Be(0);
            replacementItem.Selected.Should().BeTrue();
            SendDropdownText(platform, owner, "r");
            replacementItem.DropDown.Visible.Should().BeTrue();
            editor.Text.Should().BeEmpty();
        });
    }

    private static void ClickSubmenuCommand(HeadlessPlatform platform, ToolStripDropDown menu, ToolStripItem item)
    {
        LibrePoint point = new(item.Bounds.Left + item.Width / 2, item.Bounds.Top + item.Height / 2);
        LibreInputEventKind[] inputs =
            [LibreInputEventKind.PointerMove, LibreInputEventKind.PointerDown, LibreInputEventKind.PointerUp];
        foreach (LibreInputEventKind kind in inputs)
            platform.SendControlInput(menu, new LibreInputEvent(kind, 1, LibreInputModifiers.None,
                LibreKey.Unknown, null, point, default, LibrePointerButton.Primary));
    }
}
