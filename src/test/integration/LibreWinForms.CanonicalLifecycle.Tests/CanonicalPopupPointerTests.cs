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
    [InlineData(false, LibrePointerButton.Primary)]
    [InlineData(false, LibrePointerButton.Secondary)]
    [InlineData(false, LibrePointerButton.Middle)]
    [InlineData(true, LibrePointerButton.Primary)]
    [InlineData(true, LibrePointerButton.Secondary)]
    [InlineData(true, LibrePointerButton.Middle)]
    public void PortableOutsidePointerClosesMenuAndDeliversTheOriginalControlEvent(bool sibling, LibrePointerButton button)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false, Location = new Point(100, 100) };
        using Form other = new() { ShowIcon = false, Location = new Point(500, 100) };
        Form recipient = sibling ? other : owner;
        Button target = AddOutsideMenuButton(recipient);
        owner.Show();
        other.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        menu.Show(owner, new Point(10, 10));
        List<string> events = [];
        menu.Closing += (_, args) =>
        {
            args.CloseReason.Should().Be(ToolStripDropDownCloseReason.AppClicked);
            events.Add("closing");
        };
        menu.Closed += (_, args) =>
        {
            args.CloseReason.Should().Be(ToolStripDropDownCloseReason.AppClicked);
            events.Add("closed");
        };
        target.MouseDown += (_, _) => events.Add("down");
        target.MouseUp += (_, _) => events.Add("up");
        int clicked = 0;
        target.Click += (_, _) => clicked++;
        SendOutsideMenuPointer(platform, recipient, OutsideMenuButtonPoint(target), button);
        events.Should().Equal("closing", "closed", "down", "up");
        if (button == LibrePointerButton.Primary)
            clicked.Should().Be(1);
        menu.Visible.Should().BeFalse();
        platform.IsWindowVisible(menu).Should().BeFalse();
        platform.LastActivatedWindow.Should().NotBe(platform.GetWindowHandle(menu));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableOutsidePointerPreservesPersistentOrCanceledMenuWithoutEatingClick(bool cancel)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        Button target = AddOutsideMenuButton(owner);
        owner.Show();
        using ContextMenuStrip menu = new() { AutoClose = cancel };
        menu.Items.Add("Open");
        menu.Show(owner, new Point(10, 10));
        int closing = 0;
        menu.Closing += (_, args) =>
        {
            if (args.CloseReason == ToolStripDropDownCloseReason.AppClicked)
            {
                closing++;
                args.Cancel = true;
            }
        };
        int clicked = 0;
        target.Click += (_, _) => clicked++;
        SendOutsideMenuPointer(platform, owner, OutsideMenuButtonPoint(target));
        closing.Should().Be(cancel ? 1 : 0);
        clicked.Should().Be(1);
        menu.Visible.Should().BeTrue();
        platform.IsWindowVisible(menu).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PortableOutsidePointerClosesDeepestMenuBeforeOrdinaryCanceledOrPersistentRoot(int rootPolicy)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        Button target = AddOutsideMenuButton(owner);
        owner.Show();
        using ContextMenuStrip menu = new() { AutoClose = rootPolicy != 2 };
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        menu.Show(owner, new Point(10, 10));
        parent.ShowDropDown();
        List<string> closed = [];
        parent.DropDown.Closing += (_, args) =>
        {
            args.CloseReason.Should().Be(ToolStripDropDownCloseReason.AppClicked);
            closed.Add("child");
        };
        menu.Closing += (_, args) =>
        {
            if (args.CloseReason == ToolStripDropDownCloseReason.AppClicked)
            {
                closed.Add("root");
                args.Cancel = rootPolicy == 1;
            }
        };
        int clicked = 0;
        target.Click += (_, _) => clicked++;
        SendOutsideMenuPointer(platform, owner, OutsideMenuButtonPoint(target));
        if (rootPolicy == 2)
            closed.Should().Equal("child");
        else
            closed.Should().Equal("child", "root");
        parent.DropDown.Visible.Should().BeFalse();
        menu.Visible.Should().Be(rootPolicy != 0);
        clicked.Should().Be(1);
    }

    [Fact]
    public void PortableOutsidePointerRetainsTheNativeBoundedCanceledLeafAttempts()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        Button target = AddOutsideMenuButton(owner);
        owner.Show();
        using ContextMenuStrip menu = new();
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        menu.Show(owner, new Point(10, 10));
        parent.ShowDropDown();
        int childClosing = 0;
        int rootClosing = 0;
        bool cancel = true;
        parent.DropDown.Closing += (_, args) => { childClosing++; args.Cancel = cancel; };
        menu.Closing += (_, _) => rootClosing++;
        SendOutsideMenuPointer(platform, owner, OutsideMenuButtonPoint(target));
        childClosing.Should().Be(2);
        rootClosing.Should().Be(0);
        menu.Visible.Should().BeTrue();
        parent.DropDown.Visible.Should().BeTrue();
        cancel = false;
        SendOutsideMenuPointer(platform, owner, OutsideMenuButtonPoint(target));
        childClosing.Should().Be(3);
        rootClosing.Should().Be(1);
        menu.Visible.Should().BeFalse();
        parent.DropDown.Visible.Should().BeFalse();
    }

    [Fact]
    public void PortableInsideHostedControlPointerDoesNotDismissItsMenu()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        using Button hosted = new() { Text = "Hosted", Size = new Size(90, 25) };
        using ToolStripControlHost host = new(hosted) { AutoSize = false, Size = new Size(100, 30) };
        menu.Items.Add(host);
        menu.Show(owner, new Point(10, 10));
        int clicked = 0;
        int closing = 0;
        hosted.Click += (_, _) => clicked++;
        menu.Closing += (_, _) => closing++;
        Point point = menu.PointToClient(hosted.PointToScreen(new Point(hosted.Width / 2, hosted.Height / 2)));
        menu.ClientRectangle.Contains(point).Should().BeTrue();
        SendOutsideMenuPointer(platform, menu, point);
        clicked.Should().Be(1);
        closing.Should().Be(0);
        menu.Visible.Should().BeTrue();
    }

    [Fact]
    public void PortableParentClientPointerClosesOnlyTheOutsideChild()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        menu.Show(owner, new Point(10, 10));
        parent.ShowDropDown();
        // The cascade starts at the owner item's right edge, so the parent's
        // trailing padding can overlap the child. Use the middle of the top
        // padding (the source menu reserves two pixels above its items).
        Point point = new(menu.ClientSize.Width / 2, menu.ClientRectangle.Top);
        menu.ClientRectangle.Contains(point).Should().BeTrue();
        parent.Bounds.Contains(point).Should().BeFalse();
        parent.DropDown.ClientRectangle.Contains(parent.DropDown.PointToClient(menu.PointToScreen(point))).Should().BeFalse(
            "the parent-only point {0} in {1} must be outside the actual child bounds {2}", point, menu.Bounds, parent.DropDown.Bounds);
        int pressed = 0;
        menu.MouseDown += (_, _) => pressed++;
        SendOutsideMenuPointer(platform, menu, point, release: false);
        parent.DropDown.Visible.Should().BeFalse();
        menu.Visible.Should().BeTrue();
        pressed.Should().Be(1);
    }

    [Fact]
    public void PortableOwnerItemPointerKeepsItsCanonicalToggleInsteadOfAppClickedDismissal()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        var parent = new ToolStripMenuItem("More");
        parent.DropDownItems.Add("Child");
        menu.Items.Add(parent);
        menu.Show(owner, new Point(10, 10));
        parent.ShowDropDown();
        int pressed = 0;
        int appClicked = 0;
        parent.MouseDown += (_, _) => pressed++;
        parent.DropDown.Closing += (_, args) =>
        {
            if (args.CloseReason == ToolStripDropDownCloseReason.AppClicked)
                appClicked++;
        };
        Point point = new(parent.Bounds.Left + parent.Width / 2, parent.Bounds.Top + parent.Height / 2);
        SendOutsideMenuPointer(platform, menu, point, release: false);
        pressed.Should().Be(1);
        appClicked.Should().Be(0);
    }

    [Fact]
    public void PortableOutsidePointerDoesNotExtendItsBoundToAReentrantReplacementMenu()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        Button target = AddOutsideMenuButton(owner);
        owner.Show();
        using ContextMenuStrip menu = new();
        using ContextMenuStrip replacement = new();
        menu.Items.Add("Open");
        replacement.Items.Add("Replacement");
        menu.Show(owner, new Point(10, 10));
        menu.Closed += (_, _) => replacement.Show(owner, new Point(10, 10));
        int clicked = 0;
        target.Click += (_, _) => clicked++;
        SendOutsideMenuPointer(platform, owner, OutsideMenuButtonPoint(target));
        menu.Visible.Should().BeFalse();
        replacement.Visible.Should().BeTrue();
        clicked.Should().Be(1);
    }

    [Fact]
    public void PortableOutsidePointerPreservesClosingExceptionAndSubsequentInput()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        Button target = AddOutsideMenuButton(owner);
        owner.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        menu.Show(owner, new Point(10, 10));
        var failure = new InvalidOperationException("canonical Closing failed");
        bool fail = true;
        menu.Closing += (_, _) => { if (fail) throw failure; };
        int pressed = 0;
        target.MouseDown += (_, _) => pressed++;
        Action input = () => SendOutsideMenuPointer(platform, owner, OutsideMenuButtonPoint(target));
        input.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        pressed.Should().Be(0, "the canonical filter exception precedes ordinary dispatch");
        menu.Visible.Should().BeTrue();
        fail = false;
        input();
        pressed.Should().Be(1);
        menu.Visible.Should().BeFalse();
    }

    [Fact]
    public void PortableOutsidePointerCannotReachAReceiverDisposedByClosing()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Form recipient = new() { ShowIcon = false, Location = new Point(500, 100) };
        Button target = AddOutsideMenuButton(recipient);
        owner.Show();
        recipient.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        menu.Show(owner, new Point(10, 10));
        menu.Closed += (_, _) => recipient.Dispose();
        int pressed = 0;
        target.MouseDown += (_, _) => pressed++;
        Action input = () => SendOutsideMenuPointer(platform, recipient, OutsideMenuButtonPoint(target), release: false);
        input.Should().NotThrow();
        recipient.IsDisposed.Should().BeTrue();
        pressed.Should().Be(0);
        menu.Visible.Should().BeFalse();
    }

    [Fact]
    public void PortableOutsideExtendedButtonRetainsOriginalMenuFilterAdmission()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        Button target = AddOutsideMenuButton(owner);
        owner.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        menu.Show(owner, new Point(10, 10));
        int pressed = 0;
        target.MouseDown += (_, _) => pressed++;
        SendOutsideMenuPointer(platform, owner, OutsideMenuButtonPoint(target), LibrePointerButton.XButton1);
        menu.Visible.Should().BeTrue();
        pressed.Should().Be(1);
    }

    private static Button AddOutsideMenuButton(Form owner)
    {
        Button button = new() { Location = new Point(160, 150), Size = new Size(80, 30), Text = "Target" };
        owner.Controls.Add(button);
        return button;
    }

    private static Point OutsideMenuButtonPoint(Button button)
        => new(button.Left + button.Width / 2, button.Top + button.Height / 2);

    private static void SendOutsideMenuPointer(
        HeadlessPlatform platform, Control recipient, Point point,
        LibrePointerButton button = LibrePointerButton.Primary, bool release = true)
    {
        platform.SendControlInput(recipient, new LibreInputEvent(
            LibreInputEventKind.PointerDown, 1, LibreInputModifiers.None, LibreKey.Unknown,
            null, new LibrePoint(point.X, point.Y), default, button));
        if (release)
        {
            platform.SendControlInput(recipient, new LibreInputEvent(
                LibreInputEventKind.PointerUp, 2, LibreInputModifiers.None, LibreKey.Unknown,
                null, new LibrePoint(point.X, point.Y), default, button));
        }
    }
}
