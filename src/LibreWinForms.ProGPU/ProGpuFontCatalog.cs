// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using LibreWinForms.Platform;
using ProGPU.Text;
using System.Collections.ObjectModel;

namespace LibreWinForms.ProGPU;

/// <summary>Projects ProGPU's installed and host-registered typefaces into portable dialog metadata.</summary>
public sealed class ProGpuFontCatalog : ILibreFontCatalog
{
    private static readonly Lazy<ReadOnlyCollection<LibreFontFamilyInfo>> s_families = new(
        CreateFamilies,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public IReadOnlyList<LibreFontFamilyInfo> GetFamilies() => s_families.Value;

    private static ReadOnlyCollection<LibreFontFamilyInfo> CreateFamilies()
    {
        FontManager manager = FontApi.Manager;
        var result = new List<LibreFontFamilyInfo>();
        foreach (string familyName in manager.FontFamilies)
        {
            IReadOnlyList<FontFace> faces = manager.GetFontStyles(familyName);
            bool regular = false;
            bool bold = false;
            bool italic = false;
            bool boldItalic = false;
            bool fixedPitch = false;
            bool vector = false;
            bool symbol = faces.Count > 0;

            foreach (FontFace face in faces)
            {
                bool isBold = face.Style.Weight >= 600;
                bool isItalic = face.Style.Slant != FontSlant.Upright;
                if (isBold && isItalic) boldItalic = true;
                else if (isBold) bold = true;
                else if (isItalic) italic = true;
                else regular = true;

                TtfFont? font = face.Load();
                if (font is null) continue;
                fixedPitch |= font.IsFixedPitch;
                vector |= font.HasTrueTypeOutlines || font.HasCffOutlines;
                symbol &= font.UsesSymbolCharacterMap;
            }

            result.Add(new LibreFontFamilyInfo(
                familyName,
                regular,
                bold,
                italic,
                boldItalic,
                fixedPitch,
                vector,
                IsVertical: familyName.StartsWith('@'),
                IsSymbol: symbol));
        }

        return Array.AsReadOnly(result.ToArray());
    }
}
