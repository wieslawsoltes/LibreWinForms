// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using LibreWinForms.Platform;

namespace System.Windows.Forms;

public partial class Control
{
    private PortableNativeScrollCarry? _portableNativeScrollCarry;
    private PortableNativeScrollGesture? _portableNativeScrollGesture;

    private static void ValidatePortableNativeScroll(in LibreInputEvent input)
    {
        if (input.NativeScroll is not { } scroll
            || input.NativePointer is not { Kind: LibreNativePointerKind.Scroll, Button: -1, ClickCount: 0 } native
            || !double.IsFinite(native.X) || !double.IsFinite(native.Y)
            || !double.IsFinite(native.Timestamp) || native.Timestamp < 0
            || ((int)native.Modifiers & ~255) != 0
            || (int)input.Modifiers != ((int)native.Modifiers & 15)
            || input.Button != LibrePointerButton.None || input.Delta != default)
            throw new ArgumentException("Invalid source native-scroll metadata.", nameof(input));
        scroll.Validate();
    }

    private void RetirePortableNativeScroll(PortableNativeScrollGesture? expected = null)
    {
        if (expected is null || ReferenceEquals(_portableNativeScrollGesture, expected))
        {
            _portableNativeScrollGesture = null;
            _portableNativeScrollCarry = null;
        }
    }

    private PortableNativeScrollGesture? PreparePortableNativeScroll(in LibreNativeScrollMetadata scroll)
    {
        PortableNativeScrollGesture? previous = _portableNativeScrollGesture;
        if (previous is not null && !previous.Matches(this, scroll))
        {
            RetirePortableNativeScroll(previous);
            previous = null;
        }

        bool momentum = scroll.MomentumPhase != 0;
        uint phase = momentum ? scroll.MomentumPhase : scroll.Phase;
        if (phase == 16)
        {
            // Cancellation never executes the packet's remaining vector.
            // Retire before any source callback can install another gesture.
            if (previous is not null && (previous.Momentum == momentum || !momentum))
                RetirePortableNativeScroll(previous);
            return null;
        }

        if (momentum && phase != 1)
        {
            if (previous is null || !previous.Momentum || previous.Ended)
                return null; // An obsolete momentum tail must not hit-test anew.
        }
        else if (previous is null || previous.Momentum != momentum || previous.Ended || phase is 1 or 32)
        {
            // Only the explicit direct-to-momentum handoff shares fractions.
            // Its new cancellation identity cannot retire already applied work.
            bool handoff = momentum && previous is { Momentum: false };
            if (!handoff)
                _portableNativeScrollCarry = null;
            previous = new(this, scroll, momentum, handoff ? previous!.Fractions : new());
            _portableNativeScrollGesture = previous;
        }

        // Mark the final packet before callbacks. Nested stale Changed input
        // cannot revive this ended momentum, while this packet may still apply.
        previous!.Ended = phase == 8;
        return previous;
    }

    private void DispatchPortableNativeScroll(Control target, in LibreInputEvent input, PortableNativeScrollGesture gesture)
    {
        LibreNativeScrollMetadata scroll = input.NativeScroll!.Value;
        LibreHandle sourceHandle = _window.PortableHandle;
        bool pinned = gesture.Momentum && scroll.MomentumPhase != 1;
        Control? first = pinned ? gesture.GetConsumer(this, target) : target;
        if (pinned && first is null)
        {
            RetirePortableNativeScroll(gesture);
            return;
        }

        for (Control? current = first; current is not null; current = pinned ? null : current.ParentInternal)
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
                && ReferenceEquals(carry.Fractions, gesture.Fractions)
                && carry.Unit == scroll.Unit && carry.PointScale == scroll.PointScale && carry.Frame == frame)
            {
                x = carry.X;
                y = carry.Y;
            }

            double dx = scroll.X * (scroll.Unit == LibreNativeScrollUnit.Points ? scroll.PointScale : frame.SmallX);
            double dy = scroll.Y * (scroll.Unit == LibreNativeScrollUnit.Points ? scroll.PointScale : frame.SmallY);
            (int nextX, double remainderX) = AccumulatePortableScroll(frame.X, frame.MinX, dx, x);
            (int nextY, double remainderY) = AccumulatePortableScroll(frame.Y, frame.MinY, dy, y);
            if (!dispatch.IsCurrent || !ReferenceEquals(_portableNativeScrollGesture, gesture))
                return;

            // Publish before source scrolling can raise LocationChanged or
            // invalidation callbacks. Cancellation/nested input owns any newer
            // carry; this old packet never writes it back after those callbacks.
            PortableNativeScrollCarry published = new(new(consumer), sourceHandle, consumer._window.PortableHandle,
                scroll.Stream, scroll.Generation, gesture.Fractions, scroll.Unit, scroll.PointScale,
                frame with { X = nextX, Y = nextY }, remainderX, remainderY);
            if (gesture.Momentum && !pinned)
                gesture.Pin(target, consumer);
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
        LibreNativeScrollStream Stream, ulong Generation, PortableNativeScrollFractions Fractions,
        LibreNativeScrollUnit Unit, double PointScale,
        ScrollableControl.PortableScrollFrame Frame, double X, double Y);

    private sealed class PortableNativeScrollFractions { }

    private sealed class PortableNativeScrollGesture
    {
        private readonly LibreHandle _sourceHandle;
        private readonly LibreNativeScrollStream _stream;
        private readonly ulong _generation;
        private readonly LibreNativeScrollProtocol _protocol;
        private WeakReference<Control>? _target;
        private LibreHandle _targetHandle;
        private WeakReference<ScrollableControl>? _consumer;
        private LibreHandle _consumerHandle;

        internal PortableNativeScrollGesture(Control source, in LibreNativeScrollMetadata scroll, bool momentum,
            PortableNativeScrollFractions fractions)
        {
            _sourceHandle = source._window.PortableHandle;
            _stream = scroll.Stream;
            _generation = scroll.Generation;
            _protocol = scroll.Protocol;
            Momentum = momentum;
            Fractions = fractions;
        }

        internal bool Momentum { get; }
        internal bool Ended { get; set; }
        internal PortableNativeScrollFractions Fractions { get; }

        internal bool Matches(Control source, in LibreNativeScrollMetadata scroll)
            => source._window.PortableHandle == _sourceHandle && ReferenceEquals(scroll.Stream, _stream)
                && scroll.Generation == _generation && scroll.Protocol == _protocol;

        internal void Pin(Control target, ScrollableControl consumer)
        {
            _target = new(target);
            _targetHandle = target._window.PortableHandle;
            _consumer = new(consumer);
            _consumerHandle = consumer._window.PortableHandle;
        }

        internal Control? GetTarget(Control source)
            => _target is not null && _target.TryGetTarget(out Control? target)
                && target.IsHandleCreated && source.IsCurrentPortablePointerTarget(target, _targetHandle) ? target : null;

        internal ScrollableControl? GetConsumer(Control source, Control target)
            => _consumer is not null && _consumer.TryGetTarget(out ScrollableControl? consumer)
                && consumer.IsHandleCreated && source.IsCurrentPortablePointerTarget(consumer, _consumerHandle)
                && (ReferenceEquals(consumer, target) || consumer.Contains(target)) ? consumer : null;
    }
}
#endif
