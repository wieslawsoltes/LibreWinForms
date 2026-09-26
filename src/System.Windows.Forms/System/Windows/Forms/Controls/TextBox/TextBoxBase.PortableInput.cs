// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public abstract partial class TextBoxBase
{
    internal override void ProcessPortableTranslatedKey(ref Message message)
    {
        if (message.MsgInternal == PInvokeCore.WM_KEYDOWN
            && (Keys)(int)message.WParamInternal == Keys.Back
            && ModifierKeys == Keys.None
            && !IsPortableKeyPressSuppressed)
        {
            // KeyChar callbacks can omit control characters. Preserve the
            // canonical filtered KeyPress even for read-only/handled key-down,
            // then discard a duplicate host TextInput for this same key.
            ProcessPortableCharacter('\b');
            SuppressPortableKeyPress();
        }
    }

    internal override void ProcessPortableDefaultKeyMessage(ref Message message)
    {
        if (ReadOnly)
        {
            return;
        }

        if (message.MsgInternal == PInvokeCore.WM_CHAR)
        {
            char character = (char)(int)message.WParamInternal;
            if (character == '\b')
            {
                DeletePortableSelection(backwards: true);
            }
            else if (character == '\r' && Multiline)
            {
                ReplacePortableSelection("\r\n", userInput: true, modified: true);
            }
            else if (!char.IsControl(character) || (character == '\t' && Multiline && AcceptsTab))
            {
                ReplacePortableSelection(GetPortableInputText(character), userInput: true, modified: true);
            }

            return;
        }

        if (message.MsgInternal == PInvokeCore.WM_KEYDOWN && ModifierKeys == Keys.None)
        {
            Keys key = (Keys)(int)message.WParamInternal;
            if (key == Keys.Delete)
            {
                DeletePortableSelection(backwards: false);
                SuppressPortableKeyPress();
            }
        }
    }

    internal virtual string GetPortableInputText(char character) => character.ToString();

    private void DeletePortableSelection(bool backwards)
    {
        GetSelectionStartAndLength(out int start, out int length);
        string text = WindowText;
        if (length == 0)
        {
            if (backwards && start > 0)
            {
                length = start > 1 && char.IsSurrogatePair(text, start - 2) ? 2 : 1;
                start -= length;
            }
            else if (!backwards && start < text.Length)
            {
                length = char.IsSurrogatePair(text, start) ? 2 : 1;
            }
            else
            {
                return;
            }

            SelectInternal(start, length, text.Length);
        }

        ReplacePortableSelection(string.Empty, userInput: true, modified: true);
    }

    // Use the same source-owned UTF-16 text/selection as the public WinForms
    // properties. No backend copy, layout approximation or composition state.
    private void ReplacePortableSelection(string replacement, bool userInput, bool modified)
    {
        GetSelectionStartAndLength(out int start, out int length);
        string text = WindowText;
        if (userInput && MaxLength > 0)
        {
            int available = Math.Max(0, MaxLength - (text.Length - length));
            if (replacement.Length > available)
            {
                replacement = replacement[..available];
            }
        }

        string updated = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(start + length));
        if (updated == text)
        {
            SelectInternal(start + replacement.Length, 0, text.Length);
            return;
        }

        WindowText = updated;
        SelectInternal(start + replacement.Length, 0, updated.Length);
        Modified = modified;
        OnTextChanged(EventArgs.Empty);
        Invalidate();
    }
}
#endif
