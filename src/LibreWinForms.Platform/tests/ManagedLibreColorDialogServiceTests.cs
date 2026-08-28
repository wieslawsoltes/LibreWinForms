// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using FluentAssertions;
using Xunit;

namespace LibreWinForms.Platform.Tests;

public class ManagedLibreColorDialogServiceTests
{
    [Fact]
    public void Show_CreatesPaintsAndSelectsFromTypedPalette()
    {
        using var host = new ColorDialogHost();
        ManagedLibreColorDialogService service = host.CreateService();
        Color[] customColors = [Color.Orange];
        host.EnqueueKey(LibreKey.Right);
        host.EnqueueKey(LibreKey.Enter);

        LibreColorDialogResult result = service.Show(new LibreColorDialogRequest(
            Color.Black,
            customColors,
            LibreColorDialogOptions.None,
            HelpRequested: null,
            Owner: default));

        result.Accepted.Should().BeTrue();
        result.Color.Should().Be(Color.DimGray);
        result.CustomColors.Should().HaveCount(16);
        result.CustomColors[0].ToArgb().Should().Be(Color.Orange.ToArgb());
        customColors[0] = Color.Red;
        result.CustomColors[0].ToArgb().Should().Be(Color.Orange.ToArgb());
        host.LastCreateOptions.Title.Should().Be("Color");
        host.LastCreateOptions.Options.Should().HaveFlag(LibreWindowOptions.Decorated);
        host.LastCreateOptions.Options.Should().HaveFlag(LibreWindowOptions.ToolWindow);
        host.LastCreateOptions.ShowInTaskbar.Should().BeFalse();
        host.LastCreateOptions.CanMinimize.Should().BeFalse();
        host.LastCreateOptions.CanMaximize.Should().BeFalse();
        host.LastCreateOptions.CanClose.Should().BeTrue();
        host.WindowShown.Should().BeTrue();
        host.WindowActivated.Should().BeTrue();
        host.PaintCount.Should().BeGreaterThan(0);
        host.TextDrawCount.Should().BeGreaterThan(0);
        host.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void Show_ExpandsEditsHexAndAddsOwnedCustomColor()
    {
        using var host = new ColorDialogHost();
        ManagedLibreColorDialogService service = host.CreateService();
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Enter);
        host.EnqueueText("12ab34");
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Enter);
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Enter);

        LibreColorDialogResult result = service.Show(new LibreColorDialogRequest(
            Color.Black,
            [],
            LibreColorDialogOptions.AllowFullOpen,
            HelpRequested: null,
            Owner: default));

        Color expected = Color.FromArgb(0x12, 0xAB, 0x34);
        result.Accepted.Should().BeTrue();
        result.Color.Should().Be(expected);
        result.CustomColors[0].Should().Be(expected);
        host.LastSizeConstraints.Minimum.Should().Be(new LibreSize(560, 500));
        host.LastSizeConstraints.Maximum.Should().Be(new LibreSize(560, 500));
        host.LastWindowBounds.Height.Should().Be(500);
    }

    [Fact]
    public void Show_UsesPointerSelectionAndExplicitOkButton()
    {
        using var host = new ColorDialogHost();
        ManagedLibreColorDialogService service = host.CreateService();
        host.EnqueuePointer(new LibrePoint(91, 80), LibreInputEventKind.PointerDown);
        host.EnqueuePointer(new LibrePoint(91, 80), LibreInputEventKind.PointerUp);
        host.EnqueuePointer(new LibrePoint(370, 370), LibreInputEventKind.PointerDown);
        host.EnqueuePointer(new LibrePoint(370, 370), LibreInputEventKind.PointerUp);

        LibreColorDialogResult result = service.Show(new LibreColorDialogRequest(
            Color.Black,
            [],
            LibreColorDialogOptions.None,
            HelpRequested: null,
            Owner: default));

        result.Accepted.Should().BeTrue();
        result.Color.ToArgb().Should().Be(Color.Red.ToArgb());
        host.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void Show_RaisesHelpWithoutClosingAndEscapeCancels()
    {
        using var host = new ColorDialogHost();
        ManagedLibreColorDialogService service = host.CreateService();
        int helpRequests = 0;
        host.EnqueueKey(LibreKey.Tab);
        host.EnqueueKey(LibreKey.Enter);
        host.EnqueueKey(LibreKey.Escape);

        LibreColorDialogResult result = service.Show(new LibreColorDialogRequest(
            Color.CadetBlue,
            [],
            LibreColorDialogOptions.ShowHelp,
            () => helpRequests++,
            Owner: default));

        result.Accepted.Should().BeFalse();
        result.Color.Should().Be(Color.CadetBlue);
        helpRequests.Should().Be(1);
        host.WindowCreateCount.Should().Be(1);
        host.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void Show_RejectsInvalidStateAndWrongThreadBeforeCreatingWindow()
    {
        using var host = new ColorDialogHost();
        ManagedLibreColorDialogService service = host.CreateService();
        Color[] tooManyColors = [.. Enumerable.Repeat(Color.Black, 17)];

        Action tooMany = () => service.Show(new LibreColorDialogRequest(
            Color.Black,
            tooManyColors,
            LibreColorDialogOptions.None,
            HelpRequested: null,
            Owner: default));
        tooMany.Should().Throw<ArgumentException>();

        Action inconsistent = () => service.Show(new LibreColorDialogRequest(
            Color.Black,
            [],
            LibreColorDialogOptions.FullOpen,
            HelpRequested: null,
            Owner: default));
        inconsistent.Should().Throw<ArgumentException>();

        host.HasDispatcherAccess = false;
        Action wrongThread = () => service.Show(new LibreColorDialogRequest(
            Color.Black,
            [],
            LibreColorDialogOptions.None,
            HelpRequested: null,
            Owner: default));
        wrongThread.Should().Throw<InvalidOperationException>()
            .WithMessage("*owning dispatcher thread*");
        host.WindowCreateCount.Should().Be(0);
    }

    private sealed class ColorDialogHost :
        ILibreDispatcher,
        ILibreWindowService,
        ILibreMonitorService,
        ILibrePaintService,
        ILibreTextRendererService,
        IDisposable
    {
        private readonly Queue<Action> _nestedActions = new();
        private TestWindow? _window;

        internal ManagedLibreHandleRegistry Handles { get; } = new();

        internal bool HasDispatcherAccess { get; set; } = true;

        internal LibreWindowCreateOptions LastCreateOptions { get; private set; }

        internal (LibreSize Minimum, LibreSize Maximum) LastSizeConstraints { get; set; }

        internal LibreRectangle LastWindowBounds => _window?.Bounds ?? default;

        internal int WindowCreateCount { get; private set; }

        internal bool WindowShown { get; set; }

        internal bool WindowActivated { get; set; }

        internal int PaintCount { get; private set; }

        internal int TextDrawCount { get; private set; }

        public int ManagedThreadId => Environment.CurrentManagedThreadId;

        public bool CheckAccess() => HasDispatcherAccess;

        internal ManagedLibreColorDialogService CreateService()
            => new(this, Handles, this, this, this, this);

        internal void EnqueueKey(LibreKey key, LibreInputModifiers modifiers = LibreInputModifiers.None)
            => EnqueueInput(new LibreInputEvent(
                LibreInputEventKind.KeyDown,
                1,
                modifiers,
                key,
                null,
                default,
                default,
                LibrePointerButton.None));

        internal void EnqueueText(string value)
            => EnqueueInput(new LibreInputEvent(
                LibreInputEventKind.TextInput,
                1,
                LibreInputModifiers.None,
                LibreKey.Unknown,
                value,
                default,
                default,
                LibrePointerButton.None));

        internal void EnqueuePointer(LibrePoint position, LibreInputEventKind kind)
            => EnqueueInput(new LibreInputEvent(
                kind,
                1,
                LibreInputModifiers.None,
                LibreKey.Unknown,
                null,
                position,
                default,
                LibrePointerButton.Primary));

        public void Post(Action callback) => _nestedActions.Enqueue(callback);

        public void Send(Action callback) => callback();

        public void PumpOnce()
        {
            if (_nestedActions.TryDequeue(out Action? action))
            {
                action();
            }
        }

        public void Run(CancellationToken cancellationToken)
            => RunNested(() => true, cancellationToken);

        public void RunNested(Func<bool> continueCondition, CancellationToken cancellationToken)
        {
            int iterations = 0;
            while (continueCondition())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_nestedActions.TryDequeue(out Action? action))
                {
                    throw new InvalidOperationException("The test modal loop ran out of input.");
                }

                action();
                if (++iterations > 32)
                {
                    throw new InvalidOperationException("The test modal loop did not terminate.");
                }
            }
        }

        public void RequestExit()
        {
        }

        public ILibreWindow Create(in LibreWindowCreateOptions options, ILibreWindowEvents events)
        {
            LastCreateOptions = options;
            WindowCreateCount++;
            _window = new TestWindow(this, Handles, options, events);
            return _window;
        }

        public IReadOnlyList<LibreMonitor> GetMonitors()
            => [new("primary", new(0, 0, 1280, 720), new(0, 0, 1280, 680), 1d, true)];

        public LibreMonitor GetNearest(LibreRectangle bounds)
        {
            _ = bounds;
            return GetMonitors()[0];
        }

        public Graphics CreateGraphics(LibreHandle target, LibrePoint origin, LibreRectangle clipRectangle)
        {
            _ = target;
            _ = origin;
            _ = clipRectangle;
            throw new NotSupportedException();
        }

        public void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle)
        {
            _ = dirtyRectangle;
            InvalidateAll(target);
        }

        public void InvalidateAll(LibreHandle target)
        {
            target.Should().Be(_window!.Handle);
            using var bitmap = new Bitmap(
                Math.Max(1, _window.Bounds.Width),
                Math.Max(1, _window.Bounds.Height));
            using Graphics graphics = Graphics.FromImage(bitmap);
            _window.Events.PaintRequested(new TestPaintFrame(
                graphics,
                new LibreRectangle(0, 0, bitmap.Width, bitmap.Height)));
            PaintCount++;
        }

        public void Present(LibreHandle target)
        {
            _ = target;
        }

        public void DrawText(
            Graphics graphics,
            string text,
            Font? font,
            Rectangle bounds,
            Color foreColor,
            Color backColor,
            LibreTextFormat format)
        {
            _ = text;
            _ = font;
            _ = backColor;
            _ = format;
            TextDrawCount++;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                using var brush = new SolidBrush(foreColor);
                graphics.FillRectangle(brush, bounds);
            }
        }

        public Size MeasureText(
            Graphics? graphics,
            string text,
            Font? font,
            Size proposedSize,
            LibreTextFormat format)
        {
            _ = graphics;
            _ = font;
            _ = format;
            return new Size(Math.Min(proposedSize.Width, Math.Max(1, text.Length * 7)), 18);
        }

        public void Dispose()
        {
            _window?.Dispose();
            Handles.Count.Should().Be(0);
        }

        private void EnqueueInput(LibreInputEvent inputEvent)
            => _nestedActions.Enqueue(() => _window!.Events.Input(inputEvent));

        private sealed class TestPaintFrame(Graphics graphics, LibreRectangle bounds) : ILibrePaintFrame
        {
            public Graphics Graphics { get; } = graphics;

            public LibreRectangle SurfaceBounds { get; } = bounds;

            public LibreRectangle DirtyRectangle { get; } = bounds;
        }

        private sealed class TestWindow : ILibreWindow
        {
            private readonly ColorDialogHost _host;
            private readonly ManagedLibreHandleRegistry _handles;
            private bool _disposed;
            private LibreRectangle _bounds;

            internal TestWindow(
                ColorDialogHost host,
                ManagedLibreHandleRegistry handles,
                in LibreWindowCreateOptions options,
                ILibreWindowEvents events)
            {
                _host = host;
                _handles = handles;
                Events = events;
                Title = options.Title;
                Owner = options.Owner;
                _bounds = options.Bounds;
                ShowInTaskbar = options.ShowInTaskbar;
                CanMinimize = options.CanMinimize;
                CanMaximize = options.CanMaximize;
                CanClose = options.CanClose;
                Handle = handles.Allocate(this, LibreHandleKind.Window);
            }

            internal ILibreWindowEvents Events { get; }

            public LibreHandle Handle { get; }

            public string Title { get; set; }

            public LibreHandle Owner { get; set; }

            public LibreRectangle Bounds
            {
                get => _bounds;
                set => _bounds = value;
            }

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

            public LibreWindowCoordinateMode CoordinateMode => LibreWindowCoordinateMode.Logical;

            public double FramebufferScale => 1d;

            public double DpiScale => 1d;

            public void SetZOrder(LibreWindowZOrder value)
            {
                _ = value;
            }

            public void SetCursor(LibreCursorShape shape)
            {
                _ = shape;
            }

            public void SetSizeConstraints(LibreSize minimum, LibreSize maximum)
                => _host.LastSizeConstraints = (minimum, maximum);

            public void SetIcons(IReadOnlyList<LibreWindowIcon> icons)
            {
                _ = icons;
            }

            public void Show()
            {
                Visible = true;
                _host.WindowShown = true;
            }

            public void Hide() => Visible = false;

            public void Activate() => _host.WindowActivated = true;

            public void PresentPendingPaint()
            {
            }

            public void Close()
            {
                if (Events.Closing())
                {
                    Dispose();
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Visible = false;
                _handles.Release(Handle);
                Events.Closed();
            }
        }
    }
}
