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
    public void CanonicalDropdownOwnsAnIndependentHiddenCreatedPlatformPopup()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        menu.Show(owner, new Point(20, 30));
        platform.WindowsCreated.Should().Be(2);
        LibreWindowCreateOptions options = platform.LastWindowOptions;
        options.Options.Should().HaveFlag(LibreWindowOptions.Popup);
        options.Options.Should().HaveFlag(LibreWindowOptions.ToolWindow);
        options.Options.Should().HaveFlag(LibreWindowOptions.TopMost);
        options.Options.Should().NotHaveFlag(LibreWindowOptions.Visible);
        options.Options.Should().NotHaveFlag(LibreWindowOptions.Decorated);
        options.ShowInTaskbar.Should().BeFalse();
        options.CanClose.Should().BeFalse();
        platform.GetWindowOwner(menu).Should().Be(platform.GetWindowHandle(owner));
        platform.IsWindowVisible(menu).Should().BeTrue();
        platform.LastWindowBounds.Should().Be(new LibreRectangle(menu.Left, menu.Top, menu.Width, menu.Height));
        platform.LastActivatedWindow.IsNull.Should().BeTrue();
    }

    [Fact]
    public void PrecreatedDropdownWindowBindsItsOwnerWithoutChangingTheHandle()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        nint handle = menu.Handle;
        platform.IsWindowVisible(menu).Should().BeFalse();
        platform.GetWindowOwner(menu).IsNull.Should().BeTrue();
        menu.Show(owner, Point.Empty);
        menu.Handle.Should().Be(handle);
        platform.GetWindowOwner(menu).Should().Be(platform.GetWindowHandle(owner));
        menu.Close();
        platform.IsWindowVisible(menu).Should().BeFalse();
        menu.Show(owner, new Point(40, 50));
        menu.Handle.Should().Be(handle);
        platform.WindowsCreated.Should().Be(2);
    }

    [Fact]
    public void CanonicalDropdownPlatformPaintInvokesTheActualSourceRenderer()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        int painted = 0;
        menu.Paint += (_, _) => painted++;
        menu.Show(owner, Point.Empty);
        menu.Refresh();
        painted.Should().BeGreaterThan(0);
        platform.LastPaintCommandCount.Should().BeGreaterThan(0);
        platform.TextDrawStrings.Should().Contain("Open");
    }

    [Fact]
    public void CanonicalDropdownPlatformPointerEventsInvokeTheActualMenuItem()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new();
        ToolStripItem item = menu.Items.Add("Open");
        int clicked = 0;
        item.Click += (_, _) => clicked++;
        menu.Show(owner, Point.Empty);
        LibrePoint point = new(item.Bounds.Left + item.Width / 2, item.Bounds.Top + item.Height / 2);
        menu.ClientRectangle.Contains(point.X, point.Y).Should().BeTrue(
            "the source item center {0} must be inside the arranged popup client {1}", point, menu.ClientRectangle);
        menu.GetItemAt(point.X, point.Y).Should().BeSameAs(item);
        menu.Enabled.Should().BeTrue();
        item.Enabled.Should().BeTrue();
        menu.PointToClient(menu.PointToScreen(new Point(point.X, point.Y))).Should().Be(new Point(point.X, point.Y));
        int moved = 0;
        int pressed = 0;
        int released = 0;
        int entered = 0;
        menu.MouseEnter += (_, _) => entered++;
        item.MouseMove += (_, _) => moved++;
        item.MouseDown += (_, _) => pressed++;
        item.MouseUp += (_, _) => released++;
        LibreInputEventKind[] pointerEvents =
            [LibreInputEventKind.PointerMove, LibreInputEventKind.PointerDown, LibreInputEventKind.PointerUp];
        foreach (LibreInputEventKind kind in pointerEvents)
        {
            platform.SendControlInput(menu, new LibreInputEvent(kind, 1, LibreInputModifiers.None,
                LibreKey.Unknown, null, point, default, LibrePointerButton.Primary));
        }

        Control.MousePosition.Should().Be(menu.PointToScreen(new Point(point.X, point.Y)));
        entered.Should().Be(1);
        moved.Should().Be(1);
        pressed.Should().Be(1);
        released.Should().Be(1);
        clicked.Should().Be(1);
        menu.Visible.Should().BeFalse();
        Form.ActiveForm.Should().BeSameAs(owner);
        platform.LastActivatedWindow.IsNull.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenParentRetainsItsChildsExplicitVisibilityChoice(bool childVisible)
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Button child = new();
        owner.Controls.Add(child);
        child.Visible.Should().BeFalse();
        child.Visible = false;
        if (childVisible)
            child.Visible = true;
        owner.Show();
        child.Visible.Should().Be(childVisible);
        owner.Hide();
        owner.Show();
        child.Visible.Should().Be(childVisible);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OwnerHideMinimizeOrDisposeReleasesPersistentNativePopupChains(int action)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new() { AutoClose = false };
        var item = new ToolStripMenuItem("More");
        item.DropDownItems.Add("Child");
        item.DropDown.AutoClose = false;
        menu.Items.Add(item);
        menu.Show(owner, Point.Empty);
        item.ShowDropDown();
        platform.WindowsCreated.Should().Be(3);
        platform.GetWindowOwner(item.DropDown).Should().Be(platform.GetWindowHandle(owner));
        int closed = 0;
        menu.Closed += (_, _) => closed++;
        item.DropDown.Closed += (_, _) => closed++;
        if (action == 0)
            owner.Hide();
        else if (action == 1)
            owner.WindowState = FormWindowState.Minimized;
        else
            owner.Dispose();
        menu.Visible.Should().BeFalse();
        item.DropDown.Visible.Should().BeFalse();
        menu.IsHandleCreated.Should().BeFalse();
        item.DropDown.IsHandleCreated.Should().BeFalse();
        menu.IsDisposed.Should().BeFalse();
        closed.Should().Be(2);
    }

    [Fact]
    public void SourceDropdownCanBeReusedAfterItsNativeOwnerWindowWasDestroyed()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        menu.Show(first, Point.Empty);
        nint previous = menu.Handle;
        first.Dispose();
        menu.IsHandleCreated.Should().BeFalse();
        menu.Show(second, Point.Empty);
        menu.Handle.Should().NotBe(previous);
        platform.GetWindowOwner(menu).Should().Be(platform.GetWindowHandle(second));
        platform.IsWindowVisible(menu).Should().BeTrue();
    }

    [Fact]
    public void RejectedPopupCreationDoesNotPublishVisibleOrOpenedState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        int opened = 0;
        menu.Opened += (_, _) => opened++;
        platform.RejectPopupCreation = true;
        Action show = () => menu.Show(owner, Point.Empty);
        show.Should().Throw<PlatformNotSupportedException>();
        menu.Visible.Should().BeFalse();
        menu.IsHandleCreated.Should().BeFalse();
        platform.WindowsCreated.Should().Be(1);
        opened.Should().Be(0);
        platform.RejectPopupCreation = false;
        show();
        menu.Visible.Should().BeTrue();
        opened.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReentrantSourceReuseRequiresCompletedOldHandleDestruction(bool duringHandleDestroyed)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        using ContextMenuStrip menu = new() { AutoClose = false };
        menu.Items.Add("Open");
        menu.Show(first, Point.Empty);
        nint oldHandle = menu.Handle;
        int callbacks = 0;
        void Reopen()
        {
            callbacks++;
            Action show = () => menu.Show(second, Point.Empty);
            if (duringHandleDestroyed)
                show.Should().Throw<InvalidOperationException>();
            else
                show();
        }

        if (duringHandleDestroyed)
            menu.HandleDestroyed += (_, _) => Reopen();
        else
            menu.Closed += (_, _) => Reopen();
        first.Hide();
        callbacks.Should().Be(1);
        if (duringHandleDestroyed)
            menu.IsHandleCreated.Should().BeFalse();
        else
        {
            menu.Visible.Should().BeTrue();
            menu.Handle.Should().NotBe(oldHandle);
            platform.GetWindowOwner(menu).Should().Be(platform.GetWindowHandle(second));
        }
    }

    [Fact]
    public void ParentlessLogicalControlCannotMasqueradeAsANativePopupOwner()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Control orphan = new();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        Action show = () => menu.Show(orphan, Point.Empty);
        show.Should().Throw<InvalidOperationException>();
        menu.Visible.Should().BeFalse();
        platform.WindowsCreated.Should().Be(0);
    }
}
