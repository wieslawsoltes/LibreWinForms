# Plain TextBox word selection

Acceptance application: the existing `WinFormsControlsTest.TextBoxes` form,
starting with its `textBox` and `multilineTextBox` controls. Double-click a word,
then extend and reverse the selection while holding the second press. The source
editor must retain the original word-drag seed and notify
derived/public mouse handlers after its default processing. ToolStripTextBox and
the editable ComboBox reuse that same plain editor; this does not specify
MaskedTextBox or RichTextBox behavior.

## Remaining unmasked source gap

The native input adapter already carries completed click pairs to
`ProcessPortableMouseDownDefault` as `MouseEventArgs.Clicks == 2`. That method
still calls the ordinary caret hit path for unmasked text, and that captured
move path extends by character. Correct native click notifications and their
ordering therefore do not yet implement general word selection.

The existing retained layout owns original UTF-16 hit/caret/selection geometry.
Its row and grapheme capabilities do not define a clicked word. The source's
Ctrl+Backspace helper implements deletion, not a clicked-word span or reversal
policy. A wrapping or grapheme algorithm alone does not establish the Windows
EDIT selection contract.

## Native reference and observed policy

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

The first successful reference is the Windows automation job `109790362280`
of Build `36685521081`, PR head `7e8666e0410c9122803aaeeafcd6b17acd43a1f3`.
Its integration checkout is `bec9b19817cbf394990360403af1a8e0d12f8694`, a distinct
identity. The artifact `native-edit-word-selection-windows-latest` contains
12 cases, 200 requested coordinates, 196 observed gestures and 1,568 selection
states; four multiline CRLF coordinate requests are explicitly unavailable.
The receipt SHA-256 is
`72b334caab3a7f0a94cfda2590131e80eb9055464172f922b6db8d5008808630`.
This completed reference job is not a successful whole Build or product gate.

The observations distinguish these contracts:

- The actual native caret boundary and displayed row determine selection;
  a requested glyph index is not its substitute. At a new multiline row,
  selection starts in that row rather than selecting the preceding CRLF.
- ASCII punctuation can remain inside a word, trailing spaces are included,
  and a tab following spaces can start a separate span. Generic whitespace,
  UI Automation word units and Ctrl+Backspace do not reproduce these ranges.
- Dragging the initial leading-space range `[0,2]` back to native hit zero
  yields `[2,2]`. Unioning word ranges loses the native reversal behavior.
- Password double presses and the recorded drags retain the entire source
  selection, without exposing that source to the portable shaping provider.

The expanded reference passed Windows job `109799419524` of Build `36688369362`
on head `e7cf4da37ee5dee73df1d0311f6a067eeb911873` (integration checkout
`f8259147a0118b0b864a6476e2f30ee686ccbbcd`). Receipt SHA-256 is
`6a91435fbf228b11af930a6ffe6660ea7f80e91c8f7369bab128eb92a3b2baca`.
It records 18 cases, 294 requests, 286 observed gestures and 2,288 selection
states; eight coordinate requests are unavailable. Every observed default
word-break callback address is zero, so there is no borrowed callable default
implementation. The independent Uniscribe flags are observations, not a claim
that EDIT uses that specific API internally.

For all 286 double presses and 1,144 recorded moves, the following boundary-based
policy matches the receipt. Source endpoints, native stop flags, whitespace run
seams and CR-start boundaries form the observed boundary set. A double press
uses the preceding strict boundary, except a hit at an actual row-start may use
that boundary. Hit zero skips leading native-flagged whitespace. During dragging,
retain the original hit and both original selection endpoints: movement logically
left uses the inclusive preceding boundary with the original end; movement right
uses the original start with the inclusive following boundary. Returning to the
original hit restores the original selection. Do not use the most recent ordered
selection as a new anchor.

These observations also constrain the remaining boundary implementation:

- Soft-break and word-stop flags coincide in this receipt; it cannot distinguish
  those primitives. Raw flags miss some bidi whitespace/run seams and CR edges.
- Narrow NBSP `U+202F` breaks here, while NBSP `U+00A0` stays joined. Substituting
  a default Unicode line-break implementation without a compatibility contract
  would change the observed behavior.
- Isolated LF stays attached to preceding text; CR separates it. The two isolated
  break cases remain one native row. Do not infer visual rows from CR/LF scans.
- A long word selects across three soft-wrapped rows. Visual rows do not clamp
  word ranges.
- CRCRLF interior selections were not observed: apparent interior requests mapped
  elsewhere or were unavailable. Do not turn them into invented native expected
  ranges.

The existing caret and retained-row APIs cover the observed coordinate contract;
no extra public cluster-hit API is justified. The reusable boundary calculation
is still an implementation prerequisite; this finite receipt is not arbitrary
Unicode or physical-input qualification.

## Password source selection

An admitted password double press uses canonical `SelectAll` once, before the
derived/public mouse notifications. It does not hit-test, create another layout,
or send password source to a text provider. Held moves retain that range without
reselecting. The lease belongs to the original press, text, focus, selection and
layout generation; a callback replacement retires it even if capture remains.
Release, lost capture and control disposal also retire the lease. Ordinary
single-click caret placement and character dragging remain unchanged.

`CanonicalTextWordSelectionTests` adds 13 focused source cases through the actual
provider input adapter: both mask modes, read-only selection, drag reversal,
callback overrides and exceptions, cancellation, capture/handle replacement,
selection reentry and the next ordinary press. These are authored regressions;
their execution belongs to required CI, not a local desktop validation run.

This is the confirmed password special case, not the unresolved unmasked word
classifier. The implementation PR stays draft until general source word behavior
and its focused regressions accompany the reference. A successful reference
process alone is not a product fix. Ordinary Silk double-click classification, wheel policy,
native factory admission, IME and full platform/UI qualification remain separate.

## Native documentation boundary

Microsoft documents the edit-control word-break callback and its UTF-16 indices,
including atomic CRLF and CRCRLF treatment, but this is not a complete oracle for
double-click whitespace, punctuation or drag reversal. See
[EditWordBreakProcW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nc-winuser-editwordbreakprocw)
and [EM_SETWORDBREAKPROC](https://learn.microsoft.com/en-us/windows/win32/controls/em-setwordbreakproc).
The observed native selections, not an inferred wrapping algorithm, determine
the source selection policy.
