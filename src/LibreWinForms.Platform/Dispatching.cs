// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.Platform;

/// <summary>Runs WinForms work and nested loops on the owning UI thread.</summary>
public interface ILibreDispatcher
{
    bool CheckAccess();

    void Post(Action callback);

    void Send(Action callback);

    void PumpOnce();

    void Run(CancellationToken cancellationToken);

    void RunNested(Func<bool> continueCondition, CancellationToken cancellationToken);

    void RequestExit();
}

/// <summary>Creates timers whose callbacks are delivered by the platform dispatcher.</summary>
public interface ILibreTimerService
{
    IDisposable Start(TimeSpan interval, bool repeating, Action callback);
}
