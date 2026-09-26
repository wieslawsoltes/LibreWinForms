# Canonical dropdown platform windows

Top-level canonical `ToolStripDropDown` / `ContextMenuStrip` handles now own a
real `ILibreWindow` with the existing `Popup` capability, instead of a logical
child-control handle. The window starts hidden and undecorated, outside the
taskbar, with its source topmost policy. Precreating a handle is permitted before
an owner is available; showing is not. A parentless logical control cannot stand
in for a native owner.

After a successful source `Opening`, the chain's retained, typed `Form` owner
must have a live visible window and must not be minimized or closing. Ownership
is bound before display. A precreated window keeps its handle. A hidden/reopened
chain binds its new owner without activating it; surviving visible children are
hidden while their native owner is changed, then shown through the same popup
admission. Source bounds are already arranged in the selected logical/device
coordinate mode and must not receive a second Form autoscaling pass.

The existing typed paint/input callbacks reach the actual canonical dropdown
renderer and menu items. No alternate menu implementation, reflection, fake
HWND messages, bitmap overlay, or source clipping to the owner's bounds is used.
Ordinary child controls keep logical handles; ordinary Form creation is retained.
The backend's native ownership and nonactivating display contract is documented
in [native popup admission](native-popup-window-admission.md).

## Owner lifetime

The native owner's registered popup set covers persistent and ordinary menus,
including hidden handles. Owner hide, minimization, or handle release makes those
surfaces unavailable. This is native resource teardown, not a cancelable menu
close request: the source dropdown becomes hidden, receives handle/closed
notifications, and remains a reusable application-owned object. A later show
creates a new handle and binds its live owner. Normal dropdown close retains the
existing cancelable source lifecycle and can reuse its hidden window.

Owner transitions must reject reentrant new popup admission and finish resource
cleanup even when application callbacks throw. A provider's rejected ownership
or display destroys the rejected native surface and reports `Closed`; the source
must not retain a stale handle. Original callback exceptions remain errors.
Ownership is revalidated after synchronous native setup callbacks. A rejected
display does not raise `Opened`; an admitted display retains the original
callback behavior if a later managed visibility handler throws. Each native
window has its own event adapter identity, retired before teardown, so delayed
callbacks cannot mutate a replacement handle on the same source object.

## Hosted control visibility

The portable `Control.SetVisibleCore` path now retains the control's own
visibility flag even when effective visibility already matches because its
parent is hidden. This matches the original native source branch. Without it,
the dropdown's hidden scroll-button labels became visible when the menu opened
and intercepted pointer input over menu items. The fix applies to all source
controls; it does not special-case scroll buttons or bypass source hit testing.

## Focused source coverage and remaining acceptance

`CanonicalNativePopupTests.cs` exercises actual source controls against the typed
headless window service: independent creation, hidden staging, late ownership,
handle reuse, source painting, actual menu-item mouse move/down/up/click input, nested persistent-window
cleanup, reuse after owner disposal, failed creation, and invalid logical owners.
The old lifecycle fixture now uses a shown real Form, and its original opening/
closing assertions remain intact. Separate lifetime, admission and generation
fixtures retain callback exception identity, reentrancy, owner loss, rejected
display, reusable source objects and delayed native callback coverage.
The prior logical-handle topmost test now
requires independent popup topmost state while the owner remains unchanged.

An initial complete run was 268 passed / 3 failed / 0 skipped. All three failures
were the old recorder's global `group`/`link` text whitelist rejecting actual
menu painting of `Open`. That exact whitelist and both text identities now belong
to the original GroupBox/LinkLabel test; menu painting independently requires its
own source text. Existing TextBox, tooltip, bounds, font, format and color checks
remain intact. This is not a pixel parity assertion.

Actual native desktop acceptance still requires paired Windows reference and
portable Windows, Linux and macOS captures/input, including edges/DPI, nested
menus, keyboard focus/navigation, outside-click/Escape, owner changes, appearance
and teardown. Nonactivating native windows alone do not route owner keyboard
events to the active dropdown or establish outside-click/menu-mode parity.
Those source-input connections and native UI qualification remain open under
ProGPU issue #197; no whole-application parity is claimed here.
