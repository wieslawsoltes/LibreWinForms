# Portable plain-text undo

Canonical portable TextBox now owns one source edit record for typing, deletion
and clipboard replacement. Previously CanUndo, Undo and ClearUndo called USER32
even when the editor used portable handles. These APIs now dispatch through the
actual virtual window procedure, preserving custom handlers, handle creation and
MaskedTextBox's existing refusal of undo. Native Windows code is unchanged.

The original UTF-16 replacement range and exact inserted/removed strings define
the record. Contiguous typing and adjacent forward/backward deletion coalesce;
explicit selection changes break that grouping without discarding the record.
Undo swaps the replacement and selects restored text; another Undo restores the
inverse, matching the single-level [EDIT undo contract](https://learn.microsoft.com/en-us/windows/win32/controls/em-undo).
Playback does not reapply current casing, MaxLength or clipboard contents.

Ctrl+Z and Alt+Backspace use the existing canonical command path after parent
handling. ShortcutsEnabled and ReadOnly remain authoritative. Programmatic Text
replacement, non-undoable SelectedText assignments, ClearUndo and handle teardown
retire history. Even an identical programmatic SelectedText assignment clears
the modified flag, as the existing native branch does. Undo installs its inverse
before public notifications so callback replacement, ClearUndo or disposal cannot
resurrect the old record afterward.

This does not flatten RichTextBox into a plain-text history or replace its
multilevel document undo. It adds no runtime reflection, OS-shaped fake object,
new public API or backend copy of source text. Native desktop keyboard/rendering
and Windows differential qualification remain separate from source contracts.

The initial 16 actual-source cases produced 14 failures and two passing controls
against the unchanged parent, including direct USER32 load failures on macOS.
All 16 pass with the implementation. Eight additional cases retain virtual
message dispatch, public-callback retirement, no-op replacement and parent-command
precedence. The full canonical gate floor increases from 613 to 637 without
changing existing assertions, skips or deadlines. Evidence is retained under
`artifacts/text-undo`; complete exact-head Build and Docs success is required.

Local macOS ARM64 results: all 24 focused cases and all 637 canonical source
cases pass with zero failures or skips on the .NET 10.0.9 test runtime. The full
suite completes in 46 seconds. This is actual source-control execution through
the typed headless platform, not native desktop or package qualification.
