// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

[Collection(ProGpuDesktopCaptureCollection.Name)]
public sealed class ProGpuPointerDragCancellationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeProviderCancellationReachesActualDragRegistration(bool characterThrows, bool cleanupThrows)
    {
        using Fixture f = new();
        using NativePointerTestContext provider = new();
        ProviderTarget target = new(f) { CharacterThrows = characterThrows, CleanupThrows = cleanupThrows };
        using NativePointerInput input = new(provider, target);
        target.Subscription = input;
        InvalidOperationException leaveFailure = new("old drag target leave");
        f.Session.Leaving = () =>
        {
            Assert.True(target.SourceRetired);
            if (cleanupThrows) throw leaveFailure;
        };
        f.Session.On("feedback", () =>
        {
            provider.Emit(NativePointerInputTests.Packet(NativePointerEventKind.Leave));
            Assert.Equal(0, f.Session.Leaves);
            provider.Emit(NativePointerInputTests.Packet(NativePointerEventKind.Cancel));
        });
        if (characterThrows || cleanupThrows)
        {
            Exception actual = Assert.Throws<InvalidOperationException>(() => f.Run());
            Assert.Same(characterThrows ? target.Failure : target.SourceFailure, actual);
            if (cleanupThrows) Assert.Same(leaveFailure, actual.Data["PointerCancellationDragLeave"]);
            if (characterThrows && cleanupThrows)
                Assert.Same(target.SourceFailure, actual.Data["PointerCancellationSourceRetirement"]);
        }
        else
            Assert.Equal(LibreDragDropEffects.None, f.Run());
        Assert.True(target.SourceRetired);
        Assert.Equal(1, f.Session.Leaves);
        Assert.Equal(0, f.Session.Drops);
        Assert.Equal(1, f.Session.Queries);
    }

    private sealed class ProviderTarget(Fixture fixture) : INativePointerTarget
    {
        private bool _cancelling;
        internal NativePointerInput? Subscription { get; set; }
        internal bool CharacterThrows { get; set; }
        internal bool CleanupThrows { get; set; }
        internal bool SourceRetired { get; private set; }
        internal InvalidOperationException Failure { get; } = new("original characters");
        internal InvalidOperationException SourceFailure { get; } = new("source retirement");
        public bool IsCurrent(NativePointerInput subscription) => ReferenceEquals(subscription, Subscription);
        public LibrePoint MapPoint(double x, double y)
            => LibreWindowCoordinates.ToManagedPoint(x, y, LibreWindowCoordinateMode.Logical, 1, 1);
        public ProGpuDragCancellation? PrepareCancellation()
        {
            _cancelling = true;
            return fixture.Router.PreparePointerCancellation(fixture.Window);
        }

        public void FlushCharacters()
        {
            if (!_cancelling) return;
            Assert.False(fixture.Router.Record(fixture.Window, Event(LibreInputEventKind.PointerUp)));
            if (CharacterThrows) throw Failure;
        }

        public void Input(in LibreInputEvent input)
        {
            Assert.False(fixture.Router.Record(fixture.Window, input));
            if (input.Kind == LibreInputEventKind.PointerCancel)
            {
                SourceRetired = true;
                if (CleanupThrows) throw SourceFailure;
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedCancellationCommitsBeforeRetirementReleaseOrEscape(bool escape)
    {
        using Fixture f = new();
        bool retired = false;
        f.Session.Leaving = () => Assert.True(retired);
        f.Session.On("feedback", () =>
        {
            ProGpuDragCancellation? token = f.Router.PreparePointerCancellation(f.Window);
            Assert.NotNull(token);
            ProGpuDragCancellation.Deliver(() =>
            {
                Assert.False(f.Router.Record(f.Window, Event(escape ? LibreInputEventKind.KeyDown : LibreInputEventKind.PointerUp)));
            }, () => retired = true, token);
            token.Complete();
        });
        Assert.Equal(LibreDragDropEffects.None, f.Run());
        Assert.Equal(1, f.Session.Leaves);
        Assert.Equal(1, f.Session.Queries);
        Assert.Equal(0, f.Session.Drops);
    }

    [Fact]
    public void WrongWindowCancellationRetiresOnlyItsOwnButtons()
    {
        using Fixture f = new();
        f.Router.Record(f.Other, Event(LibreInputEventKind.PointerDown) with { Button = LibrePointerButton.Secondary });
        f.Session.On("feedback", () =>
        {
            Assert.Null(f.Router.PreparePointerCancellation(f.Other));
            Assert.Equal(0, f.Session.Leaves);
            Assert.True(f.Router.Record(f.Window, Event(LibreInputEventKind.PointerUp)));
        });
        Assert.Equal(LibreDragDropEffects.Copy, f.Run());
        Assert.Equal(1, f.Session.Drops);
        Assert.Equal(0, f.Session.Leaves);
    }

    [Theory]
    [InlineData(LibreInputEventKind.PointerLeave)]
    [InlineData(LibreInputEventKind.PointerCancel)]
    public void RetirementPacketsAreNeverDragSamples(LibreInputEventKind kind)
    {
        using Fixture f = new();
        f.Session.On("feedback", () =>
        {
            Assert.False(f.Router.Record(f.Window, Event(kind) with { Modifiers = LibreInputModifiers.Alt, Position = new(999, 999) }));
            Assert.Equal(1, f.Session.Queries);
            Assert.Equal(0, f.Session.Leaves);
            Assert.True(f.Router.Record(f.Window, Event(LibreInputEventKind.PointerUp)));
        });
        Assert.Equal(LibreDragDropEffects.Copy, f.Run());
        Assert.Equal(1, f.Session.Drops);
    }

    [Fact]
    public void MissingLegacyOwnerCannotBorrowTheMostRecentWindow()
    {
        using Fixture f = new();
        f.Session.On("feedback", () =>
        {
            Assert.Null(f.Router.PreparePointerCancellation(f.Window));
            Assert.Equal(0, f.Session.Leaves);
            f.Router.Record(f.Window, Event(LibreInputEventKind.PointerUp));
        });
        Assert.Equal(LibreDragDropEffects.Copy, f.Run(sourceWindow: default(LibreHandle)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidOwnerFailsBeforeRegistrationAndNextValidDragIsUsable(int kind)
    {
        using Fixture f = new();
        LibreHandle owner = f.Window.Handle;
        switch (kind)
        {
            case 0: owner = new(owner.Value, LibreHandleKind.LogicalControl); break;
            case 1: f.Router.Unregister(f.Window); break;
            case 2: f.Window.IsDisposed = true; break;
            case 3: Assert.True(f.Handles.Release(owner)); break;
        }

        Assert.Throws<ArgumentException>(() => f.Run(owner));
        Assert.Equal(0, f.Session.Queries);
        f.Router.Record(f.Other, Event(LibreInputEventKind.PointerDown));
        f.Session.On("feedback", () => f.Router.Record(f.Other, Event(LibreInputEventKind.PointerUp)));
        Assert.Equal(LibreDragDropEffects.Copy, f.Run(f.Other.Handle));
    }

    [Fact]
    public void RetiredOrChangedWindowDoesNotCancelALiveRegistration()
    {
        using Fixture f = new();
        f.Session.On("feedback", () =>
        {
            LibreHandle original = f.Window.Handle;
            f.Window.Handle = f.Other.Handle;
            Assert.Null(f.Router.PreparePointerCancellation(f.Window));
            f.Window.Handle = original;
            f.Window.IsDisposed = true;
            Assert.Null(f.Router.PreparePointerCancellation(f.Window));
            f.Window.IsDisposed = false;
            f.Router.Record(f.Window, Event(LibreInputEventKind.PointerUp));
        });
        Assert.Equal(LibreDragDropEffects.Copy, f.Run());
        Assert.Equal(0, f.Session.Leaves);
    }

    [Fact]
    public void DeferredTokenIsRevokedBeforeALaterSameWindowRegistration()
    {
        using Fixture f = new();
        ProGpuDragCancellation? token = null;
        f.Session.On("feedback", () => token = f.Router.PreparePointerCancellation(f.Window));
        Assert.Equal(LibreDragDropEffects.None, f.Run());
        Assert.NotNull(token);
        Assert.Equal(0, f.Session.Leaves);
        f.Router.Record(f.Window, Event(LibreInputEventKind.PointerDown));
        f.Session = new();
        f.Session.On("feedback", () =>
        {
            token.Complete();
            Assert.Equal(0, f.Session.Leaves);
            f.Router.Record(f.Window, Event(LibreInputEventKind.PointerUp));
        });
        Assert.Equal(LibreDragDropEffects.Copy, f.Run());
        Assert.Equal(1, f.Session.Drops);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetirementErrorsRemainPrimaryWhenDeferredLeaveAlsoFails(bool flushFails)
    {
        using Fixture f = new();
        var first = new InvalidOperationException("source retirement");
        var leave = new InvalidOperationException("drag leave");
        bool sourceRetired = false;
        f.Session.Leaving = () => throw leave;
        f.Session.On("feedback", () =>
        {
            ProGpuDragCancellation? token = f.Router.PreparePointerCancellation(f.Window);
            Assert.NotNull(token);
            try
            {
                ProGpuDragCancellation.Deliver(
                    () => { if (flushFails) throw first; },
                    () => { sourceRetired = true; if (!flushFails) throw first; }, token);
            }
            finally { token.Complete(); }
        });
        Assert.Same(first, Assert.Throws<InvalidOperationException>(() => f.Run()));
        Assert.Same(leave, first.Data["PointerCancellationDragLeave"]);
        Assert.True(sourceRetired);
        Assert.Equal(1, f.Session.Leaves);
        Assert.Equal(0, f.Session.Drops);
        f.Session = new();
        f.Router.Record(f.Window, Event(LibreInputEventKind.PointerDown));
        f.Session.On("feedback", () => f.Router.Record(f.Window, Event(LibreInputEventKind.PointerUp)));
        Assert.Equal(LibreDragDropEffects.Copy, f.Run());
    }

    [Theory]
    [InlineData("query", 0)]
    [InlineData("hit", 0)]
    [InlineData("enter", 1)]
    [InlineData("over", 1)]
    [InlineData("feedback", 1)]
    public void NestedCancellationCannotContinueNonterminalCallbacks(string step, int leaves)
    {
        using Fixture f = new();
        f.Session.On(step, () => ProGpuDragCancellation.Deliver(() => { }, () => { }, f.Router.PreparePointerCancellation(f.Window)));
        if (step == "over")
            f.Session.AfterInitialFeedback = () => f.Router.Record(f.Window, Event(LibreInputEventKind.PointerMove));
        Assert.Equal(LibreDragDropEffects.None, f.Run());
        Assert.Equal(leaves, f.Session.Leaves);
        Assert.Equal(0, f.Session.Drops);
    }

    [Fact]
    public void TokenCompletionIsOwnerThreadBoundAndSingleUseEvenAfterFailure()
    {
        Session session = new();
        var token = new ProGpuDragCancellation(session, Fixture.Target);
        Exception? failure = null;
        Thread other = new(() => { try { token.Complete(); } catch (Exception error) { failure = error; } });
        other.Start(); Assert.True(other.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, session.Leaves);
        token.Complete(); token.Complete();
        Assert.Equal(1, session.Leaves);
    }

    private static LibreInputEvent Event(LibreInputEventKind kind)
        => new(kind, 1, LibreInputModifiers.Control, LibreKey.Escape, null, new(10, 20), default, LibrePointerButton.Primary);

    private sealed class Fixture : IDisposable
    {
        internal static LibreHandle Target { get; } = new(71, LibreHandleKind.LogicalControl);
        internal ManagedLibreHandleRegistry Handles { get; } = new();
        internal ProGpuDispatcher Dispatcher { get; } = new();
        internal ProGpuDragInputRouter Router { get; }
        internal ProGpuDragDropService Service { get; }
        internal Window Window { get; }
        internal Window Other { get; }
        internal Session Session { get; set; } = new();
        internal Fixture()
        {
            Router = new(Handles);
            Window = new(Handles); Other = new(Handles);
            Router.Register(Window); Router.Register(Other);
            Service = new(Dispatcher, Router);
            Service.SetTargetEnabled(Target, true);
            Router.Record(Window, Event(LibreInputEventKind.PointerDown));
        }

        internal LibreDragDropEffects Run(LibreHandle? sourceWindow = null)
            => Service.DoDragDrop(new(Target, new Data(), LibreDragDropEffects.Copy, default, false)
                { SourceWindow = sourceWindow ?? Window.Handle }, Session);
        public void Dispose() => Dispatcher.Dispose();
    }

    private sealed class Window : IProGpuDragInputWindow
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        internal Window(ManagedLibreHandleRegistry handles) => Handle = handles.Allocate(this, LibreHandleKind.Window);
        public LibreHandle Handle { get; set; }
        public LibreRectangle Bounds => new(100, 200, 300, 400);
        public bool IsDisposed { get; set; }
        public bool CheckAccess() => _thread == Environment.CurrentManagedThreadId;
    }

    private sealed class Session : ILibreDragDropSession
    {
        private string? _step;
        private Action? _action;
        internal Action? Leaving { get; set; }
        internal Action? AfterInitialFeedback { get; set; }
        internal int Leaves { get; private set; }
        internal int Drops { get; private set; }
        internal int Queries { get; private set; }
        internal void On(string step, Action action) { _step = step; _action = action; }
        private void Invoke(string step)
        {
            if (_step != step) return;
            Action? action = _action; _action = null; _step = null; action?.Invoke();
        }

        public LibreHandle HitTest(LibrePoint p) { Invoke("hit"); return Fixture.Target; }
        public LibreDragTransition Enter(LibreHandle t, int k, LibrePoint p, LibreDragDropEffects e)
        { Invoke("enter"); return new(t, LibreDragDropEffects.Copy); }
        public LibreDragDropEffects Over(LibreHandle t, int k, LibrePoint p, LibreDragDropEffects e)
        { Invoke("over"); return e; }
        public void Leave(LibreHandle target) { Assert.Equal(Fixture.Target, target); Leaves++; Leaving?.Invoke(); }
        public LibreDragDropEffects Drop(LibreHandle t, int k, LibrePoint p, LibreDragDropEffects e)
        { Drops++; return LibreDragDropEffects.Copy; }
        public LibreDragAction QueryContinue(int k, bool escape) { Queries++; Invoke("query"); return LibreDragAction.Continue; }
        public bool GiveFeedback(LibreDragDropEffects effect)
        {
            Invoke("feedback");
            Action? after = AfterInitialFeedback; AfterInitialFeedback = null; after?.Invoke();
            return true;
        }
    }

    private sealed class Data : ILibreDataTransfer
    {
        public IReadOnlyList<string> Formats => [];
        public bool Contains(string format, bool autoConvert) => false;
        public object? GetData(string format, bool autoConvert) => null;
    }
}
