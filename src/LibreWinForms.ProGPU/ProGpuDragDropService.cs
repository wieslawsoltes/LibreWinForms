// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

internal enum ProGpuDragInputKind
{
    Pointer,
    Escape,
}

internal readonly record struct ProGpuDragInput(
    ProGpuDragInputKind Kind,
    LibrePoint ScreenPosition,
    int KeyState);

internal interface IProGpuDragInputSink
{
    bool Input(in ProGpuDragInput input);

    ProGpuDragCancellation? PreparePointerCancellation();
}

internal interface IProGpuDragInputSource
{
    ProGpuDragInput BeginDrag(LibreHandle sourceWindow, IProGpuDragInputSink sink);

    void EndDrag(IProGpuDragInputSink sink);
}

/// <summary>
/// Runs an application-local drag operation over the live Silk window inventory. Canonical
/// WinForms remains responsible for logical hit testing and event dispatch.
/// </summary>
public sealed class ProGpuDragDropService : ILibreDragDropService
{
    private const int MouseButtonMask = 0x0001 | 0x0002 | 0x0010;
    private const int ControlKey = 0x0008;

    private readonly ProGpuDispatcher _dispatcher;
    private readonly IProGpuDragInputSource _input;
    private readonly Lock _targetsLock = new();
    private readonly HashSet<LibreHandle> _targets = [];
    private ILibreDragDropSession? _session;
    private LibreDragDropRequest? _request;
    private LibreHandle _target;
    private LibreDragDropEffects _effect;
    private LibreDragDropEffects _result;
    private bool _complete;
    private DragInputLease? _inputLease;

    public ProGpuDragDropService(ProGpuDispatcher dispatcher, SilkWindowService windows)
        : this(dispatcher, (IProGpuDragInputSource)windows)
    {
    }

    internal ProGpuDragDropService(ProGpuDispatcher dispatcher, IProGpuDragInputSource input)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    public bool IsSupported => true;

    public void SetTargetEnabled(LibreHandle target, bool enabled)
    {
        if (target.IsNull)
        {
            return;
        }

        lock (_targetsLock)
        {
            if (enabled)
            {
                _targets.Add(target);
            }
            else
            {
                _targets.Remove(target);
            }
        }
    }

