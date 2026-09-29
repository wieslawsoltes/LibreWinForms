# Portable pointer callback lifetime

Portable source pointer dispatch retains the receiving window handle and an
input generation through outside-click dismissal, hover notifications, cursor
updates and focus changes. A nested pointer event or capture cancellation retires
that continuation. Delivery also requires the original live source root and
target handle, visibility, enabled target and source-tree ownership.

Hover is retired before calling `MouseLeave`. A callback which throws cannot
leave that old hover installed, and nested input cannot send its leave twice or
have its replacement hover overwritten by the outer event. `MouseEnter` may
dispose, hide, disable, reparent or recreate either recipient. The original
pointer event does not continue with `MouseDown` in the retired target.
The retained hover also records both native source handles: reopening the same
popup object starts a fresh hover lifetime, without delivering the old handle's
leave callback into the replacement window.

Hover ownership is also thread-wide across native source windows. A pointer
packet entering an owner, dropdown, submenu or sibling window retires the prior
window's hover before entering the recipient. This invokes the canonical
ToolStrip item-leave and submenu-timer cancellation path. Keyboard/focus packets
do not transfer physical hover. Both roots' obsolete pointer continuations are
invalidated before callbacks, so nested input returning to the previous window
or entering a third window retains its own hover. A throwing leave is not
repeated, and destroyed/recreated source handles receive no stale leave.

Focus callbacks use the same input generation in addition to the existing press
generation. A nested press retains its own pressed-button state and capture; an
older focus continuation cannot clear them or install its own stale capture.
Physical button release precedes hover callbacks. If those callbacks retire an
already captured recipient, cancel only that still-owned capture, without a
synthetic up/click or mutation of a nested replacement press.
Capture transitions have their own generation: releasing and reacquiring the
same control during MouseUp is not the old capture and survives its cleanup.
Ordinary mouse event, click and context-menu ordering remains unchanged, as do
the original Windows implementation and existing source mouse-up contracts.

The canonical headless source suite contains twenty-six regression cases covering
nested leave/enter input, throwing leave callbacks, six recipient-retirement
operations at both hover boundaries, reopening the actual source popup, and
nested focus/press/capture and retired-capture cleanup. CI retains the unfiltered suite and runs the new cases
separately with a two-minute deadline and skipped tests rejected.
The last qualified producer ran 772 canonical cases with no skips; the full-suite
minimum is raised to 798 to include the new twenty-six cases without dropping
the existing coverage.

Twelve additional cross-window cases cover all four pointer packet kinds,
returning hover, nested window changes, keyboard independence, throwing callbacks,
retired windows, actual parent/submenu ownership and pending submenu cancellation.
The unfiltered canonical minimum is 810 and the additional focused CI gate keeps
the same two-minute limit with no skips. Source-native OS leave notifications and
cross-window capture transfer are not inferred from these source transitions.

The existing Silk backend delivers `FocusLost` both for native focus loss and
when native input is disabled. Nonactivating popups may never have held keyboard
focus, but their pointer capture, pressed state and hover must still retire.
Source dispatch now retires those states before considering keyboard focus.
Button ownership is tracked independently for all five buttons: a delayed loss
from another window cannot clear the current window's held button or keyboard
modifiers. Retirement clears state before capture/leave callbacks, emits no
synthetic release/click and does not move the pointer. Nested pointer input owns
its replacement; an explicit new focus packet also supersedes the old loss even
when it targets the same still-focused source window.

Capture/leave errors do not prevent the still-current focus-loss transition.
The original callback error remains primary, with later cleanup errors attached.
Fifteen source cases cover five-button popup cancellation, independent window
ownership, nested input/focus and throwing callbacks. CI keeps the previous full
and focused suites, raises the full minimum to 825, and adds a 15-case gate with
the same no-skips/two-minute policy. This consumes the existing source event
contract; it does not opt into the lossless native pointer provider or change
native backend factory selection.

These are source lifetime contracts, not native input-provider or desktop UI
qualification. They do not select the Cocoa owned-window factory, implement
deferred native owner-bound creation, add a wheel-unit conversion policy or
qualify modal/focus behavior on an actual desktop. Those remain separate work
under ProGPU issue #197. Local work for this change is compilation only; runtime
regressions run in hosted CI.
