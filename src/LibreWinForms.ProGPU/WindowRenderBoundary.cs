// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms.ProGPU;

/// <summary>Retains source render ownership until every frame scope unwinds.</summary>
internal sealed class WindowRenderBoundary(Action drainRetirements)
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    internal bool IsActive { get; private set; }

    internal void Run(Action render)
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Window rendering belongs to its source dispatcher thread.");
        // Do not consume pending paint flags or enter an acquired-texture frame
        // recursively from an application's PaintRequested/nested pump callback.
        if (IsActive)
            return;

        IsActive = true;
        Exception? renderFailure = null;
        try { render(); }
        catch (Exception failure) { renderFailure = failure; throw; }
        finally
        {
            IsActive = false;
            try { drainRetirements(); }
            catch (Exception cleanup) when (renderFailure is not null)
            {
                // The retirement queue keeps failed owners for an explicit
                // creating-thread retry. Never replace the original frame error
                // or invoke arbitrary Exception.Data accessors while unwinding.
                System.Diagnostics.Debug.WriteLine(cleanup);
            }
        }
    }
}
