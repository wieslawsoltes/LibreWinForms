// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using Silk.NET.GLFW;

namespace LibreWinForms.ProGPU;

internal interface IGlfwCharacterCallbacks
{
    GlfwCallbacks.CharCallback? SetPlain(GlfwCallbacks.CharCallback? callback);
    GlfwCallbacks.CharModsCallback? SetModified(GlfwCallbacks.CharModsCallback? callback);
}

internal sealed unsafe class GlfwCharacterCallbacks(nint window) : IGlfwCharacterCallbacks
{
    public GlfwCallbacks.CharCallback? SetPlain(GlfwCallbacks.CharCallback? callback)
        => GlfwProvider.GLFW.Value.SetCharCallback((WindowHandle*)window, callback!);
    public GlfwCallbacks.CharModsCallback? SetModified(GlfwCallbacks.CharModsCallback? callback)
        => GlfwProvider.GLFW.Value.SetCharModsCallback((WindowHandle*)window, callback!);
}

/// <summary>Owns the character slots of one live, serialized GLFW window.</summary>
internal sealed unsafe class GlfwCharacterInput : IDisposable
{
    private readonly IGlfwCharacterCallbacks _callbacks;
    private readonly nint _window;
    private readonly NativeCharacterSequence _sequence;
    private readonly INativeCharacterTarget _target;
    private readonly Func<long> _timestamp;
    private readonly GlfwCallbacks.CharCallback _plain;
    private readonly GlfwCallbacks.CharModsCallback _modified;
    private readonly GlfwCallbacks.CharCallback? _previousPlain;
    private bool _attached;

    internal GlfwCharacterInput(IGlfwCharacterCallbacks callbacks, nint window,
        NativeCharacterSequence sequence, INativeCharacterTarget target, Func<long> timestamp)
    {
        if (window == 0)
            throw new ArgumentException("A live GLFW window is required.", nameof(window));
        _callbacks = callbacks;
        _window = window;
        _sequence = sequence;
        _target = target;
        _timestamp = timestamp;
        _plain = OnPlain;
        _modified = OnModified;
        GlfwCallbacks.CharModsCallback? previousModified = callbacks.SetModified(_modified);
        if (previousModified is not null)
        {
            callbacks.SetModified(previousModified);
            throw new InvalidOperationException("The native modified-character slot is already owned.");
        }

        try
        {
            _previousPlain = callbacks.SetPlain(_plain);
            _attached = true;
        }
        catch (Exception failure)
        {
            try { callbacks.SetModified(null); }
            catch (Exception cleanupFailure) { failure.Data[nameof(GlfwCharacterInput)] = cleanupFailure; }
            throw;
        }
    }

    private void OnModified(WindowHandle* window, uint scalar, KeyModifiers modifiers)
    {
        if (_attached && (nint)window == _window && _target.IsAlive)
            _sequence.Modified(_target, scalar, ConvertModifiers(modifiers), _timestamp());
    }

    private void OnPlain(WindowHandle* window, uint scalar)
    {
        if (!_attached || (nint)window != _window || !_target.IsAlive)
            return;
        _sequence.Plain(_target, scalar);
        // Keep Silk's ordinary character subscribers without using its lossy
        // uint-to-char conversion as the source of our own text input.
        if (_attached && _target.IsAlive)
            _previousPlain?.Invoke(window, scalar);
    }

    internal static LibreInputModifiers ConvertModifiers(KeyModifiers modifiers)
    {
        LibreInputModifiers result = LibreInputModifiers.None;
        if ((modifiers & KeyModifiers.Shift) != 0) result |= LibreInputModifiers.Shift;
        if ((modifiers & KeyModifiers.Control) != 0) result |= LibreInputModifiers.Control;
        if ((modifiers & KeyModifiers.Alt) != 0) result |= LibreInputModifiers.Alt;
        if ((modifiers & KeyModifiers.Super) != 0) result |= LibreInputModifiers.Meta;
        return result;
    }

    public void Dispose()
    {
        if (!_attached)
            return;
        _attached = false;
        _sequence.Cancel(_target);
        Exception? failure = null;
        try
        {
            GlfwCallbacks.CharCallback? displaced = _callbacks.SetPlain(_previousPlain);
            if (displaced != _plain)
                _callbacks.SetPlain(displaced);
        }
        catch (Exception error) { failure = error; }
        try
        {
            GlfwCallbacks.CharModsCallback? displaced = _callbacks.SetModified(null);
            if (displaced != _modified)
                _callbacks.SetModified(displaced);
        }
        catch (Exception error) { failure ??= error; }
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
