// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.Scene;
using ProGpuSolidColorBrush = ProGPU.Vector.SolidColorBrush;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public class CanonicalLifecycleTests
{
    [Fact]
    public void ApplicationRun_CanonicalForm_UsesTypedPortableLifecycle()
    {
        HeadlessPlatform platform = new();
        LibrePlatform.Register(platform.Services);
        using Form form = new() { Text = "Canonical portable lifecycle" };
        using Panel child = new() { Bounds = new Rectangle(12, 18, 120, 60) };
        form.Controls.Add(child);

        List<string> events = [];
        int closeAttempts = 0;
        int paintCallbacks = 0;
        Rectangle formPaintClip = default;
        Rectangle childPaintClip = default;
        RectangleF visibleClip = default;
        form.Paint += (_, e) =>
        {
            paintCallbacks++;
            formPaintClip = e.ClipRectangle;
            visibleClip = e.Graphics.VisibleClipBounds;
            e.Graphics.FillRectangle(Brushes.CornflowerBlue, new Rectangle(4, 5, 24, 16));
        };
        child.Paint += (_, e) =>
        {
            paintCallbacks++;
            childPaintClip = e.ClipRectangle;
            e.Graphics.FillRectangle(Brushes.OrangeRed, new Rectangle(2, 3, 10, 8));
        };
        form.HandleCreated += (_, _) => events.Add(nameof(form.HandleCreated));
        form.VisibleChanged += (_, _) => events.Add(nameof(form.VisibleChanged));
        form.Shown += (_, _) => events.Add(nameof(form.Shown));
        form.Shown += (_, _) =>
        {
            form.Bounds = new(40, 50, 640, 480);
            form.Invalidate();
            form.Update();
        };
        form.FormClosing += (_, e) =>
        {
            events.Add(nameof(form.FormClosing));
            e.Cancel = ++closeAttempts == 1;
        };
        form.FormClosed += (_, _) => events.Add(nameof(form.FormClosed));
        form.HandleDestroyed += (_, _) => events.Add(nameof(form.HandleDestroyed));

        Application.Run(form);

        platform.WindowsCreated.Should().Be(1);
        events.Should().ContainInOrder(
            nameof(form.HandleCreated),
            nameof(form.VisibleChanged),
            nameof(form.Shown),
            nameof(form.FormClosing),
            nameof(form.FormClosing),
            nameof(form.FormClosed),
            nameof(form.HandleDestroyed));
        closeAttempts.Should().Be(2);
        platform.LastWindowBounds.Should().Be(new LibreRectangle(40, 50, 640, 480));
        platform.LastDirtyRectangle.Should().Be(new LibreRectangle(0, 0, 640, 480));
        platform.PresentCount.Should().Be(1);
        paintCallbacks.Should().Be(2);
        formPaintClip.Should().Be(new Rectangle(0, 0, 640, 480));
        childPaintClip.Should().Be(new Rectangle(0, 0, 120, 60));
        visibleClip.Should().Be(new RectangleF(0, 0, 640, 480));
        platform.LastPaintCommandCount.Should().BeGreaterThan(0);
        platform.SawFormPaintFill.Should().BeTrue();
        platform.SawTranslatedChildPaintFill.Should().BeTrue();
        form.IsDisposed.Should().BeTrue();
        form.IsHandleCreated.Should().BeFalse();
        platform.Handles.Count.Should().Be(0);
    }

    private sealed class HeadlessPlatform :
        ILibreDispatcher,
        ILibreTimerService,
        ILibreWindowService,
        ILibreMonitorService,
        ILibrePaintService
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        private bool _exitRequested;

        internal HeadlessPlatform()
        {
            Handles = new ManagedLibreHandleRegistry();
            Services = new LibrePlatformServices(this, this, Handles, this, this, this);
        }

        internal ManagedLibreHandleRegistry Handles { get; }

        internal LibrePlatformServices Services { get; }

        internal int WindowsCreated { get; private set; }

        internal LibreRectangle LastWindowBounds { get; private set; }

        internal LibreRectangle LastDirtyRectangle { get; private set; }

        internal int PresentCount { get; private set; }

        internal int LastPaintCommandCount { get; private set; }

        internal bool SawFormPaintFill { get; private set; }

        internal bool SawTranslatedChildPaintFill { get; private set; }

        public int ManagedThreadId => Environment.CurrentManagedThreadId;

        public bool CheckAccess() => true;

        public void Post(Action callback) => _queue.Enqueue(callback);

        public void Send(Action callback) => callback();

        public void PumpOnce()
        {
            if (_queue.TryDequeue(out Action? callback))
            {
                callback();
            }
        }

        public void Run(CancellationToken cancellationToken)
        {
            for (int iterations = 0; !_exitRequested && !cancellationToken.IsCancellationRequested; iterations++)
            {
                if (iterations >= 100)
                {
                    throw new InvalidOperationException("The canonical lifecycle did not terminate its dispatcher loop.");
                }

                PumpOnce();
            }
        }

        public void RunNested(Func<bool> continueCondition, CancellationToken cancellationToken)
        {
            while (continueCondition() && !cancellationToken.IsCancellationRequested)
            {
                PumpOnce();
            }
        }

        public void RequestExit() => _exitRequested = true;

        public IDisposable Start(TimeSpan interval, bool repeating, Action callback)
            => new EmptyDisposable();

        public ILibreWindow Create(in LibreWindowCreateOptions options, ILibreWindowEvents events)
        {
            WindowsCreated++;
            return new HeadlessWindow(this, options, events);
        }

        public IReadOnlyList<LibreMonitor> GetMonitors()
            => [new("headless", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1, true)];

        public LibreMonitor GetNearest(LibreRectangle bounds) => GetMonitors()[0];

        public void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle)
        {
            Handles.TryGet(target, out HeadlessWindow? window).Should().BeTrue();
            LastDirtyRectangle = dirtyRectangle;
            window!.RequestPaint(dirtyRectangle);
        }

        public void InvalidateAll(LibreHandle target) { }

        public void Present(LibreHandle target)
        {
            Handles.TryGet<object>(target, out _).Should().BeTrue();
            PresentCount++;
        }

        private sealed class HeadlessWindow : ILibreWindow
        {
            private readonly HeadlessPlatform _platform;
            private readonly ILibreWindowEvents _events;
            private bool _disposed;

            internal HeadlessWindow(
                HeadlessPlatform platform,
                in LibreWindowCreateOptions options,
                ILibreWindowEvents events)
            {
                _platform = platform;
                _events = events;
                Bounds = options.Bounds;
                Visible = options.Options.HasFlag(LibreWindowOptions.Visible);
                Handle = platform.Handles.Allocate(this, LibreHandleKind.Window);
            }

            public LibreHandle Handle { get; }

            private LibreRectangle _bounds;

            public LibreRectangle Bounds
            {
                get => _bounds;
                set
                {
                    _bounds = value;
                    _platform.LastWindowBounds = value;
                    _events.BoundsChanged(value);
                }
            }

            public LibreWindowState State { get; set; }

            public bool Visible { get; private set; }

            public double DpiScale => 1;

            public void Show()
            {
                Visible = true;
                _platform.Post(Close);
            }

            public void Hide() => Visible = false;

            public void Activate() { }

            public void Close()
            {
                if (_disposed)
                {
                    return;
                }

                if (_events.Closing())
                {
                    Dispose();
                }
                else
                {
                    _platform.Post(Close);
                }
            }

            internal void RequestPaint(LibreRectangle dirtyRectangle)
            {
                _platform.Post(() =>
                {
                    LibreRectangle surfaceBounds = new(0, 0, Bounds.Width, Bounds.Height);
                    DrawingContext context = new();
                    using Graphics graphics = Graphics.FromProGpuDrawingContext(
                        context,
                        new RectangleF(0, 0, surfaceBounds.Width, surfaceBounds.Height));
                    _events.PaintRequested(new HeadlessPaintFrame(graphics, surfaceBounds, dirtyRectangle));
                    _platform.LastPaintCommandCount = context.Commands.Count;
                    _platform.SawFormPaintFill = ContainsSolidFill(
                        context,
                        new RectangleF(4, 5, 24, 16),
                        Color.CornflowerBlue);
                    _platform.SawTranslatedChildPaintFill = ContainsSolidFill(
                        context,
                        new RectangleF(14, 21, 10, 8),
                        Color.OrangeRed);
                });
            }

            private static bool ContainsSolidFill(
                DrawingContext context,
                RectangleF expectedRectangle,
                Color expectedColor)
            {
                Vector4 expected = new(
                    expectedColor.R / 255f,
                    expectedColor.G / 255f,
                    expectedColor.B / 255f,
                    expectedColor.A / 255f);

                foreach (RenderCommand command in context.Commands)
                {
                    if (command.Type == RenderCommandType.DrawRect &&
                        command.Pen is null &&
                        command.Brush is ProGpuSolidColorBrush brush &&
                        command.Rect.X == expectedRectangle.X &&
                        command.Rect.Y == expectedRectangle.Y &&
                        command.Rect.Width == expectedRectangle.Width &&
                        command.Rect.Height == expectedRectangle.Height &&
                        brush.Color == expected)
                    {
                        return true;
                    }
                }

                return false;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Visible = false;
                _platform.Handles.Release(Handle);
                _events.Closed();
            }
        }

        private sealed record HeadlessPaintFrame(
            Graphics Graphics,
            LibreRectangle SurfaceBounds,
            LibreRectangle DirtyRectangle) : ILibrePaintFrame;

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
