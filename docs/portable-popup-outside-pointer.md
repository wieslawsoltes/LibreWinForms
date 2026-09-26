# Portable menu dismissal for outside pointer presses

The typed native-window pointer route now applies the canonical dropdown
outside-press policy before dispatching the ordinary clicked control event.
It reuses the source menu chain registry and canonical `AppClicked` close reason,
`Closing` cancellation, `Visible` transition and `Closed` notifications. It does
not activate a popup, synthesize Win32 messages, or create an alternate menu.

The policy follows the original
`ToolStripManager.ModalMenuFilter.ProcessMouseButtonPressed` algorithm: capture
the initial eligible-menu count, inspect the current deepest leaf on each
attempt, stop at a containing client rectangle, and let an owning dropdown item's
button handle its existing toggle. A canceled leaf consumes the same bounded
attempts; a reentrant opening does not extend the bound. Persistent
`AutoClose=false` menus stay outside admission while their ordinary children
can close. Primary, secondary and middle presses match the original filter;
extended buttons, movement, release and wheel input do not acquire a new
dismissal policy.

Coordinates come from the real receiving source window and are translated using
the existing `PointToScreen`/`PointToClient` methods. The source hit control, not
the top-level window alone, determines the owner-button exemption. Hosted
controls remain inside their popup. Owner and sibling-window clicks both reach
the active menu chain. After callbacks, the receiver's original handle identity
must still be current before normal input continues; otherwise the event is not
delivered into a disposed or replacement window. Canonical callback exceptions
remain exceptions, not ignored close failures.

`CanonicalPopupPointerTests.cs` adds 19 typed-platform cases to the original
300-case source gate (minimum 319). It exercises actual source menus, buttons,
hosted controls, nested cancellation, close ordering, sibling windows, reentrant
opening, receiver disposal and preserved exceptions. No old test selector or
assertion is removed. The cases have not yet been built or executed.

This is source input integration. It does not qualify an OS-wide outside click,
other-process dismissal, compositor/window-manager capture, native keyboard
menu mode or desktop pixels. Actual Windows/macOS/X11 popup validation remains
separate under ProGPU issue #197.
