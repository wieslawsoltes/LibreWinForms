// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using FluentAssertions;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public partial class CanonicalLifecycleTests
{
    [Fact]
    public void DpiChildResultIsIndependentOfForcedTerminalColors()
    {
        if (!RunDpiCaseInNewProcess(forceTerminalColors: true))
        {
            VerifySystemAwareDesktopUnits(1);
        }
    }

    [Fact]
    public void SystemAwareUsesDeclaredWindowsDesktopUnitsForCenteringAndNativeBounds()
    {
        if (!RunDpiCaseInNewProcess())
        {
            VerifySystemAwareDesktopUnits(1);
        }
    }

    [Fact]
    public void SystemAwareUsesDeclaredCocoaDesktopUnitsForCenteringAndNativeBounds()
    {
        if (!RunDpiCaseInNewProcess())
        {
            VerifySystemAwareDesktopUnits(2);
        }
    }

    private static void VerifySystemAwareDesktopUnits(double nativeCoordinateScale)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, nativeCoordinateScale));
        platform.SetInitialPresentationScales(2, nativeCoordinateScale);
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        using CenteringForm form = new()
        {
            AutoScaleMode = AutoScaleMode.None,
            StartPosition = FormStartPosition.Manual,
            Size = new Size(400, 200),
        };

        RunSystemDpiForm(platform, form, () =>
        {
            form.DeviceDpi.Should().Be(192);
            Screen.PrimaryScreen!.Bounds.Should().Be(new Rectangle(0, 0, 2400, 1600));
            Screen.PrimaryScreen.WorkingArea.Should().Be(new Rectangle(0, 0, 2400, 1500));
            form.CenterOnScreen();
            form.Location.Should().Be(new Point(1000, 650));
            platform.LastCoordinateMode.Should().Be(LibreWindowCoordinateMode.DevicePixels);
            platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(
                (int)(1000 / nativeCoordinateScale), (int)(650 / nativeCoordinateScale),
                (int)(400 / nativeCoordinateScale), (int)(200 / nativeCoordinateScale)));
            form.PointToScreen(new Point(20, 30)).Should().Be(new Point(1020, 680));
            form.PointToClient(new Point(1020, 680)).Should().Be(new Point(20, 30));
        });
    }

    [Fact]
    public void SystemAwareUsesCanonicalDpiAutoScaleWithoutNativeDoubleScaling()
    {
        if (RunDpiCaseInNewProcess())
        {
            return;
        }

        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        platform.SetInitialPresentationScales(2, 1);
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        using Form form = new();
        // Match generated designer initialization: defer layout until the source
        // design dimensions and bounds have both been assigned.
        form.SuspendLayout();
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = new Rectangle(10, 20, 560, 250);
        form.ResumeLayout(true);

        RunSystemDpiForm(platform, form, () =>
        {
            form.DeviceDpi.Should().Be(192);
            form.CurrentAutoScaleDimensions.Should().Be(new SizeF(192, 192));
            form.Size.Should().Be(new Size(1120, 500));
            platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(
                form.Left, form.Top, form.Width, form.Height));
        });
    }

    [Fact]
    public void SystemAwareCapturesPrimaryDpiAndDoesNotFollowLaterMonitorChanges()
    {
        if (RunDpiCaseInNewProcess())
        {
            return;
        }

        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        LibreMonitor primary = SystemDpiMonitor(2, 1);
        platform.SetMonitors(primary with { Id = "secondary", IsPrimary = false, DpiScale = 3 }, primary);
        platform.SetInitialPresentationScales(3, 1);
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        platform.SetMonitors(primary with { DpiScale = 4 });
        using Form form = new()
        {
            AutoScaleMode = AutoScaleMode.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(30, 40, 400, 200),
        };
        int dpiChanges = 0;
        form.DpiChanged += (_, _) => dpiChanges++;

        RunSystemDpiForm(platform, form, () =>
        {
            form.DeviceDpi.Should().Be(192, "the primary DPI was captured before the inventory changed");
            platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(30, 40, 400, 200));
            platform.SetPresentationScales(4, 1);
            form.DeviceDpi.Should().Be(192);
            dpiChanges.Should().Be(0);
            form.Bounds.Should().Be(new Rectangle(30, 40, 400, 200));
            platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(30, 40, 400, 200));
        });
    }

    [Fact]
    public void ExplicitUnawareKeepsLogical96CoordinatesAndCentersInMatchingDesktopUnits()
    {
        if (!RunDpiCaseInNewProcess())
        {
            VerifyUnawareDesktopUnits(HighDpiMode.DpiUnaware);
        }
    }

    [Fact]
    public void ExplicitUnawareGdiScaledKeepsLogical96CoordinatesAndCentersInMatchingDesktopUnits()
    {
        if (!RunDpiCaseInNewProcess())
        {
            VerifyUnawareDesktopUnits(HighDpiMode.DpiUnawareGdiScaled);
        }
    }

    private static void VerifyUnawareDesktopUnits(HighDpiMode mode)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        platform.SetInitialPresentationScales(2, 1);
        Application.SetHighDpiMode(mode).Should().BeTrue();
        using CenteringForm form = new()
        {
            AutoScaleMode = AutoScaleMode.None,
            StartPosition = FormStartPosition.Manual,
            Size = new Size(400, 200),
        };

        RunSystemDpiForm(platform, form, () =>
        {
            form.DeviceDpi.Should().Be(96);
            Screen.PrimaryScreen!.WorkingArea.Should().Be(new Rectangle(0, 0, 1200, 750));
            form.CenterOnScreen();
            form.Location.Should().Be(new Point(400, 275));
            platform.LastCoordinateMode.Should().Be(LibreWindowCoordinateMode.Logical);
            platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(800, 550, 800, 400));
        });
    }

    [Fact]
    public void SystemAwareRejectsMissingPrimaryWithoutChangingTheCurrentMode()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1) with { IsPrimary = false });
        Action setMode = () => Application.SetHighDpiMode(HighDpiMode.SystemAware);
        setMode.Should().Throw<InvalidOperationException>();
        Application.HighDpiMode.Should().Be(HighDpiMode.DpiUnaware);
        using Control control = new();
        control.DeviceDpi.Should().Be(96);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SystemAwareRejectsInvalidPrimaryDpiWithoutChangingTheCurrentMode(double scale)
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1) with { DpiScale = scale });
        Action setMode = () => Application.SetHighDpiMode(HighDpiMode.SystemAware);
        setMode.Should().Throw<ArgumentOutOfRangeException>();
        Application.HighDpiMode.Should().Be(HighDpiMode.DpiUnaware);
        using Control control = new();
        control.DeviceDpi.Should().Be(96);
    }

    [Fact]
    public void ScreenRejectsAnUndeclaredNativeDesktopCoordinateScale()
    {
        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1) with { NativeCoordinateScale = null });
        Action readScreen = () => _ = Screen.AllScreens;
        readScreen.Should().Throw<PlatformNotSupportedException>();
    }

    [Fact]
    public void FirstExplicitUnawarePolicyCannotBeReplacedBySdkSystemAwareDefault()
    {
        if (RunDpiCaseInNewProcess())
        {
            return;
        }

        UseHeadlessPlatform(autoCloseWindows: false);
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware).Should().BeTrue();
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeFalse();
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware).Should().BeFalse();
        Application.HighDpiMode.Should().Be(HighDpiMode.DpiUnaware);
    }

    [Fact]
    public void FirstExplicitPerMonitorPolicyCannotBeReplacedBySdkSystemAwareDefault()
    {
        if (RunDpiCaseInNewProcess())
        {
            return;
        }

        UseHeadlessPlatform(autoCloseWindows: false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2).Should().BeTrue();
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeFalse();
        Application.HighDpiMode.Should().Be(HighDpiMode.PerMonitorV2);
    }

    [Fact]
    public void FailedSystemDpiAdmissionDoesNotConsumeTheFirstSuccessfulPolicy()
    {
        if (RunDpiCaseInNewProcess())
        {
            return;
        }

        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1) with { DpiScale = double.NaN });
        Action configure = () => Application.SetHighDpiMode(HighDpiMode.SystemAware);
        configure.Should().Throw<ArgumentOutOfRangeException>();
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        using Control control = new();
        control.DeviceDpi.Should().Be(192);
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware).Should().BeFalse();
    }

    [Fact]
    public void SystemAwareDropdownUsesTheOwnersDevicePixelDesktopPolicy()
    {
        if (RunDpiCaseInNewProcess())
        {
            return;
        }

        HeadlessPlatform platform = UseHeadlessPlatform(autoCloseWindows: false);
        platform.SetMonitors(SystemDpiMonitor(2, 1));
        platform.SetInitialPresentationScales(2, 1);
        Application.SetHighDpiMode(HighDpiMode.SystemAware).Should().BeTrue();
        using Form form = new() { AutoScaleMode = AutoScaleMode.None, Bounds = new Rectangle(100, 200, 500, 400), StartPosition = FormStartPosition.Manual };
        using ContextMenuStrip menu = new();
        menu.Items.Add("Command");
        RunSystemDpiForm(platform, form, () =>
        {
            menu.Show(form, new Point(20, 30));
            menu.Visible.Should().BeTrue();
            menu.DeviceDpi.Should().Be(192);
            platform.LastWindowOptions.CoordinateMode.Should().Be(LibreWindowCoordinateMode.DevicePixels);
            platform.LastWindowOptions.ScaleOnDpiChange.Should().BeFalse();
            platform.LastNativeWindowBounds.Should().Be(new LibreRectangle(menu.Left, menu.Top, menu.Width, menu.Height));
            menu.Close();
        });
    }

    private static bool RunDpiCaseInNewProcess(bool forceTerminalColors = false, [CallerMemberName] string method = "")
    {
        const string marker = "LIBREWINFORMS_DPI_TEST_METHOD";
        string name = $"LibreWinForms.CanonicalLifecycle.Tests.CanonicalLifecycleTests.{method}";
        string? childMethod = Environment.GetEnvironmentVariable(marker);
        if (childMethod is not null)
        {
            childMethod.Should().Be(name, "the child must execute only its exact admitted fact");
            return false;
        }

        ProcessStartInfo start = new(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(CanonicalLifecycleTests).Assembly.Location);
        }

        foreach (string argument in new[] { "--filter-method", name, "--minimum-expected-tests", "1", "--fail-skips", "on", "--timeout", "30s", "--no-progress", "--no-ansi" })
        {
            start.ArgumentList.Add(argument);
        }

        if (forceTerminalColors)
        {
            // Reproduce the hosted CI environment only inside this owned child.
            start.Environment["GITHUB_ACTIONS"] = "true";
            start.Environment["CI"] = "true";
            start.Environment["TERM"] = "xterm-256color";
            start.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";
            start.Environment.Remove("NO_COLOR");
        }

        // The exact one-case summary is a machine contract, not terminal output.
        // --no-ansi alone cannot prevent Console color writes when the inherited
        // runtime explicitly permits ANSI on redirected streams.
        start.Environment["NO_COLOR"] = "1";
        start.Environment["TERM"] = "dumb";
        start.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "0";
        start.Environment[marker] = name;
        using Process child = Process.Start(start)!;
        Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        Task<string> stderr = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(45_000))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
            throw new TimeoutException($"DPI child timed out: {name}\n{stdout.GetAwaiter().GetResult()}\n{stderr.GetAwaiter().GetResult()}");
        }

        string output = stdout.GetAwaiter().GetResult();
        string error = stderr.GetAwaiter().GetResult();
        child.ExitCode.Should().Be(0, $"{name}\n{output}\n{error}");
        string[] summary = output.Split('\n').Select(line => line.Trim()).ToArray();
        summary.Should().Contain("total: 1").And.Contain("failed: 0").And.Contain("succeeded: 1").And.Contain("skipped: 0");
        return true;
    }

    private static LibreMonitor SystemDpiMonitor(double dpiScale, double nativeCoordinateScale)
        => new("primary", new(0, 0, (int)(2400 / nativeCoordinateScale), (int)(1600 / nativeCoordinateScale)),
            new(0, 0, (int)(2400 / nativeCoordinateScale), (int)(1500 / nativeCoordinateScale)), dpiScale, true)
        { NativeCoordinateScale = nativeCoordinateScale };

    private static void RunSystemDpiForm(HeadlessPlatform platform, Form form, Action verify)
    {
        Exception? failure = null;
        form.Shown += (_, _) =>
        {
            try
            {
                verify();
            }
            catch (Exception exception)
            {
                failure = exception;
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
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware).Should().BeFalse();
        }

        failure.Should().BeNull();
    }
}
