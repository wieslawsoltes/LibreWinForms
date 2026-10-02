// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using ProGPU.Backend;
using Silk.NET.Windowing;

namespace LibreWinForms.ProGPU;

// One source-owned provider, never a handle-selected replacement. This consumes
// existing sessions; it deliberately cannot begin one or admit automatic modality.
internal sealed class NativeModalWindowLifetime(
    IWindow window, bool queueOnly, Action wake,
    INativeModalWindowSession? session = null)
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly INativeModalWindowSession _session = session ?? NativeModalWindowSession.s_instance;
    private PendingAction _pending;
    private bool _retiring;
    private bool _awaitingRelease;
    private bool _applying;
    private bool _closing;
    private int _dispatchDepth;
    private long _generation;
    private ExceptionDispatchInfo? _releaseFailure;

    internal void BeforeShow()
    {
        VerifyAccess();
        VerifyShowIntent();
        if (_awaitingRelease)
        {
            long generation = _generation;
            bool retained = _session.RetainsWindow(window);
            // Native identity getters can complete release or reenter source
            // intent. Neither an absent query nor this older Show may discard
            // an undelivered completion or a newer Hide/Close/retirement.
            VerifyShowIntent();
            if (_awaitingRelease && !retained)
                throw new InvalidOperationException("Native modal release completion is still outstanding.");
            if (generation != _generation)
                throw new InvalidOperationException("Native visibility changed during Show admission.");
        }

        ++_generation;
        _pending = PendingAction.None; // A newer Show supersedes only an old Hide.
    }

    private void VerifyShowIntent()
    {
        _releaseFailure?.Throw();
        ObjectDisposedException.ThrowIf(_retiring, this);
        if (_pending == PendingAction.Close || _closing)
            throw new InvalidOperationException("The native close request is awaiting modal release.");
    }

    internal void Hide() => Request(PendingAction.Hide);
    internal void Close() => Request(PendingAction.Close);

    internal void Retire()
    {
        VerifyAccess();
        _retiring = true;
        Request(PendingAction.Hide);
    }

    private void Request(PendingAction action)
    {
        VerifyAccess();
        if (action == PendingAction.Close && _closing) return;
        if (!_retiring && _pending == PendingAction.Close) return;
        ++_generation;
        _pending = action;
        ApplyPending();
    }

    internal void Pump()
    {
        VerifyAccess();
        if (_retiring) return;
        ApplyPending();
        if (_retiring) return;
        ++_dispatchDepth;
        try
        {
            // Owned panels drain only their own input. The source owner alone
            // chooses between the shared AppKit session and its ordinary poll.
            if (queueOnly || !_session.TryPumpEvents()) window.DoEvents();
        }
        finally { --_dispatchDepth; }
        ApplyPending();
    }

    internal bool CanRetire()
    {
        VerifyAccess();
        _releaseFailure?.Throw();
        if (!_retiring || _dispatchDepth != 0 || _applying) return false;
        ApplyPending();
        return !_awaitingRelease && _pending == PendingAction.None &&
            !_session.RetainsWindow(window);
    }

    private void ApplyPending()
    {
        _releaseFailure?.Throw();
        if (_applying || _awaitingRelease) return;
        _applying = true;
        try
        {
            // Native identity/visibility callbacks can replace source intent.
            // A bounded reconciliation must not publish an obsolete operation.
            for (int attempt = 0; _pending != PendingAction.None; ++attempt)
            {
                if (attempt == 8)
                    throw new InvalidOperationException("Native modal visibility did not reach stable source intent.");
                long generation = _generation;
                _awaitingRelease = true;
                bool releasing;
                try
                {
                    releasing = _session.TryReleaseWindow(window, OnReleased);
                    if (!releasing) _awaitingRelease = false;
                }
                catch (Exception failure)
                {
                    // If completion was not delivered, native End/identity
                    // release is uncertain. Keep the exact host; never retry End
                    // or treat an absent retention query as successful cleanup.
                    if (_awaitingRelease)
                        _releaseFailure = ExceptionDispatchInfo.Capture(failure);
                    throw;
                }

                if (_awaitingRelease) return;
                // Another completion callback can begin a fresh native session
                // before the original release call returns. Query again before
                // touching visibility, including synchronous completion.
                if (releasing) continue;
                if (generation != _generation) continue;
                PendingAction action = _pending;
                _pending = PendingAction.None;
                try
                {
                    ++_dispatchDepth;
                    if (action == PendingAction.Close)
                    {
                        _closing = true;
                        try { window.Close(); }
                        finally { _closing = false; }
                    }
                    else
                    {
                        // Owned Close hides before Closing; an already closing
                        // provider rejects visibility writes, even false. Its
                        // actual hidden state needs no second native transition.
                        bool visible = window.IsVisible;
                        if (generation == _generation && visible)
                        {
                            // The getter may acquire a new session without
                            // changing source intent. Preserve this request
                            // for native End before hiding its retained host.
                            bool retained = _session.RetainsWindow(window);
                            if (generation != _generation) continue;
                            if (retained)
                            {
                                _pending = action;
                                continue;
                            }

                            window.IsVisible = false;
                        }
                    }
                }
                catch
                {
                    // A failed visibility write remains retryable, unlike an
                    // uncertain native session End. Preserve newer nested intent.
                    if (generation == _generation && action == PendingAction.Hide)
                        _pending = action;
                    throw;
                }
                finally { --_dispatchDepth; }
            }
        }
        finally { _applying = false; }
    }

    private void OnReleased()
    {
        VerifyAccess();
        _awaitingRelease = false;
        // The window may already have left participant polling. Wake the owning
        // dispatcher so its retirement queue can retry after callbacks unwind.
        // Do not hide, destroy, run source handlers or poll from native completion.
        wake();
    }

    private void VerifyAccess()
    {
        if (_threadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Native modal ownership belongs to the creating source thread.");
    }

    private enum PendingAction { None, Hide, Close }
}

internal interface INativeModalWindowSession
{
    bool TryPumpEvents();
    bool RetainsWindow(IWindow window);
    bool TryReleaseWindow(IWindow window, Action completed);
}

internal sealed class NativeModalWindowSession : INativeModalWindowSession
{
    internal static readonly NativeModalWindowSession s_instance = new();
    public bool TryPumpEvents() => NativeWindowModalSession.TryPumpEvents();
    public bool RetainsWindow(IWindow window)
        => TryGetSessionWindow(window, out NativeWindowHandle handle) && NativeWindowModalSession.RetainsWindow(handle);
    public bool TryReleaseWindow(IWindow window, Action completed)
        => TryGetSessionWindow(window, out NativeWindowHandle handle) && NativeWindowModalSession.TryReleaseWindow(handle, completed);

    private static bool TryGetSessionWindow(IWindow window, out NativeWindowHandle handle)
    {
        handle = default;
        // Native.Cocoa is the actual provider's public native identity. Neither
        // an opaque IWindow.Handle nor retention itself identifies the provider.
        if (!NativeWindowModalSession.IsActive || !window.IsInitialized ||
            window.Native?.Cocoa is not { } cocoa || cocoa == 0) return false;
        handle = new(NativeWindowKind.Cocoa, cocoa, 0, "NSWindow");
        return true;
    }
}
