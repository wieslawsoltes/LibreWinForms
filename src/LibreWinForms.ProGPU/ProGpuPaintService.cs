// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

public sealed class ProGpuPaintService : ILibrePaintService
{
    private readonly ILibreHandleRegistry _handles;

    public ProGpuPaintService(ILibreHandleRegistry handles)
    {
        _handles = handles ?? throw new ArgumentNullException(nameof(handles));
    }

    public void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle)
        => Resolve(target).RequestPaint(dirtyRectangle);

    public void InvalidateAll(LibreHandle target)
        => Resolve(target).RequestPaint(dirtyRectangle: null);

    public void Present(LibreHandle target)
        => Resolve(target).RequestPaint(dirtyRectangle: null);

    private SilkLibreWindow Resolve(LibreHandle target)
        => _handles.TryGet(target, out SilkLibreWindow? window)
            ? window
            : throw new ArgumentException("The handle does not identify a live ProGPU window.", nameof(target));
}
