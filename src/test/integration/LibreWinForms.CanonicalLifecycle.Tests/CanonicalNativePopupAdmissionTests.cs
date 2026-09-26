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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativePopupOwnerAdmissionRejectsOwnerLossDuringTheSetter(bool disposeOwner, bool callbackThrows)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        using Form replacement = new() { ShowIcon = false };
        owner.Show();
        replacement.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        _ = menu.Handle;
        LibreHandle oldHandle = platform.GetWindowHandle(menu);
        int opened = 0;
        int destroyed = 0;
        menu.Opened += (_, _) => opened++;
        menu.HandleDestroyed += (_, _) => destroyed++;
        var failure = new InvalidOperationException("owner assignment callback failed");
        platform.PopupOwnerAssigned = () =>
        {
            if (disposeOwner)
                owner.Dispose();
            else
                owner.Hide();
            if (callbackThrows)
                throw failure;
        };

        Action show = () => menu.Show(owner, Point.Empty);
        InvalidOperationException actual = show.Should().Throw<InvalidOperationException>().Which;
        if (callbackThrows)
            actual.Should().BeSameAs(failure);
        menu.Visible.Should().BeFalse();
        menu.IsHandleCreated.Should().BeFalse();
        menu.IsDisposed.Should().BeFalse();
        platform.Handles.TryGet(oldHandle, out ILibreWindow? retired).Should().BeFalse();
        retired.Should().BeNull();
        opened.Should().Be(0);
        destroyed.Should().Be(1);

        platform.PopupOwnerAssigned = null;
        menu.Show(replacement, Point.Empty);
        menu.Visible.Should().BeTrue();
        platform.IsWindowVisible(menu).Should().BeTrue();
        platform.GetWindowHandle(menu).Should().NotBe(oldHandle);
        platform.GetWindowOwner(menu).Should().Be(platform.GetWindowHandle(replacement));
        opened.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedNativePopupShowDoesNotPublishOpenedAndRetainsReusableSource(bool precreated)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        LibreHandle rejectedHandle = default;
        menu.HandleCreated += (_, _) => rejectedHandle = platform.GetWindowHandle(menu);
        if (precreated)
            _ = menu.Handle;

        List<string> callbacks = [];
        bool rejecting = true;
        menu.HandleDestroyed += (_, _) => callbacks.Add("destroyed");
        menu.Closed += (_, _) =>
        {
            callbacks.Add("closed");
            if (rejecting)
            {
                menu.Visible.Should().BeFalse();
                menu.IsHandleCreated.Should().BeFalse();
            }
        };
        menu.Opened += (_, _) =>
        {
            callbacks.Add("opened");
            if (rejecting)
                throw new InvalidOperationException("A rejected native popup must not raise Opened.");
        };
        var failure = new PlatformNotSupportedException("native popup Show admission failed");
        platform.PopupShowFailure = failure;
        Action show = () => menu.Show(owner, Point.Empty);
        show.Should().Throw<PlatformNotSupportedException>().Which.Should().BeSameAs(failure);
        rejectedHandle.IsNull.Should().BeFalse();
        LibreHandle oldHandle = rejectedHandle;
        callbacks.Should().Equal("destroyed", "closed");
        menu.Visible.Should().BeFalse();
        menu.IsHandleCreated.Should().BeFalse();
        menu.IsDisposed.Should().BeFalse();
        platform.Handles.TryGet(oldHandle, out ILibreWindow? retired).Should().BeFalse();
        retired.Should().BeNull();

        // The failed generation must no longer remain in the owner's popup
        // lifetime set, and the reusable source object must admit a fresh one.
        owner.Hide();
        owner.Show();
        callbacks.Should().Equal("destroyed", "closed");
        platform.PopupShowFailure = null;
        rejecting = false;
        show();
        menu.Visible.Should().BeTrue();
        menu.IsHandleCreated.Should().BeTrue();
        platform.IsWindowVisible(menu).Should().BeTrue();
        platform.GetWindowHandle(menu).Should().NotBe(oldHandle);
        platform.GetWindowOwner(menu).Should().Be(platform.GetWindowHandle(owner));
        callbacks.Should().Equal("destroyed", "closed", "opened");
    }

    [Fact]
    public void AdmittedNativePopupRetainsOpenedAfterManagedVisibilityCallbackFailure()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        var failure = new InvalidOperationException("managed visibility callback failed");
        menu.VisibleChanged += (_, _) =>
        {
            if (menu.Visible)
                throw failure;
        };
        int opened = 0;
        menu.Opened += (_, _) => opened++;
        Action show = () => menu.Show(owner, Point.Empty);
        show.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        menu.Visible.Should().BeTrue();
        menu.IsHandleCreated.Should().BeTrue();
        platform.IsWindowVisible(menu).Should().BeTrue();
        opened.Should().Be(1);
    }
}
