// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.Scene;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuReversibleDrawingTests
{
    [Fact]
    public void ExactRepeatedOperationTogglesOff()
    {
        var store = new ProGpuReversibleDrawingStore();
        ProGpuReversibleDrawingOperation operation = ProGpuReversibleDrawingOperation.CreateFrame(
            new LibreRectangle(10, 20, 30, 40),
            new LibreArgbColor(unchecked((int)0xFF102030)),
            LibreReversibleFrameStyle.Dashed);

        store.Snapshot().Should().BeEmpty();
        store.Toggle(operation).Should().Equal(operation);
        store.Toggle(operation).Should().BeEmpty();
        store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void OperationKindColorAndStyleRemainPartOfToggleIdentity()
    {
        var store = new ProGpuReversibleDrawingStore();
        LibreRectangle rectangle = new(1, 2, 30, 40);
        LibreArgbColor dark = new(unchecked((int)0xFF102030));
        LibreArgbColor light = new(unchecked((int)0xFFE0D0C0));

        ProGpuReversibleDrawingOperation[] operations =
        [
            ProGpuReversibleDrawingOperation.CreateFrame(
                rectangle,
                dark,
                LibreReversibleFrameStyle.Dashed),
            ProGpuReversibleDrawingOperation.CreateFrame(
                rectangle,
                dark,
                LibreReversibleFrameStyle.Thick),
            ProGpuReversibleDrawingOperation.CreateFillRectangle(rectangle, dark),
            ProGpuReversibleDrawingOperation.CreateFillRectangle(rectangle, light),
            ProGpuReversibleDrawingOperation.CreateLine(
                new LibrePoint(rectangle.X, rectangle.Y),
                new LibrePoint(rectangle.Right, rectangle.Bottom),
                dark),
        ];

        foreach (ProGpuReversibleDrawingOperation operation in operations)
        {
            store.Toggle(operation);
        }

        store.Snapshot().Should().BeEquivalentTo(operations);
    }

    [Fact]
    public void RetainedPaintFrameKeepsReversibleVisualAboveTransientGraphics()
    {
        var root = new ContainerVisual();
        var fallback = new DrawingVisual();
        var transient = new DrawingVisual();
        var reversible = new DrawingVisual();
        var layers = new Dictionary<LibreHandle, DrawingVisual>();
        root.AddChild(fallback);
        root.AddTopmostChild(transient);
        root.AddTopmostChild(reversible);
        var frame = new ProGpuRetainedPaintFrame(
            root,
            fallback,
            transient,
            reversible,
            layers,
            new LibreRectangle(0, 0, 100, 100),
            new LibreRectangle(0, 0, 100, 100));

        using (frame.OpenLayer(
            new LibreHandle((nint)1, LibreHandleKind.LogicalControl),
            new LibreRectangle(5, 5, 20, 20),
            new LibreRectangle(5, 5, 20, 20)))
        {
        }

        frame.Complete();

        root.Children.Should().HaveCount(4);
        root.Children[0].Should().BeSameAs(fallback);
        root.Children[^2].Should().BeSameAs(transient);
        root.Children[^1].Should().BeSameAs(reversible);
    }
}
