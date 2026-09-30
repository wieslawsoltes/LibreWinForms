// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class NativeWindowGeometryServiceTests
{
    [Fact]
    public void MissingWindowDoesNotCreateAHandleOrPublishGeometry()
    {
        using ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        SilkWindowService service = new(dispatcher, handles, new SilkMonitorService());

        service.TryGetNativeGeometrySnapshot(default, out NativeWindowGeometrySnapshot snapshot).Should().BeFalse();
        snapshot.Should().Be(default(NativeWindowGeometrySnapshot));
        service.TryGetNativeGeometrySnapshot(new LibreHandle(17, LibreHandleKind.Window), out snapshot).Should().BeFalse();
        snapshot.Should().Be(default(NativeWindowGeometrySnapshot));
        handles.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(LibreHandleKind.LogicalControl)]
    [InlineData(LibreHandleKind.Menu)]
    [InlineData(LibreHandleKind.GraphicsTarget)]
    [InlineData(LibreHandleKind.Timer)]
    [InlineData((LibreHandleKind)99)]
    public void NonWindowIdentitiesAreRejectedWithoutReinterpretingTheirValue(LibreHandleKind kind)
    {
        using ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        SilkWindowService service = new(dispatcher, handles, new SilkMonitorService());
        object target = new();
        LibreHandle handle = handles.Allocate(target, kind);

        service.TryGetNativeGeometrySnapshot(handle, out NativeWindowGeometrySnapshot snapshot).Should().BeFalse();

        snapshot.Should().Be(default(NativeWindowGeometrySnapshot));
        handles.TryGet(handle, out object? actual).Should().BeTrue();
        actual.Should().BeSameAs(target);
        handles.Count.Should().Be(1);
    }

    [Fact]
    public void WindowKindDoesNotAdmitAnUnrelatedRegistryObject()
    {
        using ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        SilkWindowService service = new(dispatcher, handles, new SilkMonitorService());
        object target = new();
        LibreHandle handle = handles.Allocate(target, LibreHandleKind.Window);

        service.TryGetNativeGeometrySnapshot(handle, out NativeWindowGeometrySnapshot snapshot).Should().BeFalse();

        snapshot.Should().Be(default(NativeWindowGeometrySnapshot));
        handles.TryGet(handle, out object? actual).Should().BeTrue();
        actual.Should().BeSameAs(target);
    }

    [Fact]
    public void ReleasedHandleDoesNotFallBackToAnOpaqueNativePointer()
    {
        using ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        SilkWindowService service = new(dispatcher, handles, new SilkMonitorService());
        LibreHandle handle = handles.Allocate(new object(), LibreHandleKind.Window);
        handles.Release(handle).Should().BeTrue();

        service.TryGetNativeGeometrySnapshot(handle, out NativeWindowGeometrySnapshot snapshot).Should().BeFalse();

        snapshot.Should().Be(default(NativeWindowGeometrySnapshot));
        handles.Count.Should().Be(0);
    }

    [Fact]
    public void ForeignRegistryHandleDoesNotAddOrAdoptAWindow()
    {
        using ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        ManagedLibreHandleRegistry foreign = new();
        SilkWindowService service = new(dispatcher, handles, new SilkMonitorService());
        LibreHandle handle = foreign.Allocate(new object(), LibreHandleKind.Window);

        service.TryGetNativeGeometrySnapshot(handle, out NativeWindowGeometrySnapshot snapshot).Should().BeFalse();

        snapshot.Should().Be(default(NativeWindowGeometrySnapshot));
        handles.Count.Should().Be(0);
        foreign.Count.Should().Be(1);
    }

    [Fact]
    public void LiveExternalOwnerRegistrationIsNotAServiceOwnedGeometryWindow()
    {
        using ProGpuDispatcher dispatcher = new();
        ManagedLibreHandleRegistry handles = new();
        SilkWindowService service = new(dispatcher, handles, new SilkMonitorService());
        LibreHandle handle = new((nint)0x47454F31, LibreHandleKind.Window);
        ExternalOwner owner = new();
        using IDisposable registration = NativeWindowOwnerRegistry.Register(handle.Value, owner);
        int callsBeforeQuery = owner.Calls;

        service.TryGetNativeGeometrySnapshot(handle, out NativeWindowGeometrySnapshot snapshot).Should().BeFalse();

        snapshot.Should().Be(default(NativeWindowGeometrySnapshot));
        owner.Calls.Should().Be(callsBeforeQuery);
        handles.Count.Should().Be(0);
    }

    private sealed class ExternalOwner : INativeWindowOwner
    {
        internal int Calls { get; private set; }

        public NativeWindowHandle NativeHandle
        {
            get { Calls++; return new NativeWindowHandle(NativeWindowKind.Cocoa, 17, 0, "NSWindow"); }
        }

        public bool IsAlive { get { Calls++; return true; } }
        public bool IsVisible { get { Calls++; return true; } }
        public bool IsEnabled { get { Calls++; return true; } }
        public bool TrySetEnabled(bool enabled) { Calls++; return true; }
        public bool TryActivate() { Calls++; return true; }
    }
}
