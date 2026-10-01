// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using ProGPU.Backend;
using Silk.NET.Input;

namespace LibreWinForms.ProGPU;

internal interface INativePointerTarget
{
    bool IsCurrent(NativePointerInput subscription);
    LibrePoint MapPoint(double x, double y);
    double NativePointScale => throw new PlatformNotSupportedException("This source has no native point-vector mapping.");
    void FlushCharacters();
    ProGpuDragCancellation? PrepareCancellation();
    void Input(in LibreInputEvent input);
}

/// <summary>Owns one provider-native stream, never its parallel Silk projection.</summary>
internal sealed class NativePointerInput : IDisposable
{
    private readonly INativePointerInputContext _context;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private INativePointerTarget? _target;
    private ulong _delivery;
    private readonly LibreNativeScrollStream _scrollStream = new();

    internal NativePointerInput(INativePointerInputContext context, INativePointerTarget target)
    {
        _context = context;
        _target = target;
        try { context.PointerEvent += OnPointer; }
        catch (Exception failure)
        {
            _target = null;
            try { context.PointerEvent -= OnPointer; }
            catch (Exception cleanup) { failure.Data["NativePointerUnsubscribe"] = cleanup; }
            throw;
        }
    }

    internal bool Owns(IInputContext? context) => ReferenceEquals(context, _context);

    internal static bool RequiresGlfwCharacters(IInputContext context, bool popup, nint glfwWindow)
    {
        if (glfwWindow != 0) return true;
        // A non-key provider has no character slots; the actual Form keeps its
        // own keyboard/character owner. Unknown keyboard providers still reject.
        if (popup && context is INativePointerInputContext && context.Keyboards.Count == 0)
            return false;
        throw new PlatformNotSupportedException("Native character input requires the owned GLFW window or a keyboardless native popup provider.");
    }

    private bool IsCurrent(INativePointerTarget target, ulong generation, ulong delivery)
        => Environment.CurrentManagedThreadId == _thread && ReferenceEquals(_target, target)
            && _delivery == delivery && target.IsCurrent(this)
            && _context.InputGeneration == generation
            && _delivery == delivery && ReferenceEquals(_target, target);

    private void OnPointer(NativePointerEvent value)
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Native pointer input belongs to its source dispatcher thread.");
        INativePointerTarget? target = _target;
        if (target is null || !target.IsCurrent(this)) return;
        ulong generation = _context.InputGeneration;
        // CurrentEvent establishes this callback's actual provider delivery.
        // A saved callback cannot replay an old packet outside that scope.
        if (_context.CurrentEvent is not NativePointerEvent current || current != value) return;
        ulong delivery = unchecked(++_delivery);
        LibreInputEvent input = Translate(value, target, generation);
        if (!IsCurrent(target, generation, delivery)) return;
        if (input.Kind == LibreInputEventKind.PointerCancel)
        {
            // Commit drag termination before character/source callbacks can
            // reenter; notify only the captured old target after source retirement.
            ProGpuDragCancellation? cancellation = target.PrepareCancellation();
            ProGpuDragCancellation.Deliver(target.FlushCharacters,
                () => { if (IsCurrent(target, generation, delivery)) target.Input(input); }, cancellation);
            return;
        }

