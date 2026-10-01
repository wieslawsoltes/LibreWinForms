# Source-native popup menu scrolling

`ToolStripDropDownMenu` (including the actual canonical ContextMenuStrip) now consumes
the existing typed native scroll stream through Control's source-owned frame/plan
capability. This does not select an owned Cocoa factory or enable automatic modality.

## Original source units and bounds

The source menu's DisplayRectangle excludes its scroll-button bands. Its ordinary
button policy scans Available items and records the first item whose **top** is
inside that rectangle; line steps then use the difference to the adjacent original
Items entry. Adjacency does not filter unavailable entries. The native line plan
retains fractional **item counts**, executes whole steps and recomputes the actual
first visible top after each accepted step. Variable-height rows therefore do not
reuse the first row's height or a fixed SmallChange/font height/120-wheel constant.
Missing visible tops and nonprogressing source layouts remain explicitly unsupported;
the native path does not adopt the ordinary exceptional MenuHeight fallback.

Precise Points use the packet's captured source point scale and retain fractional
pixel motion separately from line fractions. Movement clamps against the actual
available content and display edges. The original status predicate tests
`DisplayRectangle.Contains(display.X, maxY)`, where maxY is the maximum item
**Bounds.Bottom**, not its last pixel. Equality at DisplayRectangle.Bottom is thus
still outside; the new integer point clamp permits maxY to reach Bottom minus one.
Top equality is inside. This is a source-derived precise-point policy, not a
pre-existing Windows or Cocoa precise-wheel pixel oracle. Ordinary whole item steps
retain their original overshoot and are not clipped to the point threshold.

The menu is vertical-only. Any nonzero X rejects the entire vector before Y moves.
No legacy MouseWheel event is synthesized, and ordinary ScrollInternal/button paths
keep their original behavior. The only shared arithmetic extraction there is the
unchanged adjacent Items index. Parent/capture/gesture routing is the existing
[native source scroll contract](native-scroll-source-input.md): normal packets
hit-test anew, momentum retains exact weak popup/consumer identities, and Cancel,
provider generations, source retirement and nested replacement remain authoritative.

## Exact layout and callback ownership

One immutable snapshot per actual menu layout generation owns a single array of
the original item identities, bounds, availability, margins and parents. Its size
is bounded by the existing Items count; ordinary scroll translation reuses it.
Only the menu and a synchronous executing plan hold that snapshot strongly. Retained
carry frames contain a shared weak snapshot reference plus numeric offset, never a
strong item-to-Owner path or an escaping pooled array. Layout/item membership and
availability invalidation happen at internal source boundaries before public
callbacks; same-count remove/reinsert cannot preserve the old generation.

The new guarded ToolStrip movement overload calls original SetItemLocation. After
every overridable SetBounds/OnBoundsChanged/LocationChanged callback it rechecks
the original dispatcher, source handles, exact collection/layout and expected
partial item positions. Close, cancel, reparent, relayout or nested input stops the
remaining old positions. Successful steps publish the matching offset/scroll amount
before subsequent callbacks. Button status writes have the same guard. Already
performed source positions are not rolled back.

SuspendLayout is paired with ResumeLayout(false) in finally. The original callback
exception remains primary if cleanup also fails; optional exception diagnostic
storage cannot itself replace that error. An incomplete/failed plan retires only
its exact published old carry, not a newer nested layout or fractional state.

## Bounded evidence and remaining gates

The actual Forms project compiled with zero errors (633 existing source/analyzer
warnings); the canonical test project compiled with zero warnings/errors, using the
unchanged pinned ProGPU managed source
`0d33ef68aaf9c9c58685f449e6ab386f5156fce5`. Dependencies were reused after one serialized
initial graph build; later builds used BuildProjectReferences=false. No native/GPU
build, package staging, submodule pin, VM, observer or application UI run occurred.

Fifteen new headless cases exercise real ContextMenuStrip source input with unequal
item heights: points, item-count fractions, recomputed line steps, half-open edges,
whole-axis rejection, momentum/Leave/Cancel, close/cancel during the first item write,
nested replacement with and without the original error, changed item bounds,
same-count membership replacement, unavailable adjacent stored bounds, missing-top
line rejection and generation/unit debt. All pass with no skips and the original
two-minute bound. The original 41 native-scroll, 24 native-pointer/click and 19
context-pointer cases also pass.

An extra existing eight-case hover batch initially failed its live-context-menu
selection assertion; one controlled unchanged rerun reproduced it after verifying
identical product/copied-test DLL hashes. The same case passed isolated and the
frozen predecessor assembly passed all eight. A deterministic persistent-popup →
live-popup sequence then proved the cause: source pointer position before Show and
after the requested hover was identical, `{X=147,Y=113}`. Original ToolStrip
ShouldSelectItem correctly suppresses that stationary-at-Show move. The separate
fixture correction sends actual input through the owner's current client rectangle
before Show, keeps the original assertion/timer/deadline, and retains the causal
two-test sequence as a regression. It does not reset static cursor state, call
OnMouseMove directly or change product hover policy.
All nine corrected hover cases pass, with no skips under the unchanged bound.

These source cases do not qualify native popup input/presentation, menu pixels,
physical-device scrolling, arbitrary custom menu layouts, non-menu ToolStripDropDown
consumers or automatic factory/modality policy. Those original gates remain open.
