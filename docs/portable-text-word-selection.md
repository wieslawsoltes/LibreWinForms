# Plain TextBox word selection

Acceptance application: the existing `WinFormsControlsTest.TextBoxes` form,
starting with its `textBox` and `multilineTextBox` controls. Double-click a word,
then extend and reverse the selection while holding the second press. The source
editor must retain whole-word selection and notify
derived/public mouse handlers after its default processing. ToolStripTextBox and
the editable ComboBox reuse that same plain editor; this does not specify
MaskedTextBox or RichTextBox behavior.

## Current source gap

The native input adapter already carries completed click pairs to
`ProcessPortableMouseDownDefault` as `MouseEventArgs.Clicks == 2`. That method
still calls the ordinary caret hit path, and its captured move path extends by
character. Correct native click notifications and their ordering therefore do
not yet implement word selection.

The existing retained layout owns original UTF-16 hit/caret/selection geometry.
Its row and grapheme capabilities do not define a clicked word. The source's
Ctrl+Backspace helper implements deletion, not a clicked-word span or reversal
policy. Neither wrapping opportunities nor Unicode grapheme boundaries may be
substituted for the Windows EDIT selection contract.

## Required reference before implementing the policy

Extend the existing `eng/NativeTextBoxReference` stock-Windows application with
an explicit word-selection mode. Its old frame-reference mode remains separate.
Use real owned EDIT handles, native character-position/hit queries and native
mouse messages; retain the actual selection after the double press, outward
drag, reversal and release. Record public callback observations so the source
implementation preserves default-before-notification order.

The reference must include interword and trailing spaces, tabs, punctuation,
CRLF/empty rows, surrogate and combining sequences, bidi text, read-only controls
and password masking. It must record actual hit positions rather than inventing
character widths or a clickable rectangle for a hard break. Password source must
not be sent to the portable shaping service when the implementation is added.

Run this package-independent prerequisite in the existing early Windows
automation CI job, retaining its original matrix, locale check and ten-minute
timeout. It does not wait for, replace or qualify the separate package checks.
No local VM, platform matrix,
screen capture or general desktop audit is required to acquire these endpoints.
Synthetic HWND messages are not physical input or rendered desktop qualification.
Inspect the completed receipt before defining portable expected selections.

The implementation PR stays draft until the actual source behavior and focused
regressions accompany this reference. A successful reference process alone is
not a product fix. Ordinary Silk double-click classification, wheel policy,
native factory admission, IME and full platform/UI qualification remain separate.

## Native documentation boundary

Microsoft documents the edit-control word-break callback and its UTF-16 indices,
including atomic CRLF and CRCRLF treatment, but this is not a complete oracle for
double-click whitespace, punctuation or drag reversal. See
[EditWordBreakProcW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nc-winuser-editwordbreakprocw)
and [EM_SETWORDBREAKPROC](https://learn.microsoft.com/en-us/windows/win32/controls/em-setwordbreakproc).
The observed native selections, not an inferred wrapping algorithm, determine
the source selection policy.
