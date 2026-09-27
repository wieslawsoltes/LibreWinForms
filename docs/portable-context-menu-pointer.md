# Canonical context menus from portable pointer input

Portable right-button release now enters the receiving control's original
`WndProc(WM_CONTEXTMENU)` before its `Click`, `MouseClick`, and `MouseUp`
callbacks. This matches the ordering in the checked-in canonical `WmMouseUp`:
Windows default processing generates the context message, or the `UserMouse`
branch sends it explicitly. Portable logical controls have no native default
window procedure to do that work.

The source procedure remains responsible for menu lookup and cancellation. This
preserves `TextBoxBase` policy, `DataGridView` cell menus, virtual overrides, and
`ContextMenuStrip.SourceControl`. The portable default procedure forwards an
unhandled context message to its actual source parent. There is no direct menu
lookup in the input adapter, alternate menu implementation, native input
injection, or Windows-native branch change.

Release cleanup owns a captured press generation. Opening/click callbacks may
dispose or recreate the receiving source, throw, or start a replacement press.
Old continuations do not deliver callbacks to a new handle or clear the new
press's capture; an original callback exception survives cursor cleanup failure.
Left/middle buttons and releases outside the captured control's client area
remain negative controls.

## Evidence and limits

The exact installed PR83 macOS popup attempt stopped at `02-context` with the
original 60-second deadline. Retained evidence is
`/Volumes/1TB-macOS/popup-macos-interaction-w2bhnxan/portable/receipt.json` and its
native request/reply files. The driver verified the complete source/native
client mapping before posting the right-button pair, but posting success is not
an acknowledgement that the application received it. An unrelated OS dialog was
left untouched. The original failure is not replaced or qualified by this fix.

The source gap is independently reproduced by 16 typed-input regression cases:
13 failed and three negative controls passed on unchanged source, with zero
skips. The tests cover Button/Panel/TextBox, parent forwarding, virtual message
consumption, canonical event order, cancellation, original exception identity,
source/owner disposal and owner recreation, replacement capture, and an actual
DataGridView cell menu. The full canonical gate retains its original 533 cases
and raises the minimum to 549; deadlines and skip policy are unchanged.

Before/fixed logs are retained under
`/Volumes/1TB-macOS/librewinforms-dropdown-keyboard.gZduNA/artifacts/context-menu-pointer/logs`.
The corrected source build completed with zero errors (630 existing warnings),
then all 16 focused tests and all 549 canonical tests passed with zero skips on
.NET 10.0.5 ARM64. The whole source run took 14.936 seconds under the unchanged
10-minute gate. The initial test-only build's naming-analyzer error is retained;
its fixture counter was corrected to a property without disabling the analyzer.
No native desktop rerun, package producer, keyboard context-menu entry, default
native EDIT menu, or complete popup parity is claimed here. Actual installed
Windows/macOS/Linux acceptance remains independent.
