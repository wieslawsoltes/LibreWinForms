// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Theory]
    [InlineData(FormStartPosition.Manual, -120, 45)]
    [InlineData(FormStartPosition.CenterScreen, 680, 395)]
    [InlineData(FormStartPosition.WindowsDefaultLocation, 100, 100)]
    public void InitialNativePositionIsPublishedBeforeHandleCreated(FormStartPosition startPosition, int x, int y)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new()
        {
            ShowIcon = false,
            AutoScaleMode = AutoScaleMode.None,
            StartPosition = startPosition,
            Bounds = new Rectangle(-120, 45, 560, 250),
        };
        List<string> notifications = [];
        form.LocationChanged += (_, _) => notifications.Add("location");
        form.HandleCreated += (_, _) =>
        {
            notifications.Add("handle");
            form.Location.Should().Be(new Point(x, y));
            form.PointToScreen(Point.Empty).Should().Be(new Point(x, y));
        };

        _ = form.Handle;

        form.Bounds.Should().Be(new Rectangle(x, y, 560, 250));
        platform.LastWindowBounds.Should().Be(new LibreRectangle(x, y, 560, 250));
        notifications.Should().Equal(startPosition == FormStartPosition.Manual
            ? new[] { "handle" }
            : new[] { "location", "handle" });
    }

    [Fact]
    public void InitialProviderPositionWithoutAMoveEventOwnsNestedScreenConversions()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.InitialWindowLocation = new LibrePoint(-730, 210);
        using Form form = new() { ShowIcon = false, AutoScaleMode = AutoScaleMode.None, Size = new Size(560, 250) };
        using Control parent = new() { Bounds = new Rectangle(20, 30, 200, 100) };
        using Control child = new() { Bounds = new Rectangle(7, 9, 80, 20) };
        form.Controls.Add(parent);
        parent.Controls.Add(child);
        int moveEventsDuringCreation = 0;
        platform.WindowCreating = _ => form.Location.Should().Be(Point.Empty);
        form.LocationChanged += (_, _) => moveEventsDuringCreation++;

        _ = form.Handle;
        _ = child.Handle;

        form.Location.Should().Be(new Point(-730, 210));
        platform.LastWindowOptions.Bounds.X.Should().Be(100, "the source request is not the provider's returned position");
        child.PointToScreen(new Point(3, 4)).Should().Be(new Point(-700, 253));
        child.PointToClient(new Point(-700, 253)).Should().Be(new Point(3, 4));
        parent.Location.Should().Be(new Point(20, 30));
        child.Location.Should().Be(new Point(7, 9));
        moveEventsDuringCreation.Should().Be(1);
    }

    [Fact]
    public void RecreatedFormReadsItsNewProviderPositionAndRejectsRetiredMoves()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using RecreatingForm form = new() { ShowIcon = false, AutoScaleMode = AutoScaleMode.None };
        _ = form.Handle;
        ILibreWindowEvents retired = platform.GetWindowEvents(form);
        platform.InitialWindowLocation = new LibrePoint(530, -240);

        form.RecreatePortableHandle();

        form.Location.Should().Be(new Point(530, -240));
        retired.BoundsChanged(new LibreRectangle(1, 2, 300, 200));
        form.Location.Should().Be(new Point(530, -240));
        platform.GetWindowEvents(form).BoundsChanged(new LibreRectangle(540, -230, 400, 300));
        form.PointToScreen(new Point(3, 4)).Should().Be(new Point(543, -226));
        form.Size.Should().Be(new Size(400, 300));
    }

    [Fact]
    public void InitialLocationCallbackMayReplaceThePositionWithoutBeingOverwritten()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.InitialWindowLocation = new LibrePoint(600, 400);
        using Form form = new() { ShowIcon = false, AutoScaleMode = AutoScaleMode.None };
        form.LocationChanged += (_, _) =>
        {
            if (form.Location == new Point(600, 400))
                form.Location = new Point(300, 200);
        };

        _ = form.Handle;

        form.Location.Should().Be(new Point(300, 200));
        platform.LastWindowBounds.X.Should().Be(300);
        platform.LastWindowBounds.Y.Should().Be(200);
    }

    [Fact]
    public void InitialLocationCallbackPreservesCanonicalDisposalDuringCreationRejection()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false, StartPosition = FormStartPosition.CenterScreen };
        int created = 0;
        LibreHandle retained = default;
        form.HandleCreated += (_, _) => created++;
        EventHandler disposeDuringCreation = (_, _) =>
        {
            retained = platform.GetWindowHandle(form);
            form.Dispose();
        };
        form.LocationChanged += disposeDuringCreation;

        Action create = () => _ = form.Handle;

        create.Should().Throw<InvalidOperationException>();
        form.IsDisposed.Should().BeFalse();
        form.IsHandleCreated.Should().BeTrue();
        retained.IsNull.Should().BeFalse();
        platform.Handles.TryGet(retained, out ILibreWindow? _).Should().BeTrue();
        created.Should().Be(0);

        form.LocationChanged -= disposeDuringCreation;
        form.Dispose();
        form.IsDisposed.Should().BeTrue();
        form.IsHandleCreated.Should().BeFalse();
        platform.Handles.TryGet(retained, out ILibreWindow? _).Should().BeFalse();
    }

    [Fact]
    public void InitialLocationCallbackNativeReleaseDoesNotPublishHandleCreatedForAReleasedWindow()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false, StartPosition = FormStartPosition.CenterScreen };
        int created = 0;
        LibreHandle released = default;
        form.HandleCreated += (_, _) => created++;
        form.LocationChanged += (_, _) =>
        {
            released = platform.GetWindowHandle(form);
            NativeWindow.FromHandle(form.Handle)!.DestroyHandle();
        };

        _ = form.Handle;

        form.IsDisposed.Should().BeFalse();
        form.IsHandleCreated.Should().BeFalse();
        released.IsNull.Should().BeFalse();
        platform.Handles.TryGet(released, out ILibreWindow? _).Should().BeFalse();
        created.Should().Be(0);
    }

    [Fact]
    public void InitialLocationCallbackReplacementDoesNotPublishTheOldHandleCreated()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using InitialPositionForm form = new() { ShowIcon = false, StartPosition = FormStartPosition.CenterScreen };
        int created = 0;
        LibreHandle retired = default;
        LibreHandle replacement = default;
        form.HandleCreated += (_, _) => created++;
        form.LocationChanged += (_, _) =>
        {
            retired = platform.GetWindowHandle(form);
            NativeWindow window = NativeWindow.FromHandle(form.Handle)!;
            window.DestroyHandle();
            window.CreateHandle(form.WindowCreationParameters);
            replacement = platform.GetWindowHandle(form);
        };

        _ = form.Handle;

        replacement.Should().NotBe(retired);
        platform.GetWindowHandle(form).Should().Be(replacement);
        platform.Handles.TryGet(retired, out ILibreWindow? _).Should().BeFalse();
        platform.Handles.TryGet(replacement, out ILibreWindow? _).Should().BeTrue();
        created.Should().Be(0, "the outer source creation must not notify for a replacement native generation");
    }

    [Fact]
    public void InitialPopupPositionIsPublishedAndLaterExplicitShowLocationRemainsAuthoritative()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        platform.InitialWindowLocation = new LibrePoint(630, 410);
        using ContextMenuStrip menu = new();
        menu.Items.Add("Open");
        int created = 0;
        menu.HandleCreated += (_, _) =>
        {
            menu.Location.Should().Be(new Point(630, 410));
            menu.PointToScreen(Point.Empty).Should().Be(new Point(630, 410));
            created++;
        };

        _ = menu.Handle;
        LibreHandle initial = platform.GetWindowHandle(menu);
        menu.Show(owner, new Point(20, 30));

        created.Should().Be(1);
        platform.GetWindowHandle(menu).Should().Be(initial);
        menu.Location.Should().Be(owner.PointToScreen(new Point(20, 30)));
        platform.LastWindowBounds.X.Should().Be(menu.Left);
        platform.LastWindowBounds.Y.Should().Be(menu.Top);
    }

    [Fact]
    public void InitialLocationCallbackFailureIsNotSuppressed()
    {
        UseHeadlessPlatform(autoCloseWindows: false);
        using Form form = new() { ShowIcon = false, StartPosition = FormStartPosition.CenterScreen };
        InvalidOperationException expected = new("initial location callback");
        int created = 0;
        form.HandleCreated += (_, _) => created++;
        form.LocationChanged += (_, _) => throw expected;

        Action create = () => _ = form.Handle;

        create.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
        form.Location.Should().NotBe(Point.Empty);
        created.Should().Be(0);
    }

    [Fact]
    public void InitialCenterScreenUsesSystemAwareWindowsCoordinatesWithoutASecondScale()
    {
        if (!RunDpiCaseInNewProcess())
            VerifyInitialSystemAwarePosition(1);
    }

    [Fact]
    public void InitialCenterScreenUsesSystemAwareCocoaCoordinatesWithoutASecondScale()
    {
        if (!RunDpiCaseInNewProcess())
            VerifyInitialSystemAwarePosition(2);
    }

    [Fact]
    public void InitialPositionPublicationPreservesCanonicalPerMonitorAutoScaling()
    {
        if (RunDpiCaseInNewProcess())
            return;

        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        platform.SetInitialPresentationScales(2, 1);
        platform.InitialWindowLocation = new LibrePoint(700, 300);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2).Should().BeTrue();
        using Form form = new() { ShowIcon = false };
        // Keep design dimensions and bounds in one generated-designer transaction.
        form.SuspendLayout();
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = new Rectangle(10, 20, 400, 300);
        using Control child = new() { Bounds = new Rectangle(20, 30, 100, 40) };
        form.Controls.Add(child);
        form.ResumeLayout(true);

        RunSystemDpiForm(platform, form, () =>
        {
            form.DeviceDpi.Should().Be(192);
            form.Bounds.Should().Be(new Rectangle(700, 300, 800, 600));
            child.Bounds.Should().Be(new Rectangle(40, 60, 200, 80));
            child.PointToScreen(Point.Empty).Should().Be(new Point(740, 360));
            platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(700, 300, 800, 600));
        });
    }

    private static void VerifyInitialSystemAwarePosition(double nativeCoordinateScale)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, nativeCoordinateScale));
        platform.SetInitialPresentationScales(2, nativeCoordinateScale);
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        using Form form = new()
        {
            ShowIcon = false,
            AutoScaleMode = AutoScaleMode.None,
            StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(400, 200),
        };

        _ = form.Handle;

        form.Bounds.Should().Be(new Rectangle(1000, 650, 400, 200));
        form.DeviceDpi.Should().Be(192);
        platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(
            (int)(1000 / nativeCoordinateScale), (int)(650 / nativeCoordinateScale),
            (int)(400 / nativeCoordinateScale), (int)(200 / nativeCoordinateScale)));
    }

    private sealed class InitialPositionForm : Form
    {
        internal CreateParams WindowCreationParameters => CreateParams;
    }
}
