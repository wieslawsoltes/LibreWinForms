// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

public sealed class ProGpuTimerService : ILibreTimerService, IProGpuLoopParticipant, IDisposable
{
    private readonly ProGpuDispatcher _dispatcher;
    private readonly Lock _lock = new();
    private readonly List<TimerRegistration> _timers = [];
    private bool _disposed;

    public ProGpuTimerService(ProGpuDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _dispatcher.Register(this);
    }

    public IDisposable Start(TimeSpan interval, bool repeating, Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        TimerRegistration registration = new(this, interval, repeating, callback);
        lock (_lock)
        {
            _timers.Add(registration);
        }

        _dispatcher.Wake();
        return registration;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispatcher.Unregister(this);
        lock (_lock)
        {
            foreach (TimerRegistration timer in _timers)
            {
                timer.Cancel();
            }

            _timers.Clear();
        }
    }

    void IProGpuLoopParticipant.Pump()
    {
        long now = Environment.TickCount64;
        TimerRegistration[] due;
        lock (_lock)
        {
            _timers.RemoveAll(static timer => timer.IsCancelled);
            due = [.. _timers.Where(timer => timer.IsDue(now))];
        }

        foreach (TimerRegistration timer in due)
        {
            timer.Fire(now);
        }
    }

    private void Remove(TimerRegistration registration)
    {
        registration.Cancel();
        lock (_lock)
        {
            _timers.Remove(registration);
        }
    }

    private sealed class TimerRegistration : IDisposable
    {
        private readonly ProGpuTimerService _owner;
        private readonly long _intervalMilliseconds;
        private readonly bool _repeating;
        private readonly Action _callback;
        private long _nextTick;

        internal TimerRegistration(ProGpuTimerService owner, TimeSpan interval, bool repeating, Action callback)
        {
            _owner = owner;
            _intervalMilliseconds = Math.Max(1, checked((long)Math.Ceiling(interval.TotalMilliseconds)));
            _repeating = repeating;
            _callback = callback;
            _nextTick = checked(Environment.TickCount64 + _intervalMilliseconds);
        }

        internal bool IsCancelled => Volatile.Read(ref _nextTick) == long.MaxValue;

        internal bool IsDue(long now) => now >= Volatile.Read(ref _nextTick);

        internal void Fire(long now)
        {
            if (IsCancelled)
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

            _callback();
        }

        internal void Cancel() => Volatile.Write(ref _nextTick, long.MaxValue);

        public void Dispose() => _owner.Remove(this);
    }
}
