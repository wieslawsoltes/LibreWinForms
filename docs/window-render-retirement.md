# Window render retirement

`SilkLibreWindow` keeps its creating-thread render boundary active around the
entire source frame: renderer initialization, `PaintRequested`, retained frame
completion, the current-context scope, GPU presentation and acquired texture/view
release. Reentrant rendering returns without consuming pending paint flags.

Logical disposal still removes input, native controller subscriptions and the
source handle and raises `Closed` once. The dispatcher retains the exact native
window, paint resources and renderer cleanup owner immediately. A per-retirement
gate returns pending while rendering or initialization is active. A nested
dispatcher pump cannot clear a recording visual, dispose the compositor/device or
destroy the native surface. Independent queued windows remain eligible to retire.

After `PaintRequested` and frame completion, the host checks disposed state and
the exact captured compositor/context identities before presentation. Accepted
source disposal stops the old frame; a canceled close or hide alone is not resource
retirement. Immediate-paint loops and update callbacks also stop after disposal.
Closing callbacks that dispose and return cancellation cannot reset a retired
provider's `IsClosing` property.

The outer frame unwind retries dispatcher retirement without polling native
events. A renderer cleanup failure remains pending and observable; the original
paint error stays primary if cleanup also fails. Compositor cleanup precedes
context cleanup, and failure retains that dependency. Renderer initialization
also owns its unpublished compositor/context until successful publication or
successful cleanup. Failed unpublished cleanup cannot be overwritten by another
renderer: dispose the source window to retry it. Native disposal remains blocked
until all renderer cleanup succeeds.

The existing no-skips/two-minute retirement gate now includes four additional
queue cases (minimum 21). A separate no-skips/two-minute boundary gate requires
ten cases: reentrant rendering, acquired-scope ordering/failure, a real retained
paint frame, nested dispatcher pumping, primary error preservation and retry,
cleanup error publication, shutdown refusal, thread ownership, and an explicit
actual-host source wiring guard. The wiring guard is not native execution; mock
acquired-scope markers are not GPU texture evidence. Full source/backend/package
CI and final platform/application checks remain required. No local build/test or
GPU execution was performed while implementing this change.

This does not select Cocoa factories, admit native scroll/modality, establish
desktop UI parity or close the popup issue by itself.
