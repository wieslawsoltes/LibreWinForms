// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Runs WinForms work and nested loops on the owning UI thread.</summary>
public interface ILibreDispatcher
{
    int ManagedThreadId { get; }

    bool CheckAccess();

    void Post(Action callback);

    void Send(Action callback);

    void PumpOnce();

    void Run(CancellationToken cancellationToken);

    void RunNested(Func<bool> continueCondition, CancellationToken cancellationToken);

    void RequestExit();
}

/// <summary>
/// Resolves the dispatcher scope owned by the calling UI thread. Backends that support only one UI
/// thread can reject additional scopes explicitly; capable backends return an independent loop.
/// </summary>
public interface ILibreThreadDispatcherProvider
{
    ILibreDispatcher GetForCurrentThread();

    void Release(ILibreDispatcher dispatcher);
}

internal sealed class SingleLibreThreadDispatcherProvider(ILibreDispatcher dispatcher)
    : ILibreThreadDispatcherProvider
{
    public ILibreDispatcher GetForCurrentThread()
    {
        if (!dispatcher.CheckAccess())
        {
            throw new PlatformNotSupportedException(
                "The registered dispatcher does not provide an independent scope for this UI thread.");
        }

        return dispatcher;
    }

    public void Release(ILibreDispatcher releasedDispatcher)
    {
        if (!ReferenceEquals(dispatcher, releasedDispatcher))
        {
            throw new ArgumentException("The dispatcher was not created by this provider.", nameof(releasedDispatcher));
        }
    }
}

/// <summary>Creates timers whose callbacks are delivered by the platform dispatcher.</summary>
public interface ILibreTimerService
{
    IDisposable Start(TimeSpan interval, bool repeating, Action callback);
}
