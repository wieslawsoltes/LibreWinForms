using System.Buffers.Binary;
using ProGPU.Text;

namespace System.Drawing.Tests;

internal static class SourceDeviceFontFixture
{
    internal static byte[] Create(bool malformed = false)
    {
        // Owned static Inter fixture, with independently authored device records.
        // This is not a claim about Inter's native metrics or original Windows fonts.
        byte[] original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "Inter-Regular.ttf"));
        SfntFontFace face = SfntFontFace.Load(original);
        if (face.Tables.ContainsKey("fvar")) throw new InvalidDataException("The owned fixture must remain static.");
        byte[] vdmx = new byte[40];
        U16(vdmx, 0, 1); U16(vdmx, 2, 1); U16(vdmx, 4, 1);
        vdmx[6] = vdmx[7] = vdmx[8] = vdmx[9] = 1;
        U16(vdmx, 10, 12); U16(vdmx, 12, malformed ? 5 : 4); vdmx[14] = 11; vdmx[15] = 24;
        (int Em, int Max, int Min)[] records = [(11, 11, -2), (12, 12, -3), (18, 20, -5), (24, 26, -6)];
        for (int i = 0; i < records.Length; i++)
        { U16(vdmx, 16 + 6 * i, records[i].Em); U16(vdmx, 18 + 6 * i, records[i].Max); U16(vdmx, 20 + 6 * i, records[i].Min); }
        var tables = face.Tables.Values.Select(t => (t.Tag, Bytes: original.AsSpan((int)t.Offset, (int)t.Length).ToArray())).ToList();
        tables.Add(("VDMX", vdmx));
        int offset = 12 + tables.Count * 16;
        byte[] result = new byte[offset + tables.Sum(t => t.Bytes.Length)];
        original.AsSpan(0, 4).CopyTo(result); U16(result, 4, tables.Count);
        for (int i = 0; i < tables.Count; i++)
        {
            System.Text.Encoding.ASCII.GetBytes(tables[i].Tag, result.AsSpan(12 + i * 16, 4));
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(20 + i * 16), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(24 + i * 16), (uint)tables[i].Bytes.Length);
            tables[i].Bytes.CopyTo(result, offset); offset += tables[i].Bytes.Length;
        }

        return result;
    }

    private static void U16(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), unchecked((ushort)value));
}
