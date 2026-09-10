// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public class ProGpuDispatcherTests
{
    [Fact]
    public void CreateServices_UsesTypedProGpuImplementations()
    {
        LibrePlatformServices services = ProGpuPlatform.CreateServices();

        services.Dispatcher.Should().BeOfType<ProGpuDispatcher>();
        services.ThreadDispatchers.Should().BeSameAs(services.Dispatcher);
        services.ThreadDispatchers.GetForCurrentThread().Should().BeSameAs(services.Dispatcher);
        Exception? secondaryThreadError = null;
        ILibreDispatcher? secondaryDispatcher = null;
        Thread secondaryThread = new(() =>
        {
            try
            {
                secondaryDispatcher = services.ThreadDispatchers.GetForCurrentThread();
                secondaryDispatcher.CheckAccess().Should().BeTrue();
                secondaryDispatcher.ManagedThreadId.Should().Be(Environment.CurrentManagedThreadId);
                services.ThreadDispatchers.Release(secondaryDispatcher);
            }
            catch (Exception exception)
            {
                secondaryThreadError = exception;
            }
        });
        secondaryThread.Start();
        secondaryThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();
        secondaryThreadError.Should().BeNull();
        secondaryDispatcher.Should().NotBeNull().And.NotBeSameAs(services.Dispatcher);
        services.ThreadDispatchers.Release(services.Dispatcher);
        services.Timers.Should().BeOfType<ProGpuTimerService>();
        services.Handles.Should().BeOfType<ManagedLibreHandleRegistry>();
        services.Windows.Should().BeOfType<SilkWindowService>();
        services.Monitors.Should().BeOfType<SilkMonitorService>();
        services.Painting.Should().BeOfType<ProGpuPaintService>();
        services.Adorners.Should().BeSameAs(services.Painting);
        services.Popups.Should().BeOfType<ProGpuPopupSurfaceService>();
        services.MessageBoxes.Should().BeOfType<ManagedLibreMessageBoxService>();
        services.ColorDialogs.Should().BeOfType<ManagedLibreColorDialogService>();
        services.FontDialogs.Should().BeOfType<ManagedLibreFontDialogService>();
        services.Clipboard.Should().BeOfType<ProGpuClipboardService>();
        services.Clipboard.IsSupported.Should().BeTrue();
        services.DragDrop.Should().BeOfType<ProGpuDragDropService>();
        services.DragDrop.IsSupported.Should().BeTrue();
        if (OperatingSystem.IsLinux())
        {
            services.FileDialogs.Should().BeOfType<PreferredLinuxLibreFileDialogService>();
        }
        else
        {
            services.FileDialogs.Should().BeSameAs(UnsupportedLibreFileDialogService.Instance);
        }

        services.Dispose();
    }

    [Fact]
    public void Clipboard_PreservesRichDataAndSynchronizesSystemText()
    {
        using ProGpuDispatcher dispatcher = new();
        string? systemText = null;
        ProGpuClipboardService clipboard = new(
            dispatcher,
            () => systemText,
            value => systemText = value);
        TestDataTransfer rich = new(new Dictionary<string, object?>
        {
            ["UnicodeText"] = "inside",
            ["CF_DESIGNERCOMPONENTS"] = new[] { "button1" },
        });

        clipboard.SetData(rich, persist: true, retryTimes: 10, retryDelay: 100);

        systemText.Should().Be("inside");
        clipboard.GetData().Should().BeSameAs(rich);

        systemText = "outside";
        ILibreDataTransfer? external = clipboard.GetData();
        external.Should().NotBeNull().And.NotBeSameAs(rich);
        external!.Contains("UnicodeText", autoConvert: true).Should().BeTrue();
        external.GetData("Text", autoConvert: true).Should().Be("outside");

        clipboard.Clear();
        systemText.Should().BeEmpty();
        clipboard.GetData().Should().BeNull();
    }

    [Fact]
    public void DragDrop_RunsNestedLocalSessionAndDropsOnPointerRelease()
    {
        using ProGpuDispatcher dispatcher = new();
        LibreHandle target = new(42, LibreHandleKind.LogicalControl);
        TestDragInputSource input = new(
            dispatcher,
            new ProGpuDragInput(ProGpuDragInputKind.Pointer, new LibrePoint(10, 20), KeyState: 1),
            [
                new ProGpuDragInput(ProGpuDragInputKind.Pointer, new LibrePoint(15, 25), KeyState: 1),
                new ProGpuDragInput(ProGpuDragInputKind.Pointer, new LibrePoint(15, 25), KeyState: 0),
            ]);
        ProGpuDragDropService dragDrop = new(dispatcher, input);
        dragDrop.SetTargetEnabled(target, enabled: true);
        TestDragDropSession session = new(target);
        LibreDragDropRequest request = new(
            new LibreHandle(7, LibreHandleKind.LogicalControl),
            new TestDataTransfer(new Dictionary<string, object?> { ["UnicodeText"] = "drag" }),
            LibreDragDropEffects.Copy | LibreDragDropEffects.Move,
            default,
            UseDefaultDragImage: false);

        dragDrop.DoDragDrop(request, session).Should().Be(LibreDragDropEffects.Copy);

        input.EndCount.Should().Be(1);
        session.Sequence.Should().Equal("enter", "feedback", "over", "feedback", "over", "feedback", "drop");
        session.LastScreenPosition.Should().Be(new LibrePoint(15, 25));
    }

    [Fact]
    public void FontCatalog_ProjectsRealProGpuFamiliesAndMetadata()
    {
        IReadOnlyList<LibreFontFamilyInfo> families = new ProGpuFontCatalog().GetFamilies();

        families.Should().NotBeEmpty();
        families.Select(static family => family.Name).Should().OnlyHaveUniqueItems();
        families.Should().Contain(static family => family.IsVector);
        families.Should().OnlyContain(static family =>
            family.HasRegular || family.HasBold || family.HasItalic || family.HasBoldItalic);
    }

    private sealed class TestDataTransfer(IReadOnlyDictionary<string, object?> data) : ILibreDataTransfer
    {
        public IReadOnlyList<string> Formats { get; } = [.. data.Keys];

        public bool Contains(string format, bool autoConvert) => data.ContainsKey(format);

        public object? GetData(string format, bool autoConvert)
            => data.TryGetValue(format, out object? value) ? value : null;
    }

    private sealed class TestDragInputSource(
        ProGpuDispatcher dispatcher,
        ProGpuDragInput initial,
        IReadOnlyList<ProGpuDragInput> inputs) : IProGpuDragInputSource
    {
        internal int EndCount { get; private set; }

        public ProGpuDragInput BeginDrag(IProGpuDragInputSink sink)
        {
            foreach (ProGpuDragInput input in inputs)
            {
                ProGpuDragInput captured = input;
                dispatcher.Post(() => sink.Input(captured));
            }

            return initial;
        }

        public void EndDrag(IProGpuDragInputSink sink) => EndCount++;
    }

    private sealed class TestDragDropSession(LibreHandle target) : ILibreDragDropSession
    {
        internal List<string> Sequence { get; } = [];

        internal LibrePoint LastScreenPosition { get; private set; }

        public LibreHandle HitTest(LibrePoint screenPosition)
        {
            LastScreenPosition = screenPosition;
            return target;
        }

        public LibreDragTransition Enter(
            LibreHandle hitTarget,
            int keyState,
            LibrePoint screenPosition,
            LibreDragDropEffects effect)
        {
            Sequence.Add("enter");
            return new LibreDragTransition(hitTarget, LibreDragDropEffects.Copy);
        }

        public LibreDragDropEffects Over(
            LibreHandle activeTarget,
            int keyState,
            LibrePoint screenPosition,
            LibreDragDropEffects effect)
        {
            Sequence.Add("over");
            return LibreDragDropEffects.Copy;
        }

        public void Leave(LibreHandle activeTarget) => Sequence.Add("leave");

        public LibreDragDropEffects Drop(
            LibreHandle activeTarget,
            int keyState,
            LibrePoint screenPosition,
            LibreDragDropEffects effect)
        {
            Sequence.Add("drop");
            return effect;
        }

        public LibreDragAction QueryContinue(int keyState, bool escapePressed)
            => escapePressed ? LibreDragAction.Cancel : LibreDragAction.Continue;

        public bool GiveFeedback(LibreDragDropEffects effect)
        {
            Sequence.Add("feedback");
            return true;
        }
    }

    [Fact]
    public void Run_DeliversPostedWorkInOrderAndExits()
    {
        using ProGpuDispatcher dispatcher = new();
        List<int> order = [];
        dispatcher.Post(() => order.Add(1));
        dispatcher.Post(() =>
        {
            order.Add(2);
            dispatcher.RequestExit();
        });

        dispatcher.Run(TestContext.Current.CancellationToken);

        order.Should().Equal(1, 2);
    }

    [Fact]
    public void Timer_FiresOnDispatcherAndCanEndLoop()
    {
        using ProGpuDispatcher dispatcher = new();
        using ProGpuTimerService timers = new(dispatcher);
        int callbackThread = 0;
        using IDisposable timer = timers.Start(TimeSpan.FromMilliseconds(1), repeating: false, () =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            dispatcher.RequestExit();
        });

        int dispatcherThread = Environment.CurrentManagedThreadId;
        dispatcher.Run(TestContext.Current.CancellationToken);

        callbackThread.Should().Be(dispatcherThread);
    }

    [Fact]
    public void Timer_OnSecondaryUiThread_FiresOnItsIndependentDispatcher()
    {
        using ProGpuDispatcher provider = new();
        using ProGpuTimerService timers = new(provider);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Exception? secondaryThreadError = null;
        int callbackThread = 0;
        int dispatcherThread = 0;
        Thread secondaryThread = new(() =>
        {
            ILibreDispatcher dispatcher = provider.GetForCurrentThread();
            try
            {
                dispatcherThread = Environment.CurrentManagedThreadId;
                using IDisposable timer = timers.Start(TimeSpan.FromMilliseconds(1), repeating: false, () =>
                {
                    callbackThread = Environment.CurrentManagedThreadId;
                    dispatcher.RequestExit();
                });
                dispatcher.Run(cancellationToken);
            }
            catch (Exception exception)
            {
                secondaryThreadError = exception;
            }
            finally
            {
                provider.Release(dispatcher);
            }
        });

        secondaryThread.Start();
        secondaryThread.Join(TimeSpan.FromSeconds(5)).Should().BeTrue();

        secondaryThreadError.Should().BeNull();
        callbackThread.Should().Be(dispatcherThread).And.NotBe(provider.ManagedThreadId);
    }

    [Fact]
    public async Task Send_FromWorkerMarshalsAndPropagatesCompletion()
    {
        using ProGpuDispatcher dispatcher = new();
        int callbackThread = 0;
        Task worker = Task.Run(() => dispatcher.Send(() =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            dispatcher.RequestExit();
        }), TestContext.Current.CancellationToken);

        int dispatcherThread = Environment.CurrentManagedThreadId;
        dispatcher.Run(TestContext.Current.CancellationToken);
        await worker.ConfigureAwait(true);

        callbackThread.Should().Be(dispatcherThread);
    }

    [Fact]
    public async Task CreateGraphics_ForLogicalControl_DoesNotRequireDispatcherPump()
    {
        using ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        LibreHandle target = handles.Allocate(new object(), LibreHandleKind.LogicalControl);
        ProGpuPaintService painting = new(dispatcher, handles);

        Task worker = Task.Run(() =>
        {
            using Graphics graphics = painting.CreateGraphics(
                target,
                new LibrePoint(7, 9),
                new LibreRectangle(7, 9, 20, 10));
            graphics.VisibleClipBounds.Should().Be(new RectangleF(0, 0, 20, 10));
            graphics.FillRectangle(Brushes.Red, 0, 0, 4, 3);
        }, TestContext.Current.CancellationToken);

        await worker.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        handles.Release(target).Should().BeTrue();
    }

    [Fact]
    public async Task Present_FromWorker_CompletesOnOwningDispatcher()
    {
        using ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        TestWindow window = new(dispatcher);
        LibreHandle target = handles.Allocate<ILibreWindow>(window, LibreHandleKind.Window);
        ProGpuPaintService painting = new(dispatcher, handles);

        Task worker = Task.Run(
            () => painting.Present(target),
            TestContext.Current.CancellationToken);
        int dispatcherThread = Environment.CurrentManagedThreadId;
        dispatcher.Run(TestContext.Current.CancellationToken);
        await worker.ConfigureAwait(true);

        window.PresentCount.Should().Be(1);
        window.PresentThread.Should().Be(dispatcherThread);
        handles.Release(target).Should().BeTrue();
    }

    private sealed class TestWindow(ProGpuDispatcher dispatcher) : ILibreWindow
    {
        public LibreHandle Handle => default;

        public string Title { get; set; } = string.Empty;

        public LibreHandle Owner { get; set; }

        public LibreRectangle Bounds { get; set; }

        public LibreWindowState State { get; set; }

        public bool Visible => true;

        public bool Enabled { get; set; } = true;

        public bool TopMost { get; set; }

        public LibreWindowBorder Border { get; set; }

        public bool ShowInTaskbar { get; set; } = true;

        public bool CanMinimize { get; set; } = true;

        public bool CanMaximize { get; set; } = true;

        public bool CanClose { get; set; } = true;

        public double Opacity { get; set; } = 1d;

        public void SetZOrder(LibreWindowZOrder value) { }

        public void SetCursor(LibreCursorShape shape) { }

        public void SetCursorVisible(bool visible) { }

        public void SetSizeConstraints(LibreSize minimum, LibreSize maximum) { }

        public LibreWindowCoordinateMode CoordinateMode => LibreWindowCoordinateMode.Logical;

        public double FramebufferScale => 1.0;

        public double DpiScale => 1.0;

        public void SetIcons(IReadOnlyList<LibreWindowIcon> icons) { }

        public int PresentCount { get; private set; }

        public int PresentThread { get; private set; }

        public void Show() { }

        public void Hide() { }

        public void Activate() { }

        public void PresentPendingPaint()
        {
            PresentCount++;
            PresentThread = Environment.CurrentManagedThreadId;
            dispatcher.RequestExit();
        }

        public void Close() { }

        public void Dispose() { }
    }
}
