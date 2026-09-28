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
    public void CanonicalMenuHoverExpandsTheLiveContextSubmenuWithoutActivatingIt()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new() { ShowItemToolTips = false };
        ToolStripMenuItem more = new("More");
        more.DropDownItems.Add("Child");
        menu.Items.Add(more);
        menu.Show(owner, Point.Empty);

        HoverMenuItem(platform, menu, more);
        more.Selected.Should().BeTrue();
        more.DropDown.Visible.Should().BeFalse();
        platform.HasActiveTimer.Should().BeTrue();
        platform.FireTimers();

        more.DropDown.Visible.Should().BeTrue();
        platform.GetWindowOwner(more.DropDown).Should().Be(platform.GetWindowHandle(owner));
        Form.ActiveForm.Should().BeSameAs(owner);
        platform.LastActivatedWindow.IsNull.Should().BeTrue();
    }

    private static void HoverMenuItem(HeadlessPlatform platform, ToolStrip menu, ToolStripItem item)
    {
        LibrePoint point = new(item.Bounds.Left + item.Width / 2, item.Bounds.Top + item.Height / 2);
        menu.GetItemAt(point.X, point.Y).Should().BeSameAs(item);
        platform.SendControlInput(menu, new LibreInputEvent(LibreInputEventKind.PointerMove, 1,
            LibreInputModifiers.None, LibreKey.Unknown, null, point, default, LibrePointerButton.None));
    }

    [Fact]
    public void CanonicalMenuHoverExpandsAnActiveNestedSubmenu()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new() { ShowItemToolTips = false };
        ToolStripMenuItem more = new("More");
        ToolStripMenuItem nested = new("Nested");
        nested.DropDownItems.Add("Command");
        more.DropDownItems.Add(nested);
        more.DropDown.ShowItemToolTips = false;
        menu.Items.Add(more);
        menu.Show(owner, Point.Empty);
        more.ShowDropDown();

        HoverMenuItem(platform, more.DropDown, nested);
        nested.Selected.Should().BeTrue();
        platform.FireTimers();

        nested.DropDown.Visible.Should().BeTrue();
        more.DropDown.Visible.Should().BeTrue();
        menu.Visible.Should().BeTrue();
        platform.GetWindowOwner(nested.DropDown).Should().Be(platform.GetWindowHandle(owner));
    }

    [Fact]
    public void CanonicalMenuHoverTransitionsBetweenSiblingSubmenus()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new() { ShowItemToolTips = false };
        ToolStripMenuItem first = new("First");
        ToolStripMenuItem second = new("Second");
        first.DropDownItems.Add("First command");
        second.DropDownItems.Add("Second command");
        menu.Items.Add(first);
        menu.Items.Add(second);
        menu.Show(owner, Point.Empty);
        first.ShowDropDown();

        HoverMenuItem(platform, menu, second);
        second.Selected.Should().BeTrue();
        platform.FireTimers();

        first.DropDown.Visible.Should().BeFalse();
        second.DropDown.Visible.Should().BeTrue();
        menu.Visible.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CanonicalMenuHoverDoesNotExpandAfterRetirementOrDisable(int change)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new() { ShowItemToolTips = false };
        ToolStripMenuItem more = new("More");
        more.DropDownItems.Add("Child");
        menu.Items.Add(more);
        int opened = 0;
        more.DropDown.Opened += (_, _) => opened++;
        menu.Show(owner, Point.Empty);
        HoverMenuItem(platform, menu, more);
        platform.HasActiveTimer.Should().BeTrue();

        if (change == 0) menu.Close();
        else if (change == 1) owner.Hide();
        else more.Enabled = false;
        platform.FireTimers();

        opened.Should().Be(0);
        more.DropDown.Visible.Should().BeFalse();
    }

    [Fact]
    public void CanonicalMenuHoverDoesNotBorrowAnotherOwnersActiveMenu()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Form otherOwner = new() { ShowIcon = false };
        owner.Show();
        otherOwner.Show();
        using ContextMenuStrip menu = new() { ShowItemToolTips = false };
        ToolStripMenuItem more = new("More");
        more.DropDownItems.Add("Child");
        menu.Items.Add(more);
        using ContextMenuStrip otherMenu = new() { ShowItemToolTips = false };
        otherMenu.Items.Add("Other command");
        menu.Show(owner, Point.Empty);
        HoverMenuItem(platform, menu, more);
        platform.HasActiveTimer.Should().BeTrue();
        otherMenu.Show(otherOwner, Point.Empty);
        platform.FireTimers();

        more.DropDown.Visible.Should().BeFalse();
        otherMenu.Visible.Should().BeTrue();
    }

    [Fact]
    public void CanonicalMenuHoverDoesNotPromotePersistentMenuIntoModalInput()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new() { ShowItemToolTips = false, AutoClose = false };
        ToolStripMenuItem more = new("More");
        more.DropDownItems.Add("Child");
        menu.Items.Add(more);
        menu.Show(owner, Point.Empty);
        HoverMenuItem(platform, menu, more);
        platform.FireTimers();

        more.DropDown.Visible.Should().BeFalse();
        menu.Visible.Should().BeTrue();
    }
}
