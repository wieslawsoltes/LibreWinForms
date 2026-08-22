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
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: true);
        using Form form = new() { Text = "Canonical portable lifecycle" };
        using InputProbeControl child = new() { Bounds = new Rectangle(12, 18, 120, 60) };
        form.Controls.Add(child);

        List<string> events = [];
        int closeAttempts = 0;
        int paintCallbacks = 0;
        Rectangle formPaintClip = default;
        Rectangle childPaintClip = default;
        RectangleF visibleClip = default;
        List<string> inputEvents = [];
        Point mouseLocation = default;
        Point mousePosition = default;
        bool focusedDuringGotFocus = false;
        bool containsFocusDuringKeyDown = false;
        bool shiftSeenDuringKeyDown = false;
        bool leftButtonSeenDuringMouseDown = false;
        bool captureSeenDuringMouseDown = false;
        bool noButtonSeenDuringMouseUp = false;
        Keys keyCode = Keys.None;
        char keyChar = default;
        int wheelDelta = 0;
        Exception? inputException = null;
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
        child.GotFocus += (_, _) =>
        {
            inputEvents.Add(nameof(child.GotFocus));
            focusedDuringGotFocus = child.Focused;
        };
        child.LostFocus += (_, _) => inputEvents.Add(nameof(child.LostFocus));
        child.MouseEnter += (_, _) => inputEvents.Add(nameof(child.MouseEnter));
        child.MouseMove += (_, e) =>
        {
            inputEvents.Add(nameof(child.MouseMove));
            mouseLocation = e.Location;
            mousePosition = Control.MousePosition;
        };
        child.MouseDown += (_, _) =>
        {
            inputEvents.Add(nameof(child.MouseDown));
            leftButtonSeenDuringMouseDown = Control.MouseButtons == MouseButtons.Left;
            captureSeenDuringMouseDown = child.Capture;
        };
        child.Click += (_, _) => inputEvents.Add(nameof(child.Click));
        child.MouseUp += (_, _) =>
        {
            inputEvents.Add(nameof(child.MouseUp));
            noButtonSeenDuringMouseUp = Control.MouseButtons == MouseButtons.None;
        };
        child.MouseWheel += (_, e) =>
        {
            inputEvents.Add(nameof(child.MouseWheel));
            wheelDelta = e.Delta;
        };
        child.KeyDown += (_, e) =>
        {
            inputEvents.Add(nameof(child.KeyDown));
            keyCode = e.KeyCode;
            shiftSeenDuringKeyDown = Control.ModifierKeys == Keys.Shift;
            containsFocusDuringKeyDown = form.ContainsFocus && child.ContainsFocus;
        };
        child.KeyPress += (_, e) =>
        {
            inputEvents.Add(nameof(child.KeyPress));
            keyChar = e.KeyChar;
        };
        child.KeyUp += (_, _) => inputEvents.Add(nameof(child.KeyUp));
        form.HandleCreated += (_, _) => events.Add(nameof(form.HandleCreated));
        form.VisibleChanged += (_, _) => events.Add(nameof(form.VisibleChanged));
        form.Shown += (_, _) => events.Add(nameof(form.Shown));
        form.Shown += (_, _) =>
        {
            form.Bounds = new(40, 50, 640, 480);
            form.Invalidate();
            form.Update();
            try
            {
                platform.SendInput(LibreInputEventKind.FocusGained);
                platform.SendInput(LibreInputEventKind.PointerMove, position: new(17, 24));
                platform.SendInput(LibreInputEventKind.PointerDown, position: new(17, 24), button: LibrePointerButton.Primary);
                platform.SendInput(LibreInputEventKind.PointerUp, position: new(17, 24), button: LibrePointerButton.Primary);
                platform.SendInput(LibreInputEventKind.PointerWheel, position: new(17, 24), delta: new(0, 120));
                platform.SendInput(LibreInputEventKind.KeyDown, modifiers: LibreInputModifiers.Shift, key: LibreKey.A);
                platform.SendInput(LibreInputEventKind.TextInput, modifiers: LibreInputModifiers.Shift, text: "a");
                platform.SendInput(LibreInputEventKind.KeyUp, key: LibreKey.A);
                platform.SendInput(LibreInputEventKind.FocusLost);
            }
            catch (Exception exception)
            {
                inputException = exception;
            }
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
        inputException.Should().BeNull();
        inputEvents.Should().ContainInOrder(
            nameof(child.MouseEnter),
            nameof(child.MouseMove),
            nameof(child.GotFocus),
            nameof(child.MouseDown),
            nameof(child.Click),
            nameof(child.MouseUp),
            nameof(child.MouseWheel),
            nameof(child.KeyDown),
            nameof(child.KeyPress),
            nameof(child.KeyUp),
            nameof(child.LostFocus));
        mouseLocation.Should().Be(new Point(5, 6));
        mousePosition.Should().Be(new Point(57, 74));
        focusedDuringGotFocus.Should().BeTrue();
        containsFocusDuringKeyDown.Should().BeTrue();
        shiftSeenDuringKeyDown.Should().BeTrue();
        leftButtonSeenDuringMouseDown.Should().BeTrue();
        captureSeenDuringMouseDown.Should().BeTrue();
        noButtonSeenDuringMouseUp.Should().BeTrue();
        keyCode.Should().Be(Keys.A);
        keyChar.Should().Be('a');
        wheelDelta.Should().Be(120);
        form.IsDisposed.Should().BeTrue();
        form.IsHandleCreated.Should().BeFalse();
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void ApplicationRun_OwnedAndNestedModalForms_PreserveCanonicalState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { Text = "Owner" };
        using Control ownerChild = new();
        using Form tool = new() { Text = "Owned tool" };
        using Form firstDialog = new() { Text = "First dialog" };
        using Form nestedDialog = new() { Text = "Nested dialog" };

        DialogResult firstResult = DialogResult.None;
        DialogResult nestedResult = DialogResult.None;
        bool ownerPublicEnabledDuringFirst = false;
        bool ownerPlatformEnabledAfterChildDisable = false;
        bool ownerPlatformDisabledDuringFirst = false;
        bool toolPlatformDisabledDuringFirst = false;
        bool firstPlatformDisabledDuringNested = false;
        bool ownerStillDisabledAfterNested = false;
        bool firstRestoredAfterNested = false;
        bool ownerRestoredAfterFirst = false;
        bool toolRestoredAfterFirst = false;
        LibreHandle toolOwner = default;
        LibreHandle firstOwner = default;
        LibreHandle nestedOwner = default;
        Exception? modalException = null;
        List<string> events = [];
        owner.Controls.Add(ownerChild);

        nestedDialog.Shown += (_, _) =>
        {
            try
            {
                events.Add("nested-shown");
                platform.TrackForm(nestedDialog);
                firstPlatformDisabledDuringNested = !platform.IsWindowEnabled(firstDialog);
                nestedOwner = platform.GetWindowOwner(nestedDialog);
                nestedDialog.Modal.Should().BeTrue();
                nestedDialog.Owner.Should().BeNull();
                nestedDialog.DialogResult = DialogResult.Retry;
            }
            catch (Exception exception)
            {
                modalException = exception;
                nestedDialog.DialogResult = DialogResult.Abort;
            }
        };
        firstDialog.Shown += (_, _) =>
        {
            try
            {
                events.Add("first-shown");
                platform.TrackForm(firstDialog);
                ownerPublicEnabledDuringFirst = owner.Enabled;
                ownerPlatformDisabledDuringFirst = !platform.IsWindowEnabled(owner);
                toolPlatformDisabledDuringFirst = !platform.IsWindowEnabled(tool);
                firstOwner = platform.GetWindowOwner(firstDialog);
                firstDialog.Modal.Should().BeTrue();
                firstDialog.Owner.Should().Be(owner);

                firstDialog.Activate();
                nestedResult = nestedDialog.ShowDialog();
                events.Add("nested-returned");
                firstRestoredAfterNested = platform.IsWindowEnabled(firstDialog);
                ownerStillDisabledAfterNested = !platform.IsWindowEnabled(owner);
                firstDialog.DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                modalException = exception;
                firstDialog.DialogResult = DialogResult.Abort;
            }
        };
        owner.Shown += (_, _) =>
        {
            try
            {
                events.Add("owner-shown");
                platform.TrackForm(owner);
                owner.Activate();
                ownerChild.Enabled = false;
                ownerPlatformEnabledAfterChildDisable = platform.IsWindowEnabled(owner);
                ownerChild.Enabled = true;
                tool.Owner = owner;
                tool.Show();
                platform.TrackForm(tool);
                toolOwner = platform.GetWindowOwner(tool);

                firstResult = firstDialog.ShowDialog(owner);
                events.Add("first-returned");
                ownerRestoredAfterFirst = platform.IsWindowEnabled(owner);
                toolRestoredAfterFirst = platform.IsWindowEnabled(tool);
            }
            catch (Exception exception)
            {
                modalException = exception;
            }
            finally
            {
                tool.Close();
                owner.Close();
            }
        };

        Application.Run(owner);

        modalException.Should().BeNull();
        firstResult.Should().Be(DialogResult.OK);
        nestedResult.Should().Be(DialogResult.Retry);
        events.Should().ContainInOrder(
            "owner-shown",
            "first-shown",
            "nested-shown",
            "nested-returned",
            "first-returned");
        ownerPublicEnabledDuringFirst.Should().BeTrue();
        ownerPlatformEnabledAfterChildDisable.Should().BeTrue();
        ownerPlatformDisabledDuringFirst.Should().BeTrue();
        toolPlatformDisabledDuringFirst.Should().BeTrue();
        firstPlatformDisabledDuringNested.Should().BeTrue();
        firstRestoredAfterNested.Should().BeTrue();
        ownerStillDisabledAfterNested.Should().BeTrue();
        ownerRestoredAfterFirst.Should().BeTrue();
        toolRestoredAfterFirst.Should().BeTrue();
        toolOwner.Should().Be(platform.GetFormerWindowHandle(owner));
        firstOwner.Should().Be(platform.GetFormerWindowHandle(owner));
        nestedOwner.Should().Be(platform.GetFormerWindowHandle(firstDialog));
        firstDialog.Owner.Should().BeNull();
        nestedDialog.Owner.Should().BeNull();
        platform.LastActivatedWindow.Should().Be(platform.GetFormerWindowHandle(owner));
        platform.WindowsCreated.Should().Be(4);
        platform.Handles.Count.Should().Be(0);
    }

    private static HeadlessPlatform UseHeadlessPlatform(bool autoCloseWindows)
    {
        HeadlessPlatform platform;
        if (LibrePlatform.IsRegistered)
        {
            platform = LibrePlatform.Current.Dispatcher.Should().BeOfType<HeadlessPlatform>().Subject;
            platform.Reset(autoCloseWindows);
        }
        else
        {
            platform = new HeadlessPlatform(autoCloseWindows);
            LibrePlatform.Register(platform.Services);
        }

        return platform;
    }

    private sealed class InputProbeControl : Control
    {
        internal InputProbeControl()
            => SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.UserPaint, true);
    }

    private sealed class HeadlessPlatform :
        ILibreDispatcher,
        ILibreTimerService,
        ILibreWindowService,
        ILibreMonitorService,
        ILibrePaintService
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        private bool _autoCloseWindows;
        private readonly Dictionary<Form, LibreHandle> _formHandles = [];
        private bool _exitRequested;
        private HeadlessWindow? _lastWindow;

        internal HeadlessPlatform(bool autoCloseWindows = true)
        {
            _autoCloseWindows = autoCloseWindows;
            Handles = new ManagedLibreHandleRegistry();
            Services = new LibrePlatformServices(this, this, Handles, this, this, this);
        }

        internal void Reset(bool autoCloseWindows)
        {
            Handles.Count.Should().Be(0);
            _autoCloseWindows = autoCloseWindows;
            _exitRequested = false;
            _lastWindow = null;
            _formHandles.Clear();
            while (_queue.TryDequeue(out _))
            {
            }

            WindowsCreated = 0;
            LastWindowBounds = default;
            LastDirtyRectangle = default;
            PresentCount = 0;
            LastPaintCommandCount = 0;
            SawFormPaintFill = false;
            SawTranslatedChildPaintFill = false;
            LastActivatedWindow = default;
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

        internal LibreHandle LastActivatedWindow { get; private set; }

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
            for (int iterations = 0; continueCondition() && !cancellationToken.IsCancellationRequested; iterations++)
            {
                if (iterations >= 100)
                {
                    throw new InvalidOperationException("The canonical nested modal loop did not terminate.");
                }

                PumpOnce();
            }
        }

        public void RequestExit() => _exitRequested = true;

        public IDisposable Start(TimeSpan interval, bool repeating, Action callback)
            => new EmptyDisposable();

        public ILibreWindow Create(in LibreWindowCreateOptions options, ILibreWindowEvents events)
        {
            WindowsCreated++;
            _lastWindow = new HeadlessWindow(this, options, events);
            return _lastWindow;
        }

        internal void TrackForm(Form form)
            => _formHandles[form] = GetWindowHandle(form);

        internal LibreHandle GetWindowHandle(Form form)
            => new(form.Handle, LibreHandleKind.Window);

        internal LibreHandle GetFormerWindowHandle(Form form)
            => _formHandles[form];

        internal bool IsWindowEnabled(Form form)
        {
            Handles.TryGet(GetWindowHandle(form), out HeadlessWindow? window).Should().BeTrue();
            return window!.Enabled;
        }

        internal LibreHandle GetWindowOwner(Form form)
        {
            Handles.TryGet(GetWindowHandle(form), out HeadlessWindow? window).Should().BeTrue();
            return window!.Owner;
        }

        internal void SendInput(
            LibreInputEventKind kind,
            LibreInputModifiers modifiers = LibreInputModifiers.None,
            LibreKey key = LibreKey.Unknown,
            string? text = null,
            LibrePoint position = default,
            LibrePoint delta = default,
            LibrePointerButton button = LibrePointerButton.None)
        {
            _lastWindow.Should().NotBeNull();
            _lastWindow!.SendInput(new LibreInputEvent(kind, 1, modifiers, key, text, position, delta, button));
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
                Owner = options.Owner;
                Visible = options.Options.HasFlag(LibreWindowOptions.Visible);
                Handle = platform.Handles.Allocate(this, LibreHandleKind.Window);
            }

            public LibreHandle Handle { get; }

            public LibreHandle Owner { get; set; }

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

            public bool Enabled { get; set; } = true;

            public double DpiScale => 1;

            public void Show()
            {
                Visible = true;
                if (_platform._autoCloseWindows)
                {
                    _platform.Post(Close);
                }
            }

            public void Hide() => Visible = false;

            public void Activate() => _platform.LastActivatedWindow = Handle;

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

            internal void SendInput(in LibreInputEvent inputEvent)
            {
                if (Enabled || inputEvent.Kind == LibreInputEventKind.FocusLost)
                {
                    _events.Input(inputEvent);
                }
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
