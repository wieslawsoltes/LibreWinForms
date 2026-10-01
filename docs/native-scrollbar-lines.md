# Native line quantities in a standalone ScrollBar

The canonical portable `ScrollBar` now owns a typed native Lines consumer through
the existing `Control.PortableScroll` frame/plan protocol. It uses the actual
source `SmallChange`, `Minimum`, `Maximum` and `LargeChange`; it does not infer
pixels from arbitrary `Value` units or fabricate a `MouseWheel` event.

## Source operations and admission

Only enabled, childless standalone bars are admitted. Horizontal bars consume X;
vertical bars consume Y. Any nonzero cross-axis component or Points unit declines
this consumer before value mutation; an ancestor may consume only the complete
vector under the existing dispatcher policy. A bar hosting children remains
unsupported so it cannot be selected behind a different preflight hit target.
Legacy wheel processing and ordinary Windows `DoScroll` remain unchanged.

Fractions stay in line units until a whole line is available. Each whole line
raises the source's actual SmallIncrement/SmallDecrement `Scroll` event, honors
its mutable `NewValue`, and calls the actual `Value` setter/`ValueChanged` path.
After an uninterrupted packet with whole lines, one EndScroll event uses the
same mutable source contract. Fraction-only packets raise neither event. This
is the declared native consumer policy, not recovered Microsoft wheel-detent
behavior. No 120-unit conversion or system wheel multiplier is involved.

The numeric line operation follows the source's directional bounds: decrement
clamps at Minimum; increment clamps at Maximum-LargeChange+1. An ordinary public
Value above that page endpoint is not silently normalized before a decrement.
LargeChange=0 also gives the actual SmallChange=0 and keeps valid public values.
Native arithmetic uses a wide intermediate; source metrics already overflowed
by the existing public getters remain explicitly unsupported. Direction uses
the actual source RightToLeft getter: HScrollBar may reverse, while VScrollBar
always returns No and intentionally ignores its setter. Native user inversion
is not applied again.

An explicit absolute 1024-line quantity bound limits source callback work per
packet. It is checked read-only at the actual source hit target, or the existing
same-generation pinned momentum consumer, before source pointer/global state,
hover, gesture or fractional-carry publication. A rejected packet leaves caller
metadata and the previous carry untouched. Cancellation executes no vector, and
stale momentum never hit-tests a replacement even for preflight. Points and other
consumers do not acquire this bound. The plan repeats the bound as a guard.
Integer and fractional additions are separate so a nearly-one remainder cannot
round an exact 1024 input up to 1025 operations in floating-point arithmetic.

## Retained lifetime and callbacks

The carry frame owns only an ownerless identity and exact numeric/direction
state. Actual source property changes retire the identity before callbacks,
including a change away and back to the same Value, range or increment. Enabled
transitions do the same. A real RightToLeft transition uses Control's existing
handle recreation, invalidating the shared exact-handle carry before public
RightToLeftChanged events; no new ScrollBar override is needed. The shared
dispatcher retains weak consumer identity, source/target handles, stream/generation, units, point-scale,
gesture cancellation and momentum routing; this consumer adds no polling.

The expected final frame/tail is published before source events. An actual
native Value assignment consumes its identity token before ValueChanged or
accessibility callbacks, so nested ordinary assignments cannot inherit it.
Every Scroll/Value callback tail rechecks dispatch and exact source state.
The virtual RightToLeft getter is also a callback boundary: both frame identity
and dispatch are rechecked after reading it, before an old Scroll can be raised.
Handler-written NewValue continues through the actual setter, but retires the
original plan's fractions. Direct source mutation, disposal or nested input
stops obsolete writes and EndScroll; already applied values are not rolled back.
Exceptions preserve their identity and cannot clear newer nested carry.

Actual-source tests are authored through the existing headless Form, typed native
input provider and source dispatcher. They cover both orientations/RTL, source
event order, fractions and bounds, metric/value/enable transitions, provider
generation/cancel, early rejection retaining prior fractions and no hover/events,
the exact callback bound, unsupported Points/cross-axis/child hosts, mutable
Scroll/EndScroll values, disposal, nested callback failures and momentum pinning.
Hosted/native/GPU/desktop qualification is separate; no such execution or
ordinary editor/native factory admission follows from this source change.

## Focused managed receipt

On 2026-10-02 the actual Forms source and canonical lifecycle test project were
rebuilt independently with `BuildProjectReferences=false` and `--no-restore`,
using isolated copied caches and read-only dependency outputs from qualified
ProGPU `60347a5f1026b8f95582535e6b7cfc8431a04499`. Forms completed with 648
warnings and zero errors; the test project completed with zero warnings/errors.
The explicit `*NativeScrollBar_*` selector then passed all 33 cases, zero skips,
in 479 ms, with the existing two-minute timeout. Source and test-output copies
of `System.Windows.Forms.dll` both hashed to
`361a695f13d967da9b33b52dbf6a2bd21202c657f28737aef41231d0b880cb75`.

The first 31-case run preserved its two failures: the fixture incorrectly
expected VScrollBar's ignored RightToLeft setter to reverse scrolling and retire
carry. Correcting that fixture to the actual VScrollBar getter and using
HScrollBar for a real RTL transition produced 31/31; the final run also includes
two source virtual-getter reentrancy controls. Local logs remain under the
isolated worktree's `artifacts/scrollbar-*.log`; no original failure was erased.

The existing hosted source-first lane selects these 33 cases explicitly with
fail-on-skip and the same timeout, and its full canonical minimum increases by
33. No native runtime artifact was staged, no dependency graph or native/GPU/VM
build was run, and this is not hosted, coherent full-graph or native UI evidence.
