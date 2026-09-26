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

    internal static NativeCharacterSequence Current => s_current ??= new();

    internal void Modified(INativeCharacterTarget target, uint scalar, LibreInputModifiers modifiers, long timestamp)
    {
        ValidateScalar(scalar);
        Pending? previous = _pending;
        // Publish before delivering the previous event. A nested native event
        // can then consume or replace this exact pending generation safely.
        _pending = new(target, scalar, modifiers, timestamp);
        EmitSystem(previous);
    }

    internal void Plain(INativeCharacterTarget target, uint scalar)
    {
        ValidateScalar(scalar);
        Pending? pending = _pending;
        if (pending is null || !ReferenceEquals(pending.Target, target) || pending.Scalar != scalar)
            throw new InvalidOperationException("The native plain character has no matching modified-character callback.");
        _pending = null;
        Emit(pending, LibreInputEventKind.TextInput);
    }

    internal void Flush()
    {
        Pending? pending = _pending;
        _pending = null;
        EmitSystem(pending);
    }

    internal void Cancel(INativeCharacterTarget target)
    {
        if (ReferenceEquals(_pending?.Target, target))
            _pending = null;
    }

    private static void EmitSystem(Pending? pending)
    {
        if (pending is not null && (pending.Modifiers & LibreInputModifiers.Alt) != 0
            && (pending.Modifiers & (LibreInputModifiers.Control | LibreInputModifiers.Meta)) == 0)
            Emit(pending, LibreInputEventKind.SystemTextInput);
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
