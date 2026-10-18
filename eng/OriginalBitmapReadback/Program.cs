using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

if (!OperatingSystem.IsWindows() || args.Length != 1)
    throw new InvalidOperationException("Original Windows reference and an explicit output directory required.");
string output = Path.GetFullPath(args[0]);
if (Directory.Exists(output)) throw new IOException("Refusing existing output directory.");
Directory.CreateDirectory(output);
var watch = Stopwatch.StartNew();
const int Width = 256, Height = 256;
var receipt = new Dictionary<string, object>
{
    ["schema"] = "original-gdiplus-premultiplied-readback-v1",
    ["qualified"] = false, ["inputEvents"] = 0, ["desktopWindows"] = 0,
    ["width"] = Width, ["height"] = Height, ["validPairs"] = 32896,
    ["sourcePixelFormat"] = "Format32bppPArgb",
    ["sourceRule"] = "At row alpha and column p<=alpha: R=p, G=alpha-p, B=(73*p)%(alpha+1), A=alpha. Remaining columns have RGB=0 and A=alpha. These are raw premultiplied bytes.",
    ["runtime"] = RuntimeInformation.FrameworkDescription,
    ["os"] = RuntimeInformation.OSDescription,
    ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString()
};
var files = new List<object>();
void Deadline()
{
    if (watch.Elapsed.TotalSeconds >= 45) throw new TimeoutException("Original 45-second observation deadline.");
}
void Save(string name, byte[] data)
{
    Deadline();
    File.WriteAllBytes(Path.Combine(output, name), data);
    files.Add(new { name, bytes = data.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(data)) });
}
byte[] Read(Bitmap bitmap, PixelFormat format)
{
    var pixels = new byte[Width * Height * 4];
    BitmapData data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, format);
    try
    {
        if (Math.Abs(data.Stride) < Width * 4) throw new InvalidDataException("Incomplete native row.");
        var row = new byte[Width * 4];
        for (int y = 0; y < Height; y++)
        {
            Deadline();
            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
            for (int x = 0; x < Width; x++)
            {
                int source = x * 4, destination = (y * Width + x) * 4;
                pixels[destination] = row[source + 2];
                pixels[destination + 1] = row[source + 1];
                pixels[destination + 2] = row[source];
                pixels[destination + 3] = row[source + 3];
            }
        }
    }
    finally { bitmap.UnlockBits(data); }
    return pixels;
}
try
{
    using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
    BitmapData write = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
    try
    {
        if (Math.Abs(write.Stride) < Width * 4) throw new InvalidDataException("Incomplete native row.");
        var row = new byte[Width * 4];
        for (int a = 0; a < Height; a++)
        {
            Deadline();
            Array.Clear(row);
            for (int p = 0; p < Width; p++)
            {
                int i = p * 4;
                row[i + 3] = (byte)a;
                if (p > a) continue;
                row[i] = (byte)((73 * p) % (a + 1));
                row[i + 1] = (byte)(a - p);
                row[i + 2] = (byte)p;
            }
            Marshal.Copy(row, 0, write.Scan0 + a * write.Stride, row.Length);
        }
    }
    finally { bitmap.UnlockBits(write); }
    byte[] original = Read(bitmap, PixelFormat.Format32bppPArgb);
    Save("original-premultiplied.rgba", original);
    var getPixel = new byte[Width * Height * 4];
    for (int y = 0; y < Height; y++)
    {
        Deadline();
        for (int x = 0; x < Width; x++)
        {
            Color color = bitmap.GetPixel(x, y);
            int i = (y * Width + x) * 4;
            getPixel[i] = color.R; getPixel[i + 1] = color.G;
            getPixel[i + 2] = color.B; getPixel[i + 3] = color.A;
        }
    }
    Save("get-pixel.rgba", getPixel);
    Save("argb-lock.rgba", Read(bitmap, PixelFormat.Format32bppArgb));
    using (var stream = new MemoryStream())
    {
        bitmap.Save(stream, ImageFormat.Png);
        Save("original.png", stream.ToArray());
        stream.Position = 0;
        using var decoded = new Bitmap(stream);
        Save("decoded-argb-lock.rgba", Read(decoded, PixelFormat.Format32bppArgb));
    }
    byte[] after = Read(bitmap, PixelFormat.Format32bppPArgb);
    Save("after-premultiplied.rgba", after);
    if (!original.AsSpan().SequenceEqual(after)) throw new InvalidDataException("Read-only source changed.");
    Deadline();
    receipt["passed"] = true;
}
catch (Exception error)
{
    receipt["passed"] = false;
    receipt["error"] = error.ToString();
    throw;
}
finally
{
    receipt["elapsedSeconds"] = watch.Elapsed.TotalSeconds;
    receipt["files"] = files;
    File.WriteAllText(Path.Combine(output, "reference.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
}
