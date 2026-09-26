// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class NativePopupAdmissionTests
{
    private static readonly LibreHandle s_first = new((nint)11, LibreHandleKind.Window);
    private static readonly LibreHandle s_second = new((nint)12, LibreHandleKind.Window);

    [Theory]
    [InlineData(NativeWindowKind.Win32, LibreWindowZOrder.Front, true)]
    [InlineData(NativeWindowKind.X11, LibreWindowZOrder.Front, false)]
    [InlineData(NativeWindowKind.X11, LibreWindowZOrder.Back, false)]
    [InlineData(NativeWindowKind.Cocoa, LibreWindowZOrder.Front, false)]
    [InlineData(NativeWindowKind.Cocoa, LibreWindowZOrder.Back, false)]
    public void PopupOrderingUsesOnlyTheNonactivatingPlatformOperation(
        NativeWindowKind kind, LibreWindowZOrder order, bool usesTopmostOperation)
        => NativePopupAdmission.RequiresNonactivatingFront(kind, order).Should().Be(usesTopmostOperation);

    [Fact]
    public void PopupWin32BackRejectsTheExistingActivatingOperation()
    {
        Action order = () => NativePopupAdmission.RequiresNonactivatingFront(NativeWindowKind.Win32, LibreWindowZOrder.Back);
        order.Should().Throw<PlatformNotSupportedException>();
    }

    [Fact]
    public void HiddenOwnerlessStagingDoesNotConfigureOrShowUntilRealOwnerArrives()
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        admission.SetOwner(default);
        host.Operations.Should().Equal("verify");
        host.Visible.Should().BeFalse();
        admission.SetOwner(s_first);
        admission.Owner.Should().Be(s_first);
        host.PrepareCount.Should().Be(1);
        host.Visible.Should().BeFalse();
        admission.Show();
        host.Visible.Should().BeTrue();
        host.ShowCount.Should().Be(1);
        host.DiscardCount.Should().Be(0);
    }

    [Fact]
    public void ShowWithoutOwnerRejectsAndDiscardsTheHiddenSurface()
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        Action show = admission.Show;
        show.Should().Throw<InvalidOperationException>();
        host.DiscardCount.Should().Be(1);
        host.ShowCount.Should().Be(0);
        host.HandleReleased.Should().BeTrue();
        host.ClosedCount.Should().Be(1);
    }

    [Fact]
    public void LiveOwnerIsResolvedAgainBeforeEveryShow()
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        admission.SetOwner(s_first);
        host.LiveOwner = false;
        Action show = admission.Show;
        show.Should().Throw<InvalidOperationException>();
        host.ShowCount.Should().Be(0);
        host.DiscardCount.Should().Be(1);
    }

    [Fact]
    public void UnknownOwnerDoesNotMutateTheExistingPreparedOwner()
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        admission.SetOwner(s_first);
        host.LiveOwner = false;
        Action change = () => admission.SetOwner(s_second);
        change.Should().Throw<ArgumentException>();
        admission.Owner.Should().Be(s_first);
        host.PrepareCount.Should().Be(1);
        host.DiscardCount.Should().Be(0);
    }

    [Fact]
    public void VisibleOwnerChangeRejectsBeforeMutationButHiddenReopenAcceptsIt()
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        admission.SetOwner(s_first);
        admission.Show();
        Action change = () => admission.SetOwner(s_second);
        change.Should().Throw<InvalidOperationException>();
        admission.Owner.Should().Be(s_first);
        host.PrepareCount.Should().Be(1);
        host.Visible.Should().BeTrue();
        host.DiscardCount.Should().Be(0);
        host.Visible = false;
        change();
        admission.Owner.Should().Be(s_second);
        host.PrepareCount.Should().Be(2);
        admission.Show();
        host.ShownOwner.Handle.Should().Be(s_second.Value);
    }

    [Fact]
    public void SameOwnerAndAlreadyVisibleShowAreNoOps()
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        admission.SetOwner(s_first);
        host.DuringShow = () => admission.SetOwner(s_first);
        admission.Show();
        admission.SetOwner(s_first);
        admission.Show();
        host.PrepareCount.Should().Be(1);
        host.ShowCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativePrepareFailureNeverPublishesTheReplacementOwner(bool throws)
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        admission.SetOwner(s_first);
        var failure = new InvalidOperationException("native preparation callback");
        host.PrepareAccepted = false;
        if (throws) host.DuringPrepare = () => throw failure;
        Action change = () => admission.SetOwner(s_second);
        if (throws)
            change.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        else
            change.Should().Throw<PlatformNotSupportedException>();
        admission.Owner.Should().Be(s_first);
        host.DiscardCount.Should().Be(1);
        host.HandleReleased.Should().BeTrue();
        host.ClosedCount.Should().Be(1);
    }

    [Fact]
    public void PreparationCannotPublishASurfaceThatBecameVisible()
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        host.DuringPrepare = () => host.Visible = true;
        Action prepare = () => admission.SetOwner(s_first);
        prepare.Should().Throw<InvalidOperationException>();
        admission.Owner.IsNull.Should().BeTrue();
        host.DiscardCount.Should().Be(1);
        host.Visible.Should().BeFalse();
    }

    [Fact]
    public void ReentrantOwnerReplacementIsRejectedWithoutPublishingEitherOwner()
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        host.DuringPrepare = () => admission.SetOwner(s_second);
        Action prepare = () => admission.SetOwner(s_first);
        prepare.Should().Throw<InvalidOperationException>();
        admission.Owner.IsNull.Should().BeTrue();
        host.DiscardCount.Should().Be(1);
        host.PrepareCount.Should().Be(1);
    }

    [Fact]
    public void OwnerReleaseIsPreparedBeforePublishingTheOwnerlessState()
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        admission.SetOwner(s_first);
        admission.SetOwner(default);
        admission.Owner.IsNull.Should().BeTrue();
        host.ClearCount.Should().Be(1);
        host.DiscardCount.Should().Be(0);
    }

    [Fact]
    public void FailedOwnerReleaseDiscardsWithoutPublishingTheOwnerlessState()
    {
        Host host = new() { ClearAccepted = false };
        NativePopupAdmission admission = new(host);
        admission.SetOwner(s_first);
        Action clear = () => admission.SetOwner(default);
        clear.Should().Throw<PlatformNotSupportedException>();
        admission.Owner.Should().Be(s_first);
        host.DiscardCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedShowDiscardsWithoutReplacingTheOriginalException(bool throws)
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        admission.SetOwner(s_first);
        var failure = new InvalidOperationException("show callback");
        host.ShowAccepted = false;
        if (throws) host.DuringShow = () => throw failure;
        Action show = admission.Show;
        if (throws)
            show.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        else
            show.Should().Throw<PlatformNotSupportedException>();
        host.DiscardCount.Should().Be(1);
        host.Visible.Should().BeFalse();
    }

    [Fact]
    public void CleanupCallbackFailureIsRetainedWithoutMaskingAdmissionFailure()
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        admission.SetOwner(s_first);
        var failure = new InvalidOperationException("show failed");
        var cleanupFailure = new InvalidOperationException("Closed handler failed");
        host.DuringShow = () => throw failure;
        host.AfterDiscard = () => throw cleanupFailure;
        Action show = admission.Show;
        show.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        failure.Data[nameof(NativePopupAdmission)].Should().BeSameAs(cleanupFailure);
        host.HandleReleased.Should().BeTrue();
        host.ClosedCount.Should().Be(1);
    }

    [Fact]
    public void ShowRunsOnlyThroughTheNonactivatingCallbackInsideNativeAdmission()
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        admission.SetOwner(s_first);
        host.Operations.Clear();
        admission.Show();
        host.Operations.Should().Equal("verify", "resolve", "native-show", "nonactivating-show", "native-shown", "verify");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignThreadFailsBeforeAnyNativeOperation(bool show)
    {
        Host host = new();
        NativePopupAdmission admission = new(host);
        host.AccessAllowed = false;
        Action operation = show ? admission.Show : () => admission.SetOwner(s_first);
        operation.Should().Throw<InvalidOperationException>();
        host.Operations.Should().Equal("verify");
        host.DiscardCount.Should().Be(0);
    }

    private sealed class Host : INativePopupAdmissionHost
    {
        internal List<string> Operations { get; } = [];
        public bool Visible { get; set; }
        internal bool LiveOwner { get; set; } = true;
        internal bool AccessAllowed { get; set; } = true;
        internal bool PrepareAccepted { get; set; } = true;
        internal bool ClearAccepted { get; set; } = true;
        internal bool ShowAccepted { get; set; } = true;
        internal bool HandleReleased { get; private set; }
        internal int PrepareCount { get; private set; }
        internal int ClearCount { get; private set; }
        internal int ShowCount { get; private set; }
        internal int DiscardCount { get; private set; }
        internal int ClosedCount { get; private set; }
        internal NativeWindowHandle ShownOwner { get; private set; }
        internal Action? DuringPrepare { get; set; }
        internal Action? DuringShow { get; set; }
        internal Action? AfterDiscard { get; set; }

        public void VerifyAccess()
        {
            Operations.Add("verify");
            ObjectDisposedException.ThrowIf(HandleReleased, this);
            if (!AccessAllowed) throw new InvalidOperationException("Wrong dispatcher thread.");
        }

        public bool TryResolveOwner(LibreHandle owner, out NativeWindowHandle nativeOwner)
        {
            Operations.Add("resolve");
            nativeOwner = new NativeWindowHandle(NativeWindowKind.Win32, owner.Value, 0, "HWND");
            return LiveOwner;
        }

        public bool PrepareOwner(NativeWindowHandle owner)
        {
            _ = owner;
            PrepareCount++;
            DuringPrepare?.Invoke();
            return PrepareAccepted;
        }

        public bool ClearOwner()
        {
            ClearCount++;
            return ClearAccepted;
        }

        public bool ShowOwned(NativeWindowHandle owner, Action showWithoutActivation)
        {
            Operations.Add("native-show");
            ShownOwner = owner;
            ShowCount++;
            DuringShow?.Invoke();
            if (!ShowAccepted) return false;
            showWithoutActivation();
            Operations.Add("native-shown");
            return true;
        }

        public void ShowWithoutActivation()
        {
            Operations.Add("nonactivating-show");
            Visible = true;
        }

        public void Discard()
        {
            DiscardCount++;
            Visible = false;
            HandleReleased = true;
            ClosedCount++;
            AfterDiscard?.Invoke();
        }
    }
}
