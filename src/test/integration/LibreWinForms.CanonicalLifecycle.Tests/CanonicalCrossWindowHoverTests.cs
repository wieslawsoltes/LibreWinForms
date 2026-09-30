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
    [InlineData(LibreInputEventKind.PointerMove)]
    [InlineData(LibreInputEventKind.PointerDown)]
    [InlineData(LibreInputEventKind.PointerUp)]
    [InlineData(LibreInputEventKind.PointerWheel)]
    public void PortableCrossWindowHover_LeavesBeforeEnteringAndCanReturn(LibreInputEventKind kind)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        using Panel firstTarget = new() { Bounds = new(10, 10, 80, 60) };
        using Panel secondTarget = new() { Bounds = new(10, 10, 80, 60) };
        first.Controls.Add(firstTarget);
        second.Controls.Add(secondTarget);
        first.Show();
        second.Show();
        List<string> events = [];
        firstTarget.MouseEnter += (_, _) => events.Add("first-enter");
        firstTarget.MouseLeave += (_, _) => events.Add("first-leave");
        secondTarget.MouseEnter += (_, _) => events.Add("second-enter");
        secondTarget.MouseLeave += (_, _) => events.Add("second-leave");

        SendReentrantPointer(platform, first, firstTarget, LibreInputEventKind.PointerMove);
        SendReentrantPointer(platform, second, secondTarget, kind);
        SendReentrantPointer(platform, second, secondTarget, LibreInputEventKind.PointerMove);
        SendReentrantPointer(platform, first, firstTarget, LibreInputEventKind.PointerMove);
        events.Should().Equal("first-enter", "first-leave", "second-enter", "second-leave", "first-enter");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableCrossWindowHover_LeaveCallbackOwnsItsNestedWindow(bool returnToFirst)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        using Form third = new() { ShowIcon = false };
        first.Show();
        second.Show();
        third.Show();
        SendReentrantPointer(platform, first, first, LibreInputEventKind.PointerMove);
        int leaves = 0, secondEnters = 0, secondDowns = 0, nestedEnters = 0;
        Form nested = returnToFirst ? first : third;
        first.MouseLeave += (_, _) =>
        {
            leaves++;
            SendReentrantPointer(platform, nested, nested, LibreInputEventKind.PointerMove);
        };
        nested.MouseEnter += (_, _) => nestedEnters++;
        second.MouseEnter += (_, _) => secondEnters++;
        second.MouseDown += (_, _) => secondDowns++;

        SendReentrantPointer(platform, second, second, LibreInputEventKind.PointerDown);
        leaves.Should().Be(1);
        secondEnters.Should().Be(0);
        secondDowns.Should().Be(0);
        second.Capture.Should().BeFalse();
        nestedEnters.Should().Be(1);
        Control.MousePosition.Should().Be(nested.PointToScreen(new(4, 5)));
        SendReentrantPointer(platform, nested, nested, LibreInputEventKind.PointerMove);
        nestedEnters.Should().Be(1);
    }

    [Fact]
    public void PortableCrossWindowHover_KeyboardInputDoesNotOwnPhysicalHover()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        SendReentrantPointer(platform, first, first, LibreInputEventKind.PointerMove);
        int leaves = 0, enters = 0;
        first.MouseLeave += (_, _) => leaves++;
        second.MouseEnter += (_, _) => enters++;
        platform.SendFormInput(second, LibreInputEventKind.FocusGained);
        platform.SendControlInput(second, new LibreInputEvent(LibreInputEventKind.KeyDown, 1,
            LibreInputModifiers.None, LibreKey.A, null, default, default, LibrePointerButton.None));
        platform.SendControlInput(second, new LibreInputEvent(LibreInputEventKind.KeyUp, 2,
            LibreInputModifiers.None, LibreKey.A, null, default, default, LibrePointerButton.None));
        leaves.Should().Be(0);
        enters.Should().Be(0);

        SendReentrantPointer(platform, second, second, LibreInputEventKind.PointerMove);
        leaves.Should().Be(1);
        enters.Should().Be(1);
    }

    [Fact]
    public void PortableCrossWindowHover_ThrowingLeaveIsRetiredBeforeTheNextPacket()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        SendReentrantPointer(platform, first, first, LibreInputEventKind.PointerMove);
        InvalidOperationException failure = new("cross-window leave failure");
        int leaves = 0, enters = 0;
        first.MouseLeave += (_, _) => { leaves++; throw failure; };
        second.MouseEnter += (_, _) => enters++;
        Action move = () => SendReentrantPointer(platform, second, second, LibreInputEventKind.PointerMove);
        move.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        enters.Should().Be(0);
        move.Should().NotThrow();
        leaves.Should().Be(1);
        enters.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableCrossWindowHover_DoesNotLeaveARetiredWindow(bool dispose)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using RecreatingForm first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        SendReentrantPointer(platform, first, first, LibreInputEventKind.PointerMove);
        int leaves = 0, enters = 0;
        first.MouseLeave += (_, _) => leaves++;
        second.MouseEnter += (_, _) => enters++;
        if (dispose)
            first.Dispose();
        else
            first.RecreatePortableHandle();

        SendReentrantPointer(platform, second, second, LibreInputEventKind.PointerMove);
        leaves.Should().Be(0);
        enters.Should().Be(1);
    }

    [Fact]
    public void PortableCrossWindowHover_ParentAndSubmenuKeepTheirNativeOwner()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ContextMenuStrip menu = new() { ShowItemToolTips = false };
        ToolStripMenuItem more = new("More");
        more.DropDownItems.Add("Child");
        menu.Items.Add(more);
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        menu.Show(owner, Point.Empty);
        more.ShowDropDown();
        more.DropDown.ShowItemToolTips = false;
        List<string> events = [];
        menu.MouseEnter += (_, _) => events.Add("parent-enter");
        menu.MouseLeave += (_, _) => events.Add("parent-leave");
        more.DropDown.MouseEnter += (_, _) => events.Add("child-enter");
        more.DropDown.MouseLeave += (_, _) => events.Add("child-leave");

        HoverMenuItem(platform, menu, more);
        HoverMenuItem(platform, more.DropDown, more.DropDownItems[0]);
        HoverMenuItem(platform, menu, more);
        events.Should().Equal("parent-enter", "parent-leave", "child-enter", "child-leave", "parent-enter");
        menu.Visible.Should().BeTrue();
        more.DropDown.Visible.Should().BeTrue();
        platform.GetWindowOwner(menu).Should().Be(platform.GetWindowHandle(owner));
        platform.GetWindowOwner(more.DropDown).Should().Be(platform.GetWindowHandle(owner));
        Form.ActiveForm.Should().BeSameAs(owner);
        platform.LastActivatedWindow.IsNull.Should().BeTrue();
    }

    [Fact]
    public void PortableCrossWindowHover_LeavingMenuCancelsItsPendingSubmenu()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ContextMenuStrip menu = new() { ShowItemToolTips = false };
        ToolStripMenuItem more = new("More");
        more.DropDownItems.Add("Child");
        menu.Items.Add(more);
        owner.Show();
        menu.Show(owner, Point.Empty);
        HoverMenuItem(platform, menu, more);
        platform.HasActiveTimer.Should().BeTrue();

        SendReentrantPointer(platform, owner, owner, LibreInputEventKind.PointerMove);
        platform.FireTimers();
        more.DropDown.Visible.Should().BeFalse();
        menu.Visible.Should().BeTrue("moving to the owner is not an outside press or deactivation");
    }
}
