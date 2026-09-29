// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

internal interface IProGpuDragInputWindow
{
    LibreHandle Handle { get; }
    LibreRectangle Bounds { get; }
    bool IsDisposed { get; }
    bool CheckAccess();
}

// One router belongs to one SilkWindowService. Its inventory contains the actual
// source windows registered by that service, never hit targets or native IDs.
internal sealed class ProGpuDragInputRouter(ILibreHandleRegistry handles) : IProGpuDragInputSource
{
    private readonly Lock _gate = new();
    private readonly HashSet<IProGpuDragInputWindow> _windows = [];
    private readonly IProGpuDragInputWindow?[] _buttonOwners = new IProGpuDragInputWindow?[3];
    private Registration? _registration;
    private ProGpuDragInput _lastInput;
    private int _keyState;

    private sealed record Registration(IProGpuDragInputSink Sink, IProGpuDragInputWindow? Window, LibreHandle Handle);

    internal void Register(IProGpuDragInputWindow window)
    {
        lock (_gate) { _windows.Add(window); }
    }

    internal void Unregister(IProGpuDragInputWindow window)
    {
        lock (_gate)
        {
            _windows.Remove(window);
            RetireButtons(window);
        }
    }

    private bool IsCurrent(IProGpuDragInputWindow window, LibreHandle handle)
        => _windows.Contains(window) && !window.IsDisposed && window.CheckAccess()
            && window.Handle == handle && handle.Kind == LibreHandleKind.Window
            && handles.TryGet(handle, out IProGpuDragInputWindow? registered)
            && ReferenceEquals(window, registered);

    public ProGpuDragInput BeginDrag(LibreHandle sourceWindow, IProGpuDragInputSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (_gate)
        {
            if (_registration is not null)
                throw new InvalidOperationException("A Silk drag operation is already active.");

            IProGpuDragInputWindow? window = null;
            if (!sourceWindow.IsNull && (!handles.TryGet(sourceWindow, out window)
                || !IsCurrent(window, sourceWindow)))
                throw new ArgumentException("The drag source window must be live on this service and dispatcher.", nameof(sourceWindow));

            // A legacy request remains usable, but has no native cancellation
            // owner. Never borrow the most recent pointer window for it.
            _registration = new(sink, window, sourceWindow);
            return _lastInput;
        }
    }

    public void EndDrag(IProGpuDragInputSink sink)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_registration?.Sink, sink)) _registration = null;
        }
    }

    internal ProGpuDragCancellation? PreparePointerCancellation(IProGpuDragInputWindow window)
    {
        lock (_gate)
        {
            if (!IsCurrent(window, window.Handle)) return null;
            RetireButtons(window); // Keep position, modifiers and other windows' buttons.
            Registration? current = _registration;
            if (current is null || !ReferenceEquals(current.Window, window) || current.Handle != window.Handle)
                return null;

            // This typed method commits state only: application DragLeave is
            // deferred until after the source's capture/press/hover retirement.
            return current.Sink.PreparePointerCancellation();
        }
    }

    private void RetireButtons(IProGpuDragInputWindow window)
    {
        for (int i = 0; i < _buttonOwners.Length; i++)
        {
            if (ReferenceEquals(_buttonOwners[i], window))
            {
                _buttonOwners[i] = null;
                _keyState &= ~ButtonMask(i);
            }
        }

        _lastInput = _lastInput with { KeyState = _keyState };
    }

    private static int ButtonMask(int index) => index switch { 0 => 1, 1 => 2, _ => 16 };

    internal bool Record(IProGpuDragInputWindow window, in LibreInputEvent inputEvent)
    {
        // Retirement is never a sample, including direct internal callers.
        if (inputEvent.Kind is LibreInputEventKind.PointerLeave or LibreInputEventKind.PointerCancel)
            return false;

        ProGpuDragInput input;
        IProGpuDragInputSink? sink;
        lock (_gate)
        {
            if (!IsCurrent(window, window.Handle)) return false;
            int index = inputEvent.Button switch
            {
                LibrePointerButton.Primary => 0,
                LibrePointerButton.Secondary => 1,
                LibrePointerButton.Middle => 2,
                _ => -1,
            };
            if (index >= 0 && inputEvent.Kind == LibreInputEventKind.PointerDown)
            {
                _keyState |= ButtonMask(index);
                _buttonOwners[index] = window;
            }
            else if (index >= 0 && inputEvent.Kind == LibreInputEventKind.PointerUp)
            {
                _keyState &= ~ButtonMask(index);
                _buttonOwners[index] = null;
            }

            _keyState &= ~(0x0004 | 0x0008 | 0x0020);
            if (inputEvent.Modifiers.HasFlag(LibreInputModifiers.Shift)) _keyState |= 0x0004;
            if (inputEvent.Modifiers.HasFlag(LibreInputModifiers.Control)) _keyState |= 0x0008;
            if (inputEvent.Modifiers.HasFlag(LibreInputModifiers.Alt)) _keyState |= 0x0020;

            ProGpuDragInputKind kind = inputEvent.Kind == LibreInputEventKind.KeyDown && inputEvent.Key == LibreKey.Escape
                ? ProGpuDragInputKind.Escape : ProGpuDragInputKind.Pointer;
            if (inputEvent.Kind is LibreInputEventKind.PointerDown or LibreInputEventKind.PointerUp or LibreInputEventKind.PointerMove)
            {
                LibreRectangle bounds = window.Bounds;
                _lastInput = new(kind, new(checked(bounds.X + inputEvent.Position.X), checked(bounds.Y + inputEvent.Position.Y)), _keyState);
            }
            else
            {
                _lastInput = _lastInput with { Kind = kind, KeyState = _keyState };
            }

            input = _lastInput;
            sink = _registration?.Sink;
        }

        return sink?.Input(input) == true;
    }
}
