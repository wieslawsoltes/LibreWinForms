// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Backend;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public class Dx12CompilerOptionsTests
{
    [Fact]
    public void UnspecifiedStandaloneContextPreservesEnvironmentPolicy()
    {
        using WgpuContext context = SilkLibreWindow.CreateRenderContext(null, null);
        Assert.Equal(WgpuDx12CompilerOptions.FromEnvironment(), context.Dx12CompilerOptions);
    }

    [Theory]
    [InlineData(WgpuDx12ShaderCompiler.Automatic)]
    [InlineData(WgpuDx12ShaderCompiler.Fxc)]
    [InlineData(WgpuDx12ShaderCompiler.Dxc)]
    public void ExplicitCompilerRetainsTheExactDescriptor(WgpuDx12ShaderCompiler compiler)
    {
        WgpuDx12CompilerOptions requested = new(compiler);
        using WgpuContext context = SilkLibreWindow.CreateRenderContext(requested, null);
        Assert.Same(requested, context.Dx12CompilerOptions);
    }

    [Fact]
    public void PopupAndRecoveryResolveTheActualOwnerAndPreserveExplicitConflict()
    {
        WgpuDx12CompilerOptions first = new(WgpuDx12ShaderCompiler.Dxc, Path.GetFullPath("first"));
        WgpuDx12CompilerOptions second = new(WgpuDx12ShaderCompiler.Dxc, Path.GetFullPath("second"));
        using WgpuContext owner = new() { Dx12CompilerOptions = first };
        using WgpuContext replacement = new() { Dx12CompilerOptions = second };
        using WgpuContext popup = SilkLibreWindow.CreateRenderContext(null, owner);
        using WgpuContext recovered = SilkLibreWindow.CreateRenderContext(null, replacement);
        using WgpuContext conflict = SilkLibreWindow.CreateRenderContext(first, replacement);
        Assert.Same(first, popup.Dx12CompilerOptions);
        Assert.Same(second, recovered.Dx12CompilerOptions);
        Assert.Same(first, conflict.Dx12CompilerOptions);
        Assert.Same(second, replacement.Dx12CompilerOptions);
    }
}
