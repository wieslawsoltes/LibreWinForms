// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

internal interface INativeCharacterTarget
{
    bool IsAlive { get; }
    void Input(in LibreInputEvent input);
}

/// <summary>Pairs GLFW modified/plain character callbacks without inventing keyboard-layout text.</summary>
internal sealed class NativeCharacterSequence
{
    [ThreadStatic]
    private static NativeCharacterSequence? s_current;

    private Pending? _pending;
    private readonly Queue<(Pending Character, LibreInputEventKind Kind)> _ready = new();

    internal static NativeCharacterSequence Current => s_current ??= new();

    internal void Modified(INativeCharacterTarget target, uint scalar, LibreInputModifiers modifiers, long timestamp)
    {
        ValidateScalar(scalar);
        // GLFW calls Modified immediately before Plain, when Plain applies.
        // Never invoke application code between them: a nested event pump must
        // not consume the outer character before its plain callback arrives.
        QueueSystem(_pending);
        _pending = new(target, scalar, modifiers, timestamp);
    }

    internal void Plain(INativeCharacterTarget target, uint scalar)
    {
        ValidateScalar(scalar);
        Pending? pending = _pending;
        if (pending is null || !ReferenceEquals(pending.Target, target) || pending.Scalar != scalar)
            throw new InvalidOperationException("The native plain character has no matching modified-character callback.");
        _pending = null;
        _ready.Enqueue((pending, LibreInputEventKind.TextInput));
        Drain();
    }

    internal void Flush()
    {
        Pending? pending = _pending;
        _pending = null;
        QueueSystem(pending);
        Drain();
    }

    internal void Cancel(INativeCharacterTarget target)
    {
        if (ReferenceEquals(_pending?.Target, target))
            _pending = null;
        // Queued characters also belong to this exact native owner lifetime.
        int remaining = _ready.Count;
        while (remaining-- > 0)
        {
            var ready = _ready.Dequeue();
            if (!ReferenceEquals(ready.Character.Target, target))
                _ready.Enqueue(ready);
        }
    }

    private void QueueSystem(Pending? pending)
    {
        if (pending is not null && (pending.Modifiers & LibreInputModifiers.Alt) != 0
            && (pending.Modifiers & (LibreInputModifiers.Control | LibreInputModifiers.Meta)) == 0)
            _ready.Enqueue((pending, LibreInputEventKind.SystemTextInput));
    }

    private void Drain()
    {
        // Remove before callbacks. A nested pump may drain subsequent ready
        // characters before its later input without duplicating this event.
        while (_ready.TryDequeue(out var ready))
            Emit(ready.Character, ready.Kind);
    }

    private static void Emit(Pending pending, LibreInputEventKind kind)
    {
        if (pending.Target.IsAlive)
            pending.Target.Input(new LibreInputEvent(kind, pending.Timestamp, pending.Modifiers,
                LibreKey.Unknown, char.ConvertFromUtf32((int)pending.Scalar), default, default, LibrePointerButton.None));
    }

    private static void ValidateScalar(uint scalar)
    {
        if (!Rune.IsValid(scalar))
            throw new ArgumentOutOfRangeException(nameof(scalar));
    }

    private sealed record Pending(INativeCharacterTarget Target, uint Scalar, LibreInputModifiers Modifiers, long Timestamp);
}
