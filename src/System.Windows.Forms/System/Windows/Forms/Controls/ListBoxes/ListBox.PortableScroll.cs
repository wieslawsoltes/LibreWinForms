// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Drawing;
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public partial class ListBox
{
    // This identity has no reference back to the list, its collection or items.
    private sealed class PortableListScrollIdentity { }
    private PortableListScrollIdentity? _portableListScrollIdentity;

    private void InvalidatePortableListScrollFrame() => _portableListScrollIdentity = null;

    internal override PortableScrollFrame? CapturePortableScrollFrame(in LibreNativeScrollMetadata scroll,
        in PortablePointerDispatchContext dispatch)
    {
        // The existing renderer/hit geometry is row-aligned and vertical only.
        // Reject the entire vector before consuming either axis or any fraction.
        if (!dispatch.IsCurrent || scroll.X != 0 || !IsPortableListMode)
            return null;

        Rectangle viewport = GetPortableListViewport();
        int rowHeight = FontHeight;
        if (viewport.Width <= 0 || viewport.Height <= 0 || rowHeight <= 0)
            return null;

        ItemArray items = Items.InnerArray;
        int maximum = Math.Max(0, items.Count - Math.Max(1, viewport.Height / rowHeight));
        return new PortableListScrollFrame(_portableListScrollIdentity ??= new(), new(items), items.Version,
            items.Count, viewport, rowHeight, DeviceDpi, Math.Clamp(_topIndex, 0, maximum), maximum);
    }

    private bool IsCurrentPortableListScrollFrame(PortableListScrollFrame frame)
        => IsPortableListMode && ReferenceEquals(_portableListScrollIdentity, frame.Identity)
            && frame.Items.TryGetTarget(out ItemArray? items) && ReferenceEquals(_itemsCollection?.InnerArray, items)
            && items.Version == frame.Version && items.Count == frame.Count
            && GetPortableListViewport() == frame.Viewport && FontHeight == frame.RowHeight && DeviceDpi == frame.Dpi
            && GetPortableTopIndex() == frame.Top;

    private sealed class PortableListScrollFrame(PortableListScrollIdentity identity, WeakReference<ItemArray> items,
        int version, int count, Rectangle viewport, int rowHeight, int dpi, int top, int maximum) : PortableScrollFrame
    {
        internal PortableListScrollIdentity Identity { get; } = identity;
        internal WeakReference<ItemArray> Items { get; } = items;
        internal int Version { get; } = version;
        internal int Count { get; } = count;
        internal Rectangle Viewport { get; } = viewport;
        internal int RowHeight { get; } = rowHeight;
        internal int Dpi { get; } = dpi;
        internal int Top { get; } = top;

        internal override bool Matches(PortableScrollFrame previous)
            => previous is PortableListScrollFrame frame && ReferenceEquals(Identity, frame.Identity)
                && Items.TryGetTarget(out ItemArray? current) && frame.Items.TryGetTarget(out ItemArray? old)
                && ReferenceEquals(current, old) && Version == frame.Version && Count == frame.Count
                && Viewport == frame.Viewport && RowHeight == frame.RowHeight && Dpi == frame.Dpi && Top == frame.Top;

        internal override PortableScrollPlan Plan(in LibreNativeScrollMetadata scroll, double x, double y)
        {
            bool points = scroll.Unit == LibreNativeScrollUnit.Points;
            double delta = points ? scroll.Y * scroll.PointScale : scroll.Y;
            if (!double.IsFinite(delta))
                throw new ArgumentException("Native list scroll exceeds its source metric range.", nameof(scroll));
            if (delta != 0 && Math.Sign(delta) != Math.Sign(y))
                y = 0;
            double total = delta + y;
            if (!double.IsFinite(total))
                throw new ArgumentException("Native list scroll exceeds its source accumulation range.", nameof(scroll));

            // Point carry stays in source pixels; line carry stays in rows.
            // Only a complete actual source row changes TopIndex. Painting and
            // hit testing remain on their original integer row boundaries.
            int step = points ? RowHeight : 1;
            double rows = Math.Truncate(total / step);
            int next = (int)Math.Clamp(Top - rows, 0, maximum);
            double tail = (next == 0 && total > 0) || (next == maximum && total < 0)
                ? 0 : total - rows * step;
            PortableListScrollFrame expected = new(next == Top ? Identity : new(), Items, Version, Count,
                Viewport, RowHeight, Dpi, next, maximum);
            return new PortableListScrollPlan(this, expected, tail);
        }
    }

    private sealed class PortableListScrollPlan(PortableListScrollFrame before, PortableListScrollFrame expected,
        double tail) : PortableScrollPlan(expected, 0, tail)
    {
        internal override bool Apply(Control consumer, in PortablePointerDispatchContext dispatch)
        {
            ListBox owner = (ListBox)consumer;
            if (!dispatch.IsCurrent || !owner.IsCurrentPortableListScrollFrame(before))
                return false;
            owner.SetPortableTopIndex(expected.Top, expected.Identity);
            return dispatch.IsCurrent && owner.IsCurrentPortableListScrollFrame(expected);
        }
    }
}
#endif
