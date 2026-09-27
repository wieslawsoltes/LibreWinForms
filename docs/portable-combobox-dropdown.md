# Canonical portable ComboBox dropdown

The portable `ComboBoxStyle.DropDownList` / `DrawMode.Normal` surface uses the
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

- Editable `DropDown` opening and owner-drawn dropdown surfaces are rejected;
  their native edit/measurement/paint contracts have not been connected.
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

## Validation status

`CanonicalComboBoxDropDownTests` exercises public source controls through the
existing typed headless window/input fixture, including an actual ListBox/window,
pointer row admission, owner keyboard, callbacks, failed native admission,
unsupported surfaces and source paint transport. Its helper observes the real
keyboard recipient via the ordinary message filter; it does not scan private
fields or fabricate a ComboBox-shaped object.

The initial implementation and tests are authored but not yet compiled or run.
They require the canonical ListBox prerequisite and the existing ComboBox hook
wiring in the integration branch. No macOS native input, screenshot or successful
14-phase application result is claimed by this change.
