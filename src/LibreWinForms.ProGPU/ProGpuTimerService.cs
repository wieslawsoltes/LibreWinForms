// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

public sealed class ProGpuTimerService : ILibreTimerService, IDisposable
{
    private readonly ProGpuDispatcher _dispatcher;
    private readonly Lock _lock = new();
    private readonly List<TimerRegistration> _timers = [];
    private bool _disposed;

    public ProGpuTimerService(ProGpuDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public IDisposable Start(TimeSpan interval, bool repeating, Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        ProGpuDispatcher dispatcher = (ProGpuDispatcher)_dispatcher.GetForCurrentThread();
        TimerRegistration registration = new(this, dispatcher, interval, repeating, callback);
        lock (_lock)
        {
            _timers.Add(registration);
        }

        dispatcher.Wake();
        return registration;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TimerRegistration[] timers;
        lock (_lock)
        {
            timers = [.. _timers];
            _timers.Clear();
        }

        foreach (TimerRegistration timer in timers)
        {
            timer.Release();
        }
    }

    private void Remove(TimerRegistration registration)
    {
        lock (_lock)
        {
            _timers.Remove(registration);
        }

        registration.Release();
    }

    private sealed class TimerRegistration : IDisposable, IProGpuLoopParticipant
    {
        private readonly ProGpuTimerService _owner;
        private readonly ProGpuDispatcher _dispatcher;
        private readonly long _intervalMilliseconds;
        private readonly bool _repeating;
        private readonly Action _callback;
        private long _nextTick;
        private int _released;

        internal TimerRegistration(
            ProGpuTimerService owner,
            ProGpuDispatcher dispatcher,
            TimeSpan interval,
            bool repeating,
            Action callback)
        {
            _owner = owner;
            _dispatcher = dispatcher;
            _intervalMilliseconds = Math.Max(1, checked((long)Math.Ceiling(interval.TotalMilliseconds)));
            _repeating = repeating;
            _callback = callback;
            _nextTick = checked(Environment.TickCount64 + _intervalMilliseconds);
            _dispatcher.Register(this);
        }

        internal bool IsCancelled => Volatile.Read(ref _nextTick) == long.MaxValue;

        void IProGpuLoopParticipant.Pump()
        {
            long now = Environment.TickCount64;
            if (IsCancelled)
            {
                return;
            }

            if (now < Volatile.Read(ref _nextTick))
            {
                return;
            }

            if (_repeating)
            {
                Volatile.Write(ref _nextTick, checked(now + _intervalMilliseconds));
            }
            else
            {
                Cancel();
            }

            try
            {
                _callback();
            }
            finally
            {
                if (!_repeating)
                {
                    _owner.Remove(this);
                }
            }
        }

        internal void Cancel() => Volatile.Write(ref _nextTick, long.MaxValue);

        internal void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            Cancel();
            _dispatcher.Unregister(this);
        }

        public void Dispose() => _owner.Remove(this);
    }
}
