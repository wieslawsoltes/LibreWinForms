using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using ProGPU.SystemDrawing;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void CanonicalSnapshotPreservesPartialAvailabilityWithoutLegacyReads()
    {
        if (RunDpiCaseInNewProcess()) return;
        var probe = new GdiQualityProbe();
        var platform = new HeadlessPlatform(autoCloseWindows: false);
        var observed = new ObservedTextSystemSettings();
        var services = new LibrePlatformServices(platform, platform, platform.Handles, platform, platform, platform,
            UnsupportedLibreDesktopCaptureService.Instance, UnsupportedLibreNativeFontInteropService.Instance,
            UnsupportedLibreNativeGraphicsInteropService.Instance, platform, observed, probe);
        LibrePlatform.Register(services);
        var context = new global::ProGPU.Scene.DrawingContext();
        using var graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 13);
        graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        TextRenderer.DrawText(graphics, "Name", font, Point.Empty, Color.Black);
        _ = TextRenderer.MeasureText(graphics, "Name", font);
        Assert.Equal(1, observed.Reads);
        Assert.Equal(DrawingGdiFontQuality.Proof, probe.Policies[0].RequestedQuality);
        Assert.Equal(observed.Value, probe.Policies[0].SystemSnapshot);
        Assert.Null(probe.Policies[0].SystemSettings);
        Assert.Null(probe.Policies[1].SystemSnapshot);
    }

    [Fact]
    public void RetainedSnapshotIsReadOnlyAtPaintAndDisposalRetiresTheCallback()
    {
        var renderer = new ProGpuTextRendererService();
        var context = new global::ProGPU.Scene.DrawingContext();
        using var graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 13);
        int reads = 0;
        const LibreTextFormat format = LibreTextFormat.NoPadding | LibreTextFormat.NoPrefix | LibreTextFormat.SingleLine;
        using var layout = renderer.CreateLayoutWithSystemSnapshot(graphics, "Name", font, new Size(200, 50), format,
            DrawingGdiFontQuality.Draft, () => { reads++; return new(null, 2, (uint)reads, null); });
        _ = layout.ContentSize;
        _ = layout.GetCaret(2);
        Assert.Equal(0, reads);
        font.Dispose();
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        layout.Draw(graphics, PointF.Empty, Color.Black);
        layout.Draw(graphics, PointF.Empty, Color.Black);
        Assert.Equal(2, reads);
        layout.Dispose();
        Assert.Throws<ObjectDisposedException>(() => layout.Draw(graphics, PointF.Empty, Color.Black));
        Assert.Equal(2, reads);
        context.Clear();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedSnapshotRejectsRetirementAndRecursivePaintBeforeCommands(bool recursive)
    {
        var renderer = new ProGpuTextRendererService();
        var context = new global::ProGPU.Scene.DrawingContext();
        using var graphics = Graphics.FromProGpuDrawingContext(context);
        using var font = new Font(FontFamily.GenericSansSerif, 13);
        ILibreTextLayout? layout = null;
        int reads = 0;
        const LibreTextFormat format = LibreTextFormat.NoPadding | LibreTextFormat.NoPrefix | LibreTextFormat.SingleLine;
        layout = renderer.CreateLayoutWithSystemSnapshot(graphics, "Name", font, new Size(200, 50), format,
            DrawingGdiFontQuality.Proof, () =>
            {
                reads++;
                if (recursive) layout!.Draw(graphics, PointF.Empty, Color.Black);
                else layout!.Dispose();
                return null;
            });
        using (layout)
        {
            if (recursive) Assert.Throws<InvalidOperationException>(() => layout.Draw(graphics, PointF.Empty, Color.Black));
            else Assert.Throws<ObjectDisposedException>(() => layout.Draw(graphics, PointF.Empty, Color.Black));
            Assert.Equal(1, reads);
            Assert.Empty(context.Commands);
        }
    }

    private sealed class ObservedTextSystemSettings : ILibreSystemSettingsService, ILibreTextSystemSettingsService
    {
        public int Reads;
        public DrawingTextSystemSettings Value = new(uint.MaxValue, null, 7, 0x80000000);
        public DrawingTextSystemSettings? CaptureTextSystemSettings() { Reads++; return Value; }
        public event EventHandler<LibreSystemSettingsChangedEventArgs>? SettingsChanged { add { } remove { } }
        public Font GetMenuFont(int dpi) => throw new InvalidOperationException("No font request");
        public bool HighContrast => throw new InvalidOperationException("Legacy property HighContrast must not be read");
        public LibreSize BorderSize => throw new InvalidOperationException("Legacy property BorderSize must not be read");
        public LibreSize FixedFrameBorderSize => throw new InvalidOperationException("Legacy property FixedFrameBorderSize must not be read");
        public LibreSize Border3DSize => throw new InvalidOperationException("Legacy property Border3DSize must not be read");
        public int VerticalScrollBarWidth => throw new InvalidOperationException("Legacy property VerticalScrollBarWidth must not be read");
        public int HorizontalScrollBarHeight => throw new InvalidOperationException("Legacy property HorizontalScrollBarHeight must not be read");
        public int CaptionHeight => throw new InvalidOperationException("Legacy property CaptionHeight must not be read");
        public int MenuHeight => throw new InvalidOperationException("Legacy property MenuHeight must not be read");
        public LibreSize MinWindowTrackSize => throw new InvalidOperationException("Legacy property MinWindowTrackSize must not be read");
        public LibreSize IconSize => throw new InvalidOperationException("Legacy property IconSize must not be read");
        public LibreSize CursorSize => throw new InvalidOperationException("Legacy property CursorSize must not be read");
        public LibreSize SmallIconSize => throw new InvalidOperationException("Legacy property SmallIconSize must not be read");
        public LibreSize MinimumWindowSize => throw new InvalidOperationException("Legacy property MinimumWindowSize must not be read");
        public LibreSize CaptionButtonSize => throw new InvalidOperationException("Legacy property CaptionButtonSize must not be read");
        public LibreSize FrameBorderSize => throw new InvalidOperationException("Legacy property FrameBorderSize must not be read");
        public LibreSize MaxWindowTrackSize => throw new InvalidOperationException("Legacy property MaxWindowTrackSize must not be read");
        public LibreSize PrimaryMonitorMaximizedWindowSize => throw new InvalidOperationException("Legacy property PrimaryMonitorMaximizedWindowSize must not be read");
        public LibreSize MinimizedWindowSpacingSize => throw new InvalidOperationException("Legacy property MinimizedWindowSpacingSize must not be read");
        public int ToolWindowCaptionHeight => throw new InvalidOperationException("Legacy property ToolWindowCaptionHeight must not be read");
        public LibreSize ToolWindowCaptionButtonSize => throw new InvalidOperationException("Legacy property ToolWindowCaptionButtonSize must not be read");
        public LibreSize MenuButtonSize => throw new InvalidOperationException("Legacy property MenuButtonSize must not be read");
        public LibreSize MinimizedWindowSize => throw new InvalidOperationException("Legacy property MinimizedWindowSize must not be read");
        public int KanjiWindowHeight => throw new InvalidOperationException("Legacy property KanjiWindowHeight must not be read");
        public bool DebugOperatingSystem => throw new InvalidOperationException("Legacy property DebugOperatingSystem must not be read");
        public bool RightAlignedMenus => throw new InvalidOperationException("Legacy property RightAlignedMenus must not be read");
        public bool PenWindows => throw new InvalidOperationException("Legacy property PenWindows must not be read");
        public bool DbcsEnabled => throw new InvalidOperationException("Legacy property DbcsEnabled must not be read");
        public bool Secure => throw new InvalidOperationException("Legacy property Secure must not be read");
        public bool Network => throw new InvalidOperationException("Legacy property Network must not be read");
        public bool TerminalServerSession => throw new InvalidOperationException("Legacy property TerminalServerSession must not be read");
        public LibreBootMode BootMode => throw new InvalidOperationException("Legacy property BootMode must not be read");
        public bool ShowSounds => throw new InvalidOperationException("Legacy property ShowSounds must not be read");
        public LibreSize MenuCheckSize => throw new InvalidOperationException("Legacy property MenuCheckSize must not be read");
        public bool MidEastEnabled => throw new InvalidOperationException("Legacy property MidEastEnabled must not be read");
        public LibreMinimizedWindowStartPosition MinimizedWindowStartPosition => throw new InvalidOperationException("Legacy property MinimizedWindowStartPosition must not be read");
        public LibreMinimizedWindowDirection MinimizedWindowDirection => throw new InvalidOperationException("Legacy property MinimizedWindowDirection must not be read");
        public bool HideMinimizedWindows => throw new InvalidOperationException("Legacy property HideMinimizedWindows must not be read");
        public LibreScreenOrientation ScreenOrientation => throw new InvalidOperationException("Legacy property ScreenOrientation must not be read");
        public int SizingBorderWidth => throw new InvalidOperationException("Legacy property SizingBorderWidth must not be read");
        public LibreSize SmallCaptionButtonSize => throw new InvalidOperationException("Legacy property SmallCaptionButtonSize must not be read");
        public LibreSize MenuBarButtonSize => throw new InvalidOperationException("Legacy property MenuBarButtonSize must not be read");
        public bool LockedTerminalSession => throw new InvalidOperationException("Legacy property LockedTerminalSession must not be read");
        public int VerticalScrollBarArrowHeight => throw new InvalidOperationException("Legacy property VerticalScrollBarArrowHeight must not be read");
        public int HorizontalScrollBarArrowWidth => throw new InvalidOperationException("Legacy property HorizontalScrollBarArrowWidth must not be read");
        public int VerticalScrollBarThumbHeight => throw new InvalidOperationException("Legacy property VerticalScrollBarThumbHeight must not be read");
        public int HorizontalScrollBarThumbWidth => throw new InvalidOperationException("Legacy property HorizontalScrollBarThumbWidth must not be read");
        public LibreSize DragSize => throw new InvalidOperationException("Legacy property DragSize must not be read");
        public bool MousePresent => throw new InvalidOperationException("Legacy property MousePresent must not be read");
        public bool MouseButtonsSwapped => throw new InvalidOperationException("Legacy property MouseButtonsSwapped must not be read");
        public int MouseButtons => throw new InvalidOperationException("Legacy property MouseButtons must not be read");
        public LibreSize DoubleClickSize => throw new InvalidOperationException("Legacy property DoubleClickSize must not be read");
        public int DoubleClickTime => throw new InvalidOperationException("Legacy property DoubleClickTime must not be read");
        public bool MouseWheelPresent => throw new InvalidOperationException("Legacy property MouseWheelPresent must not be read");
        public int CaretBlinkTime => throw new InvalidOperationException("Legacy property CaretBlinkTime must not be read");
        public int MouseWheelScrollLines => throw new InvalidOperationException("Legacy property MouseWheelScrollLines must not be read");
        public bool MenuAccessKeysUnderlined => throw new InvalidOperationException("Legacy property MenuAccessKeysUnderlined must not be read");
        public int KeyboardDelay => throw new InvalidOperationException("Legacy property KeyboardDelay must not be read");
        public bool KeyboardPreferred => throw new InvalidOperationException("Legacy property KeyboardPreferred must not be read");
        public int KeyboardSpeed => throw new InvalidOperationException("Legacy property KeyboardSpeed must not be read");
        public LibreSize MouseHoverSize => throw new InvalidOperationException("Legacy property MouseHoverSize must not be read");
        public int MouseHoverTime => throw new InvalidOperationException("Legacy property MouseHoverTime must not be read");
        public int MouseSpeed => throw new InvalidOperationException("Legacy property MouseSpeed must not be read");
        public bool SnapToDefaultButton => throw new InvalidOperationException("Legacy property SnapToDefaultButton must not be read");
        public bool DragFullWindows => throw new InvalidOperationException("Legacy property DragFullWindows must not be read");
        public bool DropShadowEnabled => throw new InvalidOperationException("Legacy property DropShadowEnabled must not be read");
        public bool FlatMenuEnabled => throw new InvalidOperationException("Legacy property FlatMenuEnabled must not be read");
        public bool PopupMenusLeftAligned => throw new InvalidOperationException("Legacy property PopupMenusLeftAligned must not be read");
        public bool MenuFadeEnabled => throw new InvalidOperationException("Legacy property MenuFadeEnabled must not be read");
        public int MenuShowDelay => throw new InvalidOperationException("Legacy property MenuShowDelay must not be read");
        public bool ComboBoxAnimationEnabled => throw new InvalidOperationException("Legacy property ComboBoxAnimationEnabled must not be read");
        public bool TitleBarGradientEnabled => throw new InvalidOperationException("Legacy property TitleBarGradientEnabled must not be read");
        public bool HotTrackingEnabled => throw new InvalidOperationException("Legacy property HotTrackingEnabled must not be read");
        public bool ListBoxSmoothScrollingEnabled => throw new InvalidOperationException("Legacy property ListBoxSmoothScrollingEnabled must not be read");
        public bool MenuAnimationEnabled => throw new InvalidOperationException("Legacy property MenuAnimationEnabled must not be read");
        public bool SelectionFadeEnabled => throw new InvalidOperationException("Legacy property SelectionFadeEnabled must not be read");
        public bool ToolTipAnimationEnabled => throw new InvalidOperationException("Legacy property ToolTipAnimationEnabled must not be read");
        public bool UIEffectsEnabled => throw new InvalidOperationException("Legacy property UIEffectsEnabled must not be read");
        public bool ActiveWindowTrackingEnabled => throw new InvalidOperationException("Legacy property ActiveWindowTrackingEnabled must not be read");
        public int ActiveWindowTrackingDelay => throw new InvalidOperationException("Legacy property ActiveWindowTrackingDelay must not be read");
        public bool MinimizeRestoreAnimationEnabled => throw new InvalidOperationException("Legacy property MinimizeRestoreAnimationEnabled must not be read");
        public int BorderMultiplierFactor => throw new InvalidOperationException("Legacy property BorderMultiplierFactor must not be read");
        public int CaretWidth => throw new InvalidOperationException("Legacy property CaretWidth must not be read");
        public int VerticalFocusThickness => throw new InvalidOperationException("Legacy property VerticalFocusThickness must not be read");
        public int HorizontalFocusThickness => throw new InvalidOperationException("Legacy property HorizontalFocusThickness must not be read");
        public int VerticalResizeBorderThickness => throw new InvalidOperationException("Legacy property VerticalResizeBorderThickness must not be read");
        public int HorizontalResizeBorderThickness => throw new InvalidOperationException("Legacy property HorizontalResizeBorderThickness must not be read");
        public bool FontSmoothingEnabled => throw new InvalidOperationException("Legacy property FontSmoothingEnabled must not be read");
        public int FontSmoothingContrast => throw new InvalidOperationException("Legacy property FontSmoothingContrast must not be read");
        public int FontSmoothingType => throw new InvalidOperationException("Legacy property FontSmoothingType must not be read");
        public int IconHorizontalSpacing => throw new InvalidOperationException("Legacy property IconHorizontalSpacing must not be read");
        public int IconVerticalSpacing => throw new InvalidOperationException("Legacy property IconVerticalSpacing must not be read");
        public bool IconTitleWrappingEnabled => throw new InvalidOperationException("Legacy property IconTitleWrappingEnabled must not be read");
    }
}
