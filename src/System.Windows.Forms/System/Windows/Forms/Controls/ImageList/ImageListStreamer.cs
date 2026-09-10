// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Private.Windows.BinaryFormat;
using System.Runtime.Serialization;
#if LIBREWINFORMS_PORTABLE
using System.Buffers.Binary;
using System.Drawing.Imaging;
using Bitmap = System.Drawing.Bitmap;
using Graphics = System.Drawing.Graphics;
using GraphicsUnit = System.Drawing.GraphicsUnit;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;
#else
using Windows.Win32.System.Com;
#endif

namespace System.Windows.Forms;

[Serializable] // This type is participating in resx serialization scenarios.
public sealed class ImageListStreamer : ISerializable, IDisposable
{
    // Compressed magic header. If we see this, the image stream is compressed.
    private static ReadOnlySpan<byte> HeaderMagic => "MSFt"u8;
#if !LIBREWINFORMS_PORTABLE
    private static readonly Lock s_syncObject = new();
#endif

    private readonly ImageList? _imageList;
#if !LIBREWINFORMS_PORTABLE
    private ImageList.NativeImageList? _nativeImageList;
#endif
#if LIBREWINFORMS_PORTABLE
    private List<Bitmap>? _portableImages;
    private Size _portableImageSize;
#endif

    internal ImageListStreamer(ImageList imageList) => _imageList = imageList;

    // Used by binary serialization
    private ImageListStreamer(SerializationInfo info, StreamingContext context)
    {
        if (info.GetValue<byte[]>("Data") is { } data)
        {
            Deserialize(data);
        }
    }

    internal ImageListStreamer(byte[] data) => Deserialize(data);

    /// <summary>
    ///  Compresses the given input, returning a new array that represents the compressed data.
    /// </summary>
    private static byte[] Compress(ReadOnlySpan<byte> input)
    {
        int length = RunLengthEncoder.GetEncodedLength(input) + HeaderMagic.Length;
        byte[] output = new byte[length];
        SpanWriter<byte> writer = new(output);
        writer.TryWrite(HeaderMagic);
        RunLengthEncoder.TryEncode(input, writer.Span[writer.Position..], out int written);
        Debug.Assert(written == length - HeaderMagic.Length, "RLE compression failure");
        return output;
    }

    /// <summary>
    ///  Decompresses the given input, returning a new array that represents the uncompressed data.
    /// </summary>
    private static byte[] Decompress(byte[] input)
    {
        SpanReader<byte> reader = new(input);
        if (!reader.TryAdvancePast(HeaderMagic))
        {
            // Not compressed, return the original
            return input;
        }

        ReadOnlySpan<byte> remaining = reader.Span[reader.Position..];
        int length = RunLengthEncoder.GetDecodedLength(remaining);
        byte[] output = new byte[length];
        RunLengthEncoder.TryDecode(remaining, output, out int written);
        Debug.Assert(written == length, "RLE decompression failure");
        return output;
    }

