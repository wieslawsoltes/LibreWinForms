// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibreWinForms.Platform;
using Xunit;

namespace LibreWinForms.ProGPU.Tests;

public sealed class WindowsThemeResourceTableTests
{
    [Fact]
    public void OriginalCompleteThemeTablesIdentifyActualArrowImages()
    {
        Assert.True(WindowsThemeResourceTable.TryGetVariant(Table("vmap"), "NormalColor", "NormalSize", out string? variant));
        Assert.Equal("Normal", variant);
        Assert.True(WindowsThemeResourceTable.TryReadArrowImages(Table("cmap"), Table("bcmap"), Table("variant"), out var images));
        Assert.Equal(new WindowsThemeResourceTable.ArrowImages(1120, 1121, 20,
            new Size(17, 17), new LibreVisualStyleMargins(8, 8, 8, 8)), images);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MalformedOriginalPropertyInventoryRejectsAtomically(int mutation)
    {
        byte[] source = Table("variant");
        switch (mutation)
        {
            case 0: source = source[..^1]; break;
            case 1: BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(8, 4), int.MaxValue); break;
            case 2: BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(28, 4), int.MaxValue); break;
            case 3: BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(24, 4), 1); break;
            case 4: source = [.. source, 0]; break;
        }

        Assert.False(WindowsThemeResourceTable.TryReadArrowImages(Table("cmap"), Table("bcmap"), source, out var images));
        Assert.Equal(default, images);
    }

    [Fact]
    public void SelectedArrowDuplicateAndStateOverrideAreNotSilentlyResolved()
    {
        byte[] source = Table("variant");
        int offset = FindArrowImage(source);
        byte[] duplicate = [.. source, .. source.AsSpan(offset, 32)];
        Assert.False(WindowsThemeResourceTable.TryReadArrowImages(Table("cmap"), Table("bcmap"), duplicate, out var images));
        Assert.Equal(default, images);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(offset + 16, 4), 1);
        Assert.False(WindowsThemeResourceTable.TryReadArrowImages(Table("cmap"), Table("bcmap"), source, out images));
        Assert.Equal(default, images);
    }

    [Fact]
    public void InvalidClassIdentityAndCyclicInheritanceReject()
    {
        byte[] classes = Table("cmap"), bases = Table("bcmap");
        byte[] marker = Encoding.Unicode.GetBytes("ScrollBar\0");
        int offset = classes.AsSpan().IndexOf(marker);
        Assert.True(offset >= 0);
        classes[offset] = 0; classes[offset + 1] = 0;
        Assert.False(WindowsThemeResourceTable.TryReadArrowImages(classes, bases, Table("variant"), out var images));
        Assert.Equal(default, images);
        BinaryPrimitives.WriteInt32LittleEndian(bases.AsSpan(4 + (136 - 4) * 4, 4), 136 - 4);
        Assert.False(WindowsThemeResourceTable.TryReadArrowImages(Table("cmap"), bases, Table("variant"), out images));
        Assert.Equal(default, images);
    }

    [Fact]
    public void EmptyUnrelatedScalarCannotBypassWholeInventoryValidation()
    {
        byte[] record = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0, 4), 2401);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4, 4), 202);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(8, 4), 4);
        byte[] source = [.. Table("variant"), .. record];
        Assert.False(WindowsThemeResourceTable.TryReadArrowImages(Table("cmap"), Table("bcmap"), source, out var images));
        Assert.Equal(default, images);
    }

    [Fact]
    public void DerivedArrowImageOverridesItsActualBaseRegardlessOfRecordOrder()
    {
        byte[] source = Table("variant");
        int offset = FindArrowImage(source);
        byte[] inherited = source.AsSpan(offset, 32).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(inherited.AsSpan(8, 4), 23);
        BinaryPrimitives.WriteInt32LittleEndian(inherited.AsSpan(20, 4), 1122);
        foreach (byte[] records in new byte[][] { [.. inherited, .. source], [.. source, .. inherited] })
        {
            Assert.True(WindowsThemeResourceTable.TryReadArrowImages(Table("cmap"), Table("bcmap"), records, out var images));
            Assert.Equal(1121, images.SelectedId);
        }
    }

    [Fact]
    public void InvalidUnrelatedBaseIsRejectedBeforePublishingSelectedImages()
    {
        byte[] bases = Table("bcmap");
        BinaryPrimitives.WriteInt32LittleEndian(bases.AsSpan(4, 4), int.MaxValue);
        Assert.False(WindowsThemeResourceTable.TryReadArrowImages(Table("cmap"), bases, Table("variant"), out var images));
        Assert.Equal(default, images);
    }

    [Fact]
    public void VariantSelectionRequiresCompleteUnambiguousOriginalStrings()
    {
        byte[] map = Table("vmap");
        Assert.False(WindowsThemeResourceTable.TryGetVariant(map, "OtherColor", "NormalSize", out string? result));
        Assert.Null(result);
        Assert.False(WindowsThemeResourceTable.TryGetVariant([.. map, .. map], "NormalColor", "NormalSize", out result));
        Assert.Null(result);
        Assert.False(WindowsThemeResourceTable.TryGetVariant(map.AsSpan(0, map.Length - 1), "NormalColor", "NormalSize", out result));
        Assert.Null(result);
        BinaryPrimitives.WriteInt32LittleEndian(map, int.MaxValue);
        Assert.False(WindowsThemeResourceTable.TryGetVariant(map, "NormalColor", "NormalSize", out result));
        Assert.Null(result);
    }

    private static int FindArrowImage(byte[] source)
    {
        for (int offset = 0; offset < source.Length;)
        {
            int Read(int field) => BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset + field, 4));
            int kind = Read(4), reference = Read(20), size = Read(28);
            if (Read(0) == 3002 && Read(8) == 136 && Read(12) == 1 && Read(16) == 0) return offset;
            offset = (offset + 32 + (reference != 0 || kind is 206 or 210 or 213 or 243 ? 0 : size) + 7) & ~7;
        }

        throw new InvalidOperationException("Original arrow image record is missing.");
    }

    private static byte[] Table(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "WindowsThemeArrowAssets");
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "receipt.json")));
        string file = name + ".native-table";
        JsonElement descriptor = receipt.RootElement.GetProperty("tables").EnumerateArray()
            .Single(item => item.GetProperty("file").GetString() == file);
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, file));
        Assert.Equal(descriptor.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }
}
