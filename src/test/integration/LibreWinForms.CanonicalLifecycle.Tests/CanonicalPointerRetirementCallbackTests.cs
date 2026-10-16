// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
    public void PortablePointerRetirementCallback_HidePreservesReplacementPress(bool sameWindow)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false, ClientSize = new(160, 100) };
        using Form other = new() { ShowIcon = false, ClientSize = new(160, 100) };
        using Panel target = new() { Bounds = new(10, 10, 80, 60) };
        using Panel replacement = new() { Bounds = new(10, 10, 80, 60) };
        owner.Controls.Add(target);
        other.Controls.Add(replacement);
        owner.Show(); other.Show();
        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        bool entered = false;
        target.MouseCaptureChanged += (_, _) =>
        {
            if (entered) return;
            entered = true;
            if (sameWindow) owner.Show();
            SendReentrantPointer(platform, sameWindow ? owner : other,
                sameWindow ? target : replacement, LibreInputEventKind.PointerDown);
        };

        owner.Hide();
        entered.Should().BeTrue();
        Control.MouseButtons.Should().Be(MouseButtons.Left);
        (sameWindow ? target : replacement).Capture.Should().BeTrue();
        SendReentrantPointer(platform, sameWindow ? owner : other,
            sameWindow ? target : replacement, LibreInputEventKind.PointerUp);
        Control.MouseButtons.Should().Be(MouseButtons.None);
    }

    [Fact]
    public void PortablePointerRetirementCallback_NewHandleSurvivesOldDestroy()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using RetiringInputForm owner = new() { ShowIcon = false, ClientSize = new(160, 100) };
        using Panel target = new() { Bounds = new(10, 10, 80, 60) };
        owner.Controls.Add(target);
        owner.Show();
        nint oldHandle = owner.Handle;
        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        bool entered = false;
        target.MouseCaptureChanged += (_, _) =>
        {
            if (entered) return;
            entered = true;
            owner.RecreateNativeHandle();
            SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        };

        owner.DestroyNativeHandle();
        entered.Should().BeTrue();
        owner.IsHandleCreated.Should().BeTrue();
        owner.Handle.Should().NotBe(oldHandle);
        Control.MouseButtons.Should().Be(MouseButtons.Left);
        target.Capture.Should().BeTrue();
        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerUp);
    }

    [Fact]
    public void PortablePointerRetirementCallback_FailureStillReleasesOriginalHandle()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using RetiringInputForm owner = new() { ShowIcon = false, ClientSize = new(160, 100) };
        using Panel target = new() { Bounds = new(10, 10, 80, 60) };
        owner.Controls.Add(target);
        owner.Show();
        SendReentrantPointer(platform, owner, target, LibreInputEventKind.PointerDown);
        var primary = new InvalidOperationException("capture retirement");
        var cleanup = new InvalidOperationException("handle notification");
        target.MouseCaptureChanged += (_, _) => throw primary;
        owner.HandleDestroyed += (_, _) => throw cleanup;
        Action destroy = owner.DestroyNativeHandle;
        destroy.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(primary);
        primary.Data["PortableHandleDestroyedCleanup"].Should().BeSameAs(cleanup);
        owner.IsHandleCreated.Should().BeFalse();
        target.Capture.Should().BeFalse();
        Control.MouseButtons.Should().Be(MouseButtons.None);
    }

    private sealed class RetiringInputForm : Form
    {
        internal void DestroyNativeHandle() => DestroyHandle();
        internal void RecreateNativeHandle() => RecreateHandle();
    }
}
