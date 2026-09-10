// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.Scene;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class ProGpuAdornerTests
{
    [Fact]
    public void CommitReplacesOneRetainedLayerAndRemoveIsIdempotent()
    {
        var root = new ContainerVisual();
        var store = new ProGpuAdornerStore(root);
        LibreAdornerId id = new(7);
        DrawingContext firstRecording = Record(Color.Red);

        DrawingVisual first = store.Commit(
            id,
            new LibreRectangle(10, 20, 30, 40),
            new LibreRectangle(12, 23, 20, 25),
            firstRecording);

        store.Count.Should().Be(1);
        root.Children.Should().ContainSingle().Which.Should().BeSameAs(first);
        first.Offset.Should().Be(new System.Numerics.Vector2(10, 20));
        first.Size.Should().Be(new System.Numerics.Vector2(30, 40));
        first.ClipBounds.Should().Be(new Rect(2, 3, 20, 25));
        first.Context.Commands.Should().NotBeEmpty();

        DrawingContext replacement = Record(Color.Blue);
        DrawingVisual second = store.Commit(
            id,
            new LibreRectangle(15, 25, 35, 45),
            new LibreRectangle(15, 25, 35, 45),
            replacement);

        second.Should().BeSameAs(first);
        store.Count.Should().Be(1);
        root.Children.Should().ContainSingle();
        second.Offset.Should().Be(new System.Numerics.Vector2(15, 25));
        second.Size.Should().Be(new System.Numerics.Vector2(35, 45));
        store.Remove(id).Should().BeTrue();
        store.Remove(id).Should().BeFalse();
        store.Count.Should().Be(0);
        root.Children.Should().BeEmpty();
    }

    private static DrawingContext Record(Color color)
    {
        DrawingContext recording = new();
        using Graphics graphics = Graphics.FromProGpuDrawingContext(recording);
        using SolidBrush brush = new(color);
        graphics.FillRectangle(brush, 0, 0, 4, 5);
        return recording;
    }
}
