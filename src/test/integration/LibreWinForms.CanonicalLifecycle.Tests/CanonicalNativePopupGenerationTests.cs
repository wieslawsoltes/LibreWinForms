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
    [Fact]
    public void RetiredNativePopupCallbacksCannotMutateOrCloseItsRecreatedSource()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        owner.Show();
        using ContextMenuStrip menu = new() { AutoClose = false };
        menu.Items.Add("Open");
        menu.Show(owner, Point.Empty);
        ILibreWindowEvents retired = platform.GetWindowEvents(menu);
        LibreHandle oldHandle = platform.GetWindowHandle(menu);
        owner.Hide();
        owner.Show();
        menu.Show(owner, Point.Empty);
        LibreHandle currentHandle = platform.GetWindowHandle(menu);
        currentHandle.Should().NotBe(oldHandle);
        Rectangle bounds = menu.Bounds;
        Point pointer = Control.MousePosition;
        int dpi = menu.DeviceDpi;
        int closed = 0;
        menu.Closed += (_, _) => closed++;
        GenerationPaintFrame frame = new();

        retired.BoundsChanged(new LibreRectangle(901, 902, 903, 904));
        retired.StateChanged(LibreWindowState.Minimized);
        retired.PresentationScaleChanged(2);
        retired.Input(GenerationPointerMove(700, 800));
        retired.PaintRequested(frame);
        retired.Closing().Should().BeTrue("the retired native surface may finish closing independently");
        retired.Closed();

        menu.Bounds.Should().Be(bounds);
        menu.DeviceDpi.Should().Be(dpi);
        Control.MousePosition.Should().Be(pointer);
        frame.SurfaceReads.Should().Be(0);
        menu.Visible.Should().BeTrue();
        platform.IsWindowVisible(menu).Should().BeTrue();
        platform.GetWindowHandle(menu).Should().Be(currentHandle);
        closed.Should().Be(0);

        ILibreWindowEvents current = platform.GetWindowEvents(menu);
        current.Input(GenerationPointerMove(4, 5));
        Control.MousePosition.Should().Be(menu.PointToScreen(new Point(4, 5)));
        current.PaintRequested(frame);
        frame.SurfaceReads.Should().Be(1);
        owner.Hide();
        closed.Should().Be(1);
    }

    [Fact]
    public void RetiredNativeFormCallbacksCannotChangeTheCurrentStateOrOwnedPopup()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using RecreatingForm owner = new() { ShowIcon = false };
        owner.Show();
        ILibreWindowEvents retired = platform.GetWindowEvents(owner);
        owner.RecreatePortableHandle();
        using ContextMenuStrip menu = new() { AutoClose = false };
        menu.Items.Add("Open");
        menu.Show(owner, Point.Empty);
        Rectangle bounds = owner.Bounds;
        LibreHandle currentHandle = platform.GetWindowHandle(owner);

        retired.StateChanged(LibreWindowState.Minimized);
        retired.BoundsChanged(new LibreRectangle(901, 902, 903, 904));
        retired.Closing().Should().BeTrue();
        retired.Closed();

        owner.WindowState.Should().Be(FormWindowState.Normal);
        owner.Bounds.Should().Be(bounds);
        owner.IsDisposed.Should().BeFalse();
        platform.GetWindowHandle(owner).Should().Be(currentHandle);
        menu.Visible.Should().BeTrue();
        menu.IsHandleCreated.Should().BeTrue();

        ILibreWindowEvents current = platform.GetWindowEvents(owner);
        current.BoundsChanged(new LibreRectangle(30, 40, 400, 300));
        owner.Bounds.Should().Be(new Rectangle(30, 40, 400, 300));
        current.StateChanged(LibreWindowState.Minimized);
        owner.WindowState.Should().Be(FormWindowState.Minimized);
        menu.IsHandleCreated.Should().BeFalse();
        current.StateChanged(LibreWindowState.Normal);
    }

    [Fact]
    public void NativeConstructionCallbacksAreCurrentBeforeTheHandleIsPublished()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        int callbacks = 0;
        platform.WindowCreating = events =>
        {
            callbacks++;
            owner.IsHandleCreated.Should().BeFalse();
            events.BoundsChanged(new LibreRectangle(30, 40, 400, 300));
            owner.Bounds.Should().Be(new Rectangle(30, 40, 400, 300));
            events.StateChanged(LibreWindowState.Maximized);
            owner.WindowState.Should().Be(FormWindowState.Maximized);
            events.StateChanged(LibreWindowState.Normal);
        };

        try
        {
            owner.Show();
        }
        finally
        {
            platform.WindowCreating = null;
        }

        callbacks.Should().Be(1);
        owner.IsHandleCreated.Should().BeTrue();
        platform.GetWindowEvents(owner).BoundsChanged(new LibreRectangle(40, 50, 420, 320));
        owner.Bounds.Should().Be(new Rectangle(40, 50, 420, 320));
    }

    [Fact]
    public void NativeClosedDuringCreationRejectsTheReturnedSurfaceAndAllowsAFreshGeneration()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        using Form owner = new() { ShowIcon = false };
        ILibreWindowEvents? retired = null;
        platform.WindowCreating = events =>
        {
            retired = events;
            events.Closed();
        };
        try
        {
            Action show = owner.Show;
            show.Should().Throw<InvalidOperationException>();
            owner.IsHandleCreated.Should().BeFalse();
        }
        finally
        {
            platform.WindowCreating = null;
        }

        owner.Show();
        LibreHandle current = platform.GetWindowHandle(owner);
        retired.Should().NotBeNull();
        retired!.Closed();
        owner.IsHandleCreated.Should().BeTrue();
        platform.GetWindowHandle(owner).Should().Be(current);
        platform.IsWindowVisible(owner).Should().BeTrue();
    }

    private static LibreInputEvent GenerationPointerMove(int x, int y)
        => new(LibreInputEventKind.PointerMove, 1, LibreInputModifiers.None,
            LibreKey.Unknown, null, new LibrePoint(x, y), default, LibrePointerButton.None);

    private sealed class GenerationPaintFrame : ILibrePaintFrame
    {
        internal int SurfaceReads { get; private set; }

        public Graphics Graphics => throw new InvalidOperationException("An empty surface has no drawing work.");

        public LibreRectangle SurfaceBounds
        {
            get
            {
                SurfaceReads++;
                return default;
            }
        }

        public LibreRectangle DirtyRectangle => default;
    }
}
