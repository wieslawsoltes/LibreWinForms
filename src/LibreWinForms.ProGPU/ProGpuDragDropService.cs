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
}

internal interface IProGpuDragInputSource
{
    ProGpuDragInput BeginDrag(IProGpuDragInputSink sink);

    void EndDrag(IProGpuDragInputSink sink);
}

/// <summary>
/// Runs an application-local drag operation over the live Silk window inventory. Canonical
/// WinForms remains responsible for logical hit testing and event dispatch.
/// </summary>
public sealed class ProGpuDragDropService : ILibreDragDropService, IProGpuDragInputSink
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

        ProGpuDragInput initial = _input.BeginDrag(this);
        try
        {
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
        finally
        {
            _input.EndDrag(this);
            _session = null;
            _request = null;
            _target = default;
            _effect = LibreDragDropEffects.None;
            _complete = true;
        }
    }

    bool IProGpuDragInputSink.Input(in ProGpuDragInput input)
    {
        if (_session is null || _request is null || _complete)
        {
            return false;
        }

        if (_dispatcher.CheckAccess())
        {
            ProcessInput(input);
        }
        else
        {
            ProGpuDragInput captured = input;
            _dispatcher.Send(() => ProcessInput(captured));
        }

        return true;
    }

    private void ProcessInput(in ProGpuDragInput input)
    {
        ILibreDragDropSession session = _session!;
        LibreDragDropRequest request = _request!;
        bool escape = input.Kind == ProGpuDragInputKind.Escape;
        LibreDragAction action = session.QueryContinue(input.KeyState, escape);
        if (action == LibreDragAction.Cancel || escape)
        {
            Cancel();
            return;
        }

        UpdateTarget(session, request, input.ScreenPosition, input.KeyState);
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
        if (!IsEnabledTarget(hit))
        {
            hit = default;
        }

        LibreDragDropEffects requestedEffect = ChooseEffect(request.AllowedEffects, keyState);
        if (hit != _target)
        {
            if (!_target.IsNull)
            {
                session.Leave(_target);
            }

            _target = default;
            _effect = LibreDragDropEffects.None;
            if (!hit.IsNull)
            {
                LibreDragTransition transition = session.Enter(hit, keyState, screenPosition, requestedEffect);
                if (IsEnabledTarget(transition.Target))
                {
                    _target = transition.Target;
                    _effect = transition.Effect & request.AllowedEffects;
                }
            }
        }
        else if (!_target.IsNull)
        {
            _effect = session.Over(_target, keyState, screenPosition, requestedEffect)
                & request.AllowedEffects;
        }

        session.GiveFeedback(_effect);
    }

    private void Drop(ILibreDragDropSession session, LibrePoint screenPosition, int keyState)
    {
        if (!_target.IsNull)
        {
            _result = session.Drop(_target, keyState, screenPosition, _effect);
        }

        _complete = true;
    }

    private void Cancel()
    {
        if (!_target.IsNull)
        {
            _session!.Leave(_target);
        }

        _result = LibreDragDropEffects.None;
        _complete = true;
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