    private void Deserialize(byte[] data)
    {
#if LIBREWINFORMS_PORTABLE
        DeserializePortable(Decompress(data));
#else
        // We enclose this ImageList handle create in a theming scope.
        using ThemingScope scope = new(Application.UseVisualStyles);
        using MemoryStream memoryStream = new(Decompress(data));

        lock (s_syncObject)
        {
            CommonControlInitializer.Initialize();
            _nativeImageList = new ImageList.NativeImageList(new ComManagedStream(memoryStream));
        }

        if (_nativeImageList.HIMAGELIST.IsNull)
        {
            throw new InvalidOperationException(SR.ImageListStreamerLoadFailed);
        }
#endif
    }

#if LIBREWINFORMS_PORTABLE
    private void DeserializePortable(byte[] data)
    {
        // ImageList_Write stores a packed ILHEAD followed by a color BMP and,
        // when ILC_MASK is set, a monochrome mask BMP. The backing bitmap uses
        // the common-controls four-column tile layout.
        const int HeaderSize = 28;
        const ushort Magic = 0x4C49; // "IL"
        const ushort Version = 0x0101;
        const ushort MaskFlag = 0x0001;

        ReadOnlySpan<byte> payload = data;
        if (payload.Length < HeaderSize
            || BinaryPrimitives.ReadUInt16LittleEndian(payload) != Magic
            || BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]) != Version)
        {
            throw new InvalidOperationException(SR.ImageListStreamerLoadFailed);
        }

        int imageCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
        int imageWidth = BinaryPrimitives.ReadUInt16LittleEndian(payload[10..]);
        int imageHeight = BinaryPrimitives.ReadUInt16LittleEndian(payload[12..]);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(payload[18..]);
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            throw new InvalidOperationException(SR.ImageListStreamerLoadFailed);
        }

        int offset = HeaderSize;
        using Bitmap colorStrip = ReadPortableBitmap(payload, ref offset);
        using Bitmap? maskStrip = (flags & MaskFlag) != 0
            ? ReadPortableBitmap(payload, ref offset)
            : null;

        _portableImageSize = new Size(imageWidth, imageHeight);
        _portableImages = new List<Bitmap>(imageCount);
        try
        {
            for (int index = 0; index < imageCount; index++)
            {
                int sourceX = (index % 4) * imageWidth;
                int sourceY = (index / 4) * imageHeight;
                if (sourceX + imageWidth > colorStrip.Width || sourceY + imageHeight > colorStrip.Height)
                {
                    throw new InvalidOperationException(SR.ImageListStreamerLoadFailed);
                }

                Bitmap image = new(imageWidth, imageHeight, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    graphics.DrawImage(
                        colorStrip,
                        new Rectangle(0, 0, imageWidth, imageHeight),
                        sourceX,
                        sourceY,
                        imageWidth,
                        imageHeight,
                        GraphicsUnit.Pixel);
                }

                if (maskStrip is not null)
                {
                    ApplyPortableMask(image, maskStrip, sourceX, sourceY);
                }

                _portableImages.Add(image);
            }
        }
        catch
        {
            DisposePortableImages();
            throw;
        }
    }

    private static Bitmap ReadPortableBitmap(ReadOnlySpan<byte> payload, ref int offset)
    {
        const int BitmapFileHeaderSize = 14;
        const int BitmapInfoHeaderSize = 40;
        if (offset < 0 || payload.Length - offset < BitmapFileHeaderSize + BitmapInfoHeaderSize
            || BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]) != 0x4D42)
        {
            throw new InvalidOperationException(SR.ImageListStreamerLoadFailed);
        }

        int pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[(offset + 10)..]);
        int infoHeaderSize = BinaryPrimitives.ReadInt32LittleEndian(payload[(offset + BitmapFileHeaderSize)..]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(payload[(offset + 18)..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(payload[(offset + 22)..]);
        int bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + 28)..]);
        int imageLength = BinaryPrimitives.ReadInt32LittleEndian(payload[(offset + 34)..]);
        if (infoHeaderSize < BitmapInfoHeaderSize
            || pixelOffset < BitmapFileHeaderSize
            || pixelOffset < (long)BitmapFileHeaderSize + infoHeaderSize
            || width <= 0
            || height == 0
            || bitsPerPixel <= 0
            || imageLength < 0)
        {
            throw new InvalidOperationException(SR.ImageListStreamerLoadFailed);
        }

        if (imageLength == 0)
        {
            long stride = ((long)width * bitsPerPixel + 31) / 32 * 4;
            long calculatedLength = stride * Math.Abs((long)height);
            if (calculatedLength > int.MaxValue)
            {
                throw new InvalidOperationException(SR.ImageListStreamerLoadFailed);
            }

            imageLength = (int)calculatedLength;
        }

        long requiredLength = (long)pixelOffset + imageLength;
        if (requiredLength > payload.Length - offset || requiredLength > int.MaxValue)
        {
            throw new InvalidOperationException(SR.ImageListStreamerLoadFailed);
        }

        int bitmapLength = (int)requiredLength;
        byte[] bitmapData = payload.Slice(offset, bitmapLength).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bitmapData.AsSpan(2), bitmapLength);
        offset += bitmapLength;
        using MemoryStream stream = new(bitmapData, writable: false);
        return new Bitmap(stream);
    }

    private static void ApplyPortableMask(Bitmap image, Bitmap maskStrip, int sourceX, int sourceY)
    {
        if (sourceX + image.Width > maskStrip.Width || sourceY + image.Height > maskStrip.Height)
        {
            throw new InvalidOperationException(SR.ImageListStreamerLoadFailed);
        }

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Color mask = maskStrip.GetPixel(sourceX + x, sourceY + y);
                if (mask.R >= 128 && mask.G >= 128 && mask.B >= 128)
                {
                    Color color = image.GetPixel(x, y);
                    image.SetPixel(x, y, Color.FromArgb(0, color.R, color.G, color.B));
                }
            }
        }
    }

    internal IReadOnlyList<Bitmap>? GetPortableImages() => _portableImages;

    internal Size PortableImageSize => _portableImageSize;

    private void DisposePortableImages()
    {
        if (_portableImages is null)
        {
            return;
        }

        foreach (Bitmap image in _portableImages)
        {
            image.Dispose();
        }

        _portableImages = null;
    }
