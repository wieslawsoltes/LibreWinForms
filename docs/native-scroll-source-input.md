# Native scroll quantities in canonical Forms

The native adapter previously rejected every Scroll packet. The native source
path now carries the original signed double X/Y quantities, point/line unit,
protocol and phase fields, actual input generation and typed subscription identity
in the additive `LibreInputEvent.NativeScroll` envelope. `PointerScroll` is distinct
from the unchanged integer `PointerWheel`; it is never sampled as a drag event.
Original native pointer metadata and the old constructor/deconstruction remain.

## Actual source consumer

The shared dispatcher now asks the actual Control for an internal source-owned
snapshot and guarded write plan. The frame defines exact carry equality and unit
arithmetic; the plan publishes its expected frame/tails before callbacks and reports
incomplete writes so only its old carry is retired. Consumer identities remain weak
Control references. AutoScroll keeps its original numeric policy below; the actual
popup menu consumer is documented in [native menu scrolling](native-menu-scroll.md).

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

The original source hit-test route walks real control ancestors until one actual
AutoScroll consumer supports **all** requested nonzero axes. It validates both
numeric plans before moving either axis, calls the existing SetDisplayRectLocation
and SyncScrollbars implementation, and produces no synthetic MouseWheel event.
Unknown consumers/axis mappings throw explicitly; no vector component disappears.
Windows compilation and the legacy wheel path are unchanged.

Subpixel debt belongs to a weak exact source target, source/target handles,
typed subscription identity, provider generation, unit, captured scale and exact
display/line metric frame. Replacement or changed numeric frames start fresh.
Opposing motion cannot inherit old-direction debt; outward motion at an exhausted
edge cannot accumulate hidden debt. Cancel and focus/capture retirement clear
the old carry before public callbacks. Leave retires direct-scroll carry but does
not cancel momentum, which AppKit delivers to the original view. The two-axis carry is published before
source callbacks and never rewritten after them. If a callback throws, only that
exact old carry is retired, preserving nested replacement input and the original
exception; source writes already performed are not rolled back.

Direct typed source packets validate original native X/Y, timestamp, modifier
bits and canonical modifier projection, as well as scroll fields, before changing
pointer/global source state. The adapter performs the corresponding checks before
mapping coordinates or flushing characters and retains its original delivery,
subscription and generation rechecks.

## Gesture ownership

The same platform metadata validator is used by the backend and direct source
entrypoint. Untagged input admits only zero phases. Explicit AppKit input admits
the SDK's individual normal phase values 0, 1, 2, 4, 8, 16 and 32 (None, Began,
Stationary, Changed, Ended, Cancelled, MayBegin); momentum admits the same values
except MayBegin. Unknown bits, combined values and simultaneous normal/momentum
are rejected before source mutation, mapping or character callbacks.

Normal scroll input selects the current real source hit target for each packet,
independently of mouse-button capture. Momentum Began pins weak exact source
target and selected AutoScroll consumer identities/handles. Further momentum never
hit-tests or walks to a replacement consumer. Detached, disabled, disposed,
recreated or provider-generation-retired targets consume stale tails without moving
another control. The native packet's actual pointer position/modifiers stay intact;
pointer movement and Leave do not rewrite the momentum target.

This is Apple's documented [normal versus momentum routing contract](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/EventOverview/HandlingTouchEvents/HandlingTouchEvents.html).
The installed `NSEvent.h` provides exact phase values, not copied implementation.
The LibreWPF gesture-lease documentation informed ownership distinctions; its
deferred ScrollViewer queue implementation is not copied into synchronous Forms.

A source-owned gesture binds its original stream, generation, protocol and source
handle independently of a pointer dispatch revision. Cancel retires the gesture
and fractions before callbacks and never executes its vector. Direct End marks
that lifecycle ended before callbacks, applies its final accepted delta, and
retains compatible fractions for an explicit momentum Began handoff. Momentum End
applies its final delta and retires only that exact old gesture. New direct scroll
interrupts momentum. A stale momentum Changed/Ended/Cancelled packet cannot
consume or erase the replacement direct gesture's fraction. The handoff shares
fraction identity, not gesture cancellation identity; no deferred work queue exists.

Every callback tail retains the original input revision and exact gesture. An old
End or callback failure cannot retire a newer nested gesture or newer carry, even
when nested direct input reuses the same live gesture. Source writes already
performed remain performed; this is lifetime control, not transactional rollback.

## Bounded source evidence

On macOS arm64, the canonical test project and backend test project compiled
against the unchanged checked-in ProGPU pin
`0d33ef68aaf9c9c58685f449e6ab386f5156fce5` managed source. The canonical dependency
build reported 634 existing source/analyzer warnings and zero errors; the backend test
build had two existing style warnings and zero errors. No warnings were suppressed.
Focused runs, each with the original two-minute bound and skips rejected, passed:

- 41 new canonical native-scroll cases: real headless Form/Panel AutoScroll,
  independent axes, actual line metrics, fractions/direction/edges, unit/metric,
  generation/cancel/target replacement, raw metadata atomicity, old character tails,
  source callback failure/nested replacement, untagged rejection and unchanged wheel;
  normal retargeting versus momentum pinning, button capture independence, Leave,
  phase cancellation/End, handoff fractions, target retirement, invalid phases,
  nested old-End success/failure and replacement direct gestures.
- All 67 native adapter cases, retaining the original 44 cases (the two unsupported
  phase controls now explicitly exercise the still-unadmitted untagged protocol).
- All 24 existing native-pointer drag-cancellation cases and one native-scroll
  nonsampling control, 25 total.
- All 24 existing canonical native-pointer/click cases.

The headless service and typed native test provider exercise source contracts;
they do not create an owned Cocoa panel, native input context, renderer or GPU.
No native product build, package staging, pin change, observer, VM or UI run occurred.

## Remaining admission

Standalone ScrollBar, non-menu ToolStripDropDown and editor/list/grid native-scroll policies
remain explicit where their source units do not match AutoScroll's pixel contract.
Owned factory selection, automatic modality, application rendering and complete
native popup/desktop qualification stay gated. No opaque handle cast, keyboard
substitute, global input polling or provider retirement change is introduced.
