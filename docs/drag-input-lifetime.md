# Drag input lifetime

Each application-local ProGPU drag operation registers its own input lease.
Queued callbacks from that registration cannot enter a later operation, including
callbacks dispatched through the owning UI thread. The lease is revoked before
external teardown and releases its service reference even if teardown fails.

Registration is inside the service lifetime guard. A rejected registration does
not call EndDrag, while a successful registration is released once using its exact
sink identity. Service session state is cleared on every exit. A source error
remains primary if release also fails; the release error is retained under
`Exception.Data["DragInputRelease"]`. A release-only failure still propagates.
This does not repair an external input provider that fails to release its own
resources; its ownership contract remains authoritative.

The service commits the terminal state before calling application Drop or
cancellation Leave callbacks. Reentrant release/Escape input cannot dispatch a
second terminal callback or replace the result, including when application code
throws. Registration cleanup still belongs to the outer DoDragDrop lifetime.
This does not serialize or qualify reentrancy in nonterminal source callbacks.

Eighteen authored backend cases cover registration failure, direct and queued source
failures, teardown errors, original-error precedence, queued old pointer/Escape
events during a later drag, teardown callbacks, dispatcher disposal and successful
subsequent drags. Eight cases exercise reentrant release/Escape in Drop and
cancellation Leave, with successful and throwing callbacks. The existing
source-first CI retains its full backend gate and an eighteen-case, two-minute,
no-skips lifetime gate. Local validation is compilation
only; hosted CI owns execution.

## Native pointer cancellation

The canonical Control now supplies its actual containing platform window in the
additive `LibreDragDropRequest.SourceWindow` property. The original constructor
and positional deconstruction are unchanged. A hosted popup control supplies the
popup window, not the Form lending it keyboard focus; unhosted callers keep a
default value without creating a window. Legacy default-owner requests retain
ordinary drag input but have no admitted native cancellation owner.

The Silk service binds a nondefault owner to its actual registered live window
object, handle generation and creating dispatcher before publishing a drag
registration. Foreign, logical, retired and wrong-thread owners are rejected.
PointerCancel cannot infer that identity from coordinates, a hit target, an opaque
native handle or the most recent pointer event. Button retirement removes only
that window's held buttons, preserving other windows, position and modifiers.

Cancellation commits None/complete before character/source callbacks can pump a
release or Escape. Its internal owner-thread token retains only the old session
and target and notifies DragLeave once after source capture/press/hover retirement.
The token releases its references before calling application code and is revoked
when its registration ends, so deferred completion cannot enter a later drag.
Character/source errors remain primary when DragLeave cleanup also fails. Source
retirement still runs after a character callback error. A cancelled nonterminal
query/hit/enter/over/feedback continuation cannot publish new state or Drop. When
DragEnter returns an accepted target after nested cancellation, that exact target
receives its terminal Leave without becoming active.

Twenty authored backend contracts exercise the actual router/service/token path,
including foreign/missing/retired owners, same-window later leases, release/Escape
reentry, nonterminal callbacks, duplicate completion and cleanup errors. Three
canonical source cases cover Form, hosted popup and unhosted request ownership.
The unchanged full gates remain, plus strict two-minute/no-skips focused selectors.
Local validation is compilation only; these new cases have not been executed
locally. Actual CI execution and native platform/desktop qualification are separate.

The actual backend/test project compiled with zero errors and two existing
IDE0017 warnings. The canonical source/test project compiled with zero errors
and 635 warnings. Both used SDK 11.0.100-preview.5.26302.115 targeting net10.0,
the pinned ProGPU `e17ba9bda99971f65cf4cd516fb194c6076336bf` in a fresh detached
source worktree, isolated output/cache/temp paths, and the original source-output
identity guards. Initial StyleCop formatting failures were retained and corrected
without disabling analyzers. These are compilation results, not test passes.

PointerLeave remains hover-only and never cancels a drag. Neither retirement event
is sent to the drag sampler; cancellation does not synthesize QueryContinue,
Escape or Drop. This does not select the owned Cocoa factory, change wheel policy,
or qualify native drag input, scrolling, popup interaction or desktop UI.
