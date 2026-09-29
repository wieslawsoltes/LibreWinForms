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

This change does not turn PointerLeave/PointerCancel into drag samples, invent a
drop on input loss, select the owned Cocoa factory or change wheel compatibility.
Native drag cancellation, provider integration and actual desktop popup/drag UI
qualification remain separate requirements.
