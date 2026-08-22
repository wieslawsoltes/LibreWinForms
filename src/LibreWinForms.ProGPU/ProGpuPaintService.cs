// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

public sealed class ProGpuPaintService : ILibrePaintService
{
    private readonly ILibreDispatcher _dispatcher;
    private readonly ILibreHandleRegistry _handles;

    public ProGpuPaintService(ILibreDispatcher dispatcher, ILibreHandleRegistry handles)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _handles = handles ?? throw new ArgumentNullException(nameof(handles));
    }

    public void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle)
        => Schedule(target, dirtyRectangle);

    public void InvalidateAll(LibreHandle target)
        => Schedule(target, dirtyRectangle: null);

    public void Present(LibreHandle target)
        => Schedule(target, dirtyRectangle: null);

    private void Schedule(LibreHandle target, LibreRectangle? dirtyRectangle)
    {
        if (_dispatcher.CheckAccess())
        {
            Resolve(target).RequestPaint(dirtyRectangle);
        }
        else
        {
            _dispatcher.Post(() => Resolve(target).RequestPaint(dirtyRectangle));
        }
    }

    private SilkLibreWindow Resolve(LibreHandle target)
        => _handles.TryGet(target, out SilkLibreWindow? window)
            ? window
            : throw new ArgumentException("The handle does not identify a live ProGPU window.", nameof(target));
}
