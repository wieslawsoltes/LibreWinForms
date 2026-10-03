// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Xunit;

namespace LibreWinForms.Platform.Tests;

// These controls exercise the actual managed service's additive handshake using
// its existing typed window double. A recorded source-release callback is not
// evidence that AppKit, a native provider, or the source modal stack completed.
public partial class ManagedLibreMessageBoxServiceTests
{
    [Fact]
    public void ModalShow_BeginsWithExactShownActivatedWindowBeforeNestedInput()
    {
        using var host = new MessageBoxHost { CreateModalWindow = true };
        var lifecycle = new RecordingMessageBoxLifecycle(host);
        lifecycle.OnBegin = () =>
        {
            lifecycle.RegistrationCount.Should().Be(1);
            lifecycle.BegunWindow.Should().BeSameAs(host.Window);
            host.WindowShown.Should().BeTrue();
            host.WindowActivated.Should().BeTrue();
            host.RunNestedCount.Should().Be(0);
            host.WindowDisposeAttempts.Should().Be(0);
        };
        host.BeforeRunNested = () =>
        {
            lifecycle.BeginCount.Should().Be(1);
            host.ModalBeginCount.Should().Be(1);
            lifecycle.ModalBegan.Should().BeTrue();
        };
        host.EnqueueInput(MessageBoxKey(LibreKey.Enter));

        ILibreModalMessageBoxService service = host.CreateService();
        service.Show(CreateRequest(LibreMessageBoxButtons.OK), lifecycle)
            .Should().Be(LibreMessageBoxResult.OK);

        host.Operations.Should().ContainInOrder(
            "register-cleanup", "create", "show", "activate", "begin", "window-modal-begin", "run");
        host.WindowCloseCount.Should().Be(0);
        host.WindowDisposeAttempts.Should().Be(0);
        host.Window.Visible.Should().BeTrue();
        host.Handles.Count.Should().Be(1);

        lifecycle.CompleteSourceRelease();

        host.WindowDisposeCount.Should().Be(1);
        host.Handles.Count.Should().Be(0);
        host.Operations.Should().ContainInOrder("source-release", "dispose");
    }

