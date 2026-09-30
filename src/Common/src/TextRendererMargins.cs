// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace LibreWinForms;

/// <summary>Shared canonical TextRenderer overhang policy, independent of font realization.</summary>
internal static class TextRendererMargins
{
    internal static (int Left, int Right) Get(int fontHeight, bool noPadding, bool leftAndRightPadding)
    {
        // LeftAndRightPadding takes precedence over NoPadding, as in SplitTextFormatFlags.
        if (noPadding && !leftAndRightPadding)
        {
            return default;
        }

        // Moved from WinForms TextExtensions.GetTextMargins. The italic allowance
        // is part of the padding policy for every font, not just italic faces.
        const float ItalicPaddingFactor = 1 / 2f;
        float overhangPadding = fontHeight / 6f;
        int factor = leftAndRightPadding ? 2 : 1;
        return ((int)Math.Ceiling(factor * overhangPadding),
            (int)Math.Ceiling(overhangPadding * (factor + ItalicPaddingFactor)));
    }
}
