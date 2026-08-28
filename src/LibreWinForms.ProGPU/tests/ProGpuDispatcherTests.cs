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
        services.Timers.Should().BeOfType<ProGpuTimerService>();
        services.Handles.Should().BeOfType<ManagedLibreHandleRegistry>();
        services.Windows.Should().BeOfType<SilkWindowService>();
        services.Monitors.Should().BeOfType<SilkMonitorService>();
        services.Painting.Should().BeOfType<ProGpuPaintService>();
        services.MessageBoxes.Should().BeOfType<ManagedLibreMessageBoxService>();
        services.ColorDialogs.Should().BeOfType<ManagedLibreColorDialogService>();
        services.FontDialogs.Should().BeOfType<ManagedLibreFontDialogService>();
        if (OperatingSystem.IsLinux())
        {
            services.FileDialogs.Should().BeOfType<ZenityLibreFileDialogService>();
        }
        else
        {
            services.FileDialogs.Should().BeSameAs(UnsupportedLibreFileDialogService.Instance);
        }

        services.Dispose();
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
