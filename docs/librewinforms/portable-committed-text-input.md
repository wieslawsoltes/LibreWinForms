# Canonical portable committed text input

## Source contract and reproduced failure

LibreWinForms issue [#6](https://github.com/wieslawsoltes/LibreWinForms/issues/6)
references the official `dotnet/samples` DataGridView application. At inspected
commit `acb39ceb13f910ae0f8f6298059c59102b749c41`, its
[entry point](https://github.com/dotnet/samples/blob/acb39ceb13f910ae0f8f6298059c59102b749c41/windowsforms/datagridview/CSWinFormDataGridView/Program.cs)
opens `CustomDataGridViewColumn.MainForm`. Its first Name column is an ordinary
`DataGridViewTextBoxColumn`; other columns use the sample's custom
`MaskedTextBox` editor. The designer leaves the default
`EditOnKeystrokeOrF2` policy intact.

Three independent headless processes using actual source-built canonical controls
reproduced different failures before this change:

- A standalone `TextBox` received its `KeyPress` event but retained the selected
  old text after actual `ILibreWindow` committed text input.
- Typing an initiating letter in the grid reached native `USER32.SendMessage`
  from `DataGridView.ProcessKeyEventArgs`.
- F2 created and focused the canonical editing control, but subsequent committed
  input did not change its text.

The earlier tests that called `BeginEdit` and assigned `editor.Text` did not cover
these input paths. Fully hosted commit also exposed `EM_EMPTYUNDOBUFFER` cleanup
on a portable editor with no native edit-control buffer.

## Ownership and operation ordering

The change adds an internal portable default-key operation after the existing
message filter, canonical preprocessing and `ProcessKeyMessage` event chain. This
is the corresponding boundary to native `WmKeyChar` followed by `DefWndProc`;
it is not a replacement input router or text-layout engine. The native Windows
branches remain unchanged.

`TextBoxBase` retains its existing `WindowText`, cached UTF-16 selection and
selection-normalization policy. Unhandled committed characters replace that
selection, advance the same source indices and raise canonical modified/text
notifications. Read-only state, input length, changed/handled `KeyPress` values,
ordinary `TextBox.CharacterCasing`, and basic Backspace/Delete are honored.
Programmatic selected-text replacement uses the same source ownership without
the user-input length limit. A programmatic `Text` reset clears `Modified`.

The original `MaskedTextBox.OnKeyPress` and `MaskedTextProvider` run first and
consume masked input; the default operation does not insert it twice. Suppressed
key text stays suppressed through the corresponding input sequence and is cleared
on key-up or focus loss. A disposed source control cannot receive the remaining
characters of its captured text-input batch.

The grid forwards the initiating key to its real editing control through this
typed path. Following portable text input is addressed to the focused editor,
not to a queued old grid HWND. The canonical editing panel, cell value lifecycle,
new-row placeholder, Enter commit and Escape cancellation stay source-owned.

## Qualification boundary

This is a committed-text and source-state contract, not complete native EDIT
emulation. IME composition/preedit, full clipboard shortcuts, undo history,
all navigation/selection gestures, caret rendering and native presentation remain
separate work and qualification. The portable grid does not clear a nonexistent
native undo buffer during detachment; the public undo API is not declared supported
by that cleanup change. No renderer or OS fallback is introduced.

The issue's form sizing, title-bar and analyzer observations are separate. Its
designer uses font autoscaling with 6x13 design metrics, so actual font/DPI evidence
is required before calling differing pixel extents a defect. Headless input tests
do not establish Linux window-decoration or rendering parity and do not close
the whole issue.

## Regression coverage

`CanonicalTextInputTests.cs` uses the existing typed headless window provider and
actual canonical controls. It verifies default-mode letter/F2 editing, the initial
new-row placeholder, real text input followed by Enter/Escape, UTF-16 positions,
selected-text replacement, exact notifications, filter and event rejection,
read-only/length/casing, suppression, surrogate-pair deletion, original masked
input and disposal during a text batch. It never starts editing by calling
`BeginEdit` or assigns the edited value instead of dispatching typed text input.
`ShowIcon=false` avoids unrelated bitmap resizing in these headless input fixtures.

The ordinary source-first and packaged/native application gates remain required;
passing these contracts alone is not full official sample or platform qualification.

Initial local source validation on macOS ARM64/.NET 10.0.5 passes all 13 new cases
with zero skips. The source test build has zero warnings/errors; the isolated
canonical Forms build has zero errors and 615 existing portable warnings. The test
host and fresh Forms project DLLs had the same SHA-256 before execution.
The unchanged compiled 13-case test fixture run against the independently retained
old source-built Forms DLL fails 12 cases (one cancellation-only control passes).
The old and new runtime identities are unchanged. This paired result is source
evidence, not a packaged or visible application run.

## Primary references inspected

- [Official sample main form](https://github.com/dotnet/samples/blob/acb39ceb13f910ae0f8f6298059c59102b749c41/windowsforms/datagridview/CSWinFormDataGridView/CustomDataGridViewColumn/MainForm.cs)
- [DataGridView edit modes](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.datagridvieweditmode?view=windowsdesktop-10.0)
- [Control.ProcessKeyMessage](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.control.processkeymessage?view=windowsdesktop-10.0)
- [TextBoxBase.SelectedText](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.textboxbase.selectedtext?view=windowsdesktop-10.0)
- [TextBoxBase.Modified](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.textboxbase.modified?view=windowsdesktop-10.0)
- [KeyEventArgs.SuppressKeyPress](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.keyeventargs.suppresskeypress?view=windowsdesktop-10.0)

Implementation provenance is the existing repository's original canonical
`Control`, `TextBoxBase`, `TextBox`, `MaskedTextBox` and `DataGridView` source.
No foreign edit engine, reflection-driven adapter or WinForms-shaped replacement
control is used.
