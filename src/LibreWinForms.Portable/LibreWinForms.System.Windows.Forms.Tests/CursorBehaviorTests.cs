using System;
using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Forms = System.Windows.Forms;

namespace LibreWinForms.SystemWindowsForms.Tests;

internal static class CursorBehaviorTests
{
    public static void Run()
    {
        SharedSystemCursorDisposalIsHarmless();
        PngBackedCursorDecodesAndDraws();
        DibCursorAppliesAlphaAndAndMask();
        DibCursorRestoresOpaqueAlphaWhenThePlaneIsUnused();
        MalformedPayloadBoundsFailClosed();
        TruncatedDibMaskFailsClosed();
        UnsupportedDibFormatsFailClearly();
        Console.WriteLine(
            "LibreWinForms Cursor behavior tests passed: shared=1 png=1 dibAlpha=1 dibMask=2 bounds=1 truncated=1 unsupported=2.");
    }

    private static void SharedSystemCursorDisposalIsHarmless()
    {
        Forms.Cursor shared = Forms.Cursors.Default;
        shared.Dispose();
        Assert(shared.Size == new Size(32, 32), "Disposing a shared system cursor invalidated its portable size.");
        Assert(ReferenceEquals(shared, Forms.Cursors.Default), "Shared system cursor identity changed after disposal.");
    }

    private static void PngBackedCursorDecodesAndDraws()
    {
        byte[] png;
        using (var source = new Bitmap(2, 2))
        {
            source.SetPixel(0, 0, Color.FromArgb(255, 220, 10, 20));
            source.SetPixel(1, 0, Color.FromArgb(255, 20, 210, 30));
            source.SetPixel(0, 1, Color.FromArgb(255, 30, 40, 200));
            source.SetPixel(1, 1, Color.Transparent);
            using var stream = new MemoryStream();
            source.Save(stream, ImageFormat.Png);
            png = stream.ToArray();
        }

        WithCursorFile(BuildCursorContainer(png, 2, 2), path =>
        {
            using var cursor = new Forms.Cursor(path);
            Assert(cursor.PortableKind == Forms.PortableCursorKind.Custom, "File cursor lost its typed custom identity.");
            Assert(cursor.Size == new Size(2, 2), "PNG cursor size did not come from its decoded frame.");

            using var target = new Bitmap(4, 4);
            using (Graphics graphics = Graphics.FromImage(target))
            {
                cursor.Draw(graphics, new Rectangle(1, 1, 2, 2));
            }

            AssertColor(target.GetPixel(1, 1), 255, 220, 10, 20, "PNG cursor top-left pixel changed while drawing.");
            AssertColor(target.GetPixel(2, 1), 255, 20, 210, 30, "PNG cursor top-right pixel changed while drawing.");
            Assert(target.GetPixel(2, 2).A == 0, "PNG cursor transparency was not preserved.");
            Assert(target.GetPixel(0, 0).A == 0, "Cursor draw escaped the target rectangle.");
        });
    }

    private static void DibCursorAppliesAlphaAndAndMask()
    {
        Color[] pixels =
        {
            Color.FromArgb(255, 255, 0, 0),
            Color.FromArgb(128, 0, 255, 0),
            Color.FromArgb(255, 0, 0, 255),
            Color.FromArgb(255, 255, 255, 255)
        };
        bool[] mask = { false, false, false, true };
        byte[] dib = BuildDibPayload(2, 2, pixels, mask);

        WithCursorFile(BuildCursorContainer(dib, 2, 2), path =>
        {
            using var cursor = new Forms.Cursor(path);
            Assert(cursor.Size == new Size(2, 2), "DIB cursor size did not come from its decoded frame.");

            using var target = new Bitmap(2, 2);
            using (Graphics graphics = Graphics.FromImage(target))
            {
                cursor.Draw(graphics, new Rectangle(0, 0, 2, 2));
            }

            AssertColor(target.GetPixel(0, 0), 255, 255, 0, 0, "DIB bottom-up row conversion changed the top-left pixel.");
            AssertColor(target.GetPixel(1, 0), 128, 0, 255, 0, "DIB explicit alpha was not preserved.");
            AssertColor(target.GetPixel(0, 1), 255, 0, 0, 255, "DIB bottom-up row conversion changed the bottom-left pixel.");
            Assert(target.GetPixel(1, 1).A == 0, "DIB AND mask did not clear the masked pixel.");
        });
    }

    private static void DibCursorRestoresOpaqueAlphaWhenThePlaneIsUnused()
    {
        Color[] pixels =
        {
            Color.FromArgb(0, 180, 30, 20),
            Color.FromArgb(0, 20, 180, 30)
        };
        bool[] mask = { false, true };
        byte[] dib = BuildDibPayload(2, 1, pixels, mask);

        WithCursorFile(BuildCursorContainer(dib, 2, 1), path =>
        {
            using var cursor = new Forms.Cursor(path);
            using var target = new Bitmap(2, 1);
            using (Graphics graphics = Graphics.FromImage(target))
            {
                cursor.Draw(graphics, new Rectangle(0, 0, 2, 1));
            }

            AssertColor(target.GetPixel(0, 0), 255, 180, 30, 20, "Unused DIB alpha plane did not restore an opaque pixel.");
            Assert(target.GetPixel(1, 0).A == 0, "Unused DIB alpha plane ignored its AND mask.");
        });
    }

