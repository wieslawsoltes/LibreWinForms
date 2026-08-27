// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using FluentAssertions;
using LibreWinForms.Platform;
using ProGPU.SystemDrawing;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProGpuDesktopCaptureCollection
{
    public const string Name = "ProGPU desktop capture";
}

[Collection(ProGpuDesktopCaptureCollection.Name)]
public sealed class ProGpuDesktopCaptureTests
{
    [Fact]
    public void CreateServicesBridgesTypedCaptureIntoBitmapGraphics()
    {
        var capture = new TestCaptureService();
        using (LibrePlatformServices services = ProGpuPlatform.CreateServices(capture))
        using (var bitmap = new Bitmap(2, 2))
        {
            services.DesktopCapture.Should().BeOfType<ProGpuDesktopCaptureService>();
            DesktopCaptureServices.IsRegistered.Should().BeTrue();
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(12, 34, 0, 0, bitmap.Size);
            bitmap.GetPixel(1, 1).Should().Be(Color.FromArgb(255, 12, 34, 46));
            capture.Request.Should().Be(new LibreRectangle(12, 34, 2, 2));
        }

        DesktopCaptureServices.IsRegistered.Should().BeFalse();
        capture.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void DefaultServicesKeepMissingOsCapabilityExplicit()
    {
        using LibrePlatformServices services = ProGpuPlatform.CreateServices();
        using var bitmap = new Bitmap(1, 1);
        using Graphics graphics = Graphics.FromImage(bitmap);

        Action capture = () => graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
        capture.Should().Throw<PlatformNotSupportedException>();
    }

    private sealed class TestCaptureService : ILibreDesktopCaptureService, IDisposable
    {
        public LibreRectangle Request { get; private set; }

        public int DisposeCount { get; private set; }

        public void Capture(LibreRectangle sourceRectangle, Span<byte> destinationRgba)
        {
            Request = sourceRectangle;
            for (int offset = 0; offset < destinationRgba.Length; offset += 4)
            {
                destinationRgba[offset] = checked((byte)sourceRectangle.X);
                destinationRgba[offset + 1] = checked((byte)sourceRectangle.Y);
                destinationRgba[offset + 2] = checked((byte)(sourceRectangle.X + sourceRectangle.Y));
                destinationRgba[offset + 3] = byte.MaxValue;
            }
        }

        public void Dispose() => DisposeCount++;
    }
}