    public LibreDragDropEffects DoDragDrop(LibreDragDropRequest request, ILibreDragDropSession session)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException("A ProGPU drag operation must begin on its owning UI thread.");
        }

        if (_session is not null)
        {
            throw new InvalidOperationException("A ProGPU drag operation is already active on this platform instance.");
        }

        _session = session;
        _request = request;
        _target = default;
        _effect = ChooseEffect(request.AllowedEffects, keyState: 0);
        _result = LibreDragDropEffects.None;
        _complete = false;

        DragInputLease lease = new(this);
        _inputLease = lease;
        bool registered = false;
        Exception? failure = null;
        try
        {
            ProGpuDragInput initial = _input.BeginDrag(request.SourceWindow, lease);
            registered = true;
            ProcessInput(initial);
            if (!_complete && (initial.KeyState & MouseButtonMask) == 0)
            {
                Cancel();
            }

            if (!_complete)
            {
                _dispatcher.RunNested(() => !_complete, CancellationToken.None);
            }

            return _result & request.AllowedEffects;
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            // Revoke callbacks before external teardown, even if registration or
            // source dispatch failed. Old queued input cannot enter a later drag.
            lease.Detach();
            try
            {
                if (registered) _input.EndDrag(lease);
            }
            catch (Exception cleanup) when (failure is not null)
            {
                failure.Data["DragInputRelease"] = cleanup;
            }
            finally
            {
                _inputLease = null;
                _session = null;
                _request = null;
                _target = default;
                _effect = LibreDragDropEffects.None;
                _complete = true;
            }
        }
    }

    private bool ReceiveInput(DragInputLease lease, in ProGpuDragInput input)
    {
        if (!lease.IsAttached)
        {
            return false;
        }

        if (_dispatcher.CheckAccess())
        {
            return ProcessCurrentInput(lease, input);
        }

        ProGpuDragInput captured = input;
        bool handled = false;
        _dispatcher.Send(() => handled = ProcessCurrentInput(lease, captured));
        return handled;
    }

    private bool ProcessCurrentInput(DragInputLease lease, in ProGpuDragInput input)
    {
        if (!ReferenceEquals(_inputLease, lease) || !lease.IsAttached ||
            _session is null || _request is null || _complete)
            return false;
        ProcessInput(input);
        return true;
    }

    private sealed class DragInputLease(ProGpuDragDropService owner) : IProGpuDragInputSink
    {
        private ProGpuDragDropService? _owner = owner;
        private ProGpuDragCancellation? _cancellation;

        internal bool IsAttached => Volatile.Read(ref _owner) is not null;
        internal void Detach()
        {
            Interlocked.Exchange(ref _owner, null);
            _cancellation?.Retire();
            _cancellation = null;
        }

        public bool Input(in ProGpuDragInput input)
            => Volatile.Read(ref _owner)?.ReceiveInput(this, input) == true;

        public ProGpuDragCancellation? PreparePointerCancellation()
        {
            ProGpuDragDropService? owner = Volatile.Read(ref _owner);
            if (owner is null || !owner._dispatcher.CheckAccess()
                || !ReferenceEquals(owner._inputLease, this) || owner._complete)
                return null;

            return _cancellation = owner.PrepareCancellation();
        }
    }

    private void ProcessInput(in ProGpuDragInput input)
    {
        if (_complete) return;
        ILibreDragDropSession session = _session!;
        LibreDragDropRequest request = _request!;
        bool escape = input.Kind == ProGpuDragInputKind.Escape;
        LibreDragAction action = session.QueryContinue(input.KeyState, escape);
        if (_complete) return;
        if (action == LibreDragAction.Cancel || escape)
        {
            Cancel();
            return;
        }

        UpdateTarget(session, request, input.ScreenPosition, input.KeyState);
        if (_complete) return;
        if (action == LibreDragAction.Drop || (input.KeyState & MouseButtonMask) == 0)
        {
            Drop(session, input.ScreenPosition, input.KeyState);
        }
    }

    private void UpdateTarget(
        ILibreDragDropSession session,
        LibreDragDropRequest request,
        LibrePoint screenPosition,
        int keyState)
    {
        LibreHandle hit = session.HitTest(screenPosition);
        if (_complete) return;
        if (!IsEnabledTarget(hit))
        {
            hit = default;
        }

        LibreDragDropEffects requestedEffect = ChooseEffect(request.AllowedEffects, keyState);
        if (hit != _target)
        {
            if (!_target.IsNull)
            {
                LibreHandle previous = _target;
                _target = default;
                _effect = LibreDragDropEffects.None;
                session.Leave(previous);
                if (_complete) return;
            }

            _target = default;
            _effect = LibreDragDropEffects.None;
            if (!hit.IsNull)
            {
                LibreDragTransition transition = session.Enter(hit, keyState, screenPosition, requestedEffect);
                if (_complete)
                {
                    // Cancellation can arrive inside DragEnter before it has
                    // returned the canonical accepted target. Retire that exact
                    // returned transition without adopting it or giving feedback.
                    if (IsEnabledTarget(transition.Target))
                        new ProGpuDragCancellation(session, transition.Target).Complete();
                    return;
                }

                if (IsEnabledTarget(transition.Target))
                {
                    _target = transition.Target;
                    _effect = transition.Effect & request.AllowedEffects;
                }
            }
        }
        else if (!_target.IsNull)
        {
            LibreDragDropEffects effect = session.Over(_target, keyState, screenPosition, requestedEffect);
            if (_complete) return;
            _effect = effect & request.AllowedEffects;
        }

        session.GiveFeedback(_effect);
    }

    private void Drop(ILibreDragDropSession session, LibrePoint screenPosition, int keyState)
    {
        // Application callbacks may pump input. Retire this operation before
        // dispatching Drop so reentrant release/Escape cannot finish it again.
        _complete = true;
        if (!_target.IsNull)
        {
            _result = session.Drop(_target, keyState, screenPosition, _effect);
        }
    }

    private void Cancel()
        => PrepareCancellation()?.Complete();

    private ProGpuDragCancellation? PrepareCancellation()
    {
        // Leave has the same reentrancy boundary as Drop, including when the
        // callback throws. DoDragDrop still owns registration cleanup.
        _result = LibreDragDropEffects.None;
        _complete = true;
        LibreHandle target = _target;
        _target = default;
        _effect = LibreDragDropEffects.None;
        return target.IsNull ? null : new ProGpuDragCancellation(_session!, target);
    }

    private bool IsEnabledTarget(LibreHandle target)
    {
        lock (_targetsLock)
        {
            return _targets.Contains(target);
        }
    }

    private static LibreDragDropEffects ChooseEffect(LibreDragDropEffects allowed, int keyState)
    {
        if ((keyState & ControlKey) != 0 && allowed.HasFlag(LibreDragDropEffects.Copy))
        {
            return LibreDragDropEffects.Copy;
        }

        if (allowed.HasFlag(LibreDragDropEffects.Move))
        {
            return LibreDragDropEffects.Move;
        }

        if (allowed.HasFlag(LibreDragDropEffects.Copy))
        {
            return LibreDragDropEffects.Copy;
        }

        return allowed.HasFlag(LibreDragDropEffects.Link)
            ? LibreDragDropEffects.Link
            : LibreDragDropEffects.None;
    }
}
