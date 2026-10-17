// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>Reads actual native scrollbar image resources without rendering into a capture.</summary>
internal static unsafe partial class WindowsThemeBitmapSource
{
    internal static bool TryDraw(Graphics graphics, string className, int part, int state,
        Rectangle bounds, Rectangle? clip)
    {
        // Other parts, source DPIs and native sizing policies have independent
        // contracts. In particular a missing SizingType is not Stretch.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063)
            || !string.Equals(className, "SCROLLBAR", StringComparison.OrdinalIgnoreCase)
            || part is not (1 or 2) || state < 1 || state > (part == 1 ? 16 : 4)
            || graphics.DpiX != 96 || graphics.DpiY != 96)
            return false;

        RetryRetirements();
        if (part == 1) return TryDrawArrow(graphics, state, bounds, clip);
        nint theme = OpenThemeDataForDpi(0, "SCROLLBAR", 96);
        if (theme == 0) return false;
        using NativeResources resources = new(theme);
        try
        {
            // Native opacity is authoritative. RGB32's unused fourth byte is
            // never treated as proof that an alpha-bearing theme is opaque.
            // This API's state parameter is reserved and must be zero; the
            // actual state remains authoritative for every resource query below.
            if (IsThemePartDefined(theme, part, 0) == 0
                || IsThemeBackgroundPartiallyTransparent(theme, part, state) != 0
                || !EnumEquals(theme, part, state, 4001, 0) // BGTYPE: image
                || !EnumEquals(theme, part, state, 4004, 1) // SIZINGTYPE: stretch
                || !EnumEquals(theme, part, state, 4011, 0) // IMAGELAYOUT: vertical
                || !EnumEquals(theme, part, state, 4013, 2) // IMAGESELECTTYPE: DPI
                || GetThemeInt(theme, part, state, 2420, out int minDpi) < 0 || minDpi != 96
                || GetThemeInt(theme, part, state, 2401, out int count) < 0 || count != 5
                || GetThemeMargins(theme, 0, part, state, 3601, 0, out NativeMargins nativeMargins) < 0)
                return false;

            // GBF_COPY owns a fresh, unselected bitmap. Initialize the result:
            // failed calls are allowed to leave output storage untouched.
            // Original thumb resources use automatic property zero. Explicit
            // TMT_DIBDATA is unavailable for these same original parts; this is
            // source selection, not a retry after a failed bitmap request.
            int result = GetThemeBitmap(theme, part, state, 0, 2, ref resources._bitmap);
            if (result < 0 || resources._bitmap == 0) return false;
            if (GetObjectW(resources._bitmap, sizeof(NativeBitmap), out NativeBitmap description) != sizeof(NativeBitmap)
                || description.Width <= 0 || description.Height <= 0 || description.Planes != 1
                || description.Height % count != 0
                || (long)description.Width * description.Height > 4 * 1024 * 1024)
                return false;

            int height = description.Height / count;
            LibreVisualStyleMargins margins = new(nativeMargins.Left, nativeMargins.Top,
                nativeMargins.Right, nativeMargins.Bottom);
            // The observed source has one-pixel stretch bands. Do not infer
            // interpolation of larger bands or contraction of fixed borders.
            if (margins.Left < 0 || margins.Top < 0 || margins.Right < 0 || margins.Bottom < 0
                || (long)margins.Left + margins.Right != description.Width - 1
                || (long)margins.Top + margins.Bottom != height - 1
                || bounds.Width < margins.Left + margins.Right || bounds.Height < margins.Top + margins.Bottom)
                return false;

            resources._dc = CreateCompatibleDC(0);
            if (resources._dc == 0) return false;
            byte[] pixels = new byte[checked(description.Width * description.Height * 4)];
            BitmapInfo info = new()
            {
                Header = new BitmapHeader
                {
                    Size = (uint)sizeof(BitmapHeader), Width = description.Width, Height = -description.Height,
                    Planes = 1, BitCount = 32
                }
            };
            fixed (byte* data = pixels)
            {
                if (GetDIBits(resources._dc, resources._bitmap, 0, (uint)description.Height, data, ref info, 0) != description.Height
                    || info.Header.Width != description.Width || info.Header.Height != -description.Height
                    || info.Header.Planes != 1 || info.Header.BitCount != 32 || info.Header.Compression != 0)
                    return false;
            }

            using Bitmap image = CreateOpaqueImage(description.Width, description.Height, pixels);
            return ProGpuThemeBitmapRenderer.TryDraw(graphics, image,
                new Rectangle(0, (state - 1) * height, description.Width, height), margins, bounds, clip);
        }
        catch (Exception error) { resources.Primary = error; throw; }
    }

    [ThreadStatic]
    private static List<NativeResources>? s_pendingRetirement;

    private static void RetryRetirements()
    {
        List<NativeResources>? pending = s_pendingRetirement;
        s_pendingRetirement = null;
        if (pending is null) return;
        List<Exception> failures = [];
        foreach (NativeResources resources in pending)
        {
            try { resources.Dispose(); }
            catch (Exception error) { failures.Add(error); }
        }

        if (failures.Count != 0) throw new AggregateException(failures);
    }

    private sealed class NativeResources(nint theme) : IDisposable
    {
        private nint _theme = theme;
        internal nint _bitmap;
        internal nint _dc;
        internal nint _module;
        internal Exception? Primary { get; set; }

        public void Dispose()
        {
            List<Exception>? failures = null;
            if (_dc != 0)
            {
                if (DeleteDC(_dc) != 0) _dc = 0;
                else (failures ??= []).Add(new InvalidOperationException("Theme conversion DC did not retire."));
            }

            if (_bitmap != 0)
            {
                if (DeleteObject(_bitmap) != 0) _bitmap = 0;
                else (failures ??= []).Add(new InvalidOperationException("Owned theme bitmap did not retire."));
            }

            if (_theme != 0)
            {
                if (CloseThemeData(_theme) >= 0) _theme = 0;
                else (failures ??= []).Add(new InvalidOperationException("Theme handle did not retire."));
            }

            if (_module != 0)
            {
                if (FreeLibrary(_module) != 0) _module = 0;
                else (failures ??= []).Add(new InvalidOperationException("Theme resource module did not retire."));
            }

            if (failures is null) return;
            // Keep each actual failed owner for a creating-thread retry. Never
            // release successful owners twice or hide the original paint error.
            (s_pendingRetirement ??= []).Add(this);
            if (Primary is not null) failures.Insert(0, Primary);
            throw new AggregateException(failures);
        }
    }

    internal static Bitmap CreateOpaqueImage(int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 != pixels.Length)
            throw new ArgumentException("A complete top-down native RGB32 image is required.");
        Bitmap image = new(width, height, PixelFormat.Format32bppRgb);
        try
        {
            BitmapData target = image.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (int row = 0; row < height; row++)
                    pixels.Slice(row * width * 4, width * 4).CopyTo(new Span<byte>((void*)(target.Scan0 + row * target.Stride), width * 4));
            }
            finally { image.UnlockBits(target); }
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    private static bool EnumEquals(nint theme, int part, int state, int property, int expected)
        => GetThemeEnumValue(theme, part, state, property, out int value) >= 0 && value == expected;

    #pragma warning disable IDE1006 // Native ABI field names mirror the Win32 structures.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        internal int Type, Width, Height, WidthBytes;
        internal ushort Planes, BitsPixel;
        internal nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMargins { internal int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, BitCount;
        internal uint Compression, SizeImage;
        internal int XPelsPerMeter, YPelsPerMeter;
        internal uint ColorsUsed, ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { internal BitmapHeader Header; internal uint Color; }

    #pragma warning restore IDE1006

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenThemeDataForDpi(nint window, string className, uint dpi);
    [LibraryImport("uxtheme.dll")]
    private static partial int CloseThemeData(nint theme);
    [LibraryImport("uxtheme.dll")]
    private static partial int IsThemePartDefined(nint theme, int part, int state);
    [LibraryImport("uxtheme.dll")]
    private static partial int IsThemeBackgroundPartiallyTransparent(nint theme, int part, int state);
    [LibraryImport("uxtheme.dll")]
    private static partial int GetThemeEnumValue(nint theme, int part, int state, int property, out int value);
    [LibraryImport("uxtheme.dll")]
    private static partial int GetThemeInt(nint theme, int part, int state, int property, out int value);
    [LibraryImport("uxtheme.dll")]
    private static partial int GetThemeMargins(nint theme, nint dc, int part, int state, int property, nint bounds, out NativeMargins margins);
    [LibraryImport("uxtheme.dll")]
    private static partial int GetThemeBitmap(nint theme, int part, int state, int property, uint flags, ref nint bitmap);
    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint bitmap, int size, out NativeBitmap description);
    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint count, byte* bits, ref BitmapInfo info, uint usage);
    [LibraryImport("gdi32.dll")]
    private static partial int DeleteObject(nint bitmap);
    [LibraryImport("gdi32.dll")]
    private static partial int DeleteDC(nint dc);
}
