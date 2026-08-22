// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Xunit;

namespace LibreWinForms.Platform.Tests;

public class LibrePlatformTests
{
    [Fact]
    public void Register_PublishesCompleteServicesAndRejectsReplacement()
    {
        TestServices test = new();
        LibrePlatformServices services = test.Create();

        LibrePlatform.Register(services);

        LibrePlatform.IsRegistered.Should().BeTrue();
        LibrePlatform.Current.Should().BeSameAs(services);
        Action replace = () => LibrePlatform.Register(test.Create());
        replace.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_RejectsMissingFocusedService()
    {
        TestServices test = new();
        Action create = () => new LibrePlatformServices(
            null!, test, test.Handles, test, test, test);

        create.Should().Throw<ArgumentNullException>().WithParameterName("dispatcher");
    }

    private sealed class TestServices :
        ILibreDispatcher,
        ILibreTimerService,
        ILibreWindowService,
        ILibreMonitorService,
        ILibrePaintService
    {
        public ManagedLibreHandleRegistry Handles { get; } = new();

        public LibrePlatformServices Create() => new(this, this, Handles, this, this, this);

        public int ManagedThreadId => Environment.CurrentManagedThreadId;

        public bool CheckAccess() => true;
        public void Post(Action callback) => callback();
        public void Send(Action callback) => callback();
        public void PumpOnce() { }
        public void Run(CancellationToken cancellationToken) { }
        public void RunNested(Func<bool> continueCondition, CancellationToken cancellationToken) { }
        public void RequestExit() { }
        public IDisposable Start(TimeSpan interval, bool repeating, Action callback) => new EmptyDisposable();
        public ILibreWindow Create(in LibreWindowCreateOptions options, ILibreWindowEvents events) => throw new NotSupportedException();
        public IReadOnlyList<LibreMonitor> GetMonitors() => [];
        public LibreMonitor GetNearest(LibreRectangle bounds) => default;
        public void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle) { }
        public void InvalidateAll(LibreHandle target) { }
        public void Present(LibreHandle target) { }

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
