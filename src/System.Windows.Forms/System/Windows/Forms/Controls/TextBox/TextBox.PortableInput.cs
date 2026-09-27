// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public partial class TextBox
{
    internal override void ProcessPortableDefaultKeyMessage(ref Message message)
    {
        if (message.MsgInternal == PInvokeCore.WM_KEYDOWN
            && TryMovePortableTextBoundary((Keys)(int)message.WParamInternal | ModifierKeys))
        {
            return;
        }

        base.ProcessPortableDefaultKeyMessage(ref message);
    }

    internal override string GetPortableClipboardText(string text)
        => CharacterCasing switch
        {
            CharacterCasing.Upper => text.ToUpper(),
            CharacterCasing.Lower => text.ToLower(),
            _ => text,
        };

    internal override string GetPortableInputText(char character)
        => CharacterCasing switch
        {
            CharacterCasing.Upper => char.ToUpper(character).ToString(),
            CharacterCasing.Lower => char.ToLower(character).ToString(),
            _ => character.ToString(),
        };
}
#endif