        target.FlushCharacters();
        if (IsCurrent(target, generation, delivery)) target.Input(input);
    }

    private LibreInputEvent Translate(in NativePointerEvent value, INativePointerTarget target, ulong generation)
    {
        bool scroll = value.Kind == NativePointerEventKind.Scroll;
        bool buttonEvent = value.Kind is NativePointerEventKind.Down or NativePointerEventKind.Up or NativePointerEventKind.Drag;
        if (value.Kind is < NativePointerEventKind.Move or > NativePointerEventKind.Cancel
            || !double.IsFinite(value.X) || !double.IsFinite(value.Y)
            || !double.IsFinite(value.Timestamp) || value.Timestamp < 0
            || ((int)value.Modifiers & ~255) != 0 || value.ClickCount < 0
            || (buttonEvent ? value.Button is < 0 or >= 64 : value.Button != -1 || value.ClickCount != 0)
            || (!scroll && (value.ScrollX != 0 || value.ScrollY != 0 || value.ScrollPhase != 0 || value.MomentumPhase != 0
                || value.ScrollUnit != NativePointerScrollUnit.Lines || value.ScrollProtocol != NativePointerScrollProtocol.Unspecified)))
            throw new ArgumentException("Invalid native pointer metadata.", nameof(value));
        if (scroll)
        {
            if (!double.IsFinite(value.ScrollX) || !double.IsFinite(value.ScrollY)
                || value.ScrollUnit is < NativePointerScrollUnit.Lines or > NativePointerScrollUnit.Points
                || value.ScrollProtocol is < NativePointerScrollProtocol.Unspecified or > NativePointerScrollProtocol.AppKit)
                throw new ArgumentException("Invalid native scroll metadata.", nameof(value));
        }

        if ((value.Kind is NativePointerEventKind.Down or NativePointerEventKind.Up) && value.Button > 4)
            throw new PlatformNotSupportedException("The native button has no canonical source button identity.");

        LibreNativePointerKind kind = value.Kind switch
        {
            NativePointerEventKind.Move => LibreNativePointerKind.Move,
            NativePointerEventKind.Drag => LibreNativePointerKind.Drag,
            NativePointerEventKind.Down => LibreNativePointerKind.Down,
            NativePointerEventKind.Up => LibreNativePointerKind.Up,
            NativePointerEventKind.Enter => LibreNativePointerKind.Enter,
            NativePointerEventKind.Leave => LibreNativePointerKind.Leave,
            NativePointerEventKind.Cancel => LibreNativePointerKind.Cancel,
            NativePointerEventKind.Scroll => LibreNativePointerKind.Scroll,
            _ => throw new ArgumentException("Unknown native pointer kind.", nameof(value)),
        };
        LibreInputEventKind canonicalKind = kind switch
        {
            LibreNativePointerKind.Down => LibreInputEventKind.PointerDown,
            LibreNativePointerKind.Up => LibreInputEventKind.PointerUp,
            LibreNativePointerKind.Leave => LibreInputEventKind.PointerLeave,
            LibreNativePointerKind.Cancel => LibreInputEventKind.PointerCancel,
            LibreNativePointerKind.Scroll => LibreInputEventKind.PointerScroll,
            _ => LibreInputEventKind.PointerMove,
        };
        LibrePointerButton button = canonicalKind is LibreInputEventKind.PointerDown or LibreInputEventKind.PointerUp
            ? (LibrePointerButton)(value.Button + 1) : LibrePointerButton.None;
        long timestamp = checked((long)Math.Round(value.Timestamp * TimeSpan.TicksPerSecond));
        LibreNativeScrollMetadata? nativeScroll = scroll ? new(value.ScrollX, value.ScrollY,
            (LibreNativeScrollUnit)value.ScrollUnit, (LibreNativeScrollProtocol)value.ScrollProtocol,
            value.ScrollPhase, value.MomentumPhase, 1, _scrollStream, generation) : null;
        nativeScroll?.Validate();
        if (nativeScroll is { Unit: LibreNativeScrollUnit.Points } precise)
        {
            nativeScroll = precise with { PointScale = target.NativePointScale };
            nativeScroll.Value.Validate();
        }

        LibrePoint position = target.MapPoint(value.X, value.Y);
        return new(canonicalKind, timestamp, (LibreInputModifiers)((int)value.Modifiers & 15),
            LibreKey.Unknown, null, position, default, button)
        {
            NativePointer = new(kind, value.X, value.Y, value.Timestamp, value.Button, value.ClickCount,
                (LibreNativePointerModifiers)value.Modifiers),
            NativeScroll = nativeScroll,
        };
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Native pointer input belongs to its source dispatcher thread.");
        if (_target is null) return;
        _target = null; // Retire before an event accessor can call back or throw.
        _context.PointerEvent -= OnPointer;
    }
}
