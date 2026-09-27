# Canonical portable ComboBox dropdown

The portable `ComboBoxStyle.DropDownList` and editable `DropDown`, with
`DrawMode.Normal`, use the
actual source `ListBox`, hosted by `ToolStripControlHost` in an owned native
`ToolStripDropDown`. It is not a menu-item approximation or a private list widget.
The ListBox prerequisite owns row painting, point lookup, selection, keyboard
navigation and scrolling through the canonical source collections and events.

The closed selection field uses the original font and text through `TextRenderer`
and existing `ControlPaint` border, arrow and focus operations. It intersects the
source clip, retains RTL direction and obtains button width from the per-DPI
system metric. This is portable content rendering, not Windows theme/pixel parity.

## Lifecycle and input boundary

`DroppedDown` reads the actual live popup surface. A pending opening is not a
visible dropdown. `DropDown` runs before display; the captured source handle,
Form and ancestor lifetime are checked after callbacks. Closing retires the old
lease before a callback can establish a replacement. Owner loss, hidden/disabled
source, reparenting and destruction cannot retain the list window. Backend popup
admission failures remain failures, with original exceptions preserved.

The existing hosted-control focus lease keeps native focus on the real Form.
Its actual ListBox source receives owner input. Caller filters and ComboBox
`KeyDown` handlers precede ListBox default navigation. Enter and accepted row
pointer input use the original `OnSelectionChangeCommittedInternal` recursion
guard; programmatic `SelectedIndex` changes do not manufacture commits. Escape
closes without commit. Opening, closing, and selection callbacks may replace or
retire the source; old input must not close or select the replacement generation.

An opening snapshots actual collection entries and their item identities. It
uses `ComboBox.GetItemText`, including source formatting, and validates again
after formatting callbacks. Changing the collection while open is currently an
explicit unsupported operation at selection/commit admission, rather than
allowing a stale row index to select a different item. Close and reopen after a
collection update until a live collection-refresh seam is implemented.

## Explicit remaining surfaces

- Owner-drawn dropdown surfaces are rejected; their source measurement/paint
  contracts have not been connected.
- `Simple` keeps the documented `CB_SHOWDROPDOWN` no-op. That does not implement
  its always-visible embedded list or editable text behavior.
- The normal ListBox source prerequisite explicitly rejects unsupported
  owner-draw, multiple selection, multicolumn and scrollbar-control modes.
- Theme fidelity, native accessibility notifications, physical keyboard layouts,
  native pointer/capture behavior on each platform, and the complete desktop
  interaction scenario remain separate qualification requirements.

