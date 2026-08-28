// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Numerics;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.Scene;
using ProGpuSolidColorBrush = ProGPU.Vector.SolidColorBrush;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public class CanonicalLifecycleTests
{
    [Fact]
    public void FormSizeConstraints_UseTypedInitialAndLivePlatformState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new()
        {
            MinimumSize = new Size(200, 150),
            MaximumSize = new Size(900, 700),
        };

        nint handle = form.Handle;

        platform.LastWindowMinimumSize.Should().Be(new LibreSize(200, 150));
        platform.LastWindowMaximumSize.Should().Be(new LibreSize(900, 700));

        form.MinimumSize = new Size(300, 240);
        platform.LastWindowMinimumSize.Should().Be(new LibreSize(300, 240));
        platform.LastWindowMaximumSize.Should().Be(new LibreSize(900, 700));
        form.Handle.Should().Be(handle);

        form.MaximumSize = new Size(640, 480);
        platform.LastWindowMinimumSize.Should().Be(new LibreSize(300, 240));
        platform.LastWindowMaximumSize.Should().Be(new LibreSize(640, 480));
        form.Handle.Should().Be(handle);

        form.MaximumSize = Size.Empty;
        platform.LastWindowMaximumSize.Should().Be(new LibreSize(0, 0));
        form.Handle.Should().Be(handle);
    }

    [Fact]
    public void MinimizeAndMaximizeBoxes_UseTypedInitialAndLiveChromeState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { MinimizeBox = false, MaximizeBox = false };

        nint handle = form.Handle;

        platform.LastWindowCanMinimize.Should().BeFalse();
        platform.LastWindowCanMaximize.Should().BeFalse();

        form.MinimizeBox = true;
        platform.LastWindowCanMinimize.Should().BeTrue();
        form.Handle.Should().Be(handle);

        form.MaximizeBox = true;
        platform.LastWindowCanMaximize.Should().BeTrue();
        form.Handle.Should().Be(handle);

        form.MinimizeBox = false;
        form.MaximizeBox = false;
        platform.LastWindowCanMinimize.Should().BeFalse();
        platform.LastWindowCanMaximize.Should().BeFalse();
        form.Handle.Should().Be(handle);
    }

    [Fact]
    public void ControlBox_UsesTypedInitialAndLiveChromeState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ControlBox = false };

        nint handle = form.Handle;

        platform.LastWindowCanClose.Should().BeFalse();
        platform.LastWindowCanMinimize.Should().BeFalse();
        platform.LastWindowCanMaximize.Should().BeFalse();

        form.ControlBox = true;
        platform.LastWindowCanClose.Should().BeTrue();
        platform.LastWindowCanMinimize.Should().BeTrue();
        platform.LastWindowCanMaximize.Should().BeTrue();
        form.Handle.Should().Be(handle);

        form.ControlBox = false;
        platform.LastWindowCanClose.Should().BeFalse();
        platform.LastWindowCanMinimize.Should().BeFalse();
        platform.LastWindowCanMaximize.Should().BeFalse();
        form.Handle.Should().Be(handle);
    }

    [Fact]
    public void Opacity_UsesTypedInitialAndLiveWholeWindowState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { Opacity = 0.35d };

        nint handle = form.Handle;

        platform.LastWindowOpacity.Should().Be(0.35d);

        form.Opacity = 0.72d;
        platform.LastWindowOpacity.Should().Be(0.72d);
        form.Handle.Should().Be(handle);

        form.Opacity = 2d;
        form.Opacity.Should().Be(1d);
        platform.LastWindowOpacity.Should().Be(1d);
        form.Handle.Should().Be(handle);

        form.Opacity = -1d;
        form.Opacity.Should().Be(0d);
        platform.LastWindowOpacity.Should().Be(0d);
        form.Handle.Should().Be(handle);

        form.Opacity = double.NaN;
        double.IsNaN(form.Opacity).Should().BeTrue();
        platform.LastWindowOpacity.Should().Be(0d);
        form.Handle.Should().Be(handle);

        form.AllowTransparency = false;
        form.Opacity.Should().Be(1d);
        platform.LastWindowOpacity.Should().Be(1d);
        form.Handle.Should().Be(handle);
    }

    [Fact]
    public void ShowInTaskbar_UsesTypedInitialAndLivePlatformState()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowInTaskbar = false };

        nint handle = form.Handle;

        platform.LastWindowShowInTaskbar.Should().BeFalse();

        form.ShowInTaskbar = true;
        platform.LastWindowShowInTaskbar.Should().BeTrue();
        form.Handle.Should().Be(handle);

        form.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        platform.LastWindowShowInTaskbar.Should().BeFalse();

        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        platform.LastWindowShowInTaskbar.Should().BeTrue();

        form.ShowInTaskbar = false;
        platform.LastWindowShowInTaskbar.Should().BeFalse();
        form.Handle.Should().Be(handle);
    }

    [Fact]
    public void FormBorderStyle_UsesTypedInitialAndLiveWindowBorder()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { FormBorderStyle = FormBorderStyle.None };

        _ = form.Handle;

        platform.LastWindowBorder.Should().Be(LibreWindowBorder.Hidden);

        form.FormBorderStyle = FormBorderStyle.Sizable;
        platform.LastWindowBorder.Should().Be(LibreWindowBorder.Resizable);

        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        platform.LastWindowBorder.Should().Be(LibreWindowBorder.Fixed);

        form.FormBorderStyle = FormBorderStyle.None;
        platform.LastWindowBorder.Should().Be(LibreWindowBorder.Hidden);
    }

    [Fact]
    public void FormTopMost_UsesTypedInitialAndLiveWindowTopMost()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { TopMost = true };

        _ = form.Handle;

        platform.LastWindowTopMost.Should().BeTrue();

        form.TopMost = false;
        platform.LastWindowTopMost.Should().BeFalse();

        form.TopMost = true;
        platform.LastWindowTopMost.Should().BeTrue();
    }

    [Fact]
    public void FormWindowState_UsesTypedInitialLiveAndPlatformDrivenTransitions()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { WindowState = FormWindowState.Maximized };

        _ = form.Handle;

        platform.LastWindowState.Should().Be(LibreWindowState.Maximized);

        form.WindowState = FormWindowState.Normal;
        platform.LastWindowState.Should().Be(LibreWindowState.Normal);

        form.Show();
        form.WindowState = FormWindowState.Minimized;
        platform.LastWindowState.Should().Be(LibreWindowState.Minimized);
        form.WindowState.Should().Be(FormWindowState.Minimized);

        platform.ChangeLastWindowState(LibreWindowState.Maximized);
        form.WindowState.Should().Be(FormWindowState.Maximized);

        platform.ChangeLastWindowState(LibreWindowState.FullScreen);
        form.WindowState.Should().Be(FormWindowState.Maximized);

        platform.ChangeLastWindowState(LibreWindowState.Normal);
        form.WindowState.Should().Be(FormWindowState.Normal);
    }

    [Fact]
    public void FormText_UsesTypedLiveWindowTitleWithoutUser32StyleRefresh()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { Text = "Initial title" };

        _ = form.Handle;

        platform.LastWindowTitle.Should().Be("Initial title");

        form.Text = "Updated title";
        platform.LastWindowTitle.Should().Be("Updated title");

        form.Text = string.Empty;
        platform.LastWindowTitle.Should().BeEmpty();

        form.Text = "Restored title";
        platform.LastWindowTitle.Should().Be("Restored title");
    }

    [Fact]
    public void FormIcon_UsesTypedRgbaWindowIconTransportAndShowIconClearsIt()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Bitmap bitmap = new(2, 1);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
        bitmap.SetPixel(1, 0, Color.FromArgb(255, 200, 150, 100));
        using MemoryStream stream = new();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        using Icon icon = new(stream);
        using Form form = new() { Icon = icon };

        _ = form.Handle;

        platform.LastWindowIcons.Should().HaveCount(2);
        LibreWindowIcon original = platform.LastWindowIcons[0];
        byte[] pixels = new byte[original.PixelByteLength];
        original.CopyPixelsTo(pixels);
        original.Width.Should().Be(2);
        original.Height.Should().Be(1);
        pixels.Should().Equal(10, 20, 30, 255, 200, 150, 100, 255);

        form.ShowIcon = false;
        platform.LastWindowIcons.Should().BeEmpty();

        form.ShowIcon = true;
        platform.LastWindowIcons.Should().HaveCount(2);
    }

    [Fact]
    public void ImageList_UsesManagedImagesWithoutHdcOrFakeNativeHandles()
    {
        using var images = new ImageList { ImageSize = new Size(4, 4) };
        using Bitmap red = CreateSolidBitmap(4, 4, Color.Red);
        using Bitmap strip = new(8, 4, PixelFormat.Format32bppArgb);
        for (int y = 0; y < strip.Height; y++)
        {
            for (int x = 0; x < strip.Width; x++)
            {
                strip.SetPixel(x, y, x < 4 ? Color.Blue : Color.Green);
            }
        }

        images.Images.Add("red", red);
        images.Images.AddStrip(strip);

        images.Images.Count.Should().Be(3);
        images.HandleCreated.Should().BeFalse();
        Action nativeHandle = () => _ = images.Handle;
        nativeHandle.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*Windows common-controls adapter*");

        using (Image first = images.Images[0])
        using (Image second = images.Images[1])
        using (Image third = images.Images[2])
        {
            ((Bitmap)first).GetPixel(2, 2).ToArgb().Should().Be(Color.Red.ToArgb());
            ((Bitmap)second).GetPixel(2, 2).ToArgb().Should().Be(Color.Blue.ToArgb());
            ((Bitmap)third).GetPixel(2, 2).ToArgb().Should().Be(Color.Green.ToArgb());
        }

        using var target = new Bitmap(12, 4, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            images.Draw(graphics, 0, 0, 0);
            images.Draw(graphics, 4, 0, 1);
            images.Draw(graphics, 8, 0, 2);
        }

        target.GetPixel(2, 2).ToArgb().Should().Be(Color.Red.ToArgb());
        target.GetPixel(6, 2).ToArgb().Should().Be(Color.Blue.ToArgb());
        target.GetPixel(10, 2).ToArgb().Should().Be(Color.Green.ToArgb());

        using Bitmap yellow = CreateSolidBitmap(4, 4, Color.Yellow);
        images.Images[0] = yellow;
        images.Images.RemoveAt(1);
        images.Images.Count.Should().Be(2);
        using (Image replacement = images.Images[0])
        using (Image remainingStripFrame = images.Images[1])
        {
            ((Bitmap)replacement).GetPixel(2, 2).ToArgb().Should().Be(Color.Yellow.ToArgb());
            ((Bitmap)remainingStripFrame).GetPixel(2, 2).ToArgb().Should().Be(Color.Green.ToArgb());
        }

        images.Images.Clear();
        images.Images.Count.Should().Be(0);

        static Bitmap CreateSolidBitmap(int width, int height, Color color)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(color);
            return bitmap;
        }
    }

    [Fact]
    public void VisualStyleBackgroundAndRegionUseTypedManagedService()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        Application.EnableVisualStyles();
        VisualStyleRenderer.IsSupported.Should().BeTrue();
        var renderer = new VisualStyleRenderer(VisualStyleElement.Button.PushButton.Normal);
        using var target = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            renderer.DrawBackground(graphics, new Rectangle(1, 1, 6, 6), new Rectangle(4, 0, 4, 8));
            using Region? region = renderer.GetBackgroundRegion(graphics, new Rectangle(1, 2, 4, 5));
            region.Should().NotBeNull();
            region!.IsVisible(2, 3).Should().BeTrue();
            region.IsVisible(0, 0).Should().BeFalse();
            renderer.GetBackgroundContentRectangle(graphics, new Rectangle(0, 0, 20, 12))
                .Should().Be(new Rectangle(2, 2, 16, 8));
            renderer.GetBackgroundExtent(graphics, new Rectangle(1, 2, 30, 12))
                .Should().Be(new Rectangle(8, 9, 40, 22));
            renderer.GetPartSize(graphics, ThemeSizeType.True).Should().Be(new Size(21, 22));
            renderer.DrawEdge(
                graphics,
                new Rectangle(0, 0, 8, 8),
                Edges.Left | Edges.Top,
                EdgeStyle.Raised,
                EdgeEffects.None).Should().Be(new Rectangle(1, 1, 7, 7));
            renderer.DrawText(
                graphics,
                new Rectangle(0, 0, 8, 8),
                "text",
                drawDisabled: false,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            renderer.GetMargins(graphics, MarginProperty.ContentMargins).Should().Be(new Padding(4, 5, 6, 7));
            using Font? themeFont = renderer.GetFont(graphics, FontProperty.TextFont);
            themeFont.Should().NotBeNull();
            themeFont!.Size.Should().Be(10f);
            renderer.GetTextExtent(
                graphics,
                new Rectangle(1, 2, 30, 12),
                "measure",
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter)
                .Should().Be(new Rectangle(6, 7, 8, 9));
            renderer.HitTestBackground(
                graphics,
                new Rectangle(1, 2, 30, 12),
                new Point(2, 3),
                HitTestOptions.ResizingBorderLeft)
                .Should().Be(HitTestCode.Left);
            using var hitRegion = new Region(new Rectangle(1, 2, 4, 4));
            renderer.HitTestBackground(
                graphics,
                new Rectangle(1, 2, 30, 12),
                hitRegion,
                new Point(2, 3),
                HitTestOptions.ResizingBorderRight)
                .Should().Be(HitTestCode.Right);
            Action nativeRegionHitTest = () => renderer.HitTestBackground(
                graphics,
                new Rectangle(1, 2, 30, 12),
                new IntPtr(1),
                new Point(2, 3),
                HitTestOptions.BackgroundSegment);
            nativeRegionHitTest.Should().Throw<PlatformNotSupportedException>();
            TextMetrics metrics = renderer.GetTextMetrics(graphics);
            metrics.Height.Should().Be(20);
            metrics.Ascent.Should().Be(14);
            metrics.Descent.Should().Be(4);
            metrics.AverageCharWidth.Should().Be(7);
            metrics.MaxCharWidth.Should().Be(12);
            metrics.Weight.Should().Be(600);
            metrics.Italic.Should().BeTrue();
            metrics.Underlined.Should().BeTrue();
            metrics.StruckOut.Should().BeFalse();
            metrics.PitchAndFamily.Should().Be(
                TextMetricsPitchAndFamilyValues.FixedPitch | TextMetricsPitchAndFamilyValues.TrueType);
            metrics.CharSet.Should().Be(TextMetricsCharacterSet.Baltic);
        }

        target.GetPixel(2, 3).ToArgb().Should().Be(0);
        target.GetPixel(5, 3).ToArgb().Should().Be(Color.Purple.ToArgb());
        renderer.GetColor(ColorProperty.TextColor).ToArgb().Should().Be(Color.Orange.ToArgb());
        renderer.GetInteger(IntegerProperty.ProgressChunkSize).Should().Be(7);
        renderer.GetBoolean(BooleanProperty.BackgroundFill).Should().BeTrue();
        renderer.GetEnumValue(EnumProperty.BackgroundType).Should().Be(1);
        renderer.GetFilename(FilenameProperty.ImageFile).Should().Be("managed-theme-image");
        renderer.GetString(StringProperty.Text).Should().Be("managed-theme-text");
        renderer.GetPoint(PointProperty.TextShadowOffset).Should().Be(new Point(2, 3));
        renderer.IsBackgroundPartiallyTransparent().Should().BeFalse();
        platform.VisualStyleDrawCount.Should().Be(1);
        platform.VisualStyleEdgeDrawCount.Should().Be(1);
        platform.VisualStyleTextDrawCount.Should().Be(1);
        Action nativeHandle = () => _ = renderer.Handle;
        nativeHandle.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*Windows UxTheme adapter*");
    }

    [Fact]
    public void VisualStyleParentBackgroundUsesManagedControlPaintingWithoutHandles()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        Application.EnableVisualStyles();
        var renderer = new VisualStyleRenderer(VisualStyleElement.Button.PushButton.Normal);
        using var parent = new ParentPaintingControl { Size = new Size(20, 20) };
        using var child = new Control { Location = new Point(4, 5), Size = new Size(6, 6) };
        parent.Controls.Add(child);
        using var target = new Bitmap(6, 6, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(target))
        {
            graphics.Clear(Color.Transparent);
            renderer.DrawParentBackground(graphics, new Rectangle(0, 0, 6, 6), child);
        }

        child.IsHandleCreated.Should().BeFalse();
        parent.BackgroundPaintCount.Should().Be(1);
        parent.ForegroundPaintCount.Should().Be(1);
        target.GetPixel(2, 2).ToArgb().Should().Be(Color.Orange.ToArgb());
        target.GetPixel(3, 3).ToArgb().Should().Be(Color.CornflowerBlue.ToArgb());
    }

    [Fact]
    public void TextRendererUsesTypedManagedServiceWithoutHdc()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var target = new Bitmap(80, 30, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Transparent);

        TextRenderer.DrawText(
            graphics,
            "portable",
            SystemFonts.DefaultFont,
            new Rectangle(4, 5, 60, 18),
            Color.Navy,
            Color.Beige,
            TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPadding
                | TextFormatFlags.TextBoxControl);
        Size headless = TextRenderer.MeasureText(
            "headless",
            SystemFonts.DefaultFont,
            new Size(70, 30),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        Size managed = TextRenderer.MeasureText(
            graphics,
            "managed",
            SystemFonts.DefaultFont,
            new Size(80, 40),
            TextFormatFlags.WordBreak | TextFormatFlags.LeftAndRightPadding);
        var nativeContext = new TrackingDeviceContext();
        Action nativeMeasure = () => TextRenderer.MeasureText(nativeContext, "native", SystemFonts.DefaultFont);

        platform.TextDrawCount.Should().Be(1);
        platform.TextMeasureCount.Should().Be(2);
        platform.LastTextBounds.Should().Be(new Rectangle(4, 5, 60, 18));
        platform.LastTextFormat.Should().Be(
            LibreTextFormat.WordBreak | LibreTextFormat.LeftAndRightPadding);
        target.GetPixel(4, 5).ToArgb().Should().Be(Color.Navy.ToArgb());
        headless.Should().Be(new Size(31, 17));
        managed.Should().Be(new Size(37, 19));
        nativeMeasure.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*managed Graphics*platform adapter*");
        nativeContext.GetHdcCalled.Should().BeFalse();
    }

    [Fact]
    public void ControlPaintDisabledTextUsesTypedManagedServiceWithoutHdc()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var target = new Bitmap(80, 30, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Transparent);

        ControlPaint.DrawStringDisabled(
            graphics,
            "disabled",
            SystemFonts.DefaultFont,
            Color.Navy,
            new Rectangle(4, 5, 60, 18),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        platform.TextDrawCount.Should().Be(2);
        platform.LastTextBounds.Should().Be(new Rectangle(4, 5, 60, 18));
        platform.LastTextFormat.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);

        var nativeContext = new TrackingDeviceContext();
        Action nativeDraw = () => ControlPaint.DrawStringDisabled(
            nativeContext,
            "disabled",
            SystemFonts.DefaultFont,
            Color.Navy,
            new Rectangle(4, 5, 60, 18),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        nativeDraw.Should().Throw<PlatformNotSupportedException>()
            .WithMessage("*managed Graphics*platform adapter*");
        nativeContext.GetHdcCalled.Should().BeFalse();
    }

    [Fact]
    public void FontAutoScaleDimensionsUseManagedTextMetricsWithoutHfont()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var container = new ContainerControl { AutoScaleMode = AutoScaleMode.Font };

        SizeF dimensions = container.CurrentAutoScaleDimensions;

        dimensions.Should().Be(new SizeF(8, container.Font.Height));
        platform.TextMeasureCount.Should().Be(1);
        platform.LastTextFormat.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
        container.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void EmptyLabelPreferredSizeUsesManagedTextMetricsWithoutHfont()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var label = new Label();

        Size preferredSize = label.GetPreferredSize(Size.Empty);

        preferredSize.Should().Be(new Size(0, label.Font.Height + 3));
        platform.TextMeasureCount.Should().Be(1);
        platform.LastTextFormat.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
        label.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void CompatibleLabelPreferredSizeUsesManagedLayoutSurfaceWithoutScreenHdc()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var label = new Label
        {
            Text = "compatible label",
            UseCompatibleTextRendering = true,
        };

        Size preferredSize = label.GetPreferredSize(Size.Empty);

        preferredSize.Should().NotBe(Size.Empty);
        platform.TextMeasureCount.Should().Be(0);
        label.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void ComboBoxPreferredHeightUsesManagedTextMetricsWithoutHfont()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var comboBox = new ComboBox { FormattingEnabled = true };
        comboBox.ItemHeight = comboBox.ItemHeight;
        int measurementsBefore = platform.TextMeasureCount;

        int preferredHeight = comboBox.PreferredHeight;

        preferredHeight.Should().Be(
            comboBox.Font.Height
                + SystemInformation.Border3DSize.Height
                + (2 * SystemInformation.FixedFrameBorderSize.Height));
        platform.TextMeasureCount.Should().Be(measurementsBefore + 2);
        platform.LastTextFormat.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
        comboBox.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void MonthCalendarDefaultSizeUsesManagedTextMetricsWithoutHfont()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        string todayText = DateTime.Now.ToShortDateString();

        using var calendar = new MonthCalendar();

        calendar.Size.Should().Be(calendar.SingleMonthSize + new Size(2, 2));
        platform.TextMeasureCount.Should().Be(1);
        platform.LastMeasuredText.Should().Be(todayText);
        platform.LastTextFormat.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
        calendar.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void ButtonPreferredSizesUseManagedLayoutSurfacesWithoutScreenHdc()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var button = new Button { Text = "button", UseCompatibleTextRendering = false };
        using var checkBox = new CheckBox { Text = "check", UseCompatibleTextRendering = false };
        using var radioButton = new RadioButton { Text = "radio", UseCompatibleTextRendering = false };
        using var compatibleButton = new Button
        {
            Text = "compatible",
            UseCompatibleTextRendering = true,
        };

        Size buttonSize = button.GetPreferredSize(Size.Empty);
        Size checkBoxSize = checkBox.GetPreferredSize(Size.Empty);
        Size radioButtonSize = radioButton.GetPreferredSize(Size.Empty);
        Size compatibleSize = compatibleButton.GetPreferredSize(Size.Empty);

        buttonSize.Should().NotBe(Size.Empty);
        checkBoxSize.Should().NotBe(Size.Empty);
        radioButtonSize.Should().NotBe(Size.Empty);
        compatibleSize.Should().NotBe(Size.Empty);
        platform.TextMeasureCount.Should().Be(3);
        platform.LastMeasuredText.Should().Be("radio");
        button.IsHandleCreated.Should().BeFalse();
        checkBox.IsHandleCreated.Should().BeFalse();
        radioButton.IsHandleCreated.Should().BeFalse();
        compatibleButton.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void DataGridViewLayoutUsesManagedGraphicsWithoutScreenHdc()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            ColumnHeadersVisible = false,
        };

        var textColumn = new DataGridViewTextBoxColumn
        {
            Width = 72,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.True },
        };
        var comboColumn = new DataGridViewComboBoxColumn { Width = 72 };
        comboColumn.Items.AddRange("first", "second");
        grid.Columns.AddRange(textColumn, comboColumn);
        int rowIndex = grid.Rows.Add("wrapped DataGridView text", "second");
        int measurementsBefore = platform.TextMeasureCount;

        SystemInformation.DragSize.Should().Be(new Size(4, 4));
        grid.AutoResizeColumn(0, DataGridViewAutoSizeColumnMode.AllCellsExceptHeader);
        grid.AutoResizeRow(rowIndex, DataGridViewAutoSizeRowMode.AllCellsExceptHeader);
        Rectangle textBounds = grid.Rows[rowIndex].Cells[0].GetContentBounds(rowIndex);
        Rectangle comboBounds = grid.Rows[rowIndex].Cells[1].GetContentBounds(rowIndex);

        grid.Columns[0].Width.Should().BeGreaterThan(0);
        grid.Rows[rowIndex].Height.Should().BeGreaterThan(0);
        textBounds.Should().NotBe(Rectangle.Empty);
        comboBounds.Should().NotBe(Rectangle.Empty);
        platform.TextMeasureCount.Should().BeGreaterThan(measurementsBefore);
        grid.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void ProfessionalColorsUseManagedLayoutGraphicsWithoutScreenHdc()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        var colors = new ProfessionalColorTable();

        colors.ButtonPressedHighlight.Should().NotBe(Color.Empty);
        colors.ButtonCheckedHighlight.Should().NotBe(Color.Empty);
    }

    [Fact]
    public void VisualStyleInformationUsesTypedPortableMetadata()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        Application.EnableVisualStyles();

        VisualStyleInformation.IsEnabledByUser.Should().BeTrue();
        VisualStyleInformation.ColorScheme.Should().Be("ManagedColor");
        VisualStyleInformation.Size.Should().Be("ManagedSize");
        VisualStyleInformation.DisplayName.Should().Be("Managed theme");
        VisualStyleInformation.Company.Should().Be("Managed company");
        VisualStyleInformation.Author.Should().Be("Managed author");
        VisualStyleInformation.Copyright.Should().Be("Managed copyright");
        VisualStyleInformation.Url.Should().Be("https://managed.test");
        VisualStyleInformation.Version.Should().Be("Managed version");
        VisualStyleInformation.Description.Should().Be("Managed description");
        VisualStyleInformation.SupportsFlatMenus.Should().BeTrue();
        VisualStyleInformation.MinimumColorDepth.Should().Be(30);
    }

    [Fact]
    public void ToolStripUsesCategorizedPortableSystemSettingsNotifications()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var toolStrip = new SettingsAwareToolStrip();
        toolStrip.Visible = false;
        toolStrip.Visible = true;
        int initialFontChanges = toolStrip.FontChangeCount;

        platform.RaiseSettingsChanged(LibreSystemSettingsChangeKind.Color);
        toolStrip.FontChangeCount.Should().Be(initialFontChanges);

        platform.RaiseSettingsChanged(LibreSystemSettingsChangeKind.Window);
        toolStrip.FontChangeCount.Should().Be(initialFontChanges + 1);

        toolStrip.Visible = false;
        platform.RaiseSettingsChanged(LibreSystemSettingsChangeKind.Window);
        toolStrip.FontChangeCount.Should().Be(initialFontChanges + 1);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableInputSettings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.MouseWheelScrollLines.Should().Be(7);
        SystemInformation.MenuAccessKeysUnderlined.Should().BeTrue();
        SystemInformation.KeyboardDelay.Should().Be(2);
        SystemInformation.IsKeyboardPreferred.Should().BeTrue();
        SystemInformation.KeyboardSpeed.Should().Be(23);
        SystemInformation.MouseHoverSize.Should().Be(new Size(13, 15));
        SystemInformation.MouseHoverTime.Should().Be(640);
        SystemInformation.MouseSpeed.Should().Be(14);
        SystemInformation.IsSnapToDefaultEnabled.Should().BeTrue();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableUiEffectSettings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.DragFullWindows.Should().BeFalse();
        SystemInformation.IsDropShadowEnabled.Should().BeFalse();
        SystemInformation.IsFlatMenuEnabled.Should().BeTrue();
        SystemInformation.PopupMenuAlignment.Should().Be(LeftRightAlignment.Right);
        SystemInformation.IsMenuFadeEnabled.Should().BeFalse();
        SystemInformation.MenuShowDelay.Should().Be(275);
        SystemInformation.IsComboBoxAnimationEnabled.Should().BeTrue();
        SystemInformation.IsTitleBarGradientEnabled.Should().BeFalse();
        SystemInformation.IsHotTrackingEnabled.Should().BeTrue();
        SystemInformation.IsListBoxSmoothScrollingEnabled.Should().BeFalse();
        SystemInformation.IsMenuAnimationEnabled.Should().BeTrue();
        SystemInformation.IsSelectionFadeEnabled.Should().BeFalse();
        SystemInformation.IsToolTipAnimationEnabled.Should().BeTrue();
        SystemInformation.UIEffectsEnabled.Should().BeFalse();
        SystemInformation.IsMinimizeRestoreAnimationEnabled.Should().BeTrue();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableRenderingAndIconSettings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.IsFontSmoothingEnabled.Should().BeFalse();
        SystemInformation.FontSmoothingContrast.Should().Be(1700);
        SystemInformation.FontSmoothingType.Should().Be(1);
        SystemInformation.IconHorizontalSpacing.Should().Be(81);
        SystemInformation.IconVerticalSpacing.Should().Be(83);
        SystemInformation.IconSpacingSize.Should().Be(new Size(81, 83));
        SystemInformation.IsIconTitleWrappingEnabled.Should().BeFalse();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableWindowTrackingAndCaretSettings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.IsActiveWindowTrackingEnabled.Should().BeTrue();
        SystemInformation.ActiveWindowTrackingDelay.Should().Be(525);
        SystemInformation.BorderMultiplierFactor.Should().Be(3);
        SystemInformation.CaretWidth.Should().Be(5);
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationAndPrintPreviewUseTypedPortableFocusAndResizeMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.VerticalFocusThickness.Should().Be(6);
        SystemInformation.HorizontalFocusThickness.Should().Be(7);
        SystemInformation.VerticalResizeBorderThickness.Should().Be(8);
        SystemInformation.HorizontalResizeBorderThickness.Should().Be(9);

        using var preview = new PrintPreviewControl();
        preview.Controls[0].Should().BeAssignableTo<HScrollBar>().Which.Left.Should().Be(7);
        preview.Controls[1].Should().BeAssignableTo<VScrollBar>().Which.Top.Should().Be(6);
        preview.IsHandleCreated.Should().BeFalse();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortablePointerAndTimingSettings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.MousePresent.Should().BeTrue();
        SystemInformation.MouseButtonsSwapped.Should().BeTrue();
        SystemInformation.MouseButtons.Should().Be(5);
        SystemInformation.DoubleClickSize.Should().Be(new Size(12, 14));
        SystemInformation.DoubleClickTime.Should().Be(650);
        SystemInformation.NativeMouseWheelSupport.Should().BeFalse();
        SystemInformation.MouseWheelPresent.Should().BeFalse();
        SystemInformation.CaretBlinkTime.Should().Be(725);
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationAndComponentEditorUseTypedPortableNonClientMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.CaptionHeight.Should().Be(29);
        SystemInformation.MenuHeight.Should().Be(31);
        SystemInformation.MinWindowTrackSize.Should().Be(new Size(140, 52));

        using var component = new System.ComponentModel.Component();
        using var editor = new System.Windows.Forms.Design.ComponentEditorForm(component, []);
        Size initialSize = editor.Size;

        platform.CaptionHeightValue = 39;
        using var tallerEditor = new System.Windows.Forms.Design.ComponentEditorForm(component, []);
        SystemInformation.CaptionHeight.Should().Be(39);
        tallerEditor.Width.Should().Be(initialSize.Width);
        tallerEditor.Height.Should().BeGreaterThan(initialSize.Height);
        editor.IsHandleCreated.Should().BeFalse();
        tallerEditor.IsHandleCreated.Should().BeFalse();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableCursorAndIconMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.IconSize.Should().Be(new Size(33, 35));
        SystemInformation.CursorSize.Should().Be(new Size(37, 39));
        SystemInformation.SmallIconSize.Should().Be(new Size(17, 19));
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableWindowGeometryMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.MinimumWindowSize.Should().Be(new Size(101, 102));
        SystemInformation.CaptionButtonSize.Should().Be(new Size(33, 34));
        SystemInformation.FrameBorderSize.Should().Be(new Size(7, 8));
        SystemInformation.MaxWindowTrackSize.Should().Be(new Size(1600, 1200));
        SystemInformation.PrimaryMonitorMaximizedWindowSize.Should().Be(new Size(1500, 1100));
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableWindowChromeAndMinimizedMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.MinimizedWindowSpacingSize.Should().Be(new Size(201, 202));
        SystemInformation.ToolWindowCaptionHeight.Should().Be(43);
        SystemInformation.ToolWindowCaptionButtonSize.Should().Be(new Size(45, 46));
        SystemInformation.MenuButtonSize.Should().Be(new Size(47, 48));
        SystemInformation.MinimizedWindowSize.Should().Be(new Size(203, 204));
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableCapabilityMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.KanjiWindowHeight.Should().Be(41);
        SystemInformation.DebugOS.Should().BeTrue();
        SystemInformation.RightAlignedMenus.Should().BeTrue();
        SystemInformation.PenWindows.Should().BeTrue();
        SystemInformation.DbcsEnabled.Should().BeTrue();
        SystemInformation.Secure.Should().BeTrue();
        SystemInformation.Network.Should().BeFalse();
        SystemInformation.TerminalServerSession.Should().BeTrue();
        SystemInformation.BootMode.Should().Be(BootMode.FailSafeWithNetwork);
        SystemInformation.ShowSounds.Should().BeTrue();
        SystemInformation.MenuCheckSize.Should().Be(new Size(27, 29));
        SystemInformation.MidEastEnabled.Should().BeTrue();
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableMinimizedWindowArrangement()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.ArrangeStartingPosition.Should().Be(
            ArrangeStartingPosition.TopRight | ArrangeStartingPosition.Hide);
        SystemInformation.ArrangeDirection.Should().Be(ArrangeDirection.Up);
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableLateDisplayMetrics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        SystemInformation.GetBorderSizeForDpi(192).Should().Be(new Size(22, 26));
        SystemInformation.ScreenOrientation.Should().Be(ScreenOrientation.Angle270);
        SystemInformation.SizingBorderWidth.Should().Be(7);
        SystemInformation.SmallCaptionButtonSize.Should().Be(new Size(31, 33));
        SystemInformation.MenuBarButtonSize.Should().Be(new Size(35, 37));
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void PowerStatusUsesTypedPortableService()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        PowerStatus power = SystemInformation.PowerStatus;

        power.PowerLineStatus.Should().Be(PowerLineStatus.Online);
        power.BatteryChargeStatus.Should().Be(BatteryChargeStatus.Low | BatteryChargeStatus.Charging);
        power.BatteryFullLifetime.Should().Be(7200);
        power.BatteryLifePercent.Should().Be(0.42f);
        power.BatteryLifeRemaining.Should().Be(1800);
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void SystemInformationUsesTypedPortableMenuFonts()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);

        using Font menuFont = SystemInformation.MenuFont;
        using Font dpiMenuFont = SystemInformation.GetMenuFontForDpi(192);
        menuFont.Size.Should().Be(11f);
        dpiMenuFont.Size.Should().Be(17f);
        platform.WindowsCreated.Should().Be(0);
        platform.Handles.Count.Should().Be(0);
    }

    [Fact]
    public void GroupBoxAndDisabledLinkLabelPaintWithoutNativeDeviceContexts()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using var target = new Bitmap(180, 60);
        using Graphics graphics = Graphics.FromImage(target);
        using var groupBox = new PaintingGroupBox
        {
            Text = "group",
            Size = new Size(160, 9),
            UseCompatibleTextRendering = false,
        };
        using var linkLabel = new PaintingLinkLabel
        {
            Text = "link",
            Enabled = false,
            Size = new Size(160, 30),
            UseCompatibleTextRendering = false,
        };

        groupBox.PaintTo(graphics);
        linkLabel.PaintTo(graphics);

        platform.TextMeasureCount.Should().BeGreaterThan(0);
        platform.TextDrawCount.Should().BeGreaterThan(1);
        groupBox.IsHandleCreated.Should().BeFalse();
        linkLabel.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void ScrollBarDefaultSizesUseTypedPortableSystemMetrics()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using var vertical = new VScrollBar();
        using var horizontal = new HScrollBar();

        SystemInformation.VerticalScrollBarWidth.Should().Be(17);
        SystemInformation.HorizontalScrollBarHeight.Should().Be(17);
        SystemInformation.VerticalScrollBarArrowHeight.Should().Be(17);
        SystemInformation.HorizontalScrollBarArrowWidth.Should().Be(17);
        SystemInformation.VerticalScrollBarThumbHeight.Should().Be(17);
        SystemInformation.HorizontalScrollBarThumbWidth.Should().Be(17);
        SystemInformation.GetVerticalScrollBarWidthForDpi(192).Should().Be(34);
        SystemInformation.GetHorizontalScrollBarHeightForDpi(192).Should().Be(34);
        SystemInformation.VerticalScrollBarArrowHeightForDpi(192).Should().Be(34);
        SystemInformation.GetHorizontalScrollBarArrowWidthForDpi(192).Should().Be(34);
        vertical.Size.Should().Be(new Size(17, 80));
        horizontal.Size.Should().Be(new Size(80, 17));
        vertical.IsHandleCreated.Should().BeFalse();
        horizontal.IsHandleCreated.Should().BeFalse();
    }

    [Fact]
    public void CanonicalManagedRenderersUsePortableVisualStylesWithoutComCtl32()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        Application.EnableVisualStyles();

        Application.RenderWithVisualStyles.Should().BeTrue();
        using var target = new Bitmap(96, 32, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Transparent);
        ButtonRenderer.DrawButton(graphics, new Rectangle(0, 0, 20, 20), PushButtonState.Normal);
        CheckBoxRenderer.DrawCheckBox(graphics, new Point(22, 3), CheckBoxState.CheckedNormal);
        RadioButtonRenderer.DrawRadioButton(graphics, new Point(40, 3), RadioButtonState.CheckedNormal);
        ComboBoxRenderer.DrawDropDownButton(graphics, new Rectangle(58, 0, 20, 20), ComboBoxState.Normal);
        TrackBarRenderer.DrawHorizontalTrack(graphics, new Rectangle(80, 0, 4, 20));

        platform.VisualStyleDrawCount.Should().BeGreaterThanOrEqualTo(5);
    }

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
        RectangleF createGraphicsVisibleClip = default;
        int paintCallbacksBeforeUpdate = -1;
        int paintCallbacksAfterUpdate = -1;
        int paintCallbacksAfterCleanUpdate = -1;
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
            paintCallbacksBeforeUpdate = paintCallbacks;
            form.Update();
            paintCallbacksAfterUpdate = paintCallbacks;
            form.Update();
            paintCallbacksAfterCleanUpdate = paintCallbacks;
            using (Graphics graphics = child.CreateGraphics())
            {
                createGraphicsVisibleClip = graphics.VisibleClipBounds;
                graphics.FillRectangle(Brushes.MediumPurple, new Rectangle(2, 3, 10, 8));
            }

            using (child.CreateGraphics())
            {
                // A recorder with no application drawing must not queue a presentation.
            }

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
        platform.PresentCount.Should().Be(2);
        paintCallbacksAfterUpdate.Should().Be(paintCallbacksBeforeUpdate + 2);
        paintCallbacksAfterCleanUpdate.Should().Be(paintCallbacksAfterUpdate);
        paintCallbacks.Should().Be(2);
        formPaintClip.Should().Be(new Rectangle(0, 0, 640, 480));
        childPaintClip.Should().Be(new Rectangle(0, 0, 120, 60));
        visibleClip.Should().Be(new RectangleF(0, 0, 640, 480));
        createGraphicsVisibleClip.Should().Be(new RectangleF(0, 0, 120, 60));
        platform.LastPaintCommandCount.Should().BeGreaterThan(0);
        platform.SawFormPaintFill.Should().BeTrue();
        platform.SawTranslatedChildPaintFill.Should().BeTrue();
        platform.CreateGraphicsCommitCount.Should().Be(1);
        platform.SawCreateGraphicsTranslatedFill.Should().BeTrue();
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
    public void ControlCreateGraphics_UsesAncestorClipWithoutNativeHwndGraphics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(100, 50) };
        using Panel parent = new() { Bounds = new Rectangle(10, 5, 30, 20) };
        using Control child = new() { Bounds = new Rectangle(20, 0, 30, 20) };
        parent.Controls.Add(child);
        form.Controls.Add(parent);

        using (Graphics graphics = child.CreateGraphics())
        {
            graphics.VisibleClipBounds.Should().Be(new RectangleF(0, 0, 10, 20));
        }

        platform.CreateGraphicsCommitCount.Should().Be(0);
    }

    [Fact]
    public void ControlCreateGraphics_FlushCommitsBatchesAndDrawingContinues()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(100, 50) };
        using Control child = new() { Bounds = new Rectangle(10, 5, 30, 20) };
        form.Controls.Add(child);
        _ = form.Handle;

        using (Graphics graphics = child.CreateGraphics())
        {
            graphics.FillRectangle(Brushes.Red, 0, 0, 4, 3);
            graphics.Flush();

            platform.CreateGraphicsCommitCount.Should().Be(1);
            platform.CreateGraphicsFlushCount.Should().Be(1);
            platform.LastCreateGraphicsFlushIntention.Should().Be(FlushIntention.Flush);

            graphics.FillRectangle(Brushes.Blue, 4, 0, 4, 3);
            graphics.Flush(FlushIntention.Sync);

            platform.CreateGraphicsCommitCount.Should().Be(2);
            platform.CreateGraphicsFlushCount.Should().Be(2);
            platform.LastCreateGraphicsFlushIntention.Should().Be(FlushIntention.Sync);
        }

        platform.CreateGraphicsCommitCount.Should().Be(2);
    }

    [Fact]
    public void RetainedPaintFrame_RepaintsDirtyLayersAndPreservesCleanSiblings()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ClientSize = new Size(320, 100) };
        using Control dirtyChild = new() { Bounds = new Rectangle(10, 10, 80, 40) };
        using Control cleanChild = new() { Bounds = new Rectangle(200, 10, 80, 40) };
        form.Controls.Add(dirtyChild);
        form.Controls.Add(cleanChild);

        int formPaints = 0;
        int dirtyChildPaints = 0;
        int cleanChildPaints = 0;
        form.Paint += (_, _) => formPaints++;
        dirtyChild.Paint += (_, _) => dirtyChildPaints++;
        cleanChild.Paint += (_, _) => cleanChildPaints++;

        form.Show();
        form.Invalidate();
        form.Update();
        formPaints.Should().Be(1);
        dirtyChildPaints.Should().Be(1);
        cleanChildPaints.Should().Be(1);
        platform.LastRetainedLayerCount.Should().Be(3);
        platform.LastRetainedLayerRepaintCount.Should().Be(3);

        formPaints = 0;
        dirtyChildPaints = 0;
        cleanChildPaints = 0;
        dirtyChild.Invalidate();
        dirtyChild.Update();

        formPaints.Should().Be(1);
        dirtyChildPaints.Should().Be(1);
        cleanChildPaints.Should().Be(0);
        platform.LastRetainedLayerCount.Should().Be(3);
        platform.LastRetainedLayerRepaintCount.Should().Be(2);
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

    [Fact]
    public void ScreenAndSystemInformation_UseTypedPortableMonitorInventory()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(
            new LibreMonitor(
                "primary",
                new(0, 0, 1920, 1080),
                new(0, 0, 1920, 1040),
                1,
                true,
                32,
                "Primary display"),
            new LibreMonitor(
                "secondary",
                new(-1280, 0, 1280, 1024),
                new(-1280, 0, 1280, 984),
                1.5,
                false,
                30,
                "Secondary display"));

        Screen[] screens = Screen.AllScreens;
        screens.Should().HaveCount(2);
        Screen.PrimaryScreen.Should().NotBeNull();
        Screen.PrimaryScreen!.DeviceName.Should().Be("Primary display");
        Screen.PrimaryScreen.Bounds.Should().Be(new Rectangle(0, 0, 1920, 1080));
        Screen.PrimaryScreen.WorkingArea.Should().Be(new Rectangle(0, 0, 1920, 1040));
        Screen.FromPoint(new Point(-100, 400)).DeviceName.Should().Be("Secondary display");
        Screen.FromRectangle(new Rectangle(-100, 100, 300, 500)).Primary.Should().BeTrue();
        SystemInformation.PrimaryMonitorSize.Should().Be(new Size(1920, 1080));
        SystemInformation.WorkingArea.Should().Be(new Rectangle(0, 0, 1920, 1040));
        SystemInformation.VirtualScreen.Should().Be(new Rectangle(-1280, 0, 3200, 1080));
        SystemInformation.MonitorCount.Should().Be(2);
        SystemInformation.MonitorsSameDisplayFormat.Should().BeFalse();

        using Form owner = new() { Bounds = new Rectangle(-1000, 100, 600, 500) };
        using CenteringForm child = new() { Size = new Size(200, 100), Owner = owner };
        Screen.FromControl(owner).DeviceName.Should().Be("Secondary display");
        child.CenterOnParent();
        child.Location.Should().Be(new Point(-800, 300));
        child.CenterOnScreen();
        child.Location.Should().Be(new Point(-740, 442));
    }

    [Fact]
    public void PresentationScaleChange_InvalidatesLogicalSurfaceWithoutDoubleScalingControls()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { Size = new Size(400, 300) };
        int deviceDpiBefore = 0;
        int deviceDpiAfter = 0;

        form.Shown += (_, _) =>
        {
            deviceDpiBefore = form.DeviceDpi;
            platform.SetPresentationScale(2.0);
            deviceDpiAfter = form.DeviceDpi;
            platform.Post(form.Close);
        };

        Application.Run(form);

        platform.LastPresentationScale.Should().Be(2.0);
        platform.PresentationInvalidationCount.Should().Be(1);
        deviceDpiBefore.Should().Be(96);
        deviceDpiAfter.Should().Be(96);
        form.Size.Should().Be(new Size(400, 300));
    }

    [Fact]
    public void LogicalPresentation_SeparatesWindowsDpiFromFramebufferScale()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetInitialPresentationScales(dpiScale: 2.0, framebufferScale: 1.0);
        using Form form = new()
        {
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(10, 20, 400, 300),
        };
        LibreRectangle initialNativeBounds = default;
        Rectangle initialManagedBounds = default;
        int initialDeviceDpi = 0;

        form.Shown += (_, _) =>
        {
            initialNativeBounds = platform.LastNativeWindowBounds;
            initialManagedBounds = form.Bounds;
            initialDeviceDpi = form.DeviceDpi;
            platform.SetPresentationScales(dpiScale: 1.0, framebufferScale: 1.0);
            platform.Post(form.Close);
        };

        Application.Run(form);

        platform.LastCoordinateMode.Should().Be(LibreWindowCoordinateMode.Logical);
        initialNativeBounds.Should().Be(new LibreRectangle(20, 40, 800, 600));
        initialManagedBounds.Should().Be(new Rectangle(10, 20, 400, 300));
        initialDeviceDpi.Should().Be(96);
        platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(10, 20, 400, 300));
        form.Bounds.Should().Be(new Rectangle(10, 20, 400, 300));
        form.DeviceDpi.Should().Be(96);
    }

    [Fact]
    public void PerMonitorV2_UsesDevicePixelCoordinatesAndRaisesCanonicalDpiEvents()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(new LibreMonitor(
            "primary",
            new(0, 0, 1920, 1080),
            new(0, 0, 1920, 1040),
            2.0,
            true));
        platform.SetInitialPresentationScales(dpiScale: 2.0, framebufferScale: 2.0);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2).Should().BeTrue();

        using Form form = new()
        {
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96),
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(10, 20, 400, 300),
        };
        using Control child = new() { Bounds = new Rectangle(20, 30, 100, 40) };
        form.Controls.Add(child);

        int initialFormDpi = 0;
        int initialChildDpi = 0;
        Rectangle initialFormBounds = default;
        Rectangle initialChildBounds = default;
        int changedFormDpi = 0;
        int changedChildDpi = 0;
        Rectangle changedFormBounds = default;
        Rectangle changedChildBounds = default;
        DpiChangedEventArgs? changed = null;
        Exception? callbackException = null;
        List<string> dpiEvents = [];

        child.DpiChangedBeforeParent += (_, _) => dpiEvents.Add("child-before");
        form.DpiChanged += (_, e) =>
        {
            dpiEvents.Add("form");
            changed = e;
        };
        child.DpiChangedAfterParent += (_, _) => dpiEvents.Add("child-after");
        form.Shown += (_, _) =>
        {
            try
            {
                initialFormDpi = form.DeviceDpi;
                initialChildDpi = child.DeviceDpi;
                initialFormBounds = form.Bounds;
                initialChildBounds = child.Bounds;

                platform.SetPresentationScale(1.0);

                changedFormDpi = form.DeviceDpi;
                changedChildDpi = child.DeviceDpi;
                changedFormBounds = form.Bounds;
                changedChildBounds = child.Bounds;
            }
            catch (Exception exception)
            {
                callbackException = exception;
            }
            finally
            {
                platform.Post(form.Close);
            }
        };

        try
        {
            Application.Run(form);
        }
        finally
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware).Should().BeTrue();
        }

        platform.LastCoordinateMode.Should().Be(LibreWindowCoordinateMode.DevicePixels);
        callbackException.Should().BeNull();
        initialFormDpi.Should().Be(192);
        initialChildDpi.Should().Be(192);
        initialFormBounds.Should().Be(new Rectangle(10, 20, 800, 600));
        initialChildBounds.Should().Be(new Rectangle(40, 60, 200, 80));
        changedFormDpi.Should().Be(96);
        changedChildDpi.Should().Be(96);
        changedFormBounds.Should().Be(new Rectangle(5, 10, 400, 300));
        changedChildBounds.Should().Be(new Rectangle(20, 30, 100, 40));
        changed.Should().NotBeNull();
        changed!.DeviceDpiOld.Should().Be(192);
        changed.DeviceDpiNew.Should().Be(96);
        changed.SuggestedRectangle.Should().Be(new Rectangle(5, 10, 400, 300));
        dpiEvents.Should().ContainInOrder("child-before", "form", "child-after");
        platform.PresentationInvalidationCount.Should().Be(1);
    }

    [Fact]
    public void PerMonitorV2_SeparatesWindowsDpiFromFramebufferScale()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(new LibreMonitor(
            "primary",
            new(0, 0, 1920, 1080),
            new(0, 0, 1920, 1040),
            2.0,
            true));
        platform.SetInitialPresentationScales(dpiScale: 2.0, framebufferScale: 1.0);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2).Should().BeTrue();

        using Form form = new()
        {
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96),
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(10, 20, 400, 300),
        };
        Rectangle initialManagedBounds = default;
        LibreRectangle initialNativeBounds = default;

        form.Shown += (_, _) =>
        {
            initialManagedBounds = form.Bounds;
            initialNativeBounds = platform.LastNativeWindowBounds;
            platform.SetPresentationScales(dpiScale: 1.0, framebufferScale: 1.0);
            platform.Post(form.Close);
        };

        try
        {
            Application.Run(form);
        }
        finally
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware).Should().BeTrue();
        }

        initialManagedBounds.Should().Be(new Rectangle(10, 20, 800, 600));
        initialNativeBounds.Should().Be(new LibreRectangle(10, 20, 800, 600));
        form.Bounds.Should().Be(new Rectangle(10, 20, 400, 300));
        platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(10, 20, 400, 300));
        form.DeviceDpi.Should().Be(96);
    }

    [Fact]
    public void BringToFrontAndSendToBack_PreserveCanonicalChildAndTopLevelSemantics()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new();
        using Control first = new();
        using Control second = new();
        form.Controls.Add(first);
        form.Controls.Add(second);

        _ = form.Handle;
        _ = first.Handle;
        _ = second.Handle;
        nint formHandle = form.Handle;

        first.BringToFront();
        form.Controls.GetChildIndex(first).Should().Be(0);
        platform.WindowZOrderChangeCount.Should().Be(0);

        first.SendToBack();
        form.Controls.GetChildIndex(first).Should().Be(form.Controls.Count - 1);
        platform.WindowZOrderChangeCount.Should().Be(0);

        form.BringToFront();
        platform.LastWindowZOrder.Should().Be(LibreWindowZOrder.Front);
        platform.WindowZOrderChangeCount.Should().Be(1);
        form.Handle.Should().Be(formHandle);

        form.SendToBack();
        platform.LastWindowZOrder.Should().Be(LibreWindowZOrder.Back);
        platform.WindowZOrderChangeCount.Should().Be(2);
        form.Handle.Should().Be(formHandle);
    }

    [Fact]
    public void StockCursors_UseTypedPortableTransportWithHoverInheritanceAndCapture()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new()
        {
            Bounds = new Rectangle(20, 30, 240, 160),
            Cursor = Cursors.Cross,
        };
        using Control child = new()
        {
            Bounds = new Rectangle(10, 12, 80, 50),
            Cursor = Cursors.Hand,
        };
        form.Controls.Add(child);
        int cursorChanged = 0;
        child.CursorChanged += (_, _) => cursorChanged++;

        _ = form.Handle;
        form.Show();
        Cursors.Default.Should().Be(Cursors.Arrow);

        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(15, 18));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Hand);

        child.Cursor = Cursors.IBeam;
        platform.LastCursorShape.Should().Be(LibreCursorShape.IBeam);
        cursorChanged.Should().Be(1);

        child.Capture = true;
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(160, 100));
        (Cursor Cursor, LibreCursorShape Shape)[] stockCursors =
        [
            (Cursors.AppStarting, LibreCursorShape.AppStarting),
            (Cursors.Arrow, LibreCursorShape.Arrow),
            (Cursors.Cross, LibreCursorShape.Cross),
            (Cursors.Default, LibreCursorShape.Arrow),
            (Cursors.IBeam, LibreCursorShape.IBeam),
            (Cursors.No, LibreCursorShape.No),
            (Cursors.SizeAll, LibreCursorShape.SizeAll),
            (Cursors.SizeNESW, LibreCursorShape.SizeNESW),
            (Cursors.SizeNS, LibreCursorShape.SizeNS),
            (Cursors.SizeNWSE, LibreCursorShape.SizeNWSE),
            (Cursors.SizeWE, LibreCursorShape.SizeWE),
            (Cursors.UpArrow, LibreCursorShape.UpArrow),
            (Cursors.WaitCursor, LibreCursorShape.Wait),
            (Cursors.Help, LibreCursorShape.Help),
            (Cursors.Hand, LibreCursorShape.Hand),
            (Cursors.HSplit, LibreCursorShape.HSplit),
            (Cursors.VSplit, LibreCursorShape.VSplit),
            (Cursors.NoMove2D, LibreCursorShape.NoMove2D),
            (Cursors.NoMoveHoriz, LibreCursorShape.NoMoveHoriz),
            (Cursors.NoMoveVert, LibreCursorShape.NoMoveVert),
            (Cursors.PanEast, LibreCursorShape.PanEast),
            (Cursors.PanNE, LibreCursorShape.PanNE),
            (Cursors.PanNorth, LibreCursorShape.PanNorth),
            (Cursors.PanNW, LibreCursorShape.PanNW),
            (Cursors.PanSE, LibreCursorShape.PanSE),
            (Cursors.PanSouth, LibreCursorShape.PanSouth),
            (Cursors.PanSW, LibreCursorShape.PanSW),
            (Cursors.PanWest, LibreCursorShape.PanWest),
        ];
        foreach ((Cursor cursor, LibreCursorShape shape) in stockCursors)
        {
            child.Cursor = cursor;
            platform.LastCursorShape.Should().Be(shape);
        }

        child.Capture = false;
        platform.LastCursorShape.Should().Be(LibreCursorShape.Cross);

        child.Cursor = null!;
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(15, 18));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Cross);

        child.UseWaitCursor = true;
        platform.LastCursorShape.Should().Be(LibreCursorShape.Wait);
        cursorChanged.Should().BeGreaterThan(20);
        platform.CursorChangeCount.Should().BeGreaterThan(20);
    }

    [Fact]
    public void PreCreatedChildHandle_ReparentsThroughCanonicalManagedTreeWithoutNativeParenting()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new()
        {
            Bounds = new Rectangle(20, 30, 280, 180),
        };
        using Panel left = new()
        {
            Bounds = new Rectangle(0, 0, 100, 100),
            Cursor = Cursors.Cross,
        };
        using Panel right = new()
        {
            Bounds = new Rectangle(120, 0, 100, 100),
            Cursor = Cursors.IBeam,
        };
        using Control child = new()
        {
            Bounds = new Rectangle(10, 10, 40, 40),
            Cursor = Cursors.Hand,
        };
        int parentChanged = 0;
        child.ParentChanged += (_, _) => parentChanged++;
        form.Controls.Add(left);
        form.Controls.Add(right);
        left.Controls.Add(child);

        nint childHandle = child.Handle;
        child.IsHandleCreated.Should().BeTrue();
        form.IsHandleCreated.Should().BeFalse();

        form.Show();
        child.Handle.Should().Be(childHandle);
        child.Parent.Should().BeSameAs(left);
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(15, 15));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Hand);

        right.Controls.Add(child);
        child.Handle.Should().Be(childHandle);
        child.Parent.Should().BeSameAs(right);
        left.Controls.Count.Should().Be(0);
        right.Controls.Count.Should().Be(1);
        right.Controls[0].Should().BeSameAs(child);
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(15, 15));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Cross);
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(135, 15));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Hand);

        right.Controls.Remove(child);
        child.Handle.Should().Be(childHandle);
        child.Parent.Should().BeNull();
        left.Controls.Add(child);
        child.Handle.Should().Be(childHandle);
        child.Parent.Should().BeSameAs(left);
        parentChanged.Should().Be(5);
        platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(15, 15));
        platform.LastCursorShape.Should().Be(LibreCursorShape.Hand);
    }

    [Fact]
    public void BaseAndFormHandleRecreation_UseLogicalAndTypedPortableLifecycles()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using RecreatingForm form = new()
        {
            Bounds = new Rectangle(20, 30, 280, 180),
            StartPosition = FormStartPosition.CenterScreen,
        };
        using RecreatingControl child = new()
        {
            Bounds = new Rectangle(10, 12, 80, 50),
        };
        using Control descendant = new()
        {
            Bounds = new Rectangle(2, 3, 20, 15),
        };
        child.Controls.Add(descendant);
        form.Controls.Add(child);
        form.Show();
        platform.SendInput(LibreInputEventKind.FocusGained);
        child.Focus().Should().BeTrue();

        nint originalFormHandle = form.Handle;
        nint originalChildHandle = child.Handle;
        nint descendantHandle = descendant.Handle;
        int childHandleCreated = 0;
        int childHandleDestroyed = 0;
        bool childCreatedWhileRecreating = false;
        bool childDestroyedWhileRecreating = false;
        child.HandleCreated += (_, _) =>
        {
            childHandleCreated++;
            childCreatedWhileRecreating = child.RecreatingHandle;
        };
        child.HandleDestroyed += (_, _) =>
        {
            childHandleDestroyed++;
            childDestroyedWhileRecreating = child.RecreatingHandle;
        };

        child.RecreatePortableHandle();

        child.Handle.Should().NotBe(originalChildHandle);
        child.IsHandleCreated.Should().BeTrue();
        child.Created.Should().BeTrue();
        child.Parent.Should().BeSameAs(form);
        descendant.Handle.Should().Be(descendantHandle);
        form.Handle.Should().Be(originalFormHandle);
        child.ContainsFocus.Should().BeTrue();
        childHandleCreated.Should().Be(1);
        childHandleDestroyed.Should().Be(1);
        childCreatedWhileRecreating.Should().BeTrue();
        childDestroyedWhileRecreating.Should().BeTrue();
        child.RecreatingHandle.Should().BeFalse();

        nint recreatedChildHandle = child.Handle;
        int formHandleCreated = 0;
        int formHandleDestroyed = 0;
        bool formCreatedWhileRecreating = false;
        bool formDestroyedWhileRecreating = false;
        form.HandleCreated += (_, _) =>
        {
            formHandleCreated++;
            formCreatedWhileRecreating = form.RecreatingHandle;
        };
        form.HandleDestroyed += (_, _) =>
        {
            formHandleDestroyed++;
            formDestroyedWhileRecreating = form.RecreatingHandle;
        };

        form.RecreatePortableHandle();

        form.Handle.Should().NotBe(originalFormHandle);
        form.IsHandleCreated.Should().BeTrue();
        form.Created.Should().BeTrue();
        form.Visible.Should().BeTrue();
        form.Bounds.Should().Be(new Rectangle(20, 30, 280, 180));
        form.StartPosition.Should().Be(FormStartPosition.CenterScreen);
        child.Handle.Should().Be(recreatedChildHandle);
        descendant.Handle.Should().Be(descendantHandle);
        platform.WindowsCreated.Should().Be(2);
        formHandleCreated.Should().Be(1);
        formHandleDestroyed.Should().Be(1);
        formCreatedWhileRecreating.Should().BeTrue();
        formDestroyedWhileRecreating.Should().BeTrue();
        form.RecreatingHandle.Should().BeFalse();
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

    private sealed class CenteringForm : Form
    {
        internal void CenterOnParent() => CenterToParent();

        internal void CenterOnScreen() => CenterToScreen();
    }

    private sealed class RecreatingControl : Control
    {
        internal RecreatingControl()
            => SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.UserPaint, true);

        internal void RecreatePortableHandle() => RecreateHandle();
    }

    private sealed class RecreatingForm : Form
    {
        internal void RecreatePortableHandle() => RecreateHandle();
    }

    private sealed class PaintingGroupBox : GroupBox
    {
        internal void PaintTo(Graphics graphics)
        {
            using var e = new PaintEventArgs(graphics, ClientRectangle);
            OnPaint(e);
        }
    }

    private sealed class SettingsAwareToolStrip : ToolStrip
    {
        internal int FontChangeCount { get; private set; }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FontChangeCount++;
        }
    }

    private sealed class PaintingLinkLabel : LinkLabel
    {
        internal void PaintTo(Graphics graphics)
        {
            using var e = new PaintEventArgs(graphics, ClientRectangle);
            OnPaint(e);
        }
    }

    private sealed class TrackingDeviceContext : IDeviceContext
    {
        internal bool GetHdcCalled { get; private set; }

        public IntPtr GetHdc()
        {
            GetHdcCalled = true;
            throw new InvalidOperationException("Portable canonical text must not acquire this HDC.");
        }

        public void ReleaseHdc()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ParentPaintingControl : Control
    {
        internal int BackgroundPaintCount { get; private set; }
        internal int ForegroundPaintCount { get; private set; }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            BackgroundPaintCount++;
            using var background = new SolidBrush(Color.CornflowerBlue);
            using var marker = new SolidBrush(Color.Orange);
            pevent.Graphics.FillRectangle(background, ClientRectangle);
            pevent.Graphics.FillRectangle(marker, new Rectangle(6, 7, 1, 1));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ForegroundPaintCount++;
        }
    }

    private sealed class HeadlessPlatform :
        ILibreDispatcher,
        ILibreTimerService,
        ILibreWindowService,
        ILibreMonitorService,
        ILibrePaintService,
        ILibreVisualStyleService,
        ILibreSystemSettingsService,
        ILibreTextRendererService,
        ILibrePowerStatusService
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        private bool _autoCloseWindows;
        private readonly Dictionary<Form, LibreHandle> _formHandles = [];
        private bool _exitRequested;
        private double? _initialDpiScale;
        private double? _initialFramebufferScale;
        private HeadlessWindow? _lastWindow;
        private IReadOnlyList<LibreMonitor> _monitors = CreateDefaultMonitorInventory();

        internal HeadlessPlatform(bool autoCloseWindows = true)
        {
            _autoCloseWindows = autoCloseWindows;
            Handles = new ManagedLibreHandleRegistry();
            Services = new LibrePlatformServices(
                this,
                this,
                Handles,
                this,
                this,
                this,
                UnsupportedLibreDesktopCaptureService.Instance,
                UnsupportedLibreNativeFontInteropService.Instance,
                UnsupportedLibreNativeGraphicsInteropService.Instance,
                this,
                this,
                this,
                this);
        }

        internal void Reset(bool autoCloseWindows)
        {
            Handles.Count.Should().Be(0);
            _autoCloseWindows = autoCloseWindows;
            _exitRequested = false;
            _initialDpiScale = null;
            _initialFramebufferScale = null;
            _lastWindow = null;
            _monitors = CreateDefaultMonitorInventory();
            CaptionHeightValue = 29;
            _formHandles.Clear();
            while (_queue.TryDequeue(out _))
            {
            }

            WindowsCreated = 0;
            LastWindowBounds = default;
            LastNativeWindowBounds = default;
            LastDirtyRectangle = default;
            PresentCount = 0;
            PresentationInvalidationCount = 0;
            LastPresentationScale = 1.0;
            LastCoordinateMode = LibreWindowCoordinateMode.Logical;
            LastPaintCommandCount = 0;
            LastRetainedLayerCount = 0;
            LastRetainedLayerRepaintCount = 0;
            SawFormPaintFill = false;
            SawTranslatedChildPaintFill = false;
            CreateGraphicsCommitCount = 0;
            CreateGraphicsFlushCount = 0;
            LastCreateGraphicsFlushIntention = null;
            SawCreateGraphicsTranslatedFill = false;
            LastActivatedWindow = default;
            LastWindowTitle = string.Empty;
            LastWindowState = LibreWindowState.Normal;
            LastWindowTopMost = false;
            LastWindowBorder = LibreWindowBorder.Hidden;
            LastWindowShowInTaskbar = true;
            LastWindowCanClose = true;
            LastWindowCanMinimize = true;
            LastWindowCanMaximize = true;
            LastWindowOpacity = 1d;
            LastWindowZOrder = null;
            WindowZOrderChangeCount = 0;
            LastCursorShape = null;
            CursorChangeCount = 0;
            LastWindowIcons = [];
            VisualStyleDrawCount = 0;
            VisualStyleEdgeDrawCount = 0;
            VisualStyleTextDrawCount = 0;
            TextDrawCount = 0;
            TextMeasureCount = 0;
            LastTextBounds = default;
            LastTextFormat = default;
            LastMeasuredText = string.Empty;
        }

        internal ManagedLibreHandleRegistry Handles { get; }

        public event EventHandler<LibreSystemSettingsChangedEventArgs>? SettingsChanged;

        internal void RaiseSettingsChanged(LibreSystemSettingsChangeKind kind)
            => SettingsChanged?.Invoke(this, new(kind));

        internal LibrePlatformServices Services { get; }

        internal int WindowsCreated { get; private set; }

        internal int CaptionHeightValue { get; set; } = 29;

        internal LibreRectangle LastWindowBounds { get; private set; }

        internal LibreRectangle LastNativeWindowBounds { get; private set; }

        internal LibreRectangle LastDirtyRectangle { get; private set; }

        internal int PresentCount { get; private set; }

        internal int PresentationInvalidationCount { get; private set; }

        internal int VisualStyleDrawCount { get; private set; }
        internal int VisualStyleEdgeDrawCount { get; private set; }
        internal int VisualStyleTextDrawCount { get; private set; }
        internal int TextDrawCount { get; private set; }
        internal int TextMeasureCount { get; private set; }
        internal Rectangle LastTextBounds { get; private set; }
        internal LibreTextFormat LastTextFormat { get; private set; }
        internal string LastMeasuredText { get; private set; } = string.Empty;

        public bool HighContrast => false;
        public Font GetMenuFont(int dpi)
            => new(FontFamily.GenericMonospace, dpi == 0 ? 11f : 17f);
        public LibreSize BorderSize => new(11, 13);
        public LibreSize FixedFrameBorderSize => new(3, 3);
        public LibreSize Border3DSize => new(2, 2);
        public int VerticalScrollBarWidth => 17;
        public int HorizontalScrollBarHeight => 17;
        public int CaptionHeight => CaptionHeightValue;
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
        public LibreScreenOrientation ScreenOrientation => LibreScreenOrientation.Angle270;
        public int SizingBorderWidth => 7;
        public LibreSize SmallCaptionButtonSize => new(31, 33);
        public LibreSize MenuBarButtonSize => new(35, 37);
        public bool LockedTerminalSession => true;
        public LibrePowerStatusSnapshot GetCurrentStatus()
            => new(
                LibrePowerLineStatus.Online,
                LibreBatteryChargeStatus.Low | LibreBatteryChargeStatus.Charging,
                7200,
                0.42f,
                1800);
        public int VerticalScrollBarArrowHeight => 17;
        public int HorizontalScrollBarArrowWidth => 17;
        public int VerticalScrollBarThumbHeight => 17;
        public int HorizontalScrollBarThumbWidth => 17;
        public LibreSize DragSize => new(4, 4);
        public bool MousePresent => true;
        public bool MouseButtonsSwapped => true;
        public int MouseButtons => 5;
        public LibreSize DoubleClickSize => new(12, 14);
        public int DoubleClickTime => 650;
        public bool MouseWheelPresent => false;
        public int CaretBlinkTime => 725;
        public int MouseWheelScrollLines => 7;
        public bool MenuAccessKeysUnderlined => true;
        public int KeyboardDelay => 2;
        public bool KeyboardPreferred => true;
        public int KeyboardSpeed => 23;
        public LibreSize MouseHoverSize => new(13, 15);
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

        public string ThemeFilename => "managed.theme";
        public string ColorScheme => "ManagedColor";
        public string ThemeSize => "ManagedSize";
        public string DisplayName => "Managed theme";
        public string Company => "Managed company";
        public string Author => "Managed author";
        public string Copyright => "Managed copyright";
        public string Url => "https://managed.test";
        public string Version => "Managed version";
        public string Description => "Managed description";
        public bool SupportsFlatMenus => true;
        public int MinimumColorDepth => 30;

        internal double LastPresentationScale { get; private set; } = 1.0;

        internal LibreWindowCoordinateMode LastCoordinateMode { get; private set; }

        internal int LastPaintCommandCount { get; private set; }

        internal int LastRetainedLayerCount { get; private set; }

        internal int LastRetainedLayerRepaintCount { get; private set; }

        internal bool SawFormPaintFill { get; private set; }

        internal bool SawTranslatedChildPaintFill { get; private set; }

        internal int CreateGraphicsCommitCount { get; private set; }

        internal int CreateGraphicsFlushCount { get; private set; }

        internal FlushIntention? LastCreateGraphicsFlushIntention { get; private set; }

        internal bool SawCreateGraphicsTranslatedFill { get; private set; }

        internal LibreHandle LastActivatedWindow { get; private set; }

        internal string LastWindowTitle { get; private set; } = string.Empty;

        internal LibreWindowState LastWindowState { get; private set; }

        internal bool LastWindowTopMost { get; private set; }

        internal LibreWindowBorder LastWindowBorder { get; private set; }

        internal bool LastWindowShowInTaskbar { get; private set; }

        internal bool LastWindowCanClose { get; private set; }

        internal bool LastWindowCanMinimize { get; private set; }

        internal bool LastWindowCanMaximize { get; private set; }

        internal double LastWindowOpacity { get; private set; }

        internal LibreWindowZOrder? LastWindowZOrder { get; private set; }

        internal int WindowZOrderChangeCount { get; private set; }

        internal LibreCursorShape? LastCursorShape { get; private set; }

        internal int CursorChangeCount { get; private set; }

        internal LibreSize LastWindowMinimumSize { get; private set; }

        internal LibreSize LastWindowMaximumSize { get; private set; }

        internal IReadOnlyList<LibreWindowIcon> LastWindowIcons { get; private set; } = [];

        internal void ChangeLastWindowState(LibreWindowState state)
        {
            _lastWindow.Should().NotBeNull();
            _lastWindow!.State = state;
        }

        internal void SetMonitors(params LibreMonitor[] monitors)
        {
            monitors.Should().NotBeEmpty();
            _monitors = monitors;
        }

        internal void SetInitialPresentationScales(double dpiScale, double framebufferScale)
        {
            _initialDpiScale = dpiScale;
            _initialFramebufferScale = framebufferScale;
        }

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
            LastCoordinateMode = options.CoordinateMode;
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

        internal void SetPresentationScale(double scale)
            => SetPresentationScales(scale, scale);

        internal void SetPresentationScales(double dpiScale, double framebufferScale)
        {
            _lastWindow.Should().NotBeNull();
            _lastWindow!.SetPresentationScales(dpiScale, framebufferScale);
            LastPresentationScale = dpiScale;
        }

        public IReadOnlyList<LibreMonitor> GetMonitors()
            => _monitors;

        public LibreMonitor GetNearest(LibreRectangle bounds)
            => LibreMonitorSelection.GetNearest(_monitors, bounds);

        public Graphics CreateGraphics(
            LibreHandle target,
            LibrePoint origin,
            LibreRectangle clipRectangle)
        {
            if (Handles.TryGet(target, out HeadlessWindow? window))
            {
                return window.CreateGraphics(origin, clipRectangle);
            }

            Handles.TryGet<object>(target, out _).Should().BeTrue();
            DrawingContext recording = new();
            Graphics graphics = Graphics.FromProGpuDrawingContext(
                recording,
                new RectangleF(
                    clipRectangle.X,
                    clipRectangle.Y,
                    clipRectangle.Width,
                    clipRectangle.Height),
                Matrix4x4.CreateTranslation(origin.X, origin.Y, 0f),
                () => recording.Clear());
            graphics.SetClip(new RectangleF(
                clipRectangle.X - origin.X,
                clipRectangle.Y - origin.Y,
                clipRectangle.Width,
                clipRectangle.Height));
            return graphics;
        }

        private static IReadOnlyList<LibreMonitor> CreateDefaultMonitorInventory()
            => [new("headless", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1, true)];

        public void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle)
        {
            Handles.TryGet(target, out HeadlessWindow? window).Should().BeTrue();
            LastDirtyRectangle = dirtyRectangle;
            window!.RequestPaint(dirtyRectangle);
        }

        public void InvalidateAll(LibreHandle target)
        {
            Handles.TryGet(target, out HeadlessWindow? window).Should().BeTrue();
            PresentationInvalidationCount++;
            LibreRectangle bounds = window!.Bounds;
            window.RequestPaint(new LibreRectangle(0, 0, bounds.Width, bounds.Height));
        }

        public void Present(LibreHandle target)
        {
            Handles.TryGet(target, out HeadlessWindow? window).Should().BeTrue();
            PresentCount++;
            window!.PresentPendingPaint();
        }

        public bool IsEnabled => true;

        public bool IsElementDefined(string className, int part)
            => !string.IsNullOrWhiteSpace(className) && part >= 0;

        public void DrawBackground(
            Graphics graphics,
            string className,
            int part,
            int state,
            Rectangle bounds,
            Rectangle? clipRectangle)
        {
            VisualStyleDrawCount++;
            GraphicsState saved = graphics.Save();
            try
            {
                if (clipRectangle is Rectangle clip)
                {
                    graphics.SetClip(clip, CombineMode.Intersect);
                }

                using var brush = new SolidBrush(Color.Purple);
                graphics.FillRectangle(brush, bounds);
            }
            finally
            {
                graphics.Restore(saved);
            }
        }

        public Region? GetBackgroundRegion(string className, int part, int state, Rectangle bounds)
            => new(bounds);

        public Rectangle GetBackgroundContentRectangle(string className, int part, int state, Rectangle bounds)
            => Rectangle.Inflate(bounds, -2, -2);

        public Rectangle GetBackgroundExtent(string className, int part, int state, Rectangle contentBounds)
        {
            contentBounds.Should().Be(new Rectangle(1, 2, 30, 12));
            return new Rectangle(8, 9, 40, 22);
        }

        public Size GetPartSize(
            string className,
            int part,
            int state,
            Rectangle? bounds,
            LibreVisualStyleSizeType type)
            => new(21, 22);

        public Color GetColor(
            string className,
            int part,
            int state,
            LibreVisualStyleColorProperty property)
            => Color.Orange;

        public int GetInteger(
            string className,
            int part,
            int state,
            LibreVisualStyleIntegerProperty property)
            => property == LibreVisualStyleIntegerProperty.ProgressChunkSize ? 7 : 3;

        public bool GetBoolean(
            string className,
            int part,
            int state,
            LibreVisualStyleBooleanProperty property)
        {
            property.Should().Be(LibreVisualStyleBooleanProperty.BackgroundFill);
            return true;
        }

        public int GetEnumValue(
            string className,
            int part,
            int state,
            LibreVisualStyleEnumProperty property)
        {
            property.Should().Be(LibreVisualStyleEnumProperty.BackgroundType);
            return 1;
        }

        public string GetFilename(
            string className,
            int part,
            int state,
            LibreVisualStyleFilenameProperty property)
        {
            property.Should().Be(LibreVisualStyleFilenameProperty.ImageFile);
            return "managed-theme-image";
        }

        public string GetString(
            string className,
            int part,
            int state,
            LibreVisualStyleStringProperty property)
        {
            property.Should().Be(LibreVisualStyleStringProperty.Text);
            return "managed-theme-text";
        }

        public Font? GetFont(
            string className,
            int part,
            int state,
            LibreVisualStyleFontProperty property)
        {
            property.Should().Be(LibreVisualStyleFontProperty.Text);
            return new Font(SystemFonts.DefaultFont.FontFamily, 10f);
        }

        public Rectangle MeasureText(
            Graphics graphics,
            string className,
            int part,
            int state,
            Rectangle? bounds,
            string text,
            LibreVisualStyleTextFormat format)
        {
            bounds.Should().Be(new Rectangle(1, 2, 30, 12));
            text.Should().Be("measure");
            format.Should().Be(LibreVisualStyleTextFormat.Right | LibreVisualStyleTextFormat.VerticalCenter);
            return new Rectangle(6, 7, 8, 9);
        }

        public LibreVisualStyleHitTestCode HitTestBackground(
            Graphics graphics,
            string className,
            int part,
            int state,
            Rectangle bounds,
            Region? region,
            Point point,
            LibreVisualStyleHitTestOptions options)
        {
            bounds.Should().Be(new Rectangle(1, 2, 30, 12));
            point.Should().Be(new Point(2, 3));
            if (region is null)
            {
                options.Should().Be(LibreVisualStyleHitTestOptions.ResizingBorderLeft);
                return LibreVisualStyleHitTestCode.Left;
            }

            options.Should().Be(LibreVisualStyleHitTestOptions.ResizingBorderRight);
            region.IsVisible(point, graphics).Should().BeTrue();
            return LibreVisualStyleHitTestCode.Right;
        }

        public LibreVisualStyleTextMetrics GetTextMetrics(
            Graphics graphics,
            string className,
            int part,
            int state)
            => new(
                Height: 20,
                Ascent: 14,
                Descent: 4,
                InternalLeading: 1,
                ExternalLeading: 1,
                AverageCharWidth: 7,
                MaxCharWidth: 12,
                Weight: 600,
                Overhang: 0,
                DigitizedAspectX: 96,
                DigitizedAspectY: 96,
                FirstChar: ' ',
                LastChar: '~',
                DefaultChar: '?',
                BreakChar: ' ',
                Italic: true,
                Underlined: true,
                StruckOut: false,
                PitchAndFamily: LibreVisualStyleTextPitchAndFamily.FixedPitch
                    | LibreVisualStyleTextPitchAndFamily.TrueType,
                CharacterSet: LibreVisualStyleTextCharacterSet.Baltic);

        public LibreVisualStyleMargins GetMargins(
            string className,
            int part,
            int state,
            LibreVisualStyleMarginProperty property)
        {
            property.Should().Be(LibreVisualStyleMarginProperty.Content);
            return new LibreVisualStyleMargins(4, 5, 6, 7);
        }

        public Point GetPoint(
            string className,
            int part,
            int state,
            LibreVisualStylePointProperty property)
        {
            property.Should().Be(LibreVisualStylePointProperty.TextShadowOffset);
            return new Point(2, 3);
        }

        public bool IsBackgroundPartiallyTransparent(string className, int part, int state)
            => false;

        public Rectangle DrawEdge(
            Graphics graphics,
            string className,
            int part,
            int state,
            Rectangle bounds,
            LibreVisualStyleEdges edges,
            LibreVisualStyleEdgeStyle style,
            LibreVisualStyleEdgeEffects effects)
        {
            VisualStyleEdgeDrawCount++;
            return Rectangle.FromLTRB(
                bounds.Left + (edges.HasFlag(LibreVisualStyleEdges.Left) ? 1 : 0),
                bounds.Top + (edges.HasFlag(LibreVisualStyleEdges.Top) ? 1 : 0),
                bounds.Right - (edges.HasFlag(LibreVisualStyleEdges.Right) ? 1 : 0),
                bounds.Bottom - (edges.HasFlag(LibreVisualStyleEdges.Bottom) ? 1 : 0));
        }

        public void DrawText(
            Graphics graphics,
            string className,
            int part,
            int state,
            Rectangle bounds,
            string text,
            bool disabled,
            LibreVisualStyleTextFormat format)
        {
            VisualStyleTextDrawCount++;
            text.Should().Be("text");
            format.Should().Be(
                LibreVisualStyleTextFormat.HorizontalCenter | LibreVisualStyleTextFormat.VerticalCenter);
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
            TextDrawCount++;
            font.Should().NotBeNull();
            if (text == "portable")
            {
                bounds.Should().Be(new Rectangle(4, 5, 60, 18));
                foreColor.Should().Be(Color.Navy);
                backColor.Should().Be(Color.Beige);
                format.Should().Be(
                    LibreTextFormat.HorizontalCenter
                        | LibreTextFormat.VerticalCenter
                        | LibreTextFormat.SingleLine
                        | LibreTextFormat.NoPadding
                        | LibreTextFormat.TextBoxControl);
            }
            else if (text == "disabled")
            {
                bounds.Should().BeOneOf(new Rectangle(5, 6, 60, 18), new Rectangle(4, 5, 60, 18));
                backColor.Should().Be(Color.Empty);
                format.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
            }
            else
            {
                text.Should().BeOneOf("group", "link");
                bounds.Width.Should().BeGreaterThan(0);
                bounds.Height.Should().BeGreaterThan(0);
            }

            LastTextBounds = bounds;
            LastTextFormat = format;
            using var marker = new SolidBrush(foreColor);
            graphics.FillRectangle(marker, bounds.X, bounds.Y, 1, 1);
        }

        public Size MeasureText(
            Graphics? graphics,
            string text,
            Font? font,
            Size proposedSize,
            LibreTextFormat format)
        {
            TextMeasureCount++;
            font.Should().NotBeNull();
            LastTextFormat = format;
            LastMeasuredText = text;
            if (text == "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ")
            {
                graphics.Should().BeNull();
                proposedSize.Should().Be(new Size(int.MaxValue, int.MaxValue));
                format.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
                return new Size(416, font!.Height);
            }

            if (text == "0")
            {
                graphics.Should().BeNull();
                proposedSize.Should().Be(new Size(int.MaxValue, int.MaxValue));
                format.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
                return new Size(8, font!.Height);
            }

            if (text == "j^")
            {
                graphics.Should().BeNull();
                proposedSize.Should().Be(new Size(short.MaxValue, (int)(font!.Height * 1.25)));
                format.Should().Be(LibreTextFormat.SingleLine);
                return new Size(12, font.Height);
            }

            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out _))
            {
                graphics.Should().BeNull();
                proposedSize.Should().Be(new Size(int.MaxValue, int.MaxValue));
                format.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
                return new Size(72, font!.Height);
            }

            if (text is "button" or "check" or "radio")
            {
                graphics.Should().BeNull();
                format.Should().HaveFlag(LibreTextFormat.TextBoxControl);
                return new Size(text.Length * 7, font!.Height);
            }

            if (text is "wrapped DataGridView text" or "first" or "second" or " ")
            {
                int availableWidth = proposedSize.Width is > 0 and < int.MaxValue
                    ? proposedSize.Width
                    : text.Length * 7;
                int lineCount = Math.Max(1, (text.Length * 7 + availableWidth - 1) / availableWidth);
                return new Size(Math.Min(text.Length * 7, availableWidth), font!.Height * lineCount);
            }

            if (text is "group" or "link")
            {
                proposedSize.Width.Should().BeGreaterThan(0);
                return new Size(text.Length * 7, font!.Height);
            }

            if (graphics is null)
            {
                text.Should().Be("headless");
                proposedSize.Should().Be(new Size(70, 30));
                format.Should().Be(LibreTextFormat.SingleLine | LibreTextFormat.NoPadding);
                return new Size(31, 17);
            }

            text.Should().Be("managed");
            proposedSize.Should().Be(new Size(80, 40));
            format.Should().Be(LibreTextFormat.WordBreak | LibreTextFormat.LeftAndRightPadding);
            return new Size(37, 19);
        }

        private sealed class HeadlessWindow : ILibreWindow
        {
            private readonly HeadlessPlatform _platform;
            private readonly ILibreWindowEvents _events;
            private readonly LibreWindowCoordinateMode _coordinateMode;
            private readonly DrawingContext _retainedContext = new();
            private readonly Dictionary<LibreHandle, HeadlessRetainedLayer> _retainedLayers = [];
            private bool _disposed;
            private bool _paintQueued;
            private LibreRectangle _dirtyRectangle;
            private double _dpiScale;
            private double _framebufferScale;
            private LibreRectangle _nativeBounds;
            private string _title = string.Empty;
            private LibreWindowState _state;
            private bool _topMost;
            private LibreWindowBorder _border;
            private bool _showInTaskbar;
            private bool _canClose;
            private bool _canMinimize;
            private bool _canMaximize;
            private double _opacity = 1d;

            internal HeadlessWindow(
                HeadlessPlatform platform,
                in LibreWindowCreateOptions options,
                ILibreWindowEvents events)
            {
                _platform = platform;
                _events = events;
                _coordinateMode = options.CoordinateMode;
                _dpiScale = platform._initialDpiScale ?? options.InitialDpiScale;
                _framebufferScale = platform._initialFramebufferScale ?? options.InitialDpiScale;
                _nativeBounds = LibreWindowCoordinates.ToNative(
                    options.Bounds,
                    _coordinateMode,
                    _dpiScale,
                    _framebufferScale);
                _platform.LastWindowBounds = options.Bounds;
                _platform.LastNativeWindowBounds = _nativeBounds;
                Title = options.Title;
                _state = options.InitialState;
                _platform.LastWindowState = _state;
                TopMost = options.Options.HasFlag(LibreWindowOptions.TopMost);
                Border = !options.Options.HasFlag(LibreWindowOptions.Decorated)
                    ? LibreWindowBorder.Hidden
                    : options.Options.HasFlag(LibreWindowOptions.Resizable)
                        ? LibreWindowBorder.Resizable
                        : LibreWindowBorder.Fixed;
                ShowInTaskbar = options.ShowInTaskbar;
                CanClose = options.CanClose;
                CanMinimize = options.CanMinimize;
                CanMaximize = options.CanMaximize;
                Opacity = options.Opacity;
                SetSizeConstraints(options.MinimumSize, options.MaximumSize);
                Owner = options.Owner;
                Visible = options.Options.HasFlag(LibreWindowOptions.Visible);
                Handle = platform.Handles.Allocate(this, LibreHandleKind.Window);
            }

            public LibreHandle Handle { get; }

            public string Title
            {
                get => _title;
                set
                {
                    ArgumentNullException.ThrowIfNull(value);
                    _title = value;
                    _platform.LastWindowTitle = value;
                }
            }

            public LibreHandle Owner { get; set; }

            public LibreRectangle Bounds
            {
                get => LibreWindowCoordinates.ToManaged(
                    _nativeBounds,
                    _coordinateMode,
                    _dpiScale,
                    _framebufferScale);
                set
                {
                    _nativeBounds = LibreWindowCoordinates.ToNative(
                        value,
                        _coordinateMode,
                        _dpiScale,
                        _framebufferScale);
                    _platform.LastWindowBounds = value;
                    _platform.LastNativeWindowBounds = _nativeBounds;
                    _events.BoundsChanged(value);
                }
            }

            public LibreWindowState State
            {
                get => _state;
                set
                {
                    _state = value;
                    _platform.LastWindowState = value;
                    _events.StateChanged(value);
                }
            }

            public bool Visible { get; private set; }

            public bool Enabled { get; set; } = true;

            public bool TopMost
            {
                get => _topMost;
                set
                {
                    _topMost = value;
                    _platform.LastWindowTopMost = value;
                }
            }

            public LibreWindowBorder Border
            {
                get => _border;
                set
                {
                    _border = value;
                    _platform.LastWindowBorder = value;
                }
            }

            public bool ShowInTaskbar
            {
                get => _showInTaskbar;
                set
                {
                    _showInTaskbar = value;
                    _platform.LastWindowShowInTaskbar = value;
                }
            }

            public bool CanMinimize
            {
                get => _canMinimize;
                set
                {
                    _canMinimize = value;
                    _platform.LastWindowCanMinimize = value;
                }
            }

            public bool CanClose
            {
                get => _canClose;
                set
                {
                    _canClose = value;
                    _platform.LastWindowCanClose = value;
                }
            }

            public bool CanMaximize
            {
                get => _canMaximize;
                set
                {
                    _canMaximize = value;
                    _platform.LastWindowCanMaximize = value;
                }
            }

            public double Opacity
            {
                get => _opacity;
                set
                {
                    _opacity = value;
                    _platform.LastWindowOpacity = value;
                }
            }

            public void SetZOrder(LibreWindowZOrder value)
            {
                _platform.LastWindowZOrder = value;
                _platform.WindowZOrderChangeCount++;
            }

            public void SetCursor(LibreCursorShape shape)
            {
                _platform.LastCursorShape = shape;
                _platform.CursorChangeCount++;
            }

            public void SetSizeConstraints(LibreSize minimum, LibreSize maximum)
            {
                _platform.LastWindowMinimumSize = minimum;
                _platform.LastWindowMaximumSize = maximum;
            }

            public LibreWindowCoordinateMode CoordinateMode => _coordinateMode;

            public double FramebufferScale => _framebufferScale;

            public double DpiScale => _dpiScale;

            public void SetIcons(IReadOnlyList<LibreWindowIcon> icons)
                => _platform.LastWindowIcons = icons.ToArray();

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

            internal Graphics CreateGraphics(
                LibrePoint origin,
                LibreRectangle clipRectangle)
            {
                DrawingContext recording = new();
                int infrastructureCommandCount = 0;
                Graphics graphics = Graphics.FromProGpuDrawingContext(
                    recording,
                    new RectangleF(
                        clipRectangle.X,
                        clipRectangle.Y,
                        clipRectangle.Width,
                        clipRectangle.Height),
                    Matrix4x4.CreateTranslation(origin.X, origin.Y, 0f),
                    intention => FlushGraphics(recording, infrastructureCommandCount, intention),
                    () => CompleteGraphics(recording, infrastructureCommandCount));
                graphics.SetClip(new RectangleF(
                    clipRectangle.X - origin.X,
                    clipRectangle.Y - origin.Y,
                    clipRectangle.Width,
                    clipRectangle.Height));
                infrastructureCommandCount = checked(recording.Commands.Count + 1);
                return graphics;
            }

            private void FlushGraphics(
                DrawingContext recording,
                int infrastructureCommandCount,
                FlushIntention intention)
            {
                _platform.CreateGraphicsFlushCount++;
                _platform.LastCreateGraphicsFlushIntention = intention;
                CompleteGraphics(recording, infrastructureCommandCount);
            }

            private void CompleteGraphics(
                DrawingContext recording,
                int infrastructureCommandCount)
            {
                try
                {
                    if (_disposed || recording.Commands.Count <= infrastructureCommandCount)
                    {
                        return;
                    }

                    _retainedContext.Append(recording);
                    _platform.CreateGraphicsCommitCount++;
                    _platform.SawCreateGraphicsTranslatedFill = ContainsSolidFill(
                        recording,
                        new RectangleF(14, 21, 10, 8),
                        Color.MediumPurple);
                }
                finally
                {
                    recording.Clear();
                }
            }

            internal void RequestPaint(LibreRectangle dirtyRectangle)
            {
                if (_paintQueued)
                {
                    _dirtyRectangle = Union(_dirtyRectangle, dirtyRectangle);
                }
                else
                {
                    _paintQueued = true;
                    _dirtyRectangle = dirtyRectangle;
                    _platform.Post(PresentPendingPaint);
                }
            }

            public void PresentPendingPaint()
            {
                if (_disposed || !_paintQueued)
                {
                    return;
                }

                LibreRectangle dirtyRectangle = _dirtyRectangle;
                _paintQueued = false;
                _dirtyRectangle = default;
                LibreRectangle surfaceBounds = new(0, 0, Bounds.Width, Bounds.Height);
                HeadlessRetainedPaintFrame frame = new(
                    _platform,
                    _retainedContext,
                    _retainedLayers,
                    surfaceBounds,
                    dirtyRectangle);
                try
                {
                    _events.PaintRequested(frame);
                }
                finally
                {
                    frame.Complete();
                }

                _platform.LastPaintCommandCount = _retainedContext.Commands.Count;
                _platform.SawFormPaintFill = ContainsSolidFill(
                    _retainedContext,
                    new RectangleF(4, 5, 24, 16),
                    Color.CornflowerBlue);
                _platform.SawTranslatedChildPaintFill = ContainsSolidFill(
                    _retainedContext,
                    new RectangleF(14, 21, 10, 8),
                    Color.OrangeRed);
            }

            internal void SendInput(in LibreInputEvent inputEvent)
            {
                if (Enabled || inputEvent.Kind == LibreInputEventKind.FocusLost)
                {
                    _events.Input(inputEvent);
                }
            }

            internal void SetPresentationScales(double dpiScale, double framebufferScale)
            {
                LibreRectangle oldManagedBounds = Bounds;
                double oldDpiScale = _dpiScale;
                _dpiScale = dpiScale;
                _framebufferScale = framebufferScale;
                int desiredWidth = _coordinateMode == LibreWindowCoordinateMode.DevicePixels
                    ? ScaleForDpi(oldManagedBounds.Width, dpiScale, oldDpiScale)
                    : oldManagedBounds.Width;
                int desiredHeight = _coordinateMode == LibreWindowCoordinateMode.DevicePixels
                    ? ScaleForDpi(oldManagedBounds.Height, dpiScale, oldDpiScale)
                    : oldManagedBounds.Height;
                if (_coordinateMode == LibreWindowCoordinateMode.Logical)
                {
                    _nativeBounds = LibreWindowCoordinates.ToNative(
                        oldManagedBounds,
                        _coordinateMode,
                        _dpiScale,
                        _framebufferScale);
                }
                else
                {
                    LibreRectangle nativeSize = LibreWindowCoordinates.ToNative(
                        new LibreRectangle(0, 0, desiredWidth, desiredHeight),
                        _coordinateMode,
                        _dpiScale,
                        _framebufferScale);
                    _nativeBounds = new LibreRectangle(
                        _nativeBounds.X,
                        _nativeBounds.Y,
                        nativeSize.Width,
                        nativeSize.Height);
                }

                _platform.LastNativeWindowBounds = _nativeBounds;
                _events.BoundsChanged(Bounds);
                _events.PresentationScaleChanged(dpiScale);
            }

            private static int ScaleForDpi(int value, double newDpiScale, double oldDpiScale)
                => checked((int)Math.Round(value * newDpiScale / oldDpiScale, MidpointRounding.AwayFromZero));

            private static LibreRectangle Union(LibreRectangle left, LibreRectangle right)
            {
                int x = Math.Min(left.X, right.X);
                int y = Math.Min(left.Y, right.Y);
                int rightEdge = Math.Max(
                    checked(left.X + left.Width),
                    checked(right.X + right.Width));
                int bottomEdge = Math.Max(
                    checked(left.Y + left.Height),
                    checked(right.Y + right.Height));
                return new LibreRectangle(x, y, checked(rightEdge - x), checked(bottomEdge - y));
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
                _retainedContext.Clear();
                foreach (HeadlessRetainedLayer layer in _retainedLayers.Values)
                {
                    layer.Context.Clear();
                }

                _retainedLayers.Clear();
                _platform.Handles.Release(Handle);
                _events.Closed();
            }

            private sealed class HeadlessRetainedPaintFrame : ILibreRetainedPaintFrame
            {
                private readonly HeadlessPlatform _platform;
                private readonly DrawingContext _output;
                private readonly Dictionary<LibreHandle, HeadlessRetainedLayer> _layers;
                private readonly DrawingContext _fallback = new();
                private readonly List<HeadlessRetainedLayer> _ordered = [];
                private readonly HashSet<LibreHandle> _visited = [];
                private int _repaintCount;
                private bool _completed;

                internal HeadlessRetainedPaintFrame(
                    HeadlessPlatform platform,
                    DrawingContext output,
                    Dictionary<LibreHandle, HeadlessRetainedLayer> layers,
                    LibreRectangle surfaceBounds,
                    LibreRectangle dirtyRectangle)
                {
                    _platform = platform;
                    _output = output;
                    _layers = layers;
                    SurfaceBounds = surfaceBounds;
                    DirtyRectangle = dirtyRectangle;
                    Graphics = Graphics.FromProGpuDrawingContext(
                        _fallback,
                        new RectangleF(0, 0, surfaceBounds.Width, surfaceBounds.Height));
                }

                public Graphics Graphics { get; }

                public LibreRectangle SurfaceBounds { get; }

                public LibreRectangle DirtyRectangle { get; }

                public ILibrePaintLayer OpenLayer(
                    LibreHandle target,
                    LibreRectangle bounds,
                    LibreRectangle clipRectangle)
                {
                    _visited.Add(target).Should().BeTrue();
                    bool isNew = !_layers.TryGetValue(target, out HeadlessRetainedLayer? layer);
                    if (isNew)
                    {
                        layer = new HeadlessRetainedLayer();
                        _layers.Add(target, layer);
                    }

                    layer!.Bounds = bounds;
                    _ordered.Add(layer);
                    if (!isNew && !Intersects(bounds, DirtyRectangle))
                    {
                        return EmptyPaintLayer.Instance;
                    }

                    _repaintCount++;
                    layer.Context.Clear();
                    Graphics graphics = Graphics.FromProGpuDrawingContext(
                        layer.Context,
                        new RectangleF(0, 0, bounds.Width, bounds.Height));
                    graphics.SetClip(new RectangleF(
                        clipRectangle.X - bounds.X,
                        clipRectangle.Y - bounds.Y,
                        clipRectangle.Width,
                        clipRectangle.Height));
                    return new RecordingPaintLayer(graphics);
                }

                internal void Complete()
                {
                    if (_completed)
                    {
                        return;
                    }

                    _completed = true;
                    Graphics.Dispose();
                    foreach ((LibreHandle target, HeadlessRetainedLayer layer) in _layers.ToArray())
                    {
                        if (_visited.Contains(target))
                        {
                            continue;
                        }

                        layer.Context.Clear();
                        _layers.Remove(target);
                    }

                    _output.Clear();
                    _output.Append(_fallback);
                    foreach (HeadlessRetainedLayer layer in _ordered)
                    {
                        _output.Append(layer.Context, new Vector2(layer.Bounds.X, layer.Bounds.Y));
                    }

                    _fallback.Clear();
                    _platform.LastRetainedLayerCount = _layers.Count;
                    _platform.LastRetainedLayerRepaintCount = _repaintCount;
                }

                private static bool Intersects(LibreRectangle left, LibreRectangle right)
                    => left.Width > 0
                        && left.Height > 0
                        && right.Width > 0
                        && right.Height > 0
                        && left.X < right.Right
                        && right.X < left.Right
                        && left.Y < right.Bottom
                        && right.Y < left.Bottom;

                private sealed class RecordingPaintLayer(Graphics graphics) : ILibrePaintLayer
                {
                    public Graphics? Graphics { get; private set; } = graphics;

                    public void Dispose()
                    {
                        Graphics?.Dispose();
                        Graphics = null;
                    }
                }

                private sealed class EmptyPaintLayer : ILibrePaintLayer
                {
                    internal static EmptyPaintLayer Instance { get; } = new();

                    public Graphics? Graphics => null;

                    public void Dispose()
                    {
                    }
                }
            }

            private sealed class HeadlessRetainedLayer
            {
                internal DrawingContext Context { get; } = new();

                internal LibreRectangle Bounds { get; set; }
            }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
