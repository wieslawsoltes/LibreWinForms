// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

// The drag has already committed its terminal state. This token can only notify
// its original target; it cannot query input, drop, or select a later session.
internal sealed class ProGpuDragCancellation(ILibreDragDropSession session, LibreHandle target)
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private ILibreDragDropSession? _session = session;
    private LibreHandle _target = target;

    internal void Retire()
    {
        _session = null;
        _target = default;
    }

    internal void Complete()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Drag cancellation belongs to its source dispatcher thread.");

        ILibreDragDropSession? previous = _session;
        LibreHandle previousTarget = _target;
        Retire();
        previous?.Leave(previousTarget);
    }

    internal static void Deliver(Action flushCharacters, Action retireSource, ProGpuDragCancellation? cancellation)
    {
        Exception? failure = null;
        try { flushCharacters(); }
        catch (Exception error) { failure = error; }
        try { retireSource(); }
        catch (Exception error)
        {
            if (failure is null) failure = error;
            else failure.Data["PointerCancellationSourceRetirement"] = error;
        }

        try { cancellation?.Complete(); }
        catch (Exception error)
        {
            if (failure is null) failure = error;
            else failure.Data["PointerCancellationDragLeave"] = error;
        }

        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
