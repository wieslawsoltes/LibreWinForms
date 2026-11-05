// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.Drawing;
using System.Text;
using LibreWinForms.Platform;

namespace LibreWinForms.ProGPU;

/// <summary>Reads bounded original version-four theme resource metadata.</summary>
internal static class WindowsThemeResourceTable
{
    private static readonly UnicodeEncoding s_unicode = new(false, false, true);

    internal readonly record struct ArrowImages(int BackgroundId, int SelectedId, int Count,
        Size MinimumSize, LibreVisualStyleMargins Margins);

    internal static bool TryGetVariant(ReadOnlySpan<byte> map, string color, string size, out string? variant)
    {
        variant = null;
        string? candidate = null;
        int cursor = 0, count = 0;
        while (cursor < map.Length)
        {
            if (++count > 128 || !ReadCountedString(map, ref cursor, out string? name)
                || !ReadCountedString(map, ref cursor, out string? sourceSize)
                || !ReadCountedString(map, ref cursor, out string? sourceColor)) return false;
            if (sourceSize == size && sourceColor == color)
            {
                if (candidate is not null) return false;
                candidate = name;
            }
        }

        variant = candidate;
        return candidate is not null;
    }

    internal static bool TryReadArrowImages(ReadOnlySpan<byte> classMap, ReadOnlySpan<byte> baseMap,
        ReadOnlySpan<byte> properties, out ArrowImages images)
    {
        images = default;
        if (classMap.Length > 1024 * 1024 || properties.Length > 16 * 1024 * 1024
            || !ReadClasses(classMap, out int classes, out int selectedClass, out int baseOffset)
            || selectedClass < baseOffset || baseMap.Length < 4) return false;
        int baseCount = ReadInt(baseMap, 0);
        if (baseCount != classes - baseOffset || baseMap.Length != 4L + baseCount * 4L) return false;
        int[] parents = new int[baseCount];
        for (int i = 0; i < parents.Length; i++)
        {
            int parent = ReadInt(baseMap, 4 + i * 4);
            if (parent < -1 || parent >= baseCount) return false;
            parents[i] = parent;
        }

        // The original base map omits leading documentation/variant names.
        // Keep its actual relative class indices, including ScrollbarStyle.
        // ReadClasses bounds this scratch space to 4096 integers. Each origin
        // has its own positive visit marker, so shared ancestors remain valid
        // while a repeat within one original path still rejects a cycle.
        Span<int> visited = stackalloc int[baseCount];
        visited.Clear();
        for (int i = 0; i < parents.Length; i++)
        {
            int depth = 0, marker = i + 1;
            for (int current = i; current >= 0; current = parents[current])
            {
                if (depth++ >= 64 || visited[current] == marker) return false;
                visited[current] = marker;
            }
        }

        Dictionary<int, int> ancestry = [];
        for (int current = selectedClass - baseOffset; current >= 0; current = parents[current])
            ancestry.Add(current + baseOffset, ancestry.Count);

        // Other classes can contain ordered duplicate records. Their bounds and
        // identifiers are validated, but their override semantics are not ours.
        // The selected arrow family must be explicit and unambiguous.
        Dictionary<int, (int Kind, int Reference, byte[] Value, int Depth)> selected = [];
        HashSet<(int Class, int Property)> seen = [];
        int cursor = 0, records = 0;
        while (cursor < properties.Length)
        {
            if (++records > 65536 || properties.Length - cursor < 32) return false;
            ReadOnlySpan<byte> header = properties.Slice(cursor, 32);
            int name = ReadInt(header, 0), kind = ReadInt(header, 4), cls = ReadInt(header, 8);
            int part = ReadInt(header, 12), state = ReadInt(header, 16), reference = ReadInt(header, 20);
            int reserved = ReadInt(header, 24), size = ReadInt(header, 28);
            if (name <= 0 || cls < 0 || cls >= classes || part < 0 || state < 0
                || reference < 0 || reserved != 0 || size is < 0 or > 16 * 1024 * 1024) return false;
            bool headerValue = kind is 206 or 210 or 213 or 243;
            if (!headerValue && kind is not (200 or 201 or 202 or 203 or 204 or 205 or 207 or 208 or 209 or 211 or 241))
                return false;
            int length = headerValue || reference != 0 ? 0 : size;
            if (length > properties.Length - cursor - 32) return false;
            ReadOnlySpan<byte> value = properties.Slice(cursor + 32, length);
            if (!headerValue && reference == 0)
            {
                int fixedSize = kind switch
                {
                    200 or 202 or 203 or 204 or 207 or 241 => 4,
                    205 or 209 => 16,
                    208 => 8,
                    _ => 0,
                };
                if (fixedSize != 0 && length != fixedSize) return false;
                if (kind == 201 && !TryString(value, out _)) return false;
                if (kind == 211 && (length < 4 || ReadInt(value, 0) < 0
                    || 4L + ReadInt(value, 0) * 4L != length)) return false;
            }

            if (ancestry.TryGetValue(cls, out int depth) && part == 1)
            {
                if (state != 0 || !seen.Add((cls, name))) return false;
                if (!selected.TryGetValue(name, out var previous) || depth < previous.Depth)
                    selected[name] = (kind, reference, value.ToArray(), depth);
            }

            int end = cursor + 32 + length;
            int padded = (end + 7) & ~7;
            if (padded > properties.Length || properties[end..padded].ContainsAnyExcept((byte)0)) return false;
            cursor = padded;
        }

        bool Integer(int id, int kind, int expected)
            => selected.TryGetValue(id, out var entry) && entry.Kind == kind
                && entry.Reference == 0 && entry.Value.Length == 4 && ReadInt(entry.Value, 0) == expected;
        bool Image(int id, out int resource)
        {
            resource = 0;
            if (!selected.TryGetValue(id, out var entry) || entry.Kind != 206
                || entry.Reference is <= 0 or > ushort.MaxValue || entry.Value.Length != 0) return false;
            resource = entry.Reference;
            return true;
        }

        if (!Integer(2401, 202, 20) // Original complete frame inventory.
            || !Integer(4001, 200, 0) || !Integer(4004, 200, 1)
            || !Integer(4011, 200, 0) || !Integer(4012, 200, 1) || !Integer(4013, 200, 1)
            || !Image(3001, out int background) || !Image(3002, out int selectedImage)
            || background == selectedImage
            || !selected.TryGetValue(3404, out var minimum) || minimum.Kind != 208
            || minimum.Reference != 0 || minimum.Value.Length != 8
            || !selected.TryGetValue(3601, out var margins) || margins.Kind != 205
            || margins.Reference != 0 || margins.Value.Length != 16) return false;
        Size minimumSize = new(ReadInt(minimum.Value, 0), ReadInt(minimum.Value, 4));
        LibreVisualStyleMargins margin = new(ReadInt(margins.Value, 0), ReadInt(margins.Value, 8),
            ReadInt(margins.Value, 4), ReadInt(margins.Value, 12));
        if (minimumSize.Width <= 0 || minimumSize.Height <= 0
            || margin.Left < 0 || margin.Right < 0 || margin.Top < 0 || margin.Bottom < 0) return false;
        images = new(background, selectedImage, 20, minimumSize, margin);
        return true;
    }

