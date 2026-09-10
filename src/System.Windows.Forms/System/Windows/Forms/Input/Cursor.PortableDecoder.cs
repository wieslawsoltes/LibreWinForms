// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace System.Windows.Forms;

/// <summary>Decodes the managed image payload used by a portable Windows cursor.</summary>
internal static class PortableCursorDecoder
{
    private const int CursorDirectoryHeaderSize = 6;
    private const int CursorDirectoryEntrySize = 16;
    private const int BitmapInfoHeaderSize = 40;
    private const int MaximumCursorEntries = 256;
    private const int MaximumCursorFileBytes = 64 * 1024 * 1024;
    private const int MaximumDecodedPixels = 256 * 256;
    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    internal static Bitmap Decode(ReadOnlySpan<byte> container)
    {
        if (container.Length > MaximumCursorFileBytes)
        {
            throw new InvalidDataException($"Cursor data exceeds the {MaximumCursorFileBytes}-byte portable decode limit.");
        }

        if (container.Length < CursorDirectoryHeaderSize)
        {
            throw new InvalidDataException("Cursor data is truncated before its directory header.");
        }

        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(container);
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(container[2..]);
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(container[4..]);
        if (reserved != 0 || type != 2)
        {
            throw new InvalidDataException("The data is not a Windows cursor container (CUR type 2).");
        }

        if (count is 0 or > MaximumCursorEntries)
        {
            throw new InvalidDataException($"Cursor directory entry count must be between 1 and {MaximumCursorEntries}.");
        }

        int directorySize = checked(CursorDirectoryHeaderSize + count * CursorDirectoryEntrySize);
        if (directorySize > container.Length)
        {
            throw new InvalidDataException("Cursor directory entries extend beyond the data bounds.");
        }

        CursorEntry? selected = null;
        for (int index = 0; index < count; index++)
        {
            int entryOffset = CursorDirectoryHeaderSize + index * CursorDirectoryEntrySize;
            ReadOnlySpan<byte> encodedEntry = container.Slice(entryOffset, CursorDirectoryEntrySize);
            int width = encodedEntry[0] == 0 ? 256 : encodedEntry[0];
            int height = encodedEntry[1] == 0 ? 256 : encodedEntry[1];
            if (encodedEntry[3] != 0)
            {
                throw new InvalidDataException($"Cursor directory entry {index} has a nonzero reserved byte.");
            }

            int hotSpotX = BinaryPrimitives.ReadUInt16LittleEndian(encodedEntry[4..]);
            int hotSpotY = BinaryPrimitives.ReadUInt16LittleEndian(encodedEntry[6..]);
            if (hotSpotX >= width || hotSpotY >= height)
            {
                throw new InvalidDataException($"Cursor directory entry {index} has a hotspot outside its image bounds.");
            }

            uint encodedLength = BinaryPrimitives.ReadUInt32LittleEndian(encodedEntry[8..]);
            uint encodedOffset = BinaryPrimitives.ReadUInt32LittleEndian(encodedEntry[12..]);
            if (encodedLength == 0)
            {
                throw new InvalidDataException($"Cursor directory entry {index} has an empty image payload.");
            }

            long payloadEnd = (long)encodedOffset + encodedLength;
            if (encodedOffset < directorySize || payloadEnd > container.Length)
            {
                throw new InvalidDataException($"Cursor directory entry {index} points outside the data bounds.");
            }

            int payloadOffset = checked((int)encodedOffset);
            int payloadLength = checked((int)encodedLength);
            CursorPayloadKind kind = ClassifyPayload(container.Slice(payloadOffset, payloadLength));
            if (kind == CursorPayloadKind.Unsupported)
            {
                continue;
            }

            CursorEntry candidate = new(width, height, payloadOffset, payloadLength, kind, index);
            if (selected is null || IsPreferred(candidate, selected.Value))
            {
                selected = candidate;
            }
        }

        if (selected is null)
        {
            throw new NotSupportedException("Cursor contains no supported PNG or uncompressed 32-bpp DIB image payload.");
        }

        return selected.Value.Kind switch
        {
            CursorPayloadKind.Png => DecodePng(container, selected.Value),
            CursorPayloadKind.Dib => DecodeDib(container, selected.Value),
            _ => throw new InvalidOperationException("Unsupported cursor payload selection state."),
        };
    }

    private static bool IsPreferred(CursorEntry candidate, CursorEntry current)
    {
        int candidateArea = candidate.Width * candidate.Height;
        int currentArea = current.Width * current.Height;
        if (candidateArea != currentArea)
        {
            return candidateArea > currentArea;
        }

        if (candidate.Kind != current.Kind)
        {
            return candidate.Kind == CursorPayloadKind.Png;
        }

        if (candidate.PayloadLength != current.PayloadLength)
        {
            return candidate.PayloadLength > current.PayloadLength;
        }

        return candidate.DirectoryIndex < current.DirectoryIndex;
    }

    private static CursorPayloadKind ClassifyPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length >= PngSignature.Length && payload[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return CursorPayloadKind.Png;
        }

        if (payload.Length >= sizeof(uint) && BinaryPrimitives.ReadUInt32LittleEndian(payload) >= BitmapInfoHeaderSize)
        {
            return CursorPayloadKind.Dib;
        }

