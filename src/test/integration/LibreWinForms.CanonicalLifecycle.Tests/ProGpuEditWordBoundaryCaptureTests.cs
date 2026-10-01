// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_NATIVE_EDIT_WORD_BOUNDARIES
using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using ProGPU.Backend.Native;
using Xunit;

namespace LibreWinForms.CanonicalLifecycle.Tests;

public sealed class ProGpuEditWordBoundaryCaptureTests
{
    [Theory]
    [InlineData(0, new[] { 0 }, 0)]
    [InlineData(15, new[] { 0, 2, 9, 15 }, 2)]
    [InlineData(11, new[] { 0, 4, 7, 9, 11 }, 0)]
    [InlineData(8, new[] { 0, 1, 4, 6, 8 }, 0)]
    public void CompleteOwnedInventoryPreservesEveryOriginalUtf16Endpoint(int length, int[] positions, int leading)
    {
        int[] original = (int[])positions.Clone();
        LibreEditWordBoundaries result = ProGpuEditWordBoundaryCapture.Copy(Success(positions.Length, leading), positions, leading, length);
        Array.Fill(positions, -1);
        Assert.Equal(original, result.Positions.ToArray());
        Assert.Equal(leading, result.LeadingContentStart);
    }

    [Theory]
    [InlineData(NativeEditWordBoundaryError.UnqualifiedBmpSymbolPolicy)]
    [InlineData(NativeEditWordBoundaryError.UnqualifiedJoinerPolicy)]
    [InlineData(NativeEditWordBoundaryError.UnqualifiedComplexScriptPolicy)]
    [InlineData(NativeEditWordBoundaryError.UnqualifiedScriptItemTransitionPolicy)]
    [InlineData(NativeEditWordBoundaryError.DependencyUnavailable)]
    public void RejectedSourcePreservesExactTypedResultWithoutFallback(NativeEditWordBoundaryError error)
    {
        NativeEditWordBoundaryResult failure = new() { Status = NativeRendererStatus.Unsupported, ErrorCode = error };
        var exception = Assert.Throws<ProGpuEditWordBoundaryException>(() =>
            ProGpuEditWordBoundaryCapture.Copy(failure, new[] { 0, 15 }, 0, 15));
        Assert.Equal(failure.Status, exception.Result.Status);
        Assert.Equal(failure.ErrorCode, exception.Result.ErrorCode);
        Assert.Equal(0U, exception.Result.BoundaryCount);
        Assert.Equal(0U, exception.Result.LeadingContentStart);
    }

    [Theory]
    [InlineData(new[] { 0, 2, 9 }, 15, 2)]
    [InlineData(new[] { 1, 2, 15 }, 15, 2)]
    [InlineData(new[] { 0, 2, 2, 15 }, 15, 2)]
    [InlineData(new[] { 0, -1, 15 }, 15, 0)]
    [InlineData(new[] { 0, 16, 15 }, 15, 0)]
    [InlineData(new[] { 0, 2, 9, 15 }, 15, 3)]
    [InlineData(new int[] { }, 0, 0)]
    public void InvalidInventoryCannotPublishAnOwnedSnapshot(int[] positions, int length, int leading)
    {
        int[] original = (int[])positions.Clone();
        Assert.Throws<InvalidOperationException>(() =>
            ProGpuEditWordBoundaryCapture.Copy(Success(positions.Length, leading), positions, leading, length));
        Assert.Equal(original, positions);
    }

    [Fact]
    public void ContradictorySuccessMetadataRejects()
    {
        NativeEditWordBoundaryResult result = Success(3);
        Assert.Throws<InvalidOperationException>(() => ProGpuEditWordBoundaryCapture.Copy(result, new[] { 0, 15 }, 0, 15));
        result = Success(2);
        result.ErrorCode = NativeEditWordBoundaryError.DependencyFailure;
        Assert.Throws<InvalidOperationException>(() => ProGpuEditWordBoundaryCapture.Copy(result, new[] { 0, 15 }, 0, 15));
        result = Success(2, 1);
        Assert.Throws<InvalidOperationException>(() => ProGpuEditWordBoundaryCapture.Copy(result, new[] { 0, 15 }, 0, 15));
        Assert.Throws<InvalidOperationException>(() => ProGpuEditWordBoundaryCapture.Copy(Success(2), null, 15));
    }

    [Fact]
    public void OrdinarySourceServiceDoesNotAdvertiseWordSelectionAdmission()
        => Assert.False((object)new ProGpuTextRendererService() is ILibreEditWordBoundaryService);

    private static NativeEditWordBoundaryResult Success(int count, int leading = 0)
        => new() { Status = NativeRendererStatus.Success, BoundaryCount = (uint)count, LeadingContentStart = (uint)leading };
}
#endif
