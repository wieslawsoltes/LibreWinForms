// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using LibreWinForms.Platform;
using Silk.NET.Windowing;

namespace LibreWinForms.ProGPU;

internal interface IProGpuLoopParticipant
{
    void Pump();
}

public sealed class ProGpuDispatcher : ILibreDispatcher, ILibreThreadDispatcherProvider, IDisposable
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly ProGpuDispatcher _provider;
    private readonly ConcurrentDictionary<int, ProGpuDispatcher> _threadDispatchers;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly AutoResetEvent _wake = new(initialState: false);
    private readonly Lock _participantsLock = new();
    private readonly List<IProGpuLoopParticipant> _participants = [];
    private readonly NativeWindowRetirementQueue _nativeRetirements;
    private volatile bool _exitRequested;
    private bool _disposed;

    public ProGpuDispatcher() : this(new NativeWindowRetirementQueue())
    {
    }

    internal ProGpuDispatcher(NativeWindowRetirementQueue nativeRetirements)
    {
        _nativeRetirements = nativeRetirements ?? throw new ArgumentNullException(nameof(nativeRetirements));
        _provider = this;
        _threadDispatchers = new ConcurrentDictionary<int, ProGpuDispatcher>();
        _threadDispatchers.TryAdd(_threadId, this);
    }

    private ProGpuDispatcher(ProGpuDispatcher provider)
    {
        _nativeRetirements = new NativeWindowRetirementQueue();
        _provider = provider;
        _threadDispatchers = provider._threadDispatchers;
    }

    public int ManagedThreadId => _threadId;

    public bool CheckAccess() => Environment.CurrentManagedThreadId == _threadId;

    public ILibreDispatcher GetForCurrentThread()
    {
        ObjectDisposedException.ThrowIf(_provider._disposed, _provider);
        int threadId = Environment.CurrentManagedThreadId;
        return _threadDispatchers.GetOrAdd(
            threadId,
            static (_, provider) => new ProGpuDispatcher(provider),
            _provider);
    }

    public void Release(ILibreDispatcher dispatcher)
    {
        if (dispatcher is not ProGpuDispatcher released
            || !ReferenceEquals(_provider, released._provider))
        {
            throw new ArgumentException("The dispatcher was not created by this provider.", nameof(dispatcher));
        }

        if (ReferenceEquals(released, _provider))
        {
            return;
        }

        // A failed release must leave the dispatcher registered and wakeable.
        released.EnsureNativeRetirementsComplete();
        if (!_threadDispatchers.TryGetValue(released.ManagedThreadId, out ProGpuDispatcher? registered)
            || !ReferenceEquals(registered, released)
            || !_threadDispatchers.TryRemove(released.ManagedThreadId, out registered)
            || !ReferenceEquals(registered, released))
        {
            throw new ArgumentException("The dispatcher is no longer registered with this provider.", nameof(dispatcher));
        }

        released.DisposeScope();
    }

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
        if (!ReferenceEquals(this, _provider))
        {
            _provider.Release(this);
            return;
        }

        if (_disposed)
        {
            return;
        }

        foreach (ProGpuDispatcher dispatcher in _threadDispatchers.Values)
            dispatcher.EnsureNativeRetirementsComplete();

        _disposed = true;
        foreach (ProGpuDispatcher dispatcher in _threadDispatchers.Values)
        {
            if (!ReferenceEquals(dispatcher, this))
            {
                dispatcher.DisposeScope();
            }
        }

        _threadDispatchers.Clear();
        _exitRequested = true;
        _wake.Set();
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

    internal void RetireNativeWindow(IWindow window, Action? releaseRenderingResources = null)
    {
        VerifyAccess();
        _nativeRetirements.Retire(window, releaseRenderingResources);
    }

    public void PumpOnce()
    {
        VerifyAccess();
        Exception? pumpFailure = null;
        try
        {
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
        catch (Exception failure)
        {
            pumpFailure = failure;
            throw;
        }
        finally
        {
            // Native polling and its managed callbacks have unwound. Retirement
            // does not poll again and must also run when a source callback fails.
            try { _nativeRetirements.Drain(); }
            catch (Exception cleanup) when (pumpFailure is not null)
            {
                pumpFailure.Data[nameof(NativeWindowRetirementQueue)] = cleanup;
            }
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

    private void DisposeScope()
    {
        if (_disposed)
        {
            return;
        }

        EnsureNativeRetirementsComplete();
        _disposed = true;
        _exitRequested = true;
        _wake.Set();
        _wake.Dispose();
    }

    private void EnsureNativeRetirementsComplete()
    {
        if (CheckAccess())
            _nativeRetirements.Drain();
        if (_nativeRetirements.HasPending)
            throw new InvalidOperationException("Complete native window retirement on its source thread before releasing the dispatcher.");
    }
}