    [Fact]
    public void ModalShow_AcceptsChromeCloseWithoutAllowingProviderToDisposeBeforeSourceRelease()
    {
        using var host = new MessageBoxHost { CreateModalWindow = true };
        var lifecycle = new RecordingMessageBoxLifecycle(host);
        host.EnqueueCloseAttempt();

        host.CreateService().Show(CreateRequest(LibreMessageBoxButtons.OKCancel), lifecycle)
            .Should().Be(LibreMessageBoxResult.Cancel);

        // Closing's false prevents provider teardown; the managed session has
        // nevertheless accepted Cancel and stopped its nested input loop.
        host.CloseWasRejected.Should().BeTrue();
        host.WindowCloseCount.Should().Be(0);
        host.WindowDisposeAttempts.Should().Be(0);
        host.Window.Visible.Should().BeTrue();
        host.Handles.Count.Should().Be(1);

        lifecycle.CompleteSourceRelease();

        host.WindowDisposeCount.Should().Be(1);
        host.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void ModalShow_ResultRetainsWindowButRejectsFurtherSessionInput()
    {
        using var host = new MessageBoxHost { CreateModalWindow = true };
        var lifecycle = new RecordingMessageBoxLifecycle(host);
        host.EnqueueInput(MessageBoxKey(LibreKey.Enter));

        host.CreateService().Show(CreateRequest(LibreMessageBoxButtons.YesNo), lifecycle)
            .Should().Be(LibreMessageBoxResult.Yes);
        int paintsAtResult = host.PaintCount;

        host.SendInput(MessageBoxKey(LibreKey.Right));
        host.SendInput(MessageBoxKey(LibreKey.Enter));

        host.PaintCount.Should().Be(paintsAtResult);
        host.WindowCloseCount.Should().Be(0);
        host.WindowDisposeAttempts.Should().Be(0);
        host.Handles.Count.Should().Be(1);

        lifecycle.CompleteSourceRelease();
        host.WindowDisposeCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModalShow_AbsentOrDisabledWindowCapabilityPreservesSourceCleanupHandshake(bool hasCapability)
    {
        using var host = new MessageBoxHost { CreateModalWindow = hasCapability, ModalPolicyEnabled = false };
        var lifecycle = new RecordingMessageBoxLifecycle(host);
        host.EnqueueInput(MessageBoxKey(LibreKey.Enter));

        host.CreateService().Show(CreateRequest(LibreMessageBoxButtons.OK), lifecycle)
            .Should().Be(LibreMessageBoxResult.OK);

        lifecycle.BeginCount.Should().Be(1);
        lifecycle.ModalBegan.Should().BeFalse();
        if (hasCapability)
        {
            lifecycle.BegunWindow.Should().BeSameAs(host.Window);
            host.ModalBeginCount.Should().Be(1);
        }
        else
        {
            lifecycle.BegunWindow.Should().BeNull();
            host.ModalBeginCount.Should().Be(0);
        }

        host.WindowCloseCount.Should().Be(0);
        host.WindowDisposeAttempts.Should().Be(0);
        lifecycle.CompleteSourceRelease();
        host.WindowDisposeCount.Should().Be(1);
        host.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void ModalShow_BeginFailureRetainsExactWindowUntilSourceRelease()
    {
        using var host = new MessageBoxHost { CreateModalWindow = true };
        var expected = new InvalidOperationException("Recorded source Begin failure.");
        var lifecycle = new RecordingMessageBoxLifecycle(host) { OnBegin = () => throw expected };

        Action show = () => host.CreateService().Show(CreateRequest(LibreMessageBoxButtons.OKCancel), lifecycle);

        show.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
        lifecycle.RegistrationCount.Should().Be(1);
        lifecycle.BegunWindow.Should().BeSameAs(host.Window);
        host.RunNestedCount.Should().Be(0);
        host.WindowCloseCount.Should().Be(0);
        host.WindowDisposeAttempts.Should().Be(0);
        host.Handles.Count.Should().Be(1);

        int paintsAtFailure = host.PaintCount;
        host.SendInput(MessageBoxKey(LibreKey.Right));
        host.SendInput(MessageBoxKey(LibreKey.Enter));
        host.PaintCount.Should().Be(paintsAtFailure);
        host.WindowCloseCount.Should().Be(0);
        host.WindowDisposeAttempts.Should().Be(0);

        lifecycle.CompleteSourceRelease();

        host.WindowDisposeCount.Should().Be(1);
        host.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void ModalShow_FailedWindowCleanupIsRetainedForOneDispatcherRetryWithoutRepeatingSuccess()
    {
        using var host = new MessageBoxHost { CreateModalWindow = true, RemainingDisposeFailures = 1 };
        var lifecycle = new RecordingMessageBoxLifecycle(host);
        host.EnqueueInput(MessageBoxKey(LibreKey.Enter));
        host.CreateService().Show(CreateRequest(LibreMessageBoxButtons.OK), lifecycle)
            .Should().Be(LibreMessageBoxResult.OK);
        ILibreWindow exactWindow = host.Window;

        Action cleanup = lifecycle.CompleteSourceRelease;
        cleanup.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(host.DisposeFailure);

        host.Window.Should().BeSameAs(exactWindow);
        host.WindowDisposeAttempts.Should().Be(1);
        host.WindowDisposeCount.Should().Be(0);
        host.Handles.Count.Should().Be(1);
        host.PostedCallbackCount.Should().Be(1);

        host.PumpOnce();

        host.Window.Should().BeSameAs(exactWindow);
        host.WindowDisposeAttempts.Should().Be(2);
        host.WindowDisposeCount.Should().Be(1);
        host.Handles.Count.Should().Be(0);
        host.PostedCallbackCount.Should().Be(1);

        lifecycle.CompleteSourceRelease();
        host.PumpOnce();
        host.WindowDisposeAttempts.Should().Be(2);
        host.WindowDisposeCount.Should().Be(1);
        host.DisposeThreadIds.Should().OnlyContain(id => id == host.ManagedThreadId);
    }

    [Fact]
    public void ModalShow_FailedCleanupOfTwoSessionsSharesOneDispatcherRetry()
    {
        using var host = new MessageBoxHost { CreateModalWindow = true, RemainingDisposeFailures = 3 };
        ManagedLibreMessageBoxService service = host.CreateService();
        var firstLifecycle = new RecordingMessageBoxLifecycle(host);
        host.EnqueueInput(MessageBoxKey(LibreKey.Enter));
        service.Show(CreateRequest(LibreMessageBoxButtons.OK), firstLifecycle)
            .Should().Be(LibreMessageBoxResult.OK);
        ILibreWindow firstWindow = host.Window;
        var secondLifecycle = new RecordingMessageBoxLifecycle(host);
        host.EnqueueInput(MessageBoxKey(LibreKey.Enter));
        service.Show(CreateRequest(LibreMessageBoxButtons.OK), secondLifecycle)
            .Should().Be(LibreMessageBoxResult.OK);
        ILibreWindow secondWindow = host.Window;
        secondWindow.Should().NotBeSameAs(firstWindow);

        Action firstCleanup = firstLifecycle.CompleteSourceRelease;
        firstCleanup.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(host.DisposeFailure);
        Action secondCleanup = secondLifecycle.CompleteSourceRelease;
        secondCleanup.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(host.DisposeFailure);

        // The second retirement retries the first pending entry once as well
        // as its own entry. All three failures still share one posted drain.
        host.WindowDisposeAttempts.Should().Be(3);
        host.WindowDisposeCount.Should().Be(0);
        host.Handles.Count.Should().Be(2);
        host.PostedCallbackCount.Should().Be(1);

        host.PumpOnce();

        host.WindowDisposeAttempts.Should().Be(5);
        host.WindowDisposeCount.Should().Be(2);
        host.Handles.Count.Should().Be(0);
        host.PostedCallbackCount.Should().Be(1);
        host.DisposeThreadIds.Should().OnlyContain(id => id == host.ManagedThreadId);
    }

    [Fact]
    public void ModalShow_SourceFailurePrecedesSeparatelyReportedRetryableCleanupFailure()
    {
        using var host = new MessageBoxHost { CreateModalWindow = true, RemainingDisposeFailures = 1 };
        var expected = new InvalidOperationException("Recorded source modal-loop failure.");
        host.BeforeRunNested = () => throw expected;
        var lifecycle = new RecordingMessageBoxLifecycle(host);
        ManagedLibreMessageBoxService service = host.CreateService();
        Action show = () => service.Show(CreateRequest(LibreMessageBoxButtons.OKCancel), lifecycle);

        show.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
        host.WindowDisposeAttempts.Should().Be(0);
        host.Handles.Count.Should().Be(1);
        int paintsAtFailure = host.PaintCount;
        host.SendInput(MessageBoxKey(LibreKey.Right));
        host.SendInput(MessageBoxKey(LibreKey.Enter));
        host.PaintCount.Should().Be(paintsAtFailure);
        host.WindowCloseCount.Should().Be(0);
        host.WindowDisposeAttempts.Should().Be(0);

        // Source owns preserving its earlier exception while completing release.
        // The Platform service must report cleanup separately and retain it; this
        // fixture does not substitute an invented source-finally implementation.
        Action cleanup = lifecycle.CompleteSourceRelease;
        cleanup.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(host.DisposeFailure);
        host.WindowDisposeAttempts.Should().Be(1);
        host.WindowDisposeCount.Should().Be(0);
        host.Handles.Count.Should().Be(1);
        host.PostedCallbackCount.Should().Be(1);

        host.PumpOnce();

        host.WindowDisposeAttempts.Should().Be(2);
        host.WindowDisposeCount.Should().Be(1);
        host.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void ModalShow_EarlyNestedReturnRetainsWindowButRejectsLaterInput()
    {
        using var host = new MessageBoxHost { CreateModalWindow = true, ReturnEarlyFromRunNested = true };
        var lifecycle = new RecordingMessageBoxLifecycle(host);

        host.CreateService().Show(CreateRequest(LibreMessageBoxButtons.OKCancel), lifecycle)
            .Should().Be(LibreMessageBoxResult.Cancel);
        int paintsAtReturn = host.PaintCount;

        host.SendInput(MessageBoxKey(LibreKey.Right));
        host.SendInput(MessageBoxKey(LibreKey.Enter));

        host.PaintCount.Should().Be(paintsAtReturn);
        host.WindowCloseCount.Should().Be(0);
        host.WindowDisposeAttempts.Should().Be(0);
        host.Handles.Count.Should().Be(1);
        lifecycle.CompleteSourceRelease();
        host.WindowDisposeCount.Should().Be(1);
        host.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void ModalShow_RejectsWrongThreadBeforeCreatingWindowOrRegisteringSourceCleanup()
    {
        using var host = new MessageBoxHost { HasDispatcherAccess = false };
        var lifecycle = new RecordingMessageBoxLifecycle(host);
        ManagedLibreMessageBoxService service = host.CreateService();
        Action show = () => service.Show(CreateRequest(LibreMessageBoxButtons.OK), lifecycle);

        show.Should().Throw<InvalidOperationException>().WithMessage("*owning dispatcher thread*");
        host.WindowCreateCount.Should().Be(0);
        lifecycle.RegistrationCount.Should().Be(0);
        lifecycle.BeginCount.Should().Be(0);
    }

    [Fact]
    public void ModalShow_RejectsYesNoChromeCloseAndRetainsOriginalButtonResultPolicy()
    {
        using var host = new MessageBoxHost { CreateModalWindow = true };
        var lifecycle = new RecordingMessageBoxLifecycle(host);
        host.EnqueueCloseAttempt();
        host.EnqueueInput(MessageBoxKey(LibreKey.Enter));

        host.CreateService().Show(CreateRequest(LibreMessageBoxButtons.YesNo), lifecycle)
            .Should().Be(LibreMessageBoxResult.Yes);

        host.LastCreateOptions.CanClose.Should().BeFalse();
        host.CloseWasRejected.Should().BeTrue();
        host.WindowCloseCount.Should().Be(0);
        host.WindowDisposeAttempts.Should().Be(0);
        lifecycle.CompleteSourceRelease();
        host.WindowDisposeCount.Should().Be(1);
    }

    private static LibreInputEvent MessageBoxKey(LibreKey key)
        => new(
            LibreInputEventKind.KeyDown,
            1,
            LibreInputModifiers.None,
            key,
            null,
            default,
            default,
            LibrePointerButton.None);

    private sealed class RecordingMessageBoxLifecycle(MessageBoxHost host) : ILibreMessageBoxModalLifecycle
    {
        private Action? _cleanup;

        internal int RegistrationCount { get; private set; }

        internal int BeginCount { get; private set; }

        internal ILibreModalWindow? BegunWindow { get; private set; }

        internal bool ModalBegan { get; private set; }

        internal Action? OnBegin { get; set; }

        public void AfterSourceRelease(Action cleanup)
        {
            host.Operations.Add("register-cleanup");
            RegistrationCount++;
            _cleanup = cleanup;
        }

        public void Begin(ILibreModalWindow? exactWindow)
        {
            host.Operations.Add("begin");
            BeginCount++;
            BegunWindow = exactWindow;
            ModalBegan = exactWindow?.BeginModalDialog() ?? false;
            OnBegin?.Invoke();
        }

        internal void CompleteSourceRelease()
        {
            host.Operations.Add("source-release");
            _cleanup.Should().NotBeNull();
            _cleanup!();
        }
    }
}