    private static void MalformedPayloadBoundsFailClosed()
    {
        byte[] container = BuildCursorContainer(new byte[40], 1, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(18, 4), (uint)(container.Length + 1));
        WithCursorFile(container, path =>
        {
            InvalidDataException exception = AssertThrows<InvalidDataException>(() => new Forms.Cursor(path));
            Assert(exception.Message.Contains("outside the file bounds", StringComparison.Ordinal), "Malformed bounds exception was not actionable.");
        });
    }

    private static void TruncatedDibMaskFailsClosed()
    {
        byte[] dib = BuildDibPayload(
            1,
            1,
            new[] { Color.FromArgb(255, 1, 2, 3) },
            new[] { false });
        Array.Resize(ref dib, dib.Length - 1);
        WithCursorFile(BuildCursorContainer(dib, 1, 1), path =>
        {
            InvalidDataException exception = AssertThrows<InvalidDataException>(() => new Forms.Cursor(path));
            Assert(exception.Message.Contains("truncated", StringComparison.OrdinalIgnoreCase), "Truncated DIB exception was not actionable.");
        });
    }

    private static void UnsupportedDibFormatsFailClearly()
    {
        byte[] bitDepth = BuildDibPayload(
            1,
            1,
            new[] { Color.FromArgb(255, 1, 2, 3) },
            new[] { false });
        BinaryPrimitives.WriteUInt16LittleEndian(bitDepth.AsSpan(14, 2), 24);
        WithCursorFile(BuildCursorContainer(bitDepth, 1, 1), path =>
        {
            NotSupportedException exception = AssertThrows<NotSupportedException>(() => new Forms.Cursor(path));
            Assert(exception.Message.Contains("bit depth 24", StringComparison.Ordinal), "Unsupported bit depth exception was not actionable.");
        });

        byte[] compression = BuildDibPayload(
            1,
            1,
            new[] { Color.FromArgb(255, 1, 2, 3) },
            new[] { false });
        BinaryPrimitives.WriteUInt32LittleEndian(compression.AsSpan(16, 4), 3);
        WithCursorFile(BuildCursorContainer(compression, 1, 1), path =>
        {
            NotSupportedException exception = AssertThrows<NotSupportedException>(() => new Forms.Cursor(path));
            Assert(exception.Message.Contains("compression mode 3", StringComparison.Ordinal), "Unsupported compression exception was not actionable.");
        });
    }

    private static byte[] BuildCursorContainer(byte[] payload, int width, int height)
    {
        const int payloadOffset = 6 + 16;
        var container = new byte[payloadOffset + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(container.AsSpan(2, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(container.AsSpan(4, 2), 1);
        container[6] = width == 256 ? (byte)0 : checked((byte)width);
        container[7] = height == 256 ? (byte)0 : checked((byte)height);
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(14, 4), checked((uint)payload.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(18, 4), payloadOffset);
        payload.CopyTo(container, payloadOffset);
        return container;
    }

    private static byte[] BuildDibPayload(int width, int height, Color[] pixels, bool[] mask)
    {
        Assert(pixels.Length == width * height, "Synthetic DIB pixel fixture has the wrong length.");
        Assert(mask.Length == width * height, "Synthetic DIB mask fixture has the wrong length.");

        const int headerSize = 40;
        int colorStride = checked(width * 4);
        int maskStride = checked(((width + 31) / 32) * 4);
        int colorByteCount = checked(colorStride * height);
        var payload = new byte[checked(headerSize + colorByteCount + maskStride * height)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), checked(height * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14, 2), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), checked((uint)(colorByteCount + maskStride * height)));

        int maskOffset = headerSize + colorByteCount;
        for (int encodedRow = 0; encodedRow < height; encodedRow++)
        {
            int sourceY = height - 1 - encodedRow;
            for (int x = 0; x < width; x++)
            {
                Color color = pixels[sourceY * width + x];
                int colorOffset = headerSize + encodedRow * colorStride + x * 4;
                payload[colorOffset] = color.B;
                payload[colorOffset + 1] = color.G;
                payload[colorOffset + 2] = color.R;
                payload[colorOffset + 3] = color.A;
                if (mask[sourceY * width + x])
                {
                    payload[maskOffset + encodedRow * maskStride + x / 8] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return payload;
    }

    private static void WithCursorFile(byte[] content, Action<string> action)
    {
        string path = Path.Combine(Path.GetTempPath(), $"librewinforms-cursor-{Guid.NewGuid():N}.cur");
        File.WriteAllBytes(path, content);
        try
        {
            action(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static TException AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void AssertColor(Color actual, byte alpha, byte red, byte green, byte blue, string message)
    {
        if (actual.A != alpha || actual.R != red || actual.G != green || actual.B != blue)
        {
            throw new InvalidOperationException(
                $"{message} Expected ARGB=({alpha},{red},{green},{blue}), actual=({actual.A},{actual.R},{actual.G},{actual.B}).");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
