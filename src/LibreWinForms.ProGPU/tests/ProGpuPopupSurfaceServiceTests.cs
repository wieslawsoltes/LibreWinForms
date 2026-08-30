// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.Scene;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuPopupSurfaceServiceTests
{
    [Fact]
    public void CreateUpdateAndHideOwnOneNonActivatingRetainedPopup()
    {
        using ProGpuDispatcher dispatcher = new();
        var windows = new FakeWindowService();
        var adorners = new FakeAdornerService();
        using var service = new ProGpuPopupSurfaceService(dispatcher, windows, adorners);
        LibreHandle owner = new((nint)99, LibreHandleKind.Window);
        LibrePopupId id = new(7);
        LibrePopupSurfaceRequest initial = new(
            owner,
            id,
            new LibreRectangle(100, 120, 180, 48),
            1.25d,
            InputTransparent: true,
            LibrePopupDismissalPolicy.Explicit);

        using (Graphics graphics = service.CreateGraphics(initial))
        {
            graphics.FillRectangle(Brushes.Red, 0, 0, 10, 10);
        }

        windows.Created.Should().ContainSingle();
        FakeWindow window = windows.Created.Single();
        windows.LastOptions.Owner.Should().Be(owner);
        windows.LastOptions.Bounds.Should().Be(initial.ScreenBounds);
        windows.LastOptions.Options.Should().HaveFlag(LibreWindowOptions.Popup);
        windows.LastOptions.Options.Should().HaveFlag(LibreWindowOptions.TopMost);
        windows.LastOptions.Options.Should().HaveFlag(LibreWindowOptions.ToolWindow);
        windows.LastOptions.Options.Should().HaveFlag(LibreWindowOptions.InputTransparent);
        windows.LastOptions.Options.Should().NotHaveFlag(LibreWindowOptions.Decorated);
        windows.LastOptions.ShowInTaskbar.Should().BeFalse();
        window.Visible.Should().BeTrue();
        window.ActivateCount.Should().Be(0);
        window.LastZOrder.Should().Be(LibreWindowZOrder.Front);
        adorners.LastOwner.Should().Be(window.Handle);
        adorners.LastBounds.Should().Be(new LibreRectangle(0, 0, 180, 48));

        LibrePopupSurfaceRequest updated = initial with
        {
            ScreenBounds = new LibreRectangle(130, 150, 220, 60),
        };
        using Graphics replacement = service.CreateGraphics(updated);

        windows.Created.Should().ContainSingle();
        window.Bounds.Should().Be(updated.ScreenBounds);
        window.LastMinimumSize.Should().Be(new LibreSize(220, 60));
        window.LastMaximumSize.Should().Be(new LibreSize(220, 60));

        service.Hide(owner, id);
        service.Hide(owner, id);

        window.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void UnsupportedAutomaticDismissalFailsBeforeCreatingAWindow()
    {
        using ProGpuDispatcher dispatcher = new();
        var windows = new FakeWindowService();
        using var service = new ProGpuPopupSurfaceService(
            dispatcher,
            windows,
            new FakeAdornerService());
        LibrePopupSurfaceRequest request = new(
            new LibreHandle((nint)99, LibreHandleKind.Window),
            new LibrePopupId(7),
            new LibreRectangle(100, 120, 180, 48),
            1d,
            InputTransparent: true,
            LibrePopupDismissalPolicy.PointerPressedOutside);

        Action create = () => service.CreateGraphics(request);

        create.Should().Throw<PlatformNotSupportedException>();
        windows.Created.Should().BeEmpty();
    }

    private sealed class FakeWindowService : ILibreWindowService
    {
        private long _nextHandle;

        internal List<FakeWindow> Created { get; } = [];

        internal LibreWindowCreateOptions LastOptions { get; private set; }

        public ILibreWindow Create(in LibreWindowCreateOptions options, ILibreWindowEvents events)
        {
            LastOptions = options;
            var window = new FakeWindow(
                new LibreHandle((nint)Interlocked.Increment(ref _nextHandle), LibreHandleKind.Window),
                options,
                events);
            Created.Add(window);
            return window;
        }
    }

    private sealed class FakeAdornerService : ILibreAdornerService
    {
        internal LibreHandle LastOwner { get; private set; }

        internal LibreRectangle LastBounds { get; private set; }

        public Graphics CreateGraphics(
            LibreHandle owner,
            LibreAdornerId adorner,
            LibreRectangle bounds,
            LibreRectangle clipRectangle)
        {
            LastOwner = owner;
            LastBounds = bounds;
            DrawingContext recording = new();
            return Graphics.FromProGpuDrawingContext(recording);
        }

        public void Remove(LibreHandle owner, LibreAdornerId adorner)
        {
        }
    }

    private sealed class FakeWindow : ILibreWindow
    {
        private readonly ILibreWindowEvents _events;

        internal FakeWindow(
            LibreHandle handle,
            in LibreWindowCreateOptions options,
            ILibreWindowEvents events)
        {
            Handle = handle;
            Title = options.Title;
            Owner = options.Owner;
            Bounds = options.Bounds;
            State = options.InitialState;
            TopMost = options.Options.HasFlag(LibreWindowOptions.TopMost);
            ShowInTaskbar = options.ShowInTaskbar;
            CanMinimize = options.CanMinimize;
            CanMaximize = options.CanMaximize;
            CanClose = options.CanClose;
            _events = events;
        }

        public LibreHandle Handle { get; }

        public string Title { get; set; }

        public LibreHandle Owner { get; set; }

        public LibreRectangle Bounds { get; set; }

        public LibreWindowState State { get; set; }

        public bool Visible { get; private set; }

        public bool Enabled { get; set; } = true;

        public bool TopMost { get; set; }

        public LibreWindowBorder Border { get; set; }

        public bool ShowInTaskbar { get; set; }

        public bool CanMinimize { get; set; }

        public bool CanMaximize { get; set; }

        public bool CanClose { get; set; }

        public double Opacity { get; set; } = 1d;

        internal LibreWindowZOrder? LastZOrder { get; private set; }

        internal LibreSize LastMinimumSize { get; private set; }

        internal LibreSize LastMaximumSize { get; private set; }

        internal int ActivateCount { get; private set; }

        internal int DisposeCount { get; private set; }

        public void SetZOrder(LibreWindowZOrder value) => LastZOrder = value;

        public void SetCursor(LibreCursorShape shape)
        {
        }

        public void SetSizeConstraints(LibreSize minimum, LibreSize maximum)
        {
            LastMinimumSize = minimum;
            LastMaximumSize = maximum;
        }

        public LibreWindowCoordinateMode CoordinateMode => LibreWindowCoordinateMode.Logical;

        public double FramebufferScale => 1d;

        public double DpiScale => 1d;

        public void SetIcons(IReadOnlyList<LibreWindowIcon> icons)
        {
        }

        public void Show() => Visible = true;

        public void Hide() => Visible = false;

        public void Activate() => ActivateCount++;

        public void PresentPendingPaint()
        {
        }

        public void Close()
        {
            if (_events.Closing())
            {
                Visible = false;
                _events.Closed();
            }
        }

        public void Dispose() => DisposeCount++;
    }
}
