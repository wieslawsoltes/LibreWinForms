// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public partial class TextBox
{
    internal override string GetPortableInputText(char character)
        => CharacterCasing switch
        {
            CharacterCasing.Upper => char.ToUpper(character).ToString(),
            CharacterCasing.Lower => char.ToLower(character).ToString(),
            _ => character.ToString(),
        };
}
#endif
