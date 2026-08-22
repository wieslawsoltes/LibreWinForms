// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
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
        private double? _initialDpiScale;
        private double? _initialFramebufferScale;
        private HeadlessWindow? _lastWindow;
        private IReadOnlyList<LibreMonitor> _monitors = CreateDefaultMonitorInventory();

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
            _initialDpiScale = null;
            _initialFramebufferScale = null;
            _lastWindow = null;
            _monitors = CreateDefaultMonitorInventory();
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
            SawCreateGraphicsTranslatedFill = false;
            LastActivatedWindow = default;
            LastWindowTitle = string.Empty;
            LastWindowState = LibreWindowState.Normal;
            LastWindowTopMost = false;
            LastWindowIcons = [];
        }

        internal ManagedLibreHandleRegistry Handles { get; }

        internal LibrePlatformServices Services { get; }

        internal int WindowsCreated { get; private set; }

        internal LibreRectangle LastWindowBounds { get; private set; }

        internal LibreRectangle LastNativeWindowBounds { get; private set; }

        internal LibreRectangle LastDirtyRectangle { get; private set; }

        internal int PresentCount { get; private set; }

        internal int PresentationInvalidationCount { get; private set; }

        internal double LastPresentationScale { get; private set; } = 1.0;

        internal LibreWindowCoordinateMode LastCoordinateMode { get; private set; }

        internal int LastPaintCommandCount { get; private set; }

        internal int LastRetainedLayerCount { get; private set; }

        internal int LastRetainedLayerRepaintCount { get; private set; }

        internal bool SawFormPaintFill { get; private set; }

        internal bool SawTranslatedChildPaintFill { get; private set; }

        internal int CreateGraphicsCommitCount { get; private set; }

        internal bool SawCreateGraphicsTranslatedFill { get; private set; }

        internal LibreHandle LastActivatedWindow { get; private set; }

        internal string LastWindowTitle { get; private set; } = string.Empty;

        internal LibreWindowState LastWindowState { get; private set; }

        internal bool LastWindowTopMost { get; private set; }

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
                    () => CompleteGraphics(recording, infrastructureCommandCount));
                graphics.SetClip(new RectangleF(
                    clipRectangle.X - origin.X,
                    clipRectangle.Y - origin.Y,
                    clipRectangle.Width,
                    clipRectangle.Height));
                infrastructureCommandCount = checked(recording.Commands.Count + 1);
                return graphics;
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
