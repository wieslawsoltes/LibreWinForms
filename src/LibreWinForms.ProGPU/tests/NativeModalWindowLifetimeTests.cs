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
        fixture.Session.Active = active;
        fixture.Lifetime.Pump();
        Assert.Equal(modalPolls, fixture.Session.Polls);
        Assert.Equal(windowPolls, fixture.Window.Polls);
    }

    [Fact]
    public void ModalPollFailureNeverFallsBackToOrdinaryPoll()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("modal poll");
        fixture.Session.Poll = () => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Lifetime.Pump));
        Assert.Equal(0, fixture.Window.Polls);
    }

    [Fact]
    public void HideCoalescesAndCompletionOnlyWakesCreatingThread()
    {
        var fixture = new Fixture();
        fixture.Session.Held = true;
        fixture.Lifetime.Hide();
        fixture.Lifetime.Hide();
        Assert.Equal(1, fixture.Session.Releases);
        Assert.Equal(0, fixture.Window.Hides);
        fixture.Session.Complete();
        Assert.Equal(1, fixture.Wakes);
        Assert.Equal(0, fixture.Window.Hides);
        fixture.Lifetime.Pump();
        Assert.Equal(1, fixture.Window.Hides);
    }

    [Fact]
    public void LaterShowInvalidatesPendingHideWithoutRepeatingRelease()
    {
        var fixture = new Fixture();
        fixture.Session.Held = true;
        fixture.Lifetime.Hide();
        fixture.Lifetime.BeforeShow();
        fixture.Session.Complete();
        fixture.Lifetime.Pump();
        Assert.Equal(0, fixture.Window.Hides);
        Assert.Equal(1, fixture.Session.Releases);
    }

    [Fact]
    public void CloseSupersedesHideAndRejectsShowUntilActualClosingReturns()
    {
        var fixture = new Fixture();
        fixture.Session.Held = true;
        fixture.Lifetime.Hide();
        fixture.Lifetime.Close();
        Assert.Throws<InvalidOperationException>(fixture.Lifetime.BeforeShow);
        fixture.Session.Complete();
        Assert.Equal(0, fixture.Window.Closes);
        fixture.Window.Closing = () => Assert.Throws<InvalidOperationException>(fixture.Lifetime.BeforeShow);
        fixture.Lifetime.Pump();
        Assert.Equal(1, fixture.Window.Closes);
        Assert.Equal(0, fixture.Window.Hides);
        fixture.Lifetime.BeforeShow(); // The real provider may cancel Closing.
    }

    [Fact]
    public void ReentrantCloseCannotDuplicateProviderTransition()
    {
        var fixture = new Fixture();
        fixture.Window.Closing = fixture.Lifetime.Close;
        fixture.Lifetime.Close();
        Assert.Equal(1, fixture.Window.Closes);
    }

    [Fact]
    public void DisposalSupersedesPendingCloseAndRetainsRendererUntilEnd()
    {
        var fixture = new Fixture();
        fixture.Session.Held = true;
        fixture.Lifetime.Close();
        fixture.Lifetime.Retire();
        int rendering = 0, native = 0;
        NativeWindowRetirementQueue queue = new(window => { Assert.Same(fixture.Provider, window); native++; return true; });
        queue.Retire(fixture.Provider, () => rendering++, fixture.Lifetime.CanRetire);
        Assert.True(queue.HasPending);
        Assert.Equal(0, rendering);
        Assert.Equal(0, native);
        Assert.Throws<ObjectDisposedException>(fixture.Lifetime.BeforeShow);
        fixture.Session.Complete();
        Assert.Equal(0, rendering);
        queue.Drain();
        Assert.False(queue.HasPending);
        Assert.Equal(1, rendering);
        Assert.Equal(1, native);
        Assert.Equal(1, fixture.Window.Hides);
        Assert.Equal(0, fixture.Window.Closes);
    }

    [Fact]
    public void ReplacementSessionAfterCompletionMustAlsoEndBeforeHide()
    {
        var fixture = new Fixture();
        fixture.Session.Held = true;
        fixture.Lifetime.Hide();
        fixture.Session.Complete();
        fixture.Session.Held = true;
        fixture.Lifetime.Pump();
        Assert.Equal(0, fixture.Window.Hides);
        Assert.Equal(2, fixture.Session.Releases);
        fixture.Session.Complete();
        fixture.Lifetime.Pump();
        Assert.Equal(1, fixture.Window.Hides);
    }

    [Fact]
    public void SynchronousCompletionRetainsLatestReentrantShowIntent()
    {
        var fixture = new Fixture();
        fixture.Session.Release = completed => { fixture.Lifetime.BeforeShow(); completed(); return true; };
        fixture.Lifetime.Hide();
        Assert.Equal(0, fixture.Window.Hides);
        Assert.Equal(1, fixture.Wakes);
    }

    [Fact]
    public void SynchronousCompletionCanHideOnlyAfterTheReleaseCallReturns()
    {
        var fixture = new Fixture();
        fixture.Session.Release = completed =>
        {
            fixture.Session.Release = null;
            completed();
            Assert.Equal(0, fixture.Window.Hides);
            return true;
        };
        fixture.Lifetime.Hide();
        Assert.Equal(1, fixture.Window.Hides);
    }

    [Fact]
    public void EndFailureRetainsExactHostWithoutRetryOrVisibleSuccess()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("uncertain native End");
        fixture.Session.Release = _ => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Lifetime.Retire));
        NativeWindowRetirementQueue queue = new(_ => throw new Xunit.Sdk.XunitException("must retain native window"));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            queue.Retire(fixture.Provider, () => throw new Xunit.Sdk.XunitException("must retain renderer"), fixture.Lifetime.CanRetire)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(queue.Drain));
        Assert.True(queue.HasPending);
        Assert.Equal(1, fixture.Session.Releases);
        Assert.Equal(0, fixture.Window.Hides);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Lifetime.BeforeShow));
    }

    [Fact]
    public void FailedHideRemainsOwnedAndRetryableAfterSuccessfulEnd()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("hide");
        fixture.Window.Hide = () => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Lifetime.Retire));
        fixture.Window.Hide = null;
        Assert.True(fixture.Lifetime.CanRetire());
        Assert.Equal(2, fixture.Window.Hides);
    }

    [Fact]
    public void FailedCompletionWakeKeepsRetirementAvailableForCreatingThreadRetry()
    {
        var fixture = new Fixture();
        fixture.Session.Held = true;
        fixture.Lifetime.Retire();
        var failure = new InvalidOperationException("wake");
        fixture.OnWake = () => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Session.Complete));
        Assert.Equal(0, fixture.Window.Hides);
        fixture.OnWake = null;
        Assert.True(fixture.Lifetime.CanRetire());
        Assert.Equal(1, fixture.Window.Hides);
    }

    [Fact]
    public void NativePollCallbackCannotRetireItsOwnWindowOrRenderer()
    {
        var fixture = new Fixture();
        int cleanup = 0;
        NativeWindowRetirementQueue queue = new(_ => { cleanup++; return true; });
        fixture.Window.Poll = () =>
        {
            fixture.Lifetime.Retire();
            queue.Retire(fixture.Provider, () => cleanup++, fixture.Lifetime.CanRetire);
            Assert.Equal(0, cleanup);
        };
        fixture.Lifetime.Pump();
        queue.Drain();
        Assert.Equal(2, cleanup);
        Assert.False(queue.HasPending);
    }

    [Fact]
    public void ModalHeldWindowDoesNotPreventPeerRetirement()
    {
        var fixture = new Fixture();
        fixture.Session.Held = true;
        fixture.Lifetime.Retire();
        var peer = new Fixture();
        List<IWindow> retired = [];
        NativeWindowRetirementQueue queue = new(window => { retired.Add(window); return true; });
        queue.Retire(fixture.Provider, canReleaseRenderingResources: fixture.Lifetime.CanRetire);
        queue.Retire(peer.Provider);
        Assert.Single(retired);
        Assert.Same(peer.Provider, retired[0]);
        fixture.Session.Complete();
        queue.Drain();
        Assert.Equal(2, retired.Count);
        Assert.Same(fixture.Provider, retired[1]);
    }

    [Fact]
    public void ForeignThreadCannotPublishOrReleaseProviderState()
    {
        var fixture = new Fixture();
        Exception? failure = null;
        Thread thread = new(() => { try { fixture.Lifetime.Hide(); } catch (Exception error) { failure = error; } });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, fixture.Session.Releases);
        Assert.Equal(0, fixture.Window.Hides);
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
    }

    private sealed class Fixture
    {
        internal readonly IWindow Provider = DispatchProxy.Create<IWindow, WindowProxy>();
        internal readonly Session Session;
        internal readonly NativeModalWindowLifetime Lifetime;
        internal int Wakes;
        internal Action? OnWake;
        internal WindowProxy Window => (WindowProxy)(object)Provider;
        internal Fixture(bool queueOnly = false)
        {
            Session = new(Provider);
            Lifetime = new(Provider, queueOnly, () => { Wakes++; OnWake?.Invoke(); }, Session);
        }
    }

    private sealed class Session(IWindow expected) : INativeModalWindowSession
    {
        internal bool Held, Active;
        internal int Polls, Releases;
        internal Action? Completion;
        internal Func<bool>? Poll;
        internal Func<Action, bool>? Release;
        public bool TryPumpEvents() { Polls++; return Poll?.Invoke() ?? Active; }
        public bool RetainsWindow(IWindow window) { Assert.Same(expected, window); return Held; }
        public bool TryReleaseWindow(IWindow window, Action completed)
        {
            Assert.Same(expected, window);
            Releases++;
            if (Release is not null) return Release(completed);
            if (!Held) return false;
            Assert.Null(Completion);
            Completion = completed;
            return true;
        }
        internal void Complete()
        {
            Held = false;
            Action completed = Completion!;
            Completion = null;
            completed();
        }
    }

    public class WindowProxy : DispatchProxy
    {
        internal int Polls, Hides, Closes;
        internal Action? Poll, Hide, Closing;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            switch (method!.Name)
            {
                case "DoEvents": Polls++; Poll?.Invoke(); break;
                case "set_IsVisible": Assert.False((bool)arguments![0]!); Hides++; Hide?.Invoke(); break;
                case "Close": Closes++; Closing?.Invoke(); break;
                default: throw new Xunit.Sdk.XunitException("Unexpected native provider access: " + method.Name);
            }
            return null;
        }
    }
}
