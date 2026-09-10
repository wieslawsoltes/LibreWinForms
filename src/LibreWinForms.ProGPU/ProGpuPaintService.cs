// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

public sealed class ProGpuPaintService : ILibrePaintService, ILibreAdornerService, ILibreReversibleDrawingService
{
    private readonly ILibreDispatcher _fallbackDispatcher;
    private readonly ILibreHandleRegistry _handles;
    private readonly SilkWindowService? _windows;

    public ProGpuPaintService(ILibreDispatcher dispatcher, ILibreHandleRegistry handles)
    {
        _fallbackDispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _handles = handles ?? throw new ArgumentNullException(nameof(handles));
    }

    public ProGpuPaintService(
        ILibreDispatcher dispatcher,
        ILibreHandleRegistry handles,
        SilkWindowService windows)
    {
        _fallbackDispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _handles = handles ?? throw new ArgumentNullException(nameof(handles));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
    }

    public System.Drawing.Graphics CreateGraphics(
        LibreHandle target,
        LibrePoint origin,
        LibreRectangle clipRectangle)
    {
        if (_handles.TryGet(target, out SilkLibreWindow? window))
        {
            return window.CreateGraphics(origin, clipRectangle);
        }

        if (!_handles.TryGet<object>(target, out _))
        {
            throw new ArgumentException(
                "The handle does not identify a live ProGPU window or logical control.",
                nameof(target));
        }

        return SilkLibreWindow.CreateDetachedGraphics(origin, clipRectangle);
    }

    public void Invalidate(LibreHandle target, LibreRectangle dirtyRectangle)
        => Schedule(target, dirtyRectangle);

    public void InvalidateAll(LibreHandle target)
        => Schedule(target, dirtyRectangle: null);

    public void Present(LibreHandle target)
    {
        ILibreWindow window = ResolveWindow(target);
        ILibreDispatcher dispatcher = window is SilkLibreWindow silkWindow
            ? silkWindow.Dispatcher
            : _fallbackDispatcher;
        if (dispatcher.CheckAccess())
        {
            window.PresentPendingPaint();
        }
        else
        {
            dispatcher.Send(window.PresentPendingPaint);
        }
    }

    public System.Drawing.Graphics CreateGraphics(
        LibreHandle owner,
        LibreAdornerId adorner,
        LibreRectangle bounds,
        LibreRectangle clipRectangle)
        => Resolve(owner).CreateAdornerGraphics(adorner, bounds, clipRectangle);

    public void Remove(LibreHandle owner, LibreAdornerId adorner)
        => Resolve(owner).RemoveAdorner(adorner);

    public void DrawFrame(
        LibreRectangle rectangle,
        LibreArgbColor backColor,
        LibreReversibleFrameStyle style)
        => GetWindowService().ToggleReversibleDrawing(
            ProGpuReversibleDrawingOperation.CreateFrame(rectangle, backColor, style));

    public void DrawLine(LibrePoint start, LibrePoint end, LibreArgbColor backColor)
        => GetWindowService().ToggleReversibleDrawing(
            ProGpuReversibleDrawingOperation.CreateLine(start, end, backColor));

    public void FillRectangle(LibreRectangle rectangle, LibreArgbColor backColor)
        => GetWindowService().ToggleReversibleDrawing(
            ProGpuReversibleDrawingOperation.CreateFillRectangle(rectangle, backColor));

    private void Schedule(LibreHandle target, LibreRectangle? dirtyRectangle)
    {
        SilkLibreWindow window = Resolve(target);
        ProGpuDispatcher dispatcher = window.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            window.RequestPaint(dirtyRectangle);
        }
        else
        {
            dispatcher.Post(() => window.RequestPaint(dirtyRectangle));
        }
    }

    private SilkLibreWindow Resolve(LibreHandle target)
        => _handles.TryGet(target, out SilkLibreWindow? window)
            ? window
            : throw new ArgumentException("The handle does not identify a live ProGPU window.", nameof(target));

    private ILibreWindow ResolveWindow(LibreHandle target)
        => _handles.TryGet(target, out ILibreWindow? window)
            ? window
            : throw new ArgumentException("The handle does not identify a live platform window.", nameof(target));

    private SilkWindowService GetWindowService()
        => _windows
            ?? throw new PlatformNotSupportedException(
                "This ProGPU paint service was created without a Silk window service for screen-space overlays.");
}
