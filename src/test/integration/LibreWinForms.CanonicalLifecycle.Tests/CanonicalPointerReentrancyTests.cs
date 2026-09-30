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
    public void PortablePointerReentrancy_NestedHoverOwnsTheContinuation(bool nestedOnEnter)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false, ClientSize = new(360, 120) };
        using Panel previous = new() { Bounds = new(10, 10, 80, 60) };
        using Panel target = new() { Bounds = new(110, 10, 80, 60) };
        using Panel replacement = new() { Bounds = new(210, 10, 80, 60) };
        owner.Controls.AddRange([previous, target, replacement]);
        owner.Show();
        SendReentrantPointer(platform, owner, previous, LibreInputEventKind.PointerMove);
        int previousLeaves = 0, replacementEnters = 0, replacementLeaves = 0, targetDowns = 0;
        bool nested = false;
        previous.MouseLeave += (_, _) => previousLeaves++;
        replacement.MouseEnter += (_, _) => replacementEnters++;
        replacement.MouseLeave += (_, _) => replacementLeaves++;
        target.MouseDown += (_, _) => targetDowns++;
        EventHandler nestedInput = (_, _) =>
        {
            if (nested)
                return;
            nested = true;
            SendReentrantPointer(platform, owner, replacement, LibreInputEventKind.PointerMove);
        };
        if (nestedOnEnter)
            target.MouseEnter += nestedInput;
        else
            previous.MouseLeave += nestedInput;

        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        previousLeaves.Should().Be(1);
        replacementEnters.Should().Be(1);
        targetDowns.Should().Be(0);
        target.Capture.Should().BeFalse();
        Control.MousePosition.Should().Be(replacement.PointToScreen(new(4, 5)));

        SendReentrantPointer(platform, owner, replacement, LibreInputEventKind.PointerMove);
        replacementEnters.Should().Be(1, "the nested hover must survive the old callback");
        replacementLeaves.Should().Be(0);
    }

    [Fact]
    public void PortablePointerReentrancy_ThrowingLeaveRetiresOnlyItsOldHover()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Panel previous = new() { Bounds = new(10, 10, 80, 60) };
        using Panel target = new() { Bounds = new(110, 10, 80, 60) };
        owner.Controls.AddRange([previous, target]);
        owner.Show();
        SendReentrantPointer(platform, owner, previous, LibreInputEventKind.PointerMove);
        InvalidOperationException failure = new("source hover callback failure");
        int leaves = 0, enters = 0;
        previous.MouseLeave += (_, _) => { leaves++; throw failure; };
        target.MouseEnter += (_, _) => enters++;
        Action move = () => SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerMove);
        move.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        enters.Should().Be(0);

        move.Should().NotThrow();
        leaves.Should().Be(1);
        enters.Should().Be(1);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
    public void PortablePointerReentrancy_HoverCannotDeliverIntoRetiredTarget(bool retireOnEnter, int retirement)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using RecreatingForm owner = new() { ShowIcon = false, ClientSize = new(320, 160) };
        using Form other = new() { ShowIcon = false };
        using Panel previous = new() { Bounds = new(10, 10, 80, 60) };
        using PointerLifetimePanel target = new() { Bounds = new(110, 10, 80, 60) };
        owner.Controls.AddRange([previous, target]);
        owner.Show();
        other.Show();
        SendReentrantPointer(platform, owner, previous, LibreInputEventKind.PointerMove);
        int enters = 0, downs = 0, mutations = 0;
        target.MouseEnter += (_, _) => enters++;
        target.MouseDown += (_, _) => downs++;
        EventHandler retire = (_, _) =>
        {
            mutations++;
            switch (retirement)
            {
                case 0: target.Dispose(); break;
                case 1: target.Hide(); break;
                case 2: target.Enabled = false; break;
                case 3: target.RecreatePortableHandle(); break;
                case 4: other.Controls.Add(target); break;
                case 5: owner.RecreatePortableHandle(); break;
            }
        };
        if (retireOnEnter)
            target.MouseEnter += retire;
        else
            previous.MouseLeave += retire;

        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        mutations.Should().Be(1);
        enters.Should().Be(retireOnEnter ? 1 : 0);
        downs.Should().Be(0);
        Control.MouseButtons.Should().Be(MouseButtons.None);
    }

    [Fact]
    public void PortablePointerReentrancy_ReopenedPopupRejectsTheOldMouseEnterContinuation()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ContextMenuStrip menu = new() { AutoClose = false };
        menu.Items.Add("Open");
        owner.Show();
        menu.Show(owner, Point.Empty);
        LibreHandle retired = platform.GetWindowHandle(menu);
        int entered = 0, pressed = 0;
        menu.MouseDown += (_, _) => pressed++;
        menu.MouseEnter += (_, _) =>
        {
            entered++;
            if (entered != 1)
                return;
            owner.Hide();
            owner.Show();
            menu.Show(owner, Point.Empty);
        };

        SendReentrantPointer(platform, menu, menu, LibreInputEventKind.PointerDown);
        entered.Should().Be(1);
        pressed.Should().Be(0);
        platform.GetWindowHandle(menu).Should().NotBe(retired);
        menu.Visible.Should().BeTrue();
        menu.Capture.Should().BeFalse();
        SendReentrantPointer(platform, menu, menu, LibreInputEventKind.PointerMove);
        entered.Should().Be(2, "the replacement native handle owns a fresh hover lifetime");
        SendReentrantPointer(platform, menu, menu, LibreInputEventKind.PointerMove);
        entered.Should().Be(2);
        owner.Hide();
    }

    [Fact]
    public void PortablePointerReentrancy_FocusCallbackPreservesTheNestedPressAndCapture()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false, ClientSize = new(360, 120) };
        using Button previous = new() { Bounds = new(10, 10, 80, 60) };
        using Button target = new() { Bounds = new(110, 10, 80, 60) };
        using Panel replacement = new() { Bounds = new(210, 10, 80, 60) };
        owner.Controls.AddRange([previous, target, replacement]);
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        previous.Focus().Should().BeTrue();
        int targetDowns = 0, replacementDowns = 0, replacementClicks = 0;
        target.MouseDown += (_, _) => targetDowns++;
        replacement.MouseDown += (_, _) => replacementDowns++;
        replacement.Click += (_, _) => replacementClicks++;
        target.GotFocus += (_, _) =>
            SendReentrantPointer(platform, owner, replacement, LibreInputEventKind.PointerDown);

        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        targetDowns.Should().Be(0);
        replacementDowns.Should().Be(1);
        target.Capture.Should().BeFalse();
        replacement.Capture.Should().BeTrue();
        Control.MouseButtons.Should().Be(MouseButtons.Left);
        SendReentrantPointer(platform, owner, replacement, LibreInputEventKind.PointerUp);
        replacementClicks.Should().Be(1);
        replacement.Capture.Should().BeFalse();
        Control.MouseButtons.Should().Be(MouseButtons.None);
    }

    [Theory]
    [InlineData(LibreInputEventKind.PointerMove, 0)]
    [InlineData(LibreInputEventKind.PointerMove, 1)]
    [InlineData(LibreInputEventKind.PointerMove, 2)]
    [InlineData(LibreInputEventKind.PointerMove, 3)]
    [InlineData(LibreInputEventKind.PointerUp, 0)]
    [InlineData(LibreInputEventKind.PointerUp, 1)]
    [InlineData(LibreInputEventKind.PointerUp, 2)]
    [InlineData(LibreInputEventKind.PointerUp, 3)]
    public void PortablePointerReentrancy_RetiredCaptureDoesNotKeepAPressedButton(LibreInputEventKind kind, int retirement)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using PointerLifetimePanel captured = new() { Bounds = new(10, 10, 80, 60) };
        using Panel target = new() { Bounds = new(110, 10, 80, 60) };
        owner.Controls.AddRange([captured, target]);
        owner.Show();
        SendReentrantPointer(platform, owner, captured, LibreInputEventKind.PointerDown);
        captured.Capture.Should().BeTrue();
        int releases = 0, clicks = 0, targetClicks = 0;
        captured.MouseUp += (_, _) => releases++;
        captured.Click += (_, _) => clicks++;
        target.Click += (_, _) => targetClicks++;
        captured.MouseLeave += (_, _) =>
        {
            switch (retirement)
            {
                case 0: captured.Hide(); break;
                case 1: captured.Enabled = false; break;
                case 2: captured.Dispose(); break;
                case 3: captured.RecreatePortableHandle(); break;
            }
        };

        SendReentrantPointer(platform, owner, target, kind);
        Control.MouseButtons.Should().Be(MouseButtons.None);
        captured.Capture.Should().BeFalse();
        releases.Should().Be(0, "a retired capture receives cancellation, not a synthetic up");
        clicks.Should().Be(0);
        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerUp);
        targetClicks.Should().Be(1);
        target.Capture.Should().BeFalse();
    }

    [Fact]
    public void PortablePointerReentrancy_MouseUpPreservesAReacquiredCaptureOnTheSameControl()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Panel target = new() { Bounds = new(10, 10, 80, 60) };
        owner.Controls.Add(target);
        owner.Show();
        target.MouseUp += (_, _) => { target.Capture = false; target.Capture = true; };
        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerUp);
        target.Capture.Should().BeTrue("the callback created a distinct capture lifetime");
        Control.MouseButtons.Should().Be(MouseButtons.None);
        target.Capture = false;
    }

    private static void SendReentrantPointer(HeadlessPlatform platform, Control root, Control target, LibreInputEventKind kind)
    {
        Point point = root.PointToClient(target.PointToScreen(new(4, 5)));
        LibrePointerButton button = kind is LibreInputEventKind.PointerDown or LibreInputEventKind.PointerUp
            ? LibrePointerButton.Primary : LibrePointerButton.None;
        platform.SendControlInput(root, new LibreInputEvent(kind, 1, LibreInputModifiers.None,
            LibreKey.Unknown, null, new(point.X, point.Y), default, button));
    }

    private sealed class PointerLifetimePanel : Panel
    {
        internal void RecreatePortableHandle() => RecreateHandle();
    }
}
