// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_NATIVE_EDIT_WORD_BOUNDARIES
using LibreWinForms.Platform;
using ProGPU.Backend.Native;
using ProGPU.SystemDrawing;

namespace LibreWinForms.ProGPU;

/// <summary>The exact native failure of an explicitly requested EDIT inventory.</summary>
public sealed class ProGpuEditWordBoundaryException : NotSupportedException
{
    public NativeEditWordBoundaryResult Result { get; }

    internal ProGpuEditWordBoundaryException(NativeEditWordBoundaryResult result)
        : base($"The retained native EDIT profile rejected this source: {result.Status}/{result.ErrorCode}.")
        => Result = result;
}

internal static class ProGpuEditWordBoundaryCapture
{
    internal static LibreEditWordBoundaries Copy(NativeEditWordBoundaryResult result,
        DrawingEditWordBoundarySnapshot? snapshot, int sourceLength)
    {
        if (result.Status != NativeRendererStatus.Success)
            throw new ProGpuEditWordBoundaryException(result);
        if (snapshot is null || snapshot.TextLength != sourceLength ||
            (uint)snapshot.ParagraphLevel > 1U)
            throw new InvalidOperationException("The EDIT inventory does not belong to the retained source generation.");
        return Copy(result, snapshot.Positions, snapshot.LeadingContentStart, sourceLength);
    }

    // Pure transport validation used by focused source controls; the production
    // caller always obtains these fields from its own DrawingTextLayout above.
    internal static LibreEditWordBoundaries Copy(NativeEditWordBoundaryResult result,
        IReadOnlyList<int> positions, int leadingContentStart, int sourceLength)
    {
        if (result.Status != NativeRendererStatus.Success)
            throw new ProGpuEditWordBoundaryException(result);
        ArgumentNullException.ThrowIfNull(positions);
        if (sourceLength < 0 || result.ErrorCode != NativeEditWordBoundaryError.None ||
            result.BoundaryCount != (uint)positions.Count || positions.Count == 0 ||
            positions.Count > (long)sourceLength + 1 || result.LeadingContentStart != (uint)leadingContentStart ||
            (uint)leadingContentStart > (uint)sourceLength)
            throw new InvalidOperationException("The retained EDIT inventory has invalid result metadata.");

        int[] copy = new int[positions.Count];
        bool containsLeading = false;
        for (int i = 0; i < copy.Length; i++)
        {
            int position = positions[i];
            if ((uint)position > (uint)sourceLength || (i != 0 && position <= copy[i - 1]))
                throw new InvalidOperationException("EDIT boundaries must be strictly increasing original UTF-16 offsets.");
            copy[i] = position;
            containsLeading |= position == leadingContentStart;
        }

        if (copy[0] != 0 || copy[^1] != sourceLength || !containsLeading)
            throw new InvalidOperationException("The retained EDIT inventory does not cover the complete source.");
        return new(copy, leadingContentStart);
    }
}
#endif
