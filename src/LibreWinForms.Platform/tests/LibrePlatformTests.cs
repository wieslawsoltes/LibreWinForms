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
        services.VisualStyles.ThemeFilename.Should().Be("test.theme");
        services.VisualStyles.ColorScheme.Should().Be("TestColor");
        services.VisualStyles.ThemeSize.Should().Be("TestSize");
        services.VisualStyles.DisplayName.Should().Be("Test theme");
        services.VisualStyles.SupportsFlatMenus.Should().BeTrue();
        services.VisualStyles.MinimumColorDepth.Should().Be(24);
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
        services.SystemSettings.BorderSize.Should().Be(new LibreSize(7, 8));
        services.SystemSettings.FixedFrameBorderSize.Should().Be(new LibreSize(9, 10));
        services.SystemSettings.Border3DSize.Should().Be(new LibreSize(11, 12));
        services.SystemSettings.VerticalScrollBarWidth.Should().Be(13);
        services.SystemSettings.HorizontalScrollBarHeight.Should().Be(14);
        services.SystemSettings.CaptionHeight.Should().Be(29);
        services.SystemSettings.MenuHeight.Should().Be(31);
        services.SystemSettings.MinWindowTrackSize.Should().Be(new LibreSize(140, 52));
        services.SystemSettings.IconSize.Should().Be(new LibreSize(33, 35));
        services.SystemSettings.CursorSize.Should().Be(new LibreSize(37, 39));
        services.SystemSettings.SmallIconSize.Should().Be(new LibreSize(17, 19));
        services.SystemSettings.MinimumWindowSize.Should().Be(new LibreSize(101, 102));
        services.SystemSettings.CaptionButtonSize.Should().Be(new LibreSize(33, 34));
        services.SystemSettings.FrameBorderSize.Should().Be(new LibreSize(7, 8));
        services.SystemSettings.MaxWindowTrackSize.Should().Be(new LibreSize(1600, 1200));
        services.SystemSettings.PrimaryMonitorMaximizedWindowSize.Should().Be(new LibreSize(1500, 1100));
        services.SystemSettings.MinimizedWindowSpacingSize.Should().Be(new LibreSize(201, 202));
        services.SystemSettings.ToolWindowCaptionHeight.Should().Be(43);
        services.SystemSettings.ToolWindowCaptionButtonSize.Should().Be(new LibreSize(45, 46));
        services.SystemSettings.MenuButtonSize.Should().Be(new LibreSize(47, 48));
        services.SystemSettings.MinimizedWindowSize.Should().Be(new LibreSize(203, 204));
        services.SystemSettings.KanjiWindowHeight.Should().Be(41);
        services.SystemSettings.DebugOperatingSystem.Should().BeTrue();
        services.SystemSettings.RightAlignedMenus.Should().BeTrue();
        services.SystemSettings.PenWindows.Should().BeTrue();
        services.SystemSettings.DbcsEnabled.Should().BeTrue();
        services.SystemSettings.Secure.Should().BeTrue();
        services.SystemSettings.Network.Should().BeFalse();
        services.SystemSettings.TerminalServerSession.Should().BeTrue();
        services.SystemSettings.BootMode.Should().Be(LibreBootMode.FailSafeWithNetwork);
        services.SystemSettings.ShowSounds.Should().BeTrue();
        services.SystemSettings.MenuCheckSize.Should().Be(new LibreSize(27, 29));
        services.SystemSettings.MidEastEnabled.Should().BeTrue();
        services.SystemSettings.MinimizedWindowStartPosition.Should().Be(LibreMinimizedWindowStartPosition.TopRight);
        services.SystemSettings.MinimizedWindowDirection.Should().Be(LibreMinimizedWindowDirection.Up);
        services.SystemSettings.HideMinimizedWindows.Should().BeTrue();
        services.SystemSettings.VerticalScrollBarArrowHeight.Should().Be(15);
        services.SystemSettings.HorizontalScrollBarArrowWidth.Should().Be(16);
        services.SystemSettings.VerticalScrollBarThumbHeight.Should().Be(17);
        services.SystemSettings.HorizontalScrollBarThumbWidth.Should().Be(18);
        services.SystemSettings.DragSize.Should().Be(new LibreSize(19, 20));
        services.SystemSettings.MousePresent.Should().BeTrue();
        services.SystemSettings.MouseButtonsSwapped.Should().BeTrue();
        services.SystemSettings.MouseButtons.Should().Be(5);
        services.SystemSettings.DoubleClickSize.Should().Be(new LibreSize(12, 14));
        services.SystemSettings.DoubleClickTime.Should().Be(650);
        services.SystemSettings.MouseWheelPresent.Should().BeFalse();
        services.SystemSettings.CaretBlinkTime.Should().Be(725);
        services.SystemSettings.MouseWheelScrollLines.Should().Be(21);
        services.SystemSettings.MenuAccessKeysUnderlined.Should().BeTrue();
        services.SystemSettings.KeyboardDelay.Should().Be(2);
        services.SystemSettings.KeyboardPreferred.Should().BeTrue();
        services.SystemSettings.KeyboardSpeed.Should().Be(23);
        services.SystemSettings.MouseHoverSize.Should().Be(new LibreSize(24, 25));
        services.SystemSettings.MouseHoverTime.Should().Be(640);
        services.SystemSettings.MouseSpeed.Should().Be(14);
        services.SystemSettings.SnapToDefaultButton.Should().BeTrue();
        services.SystemSettings.DragFullWindows.Should().BeFalse();
        services.SystemSettings.DropShadowEnabled.Should().BeFalse();
        services.SystemSettings.FlatMenuEnabled.Should().BeTrue();
        services.SystemSettings.PopupMenusLeftAligned.Should().BeFalse();
        services.SystemSettings.MenuFadeEnabled.Should().BeFalse();
        services.SystemSettings.MenuShowDelay.Should().Be(275);
        services.SystemSettings.ComboBoxAnimationEnabled.Should().BeTrue();
        services.SystemSettings.TitleBarGradientEnabled.Should().BeFalse();
        services.SystemSettings.HotTrackingEnabled.Should().BeTrue();
        services.SystemSettings.ListBoxSmoothScrollingEnabled.Should().BeFalse();
        services.SystemSettings.MenuAnimationEnabled.Should().BeTrue();
        services.SystemSettings.SelectionFadeEnabled.Should().BeFalse();
        services.SystemSettings.ToolTipAnimationEnabled.Should().BeTrue();
        services.SystemSettings.UIEffectsEnabled.Should().BeFalse();
        services.SystemSettings.ActiveWindowTrackingEnabled.Should().BeTrue();
        services.SystemSettings.ActiveWindowTrackingDelay.Should().Be(525);
        services.SystemSettings.MinimizeRestoreAnimationEnabled.Should().BeTrue();
        services.SystemSettings.BorderMultiplierFactor.Should().Be(3);
        services.SystemSettings.CaretWidth.Should().Be(5);
        services.SystemSettings.VerticalFocusThickness.Should().Be(6);
        services.SystemSettings.HorizontalFocusThickness.Should().Be(7);
        services.SystemSettings.VerticalResizeBorderThickness.Should().Be(8);
        services.SystemSettings.HorizontalResizeBorderThickness.Should().Be(9);
        services.SystemSettings.FontSmoothingEnabled.Should().BeFalse();
        services.SystemSettings.FontSmoothingContrast.Should().Be(1700);
        services.SystemSettings.FontSmoothingType.Should().Be(1);
        services.SystemSettings.IconHorizontalSpacing.Should().Be(81);
        services.SystemSettings.IconVerticalSpacing.Should().Be(83);
        services.SystemSettings.IconTitleWrappingEnabled.Should().BeFalse();
        LibreSystemSettingsChangedEventArgs? change = null;
        services.SystemSettings.SettingsChanged += (_, e) => change = e;
        test.RaiseSettingsChanged(LibreSystemSettingsChangeKind.Color | LibreSystemSettingsChangeKind.VisualStyle);
        change.Should().NotBeNull();
        change!.Includes(LibreSystemSettingsChangeKind.Color).Should().BeTrue();
        change.Includes(LibreSystemSettingsChangeKind.VisualStyle).Should().BeTrue();
        change.Includes(LibreSystemSettingsChangeKind.Locale).Should().BeFalse();
        Action invalidChange = () => new LibreSystemSettingsChangedEventArgs(LibreSystemSettingsChangeKind.None);
        invalidChange.Should().Throw<ArgumentOutOfRangeException>();
        Action create = () => new LibrePlatformServices(
            test, test, test.Handles, test, test, test, test, test, test, test, null!);
        create.Should().Throw<ArgumentNullException>().WithParameterName("systemSettings");
    }

    [Fact]
    public void ConstructorPublishesTypedTextRendererAndRejectsMissingCapability()
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
            test,
            test);

        services.TextRenderer.Should().BeSameAs(test);
        Action create = () => new LibrePlatformServices(
            test, test, test.Handles, test, test, test, test, test, test, test, test, null!);
        create.Should().Throw<ArgumentNullException>().WithParameterName("textRenderer");
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
        ILibreSystemSettingsService,
        ILibreTextRendererService
    {
        public event EventHandler<LibreSystemSettingsChangedEventArgs>? SettingsChanged;

        public void RaiseSettingsChanged(LibreSystemSettingsChangeKind kind)
            => SettingsChanged?.Invoke(this, new(kind));

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
        public string ThemeFilename => "test.theme";
        public string ColorScheme => "TestColor";
        public string ThemeSize => "TestSize";
        public string DisplayName => "Test theme";
        public string Company => "Test company";
        public string Author => "Test author";
        public string Copyright => "Test copyright";
        public string Url => "https://example.test";
        public string Version => "Test version";
        public string Description => "Test description";
        public bool SupportsFlatMenus => true;
        public int MinimumColorDepth => 24;
        public bool HighContrast => true;
        public LibreSize BorderSize => new(7, 8);
        public LibreSize FixedFrameBorderSize => new(9, 10);
        public LibreSize Border3DSize => new(11, 12);
        public int VerticalScrollBarWidth => 13;
        public int HorizontalScrollBarHeight => 14;
        public int CaptionHeight => 29;
        public int MenuHeight => 31;
        public LibreSize MinWindowTrackSize => new(140, 52);
        public LibreSize IconSize => new(33, 35);
        public LibreSize CursorSize => new(37, 39);
        public LibreSize SmallIconSize => new(17, 19);
        public LibreSize MinimumWindowSize => new(101, 102);
        public LibreSize CaptionButtonSize => new(33, 34);
        public LibreSize FrameBorderSize => new(7, 8);
        public LibreSize MaxWindowTrackSize => new(1600, 1200);
        public LibreSize PrimaryMonitorMaximizedWindowSize => new(1500, 1100);
        public LibreSize MinimizedWindowSpacingSize => new(201, 202);
        public int ToolWindowCaptionHeight => 43;
        public LibreSize ToolWindowCaptionButtonSize => new(45, 46);
        public LibreSize MenuButtonSize => new(47, 48);
        public LibreSize MinimizedWindowSize => new(203, 204);
        public int KanjiWindowHeight => 41;
        public bool DebugOperatingSystem => true;
        public bool RightAlignedMenus => true;
        public bool PenWindows => true;
        public bool DbcsEnabled => true;
        public bool Secure => true;
        public bool Network => false;
        public bool TerminalServerSession => true;
        public LibreBootMode BootMode => LibreBootMode.FailSafeWithNetwork;
        public bool ShowSounds => true;
        public LibreSize MenuCheckSize => new(27, 29);
        public bool MidEastEnabled => true;
        public LibreMinimizedWindowStartPosition MinimizedWindowStartPosition
            => LibreMinimizedWindowStartPosition.TopRight;
        public LibreMinimizedWindowDirection MinimizedWindowDirection => LibreMinimizedWindowDirection.Up;
        public bool HideMinimizedWindows => true;
        public int VerticalScrollBarArrowHeight => 15;
        public int HorizontalScrollBarArrowWidth => 16;
        public int VerticalScrollBarThumbHeight => 17;
        public int HorizontalScrollBarThumbWidth => 18;
        public LibreSize DragSize => new(19, 20);
        public bool MousePresent => true;
        public bool MouseButtonsSwapped => true;
        public int MouseButtons => 5;
        public LibreSize DoubleClickSize => new(12, 14);
        public int DoubleClickTime => 650;
        public bool MouseWheelPresent => false;
        public int CaretBlinkTime => 725;
        public int MouseWheelScrollLines => 21;
        public bool MenuAccessKeysUnderlined => true;
        public int KeyboardDelay => 2;
        public bool KeyboardPreferred => true;
        public int KeyboardSpeed => 23;
        public LibreSize MouseHoverSize => new(24, 25);
        public int MouseHoverTime => 640;
        public int MouseSpeed => 14;
        public bool SnapToDefaultButton => true;
        public bool DragFullWindows => false;
        public bool DropShadowEnabled => false;
        public bool FlatMenuEnabled => true;
        public bool PopupMenusLeftAligned => false;
        public bool MenuFadeEnabled => false;
        public int MenuShowDelay => 275;
        public bool ComboBoxAnimationEnabled => true;
        public bool TitleBarGradientEnabled => false;
        public bool HotTrackingEnabled => true;
        public bool ListBoxSmoothScrollingEnabled => false;
        public bool MenuAnimationEnabled => true;
        public bool SelectionFadeEnabled => false;
        public bool ToolTipAnimationEnabled => true;
        public bool UIEffectsEnabled => false;
        public bool ActiveWindowTrackingEnabled => true;
        public int ActiveWindowTrackingDelay => 525;
        public bool MinimizeRestoreAnimationEnabled => true;
        public int BorderMultiplierFactor => 3;
        public int CaretWidth => 5;
        public int VerticalFocusThickness => 6;
        public int HorizontalFocusThickness => 7;
        public int VerticalResizeBorderThickness => 8;
        public int HorizontalResizeBorderThickness => 9;
        public bool FontSmoothingEnabled => false;
        public int FontSmoothingContrast => 1700;
        public int FontSmoothingType => 1;
        public int IconHorizontalSpacing => 81;
        public int IconVerticalSpacing => 83;
        public bool IconTitleWrappingEnabled => false;
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
        public void DrawText(
            System.Drawing.Graphics graphics,
            string text,
            System.Drawing.Font? font,
            System.Drawing.Rectangle bounds,
            System.Drawing.Color foreColor,
            System.Drawing.Color backColor,
            LibreTextFormat format)
            => throw new NotSupportedException();
        public System.Drawing.Size MeasureText(
            System.Drawing.Graphics? graphics,
            string text,
            System.Drawing.Font? font,
            System.Drawing.Size proposedSize,
            LibreTextFormat format)
            => throw new NotSupportedException();

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
