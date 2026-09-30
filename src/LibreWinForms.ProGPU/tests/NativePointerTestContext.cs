// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Backend;
using Silk.NET.Input;

namespace LibreWinForms.ProGPU.Tests;

// Typed provider fixture only; never selected by application window creation.
internal sealed class NativePointerTestContext : IInputContext, INativePointerInputContext
{
    internal Action<NativePointerEvent>? Callbacks { get; private set; }
    internal Exception? SubscribeFailure { get; set; }
    internal Exception? UnsubscribeFailure { get; set; }
    internal int Subscribers { get; private set; }
    public nint Handle => 123;
    public IReadOnlyList<IKeyboard> Keyboards { get; set; } = [];
    public IReadOnlyList<IMouse> Mice => [];
    public IReadOnlyList<IGamepad> Gamepads => [];
    public IReadOnlyList<IJoystick> Joysticks => [];
    public IReadOnlyList<IInputDevice> OtherDevices => [];
    public NativePointerEvent? CurrentEvent { get; private set; }
    public ulong InputGeneration { get; set; } = 1;
    public event Action<IInputDevice, bool>? ConnectionChanged { add { } remove { } }
    public event Action<NativePointerEvent>? PointerEvent
    {
        add
        {
            Callbacks += value;
            Subscribers++;
            if (SubscribeFailure is Exception failure) throw failure;
        }
        remove
        {
            if (UnsubscribeFailure is Exception failure) throw failure;
            Callbacks -= value;
            Subscribers--;
        }
    }

    internal void Emit(NativePointerEvent value)
    {
        NativePointerEvent? prior = CurrentEvent;
        CurrentEvent = value;
        try { Callbacks?.Invoke(value); }
        finally { CurrentEvent = prior; }
    }

    public void Dispose()
    {
        if (Subscribers != 0) throw new InvalidOperationException("Unsubscribe before disposing provider input.");
    }
}
