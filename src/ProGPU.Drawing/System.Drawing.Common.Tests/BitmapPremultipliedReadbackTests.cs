using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Xunit;

namespace System.Drawing.Tests;

public sealed class BitmapPremultipliedReadbackTests
{
    // Original Microsoft output from all 32,896 valid byte channel/alpha pairs.
    // See docs/drawing-premultiplied-readback.md for source and artifact identity.
    private const string OriginalRawHash = "22877e4eeeb51d77d2ec1c4201d93f69f1d6e86d71fece77f2111692d8ba79ce";
    private const string OriginalStraightHash = "3d2b9524f0ff9a5e19c468a4568511c96d9ee4e2e67404901ef18de6beb42d71";
    private const int Extent = 256;

    [Fact]
    public void OwnedPremultipliedReadbackMatchesCompleteOriginalInventory()
    {
        using var bitmap = new Bitmap(Extent, Extent, PixelFormat.Format32bppPArgb);
        byte[] bgra = CreateOriginalPremultipliedInput();
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, Extent, Extent),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (int y = 0; y < Extent; y++)
                Marshal.Copy(bgra, y * Extent * 4, data.Scan0 + y * data.Stride, Extent * 4);
        }
        finally { bitmap.UnlockBits(data); }

        VerifyOriginalReadback(bitmap);
    }

    [Fact]
    public void ExternalPremultipliedReadbackMatchesCompleteOriginalInventory()
    {
        byte[] bgra = CreateOriginalPremultipliedInput();
        GCHandle pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(Extent, Extent, Extent * 4,
                PixelFormat.Format32bppPArgb, pin.AddrOfPinnedObject());
            VerifyOriginalReadback(bitmap);
        }
        finally { pin.Free(); }
    }

    private static void VerifyOriginalReadback(Bitmap bitmap)
    {
        Assert.Equal(OriginalRawHash, Hash(Read(bitmap, PixelFormat.Format32bppPArgb)));
        var pixels = new byte[Extent * Extent * 4];
        for (int y = 0; y < Extent; y++)
        {
            for (int x = 0; x < Extent; x++)
            {
                Color color = bitmap.GetPixel(x, y);
                int offset = (y * Extent + x) * 4;
                pixels[offset] = color.R;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.B;
                pixels[offset + 3] = color.A;
            }
        }

        Assert.Equal(OriginalStraightHash, Hash(pixels));
        Assert.Equal(OriginalStraightHash, Hash(Read(bitmap, PixelFormat.Format32bppArgb)));
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        using var decoded = new Bitmap(stream);
        Assert.Equal(OriginalStraightHash, Hash(Read(decoded, PixelFormat.Format32bppArgb)));
        Assert.Equal(OriginalRawHash, Hash(Read(bitmap, PixelFormat.Format32bppPArgb)));
    }

    private static byte[] CreateOriginalPremultipliedInput()
    {
        var pixels = new byte[Extent * Extent * 4];
        for (int alpha = 0; alpha < Extent; alpha++)
        {
            for (int p = 0; p < Extent; p++)
            {
                int offset = (alpha * Extent + p) * 4;
                pixels[offset + 3] = (byte)alpha;
                if (p > alpha) continue;
                pixels[offset] = (byte)((73 * p) % (alpha + 1));
                pixels[offset + 1] = (byte)(alpha - p);
                pixels[offset + 2] = (byte)p;
            }
        }

        return pixels;
    }

    private static byte[] Read(Bitmap bitmap, PixelFormat format)
    {
        var result = new byte[Extent * Extent * 4];
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, Extent, Extent), ImageLockMode.ReadOnly, format);
        try
        {
            var row = new byte[Extent * 4];
            for (int y = 0; y < Extent; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < Extent; x++)
                {
                    int source = x * 4, destination = (y * Extent + x) * 4;
                    result[destination] = row[source + 2];
                    result[destination + 1] = row[source + 1];
                    result[destination + 2] = row[source];
                    result[destination + 3] = row[source + 3];
                }
            }
        }
        finally { bitmap.UnlockBits(data); }

        return result;
    }

    private static string Hash(byte[] pixels) => Convert.ToHexStringLower(SHA256.HashData(pixels));
}
