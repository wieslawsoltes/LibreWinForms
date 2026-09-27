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
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CanonicalContextPointerOpensTheAssignedSourceMenu(int kind)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false, ClientSize = new(300, 180) };
        using Control target = kind switch { 0 => new Button(), 1 => new Panel(), _ => new TextBox() };
        target.Bounds = new(24, 40, 120, 36);
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        target.ContextMenuStrip = menu;
        owner.Controls.Add(target);
        owner.Show();
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        menu.Visible.Should().BeFalse();
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerUp);
        menu.Visible.Should().BeTrue();
        menu.SourceControl.Should().BeSameAs(target);
        platform.GetWindowOwner(menu).Should().Be(platform.GetWindowHandle(owner));
        platform.IsWindowVisible(menu).Should().BeTrue();
        target.Capture.Should().BeFalse();
    }

    [Fact]
    public void CanonicalContextPointerPreservesContextClickMouseUpOrdering()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Panel target = new() { Bounds = new(20, 20, 100, 60) };
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        target.ContextMenuStrip = menu;
        owner.Controls.Add(target);
        List<string> order = [];
        menu.Opening += (_, _) => order.Add("opening");
        menu.Opened += (_, _) => order.Add("opened");
        target.Click += (_, _) => order.Add("click");
        target.MouseClick += (_, e) => { e.Button.Should().Be(MouseButtons.Right); order.Add("mouse-click"); };
        target.MouseUp += (_, e) => { e.Button.Should().Be(MouseButtons.Right); order.Add("mouse-up"); };
        owner.Show();
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerUp);
        order.Should().Equal("opening", "opened", "click", "mouse-click", "mouse-up");
        target.Capture.Should().BeFalse();
    }

    [Fact]
    public void CanonicalContextPointerUsesDefaultParentMessageForwarding()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Panel parent = new() { Bounds = new(20, 20, 160, 100) };
        using Label target = new() { Bounds = new(10, 10, 100, 40) };
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        parent.ContextMenuStrip = menu;
        parent.Controls.Add(target);
        owner.Controls.Add(parent);
        owner.Show();
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerUp);
        menu.Visible.Should().BeTrue();
        menu.SourceControl.Should().BeSameAs(parent);
        target.Capture.Should().BeFalse();
    }

    [Fact]
    public void CanonicalContextPointerUsesTheVirtualWindowProcedure()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ContextMessagePanel target = new() { Bounds = new(20, 20, 100, 60) };
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        target.ContextMenuStrip = menu;
        owner.Controls.Add(target);
        owner.Show();
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerUp);
        target.ContextMessages.Should().Be(1);
        menu.Visible.Should().BeFalse("the actual source window procedure consumed WM_CONTEXTMENU");
        target.Capture.Should().BeFalse();
    }

    [Fact]
    public void CanonicalContextPointerRetainsCanceledOpeningAndMouseUp()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Panel target = new() { Bounds = new(20, 20, 100, 60) };
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        target.ContextMenuStrip = menu;
        owner.Controls.Add(target);
        int opening = 0, released = 0;
        menu.Opening += (_, e) => { opening++; e.Cancel = true; };
        target.MouseUp += (_, _) => released++;
        owner.Show();
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerUp);
        opening.Should().Be(1);
        released.Should().Be(1);
        menu.Visible.Should().BeFalse();
        target.Capture.Should().BeFalse();
    }

    [Fact]
    public void CanonicalContextPointerReleasesCaptureWhenOpeningThrows()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Panel target = new() { Bounds = new(20, 20, 100, 60) };
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        target.ContextMenuStrip = menu;
        owner.Controls.Add(target);
        InvalidOperationException failure = new("context opening failure");
        menu.Opening += (_, _) => throw failure;
        owner.Show();
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        Action release = () => SendContextPointer(platform, owner, target, LibreInputEventKind.PointerUp);
        release.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        target.Capture.Should().BeFalse();
        Control.MouseButtons.Should().Be(MouseButtons.None);
        menu.Visible.Should().BeFalse();
    }

    [Theory]
    [InlineData(LibrePointerButton.Primary)]
    [InlineData(LibrePointerButton.Middle)]
    public void CanonicalContextPointerDoesNotOpenForOtherButtons(LibrePointerButton button)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Panel target = new() { Bounds = new(20, 20, 100, 60) };
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        target.ContextMenuStrip = menu;
        owner.Controls.Add(target);
        int opening = 0;
        menu.Opening += (_, _) => opening++;
        owner.Show();
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerDown, button);
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerUp, button);
        opening.Should().Be(0);
        menu.Visible.Should().BeFalse();
    }

    [Fact]
    public void CanonicalContextPointerOutsideClientDoesNotOpenTheCapturedControlsMenu()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false, ClientSize = new(300, 180) };
        using Panel target = new() { Bounds = new(20, 20, 100, 60) };
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        target.ContextMenuStrip = menu;
        owner.Controls.Add(target);
        owner.Show();
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.PointerUp, 1,
            LibreInputModifiers.None, LibreKey.Unknown, null, new(250, 140), default, LibrePointerButton.Secondary));
        menu.Visible.Should().BeFalse();
        target.Capture.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CanonicalContextPointerDoesNotContinueInARetiredSourceGeneration(int action)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using RecreatingForm owner = new() { ShowIcon = false };
        using Panel target = new() { Bounds = new(20, 20, 100, 60) };
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        target.ContextMenuStrip = menu;
        owner.Controls.Add(target);
        int opening = 0, released = 0;
        menu.Opening += (_, e) =>
        {
            opening++;
            e.Cancel = true;
            if (action == 0) target.Dispose();
            else if (action == 1) owner.Dispose();
            else owner.RecreatePortableHandle();
        };
        target.MouseUp += (_, _) => released++;
        owner.Show();
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerUp);
        opening.Should().Be(1);
        released.Should().Be(0);
        menu.Visible.Should().BeFalse();
        target.Capture.Should().BeFalse();
    }

    [Fact]
    public void CanonicalContextPointerCleanupRetainsANestedNewPress()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false, ClientSize = new(300, 180) };
        using Panel target = new() { Bounds = new(20, 20, 100, 60) };
        using Button replacement = new() { Bounds = new(160, 20, 100, 60) };
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        target.ContextMenuStrip = menu;
        owner.Controls.AddRange([target, replacement]);
        int opening = 0, clicks = 0;
        menu.Opening += (_, e) =>
        {
            opening++;
            e.Cancel = true;
            SendContextPointer(platform, owner, replacement, LibreInputEventKind.PointerDown, LibrePointerButton.Primary);
        };
        replacement.Click += (_, _) => clicks++;
        owner.Show();
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        SendContextPointer(platform, owner, target, LibreInputEventKind.PointerUp);
        opening.Should().Be(1);
        replacement.Capture.Should().BeTrue();
        Control.MouseButtons.Should().Be(MouseButtons.Left);
        SendContextPointer(platform, owner, replacement, LibreInputEventKind.PointerUp, LibrePointerButton.Primary);
        replacement.Capture.Should().BeFalse();
        clicks.Should().Be(1);
    }

    [Fact]
    public void CanonicalContextPointerUsesTheActualDataGridCellMenuPolicy()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false, ClientSize = new(300, 180) };
        using DataGridView grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false };
        grid.Columns.Add("name", "Name");
        grid.Rows.Add("Alice");
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        grid.Rows[0].Cells[0].ContextMenuStrip = menu;
        owner.Controls.Add(grid);
        owner.Show();
        Rectangle cell = grid.GetCellDisplayRectangle(0, 0, cutOverflow: true);
        Point position = owner.PointToClient(grid.PointToScreen(new(cell.X + cell.Width / 2, cell.Y + cell.Height / 2)));
        foreach (LibreInputEventKind kind in new[] { LibreInputEventKind.PointerDown, LibreInputEventKind.PointerUp })
            platform.SendControlInput(owner, new LibreInputEvent(kind, 1, LibreInputModifiers.None, LibreKey.Unknown,
                null, new(position.X, position.Y), default, LibrePointerButton.Secondary));
        menu.Visible.Should().BeTrue();
        menu.SourceControl.Should().BeSameAs(grid);
    }

    private static void SendContextPointer(HeadlessPlatform platform, Form owner, Control target,
        LibreInputEventKind kind, LibrePointerButton button = LibrePointerButton.Secondary)
    {
        Point position = owner.PointToClient(target.PointToScreen(new(target.ClientSize.Width / 2, target.ClientSize.Height / 2)));
        platform.SendControlInput(owner, new LibreInputEvent(kind, 1, LibreInputModifiers.None, LibreKey.Unknown,
            null, new(position.X, position.Y), default, button));
    }

    private sealed class ContextMessagePanel : Panel
    {
        internal int ContextMessages;

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x007B)
                ContextMessages++;
            else
                base.WndProc(ref message);
        }
    }
}