Microsoft's [combo-box documentation](https://learn.microsoft.com/en-us/windows/win32/controls/about-combo-boxes)
defines a single selection and distinguishes acceptance from cancellation.
[`CB_SHOWDROPDOWN`](https://learn.microsoft.com/en-us/windows/win32/controls/cb-showdropdown)
has no effect for Simple style;
[`CB_GETDROPPEDSTATE`](https://learn.microsoft.com/en-us/windows/win32/controls/cb-getdroppedstate)
reports actual list visibility.

The retained Windows reference from `paired-pr74-layout` proves only the actual
DropDownList opening and Down/Enter acceptance: phase 12 has index 0 and a real
native list, phase 13 has index 1 and no list, with one each of `DropDown`,
`SelectionChangeCommitted`, and `DropDownClosed`. It does not observe selection
change event order, focus getters, Escape, toggle-close, F4, mutation or callback
exceptions. Source regressions for those paths must not be relabeled as native
desktop parity.

## Editable source ownership

Editable `DropDown` creates an actual canonical, borderless, single-line
`TextBox` child. That child owns text input, source painting, UTF-16 selection,
selected-text replacement, and MaxLength input admission. Programmatic `Text`
keeps the original ComboBox matching-item policy; actual edits instead follow
the checked-in native `CBN_EDITUPDATE` / `CBN_EDITCHANGE` path, without inventing
selection-change or acceptance events. Source callbacks can replace text,
change focus or dispose the control; obsolete work must not finish afterward.

The editor retains real Form focus while its actual ListBox popup is open. It
does not borrow the ToolStripControlHost focus lease: a distinct typed keyboard
target capability verifies the exact popup, editor and owner handles, parent,
active owner and current source focus. The list remains the canonical ListBox;
only its source Selectable style is disabled for this nonactivating composite.
Row pointer selection and plain Up/Down use its existing source operations.
Filters and ComboBox key events run once before defaults; Home/End and modified
edit keys are not redirected into list navigation. Enter accepts; Escape restores
the opening text/selection when no callback has superseded that source state.
Leaving the composite or retiring the editor closes the old popup target.

The actual editor now handles single-line Home/End and Shift selection through
the shared [plain-text boundary navigation](portable-text-boundary-navigation.md)
path, without committing or moving the popup list selection. This is source
selection movement, not drawn caret or native desktop qualification.

The original source documentation and Microsoft's
[combo-box key routing](https://learn.microsoft.com/en-us/windows/win32/controls/combo-box-features)
distinguish editable input from DropDownList list input. This connection does not
implement missing TextBox caret/selection ink, glyph hit placement or scrolling,
nor Simple's embedded list, owner draw, autocomplete, native accessibility or
Windows theme equivalence. Those capabilities and actual native editable desktop
interaction remain separate requirements.

## Validation status

`CanonicalComboBoxDropDownTests` exercises public source controls through the
existing typed headless window/input fixture, including an actual ListBox/window,
pointer row admission, owner keyboard, callbacks, failed native admission,
unsupported surfaces and source paint transport. Its helper observes the real
keyboard recipient via the ordinary message filter; it does not scan private
fields or fabricate a ComboBox-shaped object.

The integrated source build completed with zero errors and 629 warnings (the
existing 623 plus six portable protected-override API declarations). All **479
source cases passed, zero failures/skips**, including all 406 prior cases, 33
ListBox cases, nine closed ComboBox observation cases and 31 popup cases. The
unchanged ten-minute deadline and fail-on-skip policy remain in the source gate.

The first complete run retained 476 passes and three failures: disposal had
retired its native handle without reporting closure, and two paint assertions
incorrectly observed a TextBox-only fixture collection. Disposal now reports
closure from actual handle retirement, once, with the reentrant source guard
still held. Painting is observed through the shared text service, including
font, color, direction and intersected clip; the product was not changed to emit
TextBox flags. Initial compile/analyzer failures and the failed test run remain
beside the final logs under `artifacts/dropdown-keyboard/log/combobox-*`.

The integrated editable implementation has **32 source cases**, covering the
actual editor, composite focus,
input filter/event ordering, UTF-16 selection, user/programmatic text policies,
plain versus edit-navigation keys, real row/arrow pointer input, cancellation,
callback replacement/exception and lifetime retirement. The existing three
unsupported-style cases remain three explicit owner-draw rejection cases.
Four additional shared input cases keep standalone text-packet retirement
separate from held-key suppression, including nested input ownership. The first
integrated run passed 519 of 521 and exposed two real bugs: source recreation
kept the ComboBox focus identity but did not focus its replacement editor, and
retirement during a standalone character callback could suppress later text
forever without a matching key-up. Exact-generation focus completion now routes
to the actual replacement editor; obsolete text suffixes and physical key cycles
retain separate cancellation ownership. Neither failure was removed or relaxed.

The final integrated source build passed with zero errors; all **529 source
cases passed with zero skips** on macOS ARM64/.NET 10.0.5, including the 493-case
initial-position prerequisite and 36 additional editable/input cases. Logs are
`artifacts/dropdown-keyboard/log/combobox-edit-input-fixed-build.log` and
`combobox-edit-input-tests.log`. Earlier failed runs remain alongside them.
The 479-case result above predates these changes and is not their qualification.

Full exact-head CI and installed native desktop interaction remain required.
No macOS native input, screenshot or successful fourteen-phase application
result is claimed by these source tests.
