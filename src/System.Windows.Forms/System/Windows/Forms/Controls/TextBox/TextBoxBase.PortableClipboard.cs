// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public abstract partial class TextBoxBase
{
    // Rich-text clipboard formats require the rich document's own transport.
    private protected virtual bool SupportsPortableTextClipboard => true;

    private void SendPortableClipboardMessage(uint message)
    {
        if (!SupportsPortableTextClipboard)
        {
            throw new PlatformNotSupportedException("This text control requires a rich-text clipboard service.");
        }

        // Preserve handle creation and virtual WndProc dispatch. In particular,
        // MaskedTextBox retains its original provider-owned clipboard handlers.
        Message nativeMessage = Message.Create(Handle, (int)message, 0, 0);
        WndProc(ref nativeMessage);
    }

    private bool ProcessPortableClipboardShortcut(Keys keyData)
    {
        if (!ShortcutsEnabled || !SupportsPortableTextClipboard)
        {
            return false;
        }

        switch (keyData)
        {
            case Keys.Control | Keys.Z:
            case Keys.Alt | Keys.Back:
                if (!SupportsPortableTextUndo) return false;
                if (!ReadOnly) Undo();
                return true;
            case Keys.Control | Keys.C:
                Copy();
                return true;
            case Keys.Control | Keys.X:
            case Keys.Shift | Keys.Delete:
                Cut();
                return true;
            case Keys.Control | Keys.V:
            case Keys.Shift | Keys.Insert:
                Paste();
                return true;
            default:
                return false;
        }
    }

    private bool CopyPortableSelection()
    {
        if (PasswordProtect || SelectionLength == 0)
        {
            return false;
        }

        // Same canonical Clipboard/selection ownership as MaskedTextBox.WmCopy.
        // A failed clipboard write must not remove the source selection.
        try
        {
            Clipboard.SetText(SelectedText);
            return true;
        }
        catch (Exception ex) when (!ex.IsCriticalException())
        {
            return false;
        }
    }

    private void PastePortableSelection()
    {
        if (ReadOnly)
        {
            return;
        }

        string? text;
        try
        {
            // Distinguish an absent text format from a present empty string:
            // only the latter is a replacement of an existing selection.
            text = Clipboard.GetTypedDataIfAvailable<string>(DataFormats.UnicodeText);
            if (text is null)
            {
                return;
            }
        }
        catch (Exception ex) when (!ex.IsCriticalException())
        {
            return;
        }

        int end = text.IndexOf('\0');
        if (end >= 0)
        {
            text = text[..end];
        }

        if (!Multiline)
        {
            end = text.AsSpan().IndexOfAny('\r', '\n');
            if (end >= 0)
            {
                text = text[..end];
            }
        }

        ReplacePortableSelection(GetPortableClipboardText(text), userInput: true, modified: true);
    }

    internal virtual string GetPortableClipboardText(string text) => text;
}
#endif
