// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.InteropServices;

namespace LibreWinForms.ProGPU;

internal static unsafe partial class WindowsThemeBitmapSource
{
    private readonly record struct ThemeIdentity(string Path, string Color, string Size);

    private static bool TryDrawArrow(Graphics graphics, int state, Rectangle bounds, Rectangle? clip)
    {
        if (!TryReadThemeIdentity(out ThemeIdentity identity)) return false;
        nint theme = OpenThemeDataForDpi(0, "SCROLLBAR", 96);
        if (theme == 0) return false;
        using NativeResources resources = new(theme);
        try
        {
            if (IsThemePartDefined(theme, 1, 0) == 0
                || !EnumEquals(theme, 1, state, 4001, 0) || !EnumEquals(theme, 1, state, 4004, 1)
                || !EnumEquals(theme, 1, state, 4011, 0) || !EnumEquals(theme, 1, state, 4012, 1)
                || !EnumEquals(theme, 1, state, 4013, 1)
                || GetThemeInt(theme, 1, state, 2401, out int count) < 0 || count != 20)
                return false;

            // Map the actual selected source file as resources, never execute
            // it, install a theme or render a captured replacement image.
            resources._module = LoadLibraryExW(identity.Path, 0, 0x22);
            if (resources._module == 0) return false;
            byte[]? version = ReadResource(resources._module, "PACKTHEM_VERSION", null, 1);
            byte[]? variants = ReadResource(resources._module, "VMAP", "VMAP");
            if (version is not [4, 0] || variants is null
                || !WindowsThemeResourceTable.TryGetVariant(variants, identity.Color, identity.Size, out string? variant))
                return false;
            byte[]? classes = ReadResource(resources._module, "CMAP", "CMAP");
            byte[]? bases = ReadResource(resources._module, "BCMAP", "BCMAP");
            byte[]? properties = ReadResource(resources._module, "VARIANT", variant);
            if (classes is null || bases is null || properties is null
                || !WindowsThemeResourceTable.TryReadArrowImages(classes, bases, properties, out var selected))
                return false;

            // The first original size-selected family was observed at its
            // explicit minimum target. Larger selections and contraction keep
            // separate native contracts; do not infer them from IMAGEFILE1.
            if (bounds.Size != selected.MinimumSize
                || (long)bounds.X + bounds.Width > int.MaxValue
                || (long)bounds.Y + bounds.Height > int.MaxValue) return false;
            byte[]? backgroundBytes = ReadResource(resources._module, "IMAGE", null, (ushort)selected.BackgroundId);
            byte[]? selectedBytes = ReadResource(resources._module, "IMAGE", null, (ushort)selected.SelectedId);
            if (backgroundBytes is null || selectedBytes is null
                || !TryReadPngSize(backgroundBytes, out Size backgroundSize)
                || !TryReadPngSize(selectedBytes, out Size selectedSize)
                || backgroundSize.Height % count != 0 || selectedSize.Height % count != 0)
                return false;
            int height = backgroundSize.Height / count, selectedHeight = selectedSize.Height / count;
            if (backgroundSize.Width != bounds.Width || height != bounds.Height
                || (long)selected.Margins.Left + selected.Margins.Right != backgroundSize.Width - 1
                || (long)selected.Margins.Top + selected.Margins.Bottom != height - 1
                || selectedSize.Width != bounds.Width - 4 || selectedHeight != bounds.Height - 4)
                return false;

            using MemoryStream backgroundStream = new(backgroundBytes, writable: false);
            using MemoryStream selectedStream = new(selectedBytes, writable: false);
            using Bitmap background = new(backgroundStream);
            using Bitmap image = new(selectedStream);
            if (background.Size != backgroundSize || image.Size != selectedSize
                || !TryReadThemeIdentity(out ThemeIdentity current) || current != identity) return false;
            // All metadata, decoded images and both frames are prepared before
            // the first command. The PNG retains its own real alpha coverage.
            if (!ProGpuThemeBitmapRenderer.TryDraw(graphics, background,
                new(0, (state - 1) * height, backgroundSize.Width, height), selected.Margins, bounds, clip)) return false;
            return ProGpuThemeBitmapRenderer.TryDraw(graphics, image,
                new(0, (state - 1) * selectedHeight, selectedSize.Width, selectedHeight), default,
                new(bounds.X + 2, bounds.Y + 2, selectedSize.Width, selectedHeight), clip);
        }
        catch (Exception error) { resources.Primary = error; throw; }
    }

    internal static bool TryReadPngSize(ReadOnlySpan<byte> bytes, out Size size)
    {
        size = default;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature)
            || BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8, 4)) != 13
            || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
        uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        if (width == 0 || height == 0 || (ulong)width * height > 4 * 1024 * 1024) return false;
        size = new((int)width, (int)height);
        return true;
    }

    private static bool TryReadThemeIdentity(out ThemeIdentity identity)
    {
        identity = default;
        Span<char> path = stackalloc char[1024], color = stackalloc char[256], size = stackalloc char[256];
        path.Clear(); color.Clear(); size.Clear();
        fixed (char* p = path, c = color, s = size)
        {
            if (GetCurrentThemeName(p, path.Length, c, color.Length, s, size.Length) < 0) return false;
        }

        int pathEnd = path.IndexOf('\0'), colorEnd = color.IndexOf('\0'), sizeEnd = size.IndexOf('\0');
        if (pathEnd <= 0 || colorEnd <= 0 || sizeEnd <= 0) return false;
        string file = new(path[..pathEnd]);
        if (!Path.IsPathFullyQualified(file)) return false;
        identity = new(file, new(color[..colorEnd]), new(size[..sizeEnd]));
        return true;
    }

    private static byte[]? ReadResource(nint module, string type, string? name, ushort id = 0)
    {
        nint resource;
        fixed (char* text = name)
            resource = FindResourceW(module, name is null ? (nint)id : (nint)text, type);
        if (resource == 0) return null;
        uint size = SizeofResource(module, resource);
        if (size is 0 or > 16 * 1024 * 1024) return null;
        nint loaded = LoadResource(module, resource);
        if (loaded == 0) return null;
        nint data = LockResource(loaded);
        if (data == 0) return null;
        byte[] bytes = new byte[(int)size];
        Marshal.Copy(data, bytes, 0, bytes.Length);
        // LoadResource/LockResource borrow module storage; neither has a separate
        // free operation. NativeResources retains the actual module until now.
        return bytes;
    }

    [LibraryImport("uxtheme.dll")]
    private static partial int GetCurrentThemeName(char* path, int pathCount, char* color, int colorCount, char* size, int sizeCount);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryExW(string path, nint file, uint flags);
    [LibraryImport("kernel32.dll")]
    private static partial int FreeLibrary(nint module);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindResourceW(nint module, nint name, string type);
    [LibraryImport("kernel32.dll")]
    private static partial uint SizeofResource(nint module, nint resource);
    [LibraryImport("kernel32.dll")]
    private static partial nint LoadResource(nint module, nint resource);
    [LibraryImport("kernel32.dll")]
    private static partial nint LockResource(nint resource);
}
