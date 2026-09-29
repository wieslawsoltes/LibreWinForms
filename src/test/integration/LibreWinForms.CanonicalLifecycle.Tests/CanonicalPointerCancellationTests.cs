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
    [InlineData(LibrePointerButton.Primary)]
    [InlineData(LibrePointerButton.Secondary)]
    [InlineData(LibrePointerButton.Middle)]
    [InlineData(LibrePointerButton.XButton1)]
    [InlineData(LibrePointerButton.XButton2)]
    public void PortablePointerCancellation_UnfocusedPopupRetiresAllButtonKinds(LibrePointerButton button)
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
        int captures = 0, leaves = 0, releases = 0, clicks = 0, enters = 0;
        target.MouseCaptureChanged += (_, _) => captures++;
        target.MouseLeave += (_, _) => leaves++;
        target.MouseUp += (_, _) => releases++;
        target.Click += (_, _) => clicks++;
        target.MouseEnter += (_, _) => enters++;

        SendPointerLoss(platform, menu);
        SendPointerLoss(platform, menu);
        target.Capture.Should().BeFalse();
        Control.MouseButtons.Should().Be(MouseButtons.None);
        Control.MousePosition.Should().Be(position);
        captures.Should().Be(1);
        leaves.Should().Be(1);
        releases.Should().Be(0);
        clicks.Should().Be(0);
        menu.Visible.Should().BeTrue();
        Form.ActiveForm.Should().BeSameAs(owner);
        SendReentrantPointer(platform, menu, target, LibreInputEventKind.PointerMove);
        enters.Should().Be(1, "cancelled hover must enter afresh without a fabricated release");
    }

    [Theory]
    [InlineData(LibrePointerButton.Primary, MouseButtons.Left)]
    [InlineData(LibrePointerButton.Secondary, MouseButtons.Right)]
    public void PortablePointerCancellation_OldWindowCannotClearAnotherWindowsButton(
        LibrePointerButton replacementButton, MouseButtons expected)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        using Panel firstTarget = new() { Bounds = new(10, 10, 100, 60) };
        using Panel secondTarget = new() { Bounds = new(10, 10, 100, 60) };
        first.Controls.Add(firstTarget);
        second.Controls.Add(secondTarget);
        first.Show();
        second.Show();
        SendCancellationButton(platform, first, firstTarget, LibrePointerButton.Primary);
        SendCancellationButton(platform, second, secondTarget, replacementButton);
        platform.SendControlInput(second, new LibreInputEvent(LibreInputEventKind.KeyDown, 1,
            LibreInputModifiers.Control, LibreKey.LeftControl, null, default, default, LibrePointerButton.None));
        int leaves = 0, enters = 0;
        secondTarget.MouseLeave += (_, _) => leaves++;
        secondTarget.MouseEnter += (_, _) => enters++;

        SendPointerLoss(platform, first);
        firstTarget.Capture.Should().BeFalse();
        secondTarget.Capture.Should().BeTrue();
        Control.MouseButtons.Should().Be(expected);
        Control.ModifierKeys.Should().Be(Keys.Control);
        SendReentrantPointer(platform, second, secondTarget, LibreInputEventKind.PointerMove);
        leaves.Should().Be(0);
        enters.Should().Be(0);
        SendPointerLoss(platform, second);
        Control.MouseButtons.Should().Be(MouseButtons.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortablePointerCancellation_CaptureCallbackOwnsItsReplacementPress(bool sameWindow)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        using Panel firstTarget = new() { Bounds = new(10, 10, 100, 60) };
        using Panel secondTarget = new() { Bounds = new(10, 10, 100, 60) };
        first.Controls.Add(firstTarget);
        second.Controls.Add(secondTarget);
        first.Show();
        second.Show();
        SendCancellationButton(platform, first, firstTarget, LibrePointerButton.Primary);
        Form replacementRoot = sameWindow ? first : second;
        Panel replacement = sameWindow ? firstTarget : secondTarget;
        int leaves = 0, presses = 0;
        firstTarget.MouseLeave += (_, _) => leaves++;
        replacement.MouseDown += (_, _) => presses++;
        EventHandler reacquire = (_, _) => SendCancellationButton(platform, replacementRoot,
            replacement, LibrePointerButton.Primary);
        firstTarget.MouseCaptureChanged += reacquire;

        SendPointerLoss(platform, first);
        firstTarget.MouseCaptureChanged -= reacquire;
        replacement.Capture.Should().BeTrue();
        Control.MouseButtons.Should().Be(MouseButtons.Left);
        presses.Should().Be(1);
        leaves.Should().Be(0, "the old leave must not follow a replacement pointer generation");
        SendPointerLoss(platform, replacementRoot);
        replacement.Capture.Should().BeFalse();
        Control.MouseButtons.Should().Be(MouseButtons.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortablePointerCancellation_ThrowingCaptureStillRetiresHover(bool throwingLeave)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Panel target = new() { Bounds = new(10, 10, 100, 60) };
        owner.Controls.Add(target);
        owner.Show();
        SendCancellationButton(platform, owner, target, LibrePointerButton.Primary);
        InvalidOperationException captureFailure = new("capture callback");
        InvalidOperationException leaveFailure = new("leave callback");
        int leaves = 0;
        target.MouseCaptureChanged += (_, _) => throw captureFailure;
        target.MouseLeave += (_, _) =>
        {
            leaves++;
            if (throwingLeave)
                throw leaveFailure;
        };

        Action cancel = () => SendPointerLoss(platform, owner);
        cancel.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(captureFailure);
        if (throwingLeave)
            captureFailure.Data["PortablePointerLeaveCleanup"].Should().BeSameAs(leaveFailure);
        target.Capture.Should().BeFalse();
        Control.MouseButtons.Should().Be(MouseButtons.None);
        leaves.Should().Be(1);
        cancel.Should().NotThrow();
        leaves.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortablePointerCancellation_FocusCallbackRetainsTheNewOwnersKeys(bool sameWindow)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form first = new() { ShowIcon = false };
        using Form second = new() { ShowIcon = false };
        using Panel target = new() { Bounds = new(10, 10, 100, 60) };
        first.Controls.Add(target);
        first.Show();
        second.Show();
        platform.SendFormInput(first, LibreInputEventKind.FocusGained);
        SendCancellationButton(platform, first, target, LibrePointerButton.Primary);
        Form replacement = sameWindow ? first : second;
        target.MouseCaptureChanged += (_, _) =>
        {
            platform.SendFormInput(replacement, LibreInputEventKind.FocusGained);
            platform.SendControlInput(replacement, new LibreInputEvent(LibreInputEventKind.KeyDown, 1,
                LibreInputModifiers.Shift, LibreKey.LeftShift, null, default, default, LibrePointerButton.None));
        };

        SendPointerLoss(platform, first);
        Form.ActiveForm.Should().BeSameAs(replacement);
        Control.ModifierKeys.Should().Be(Keys.Shift);
        Control.MouseButtons.Should().Be(MouseButtons.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortablePointerCancellation_CallbackErrorsCannotRollBackFocusLoss(bool throwingDeactivate)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Panel target = new() { Bounds = new(10, 10, 100, 60) };
        owner.Controls.Add(target);
        owner.Show();
        platform.SendFormInput(owner, LibreInputEventKind.FocusGained);
        SendCancellationButton(platform, owner, target, LibrePointerButton.Primary);
        InvalidOperationException captureFailure = new("capture callback");
        InvalidOperationException focusFailure = new("deactivation callback");
        int deactivations = 0;
        target.MouseCaptureChanged += (_, _) => throw captureFailure;
        owner.Deactivate += (_, _) =>
        {
            deactivations++;
            if (throwingDeactivate)
                throw focusFailure;
        };

        Action cancel = () => SendPointerLoss(platform, owner);
        cancel.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(captureFailure);
        if (throwingDeactivate)
            captureFailure.Data["PortablePointerFocusCleanup"].Should().BeSameAs(focusFailure);
        Form.ActiveForm.Should().BeNull();
        deactivations.Should().Be(1);
        target.Capture.Should().BeFalse();
        Control.MouseButtons.Should().Be(MouseButtons.None);
        cancel.Should().NotThrow();
        deactivations.Should().Be(1);
    }

    private static void SendPointerLoss(HeadlessPlatform platform, Control root)
        => platform.SendControlInput(root, new LibreInputEvent(LibreInputEventKind.FocusLost, 1,
            LibreInputModifiers.None, LibreKey.Unknown, null, default, default, LibrePointerButton.None));

    private static void SendCancellationButton(HeadlessPlatform platform, Control root, Control target,
        LibrePointerButton button)
    {
        Point point = root.PointToClient(target.PointToScreen(new(4, 5)));
        target.IsHandleCreated.Should().BeTrue();
        target.ClientRectangle.Contains(4, 5).Should().BeTrue("the target must have an actual pointer hit area");
        root.ClientRectangle.Contains(point).Should().BeTrue("the target point must be inside the source window");
        platform.SendControlInput(root, new LibreInputEvent(LibreInputEventKind.PointerDown, 1,
            LibreInputModifiers.None, LibreKey.Unknown, null, new(point.X, point.Y), default, button));
    }
}
