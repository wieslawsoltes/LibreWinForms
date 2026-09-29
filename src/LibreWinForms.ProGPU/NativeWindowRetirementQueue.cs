// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using ProGPU.Backend;
using Silk.NET.Windowing;

namespace LibreWinForms.ProGPU;

internal sealed class NativeWindowRetirementQueue(Func<IWindow, bool>? tryDispose = null)
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly Func<IWindow, bool> _tryDispose = tryDispose ?? NativeWindowLifetime.TryDispose;
    private readonly List<Retirement> _pending = [];
    private volatile int _pendingCount;
    private bool _draining;

    internal bool HasPending => _pendingCount != 0;

    internal void Retire(IWindow window, Action? releaseRenderingResources = null)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(window);
        if (!_pending.Any(candidate => ReferenceEquals(candidate.Window, window)))
        {
            // Acquire ownership before invoking a reentrant or throwing provider.
            _pending.Add(new Retirement(window, releaseRenderingResources));
            _pendingCount = _pending.Count;
        }

        Drain();
    }

    internal void Drain()
    {
        VerifyAccess();
        if (_draining || !HasPending)
            return;

        _draining = true;
        ExceptionDispatchInfo? failure = null;
        List<Exception>? laterFailures = null;
        try
        {
            // One attempt per original entry per drain. Reentrant additions wait
            // for the next host boundary; they cannot create a recursive poll.
            foreach (Retirement retirement in _pending.ToArray())
            {
                try
                {
                    if (!TryComplete(retirement))
                        continue;

                    _pending.Remove(retirement);
                    _pendingCount = _pending.Count;
                }
                catch (Exception exception)
                {
                    if (failure is null)
                        failure = ExceptionDispatchInfo.Capture(exception);
                    else
                        (laterFailures ??= []).Add(exception);
                }
            }
        }
        finally
        {
            _draining = false;
        }

        if (laterFailures is not null)
            failure!.SourceException.Data[nameof(NativeWindowRetirementQueue)] = laterFailures;
        failure?.Throw();
    }

    private bool TryComplete(Retirement retirement)
    {
        ExceptionDispatchInfo? renderingFailure = null;
        try
        {
            if (!retirement.RenderingResourcesReleased)
            {
                retirement.ReleaseRenderingResources?.Invoke();
                retirement.RenderingResourcesReleased = true;
            }
        }
        catch (Exception failure) { renderingFailure = ExceptionDispatchInfo.Capture(failure); }

        bool retired = false;
        try { retired = _tryDispose(retirement.Window); }
        catch (Exception cleanup) when (renderingFailure is not null)
        {
            renderingFailure.SourceException.Data["NativeWindowDisposal"] = cleanup;
        }

        // A failed renderer owner stays retained even if native retirement
        // succeeded; conversely, native disposal still blocks input/hides when
        // a renderer failed and its lease must protect the surviving view.
        renderingFailure?.Throw();
        return retired;
    }

    private void VerifyAccess()
    {
        if (_threadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Native window retirement belongs to the source dispatcher thread.");
    }

    private sealed class Retirement(IWindow window, Action? releaseRenderingResources)
    {
        internal IWindow Window { get; } = window;
        internal Action? ReleaseRenderingResources { get; } = releaseRenderingResources;
        internal bool RenderingResourcesReleased { get; set; }
    }
}
