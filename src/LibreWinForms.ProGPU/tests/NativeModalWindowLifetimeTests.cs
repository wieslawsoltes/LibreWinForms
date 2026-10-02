// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Silk.NET.Windowing;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class NativeModalWindowLifetimeTests
{
    [Theory]
    [InlineData(false, false, 1, 1)]
    [InlineData(false, true, 1, 0)]
    [InlineData(true, true, 0, 1)]
    public void ActualProviderPollSeparatesOwnedQueueFromGlobalSession(bool queueOnly, bool active, int modalPolls, int windowPolls)
    {
        var fixture = new Fixture(queueOnly);
        fixture._session._active = active;
        fixture._lifetime.Pump();
        Assert.Equal(modalPolls, fixture._session._polls);
        Assert.Equal(windowPolls, fixture.Window._polls);
    }

    [Fact]
    public void ModalPollFailureNeverFallsBackToOrdinaryPoll()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("modal poll");
        fixture._session._poll = () => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture._lifetime.Pump));
        Assert.Equal(0, fixture.Window._polls);
    }

    [Fact]
    public void HideCoalescesAndCompletionOnlyWakesCreatingThread()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Hide();
        fixture._lifetime.Hide();
        Assert.Equal(1, fixture._session._releases);
        Assert.Equal(0, fixture.Window._hides);
        fixture._session.Complete();
        Assert.Equal(1, fixture._wakes);
        Assert.Equal(0, fixture.Window._hides);
        fixture._lifetime.Pump();
        Assert.Equal(1, fixture.Window._hides);
    }

    [Fact]
    public void LaterShowInvalidatesPendingHideWithoutRepeatingRelease()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Hide();
        fixture._lifetime.BeforeShow();
        fixture._session.Complete();
        fixture._lifetime.Pump();
        Assert.Equal(0, fixture.Window._hides);
        Assert.Equal(1, fixture._session._releases);
    }

    [Fact]
    public void ShowCannotDiscardUndeliveredReleaseWhenNativeQueryDisappears()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Hide();
        fixture._session._held = false;
        Assert.Throws<InvalidOperationException>(fixture._lifetime.BeforeShow);
        fixture._lifetime.Pump();
        Assert.Equal(0, fixture.Window._hides);
        Assert.Equal(1, fixture._session._releases);
        Assert.NotNull(fixture._session._completion);
        fixture._session.Complete();
        Assert.Equal(1, fixture._wakes);
        Assert.Equal(0, fixture.Window._hides);
        fixture._lifetime.Pump();
        Assert.Equal(1, fixture.Window._hides);
        fixture._lifetime.BeforeShow();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShowRetentionReadCannotOverwriteCloseOrRetirement(bool retire)
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Hide();
        fixture._session._retains = () =>
        {
            fixture._session._retains = null;
            if (retire) fixture._lifetime.Retire();
            else fixture._lifetime.Close();
            return true;
        };
        if (retire) Assert.Throws<ObjectDisposedException>(fixture._lifetime.BeforeShow);
        else Assert.Throws<InvalidOperationException>(fixture._lifetime.BeforeShow);
        Assert.Equal(0, fixture.Window._hides);
        Assert.Equal(0, fixture.Window._closes);
        Assert.Equal(1, fixture._session._releases);
        fixture._session.Complete();
        if (retire) Assert.True(fixture._lifetime.CanRetire());
        else fixture._lifetime.Pump();
        Assert.Equal(retire ? 1 : 0, fixture.Window._hides);
        Assert.Equal(retire ? 0 : 1, fixture.Window._closes);
        Assert.Equal(1, fixture._wakes);
    }

    [Fact]
    public void ShowRetentionReadCannotOverwriteNewerHide()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Hide();
        fixture._session._retains = () =>
        {
            fixture._session._retains = null;
            fixture._lifetime.Hide();
            return true;
        };
        Assert.Throws<InvalidOperationException>(fixture._lifetime.BeforeShow);
        fixture._session.Complete();
        fixture._lifetime.Pump();
        Assert.Equal(1, fixture.Window._hides);
        Assert.Equal(2, fixture._session._releases); // Completed Hide checks release again.
    }

    [Fact]
    public void ShowRetentionReadAcceptsDeliveredCompletionWithoutNewerIntent()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Hide();
        fixture._session._retains = () =>
        {
            fixture._session._retains = null;
            fixture._session.Complete();
            return false;
        };
        fixture._lifetime.BeforeShow();
        fixture._lifetime.Pump();
        Assert.Equal(1, fixture._wakes);
        Assert.Equal(0, fixture.Window._hides);
        Assert.Equal(1, fixture._session._releases);
    }

    [Fact]
    public void ShowRetentionReadPreservesCompletionWakeFailureAndPendingHide()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Hide();
        var failure = new InvalidOperationException("completion wake");
        fixture._onWake = () => throw failure;
        fixture._session._retains = () =>
        {
            fixture._session._retains = null;
            fixture._session.Complete();
            return false;
        };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture._lifetime.BeforeShow));
        Assert.Equal(0, fixture.Window._hides);
        fixture._onWake = null;
        fixture._lifetime.Pump();
        Assert.Equal(1, fixture.Window._hides);
        fixture._lifetime.BeforeShow();
    }

    [Fact]
    public void CloseSupersedesHideAndRejectsShowUntilActualClosingReturns()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Hide();
        fixture._lifetime.Close();
        Assert.Throws<InvalidOperationException>(fixture._lifetime.BeforeShow);
        fixture._session.Complete();
        Assert.Equal(0, fixture.Window._closes);
        fixture.Window._closing = () => Assert.Throws<InvalidOperationException>(fixture._lifetime.BeforeShow);
        fixture._lifetime.Pump();
        Assert.Equal(1, fixture.Window._closes);
        Assert.Equal(0, fixture.Window._hides);
        fixture._lifetime.BeforeShow(); // The real provider may cancel Closing.
    }

    [Fact]
    public void ReentrantCloseCannotDuplicateProviderTransition()
    {
        var fixture = new Fixture();
        fixture.Window._closing = fixture._lifetime.Close;
        fixture._lifetime.Close();
        Assert.Equal(1, fixture.Window._closes);
    }

    [Fact]
    public void DisposalSupersedesPendingCloseAndRetainsRendererUntilEnd()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Close();
        fixture._lifetime.Retire();
        int rendering = 0, native = 0;
        NativeWindowRetirementQueue queue = new(window => { Assert.Same(fixture._provider, window); native++; return true; });
        queue.Retire(fixture._provider, () => rendering++, fixture._lifetime.CanRetire);
        Assert.True(queue.HasPending);
        Assert.Equal(0, rendering);
        Assert.Equal(0, native);
        Assert.Throws<ObjectDisposedException>(fixture._lifetime.BeforeShow);
        fixture._session.Complete();
        Assert.Equal(0, rendering);
        queue.Drain();
        Assert.False(queue.HasPending);
        Assert.Equal(1, rendering);
        Assert.Equal(1, native);
        Assert.Equal(1, fixture.Window._hides);
        Assert.Equal(0, fixture.Window._closes);
    }

    [Fact]
    public void AcceptedProviderCloseRetiresAlreadyHiddenWindowWithoutSecondVisibilityWrite()
    {
        var fixture = new Fixture();
        NativeWindowRetirementQueue queue = new(_ => true);
        fixture.Window._closing = () =>
        {
            fixture.Window._visible = false; // Actual owned Close's pre-Closing state.
            fixture.Window._hide = () => throw new InvalidOperationException("closing provider rejects setter");
            fixture._lifetime.Retire();
            queue.Retire(fixture._provider, canReleaseRenderingResources: fixture._lifetime.CanRetire);
            Assert.True(queue.HasPending);
        };
        fixture._lifetime.Close();
        queue.Drain();
        Assert.False(queue.HasPending);
        Assert.Equal(1, fixture.Window._closes);
        Assert.Equal(0, fixture.Window._hides);
    }

    [Fact]
    public void ReentrantVisibilityReadCannotApplyOldHideOverNewShow()
    {
        var fixture = new Fixture();
        fixture.Window._readVisible = fixture._lifetime.BeforeShow;
        fixture._lifetime.Hide();
        Assert.Equal(0, fixture.Window._hides);
    }

    [Fact]
    public void VisibilityReadAcquiringNewSessionDefersHideUntilItsOwnCompletion()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Hide();
        fixture._session.Complete();
        fixture.Window._readVisible = () =>
        {
            fixture.Window._readVisible = null;
            fixture._session._held = true;
        };
        fixture._lifetime.Pump();
        Assert.Equal(0, fixture.Window._hides);
        Assert.NotNull(fixture._session._completion);
        Assert.Equal(1, fixture._wakes);
        fixture._session.Complete();
        Assert.Equal(0, fixture.Window._hides);
        fixture._lifetime.Pump();
        Assert.Equal(1, fixture.Window._hides);
        Assert.Equal(2, fixture._wakes);
    }

    [Fact]
    public void ReplacementSessionAfterCompletionMustAlsoEndBeforeHide()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Hide();
        fixture._session.Complete();
        fixture._session._held = true;
        fixture._lifetime.Pump();
        Assert.Equal(0, fixture.Window._hides);
        Assert.Equal(2, fixture._session._releases);
        fixture._session.Complete();
        fixture._lifetime.Pump();
        Assert.Equal(1, fixture.Window._hides);
    }

    [Fact]
    public void SynchronousCompletionRetainsLatestReentrantShowIntent()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._session._release = completed =>
        {
            fixture._lifetime.BeforeShow();
            fixture._session._held = false;
            completed();
            return true;
        };
        fixture._lifetime.Hide();
        Assert.Equal(0, fixture.Window._hides);
        Assert.Equal(1, fixture._wakes);
    }

    [Fact]
    public void SynchronousCompletionCanHideOnlyAfterTheReleaseCallReturns()
    {
        var fixture = new Fixture();
        fixture._session._release = completed =>
        {
            fixture._session._release = null;
            completed();
            Assert.Equal(0, fixture.Window._hides);
            return true;
        };
        fixture._lifetime.Hide();
        Assert.Equal(1, fixture.Window._hides);
    }

    [Fact]
    public void EndFailureRetainsExactHostWithoutRetryOrVisibleSuccess()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("uncertain native End");
        fixture._session._release = _ => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture._lifetime.Retire));
        NativeWindowRetirementQueue queue = new(_ => throw new Xunit.Sdk.XunitException("must retain native window"));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            queue.Retire(fixture._provider, () => throw new Xunit.Sdk.XunitException("must retain renderer"), fixture._lifetime.CanRetire)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(queue.Drain));
        Assert.True(queue.HasPending);
        Assert.Equal(1, fixture._session._releases);
        Assert.Equal(0, fixture.Window._hides);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture._lifetime.BeforeShow));
    }

    [Fact]
    public void FailedHideRemainsOwnedAndRetryableAfterSuccessfulEnd()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("hide");
        fixture.Window._hide = () => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture._lifetime.Retire));
        fixture.Window._hide = null;
        Assert.True(fixture._lifetime.CanRetire());
        Assert.Equal(2, fixture.Window._hides);
    }

    [Fact]
    public void FailedCompletionWakeKeepsRetirementAvailableForCreatingThreadRetry()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Retire();
        var failure = new InvalidOperationException("wake");
        fixture._onWake = () => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture._session.Complete));
        Assert.Equal(0, fixture.Window._hides);
        fixture._onWake = null;
        Assert.True(fixture._lifetime.CanRetire());
        Assert.Equal(1, fixture.Window._hides);
    }

    [Fact]
    public void NativePollCallbackCannotRetireItsOwnWindowOrRenderer()
    {
        var fixture = new Fixture();
        int cleanup = 0;
        NativeWindowRetirementQueue queue = new(_ => { cleanup++; return true; });
        fixture.Window._poll = () =>
        {
            fixture._lifetime.Retire();
            queue.Retire(fixture._provider, () => cleanup++, fixture._lifetime.CanRetire);
            Assert.Equal(0, cleanup);
        };
        fixture._lifetime.Pump();
        queue.Drain();
        Assert.Equal(2, cleanup);
        Assert.False(queue.HasPending);
    }

    [Fact]
    public void ModalHeldWindowDoesNotPreventPeerRetirement()
    {
        var fixture = new Fixture();
        fixture._session._held = true;
        fixture._lifetime.Retire();
        var peer = new Fixture();
        List<IWindow> retired = [];
        NativeWindowRetirementQueue queue = new(window => { retired.Add(window); return true; });
        queue.Retire(fixture._provider, canReleaseRenderingResources: fixture._lifetime.CanRetire);
        queue.Retire(peer._provider);
        Assert.Single(retired);
        Assert.Same(peer._provider, retired[0]);
        fixture._session.Complete();
        queue.Drain();
        Assert.Equal(2, retired.Count);
        Assert.Same(fixture._provider, retired[1]);
    }

    [Fact]
    public void ForeignThreadCannotPublishOrReleaseProviderState()
    {
        var fixture = new Fixture();
        Exception? failure = null;
        Thread thread = new(() => { try { fixture._lifetime.Hide(); } catch (Exception error) { failure = error; } });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, fixture._session._releases);
        Assert.Equal(0, fixture.Window._hides);
    }

    [Fact]
    public void ActualHostUsesLifetimeBeforeVisibilityPollingAndResourceRetirement()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SourceContracts", "SilkWindowService.cs"));
        Assert.Contains("_modalLifetime = new(_window, _usesOwnedCocoaPopup, dispatcher.Wake)", source);
        Assert.Contains("_modalLifetime.BeforeShow();", source);
        Assert.Contains("_modalLifetime.Hide();", source);
        Assert.Contains("_modalLifetime.Close();", source);
        Assert.Contains("Release(_modalLifetime.Retire)", source);
        Assert.Contains("_modalLifetime.CanRetire()", source);
        Assert.Contains("_modalLifetime.Pump();", source);
        Assert.DoesNotContain("_window.DoEvents();", source);
        Assert.DoesNotContain("_window.Close();", source);
        Assert.DoesNotContain("NativeWindowModalSession.TryBegin", source);
        Assert.True(source.IndexOf("private void ReleaseRenderingResources()", StringComparison.Ordinal)
            < source.IndexOf("_nativePointerInput?.Dispose()", StringComparison.Ordinal));
        Assert.Contains("bool INativePointerTarget.IsCurrent", source);
        Assert.Contains("bool INativeCharacterTarget.IsAlive", source);
        Assert.Contains("private void DeliverInputAfterCharacters", source);
    }

    private sealed class Fixture
    {
        internal readonly IWindow _provider = DispatchProxy.Create<IWindow, WindowProxy>();
        internal readonly Session _session;
        internal readonly NativeModalWindowLifetime _lifetime;
        internal int _wakes;
        internal Action? _onWake;
        internal WindowProxy Window => (WindowProxy)(object)_provider;
        internal Fixture(bool queueOnly = false)
        {
            _session = new(_provider);
            _lifetime = new(_provider, queueOnly, () => { _wakes++; _onWake?.Invoke(); }, _session);
        }
    }

    private sealed class Session(IWindow expected) : INativeModalWindowSession
    {
        internal bool _held, _active;
        internal int _polls, _releases;
        internal Action? _completion;
        internal Func<bool>? _poll, _retains;
        internal Func<Action, bool>? _release;
        public bool TryPumpEvents() { _polls++; return _poll?.Invoke() ?? _active; }
        public bool RetainsWindow(IWindow window) { Assert.Same(expected, window); return _retains?.Invoke() ?? _held; }
        public bool TryReleaseWindow(IWindow window, Action completed)
        {
            Assert.Same(expected, window);
            _releases++;
            if (_release is not null) return _release(completed);
            if (!_held) return false;
            Assert.Null(_completion);
            _completion = completed;
            return true;
        }

        internal void Complete()
        {
            _held = false;
            Action completed = _completion!;
            _completion = null;
            completed();
        }
    }

    public class WindowProxy : DispatchProxy
    {
        internal int _polls, _hides, _closes;
        internal bool _visible = true;
        internal Action? _poll, _hide, _closing, _readVisible;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case "DoEvents": _polls++; _poll?.Invoke(); break;
                case "get_IsVisible": _readVisible?.Invoke(); return _visible;
                case "set_IsVisible": Assert.False((bool)args![0]!); _hides++; _hide?.Invoke(); _visible = false; break;
                case "Close": _closes++; _closing?.Invoke(); break;
                default: throw new Xunit.Sdk.XunitException("Unexpected native provider access: " + targetMethod.Name);
            }

            return null;
        }
    }
}
