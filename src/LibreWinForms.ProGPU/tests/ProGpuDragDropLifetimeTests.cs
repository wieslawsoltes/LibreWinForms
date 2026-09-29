// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

[Collection(ProGpuDesktopCaptureCollection.Name)]
public sealed class ProGpuDragDropLifetimeTests
{
    private static LibreHandle Target { get; } = new(42, LibreHandleKind.LogicalControl);
    private static ProGpuDragInput Released { get; } = new(ProGpuDragInputKind.Pointer, new LibrePoint(10, 20), 0);
    private static ProGpuDragInput Escape { get; } = new(ProGpuDragInputKind.Escape, new LibrePoint(10, 20), 1);
    private static LibreDragDropRequest Request => new(new(7, LibreHandleKind.LogicalControl),
        new EmptyData(), LibreDragDropEffects.Copy, default, UseDefaultDragImage: false);

    [Fact]
    public void FailedRegistrationRevokesItsSinkAndAllowsAnotherDrag()
    {
        using ProGpuDispatcher dispatcher = new();
        var failure = new InvalidOperationException("begin");
        var input = new InputSource { BeginFailure = failure };
        var service = Create(dispatcher, input);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => service.DoDragDrop(Request, new Session())));
        Assert.Equal(0, input.EndCount);
        Assert.False(input.Sinks[0].Input(Escape));
        input.BeginFailure = null;
        Assert.Equal(LibreDragDropEffects.Copy, service.DoDragDrop(Request, new Session()));
        Assert.Equal(1, input.EndCount);
        Assert.NotSame(input.Sinks[0], input.Sinks[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceFailureRemainsPrimaryAndReleasesServiceState(bool releaseFails)
    {
        using ProGpuDispatcher dispatcher = new();
        var failure = new InvalidOperationException("source");
        var cleanup = new InvalidOperationException("end");
        var input = new InputSource { EndFailure = releaseFails ? cleanup : null };
        var service = Create(dispatcher, input);
        var session = new Session { Query = _ => throw failure };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => service.DoDragDrop(Request, session)));
        Assert.Equal(1, input.EndCount);
        Assert.False(input.Sinks[0].Input(Escape));
        if (releaseFails) Assert.Same(cleanup, failure.Data["DragInputRelease"]);
        else Assert.False(failure.Data.Contains("DragInputRelease"));
        input.EndFailure = null;
        Assert.Equal(LibreDragDropEffects.Copy, service.DoDragDrop(Request, new Session()));
        Assert.Equal(2, input.EndCount);
    }

    [Fact]
    public void TeardownFailureIsReportedWithoutLeavingAnActiveSession()
    {
        using ProGpuDispatcher dispatcher = new();
        var failure = new InvalidOperationException("end");
        var input = new InputSource { EndFailure = failure };
        var service = Create(dispatcher, input);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => service.DoDragDrop(Request, new Session())));
        Assert.False(input.Sinks[0].Input(Escape));
        input.EndFailure = null;
        Assert.Equal(LibreDragDropEffects.Copy, service.DoDragDrop(Request, new Session()));
        Assert.Equal(2, input.EndCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedInputFromAnEarlierDragCannotEnterTheNextSession(bool escape)
    {
        using ProGpuDispatcher dispatcher = new();
        bool? obsoleteHandled = null;
        var input = new InputSource();
        input.OnBegin = sink =>
        {
            if (input.Sinks.Count == 1)
                dispatcher.Post(() => obsoleteHandled = sink.Input(escape ? Escape : Released));
            else
                dispatcher.Post(() => Assert.True(sink.Input(Released)));
        };
        var service = Create(dispatcher, input);
        Assert.Equal(LibreDragDropEffects.Copy, service.DoDragDrop(Request, new Session()));
        Assert.Null(obsoleteHandled);
        input.Initial = Released with { KeyState = 1 };
        var next = new Session();
        Assert.Equal(LibreDragDropEffects.Copy, service.DoDragDrop(Request, next));
        Assert.Equal(false, obsoleteHandled);
        Assert.Equal(2, next.QueryCount);
        Assert.Equal(1, next.DropCount);
        Assert.Equal(2, input.EndCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InputIsRevokedBeforeExternalTeardownCallbacks(bool sourceFails)
    {
        using ProGpuDispatcher dispatcher = new();
        var input = new InputSource();
        bool? handledDuringEnd = null;
        input.OnEnd = sink => handledDuringEnd = sink.Input(Escape);
        var service = Create(dispatcher, input);
        var failure = new InvalidOperationException("source");
        var session = new Session { Query = _ => sourceFails ? throw failure : LibreDragAction.Continue };
        if (sourceFails)
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => service.DoDragDrop(Request, session)));
        else
            Assert.Equal(LibreDragDropEffects.Copy, service.DoDragDrop(Request, session));
        Assert.Equal(false, handledDuringEnd);
        Assert.Equal(1, session.QueryCount);
        Assert.Equal(1, input.EndCount);
    }

    [Fact]
    public void RetiredSinkDoesNotEnterADisposedDispatcher()
    {
        var dispatcher = new ProGpuDispatcher();
        var input = new InputSource();
        var service = Create(dispatcher, input);
        try { Assert.Equal(LibreDragDropEffects.Copy, service.DoDragDrop(Request, new Session())); }
        finally { dispatcher.Dispose(); }
        Assert.False(input.Sinks[0].Input(Escape));
    }

    [Fact]
    public void QueuedSourceFailureRetiresTheSessionAndAllowsAnotherDrag()
    {
        using ProGpuDispatcher dispatcher = new();
        var failure = new InvalidOperationException("queued source");
        var input = new InputSource { Initial = Released with { KeyState = 1 } };
        input.OnBegin = sink => dispatcher.Post(() => sink.Input(Released));
        var service = Create(dispatcher, input);
        var session = new Session { Query = count => count == 2 ? throw failure : LibreDragAction.Continue };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => service.DoDragDrop(Request, session)));
        Assert.False(input.Sinks[0].Input(Escape));
        Assert.Equal(LibreDragDropEffects.Copy, service.DoDragDrop(Request, new Session()));
        Assert.Equal(2, input.EndCount);
    }

    private static ProGpuDragDropService Create(ProGpuDispatcher dispatcher, InputSource input)
    {
        var service = new ProGpuDragDropService(dispatcher, input);
        service.SetTargetEnabled(Target, true);
        return service;
    }

    private sealed class InputSource : IProGpuDragInputSource
    {
        internal List<IProGpuDragInputSink> Sinks { get; } = [];
        internal IProGpuDragInputSink? Active { get; set; }
        internal Exception? BeginFailure { get; set; }
        internal Exception? EndFailure { get; set; }
        internal Action<IProGpuDragInputSink>? OnBegin { get; set; }
        internal Action<IProGpuDragInputSink>? OnEnd { get; set; }
        internal ProGpuDragInput Initial { get; set; } = Released;
        internal int EndCount { get; set; }

        public ProGpuDragInput BeginDrag(IProGpuDragInputSink sink)
        {
            Assert.Null(Active);
            Sinks.Add(sink);
            if (BeginFailure is not null) throw BeginFailure;
            Active = sink;
            OnBegin?.Invoke(sink);
            return Initial;
        }

        public void EndDrag(IProGpuDragInputSink sink)
        {
            Assert.Same(Active, sink);
            Active = null;
            EndCount++;
            OnEnd?.Invoke(sink);
            if (EndFailure is not null) throw EndFailure;
        }
    }

    private sealed class Session : ILibreDragDropSession
    {
        internal Func<int, LibreDragAction>? Query { get; set; }
        internal int QueryCount { get; set; }
        internal int DropCount { get; set; }
        public LibreHandle HitTest(LibrePoint position) => Target;
        public LibreDragTransition Enter(LibreHandle target, int keys, LibrePoint position, LibreDragDropEffects effect)
            => new(target, LibreDragDropEffects.Copy);
        public LibreDragDropEffects Over(LibreHandle target, int keys, LibrePoint position, LibreDragDropEffects effect) => effect;
        public void Leave(LibreHandle target) { }
        public LibreDragDropEffects Drop(LibreHandle target, int keys, LibrePoint position, LibreDragDropEffects effect)
        { DropCount++; return effect; }
        public LibreDragAction QueryContinue(int keys, bool escape)
        { QueryCount++; return Query?.Invoke(QueryCount) ?? (escape ? LibreDragAction.Cancel : LibreDragAction.Continue); }
        public bool GiveFeedback(LibreDragDropEffects effect) => true;
    }

    private sealed class EmptyData : ILibreDataTransfer
    {
        public IReadOnlyList<string> Formats => [];
        public bool Contains(string format, bool autoConvert) => false;
        public object? GetData(string format, bool autoConvert) => null;
    }
}
