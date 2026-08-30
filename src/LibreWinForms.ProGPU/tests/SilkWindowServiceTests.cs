// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class SilkWindowServiceTests
{
    [Fact]
    public void ExternalOwnerServiceUsesTypedProGpuRegistryForModalStateAndActivation()
    {
        using ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        var service = new SilkWindowService(dispatcher, handles, new SilkMonitorService());
        nint presentationHandle = (nint)0x505731;
        LibreHandle ownerHandle = new(presentationHandle, LibreHandleKind.Window);
        var nativeOwner = new TestNativeWindowOwner();
        using IDisposable registration =
            NativeWindowOwnerRegistry.Register(presentationHandle, nativeOwner);

        service.IsLive(ownerHandle).Should().BeTrue();
        service.TryGetState(ownerHandle, out LibreExternalWindowOwnerState state).Should().BeTrue();
        state.Should().Be(new LibreExternalWindowOwnerState(IsVisible: true, IsEnabled: true));

        service.TrySetEnabled(ownerHandle, enabled: false).Should().BeTrue();
        nativeOwner.IsEnabled.Should().BeFalse();
        service.TryActivate(ownerHandle).Should().BeTrue();
        nativeOwner.ActivationCount.Should().Be(1);

        registration.Dispose();

        service.IsLive(ownerHandle).Should().BeFalse();
        service.TryGetState(ownerHandle, out _).Should().BeFalse();
        service.TrySetEnabled(ownerHandle, enabled: true).Should().BeFalse();
        service.TryActivate(ownerHandle).Should().BeFalse();
    }

    private sealed class TestNativeWindowOwner : INativeWindowOwner
    {
        public NativeWindowHandle NativeHandle { get; } = new(
            NativeWindowKind.X11,
            (nint)0x1234,
            (nint)0x5678,
            "X11");

        public bool IsAlive { get; set; } = true;

        public bool IsVisible { get; set; } = true;

        public bool IsEnabled { get; private set; } = true;

        public int ActivationCount { get; private set; }

        public bool TrySetEnabled(bool enabled)
        {
            IsEnabled = enabled;
            return true;
        }

        public bool TryActivate()
        {
            ActivationCount++;
            return true;
        }
    }
}
