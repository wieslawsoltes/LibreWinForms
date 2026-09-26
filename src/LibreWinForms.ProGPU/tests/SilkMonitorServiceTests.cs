// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class SilkMonitorServiceTests
{
    [Fact]
    public void GetMonitorsMapsBoundsPrimaryWorkAreaAndTypedScale()
    {
        FakeMonitor primary = new(
            "Primary",
            0,
            new Rectangle<int>(0, 0, 1920, 1080),
            new VideoMode(new Vector2D<int>(3840, 2160), 60));
        FakeMonitor secondary = new(
            "Secondary",
            1,
            new Rectangle<int>(1920, 0, 1280, 720),
            new VideoMode(new Vector2D<int>(1280, 720), 60));
        SilkMonitorService service = new(
            () => [primary, secondary],
            () => primary,
            monitor => monitor.Index == 0 ? 1.75 : 1.0,
            monitor => monitor.Index == 0
                ? new Rectangle<int>(0, 48, 1920, 1032)
                : monitor.Bounds);

        IReadOnlyList<LibreMonitor> monitors = service.GetMonitors();

        Assert.Equal(2, monitors.Count);
        Assert.Equal(new LibreRectangle(0, 0, 1920, 1080), monitors[0].Bounds);
        Assert.Equal(new LibreRectangle(0, 48, 1920, 1032), monitors[0].WorkArea);
        Assert.Equal(1.75, monitors[0].DpiScale);
        Assert.True(monitors[0].IsPrimary);
        Assert.Equal("Primary", monitors[0].DisplayName);
        Assert.False(monitors[1].IsPrimary);
    }

    [Fact]
    public void GetMonitorsDoesNotInferPixelScaleFromVideoModeAndWorkArea()
    {
        FakeMonitor monitor = new(
            "HiDpi",
            0,
            new Rectangle<int>(0, 0, 1920, 1080),
            new VideoMode(new Vector2D<int>(3840, 2160), 60));
        SilkMonitorService service = new(() => [monitor], () => monitor);

        LibreMonitor mapped = Assert.Single(service.GetMonitors());

        Assert.Equal(1.0, mapped.DpiScale);
        Assert.Null(mapped.NativeCoordinateScale);
        Assert.Equal(mapped.Bounds, mapped.WorkArea);
    }

    [Fact]
    public void GetMonitorsKeepsExplicitFullBoundsWorkAreaAndNativeUnitsIndependentOfDpi()
    {
        FakeMonitor monitor = new("Windows", 0, new Rectangle<int>(0, 40, 2400, 1460),
            new VideoMode(new Vector2D<int>(2400, 1600), 60));
        SilkMonitorService service = new(() => [monitor], () => monitor,
            _ => 2, _ => monitor.Bounds, _ => 1, _ => new Rectangle<int>(0, 0, 2400, 1600));

        LibreMonitor mapped = Assert.Single(service.GetMonitors());

        Assert.Equal(new LibreRectangle(0, 0, 2400, 1600), mapped.Bounds);
        Assert.Equal(new LibreRectangle(0, 40, 2400, 1460), mapped.WorkArea);
        Assert.Equal(2, mapped.DpiScale);
        Assert.Equal(1, mapped.NativeCoordinateScale);
        Assert.Equal(mapped.Bounds, LibreMonitorCoordinates.ToManaged(mapped, LibreWindowCoordinateMode.DevicePixels).Bounds);
        Assert.Equal(new LibreRectangle(0, 0, 1200, 800),
            LibreMonitorCoordinates.ToManaged(mapped, LibreWindowCoordinateMode.Logical).Bounds);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MonitorConversionRejectsInvalidDeclaredNativeScale(double scale)
    {
        LibreMonitor monitor = new("invalid", new(0, 0, 2400, 1600), new(0, 0, 2400, 1500), 2, true)
        { NativeCoordinateScale = scale };

        Assert.Throws<ArgumentOutOfRangeException>(() => LibreMonitorCoordinates.ToManaged(monitor, LibreWindowCoordinateMode.Logical));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ExplicitContentDpiProviderCannotSilentlyFallBackTo96Dpi(double? scale)
    {
        FakeMonitor monitor = new("invalid", 0, new Rectangle<int>(0, 0, 2400, 1600),
            new VideoMode(new Vector2D<int>(2400, 1600), 60));
        SilkMonitorService service = new(() => [monitor], () => monitor, _ => scale);
        Assert.Throws<PlatformNotSupportedException>(() => service.GetMonitors());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitFullBoundsProviderCannotSilentlyFallBackToWorkArea(bool empty)
    {
        FakeMonitor monitor = new("invalid", 0, new Rectangle<int>(0, 40, 2400, 1460),
            new VideoMode(new Vector2D<int>(2400, 1600), 60));
        SilkMonitorService service = new(() => [monitor], () => monitor, _ => 2, null, _ => 1,
            _ => empty ? new Rectangle<int>(0, 0, 0, 0) : null);
        Assert.Throws<PlatformNotSupportedException>(() => service.GetMonitors());
    }

    [Fact]
    public void GetMonitorsFallsBackToVideoModeWhenBoundsSizeIsUnavailable()
    {
        FakeMonitor monitor = new(
            "Headless",
            0,
            new Rectangle<int>(10, 20, 0, 0),
            new VideoMode(new Vector2D<int>(1024, 768), 60));
        SilkMonitorService service = new(() => [monitor], () => monitor);

        LibreMonitor mapped = Assert.Single(service.GetMonitors());

        Assert.Equal(new LibreRectangle(10, 20, 1024, 768), mapped.Bounds);
    }

    [Fact]
    public void GetMonitorsRejectsEmptyInventory()
    {
        SilkMonitorService service = new(() => [], () => null);

        Assert.Throws<PlatformNotSupportedException>(() => service.GetMonitors());
    }

    private sealed class FakeMonitor : IMonitor
    {
        public FakeMonitor(string name, int index, Rectangle<int> bounds, VideoMode videoMode)
        {
            Name = name;
            Index = index;
            Bounds = bounds;
            VideoMode = videoMode;
        }

        public string Name { get; }

        public int Index { get; }

        public Rectangle<int> Bounds { get; }

        public VideoMode VideoMode { get; }

        public float Gamma { get; set; } = 1.0f;

        public IEnumerable<VideoMode> GetAllVideoModes() => [VideoMode];

        public IWindow CreateWindow(WindowOptions opts) => throw new NotSupportedException();
    }
}
