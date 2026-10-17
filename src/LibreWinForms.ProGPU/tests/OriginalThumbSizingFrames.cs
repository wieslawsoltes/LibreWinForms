// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace LibreWinForms.ProGPU.Tests;

internal static class OriginalThumbSizingFrames
{
    internal static IEnumerable<(JsonElement Item, string Path)> Uncontracted()
    {
        string directory = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "WindowsThemeSizingReference");
        using JsonDocument provenance = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(directory, "provenance.json")));
        byte[] bytes = File.ReadAllBytes(System.IO.Path.Combine(directory, "reference.json"));
        Assert.Equal(provenance.RootElement.GetProperty("referenceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using JsonDocument receipt = JsonDocument.Parse(bytes);
        JsonElement cases = receipt.RootElement.GetProperty("cases");
        Assert.Equal(128, cases.GetArrayLength());
        foreach (JsonElement item in cases.EnumerateArray())
        {
            JsonElement margin = item.GetProperty("sizingMargins");
            Assert.Equal(0, margin.GetProperty("hresult").GetInt32());
            JsonElement values = margin.GetProperty("value");
            if (item.GetProperty("Width").GetInt32() < values[0].GetInt32() + values[2].GetInt32()
                || item.GetProperty("Height").GetInt32() < values[1].GetInt32() + values[3].GetInt32()) continue;
            string path = System.IO.Path.Combine(directory, item.GetProperty("file").GetString()!);
            Assert.Equal(item.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            yield return (item, path);
        }
    }
}
