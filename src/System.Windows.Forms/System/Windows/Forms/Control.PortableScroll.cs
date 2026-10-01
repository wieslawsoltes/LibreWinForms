// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public partial class Control
{
    private PortableNativeScrollCarry? _portableNativeScrollCarry;

    private static void ValidatePortableNativeScroll(in LibreInputEvent input)
    {
        if (input.NativeScroll is not { } scroll || scroll.Stream is null
            || input.NativePointer is not { Kind: LibreNativePointerKind.Scroll, Button: -1, ClickCount: 0 } native
            || !double.IsFinite(native.X) || !double.IsFinite(native.Y)
            || !double.IsFinite(native.Timestamp) || native.Timestamp < 0
            || ((int)native.Modifiers & ~255) != 0
            || (int)input.Modifiers != ((int)native.Modifiers & 15)
            || input.Button != LibrePointerButton.None || input.Delta != default
            || !double.IsFinite(scroll.X) || !double.IsFinite(scroll.Y)
            || !double.IsFinite(scroll.PointScale) || scroll.PointScale <= 0
            || scroll.Unit is < LibreNativeScrollUnit.Lines or > LibreNativeScrollUnit.Points
            || scroll.Protocol is < LibreNativeScrollProtocol.Unspecified or > LibreNativeScrollProtocol.AppKit)
            throw new ArgumentException("Invalid source native-scroll metadata.", nameof(input));
        if (scroll.Phase != 0 || scroll.MomentumPhase != 0)
            throw new PlatformNotSupportedException("Native scroll phases require source gesture/target ownership.");
    }

    private void DispatchPortableNativeScroll(Control target, in LibreInputEvent input)
    {
        LibreNativeScrollMetadata scroll = input.NativeScroll!.Value;
        LibreHandle sourceHandle = _window.PortableHandle;
        for (Control? current = target; current is not null; current = current.ParentInternal)
        {
            if (current is not ScrollableControl consumer)
                continue;
            PortablePointerDispatchContext dispatch = new(this, consumer);
            if (!dispatch.IsCurrent)
                return;
            if (!consumer.TryGetPortableScrollFrame(scroll.X != 0, scroll.Y != 0, out var frame))
                continue;
            if (!dispatch.IsCurrent)
                return;

            double x = 0, y = 0;
            if (_portableNativeScrollCarry is { } carry
                && carry.SourceHandle == sourceHandle && carry.TargetHandle == consumer._window.PortableHandle
                && carry.Target.TryGetTarget(out ScrollableControl? previous) && ReferenceEquals(previous, consumer)
                && ReferenceEquals(carry.Stream, scroll.Stream) && carry.Generation == scroll.Generation
                && carry.Unit == scroll.Unit && carry.PointScale == scroll.PointScale && carry.Frame == frame)
            {
                x = carry.X;
                y = carry.Y;
            }

            double dx = scroll.X * (scroll.Unit == LibreNativeScrollUnit.Points ? scroll.PointScale : frame.SmallX);
            double dy = scroll.Y * (scroll.Unit == LibreNativeScrollUnit.Points ? scroll.PointScale : frame.SmallY);
            (int nextX, double remainderX) = AccumulatePortableScroll(frame.X, frame.MinX, dx, x);
            (int nextY, double remainderY) = AccumulatePortableScroll(frame.Y, frame.MinY, dy, y);
            if (!dispatch.IsCurrent)
                return;

            // Publish before source scrolling can raise LocationChanged or
            // invalidation callbacks. Cancellation/nested input owns any newer
            // carry; this old packet never writes it back after those callbacks.
            PortableNativeScrollCarry published = new(new(consumer), sourceHandle, consumer._window.PortableHandle,
                scroll.Stream, scroll.Generation, scroll.Unit, scroll.PointScale,
                frame with { X = nextX, Y = nextY }, remainderX, remainderY);
            _portableNativeScrollCarry = published;
            try
            {
                consumer.ApplyPortableNativeScroll(nextX, nextY, dispatch);
            }
            catch
            {
                if (ReferenceEquals(_portableNativeScrollCarry, published))
                    _portableNativeScrollCarry = null;
                throw;
            }

            return;
        }

        throw new PlatformNotSupportedException("No source scroll consumer supports every requested native axis.");
    }

    private static (int Position, double Remainder) AccumulatePortableScroll(int position, int minimum, double delta, double remainder)
    {
        if (!double.IsFinite(delta))
            throw new ArgumentException("Native scroll exceeds its source metric range.", nameof(delta));
        // Opposing motion cannot inherit debt in the previous direction.
        if (delta != 0 && Math.Sign(delta) != Math.Sign(remainder))
            remainder = 0;
        double total = delta + remainder;
        if (!double.IsFinite(total))
            throw new ArgumentException("Native scroll exceeds its source accumulation range.", nameof(delta));
        double integral = Math.Truncate(total);
        int next = (int)Math.Clamp(position + integral, minimum, 0);
        double tail = total - integral;
        if ((next == 0 && total > 0) || (next == minimum && total < 0))
            tail = 0;
        return (next, tail);
    }

    private sealed record PortableNativeScrollCarry(
        WeakReference<ScrollableControl> Target, LibreHandle SourceHandle, LibreHandle TargetHandle,
        LibreNativeScrollStream Stream, ulong Generation, LibreNativeScrollUnit Unit, double PointScale,
        ScrollableControl.PortableScrollFrame Frame, double X, double Y);
}
#endif
