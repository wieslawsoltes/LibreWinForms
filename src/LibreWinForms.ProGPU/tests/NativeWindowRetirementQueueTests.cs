// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using FluentAssertions;
using Silk.NET.Windowing;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public class NativeWindowRetirementQueueTests
{
    [Fact]
    public void DefaultRetirementPreservesProviderDisposalWithoutHandleProbes()
    {
        int disposed = 0;
        IWindow window = CreateWindow(() => disposed++);
        NativeWindowRetirementQueue queue = new();
        queue.Retire(window);
        disposed.Should().Be(1);
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public void PendingWindowIsRetainedOnceAndExplicitlyRetried()
    {
        bool ready = false;
        int attempts = 0;
        NativeWindowRetirementQueue queue = new(_ => { attempts++; return ready; });
        IWindow window = CreateWindow();
        queue.Retire(window);
        queue.Retire(window);
        attempts.Should().Be(2);
        queue.HasPending.Should().BeTrue();
        ready = true;
        queue.Drain();
        attempts.Should().Be(3);
        queue.HasPending.Should().BeFalse();
        queue.Drain();
        attempts.Should().Be(3);
    }

    [Fact]
    public void FailedDisposalRetainsItsOwnerAndOriginalErrorForRetry()
    {
        InvalidOperationException failure = new("native dispose");
        bool ready = false;
        NativeWindowRetirementQueue queue = new(_ => ready ? true : throw failure);
        Action retire = () => queue.Retire(CreateWindow());
        retire.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        queue.HasPending.Should().BeTrue();
        ready = true;
        queue.Drain();
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public void DrainAttemptsEveryOriginalWindowAndKeepsAllCleanupErrors()
    {
        IWindow first = CreateWindow(), second = CreateWindow(), third = CreateWindow();
        InvalidOperationException firstFailure = new("first"), secondFailure = new("second");
        int phase = 0, thirdCalls = 0;
        NativeWindowRetirementQueue queue = new(window =>
        {
            if (phase == 0)
                return false;
            if (phase == 1 && ReferenceEquals(window, first))
                throw firstFailure;
            if (phase == 1 && ReferenceEquals(window, second))
                throw secondFailure;
            if (ReferenceEquals(window, third))
                thirdCalls++;
            return true;
        });
        queue.Retire(first);
        queue.Retire(second);
        queue.Retire(third);
        phase = 1;
        Action drain = queue.Drain;
        drain.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(firstFailure);
        ((List<Exception>)firstFailure.Data[nameof(NativeWindowRetirementQueue)]!).Should().Equal(secondFailure);
        thirdCalls.Should().Be(1);
        queue.HasPending.Should().BeTrue();
        phase = 2;
        queue.Drain();
        queue.HasPending.Should().BeFalse();
        thirdCalls.Should().Be(1);
    }

    [Fact]
    public void ReentrantSameWindowAndNewWindowCannotRecursivelyDispose()
    {
        IWindow first = CreateWindow(), second = CreateWindow();
        NativeWindowRetirementQueue? queue = null;
        List<IWindow> attempts = [];
        queue = new(window =>
        {
            attempts.Add(window);
            if (ReferenceEquals(window, first))
            {
                queue!.Retire(first);
                queue.Retire(second);
                queue.Drain();
            }

            return true;
        });
        queue.Retire(first);
        attempts.Should().Equal(first);
        queue.HasPending.Should().BeTrue();
        queue.Drain();
        attempts.Should().Equal(first, second);
        queue.HasPending.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignThreadCannotRetireOrDrainNativeWindows(bool drain)
    {
        int attempts = 0;
        NativeWindowRetirementQueue queue = new(_ => { attempts++; return true; });
        IWindow window = CreateWindow();
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                if (drain)
                    queue.Drain();
                else
                    queue.Retire(window);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        failure.Should().BeOfType<InvalidOperationException>();
        attempts.Should().Be(0);
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public void NullWindowDoesNotEnterTheRetirementQueue()
    {
        NativeWindowRetirementQueue queue = new();
        Action retire = () => queue.Retire(null!);
        retire.Should().Throw<ArgumentNullException>();
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public void DispatcherDrainsAfterNormalParticipantPollingWithoutPollingAgain()
    {
        bool ready = false;
        int attempts = 0, polls = 0;
        NativeWindowRetirementQueue queue = new(_ => { attempts++; return ready; });
        using ProGpuDispatcher dispatcher = new(queue);
        dispatcher.RetireNativeWindow(CreateWindow());
        dispatcher.Register(new Participant(() => { polls++; ready = true; }));
        dispatcher.PumpOnce();
        polls.Should().Be(1);
        attempts.Should().Be(2);
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public void DispatcherDrainsEvenWhenSourceWorkThrows()
    {
        bool ready = false;
        NativeWindowRetirementQueue queue = new(_ => ready);
        using ProGpuDispatcher dispatcher = new(queue);
        dispatcher.RetireNativeWindow(CreateWindow());
        InvalidOperationException failure = new("source work");
        dispatcher.Post(() => { ready = true; throw failure; });
        Action pump = dispatcher.PumpOnce;
        pump.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public void SourceFailureRemainsPrimaryWhenNativeRetirementAlsoFails()
    {
        int phase = 0;
        InvalidOperationException sourceFailure = new("source"), retirementFailure = new("retirement");
        NativeWindowRetirementQueue queue = new(_ => phase == 1 ? throw retirementFailure : phase == 2);
        using ProGpuDispatcher dispatcher = new(queue);
        dispatcher.RetireNativeWindow(CreateWindow());
        dispatcher.Post(() => { phase = 1; throw sourceFailure; });
        Action pump = dispatcher.PumpOnce;
        pump.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(sourceFailure);
        sourceFailure.Data[nameof(NativeWindowRetirementQueue)].Should().BeSameAs(retirementFailure);
        queue.HasPending.Should().BeTrue();
        phase = 2;
        dispatcher.PumpOnce();
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public void PendingRetirementPreventsDispatcherShutdownWithoutPoisoningTheLoop()
    {
        bool ready = false;
        NativeWindowRetirementQueue queue = new(_ => ready);
        using ProGpuDispatcher dispatcher = new(queue);
        dispatcher.RetireNativeWindow(CreateWindow());
        Action dispose = dispatcher.Dispose;
        dispose.Should().Throw<InvalidOperationException>();
        dispatcher.CheckAccess().Should().BeTrue();
        dispatcher.Post(() => ready = true);
        dispatcher.PumpOnce();
        queue.HasPending.Should().BeFalse();
        dispose.Should().NotThrow();
    }

    [Fact]
    public void ExitRequestedDuringSourceWorkStillDrainsTheCurrentBoundary()
    {
        bool ready = false;
        NativeWindowRetirementQueue queue = new(_ => ready);
        using ProGpuDispatcher dispatcher = new(queue);
        dispatcher.RetireNativeWindow(CreateWindow());
        dispatcher.Post(() => { ready = true; dispatcher.RequestExit(); });
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        dispatcher.Run(timeout.Token);
        queue.HasPending.Should().BeFalse();
    }

    private sealed class Participant(Action poll) : IProGpuLoopParticipant
    {
        public void Pump() => poll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RendererFailureProtectsTheNativeSurfaceAndRetainsItsCleanupOwner(bool nativeFinishes)
    {
        bool rendererReady = false;
        int rendererAttempts = 0, nativeAttempts = 0;
        InvalidOperationException failure = new("renderer retirement");
        NativeWindowRetirementQueue queue = new(_ => { nativeAttempts++; return nativeFinishes; });
        Action releaseRenderer = () =>
        {
            rendererAttempts++;
            if (!rendererReady)
                throw failure;
        };
        Action retire = () => queue.Retire(CreateWindow(), releaseRenderer);
        retire.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        nativeAttempts.Should().Be(0, "native providers need not have a view-lease guard");
        rendererAttempts.Should().Be(1);
        queue.HasPending.Should().BeTrue("both renderer and native ownership must complete");
        rendererReady = true;
        queue.Drain();
        rendererAttempts.Should().Be(2);
        nativeAttempts.Should().Be(1);
        queue.HasPending.Should().Be(!nativeFinishes);
        int expectedNativeAttempts = nativeFinishes ? 1 : 2;
        nativeFinishes = true;
        queue.Drain();
        rendererAttempts.Should().Be(2);
        nativeAttempts.Should().Be(expectedNativeAttempts);
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public void NativeFailureIsOnlyObservedAfterRendererRetirementCompletes()
    {
        bool rendererReady = false, nativeReady = false;
        int nativeAttempts = 0;
        InvalidOperationException rendererFailure = new("renderer"), nativeFailure = new("native");
        NativeWindowRetirementQueue queue = new(_ =>
        {
            nativeAttempts++;
            return nativeReady ? true : throw nativeFailure;
        });
        Action retire = () => queue.Retire(CreateWindow(), () =>
        {
            if (!rendererReady)
                throw rendererFailure;
        });
        retire.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(rendererFailure);
        nativeAttempts.Should().Be(0);
        queue.HasPending.Should().BeTrue();
        rendererReady = true;
        Action drain = queue.Drain;
        drain.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(nativeFailure);
        nativeAttempts.Should().Be(1);
        queue.HasPending.Should().BeTrue();
        nativeReady = true;
        queue.Drain();
        nativeAttempts.Should().Be(2);
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public void SuccessfulRendererCleanupIsNotRepeatedWhileNativeRetirementIsPending()
    {
        bool nativeReady = false;
        int renderingCalls = 0, nativeCalls = 0;
        NativeWindowRetirementQueue queue = new(_ => { nativeCalls++; return nativeReady; });
        queue.Retire(CreateWindow(), () => renderingCalls++);
        queue.Drain();
        queue.HasPending.Should().BeTrue();
        nativeReady = true;
        queue.Drain();
        queue.HasPending.Should().BeFalse();
        renderingCalls.Should().Be(1);
        nativeCalls.Should().Be(3);
    }

    [Fact]
    public void ActiveSourceFrameBlocksRenderingCleanupAndNativeDisposal()
    {
        bool active = true;
        int renderingCalls = 0, nativeCalls = 0;
        NativeWindowRetirementQueue queue = new(_ => { nativeCalls++; return true; });
        queue.Retire(CreateWindow(), () => renderingCalls++, () => !active);
        queue.Drain(); // A nested dispatcher pump is not a render-scope unwind.
        queue.HasPending.Should().BeTrue();
        renderingCalls.Should().Be(0);
        nativeCalls.Should().Be(0);
        active = false;
        queue.Drain();
        renderingCalls.Should().Be(1);
        nativeCalls.Should().Be(1);
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public void DuplicateRetirementCannotReplaceItsOriginalFrameGuardOrCleanupOwner()
    {
        bool active = true;
        int originalCleanup = 0, replacementCleanup = 0, nativeCalls = 0;
        IWindow window = CreateWindow();
        NativeWindowRetirementQueue queue = new(_ => { nativeCalls++; return true; });
        queue.Retire(window, () => originalCleanup++, () => !active);
        queue.Retire(window, () => replacementCleanup++);
        nativeCalls.Should().Be(0);
        active = false;
        queue.Drain();
        originalCleanup.Should().Be(1);
        replacementCleanup.Should().Be(0);
        nativeCalls.Should().Be(1);
    }

    [Fact]
    public void PendingFrameDoesNotPreventAnIndependentWindowFromRetiring()
    {
        bool active = true;
        IWindow first = CreateWindow(), second = CreateWindow();
        List<IWindow> disposed = [];
        NativeWindowRetirementQueue queue = new(window => { disposed.Add(window); return true; });
        queue.Retire(first, canReleaseRenderingResources: () => !active);
        queue.Retire(second);
        disposed.Should().Equal(second);
        queue.HasPending.Should().BeTrue();
        active = false;
        queue.Drain();
        disposed.Should().Equal(second, first);
        queue.HasPending.Should().BeFalse();
    }

    [Fact]
    public void ThrowingFrameGuardRetainsExactOwnerBeforeAnyCleanupOrNativeAttempt()
    {
        InvalidOperationException expected = new("source guard");
        bool ready = false;
        int cleanup = 0, native = 0;
        NativeWindowRetirementQueue queue = new(_ => { native++; return true; });
        Action retire = () => queue.Retire(CreateWindow(), () => cleanup++,
            () => ready ? true : throw expected);
        retire.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
        cleanup.Should().Be(0);
        native.Should().Be(0);
        queue.HasPending.Should().BeTrue();
        ready = true;
        queue.Drain();
        cleanup.Should().Be(1);
        native.Should().Be(1);
        queue.HasPending.Should().BeFalse();
    }

    internal static IWindow CreateWindow(Action? dispose = null)
    {
        IWindow window = DispatchProxy.Create<IWindow, WindowIdentity>();
        ((WindowIdentity)(object)window).DisposeWindow = dispose;
        return window;
    }

    public class WindowIdentity : DispatchProxy
    {
        internal Action? DisposeWindow { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            targetMethod!.Name.Should().Be(nameof(IDisposable.Dispose));
            DisposeWindow.Should().NotBeNull("only the production disposal test may invoke the window provider");
            DisposeWindow!();
            return null;
        }
    }
}
