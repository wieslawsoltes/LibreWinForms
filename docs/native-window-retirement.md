# Native window retirement in the source dispatcher

Logical source disposal is not proof that an owned native popup released its
panel/view. ProGPU's `NativeWindowLifetime.TryDispose` can return pending while a
native/managed callback, initialization or renderer lease still owns it. A native
hide/session failure also requires retaining the owner for a later explicit retry.

`SilkLibreWindow.ReleaseNativeWindow` still removes the logical service/handle and
normal window participant, disposes input, clears source visuals and raises Closed
once. Native retirement is now transferred to its creating dispatcher before a
provider attempt. The queue retains both the exact `IWindow` and the source owner's
render-resource cleanup callback. Compositor/context cleanup remains ordered; a
successfully released resource is cleared, and successful rendering cleanup is not
repeated while native completion is pending. A failed renderer owner remains
reachable for retry. Source teardown first attempts to hide the window without
destroying its surface. Native destruction starts only after renderer cleanup
succeeds, including for legacy providers without Cocoa's native view-lease guard.
Renderer failure therefore retains both the renderer owner and its native surface.

The queue deduplicates by reference identity. Each drain attempts each original
entry once; reentrant requests cannot recurse into disposal, and newly queued
windows wait for the next boundary. Only successful rendering and native
retirement removes an entry. It does not inspect raw handles or poll events.
All failures remain observable: the first error stays primary, later errors are
attached, and one failed window does not prevent other entries from retiring.

`ProGpuDispatcher.PumpOnce` drains in its finally path after source work and native
polling unwind, including callback failure and an exit request. A disposal failure
does not replace an original source exception. Dispatcher/provider release first
checks pending retirement, draining only on the owning thread; it refuses to
discard a pending queue or dispose its wake handle. A rejected release leaves the
dispatcher available for further pumping and explicit shutdown retry.

Seventeen managed queue/dispatcher tests cover synchronous/deferred/failed/native
disposal, reference ownership, reentrancy, thread guards, continued cleanup,
renderer failure retention, one-time successful renderer cleanup, exception
precedence, exit and shutdown. They are included in the full backend suite and a
separate two-minute/no-skips hosted gate. Local work is compilation only.

This source change depends on ProGPU's retirement API and the source pointer
boundary parent. Both exact producer/dependency PRs and the whole current Forms
Build must pass before merge/package admission. No partial/failed producer package
is staged by this work. It does not select the owned Cocoa factory, change wheel
compatibility, qualify native modal sessions or establish platform UI parity.
