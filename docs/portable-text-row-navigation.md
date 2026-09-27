# Portable TextBox row navigation

Plain multiline TextBox now implements Up/Down and visual-row Home/End using
ProGPU's owned horizontal layout generation. The renderer opts in through
`ILibreTextRowNavigationService`; its layouts implement `ILibreTextRowNavigation`.
Existing paint-only and ordinary retained-layout providers keep their original
contracts. Claiming the capability without supplying it fails explicitly.

The source control owns preferred horizontal X. Consecutive Up/Down keeps it
through short and empty rows; pointer/horizontal/boundary movement, external
selection and replacement layout generations reset it. Target carets retain
their original source indices and affinities. Shift keeps the signed selection
anchor, including when reversing direction. Ctrl+Home/End still uses the
existing whole-document policy. Read-only navigation never edits text.

The renderer supplies actual row identities, wrapping, blank rows, shaped
cluster stops and bidi placement; the source control does not scan newlines,
reshape prefixes, synthesize carets or use ink bounds. Keyboard filtering and
public KeyDown handlers still precede default processing. Input admission for
Up/Down requires multiline source and the optional capability. RichTextBox,
MaskedTextBox and native Windows EDIT processing are unchanged.

Six fresh-process canonical cases use the actual ProGPU provider, covering
preferred X, read-only navigation, signed Shift reversal across blank rows,
wrapped boundary affinity, whole-document modifiers, selection reset, public
handlers/disposal, and trailing-row boundaries. The source test minimum rises
from 607 to 613, preserving deadlines and zero-skip policy. Local build/test
evidence is retained under `artifacts/row-navigation/`.

This depends on ProGPU's retained row-navigation PR and LibreWinForms #91/#90.
Native Windows/Linux/macOS UI comparison, word/page navigation, IME, scrollbar
interaction and full release validation remain outstanding.
