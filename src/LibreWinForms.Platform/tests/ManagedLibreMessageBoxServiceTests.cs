// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using FluentAssertions;
using Xunit;

namespace LibreWinForms.Platform.Tests;

public class ManagedLibreMessageBoxServiceTests
{
    [Fact]
    public void Show_CreatesPaintsAndNavigatesRealTypedModalWindow()
    {
        using var host = new MessageBoxHost();
        var service = new ManagedLibreMessageBoxService(
            host,
            host.Handles,
            host,
            host,
            host,
            host);
        host.EnqueueInput(new LibreInputEvent(
            LibreInputEventKind.KeyDown,
            1,
            LibreInputModifiers.None,
            LibreKey.Right,
            null,
            default,
            default,
            LibrePointerButton.None));
        host.EnqueueInput(new LibreInputEvent(
            LibreInputEventKind.KeyDown,
            2,
            LibreInputModifiers.None,
            LibreKey.Enter,
            null,
            default,
            default,
            LibrePointerButton.None));

        LibreMessageBoxResult result = service.Show(new LibreMessageBoxRequest(
            "Choose one of the available actions.",
            "Question",
            LibreMessageBoxButtons.YesNo,
            LibreMessageBoxIcon.Question,
            LibreMessageBoxDefaultButton.Button1,
            LibreMessageBoxOptions.RightAlign | LibreMessageBoxOptions.RightToLeftReading,
            ShowHelp: false,
            Owner: default));

        result.Should().Be(LibreMessageBoxResult.No);
        host.LastCreateOptions.Title.Should().Be("Question");
        host.LastCreateOptions.Options.Should().HaveFlag(LibreWindowOptions.Decorated);
        host.LastCreateOptions.Options.Should().HaveFlag(LibreWindowOptions.ToolWindow);
        host.LastCreateOptions.ShowInTaskbar.Should().BeFalse();
        host.LastCreateOptions.CanMinimize.Should().BeFalse();
        host.LastCreateOptions.CanMaximize.Should().BeFalse();
        host.LastCreateOptions.CanClose.Should().BeFalse();
        host.LastCreateOptions.MinimumSize.Should().Be(host.LastCreateOptions.MaximumSize);
        host.WindowShown.Should().BeTrue();
        host.WindowActivated.Should().BeTrue();
        host.PaintCount.Should().BeGreaterThan(0);
        host.TextDrawCount.Should().BeGreaterThan(0);
        host.LastTextFormat.Should().HaveFlag(LibreTextFormat.Right);
        host.LastTextFormat.Should().HaveFlag(LibreTextFormat.RightToLeft);
        host.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void Show_UsesEscapeForCancelAndRejectsCloseWhenButtonsHaveNoCancelResult()
    {
        using var cancelHost = new MessageBoxHost();
        var cancelService = cancelHost.CreateService();
        cancelHost.EnqueueInput(new LibreInputEvent(
            LibreInputEventKind.KeyDown,
            1,
            LibreInputModifiers.None,
            LibreKey.Escape,
            null,
            default,
            default,
            LibrePointerButton.None));

        cancelService.Show(CreateRequest(LibreMessageBoxButtons.OKCancel))
            .Should().Be(LibreMessageBoxResult.Cancel);
        cancelHost.LastCreateOptions.CanClose.Should().BeTrue();

        using var noCancelHost = new MessageBoxHost();
        var noCancelService = noCancelHost.CreateService();
        noCancelHost.EnqueueCloseAttempt();
        noCancelHost.EnqueueInput(new LibreInputEvent(
            LibreInputEventKind.KeyDown,
            2,
            LibreInputModifiers.None,
            LibreKey.Enter,
            null,
            default,
            default,
            LibrePointerButton.None));

        noCancelService.Show(CreateRequest(LibreMessageBoxButtons.YesNo))
            .Should().Be(LibreMessageBoxResult.Yes);
        noCancelHost.CloseWasRejected.Should().BeTrue();
        noCancelHost.LastCreateOptions.CanClose.Should().BeFalse();
    }

    [Fact]
    public void Show_RejectsHelpAndWrongThreadBeforeCreatingWindow()
    {
        using var host = new MessageBoxHost();
        ManagedLibreMessageBoxService service = host.CreateService();
        LibreMessageBoxRequest helpRequest = CreateRequest(LibreMessageBoxButtons.OK) with { ShowHelp = true };

        Action showHelp = () => service.Show(helpRequest);
        showHelp.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*local-OS help launcher*");
        host.WindowCreateCount.Should().Be(0);

        host.HasDispatcherAccess = false;
        Action wrongThread = () => service.Show(CreateRequest(LibreMessageBoxButtons.OK));
        wrongThread.Should().Throw<InvalidOperationException>()
            .WithMessage("*owning dispatcher thread*");
        host.WindowCreateCount.Should().Be(0);
    }

    private static LibreMessageBoxRequest CreateRequest(LibreMessageBoxButtons buttons)
        => new(
            "message",
            "caption",
            buttons,
            LibreMessageBoxIcon.None,
            LibreMessageBoxDefaultButton.Button1,
            LibreMessageBoxOptions.None,
            ShowHelp: false,
            Owner: default);

    private sealed class MessageBoxHost :
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

        internal int WindowCreateCount { get; private set; }

        internal bool WindowShown { get; set; }

        internal bool WindowActivated { get; set; }

        internal bool CloseWasRejected { get; set; }

        internal int PaintCount { get; private set; }

        internal int TextDrawCount { get; private set; }

        internal LibreTextFormat LastTextFormat { get; private set; }

        public int ManagedThreadId => Environment.CurrentManagedThreadId;

        public bool CheckAccess() => HasDispatcherAccess;

        internal ManagedLibreMessageBoxService CreateService()
            => new(this, Handles, this, this, this, this);

        internal void EnqueueInput(LibreInputEvent inputEvent)
            => _nestedActions.Enqueue(() => _window!.Events.Input(inputEvent));

        internal void EnqueueCloseAttempt()
            => _nestedActions.Enqueue(() => CloseWasRejected = !_window!.TryClose());

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
                if (++iterations > 16)
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
                Math.Max(1, LastCreateOptions.Bounds.Width),
                Math.Max(1, LastCreateOptions.Bounds.Height));
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
            TextDrawCount++;
            LastTextFormat |= format;
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
            int width = Math.Min(Math.Max(1, proposedSize.Width), Math.Max(40, text.Length * 7));
            int lines = Math.Max(1, (text.Length * 7 + width - 1) / width);
            return new Size(width, lines * 18);
        }

        public void Dispose()
        {
            _window?.Dispose();
            Handles.Count.Should().Be(0);
        }

        private sealed class TestPaintFrame(Graphics graphics, LibreRectangle bounds) : ILibrePaintFrame
        {
            public Graphics Graphics { get; } = graphics;

            public LibreRectangle SurfaceBounds { get; } = bounds;

            public LibreRectangle DirtyRectangle { get; } = bounds;
        }

        private sealed class TestWindow : ILibreWindow
        {
            private readonly MessageBoxHost _host;
            private readonly ManagedLibreHandleRegistry _handles;
            private bool _disposed;

            internal TestWindow(
                MessageBoxHost host,
                ManagedLibreHandleRegistry handles,
                in LibreWindowCreateOptions options,
                ILibreWindowEvents events)
            {
                _host = host;
                _handles = handles;
                Events = events;
                Title = options.Title;
                Owner = options.Owner;
                Bounds = options.Bounds;
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
            {
                _ = minimum;
                _ = maximum;
            }

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

            public void Close() => TryClose();

            internal bool TryClose()
            {
                if (!Events.Closing())
                {
                    return false;
                }

                Dispose();
                return true;
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
