// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public partial class TextBox
{
    private protected override bool SupportsPortableTextUndo => true;

    internal override void ProcessPortableTranslatedKey(ref Message message)
    {
        if (message.MsgInternal == PInvokeCore.WM_KEYDOWN
            && (Keys)(int)message.WParamInternal == Keys.Return
            && Multiline
            && ModifierKeys == Keys.None
            && !IsPortableKeyPressSuppressed)
        {
            // Native text callbacks may omit Enter. Retain the canonical
            // KeyPress/edit path and suppress a duplicate host character only
            // for this key cycle, as the existing Backspace translation does.
            ProcessPortableCharacter('\r');
            SuppressPortableKeyPress();
            return;
        }

        base.ProcessPortableTranslatedKey(ref message);
    }

    internal override void ProcessPortableDefaultKeyMessage(ref Message message)
    {
        if (message.MsgInternal == PInvokeCore.WM_KEYDOWN
            && (TryMovePortableTextBoundary((Keys)(int)message.WParamInternal | ModifierKeys)
                || TryMovePortableLayoutCaret((Keys)(int)message.WParamInternal | ModifierKeys)
                || TryMovePortableRowCaret((Keys)(int)message.WParamInternal | ModifierKeys)))
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
