// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class SourceWindowFactoryTests
{
    [Theory]
    [InlineData(false, LibreWindowOptions.None, false)]
    [InlineData(false, LibreWindowOptions.Popup, false)]
    [InlineData(true, LibreWindowOptions.None, false)]
    [InlineData(true, LibreWindowOptions.Popup | LibreWindowOptions.TopMost, true)]
    public void OnlyMacOsSourcePopupsSelectOwnedFactory(bool macOS, LibreWindowOptions options, bool expected)
        => Assert.Equal(expected, SourceWindowFactory.UsesOwnedCocoaPopup(macOS, options));

    [Fact]
    public void OwnedFactoryGetsExactSourceWakeAndOnlySourceOwnedOptionProjection()
    {
        IWindow expected = NativeWindowRetirementQueueTests.CreateWindow();
        int wakes = 0;
        Action wake = () => wakes++;
        WindowOptions requested = WindowOptions.Default with
        {
            Title = "source tooltip metadata", IsVisible = true,
            WindowState = WindowState.Maximized, WindowBorder = WindowBorder.Resizable,
            IsContextControlDisabled = false, TopMost = true,
            Position = new(-811, 123), Size = new(271, 63),
            TransparentFramebuffer = true, VSync = false, IsEventDriven = false,
            FramesPerSecond = 0, UpdatesPerSecond = 0, ShouldSwapAutomatically = false,
        };
        WindowOptions projected = default;
        IWindow actual = SourceWindowFactory.Create(true, requested, wake,
            _ => throw new InvalidOperationException("No ordinary fallback is allowed."),
            (options, sourceWake) =>
            {
                projected = options;
                Assert.Same(wake, sourceWake);
                sourceWake();
                return expected;
            });

        Assert.Same(expected, actual);
        Assert.Equal(1, wakes);
        Assert.Equal(requested with
        {
            IsVisible = false, Title = string.Empty, API = GraphicsAPI.None,
            IsContextControlDisabled = true, WindowState = WindowState.Normal,
            WindowBorder = WindowBorder.Hidden,
        }, projected);
        Assert.Equal("source tooltip metadata", requested.Title);
        Assert.True(requested.IsVisible);
    }

    [Fact]
    public void OrdinaryFactoryGetsUnchangedOptionsWithoutOwnedProviderOrWake()
    {
        IWindow expected = NativeWindowRetirementQueueTests.CreateWindow();
        WindowOptions requested = WindowOptions.Default with
        {
            Title = "ordinary", Position = new Vector2D<int>(40, 70),
            WindowBorder = WindowBorder.Resizable, WindowState = WindowState.Minimized,
        };
        IWindow actual = SourceWindowFactory.Create(false, requested,
            () => throw new InvalidOperationException("Factory does not pump or wake."),
            options => { Assert.Equal(requested, options); return expected; },
            (_, _) => throw new InvalidOperationException("No owned provider for an ordinary window."));
        Assert.Same(expected, actual);
    }

    [Fact]
    public void OwnedFactoryFailureNeverCallsOrdinaryFactory()
    {
        Exception expected = new InvalidOperationException("AppKit unavailable");
        int ordinary = 0;
        Exception actual = Assert.Throws<InvalidOperationException>(() =>
            SourceWindowFactory.Create(true, WindowOptions.Default, () => { },
                _ => { ordinary++; return NativeWindowRetirementQueueTests.CreateWindow(); },
                (_, _) => throw expected));
        Assert.Same(expected, actual);
        Assert.Equal(0, ordinary);
    }

    [Fact]
    public void MissingSourceWakeRejectsBeforeEitherFactory()
    {
        int calls = 0;
        Assert.Throws<ArgumentNullException>(() => SourceWindowFactory.Create(true, WindowOptions.Default, null!,
            _ => { calls++; return null!; }, (_, _) => { calls++; return null!; }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void AcceptedNativeOptionChecksCurrentLifetimeBeforePublishing()
    {
        List<string> order = [];
        OwnedPopupConfiguration.Apply(
            () => { order.Add("native"); return true; },
            () => { order.Add("lifetime"); return true; },
            () => order.Add("discard"), "opacity");
        Assert.Equal(new[] { "native", "lifetime" }, order);
    }

    [Theory]
    [InlineData("topmost level")]
    [InlineData("opacity")]
    [InlineData("content size constraints")]
    [InlineData("content geometry")]
    [InlineData("input permission")]
    [InlineData("nonactivating ordering")]
    public void RejectedNativeOptionRetiresInsteadOfPublishingOrFallingBack(string option)
    {
        int retired = 0;
        PlatformNotSupportedException failure = Assert.Throws<PlatformNotSupportedException>(() =>
            OwnedPopupConfiguration.Apply(() => false, () => true, () => retired++, option));
        Assert.Contains(option, failure.Message);
        Assert.Equal(1, retired);
    }

    [Fact]
    public void ReentrantCloseCannotPublishNativeSuccess()
    {
        bool live = true;
        int retired = 0;
        Assert.Throws<PlatformNotSupportedException>(() => OwnedPopupConfiguration.Apply(
            () => { live = false; return true; }, () => live, () => retired++, "content geometry"));
        Assert.Equal(1, retired);
    }

    [Fact]
    public void ProviderExceptionRemainsPrimaryWhenRetirementAlsoFails()
    {
        Exception expected = new InvalidOperationException("native failure");
        Exception cleanup = new IOException("retirement pending");
        Exception actual = Assert.Throws<InvalidOperationException>(() => OwnedPopupConfiguration.Apply(
            () => throw expected, () => true, () => throw cleanup, "opacity"));
        Assert.Same(expected, actual);
        Assert.Same(cleanup, actual.Data[nameof(OwnedPopupConfiguration)]);
    }

    [Fact]
    public void RejectedNativeOptionRemainsPrimaryWhenRetirementFails()
    {
        Exception cleanup = new IOException("retirement pending");
        Exception actual = Assert.Throws<PlatformNotSupportedException>(() => OwnedPopupConfiguration.Apply(
            () => false, () => true, () => throw cleanup, "content size constraints"));
        Assert.Same(cleanup, actual.Data[nameof(OwnedPopupConfiguration)]);
    }

    [Fact]
    public void ProviderExceptionRemainsPrimaryWhenCleanupDiagnosticsThrow()
    {
        var expected = new ThrowingDataException();
        Exception actual = Assert.Throws<ThrowingDataException>(() => OwnedPopupConfiguration.Apply(
            () => throw expected, () => true, () => throw new IOException("retirement pending"), "opacity"));
        Assert.Same(expected, actual);
        OwnedPopupConfiguration.AttachCleanup(expected, new IOException("constructor retirement"), "constructor");
    }

    private sealed class ThrowingDataException : Exception
    {
        public override System.Collections.IDictionary Data => throw new InvalidOperationException("diagnostics unavailable");
    }

    [Fact]
    public void ActualHostConnectsFactoryRequiredOptionsInputAndRetirement()
    {
        // Source-wiring guard paired with behavioral factory/admission cases.
        // It deliberately makes no native window, rendering or UI parity claim.
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "SourceContracts", "SilkWindowService.cs"));
        Assert.Contains("SourceWindowFactory.UsesOwnedCocoaPopup(OperatingSystem.IsMacOS(), options.Options)", source);
        Assert.Contains("SourceWindowFactory.Create(_usesOwnedCocoaPopup, silkOptions, dispatcher.Wake)", source);
        Assert.DoesNotContain("Silk.NET.Windowing.Window.Create(", source);
        Assert.Contains("get => _usesOwnedCocoaPopup ? _title : _window.Title", source);
        Assert.Contains("ApplyNativeOption(() => _controller.SetTopMost(silkOptions.TopMost)", source);
        Assert.Contains("ApplyNativeOption(() => _controller.SetOpacity(_opacity)", source);
        Assert.Contains("ApplyNativeOption(() => _controller.SetSizeConstraints(", source);
        Assert.Contains("ApplyNativeOption(() => _controller.SetEnabled(value)", source);
        Assert.Contains("() => !_disposed && !_closed && _window.IsInitialized && !_window.IsClosing", source);
        Assert.Contains("ReleaseNativeWindow, option", source);
        Assert.Contains("_input = NativeWindowInput.CreateInput(_window)", source);
        Assert.Contains("_input is INativePointerInputContext nativePointer", source);
        Assert.Contains("_nativePointerInput = new NativePointerInput(nativePointer, this)", source);
        Assert.Contains("NativePopupWindow.TryPrepareOwner(owner, _window)", source);
        Assert.Contains("NativePopupWindow.TryShowOwned(owner, _window, showWithoutActivation)", source);
        Assert.Contains("RetireNativeWindow(_window, ReleaseRenderingResources,", source);
        Assert.Contains("context.InitializeSharedDevice(_window, ownerContext)", source);
    }
}