        return CursorPayloadKind.Unsupported;
    }

    private static Bitmap DecodePng(ReadOnlySpan<byte> container, CursorEntry entry)
    {
        ReadOnlySpan<byte> payload = container.Slice(entry.PayloadOffset, entry.PayloadLength);
        if (payload.Length < 33
            || BinaryPrimitives.ReadUInt32BigEndian(payload[8..]) != 13
            || !payload.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException("PNG-backed cursor image has a malformed IHDR chunk.");
        }

        uint pngWidth = BinaryPrimitives.ReadUInt32BigEndian(payload[16..]);
        uint pngHeight = BinaryPrimitives.ReadUInt32BigEndian(payload[20..]);
        ValidateImageDimensions(pngWidth, pngHeight, entry);

        try
        {
            using MemoryStream stream = new(payload.ToArray(), writable: false);
            Bitmap bitmap = new(stream);
            if (bitmap.Width != entry.Width || bitmap.Height != entry.Height)
            {
                bitmap.Dispose();
                throw new InvalidDataException("Decoded PNG cursor dimensions do not match its directory entry.");
            }

            return bitmap;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("PNG-backed cursor image could not be decoded.", exception);
        }
    }

    private static Bitmap DecodeDib(ReadOnlySpan<byte> container, CursorEntry entry)
    {
        ReadOnlySpan<byte> payload = container.Slice(entry.PayloadOffset, entry.PayloadLength);
        uint encodedHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (encodedHeaderSize < BitmapInfoHeaderSize || encodedHeaderSize > payload.Length)
        {
            throw new InvalidDataException("Cursor DIB has an invalid bitmap information header size.");
        }

        int headerSize = checked((int)encodedHeaderSize);
        int width = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        int encodedHeight = BinaryPrimitives.ReadInt32LittleEndian(payload[8..]);
        ushort planes = BinaryPrimitives.ReadUInt16LittleEndian(payload[12..]);
        ushort bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(payload[14..]);
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(payload[16..]);
        if (planes != 1)
        {
            throw new NotSupportedException($"Cursor DIB plane count {planes} is not supported; expected one plane.");
        }

        if (bitsPerPixel != 32)
        {
            throw new NotSupportedException($"Cursor DIB bit depth {bitsPerPixel} is not supported; only uncompressed 32-bpp images are supported.");
        }

        if (compression != 0)
        {
            throw new NotSupportedException($"Cursor DIB compression mode {compression} is not supported; only BI_RGB is supported.");
        }

        if (width <= 0 || encodedHeight == 0 || encodedHeight == int.MinValue)
        {
            throw new InvalidDataException("Cursor DIB dimensions must be finite positive values.");
        }

        bool topDown = encodedHeight < 0;
        int combinedHeight = Math.Abs(encodedHeight);
        if ((combinedHeight & 1) != 0)
        {
            throw new InvalidDataException("Cursor DIB height must contain equally sized color and AND-mask planes.");
        }

        int height = combinedHeight / 2;
        ValidateImageDimensions((uint)width, (uint)height, entry);

        int colorStride = checked(width * 4);
        int maskStride = checked(((width + 31) / 32) * 4);
        int colorByteCount = checked(colorStride * height);
        int maskByteCount = checked(maskStride * height);
        int colorOffset = headerSize;
        int maskOffset = checked(colorOffset + colorByteCount);
        int requiredLength = checked(maskOffset + maskByteCount);
        if (requiredLength > payload.Length)
        {
            throw new InvalidDataException("Cursor DIB color or AND-mask plane is truncated.");
        }

        bool hasExplicitAlpha = false;
        ReadOnlySpan<byte> colorPlane = payload.Slice(colorOffset, colorByteCount);
        for (int offset = 3; offset < colorPlane.Length; offset += 4)
        {
            if (colorPlane[offset] != 0)
            {
                hasExplicitAlpha = true;
                break;
            }
        }

        byte[] pixels = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
        ReadOnlySpan<byte> maskPlane = payload.Slice(maskOffset, maskByteCount);
        for (int y = 0; y < height; y++)
        {
            int sourceRow = topDown ? y : height - 1 - y;
            ReadOnlySpan<byte> colorRow = colorPlane.Slice(sourceRow * colorStride, colorStride);
            ReadOnlySpan<byte> maskRow = maskPlane.Slice(sourceRow * maskStride, maskStride);
            Span<byte> destinationRow = pixels.AsSpan(y * colorStride, colorStride);
            for (int x = 0; x < width; x++)
            {
                int pixelOffset = x * 4;
                bool masked = (maskRow[x / 8] & (0x80 >> (x & 7))) != 0;
                destinationRow[pixelOffset] = colorRow[pixelOffset];
                destinationRow[pixelOffset + 1] = colorRow[pixelOffset + 1];
                destinationRow[pixelOffset + 2] = colorRow[pixelOffset + 2];
                destinationRow[pixelOffset + 3] = masked
                    ? (byte)0
                    : hasExplicitAlpha ? colorRow[pixelOffset + 3] : byte.MaxValue;
            }
        }

        Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        try
        {
            BitmapData bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(pixels, y * colorStride, IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride), colorStride);
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void ValidateImageDimensions(uint width, uint height, CursorEntry entry)
    {
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
        {
            throw new InvalidDataException("Cursor image dimensions must be finite positive values.");
        }

        if ((long)width * height > MaximumDecodedPixels)
        {
            throw new InvalidDataException($"Cursor image exceeds the {MaximumDecodedPixels}-pixel portable decode limit.");
        }

        if (width != entry.Width || height != entry.Height)
        {
            throw new InvalidDataException("Cursor image dimensions do not match its directory entry.");
        }
    }

    private enum CursorPayloadKind
    {
        Unsupported,
        Png,
        Dib,
    }

    private readonly record struct CursorEntry(
        int Width,
        int Height,
        int PayloadOffset,
        int PayloadLength,
        CursorPayloadKind Kind,
        int DirectoryIndex);
}
#endif
