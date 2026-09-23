// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Xunit;

namespace LibreWinForms.Platform.Tests;

public class ManagedLibreHandleRegistryTests
{
    [Fact]
    public void AllocateResolveRelease_PreservesTypeKindAndLifetime()
    {
        ManagedLibreHandleRegistry registry = new();
        object owner = new();

        LibreHandle handle = registry.Allocate(owner, LibreHandleKind.LogicalControl);

        handle.IsNull.Should().BeFalse();
        handle.Value.Should().BeLessThan(0);
        registry.Count.Should().Be(1);
        registry.TryGet(handle, out object? resolved).Should().BeTrue();
        resolved.Should().BeSameAs(owner);
        registry.TryGet(new LibreHandle(handle.Value, LibreHandleKind.Window), out object? wrongKind).Should().BeFalse();
        wrongKind.Should().BeNull();
        registry.TryGet(handle, out string? wrongType).Should().BeFalse();
        wrongType.Should().BeNull();

        registry.Release(handle).Should().BeTrue();
        registry.Release(handle).Should().BeFalse();
        registry.TryGet(handle, out object? released).Should().BeFalse();
        released.Should().BeNull();
        registry.Count.Should().Be(0);
    }

    [Fact]
    public void Allocate_ConcurrentlyProducesUniqueOpaqueHandles()
    {
        ManagedLibreHandleRegistry registry = new();
        LibreHandle[] handles = new LibreHandle[1_024];

        Parallel.For(0, handles.Length, index =>
        {
            handles[index] = registry.Allocate(new object(), LibreHandleKind.GraphicsTarget);
        });

        handles.Select(handle => handle.Value).Distinct().Should().HaveCount(handles.Length);
        registry.Count.Should().Be(handles.Length);
    }
}
