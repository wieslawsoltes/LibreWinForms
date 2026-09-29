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
    public void PortablePointerBoundary_PreservesExistingEventWireValues()
    {
        ((int)LibreInputEventKind.KeyDown).Should().Be(0);
        ((int)LibreInputEventKind.SystemTextInput).Should().Be(9);
        ((int)LibreInputEventKind.PointerLeave).Should().Be(10);
        ((int)LibreInputEventKind.PointerCancel).Should().Be(11);
    }

    [Theory]
    [InlineData(LibrePointerButton.Primary)]
    [InlineData(LibrePointerButton.Secondary)]
    [InlineData(LibrePointerButton.Middle)]
    [InlineData(LibrePointerButton.XButton1)]
    [InlineData(LibrePointerButton.XButton2)]
    public void PortablePointerBoundary_CancelRetiresUnfocusedPopupWithoutFocusLoss(LibrePointerButton button)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using ContextMenuStrip menu = new() { AutoClose = false };
        using Panel target = new() { Size = new(100, 60) };
        menu.Items.Add(new ToolStripControlHost(target) { AutoSize = false, Size = target.Size });
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        menu.Show(owner, Point.Empty);
        SendCancellationButton(platform, menu, target, button);
        target.Capture.Should().BeTrue();
        Point position = Control.MousePosition;
        int captures = 0, leaves = 0, releases = 0, clicks = 0, deactivations = 0;
        target.MouseCaptureChanged += (_, _) => captures++;
        target.MouseLeave += (_, _) => leaves++;
        target.MouseUp += (_, _) => releases++;
        target.Click += (_, _) => clicks++;
        owner.Deactivate += (_, _) => deactivations++;

        SendPointerBoundary(platform, menu, LibreInputEventKind.PointerCancel);
        SendPointerBoundary(platform, menu, LibreInputEventKind.PointerCancel);
        target.Capture.Should().BeFalse();
        Control.MouseButtons.Should().Be(MouseButtons.None);
        Control.MousePosition.Should().Be(position);
        captures.Should().Be(1);
        leaves.Should().Be(1);
        releases.Should().Be(0);
        clicks.Should().Be(0);
        deactivations.Should().Be(0);
        menu.Visible.Should().BeTrue();
        Form.ActiveForm.Should().BeSameAs(owner);
    }

    [Fact]
    public void PortablePointerBoundary_CancelPreservesFocusedWindowAndKeyboardState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using TextBox editor = new() { Bounds = new(10, 10, 120, 30) };
        owner.Controls.Add(editor);
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        editor.Focus().Should().BeTrue();
        SendCancellationButton(platform, owner, editor, LibrePointerButton.Primary);
        platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.KeyDown, 1,
            LibreInputModifiers.Control, LibreKey.LeftControl, null, default, default, LibrePointerButton.None));
        int lostFocus = 0, deactivations = 0;
        editor.LostFocus += (_, _) => lostFocus++;
        owner.Deactivate += (_, _) => deactivations++;

        SendPointerBoundary(platform, owner, LibreInputEventKind.PointerCancel);
        editor.Capture.Should().BeFalse();
        editor.Focused.Should().BeTrue();
        Form.ActiveForm.Should().BeSameAs(owner);
        Control.ModifierKeys.Should().Be(Keys.Control);
        lostFocus.Should().Be(0);
        deactivations.Should().Be(0);
        platform.SendControlInput(owner, new LibreInputEvent(LibreInputEventKind.KeyUp, 2,
            LibreInputModifiers.None, LibreKey.LeftControl, null, default, default, LibrePointerButton.None));
        Control.ModifierKeys.Should().Be(Keys.None);
    }

    [Fact]
    public void PortablePointerBoundary_LeaveKeepsCaptureAndPressUntilPhysicalRelease()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Button target = new() { Bounds = new(10, 10, 100, 60) };
        owner.Controls.Add(target);
        owner.Show();
        SendCancellationButton(platform, owner, target, LibrePointerButton.Primary);
        Point position = Control.MousePosition;
        int leaves = 0, releases = 0, clicks = 0;
        target.MouseLeave += (_, _) => leaves++;
        target.MouseUp += (_, _) => releases++;
        target.Click += (_, _) => clicks++;

        SendPointerBoundary(platform, owner, LibreInputEventKind.PointerLeave);
        SendPointerBoundary(platform, owner, LibreInputEventKind.PointerLeave);
        target.Capture.Should().BeTrue();
        Control.MouseButtons.Should().Be(MouseButtons.Left);
        Control.MousePosition.Should().Be(position);
        leaves.Should().Be(1);
        releases.Should().Be(0);
        clicks.Should().Be(0);
        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerUp);
        target.Capture.Should().BeFalse();
        Control.MouseButtons.Should().Be(MouseButtons.None);
        releases.Should().Be(1);
        clicks.Should().Be(1);
    }

    [Theory]
    [InlineData(LibreInputEventKind.PointerLeave)]
    [InlineData(LibreInputEventKind.PointerCancel)]
    public void PortablePointerBoundary_OldWindowCannotRetireAnotherWindowsInput(LibreInputEventKind kind)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        SendCancellationButton(platform, second, second, LibrePointerButton.Primary);
        int leaves = 0, enters = 0;
        second.MouseLeave += (_, _) => leaves++;
        second.MouseEnter += (_, _) => enters++;
        Point position = Control.MousePosition;

        SendPointerBoundary(platform, first, kind);
        second.Capture.Should().BeTrue();
        Control.MouseButtons.Should().Be(MouseButtons.Left);
        Control.MousePosition.Should().Be(position);
        SendReentrantPointer(platform, second, second, LibreInputEventKind.PointerMove);
        leaves.Should().Be(0);
        enters.Should().Be(0);
        SendPointerBoundary(platform, second, LibreInputEventKind.PointerCancel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortablePointerBoundary_LeaveCallbackOwnsItsReplacementHover(bool sameWindow)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        first.Show();
        second.Show();
        SendReentrantPointer(platform, first, first, LibreInputEventKind.PointerMove);
        Form replacement = sameWindow ? first : second;
        int leaves = 0, enters = 0;
        EventHandler leave = (_, _) =>
        {
            leaves++;
            SendReentrantPointer(platform, replacement, replacement, LibreInputEventKind.PointerMove);
        };
        first.MouseLeave += leave;
        replacement.MouseEnter += (_, _) => enters++;
        SendPointerBoundary(platform, first, LibreInputEventKind.PointerLeave);
        first.MouseLeave -= leave;
        SendReentrantPointer(platform, replacement, replacement, LibreInputEventKind.PointerMove);
        leaves.Should().Be(1);
        enters.Should().Be(1);
    }

    [Theory]
    [InlineData(LibreInputEventKind.PointerLeave)]
    [InlineData(LibreInputEventKind.PointerCancel)]
    public void PortablePointerBoundary_NestedBoundaryStopsTheOldPointerContinuation(LibreInputEventKind kind)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Panel target = new() { Bounds = new(10, 10, 100, 60) };
        owner.Controls.Add(target);
        owner.Show();
        int downs = 0, leaves = 0;
        target.MouseEnter += (_, _) => SendPointerBoundary(platform, owner, kind);
        target.MouseLeave += (_, _) => leaves++;
        target.MouseDown += (_, _) => downs++;
        SendCancellationButton(platform, owner, target, LibrePointerButton.Primary);
        downs.Should().Be(0);
        leaves.Should().Be(1);
        target.Capture.Should().BeFalse();
        Control.MouseButtons.Should().Be(MouseButtons.None);
    }

    [Theory]
    [InlineData(LibreInputEventKind.PointerLeave)]
    [InlineData(LibreInputEventKind.PointerCancel)]
    public void PortablePointerBoundary_ThrowingLeaveIsAlreadyRetired(LibreInputEventKind kind)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        SendReentrantPointer(platform, owner, owner, LibreInputEventKind.PointerMove);
        InvalidOperationException failure = new("native leave callback");
        int leaves = 0;
        owner.MouseLeave += (_, _) => { leaves++; throw failure; };
        Action retire = () => SendPointerBoundary(platform, owner, kind);
        retire.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        retire.Should().NotThrow();
        leaves.Should().Be(1);
    }

    [Fact]
    public void PortablePointerBoundary_CancelPreservesTheOriginalCleanupFailure()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        SendCancellationButton(platform, owner, owner, LibrePointerButton.Primary);
        InvalidOperationException captureFailure = new("capture callback");
        InvalidOperationException leaveFailure = new("leave callback");
        owner.MouseCaptureChanged += (_, _) => throw captureFailure;
        owner.MouseLeave += (_, _) => throw leaveFailure;
        Action retire = () => SendPointerBoundary(platform, owner, LibreInputEventKind.PointerCancel);
        retire.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(captureFailure);
        captureFailure.Data["PortablePointerLeaveCleanup"].Should().BeSameAs(leaveFailure);
        owner.Capture.Should().BeFalse();
        Control.MouseButtons.Should().Be(MouseButtons.None);
        retire.Should().NotThrow();
    }

    private static void SendPointerBoundary(HeadlessPlatform platform, Control root, LibreInputEventKind kind)
        => platform.SendControlInput(root, new LibreInputEvent(kind, 1,
            LibreInputModifiers.None, LibreKey.Unknown, null, default, default, LibrePointerButton.None));
}
