// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using LibreWinForms.Platform;
using ProGPU.SystemDrawing;

namespace LibreWinForms.ProGPU;

/// <summary>
/// Bridges the renderer-neutral LibreWinForms platform capability to ProGPU's
/// process-scoped System.Drawing capture seam.
/// </summary>
public sealed class ProGpuDesktopCaptureService :
    ILibreDesktopCaptureService,
    IDesktopCaptureService,
    IDisposable
{
    private readonly ILibreDesktopCaptureService _platformService;
    private IDisposable? _registration;

    public ProGpuDesktopCaptureService(ILibreDesktopCaptureService platformService)
    {
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _registration = DesktopCaptureServices.Register(this);
    }

    public void Capture(LibreRectangle sourceRectangle, Span<byte> destinationRgba)
        => _platformService.Capture(sourceRectangle, destinationRgba);

    void IDesktopCaptureService.Capture(Rectangle sourceRectangle, Span<byte> destinationRgba)
        => Capture(
            new LibreRectangle(
                sourceRectangle.X,
                sourceRectangle.Y,
                sourceRectangle.Width,
                sourceRectangle.Height),
            destinationRgba);

    public void Dispose()
    {
        Interlocked.Exchange(ref _registration, null)?.Dispose();
        if (_platformService is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
