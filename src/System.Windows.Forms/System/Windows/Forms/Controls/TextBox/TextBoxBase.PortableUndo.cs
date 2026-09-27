// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if LIBREWINFORMS_PORTABLE
namespace System.Windows.Forms;

public abstract partial class TextBoxBase
{
    private protected virtual bool SupportsPortableTextUndo => false;

    private enum PortableEditKind { Replacement, Typing, Backspace, Delete }

    // One EDIT operation, not a rich-text history or a backend-owned text copy.
    private sealed record PortableUndoEdit(int Start, string Removed, string Inserted, PortableEditKind Kind);

    private PortableUndoEdit? _portableUndoEdit;
    private bool _portableUndoCanCoalesce;

    private nint SendPortableUndoMessage(MessageId message)
    {
        // Retain handle creation and virtual dispatch, including MaskedTextBox's
        // original refusal of undo and application-defined message handlers.
        Message nativeMessage = Message.Create(Handle, (int)message, 0, 0);
        WndProc(ref nativeMessage);
        return nativeMessage.Result;
    }

    private void ClearPortableUndo()
    {
        _portableUndoEdit = null;
        _portableUndoCanCoalesce = false;
    }

    private PortableUndoEdit CreatePortableUndoEdit(int start, string removed, string inserted, PortableEditKind kind)
    {
        if (_portableUndoCanCoalesce && _portableUndoEdit is { } previous && previous.Kind == kind)
        {
            if (kind == PortableEditKind.Typing && removed.Length == 0
                && start == previous.Start + previous.Inserted.Length)
                return previous with { Inserted = previous.Inserted + inserted };
            if (kind == PortableEditKind.Backspace && inserted.Length == 0 && previous.Inserted.Length == 0
                && start + removed.Length == previous.Start)
                return previous with { Start = start, Removed = removed + previous.Removed };
            if (kind == PortableEditKind.Delete && inserted.Length == 0 && previous.Inserted.Length == 0
                && start == previous.Start)
                return previous with { Removed = previous.Removed + removed };
        }

        return new(start, removed, inserted, kind);
    }

    private bool UndoPortableEdit()
    {
        if (!SupportsPortableTextUndo || ReadOnly || _portableUndoEdit is not { } edit)
            return false;

        string text = WindowText;
        string updated = string.Concat(text.AsSpan(0, edit.Start), edit.Removed,
            text.AsSpan(edit.Start + edit.Inserted.Length));
        WindowText = updated;
        SelectInternal(edit.Start, edit.Removed.Length, updated.Length);
        // Install before public notifications; a reentrant Text assignment or
        // ClearUndo must invalidate this inverse instead of being overwritten.
        _portableUndoEdit = new(edit.Start, edit.Inserted, edit.Removed, PortableEditKind.Replacement);
        _portableUndoCanCoalesce = false;
        Modified = true;
        OnTextChanged(EventArgs.Empty);
        Invalidate();
        return true;
    }
}
#endif
