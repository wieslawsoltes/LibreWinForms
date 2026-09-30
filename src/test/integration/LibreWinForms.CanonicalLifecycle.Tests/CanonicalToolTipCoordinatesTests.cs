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
    public void PortableToolTipCoordinateModeWindowsDevicePixels()
    {
        if (!RunDpiCaseInNewProcess())
            VerifyToolTipCoordinatePolicy(HighDpiMode.SystemAware, 1d, 100);
    }

    [Fact]
    public void PortableToolTipCoordinateModeNegativeDesktopOrigin()
    {
        if (!RunDpiCaseInNewProcess())
            VerifyToolTipCoordinatePolicy(HighDpiMode.SystemAware, 1d, -1200);
    }

    [Fact]
    public void PortableToolTipCoordinateModeCocoaDesktopPoints()
    {
        if (!RunDpiCaseInNewProcess())
            VerifyToolTipCoordinatePolicy(HighDpiMode.SystemAware, 2d, 100);
    }

    [Fact]
    public void PortableToolTipCoordinateModeExplicitUnaware()
    {
        if (!RunDpiCaseInNewProcess())
            VerifyToolTipCoordinatePolicy(HighDpiMode.DpiUnaware, 1d, 100);
    }

    private static void VerifyToolTipCoordinatePolicy(HighDpiMode mode, double nativeScale, int formX)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        LibreRectangle desktop = new(
            (int)(-2400 / nativeScale), (int)(-1600 / nativeScale),
            (int)(4800 / nativeScale), (int)(3200 / nativeScale));
        platform.SetMonitors(new LibreMonitor("primary", desktop, desktop, 2d, true)
        {
            NativeCoordinateScale = nativeScale
        });
        platform.SetInitialPresentationScales(2d, nativeScale);
        Application.SetHighDpiMode(mode).Should().BeTrue();
        using Form form = new()
        {
            AutoScaleMode = AutoScaleMode.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(formX, 200, 500, 400)
        };
        using Button target = new() { Bounds = new Rectangle(20, 30, 200, 80) };
        form.Controls.Add(target);
        using ToolTip tip = new() { InitialDelay = 35, AutoPopDelay = 80, OwnerDraw = true };
        tip.Popup += (_, args) => args.ToolTipSize = new Size(146, 31);
        tip.Draw += (_, args) => args.Graphics.FillRectangle(Brushes.Red, args.Bounds);
        tip.SetToolTip(target, "Tooltip coordinates");
        LibreWindowCoordinateMode expectedMode = mode == HighDpiMode.SystemAware
            ? LibreWindowCoordinateMode.DevicePixels : LibreWindowCoordinateMode.Logical;
        double sourceToNative = expectedMode == LibreWindowCoordinateMode.DevicePixels
            ? 1d / nativeScale : 2d / nativeScale;

        RunSystemDpiForm(platform, form, () =>
        {
            platform.LastCoordinateMode.Should().Be(expectedMode);
            form.Location.Should().Be(new Point(formX, 200));
            form.DeviceDpi.Should().Be(mode == HighDpiMode.SystemAware ? 192 : 96);
            tip.Show("Explicit coordinates", target, new Point(7, 9));
            AssertRequest(new LibreRectangle(formX + 27, 239, 146, 31));
            tip.Hide(target);

            platform.SendInput(LibreInputEventKind.PointerMove, position: new LibrePoint(25, 36));
            Cursor.Position.Should().Be(new Point(formX + 25, 236));
            platform.HasActiveTimer.Should().BeTrue();
            platform.FireTimer();
            AssertRequest(new LibreRectangle(formX + 41, 256, 146, 31));
            tip.Hide(target);
            platform.Popups.Should().BeEmpty();
        });

        void AssertRequest(LibreRectangle expected)
        {
            platform.Popups.Should().ContainSingle();
            LibrePopupSurfaceRequest request = platform.Popups.Values.Single().Request;
            request.Owner.Should().Be(platform.GetWindowHandle(form));
            request.CoordinateMode.Should().Be(expectedMode);
            request.ScreenBounds.Should().Be(expected);
            request.DpiScale.Should().Be(mode == HighDpiMode.SystemAware ? 2d : 1d);
            request.InputTransparent.Should().BeTrue();
            LibreWindowCoordinates.ToNative(request.ScreenBounds, request.CoordinateMode, 2d, nativeScale)
                .Should().Be(new LibreRectangle(
                    Round(expected.X * sourceToNative), Round(expected.Y * sourceToNative),
                    Round(146 * sourceToNative), Round(31 * sourceToNative)));
        }

        static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
