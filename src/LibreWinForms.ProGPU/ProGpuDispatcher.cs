// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

internal interface IProGpuLoopParticipant
{
    void Pump();
}

public sealed class ProGpuDispatcher : ILibreDispatcher, IDisposable
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly AutoResetEvent _wake = new(initialState: false);
    private readonly Lock _participantsLock = new();
    private readonly List<IProGpuLoopParticipant> _participants = [];
    private volatile bool _exitRequested;
    private bool _disposed;

    public bool CheckAccess() => Environment.CurrentManagedThreadId == _threadId;

    public void Post(Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        _work.Enqueue(callback);
        _wake.Set();
    }

    public void Send(Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        if (CheckAccess())
        {
            callback();
            return;
        }

        ExceptionDispatchInfo? error = null;
        using ManualResetEventSlim completed = new();
        Post(() =>
        {
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                error = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                completed.Set();
            }
        });
        completed.Wait();
        error?.Throw();
    }

    public void Run(CancellationToken cancellationToken)
    {
        VerifyAccess();
        _exitRequested = false;
        RunNested(() => !_exitRequested, cancellationToken);
    }

    public void RunNested(Func<bool> continueCondition, CancellationToken cancellationToken)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(continueCondition);
        while (continueCondition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            PumpOnce();
        }
    }

    public void RequestExit()
    {
        _exitRequested = true;
        _wake.Set();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RequestExit();
        _wake.Dispose();
    }

    internal void Register(IProGpuLoopParticipant participant)
    {
        VerifyAccess();
        lock (_participantsLock)
        {
            _participants.Add(participant);
        }
    }

    internal void Unregister(IProGpuLoopParticipant participant)
    {
        lock (_participantsLock)
        {
            _participants.Remove(participant);
        }
    }

    internal void Wake() => _wake.Set();

    public void PumpOnce()
    {
        VerifyAccess();
        while (_work.TryDequeue(out Action? callback))
        {
            callback();
        }

        IProGpuLoopParticipant[] participants;
        lock (_participantsLock)
        {
            participants = [.. _participants];
        }

        foreach (IProGpuLoopParticipant participant in participants)
        {
            participant.Pump();
        }

        if (_work.IsEmpty)
        {
            _wake.WaitOne(millisecondsTimeout: participants.Length == 0 ? 10 : 1);
        }
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CheckAccess())
        {
            throw new InvalidOperationException("The ProGPU dispatcher must be used from its owning thread.");
        }
    }
}
