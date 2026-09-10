// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using LibreWinForms.Platform;
using ProGPU.SystemDrawing;

namespace LibreWinForms.ProGPU;

/// <summary>
/// Bridges renderer-neutral LibreWinForms native interoperability into the
/// process-scoped ProGPU System.Drawing services.
/// </summary>
public sealed class ProGpuNativeDrawingInteropService :
    ILibreNativeFontInteropService,
    ILibreNativeGraphicsInteropService,
    INativeFontInteropService,
    INativeGraphicsInteropService,
    IDisposable
{
    private readonly ILibreNativeFontInteropService _fontService;
    private readonly ILibreNativeGraphicsInteropService _graphicsService;
    private IDisposable? _fontRegistration;
    private IDisposable? _graphicsRegistration;

    public ProGpuNativeDrawingInteropService(
        ILibreNativeFontInteropService fontService,
        ILibreNativeGraphicsInteropService graphicsService)
    {
        _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
        _graphicsService = graphicsService ?? throw new ArgumentNullException(nameof(graphicsService));
        _fontRegistration = NativeFontInteropServices.Register(this);
        try
        {
            _graphicsRegistration = NativeGraphicsInteropServices.Register(this);
        }
        catch
        {
            Interlocked.Exchange(ref _fontRegistration, null)?.Dispose();
            throw;
        }
    }

    public Font ImportFromDeviceContext(IntPtr deviceContext)
        => _fontService.ImportFromDeviceContext(deviceContext);

    public Graphics CreateFromDeviceContext(IntPtr deviceContext, IntPtr device)
        => _graphicsService.CreateFromDeviceContext(deviceContext, device);

    public Graphics CreateFromWindow(IntPtr window)
        => _graphicsService.CreateFromWindow(window);

    public IntPtr CreateHalftonePalette()
        => _graphicsService.CreateHalftonePalette();

    public void Dispose()
    {
        Interlocked.Exchange(ref _graphicsRegistration, null)?.Dispose();
        Interlocked.Exchange(ref _fontRegistration, null)?.Dispose();

        HashSet<IDisposable> disposed = new(ReferenceEqualityComparer.Instance);
        DisposeService(_graphicsService, disposed);
        DisposeService(_fontService, disposed);
    }

    private static void DisposeService(object service, HashSet<IDisposable> disposed)
    {
        if (service is IDisposable disposable && disposed.Add(disposable))
        {
            disposable.Dispose();
        }
    }
}