    private static bool ReadClasses(ReadOnlySpan<byte> map, out int count, out int selected, out int baseOffset)
    {
        count = 0;
        selected = -1;
        baseOffset = -1;
        int cursor = 0;
        while (cursor < map.Length)
        {
            int start = cursor;
            while (map.Length - cursor >= 2 && (map[cursor] != 0 || map[cursor + 1] != 0)) cursor += 2;
            if (map.Length - cursor < 2 || cursor == start || cursor - start > 2048
                || !TryString(map.Slice(start, cursor - start + 2), out string? name)) return false;
            cursor += 2;
            int aligned = (cursor + 7) & ~7;
            if (aligned > map.Length || map[cursor..aligned].ContainsAnyExcept((byte)0)) return false;
            cursor = aligned;
            if (string.Equals(name, "globals", StringComparison.OrdinalIgnoreCase))
            {
                if (baseOffset >= 0) return false;
                baseOffset = count;
            }
            else if (baseOffset < 0 && name != "documentation"
                && !name!.StartsWith("sizevariant.", StringComparison.Ordinal)
                && !name.StartsWith("colorvariant.", StringComparison.Ordinal)) return false;

            if (string.Equals(name, "ScrollBar", StringComparison.OrdinalIgnoreCase))
            {
                if (selected >= 0) return false;
                selected = count;
            }

            if (++count > 4096) return false;
        }

        return baseOffset >= 0 && selected >= baseOffset;
    }

    private static bool ReadCountedString(ReadOnlySpan<byte> map, ref int cursor, out string? text)
    {
        text = null;
        if (map.Length - cursor < 4) return false;
        int chars = ReadInt(map, cursor);
        cursor += 4;
        if (chars is <= 1 or > 1024 || chars * 2 > map.Length - cursor
            || !TryString(map.Slice(cursor, chars * 2), out text)) return false;
        cursor += chars * 2;
        int aligned = (cursor + 3) & ~3;
        if (aligned > map.Length || map[cursor..aligned].ContainsAnyExcept((byte)0)) return false;
        cursor = aligned;
        return true;
    }

    private static bool TryString(ReadOnlySpan<byte> bytes, out string? text)
    {
        text = null;
        if (bytes.Length < 2 || (bytes.Length & 1) != 0 || bytes[^1] != 0 || bytes[^2] != 0) return false;
        try
        {
            string value = s_unicode.GetString(bytes[..^2]);
            if (value.Length == 0 || value.Contains('\0')) return false;
            text = value;
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    private static int ReadInt(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
}
