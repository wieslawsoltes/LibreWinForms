// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

internal enum ProGpuReversibleDrawingKind
{
    Frame,
    Line,
    FillRectangle,
}

internal readonly record struct ProGpuReversibleDrawingOperation(
    ProGpuReversibleDrawingKind Kind,
    LibreRectangle Rectangle,
    LibrePoint Start,
    LibrePoint End,
    LibreArgbColor BackColor,
    LibreReversibleFrameStyle FrameStyle)
{
    internal static ProGpuReversibleDrawingOperation CreateFrame(
        LibreRectangle rectangle,
        LibreArgbColor backColor,
        LibreReversibleFrameStyle style)
        => new(
            ProGpuReversibleDrawingKind.Frame,
            rectangle,
            default,
            default,
            backColor,
            style);

    internal static ProGpuReversibleDrawingOperation CreateLine(
        LibrePoint start,
        LibrePoint end,
        LibreArgbColor backColor)
        => new(
            ProGpuReversibleDrawingKind.Line,
            default,
            start,
            end,
            backColor,
            default);

    internal static ProGpuReversibleDrawingOperation CreateFillRectangle(
        LibreRectangle rectangle,
        LibreArgbColor backColor)
        => new(
            ProGpuReversibleDrawingKind.FillRectangle,
            rectangle,
            default,
            default,
            backColor,
            default);
}

/// <summary>
/// Stores retained reversible operations. Exact repeated operations toggle off,
/// matching the public ControlPaint contract without depending on native XOR.
/// </summary>
internal sealed class ProGpuReversibleDrawingStore
{
    private readonly List<ProGpuReversibleDrawingOperation> _operations = [];

    internal IReadOnlyList<ProGpuReversibleDrawingOperation> Toggle(
        ProGpuReversibleDrawingOperation operation)
    {
        int index = _operations.IndexOf(operation);
        if (index >= 0)
        {
            _operations.RemoveAt(index);
        }
        else
        {
            _operations.Add(operation);
        }

        return [.. _operations];
    }

    internal IReadOnlyList<ProGpuReversibleDrawingOperation> Snapshot()
        => [.. _operations];
}
