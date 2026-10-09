// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void FormShowDialog_RetainsSourceHandleOwnerAndFocusUntilNativeCompletion()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.NativeModalSessions = true;
        using Form owner = new();
        using Form dialog = new();
        owner.Show();
        owner.Activate();
        dialog.Shown += (_, _) => dialog.DialogResult = DialogResult.OK;
        dialog.ShowDialog(owner).Should().Be(DialogResult.OK);
        LibreHandle dialogHandle = platform.GetWindowHandle(dialog);
        platform.NativeModalBegins.Should().Be(1);
        dialog.Modal.Should().BeTrue();
        dialog.IsHandleCreated.Should().BeTrue();
        platform.IsWindowEnabled(owner).Should().BeFalse();
        platform.NativeModalReleases.Should().ContainKey(dialogHandle);
        platform.CompleteNativeModal(dialogHandle);
        dialog.Modal.Should().BeFalse();
        dialog.IsHandleCreated.Should().BeFalse();
        platform.IsWindowEnabled(owner).Should().BeTrue();
        owner.Close();
    }

    [Fact]
    public void FormShowDialog_OutOfOrderNativeCompletionCannotPopNewerSourceFrame()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.NativeModalSessions = true;
        using Form owner = new();
        using Form first = new();
        using Form second = new();
        owner.Show();
        first.Shown += (_, _) => first.DialogResult = DialogResult.OK;
        second.Shown += (_, _) => second.DialogResult = DialogResult.Cancel;
        first.ShowDialog(owner).Should().Be(DialogResult.OK);
        LibreHandle firstHandle = platform.GetWindowHandle(first);
        second.ShowDialog(owner).Should().Be(DialogResult.Cancel);
        LibreHandle secondHandle = platform.GetWindowHandle(second);
        platform.CompleteNativeModal(firstHandle);
        first.IsHandleCreated.Should().BeTrue();
        second.IsHandleCreated.Should().BeTrue();
        platform.IsWindowEnabled(owner).Should().BeFalse();
        platform.CompleteNativeModal(secondHandle);
        first.IsHandleCreated.Should().BeFalse();
        second.IsHandleCreated.Should().BeFalse();
        platform.IsWindowEnabled(owner).Should().BeTrue();
        platform.NativeModalBegins.Should().Be(2);
        owner.Close();
    }

    [Fact]
    public void FormShowDialog_ExternalOwnerRestorationAlsoWaitsForNativeCompletion()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.NativeModalSessions = true;
        nint ownerHandle = (nint)0x505729;
        platform.RegisterExternalWindowOwner(ownerHandle);
        using Form dialog = new();
        dialog.Shown += (_, _) => dialog.DialogResult = DialogResult.OK;
        dialog.ShowDialog(new ExternalWindowOwner(ownerHandle)).Should().Be(DialogResult.OK);
        platform.ExternalOwnerEnableCount.Should().Be(0);
        platform.ExternalOwnerActivateCount.Should().Be(0);
        platform.CompleteNativeModal(platform.GetWindowHandle(dialog));
        platform.ExternalOwnerEnableCount.Should().Be(1);
        platform.ExternalOwnerActivateCount.Should().Be(1);
        dialog.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void FormShowDialog_DelayedRestoreDoesNotEnableReplacementOwnerHandle()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.NativeModalSessions = true;
        using var owner = new ModalRecreatingForm();
        using Form dialog = new();
        owner.Show();
        LibreHandle originalOwner = platform.GetWindowHandle(owner);
        dialog.Shown += (_, _) => dialog.DialogResult = DialogResult.OK;
        dialog.ShowDialog(owner).Should().Be(DialogResult.OK);
        owner.ReplaceHandle();
        platform.GetWindowHandle(owner).Should().NotBe(originalOwner);
        owner.Enabled = false;
        platform.CompleteNativeModal(platform.GetWindowHandle(dialog));
        platform.IsWindowEnabled(owner).Should().BeFalse();
        dialog.IsHandleCreated.Should().BeFalse();
        owner.Close();
    }

    [Fact]
    public void FormShowDialog_DelayedCleanupCannotDestroyReplacementDialogHandle()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.NativeModalSessions = true;
        using var dialog = new ModalRecreatingForm();
        dialog.Shown += (_, _) => dialog.DialogResult = DialogResult.OK;
        dialog.ShowDialog().Should().Be(DialogResult.OK);
        LibreHandle original = platform.GetWindowHandle(dialog);
        dialog.ReplaceHandle();
        LibreHandle replacement = platform.GetWindowHandle(dialog);
        replacement.Should().NotBe(original);
        Action complete = () => platform.CompleteNativeModal(original);
        complete.Should().Throw<InvalidOperationException>().WithMessage("*replacement source handle*");
        dialog.IsHandleCreated.Should().BeTrue();
        platform.GetWindowHandle(dialog).Should().Be(replacement);
    }

    private sealed class ModalRecreatingForm : Form
    {
        internal void ReplaceHandle() => RecreateHandle();
    }
}
