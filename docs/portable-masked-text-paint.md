# Portable MaskedTextBox content painting

The custom-column application in [issue #6](https://github.com/wieslawsoltes/LibreWinForms/issues/6)
uses an actual `MaskedTextBox` implementing `IDataGridViewEditingControl`.
Portable character input already reaches the original `MaskedTextProvider`, but
the content paint implementation belonged only to `TextBox`. `MaskedTextBox`
inherits `TextBoxBase`, so its editor remained blank even when its source value
and mask changed successfully.

The portable masked control now draws its original `GetFormattedDisplayString()`
through `TextRenderer`. Public `TextMaskFormat` controls output, not display:
display still includes literals and follows the canonical prompt, read-only,
password and `HidePromptOnLeave` rules. A null mask displays the original stored
text, applying the selected password character before handing text to the
renderer. No password source is sent to the drawing service.

The private system-password lookup uses the same U+25CF policy as the portable
plain TextBox. The old lookup created a TextBox and read `PasswordChar`; on the
portable path that getter returns the custom character, not the active system
character, so the mask provider incorrectly received zero. Native Windows keeps
its original EDIT query unchanged.

Content drawing retains the existing client frame, source font, disabled color,
RTL-translated horizontal alignment and single-line/no-prefix/no-padding flags.
The caller's clip and paint clip are intersected and Graphics state is restored.
Display changes, focus changes and enabled changes invalidate the source control;
they do not introduce `TextChanged` events or change mask input/selection.

This is a content-painting fix, not complete masked-editor or issue #6 parity.
Masked borders, retained selection/caret/pointer geometry, scrolling, IME and the
original application's native startup/desktop comparisons remain separate.
No explicit grid row heights, column widths, observation deadlines or renderer
defaults change.

## Bounded source regression

`CanonicalMaskedTextBoxPaintTests.cs` uses the existing headless platform and real
source controls. Independent expected strings cover all four `TextMaskFormat`
values, six alignment/direction combinations, custom/system passwords with and
without a mask, prompt/read-only policies, focus-driven prompt changes, repainting,
clip/transform restoration, empty bounds and a real custom DataGridView editing
control's paint/commit lifecycle. These checks do not start a native renderer or
qualify desktop pixels.

The unchanged implementation failed 21 of the 22 new cases (only empty bounds
passed). The content fix passes all 22 with zero skips. A bounded combined run
also passes 48 new/existing masked-input, clipboard, undo, default-grid editing
and plain-editor paint cases. The managed test graph uses the default branch's
exact ProGPU pin `0d33ef68aaf9c9c58685f449e6ab386f5156fce5`; no submodule pin or
native artifact changes are involved. Build retains existing analyzer/API
warnings and reports zero errors; these are source checks, not full CI or UI
qualification.
