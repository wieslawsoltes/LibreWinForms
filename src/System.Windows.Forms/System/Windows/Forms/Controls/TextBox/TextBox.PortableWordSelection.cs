// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public partial class TextBox
{
    private PortableWordPointerSelection? _portableWordPointerSelection;

    private bool ApplyPortableWordPointerDown(MouseEventArgs e, in PortablePointerDispatchContext context)
    {
        PortableTextPointerState state = new(this);
        if (!context.IsCurrent) return false;
        ILibreTextLayout? layout = GetPortableInputLayout(context);
        if (!state.IsCurrent(this, state.SelectionVersion) || !context.IsCurrent) return false;
        if (layout is not ILibreEditWordBoundaryLayout words)
            throw new InvalidOperationException("The text provider declared EDIT word boundaries but returned a layout without that capability.");
        uint layoutVersion = _portableLayoutVersion;
        bool IsCurrent() => state.IsCurrent(this, state.SelectionVersion)
            && state.SelectionVersion == _portableSelectionVersion
            && ReferenceEquals(layout, _portableTextLayout) && layoutVersion == _portableLayoutVersion;

        if (!IsCurrent() || !context.IsCurrent) return false;
        LibreEditWordBoundaries boundaries = words.GetWordBoundaries();
        if (!IsCurrent() || !context.IsCurrent) return false;
        int length = state.TextLength;
        ReadOnlySpan<int> positions = boundaries.Positions.Span;
        if (!IsCurrent() || !context.IsCurrent) return false;
        ValidatePortableWordBoundaries(positions, boundaries.LeadingContentStart, length);
        if (!IsCurrent() || !context.IsCurrent) return false;
        LibreTextHit hit = layout.HitTest(new PointF(e.X - state.Viewport.X + state.Scroll.X,
            e.Y - state.Viewport.Y + state.Scroll.Y));
        if (!IsCurrent() || !context.IsCurrent) return false;
        ValidatePortableWordHit(hit.TextPosition, length);
        int row = words.GetCaretRowIndex(hit.TextPosition, hit.IsTrailing);
        if (!IsCurrent() || !context.IsCurrent) return false;
        int rowCount = words.RowCount;
        if (!IsCurrent() || !context.IsCurrent) return false;
        if (rowCount <= 0 || (uint)row >= (uint)rowCount)
            throw new InvalidOperationException("The word-selection hit has no retained source row.");
        int rowStart = words.GetRowSourceStart(row);
        if (!IsCurrent() || !context.IsCurrent) return false;
        if ((uint)rowStart > (uint)length)
            throw new InvalidOperationException("The word-selection row has an invalid source start.");

        int effectiveHit = hit.TextPosition == 0 ? boundaries.LeadingContentStart : hit.TextPosition;
        if (!IsCurrent() || !context.IsCurrent) return false;
        int index = PortableWordFloorIndex(positions, effectiveHit);
        if (positions[index] == effectiveHit && hit.TextPosition != 0 && hit.TextPosition != rowStart && index > 0) index--;
        int start = positions[index];
        int end = positions[Math.Min(index + 1, positions.Length - 1)];
        SelectInternal(start, end - start, length);
        uint selectedVersion = unchecked(state.SelectionVersion + 1);
        if (!state.IsCurrent(this, selectedVersion, checkScroll: false) || selectedVersion != _portableSelectionVersion
            || !ReferenceEquals(layout, _portableTextLayout) || layoutVersion != _portableLayoutVersion
            || !context.IsCurrent) return false;
        _portableWordPointerSelection = new(layout, layoutVersion, boundaries, state, selectedVersion,
            length, hit.TextPosition, start, end);
        return true;
    }

    private bool ApplyPortableWordPointerMove(MouseEventArgs e, in PortablePointerDispatchContext context,
        PortableWordPointerSelection word)
    {
        bool current = word.IsCurrent(this);
        if (!context.IsCurrent) return false;
        if (!current)
        {
            RetirePortablePointerSelection();
            return true;
        }

        PortableTextPointerState state = new(this);
        if (!word.IsCurrent(this) || !context.IsCurrent) return false;
        LibreTextHit hit = word.Layout.HitTest(new PointF(e.X - state.Viewport.X + state.Scroll.X,
            e.Y - state.Viewport.Y + state.Scroll.Y));
        if (!state.IsCurrent(this, state.SelectionVersion) || !word.IsCurrent(this) || !context.IsCurrent) return false;
        ValidatePortableWordHit(hit.TextPosition, word.Length);
        int effectiveHit = hit.TextPosition == 0 ? word.Boundaries.LeadingContentStart : hit.TextPosition;
        int anchor;
        int active;
        ReadOnlySpan<int> positions = word.Boundaries.Positions.Span;
        if (!state.IsCurrent(this, state.SelectionVersion) || !word.IsCurrent(this) || !context.IsCurrent) return false;
        if (hit.TextPosition < word.Hit)
        {
            anchor = word.End;
            active = positions[PortableWordFloorIndex(positions, effectiveHit)];
        }
        else if (hit.TextPosition > word.Hit)
        {
            anchor = word.Start;
            int index = PortableWordFloorIndex(positions, effectiveHit);
            active = positions[positions[index] == effectiveHit ? index : index + 1];
        }
        else
        {
            anchor = word.Start;
            active = word.End;
        }

        SelectInternal(anchor, active - anchor, word.Length);
        uint selectedVersion = unchecked(state.SelectionVersion + 1);
        if (!state.IsCurrent(this, selectedVersion, checkScroll: false) || selectedVersion != _portableSelectionVersion
            || !ReferenceEquals(_portableWordPointerSelection, word)
            || !ReferenceEquals(word.Layout, _portableTextLayout) || word.LayoutVersion != _portableLayoutVersion
            || !context.IsCurrent) return false;
        word.SelectionVersion = selectedVersion;
        return true;
    }

    private static void ValidatePortableWordBoundaries(ReadOnlySpan<int> positions, int leadingContentStart, int length)
    {
        if (positions.IsEmpty || positions[0] != 0 || positions[^1] != length
            || (uint)leadingContentStart > (uint)length)
            throw new InvalidOperationException("The EDIT word-boundary snapshot does not cover the source text.");
        for (int i = 1; i < positions.Length; i++)
        {
            if (positions[i] <= positions[i - 1])
                throw new InvalidOperationException("EDIT word boundaries must be strictly increasing UTF-16 positions.");
        }

        if (positions[PortableWordFloorIndex(positions, leadingContentStart)] != leadingContentStart)
            throw new InvalidOperationException("The leading content position must be an EDIT word boundary.");
    }

    private static void ValidatePortableWordHit(int position, int length)
    {
        if ((uint)position > (uint)length)
            throw new InvalidOperationException("The word-selection hit is outside the source text.");
    }

    private static int PortableWordFloorIndex(ReadOnlySpan<int> positions, int position)
    {
        int low = 0, high = positions.Length - 1;
        while (low < high)
        {
            int middle = low + (high - low + 1) / 2;
            if (positions[middle] <= position) low = middle;
            else high = middle - 1;
        }

        return low;
    }

    private sealed class PortableWordPointerSelection(ILibreTextLayout layout, uint layoutVersion,
        LibreEditWordBoundaries boundaries, PortableTextPointerState state, uint selectionVersion,
        int length, int hit, int start, int end)
    {
        internal ILibreTextLayout Layout { get; } = layout;
        internal uint LayoutVersion { get; } = layoutVersion;
        internal LibreEditWordBoundaries Boundaries { get; } = boundaries;
        internal int Length { get; } = length;
        internal int Hit { get; } = hit;
        internal int Start { get; } = start;
        internal int End { get; } = end;
        internal uint SelectionVersion { get; set; } = selectionVersion;

        internal bool IsCurrent(TextBox owner) => state.IsCurrent(owner, SelectionVersion, checkScroll: false)
            && SelectionVersion == owner._portableSelectionVersion
            && ReferenceEquals(owner._portableWordPointerSelection, this)
            && ReferenceEquals(owner._portableTextLayout, Layout) && owner._portableLayoutVersion == LayoutVersion;
    }
}
#endif