#endif

    public void GetObjectData(SerializationInfo si, StreamingContext context) =>
        si.AddValue("Data", Serialize());

    internal byte[] Serialize()
    {
        using MemoryStream stream = new();
        if (!WriteImageList(stream))
        {
            throw new InvalidOperationException(SR.ImageListStreamerSaveFailed);
        }

        ReadOnlySpan<byte> buffer = stream.GetBuffer().AsSpan()[..(int)stream.Length];
        return Compress(buffer);
    }

    internal void GetObjectData(Stream stream)
    {
        if (!WriteImageList(stream))
        {
            throw new InvalidOperationException(SR.ImageListStreamerSaveFailed);
        }
    }

#if !LIBREWINFORMS_PORTABLE
    internal ImageList.NativeImageList? GetNativeImageList() => _nativeImageList;
#endif

    private bool WriteImageList(Stream stream)
    {
#if LIBREWINFORMS_PORTABLE
        if (_imageList is null || _imageList.Images.Count == 0 || !stream.CanWrite)
        {
            return false;
        }

        // Match the down-level common-controls stream consumed by ImageList_Read:
        // a packed ILHEAD followed by a four-column tiled color bitmap. A 32-bpp
        // bitmap retains alpha, so no separate monochrome mask is necessary.
        const int HeaderSize = 28;
        const ushort Magic = 0x4C49; // "IL"
        const ushort Version = 0x0101;
        const ushort Color32Flag = 0x0020;
        const uint TransparentBackground = 0xFFFFFFFF;

        int imageCount = _imageList.Images.Count;
        Size imageSize = _imageList.ImageSize;
        if (imageCount > ushort.MaxValue
            || imageSize.Width <= 0
            || imageSize.Width > ushort.MaxValue
            || imageSize.Height <= 0
            || imageSize.Height > ushort.MaxValue)
        {
            return false;
        }

        int rowCount = checked((imageCount + 3) / 4);
        using Bitmap colorStrip = new(
            checked(imageSize.Width * 4),
            checked(imageSize.Height * rowCount),
            PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(colorStrip))
        {
            graphics.Clear(Color.Transparent);
            for (int index = 0; index < imageCount; index++)
            {
                using System.Drawing.Image image = _imageList.Images[index];
                graphics.DrawImage(
                    image,
                    new Rectangle(
                        (index % 4) * imageSize.Width,
                        (index / 4) * imageSize.Height,
                        imageSize.Width,
                        imageSize.Height),
                    0,
                    0,
                    image.Width,
                    image.Height,
                    GraphicsUnit.Pixel);
            }
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(header, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)imageCount);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)imageCount);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[10..], (ushort)imageSize.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], (ushort)imageSize.Height);
        BinaryPrimitives.WriteUInt32LittleEndian(header[14..], TransparentBackground);
        BinaryPrimitives.WriteUInt16LittleEndian(header[18..], Color32Flag);
        for (int offset = 20; offset < HeaderSize; offset += sizeof(short))
        {
            BinaryPrimitives.WriteInt16LittleEndian(header[offset..], -1);
        }

        stream.Write(header);
        colorStrip.Save(stream, ImageFormat.Bmp);
        return true;
#else
        HandleRef<HIMAGELIST> handle = default;
        if (_imageList is not null)
        {
            handle = new(_imageList, (HIMAGELIST)_imageList.Handle);
        }
        else if (_nativeImageList is not null)
        {
            handle = new(_nativeImageList, _nativeImageList.HIMAGELIST);
        }

        if (handle.IsNull)
        {
            return false;
        }

        try
        {
            return PInvoke.ImageList.WriteEx(
                handle,
                IMAGE_LIST_WRITE_STREAM_FLAGS.ILP_DOWNLEVEL,
                stream).Succeeded;
        }
        catch (EntryPointNotFoundException)
        {
            // Not running on ComCtl32 v6, fall back to the old API.
        }

        return PInvoke.ImageList.Write(handle, stream);
#endif
    }

    public void Dispose()
    {
#if LIBREWINFORMS_PORTABLE
        DisposePortableImages();
#else
        _nativeImageList?.Dispose();
        _nativeImageList = null;
#endif
    }
}
