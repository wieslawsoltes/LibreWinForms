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
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void NativePopupCallbackFailureStillCompletesOwnerTransition(int transition, bool destroyed)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        LibreHandle ownerHandle = platform.GetWindowHandle(owner);
        platform.Handles.TryGet(ownerHandle, out ILibreWindow? nativeOwner).Should().BeTrue();
        using ContextMenuStrip first = new() { AutoClose = false };
        using ContextMenuStrip second = new() { AutoClose = false };
        first.Items.Add("Open");
        second.Items.Add("Open");
        first.Show(owner, Point.Empty);
        second.Show(owner, Point.Empty);
        LibreHandle firstHandle = platform.GetWindowHandle(first);
        LibreHandle secondHandle = platform.GetWindowHandle(second);
        var failure = new InvalidOperationException("popup callback failed");
        if (destroyed)
            first.HandleDestroyed += (_, _) => throw failure;
        else
            first.Closed += (_, _) => throw failure;
        int secondClosed = 0;
        second.Closed += (_, _) => secondClosed++;

        Action change = () => ChangeNativePopupOwner(owner, transition);
        change.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);

        first.Visible.Should().BeFalse();
        second.Visible.Should().BeFalse();
        first.IsHandleCreated.Should().BeFalse();
        second.IsHandleCreated.Should().BeFalse();
        platform.Handles.TryGet(firstHandle, out ILibreWindow? _).Should().BeFalse();
        platform.Handles.TryGet(secondHandle, out ILibreWindow? _).Should().BeFalse();
        secondClosed.Should().Be(1);
        if (transition == 1)
        {
            owner.WindowState.Should().Be(FormWindowState.Minimized);
            nativeOwner!.State.Should().Be(LibreWindowState.Minimized);
        }
        else
        {
            nativeOwner!.Visible.Should().BeFalse();
            if (transition == 0)
                owner.Visible.Should().BeFalse("a committed native hide must not roll source visibility back");
            else
            {
                owner.IsDisposed.Should().BeTrue();
                owner.IsHandleCreated.Should().BeFalse();
                platform.Handles.TryGet(ownerHandle, out ILibreWindow? _).Should().BeFalse();
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NativePopupClosedCannotBindANewPopupDuringOwnerTransition(int transition)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new() { AutoClose = false };
        using ContextMenuStrip replacement = new() { AutoClose = false };
        menu.Items.Add("Open");
        replacement.Items.Add("Open");
        menu.Show(owner, Point.Empty);
        int attempts = 0;
        menu.Closed += (_, _) =>
        {
            attempts++;
            Action show = () => replacement.Show(owner, Point.Empty);
            show.Should().Throw<InvalidOperationException>();
        };

        ChangeNativePopupOwner(owner, transition);

        attempts.Should().Be(1);
        menu.IsHandleCreated.Should().BeFalse();
        replacement.Visible.Should().BeFalse();
        if (replacement.IsHandleCreated)
        {
            platform.IsWindowVisible(replacement).Should().BeFalse();
            platform.GetWindowOwner(replacement).IsNull.Should().BeTrue();
        }

        if (transition != 2)
        {
            owner.WindowState = FormWindowState.Normal;
            owner.Show();
            replacement.Show(owner, Point.Empty);
            replacement.Visible.Should().BeTrue("the completed transition must release its admission guard");
            platform.GetWindowOwner(replacement).Should().Be(platform.GetWindowHandle(owner));
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void NativePopupCallbackCanDisposeOwnerWithoutTouchingItsRetiredWindow(int transition, bool destroyed)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        LibreHandle ownerHandle = platform.GetWindowHandle(owner);
        using ContextMenuStrip menu = new() { AutoClose = false };
        menu.Items.Add("Open");
        menu.Show(owner, Point.Empty);
        LibreHandle menuHandle = platform.GetWindowHandle(menu);
        int callbacks = 0;
        void DisposeOwner()
        {
            callbacks++;
            callbacks.Should().Be(1, "destruction callbacks must not recursively destroy the same popup");
            owner.Dispose();
        }

        if (destroyed)
            menu.HandleDestroyed += (_, _) => DisposeOwner();
        else
            menu.Closed += (_, _) => DisposeOwner();

        Action change = () => ChangeNativePopupOwner(owner, transition);
        change.Should().NotThrow();

        callbacks.Should().Be(1);
        owner.IsDisposed.Should().BeTrue();
        owner.IsHandleCreated.Should().BeFalse();
        menu.Visible.Should().BeFalse();
        menu.IsHandleCreated.Should().BeFalse();
        platform.Handles.TryGet(ownerHandle, out ILibreWindow? _).Should().BeFalse();
        platform.Handles.TryGet(menuHandle, out ILibreWindow? _).Should().BeFalse();
    }

    [Fact]
    public void EveryNativePopupIsReleasedWhileTheFirstCallbackExceptionIsPreserved()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip first = new() { AutoClose = false };
        using ContextMenuStrip second = new() { AutoClose = false };
        first.Items.Add("Open");
        second.Items.Add("Open");
        first.Show(owner, Point.Empty);
        second.Show(owner, Point.Empty);
        Exception? observedFirst = null;
        int callbacks = 0;
        void Fail(string message)
        {
            callbacks++;
            var failure = new InvalidOperationException(message);
            observedFirst ??= failure;
            throw failure;
        }

        first.Closed += (_, _) => Fail("first popup");
        second.Closed += (_, _) => Fail("second popup");
        Action hide = owner.Hide;
        hide.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(observedFirst);

        callbacks.Should().Be(2);
        first.IsHandleCreated.Should().BeFalse();
        second.IsHandleCreated.Should().BeFalse();
        owner.Visible.Should().BeFalse();
        platform.IsWindowVisible(owner).Should().BeFalse();
    }

    [Fact]
    public void NativeMinimizeNotificationKeepsCommittedStateWhenPopupCallbackThrows()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.Handles.TryGet(platform.GetWindowHandle(owner), out ILibreWindow? nativeOwner).Should().BeTrue();
        using ContextMenuStrip menu = new() { AutoClose = false };
        menu.Items.Add("Open");
        menu.Show(owner, Point.Empty);
        var failure = new InvalidOperationException("native minimize popup callback failed");
        menu.Closed += (_, _) => throw failure;

        Action minimize = () => nativeOwner!.State = LibreWindowState.Minimized;
        minimize.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);

        owner.WindowState.Should().Be(FormWindowState.Minimized);
        nativeOwner!.State.Should().Be(LibreWindowState.Minimized);
        menu.Visible.Should().BeFalse();
        menu.IsHandleCreated.Should().BeFalse();
    }

    private static void ChangeNativePopupOwner(Form owner, int transition)
    {
        if (transition == 0)
            owner.Hide();
        else if (transition == 1)
            owner.WindowState = FormWindowState.Minimized;
        else
            owner.Dispose();
    }
}
