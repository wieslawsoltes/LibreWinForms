# Portable plain-text boundary navigation

Portable `TextBox` now supplies the default Home/End operation that an EDIT
window would otherwise own. Single-line Home/End moves to the source text
endpoints. Ctrl+Home/End also works for multiline document endpoints. Shift
extends from the original signed selection anchor, including after reversing
direction. Indices remain original UTF-16 positions.

The implementation runs through the existing filtered, preprocessed and managed
key-event path, after an unhandled `KeyDown`. It reads current source text and
selection after those callbacks, uses `SelectInternal`, and invalidates the
control without changing text, `Modified`, or edit notifications. Read-only and
password controls retain selection movement. No popup activation or focus
transfer is introduced.

The override belongs to plain `TextBox`, including its real editable ComboBox
child and TextBox-derived editors. It does not replace `MaskedTextBox` or
`RichTextBox` navigation. The shared base helper keeps the private signed anchor;
public `SelectionStart` exposes an ordered range and must not become the anchor.

Multiline Home/End without Ctrl still requires actual retained visual-line
layout. This change does not substitute newline scans or estimate wrapped glyph
positions. Drawn caret/selection, hit-to-character placement, scrolling, shaped
navigation and native desktop comparison remain separate work.

## Implementation checks

The seven initial regression cases failed against the unchanged source: all
retained the old selection instead of moving or extending it. After the fix,
all nine focused source cases pass, including empty password input and the
explicit multiline visual-line boundary. The editable ComboBox tests also cover
open/closed popups, Home/End and Shift reversal without list acceptance or edit
notifications. These use real source controls through the typed headless input
fixture, not a substitute editor.

The complete canonical source suite passes **581 tests, zero failures/skips** on
macOS ARM64/.NET 10.0.5. The full-source build has zero errors and 629 existing
warnings; the final incremental test build has zero errors/warnings. The CI
minimum increases from 568 to 581, retaining every original source case and
deadline. Exact-head hosted CI remains required before merge.

Evidence is retained under `artifacts/text-boundary-navigation/`: the initial
test-helper compile failure, compiled failing baseline, corrected builds and
focused/full test logs. No VM or native desktop was used for these checks.
