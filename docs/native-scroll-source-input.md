# Native scroll quantities in canonical Forms

The native adapter previously rejected every Scroll packet. The phase-less source
path now carries the original signed double X/Y quantities, point/line unit,
protocol and phase fields, actual input generation and typed subscription identity
in the additive `LibreInputEvent.NativeScroll` envelope. `PointerScroll` is distinct
from the unchanged integer `PointerWheel`; it is never sampled as a drag event.
Original native pointer metadata and the old constructor/deconstruction remain.

## Actual source consumer

`ScrollableControl` with AutoScroll has an existing pixel-valued display rectangle
and per-axis source `SmallChange`. Native Lines multiply by that actual axis's
SmallChange, not by a fabricated 120-unit wheel constant. Native Points use the
same actual source coordinate-mode/DPI/framebuffer transform as native pointer
positions, without integer rounding until consumption. Both axes keep their sign;
no second user-inversion transform is applied.

This follows the AppKit `NSEvent.h` scrolling contract (local macOS SDK, comments
at lines 345–360): nonprecise scrolling deltas multiply by the view's line/row
height, precise deltas are points, and deltas already honor user inversion.
See Apple's [scrollingDeltaY](https://developer.apple.com/documentation/appkit/nsevent/scrollingdeltay)
and [hasPreciseScrollingDeltas](https://developer.apple.com/documentation/appkit/nsevent/hasprecisescrollingdeltas).
The source's existing WM_VSCROLL/WM_HSCROLL line operations use the same actual
SmallChange values. No arbitrary ScrollBar.Value-to-pixel mapping is inferred.

The original hit/capture route walks real control ancestors until one actual
AutoScroll consumer supports **all** requested nonzero axes. It validates both
numeric plans before moving either axis, calls the existing SetDisplayRectLocation
and SyncScrollbars implementation, and produces no synthetic MouseWheel event.
Unknown consumers/axis mappings throw explicitly; no vector component disappears.
Windows compilation and the legacy wheel path are unchanged.

Subpixel debt belongs to a weak exact source target, source/target handles,
typed subscription identity, provider generation, unit, captured scale and exact
display/line metric frame. Replacement or changed numeric frames start fresh.
Opposing motion cannot inherit old-direction debt; outward motion at an exhausted
edge cannot accumulate hidden debt. Cancel, focus/capture retirement and Leave
clear the old carry before public callbacks. The two-axis carry is published before
source callbacks and never rewritten after them. If a callback throws, only that
exact old carry is retired, preserving nested replacement input and the original
exception; source writes already performed are not rolled back.

Direct typed source packets validate original native X/Y, timestamp, modifier
bits and canonical modifier projection, as well as scroll fields, before changing
pointer/global source state. The adapter performs the corresponding checks before
mapping coordinates or flushing characters and retains its original delivery,
subscription and generation rechecks.

## Bounded source evidence

On macOS arm64, the canonical test project and backend test project compiled
against the unchanged checked-in ProGPU pin
`0d33ef68aaf9c9c58685f449e6ab386f5156fce5` managed source. The canonical dependency
build had 633 existing source/analyzer warnings and zero errors; the backend test
build had two existing style warnings and zero errors. No warnings were suppressed.
Focused runs, each with the original two-minute bound and skips rejected, passed:

- 19 new canonical native-scroll cases: real headless Form/Panel AutoScroll,
  independent axes, actual line metrics, fractions/direction/edges, unit/metric,
  generation/cancel/target replacement, raw metadata atomicity, old character tails,
  source callback failure/nested replacement, phased rejection and unchanged wheel.
- All 50 native adapter cases, including the original 44 controls.
- All 24 existing native-pointer drag-cancellation cases.
- All 24 existing canonical native-pointer/click cases.

The headless service and typed native test provider exercise source contracts;
they do not create an owned Cocoa panel, native input context, renderer or GPU.
No native product build, package staging, pin change, observer, VM or UI run occurred.

## Remaining admission

Nonzero phase or momentum values still fail before character/source delivery.
AppKit momentum has a target-latching contract distinct from ordinary hit testing;
normal/ended/cancelled leases, stale momentum tails, accepted work ownership and
source generation cancellation must connect before enabling phased input. This
phase-less foundation is not the full popup scrolling outcome.

Standalone ScrollBar, ToolStripDropDown and editor/list/grid native-scroll policies
remain explicit where their source units do not match AutoScroll's pixel contract.
Owned factory selection, automatic modality, application rendering and complete
native popup/desktop qualification stay gated. No opaque handle cast, keyboard
substitute, global input polling or provider retirement change is introduced.
