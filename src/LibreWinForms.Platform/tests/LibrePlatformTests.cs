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

    [Fact]
    public void ConstructorPublishesTypedDesktopCaptureAndRejectsMissingCapability()
    {
        TestServices test = new();
        using LibrePlatformServices services = new(
            test,
            test,
            test.Handles,
            test,
            test,
            test,
            test);

        services.DesktopCapture.Should().BeSameAs(test);
        Action create = () => new LibrePlatformServices(
            test,
            test,
            test.Handles,
            test,
            test,
            test,
            null!);
        create.Should().Throw<ArgumentNullException>().WithParameterName("desktopCapture");
    }

    [Fact]
    public void ConstructorPublishesTypedNativeInteropAndRejectsMissingCapabilities()
    {
        TestServices test = new();
        using LibrePlatformServices services = new(
            test,
            test,
            test.Handles,
            test,
            test,
            test,
            test,
            test,
            test);

        services.NativeFonts.Should().BeSameAs(test);
        services.NativeGraphics.Should().BeSameAs(test);
        Action missingFonts = () => new LibrePlatformServices(
            test, test, test.Handles, test, test, test, test, null!, test);
        Action missingGraphics = () => new LibrePlatformServices(
            test, test, test.Handles, test, test, test, test, test, null!);
        missingFonts.Should().Throw<ArgumentNullException>().WithParameterName("nativeFonts");
        missingGraphics.Should().Throw<ArgumentNullException>().WithParameterName("nativeGraphics");
    }

    [Fact]
    public void ConstructorPublishesTypedVisualStylesAndRejectsMissingCapability()
    {
        TestServices test = new();
        using LibrePlatformServices services = new(
            test,
            test,
            test.Handles,
            test,
            test,
            test,
            test,
            test,
            test,
            test);

        services.VisualStyles.Should().BeSameAs(test);
        Action create = () => new LibrePlatformServices(
            test, test, test.Handles, test, test, test, test, test, test, null!);
        create.Should().Throw<ArgumentNullException>().WithParameterName("visualStyles");
    }

    [Fact]
    public void ConstructorPublishesTypedSystemSettingsAndRejectsMissingCapability()
    {
        TestServices test = new();
        using LibrePlatformServices services = new(
            test,
            test,
            test.Handles,
            test,
            test,
            test,
            test,
            test,
            test,
            test,
            test);

        services.SystemSettings.Should().BeSameAs(test);
        services.SystemSettings.HighContrast.Should().BeTrue();
        Action create = () => new LibrePlatformServices(
            test, test, test.Handles, test, test, test, test, test, test, test, null!);
        create.Should().Throw<ArgumentNullException>().WithParameterName("systemSettings");
    }

    [Fact]
    public void MonitorSelection_PrefersLargestIntersection()
    {
        LibreMonitor[] monitors = CreateMonitorInventory();

        LibreMonitor selected = LibreMonitorSelection.GetNearest(
            monitors,
            new LibreRectangle(-100, 100, 300, 500));

        selected.Id.Should().Be("primary");
    }

    [Fact]
    public void MonitorSelection_UsesNearestDistanceForPointsOutsideEveryMonitor()
    {
        LibreMonitor[] monitors = CreateMonitorInventory();

        LibreMonitor left = LibreMonitorSelection.GetNearest(
            monitors,
            new LibreRectangle(-1400, 400, 0, 0));
        LibreMonitor right = LibreMonitorSelection.GetNearest(
            monitors,
            new LibreRectangle(2200, 400, 0, 0));

        left.Id.Should().Be("secondary");
        right.Id.Should().Be("primary");
    }

    [Fact]
    public void MonitorSelection_RejectsEmptyInventory()
    {
        Action select = () => LibreMonitorSelection.GetNearest([], default);

        select.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void WindowCoordinates_DevicePixels_ScaleAndRoundTripNegativeBounds()
    {
        LibreRectangle nativeBounds = new(-100, 20, 400, 300);

        LibreRectangle managedBounds = LibreWindowCoordinates.ToManaged(
            nativeBounds,
            LibreWindowCoordinateMode.DevicePixels,
            1.5,
            1.5);

        managedBounds.Should().Be(new LibreRectangle(-150, 30, 600, 450));
        LibreWindowCoordinates.ToNative(
            managedBounds,
            LibreWindowCoordinateMode.DevicePixels,
            1.5,
            1.5).Should().Be(nativeBounds);
        LibreWindowCoordinates.ToDeviceDpi(1.5).Should().Be(144);
    }

    [Fact]
    public void WindowCoordinates_LogicalMode_DoesNotScaleBounds()
    {
        LibreRectangle bounds = new(-101, 21, 401, 301);

        LibreWindowCoordinates.ToManaged(bounds, LibreWindowCoordinateMode.Logical, 2.0, 2.0).Should().Be(bounds);
        LibreWindowCoordinates.ToNative(bounds, LibreWindowCoordinateMode.Logical, 2.0, 2.0).Should().Be(bounds);
    }

    [Fact]
    public void WindowCoordinates_LogicalMode_SeparatesDpiFromFramebufferScale()
    {
        LibreRectangle nativeBounds = new(20, 40, 800, 600);

        LibreRectangle managedBounds = LibreWindowCoordinates.ToManaged(
            nativeBounds,
            LibreWindowCoordinateMode.Logical,
            dpiScale: 2.0,
            framebufferScale: 1.0);

        managedBounds.Should().Be(new LibreRectangle(10, 20, 400, 300));
        LibreWindowCoordinates.ToNative(
            managedBounds,
            LibreWindowCoordinateMode.Logical,
            dpiScale: 2.0,
            framebufferScale: 1.0).Should().Be(nativeBounds);
    }

    [Fact]
    public void WindowCoordinates_DevicePixels_DpiDoesNotAlterPixelConversion()
    {
        LibreRectangle nativeBounds = new(10, 20, 800, 600);

        LibreWindowCoordinates.ToManaged(
            nativeBounds,
            LibreWindowCoordinateMode.DevicePixels,
            dpiScale: 2.0,
            framebufferScale: 1.0).Should().Be(nativeBounds);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(8.01)]
    public void WindowCoordinates_DevicePixels_RejectInvalidDpiScale(double dpiScale)
    {
        Action convert = () => LibreWindowCoordinates.ToManaged(
            new LibreRectangle(0, 0, 1, 1),
            LibreWindowCoordinateMode.DevicePixels,
            dpiScale,
            framebufferScale: 1.0);

        convert.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(dpiScale));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(8.01)]
    public void WindowCoordinates_DevicePixels_RejectInvalidFramebufferScale(double framebufferScale)
    {
        Action convert = () => LibreWindowCoordinates.ToManaged(
            new LibreRectangle(0, 0, 1, 1),
            LibreWindowCoordinateMode.DevicePixels,
            dpiScale: 1.0,
            framebufferScale);

        convert.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(framebufferScale));
    }

    [Fact]
    public void WindowCoordinates_RejectUnknownCoordinateMode()
    {
        const LibreWindowCoordinateMode mode = (LibreWindowCoordinateMode)42;
        Action convert = () => LibreWindowCoordinates.ToManaged(
            default,
            mode,
            1.0,
            1.0);

        convert.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(mode));
    }

    [Fact]
    public void WindowIcon_SnapshotsValidatedRgbaPixels()
    {
        byte[] source = [1, 2, 3, 4, 5, 6, 7, 8];
        LibreWindowIcon icon = new(2, 1, source);
        source[0] = 99;
        byte[] copied = new byte[icon.PixelByteLength];

        icon.CopyPixelsTo(copied);

        icon.Width.Should().Be(2);
        icon.Height.Should().Be(1);
        copied.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
    }

    [Fact]
    public void WindowIcon_RejectsInvalidDimensionsAndPixelLength()
    {
        Action invalidWidth = () => new LibreWindowIcon(0, 1, []);
        Action invalidLength = () => new LibreWindowIcon(2, 1, [1, 2, 3, 4]);

        invalidWidth.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("width");
        invalidLength.Should().Throw<ArgumentException>().WithParameterName("rgbaPixels");
    }

    private static LibreMonitor[] CreateMonitorInventory() =>
    [
        new("primary", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1, true),
        new("secondary", new(-1280, 0, 1280, 1024), new(-1280, 0, 1280, 984), 1.5, false),
    ];

    private sealed class TestServices :
        ILibreDispatcher,
        ILibreTimerService,
        ILibreWindowService,
        ILibreMonitorService,
        ILibrePaintService,
        ILibreDesktopCaptureService,
        ILibreNativeFontInteropService,
        ILibreNativeGraphicsInteropService,
        ILibreVisualStyleService,
        ILibreSystemSettingsService
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
        public System.Drawing.Graphics CreateGraphics(
            LibreHandle target,
            LibrePoint origin,
            LibreRectangle clipRectangle)
            => throw new NotSupportedException();
        public void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle) { }
        public void InvalidateAll(LibreHandle target) { }
        public void Present(LibreHandle target) { }
        public void Capture(LibreRectangle sourceRectangle, Span<byte> destinationRgba)
            => destinationRgba.Clear();
        public System.Drawing.Font ImportFromDeviceContext(IntPtr deviceContext)
            => throw new NotSupportedException();
        public System.Drawing.Graphics CreateFromDeviceContext(IntPtr deviceContext, IntPtr device)
            => throw new NotSupportedException();
        public System.Drawing.Graphics CreateFromWindow(IntPtr window)
            => throw new NotSupportedException();
        public IntPtr CreateHalftonePalette() => IntPtr.Zero;
        public bool IsEnabled => true;
        public bool HighContrast => true;
        public bool IsElementDefined(string className, int part) => true;
        public void DrawBackground(
            System.Drawing.Graphics graphics,
            string className,
            int part,
            int state,
            System.Drawing.Rectangle bounds,
            System.Drawing.Rectangle? clipRectangle)
            => throw new NotSupportedException();
        public System.Drawing.Region? GetBackgroundRegion(
            string className,
            int part,
            int state,
            System.Drawing.Rectangle bounds)
            => throw new NotSupportedException();
        public System.Drawing.Rectangle GetBackgroundContentRectangle(
            string className,
            int part,
            int state,
            System.Drawing.Rectangle bounds)
            => throw new NotSupportedException();
        public System.Drawing.Rectangle GetBackgroundExtent(
            string className,
            int part,
            int state,
            System.Drawing.Rectangle contentBounds)
            => throw new NotSupportedException();
        public System.Drawing.Size GetPartSize(
            string className,
            int part,
            int state,
            System.Drawing.Rectangle? bounds,
            LibreVisualStyleSizeType type)
            => throw new NotSupportedException();
        public System.Drawing.Color GetColor(
            string className,
            int part,
            int state,
            LibreVisualStyleColorProperty property)
            => throw new NotSupportedException();
        public int GetInteger(
            string className,
            int part,
            int state,
            LibreVisualStyleIntegerProperty property)
            => throw new NotSupportedException();
        public bool GetBoolean(
            string className,
            int part,
            int state,
            LibreVisualStyleBooleanProperty property)
            => throw new NotSupportedException();
        public int GetEnumValue(
            string className,
            int part,
            int state,
            LibreVisualStyleEnumProperty property)
            => throw new NotSupportedException();
        public string GetFilename(
            string className,
            int part,
            int state,
            LibreVisualStyleFilenameProperty property)
            => throw new NotSupportedException();
        public string GetString(
            string className,
            int part,
            int state,
            LibreVisualStyleStringProperty property)
            => throw new NotSupportedException();
        public System.Drawing.Font? GetFont(
            string className,
            int part,
            int state,
            LibreVisualStyleFontProperty property)
            => throw new NotSupportedException();
        public System.Drawing.Rectangle MeasureText(
            System.Drawing.Graphics graphics,
            string className,
            int part,
            int state,
            System.Drawing.Rectangle? bounds,
            string text,
            LibreVisualStyleTextFormat format)
            => throw new NotSupportedException();
        public LibreVisualStyleHitTestCode HitTestBackground(
            System.Drawing.Graphics graphics,
            string className,
            int part,
            int state,
            System.Drawing.Rectangle bounds,
            System.Drawing.Region? region,
            System.Drawing.Point point,
            LibreVisualStyleHitTestOptions options)
            => throw new NotSupportedException();
        public LibreVisualStyleTextMetrics GetTextMetrics(
            System.Drawing.Graphics graphics,
            string className,
            int part,
            int state)
            => throw new NotSupportedException();
        public LibreVisualStyleMargins GetMargins(
            string className,
            int part,
            int state,
            LibreVisualStyleMarginProperty property)
            => throw new NotSupportedException();
        public System.Drawing.Point GetPoint(
            string className,
            int part,
            int state,
            LibreVisualStylePointProperty property)
            => throw new NotSupportedException();
        public bool IsBackgroundPartiallyTransparent(string className, int part, int state)
            => throw new NotSupportedException();
        public System.Drawing.Rectangle DrawEdge(
            System.Drawing.Graphics graphics,
            string className,
            int part,
            int state,
            System.Drawing.Rectangle bounds,
            LibreVisualStyleEdges edges,
            LibreVisualStyleEdgeStyle style,
            LibreVisualStyleEdgeEffects effects)
            => throw new NotSupportedException();
        public void DrawText(
            System.Drawing.Graphics graphics,
            string className,
            int part,
            int state,
            System.Drawing.Rectangle bounds,
            string text,
            bool disabled,
            LibreVisualStyleTextFormat format)
            => throw new NotSupportedException();

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
