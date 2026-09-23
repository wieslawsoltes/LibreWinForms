// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Numerics;
using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.Scene;
using ProGPU.SystemDrawing;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

[Collection(ProGpuDesktopCaptureCollection.Name)]
public sealed class ProGpuNativeDrawingInteropTests
{
    [Fact]
    public void CreateServicesBridgesExactNativeInputsAndTypedProducts()
    {
        var native = new TestNativeInteropService();
        using (LibrePlatformServices services = ProGpuPlatform.CreateServices(
            UnsupportedLibreDesktopCaptureService.Instance,
            native,
            native))
        {
            services.NativeFonts.Should().BeOfType<ProGpuNativeDrawingInteropService>();
            services.NativeGraphics.Should().BeSameAs(services.NativeFonts);
            NativeFontInteropServices.IsRegistered.Should().BeTrue();
            NativeGraphicsInteropServices.IsRegistered.Should().BeTrue();

            using Font font = Font.FromHdc((IntPtr)10);
            using Graphics hdc = Graphics.FromHdc((IntPtr)11, (IntPtr)12);
            using Graphics window = Graphics.FromHwnd(IntPtr.Zero);
            Graphics.GetHalftonePalette().Should().Be((IntPtr)909);

            font.Size.Should().Be(12f);
            hdc.VisibleClipBounds.Should().Be(new RectangleF(-2f, -3f, 100f, 80f));
            window.VisibleClipBounds.Should().Be(new RectangleF(-2f, -3f, 100f, 80f));
            native.FontHandles.Should().Equal((IntPtr)10);
            native.DeviceContexts.Should().Equal(((IntPtr)11, (IntPtr)12));
            native.Windows.Should().Equal(IntPtr.Zero);
        }

        NativeFontInteropServices.IsRegistered.Should().BeFalse();
        NativeGraphicsInteropServices.IsRegistered.Should().BeFalse();
        native.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void DefaultServicesKeepNativeOsCapabilitiesExplicit()
    {
        using LibrePlatformServices services = ProGpuPlatform.CreateServices();

        Action font = () => Font.FromHdc((IntPtr)1);
        Action hdc = () => Graphics.FromHdc((IntPtr)1);
        Action window = () => Graphics.FromHwnd(IntPtr.Zero);
        Action palette = () => Graphics.GetHalftonePalette();

        font.Should().Throw<PlatformNotSupportedException>();
        hdc.Should().Throw<PlatformNotSupportedException>();
        window.Should().Throw<PlatformNotSupportedException>();
        palette.Should().Throw<PlatformNotSupportedException>();
    }

    private sealed class TestNativeInteropService :
        ILibreNativeFontInteropService,
        ILibreNativeGraphicsInteropService,
        IDisposable
    {
        public List<IntPtr> FontHandles { get; } = [];

        public List<(IntPtr DeviceContext, IntPtr Device)> DeviceContexts { get; } = [];

        public List<IntPtr> Windows { get; } = [];

        public int DisposeCount { get; private set; }

        public Font ImportFromDeviceContext(IntPtr deviceContext)
        {
            FontHandles.Add(deviceContext);
            using FontFamily family = FontFamily.GenericSansSerif;
            return new Font(family, 12f);
        }

        public Graphics CreateFromDeviceContext(IntPtr deviceContext, IntPtr device)
        {
            DeviceContexts.Add((deviceContext, device));
            return CreateGraphics();
        }

        public Graphics CreateFromWindow(IntPtr window)
        {
            Windows.Add(window);
            return CreateGraphics();
        }

        public IntPtr CreateHalftonePalette() => (IntPtr)909;

        public void Dispose() => DisposeCount++;

        private static Graphics CreateGraphics()
            => Graphics.FromProGpuDrawingContext(
                new DrawingContext(),
                new RectangleF(0f, 0f, 100f, 80f),
                Matrix4x4.CreateTranslation(2f, 3f, 0f));
    }
}
