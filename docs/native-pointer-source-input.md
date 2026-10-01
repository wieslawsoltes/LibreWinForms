# Provider-native pointer delivery into canonical Forms

The Forms backend creates input through ProGPU's `NativeWindowInput.CreateInput`
using the actual window object. An `INativePointerInputContext` supplies exactly
one subscribed stream; the same context's Silk mouse projection is not subscribed.
Ordinary Silk windows keep their existing mouse and legacy wheel behavior. Window
factory selection is unchanged: this prerequisite does not enable owned Cocoa
popup creation or its deferred owner-bound construction.

The native stream reaches the existing canonical pointer and application-local
drag paths. Move/Drag/Enter use ordinary source movement, Down/Up use the five
existing button identities, Leave retires only hover, and Cancel retires exact-source
capture/press/hover plus its owned drag registration. Cancellation commits that
drag's terminal state before character callbacks can reenter, then completes only
the captured old DragLeave target. Original callback failures remain primary.

The subscription retains the actual context and source-window identity, checks the
provider's current delivery scope, and captures its generation separately for each
packet. Character flushing and coordinate reads may retire or replace that packet;
the old tail cannot reach source delivery or drag sampling. A later live provider
generation can deliver policy cancellation and reopened-window input. Subscription
retirement precedes input disposal and remains committed if unsubscription throws.

Native double view-point coordinates map directly through the existing source
coordinate-mode/DPI policy, with one final integer rounding and no intermediate
`Vector2` narrowing. The additive, non-positional `LibreInputEvent.NativePointer`
retains original coordinates, provider-clock seconds, kind, button, click count and
all admitted native modifier flags. The original eight-argument constructor and
deconstruction remain unchanged. Canonical shortcut modifiers contain only their
existing four flags. The canonical source now consumes native counts through its
[source-owned click pairing](native-pointer-clicks.md); the backend retains raw
counts and does not synthesize duplicate down/up events.

Phase-less native Scroll now retains original point/line quantities in a separate
source event and reaches the actual AutoScroll consumer described in
[source native scrolling](native-scroll-source-input.md). It is neither swallowed
nor multiplied into invented wheel notches. Nonzero phases/momentum still reject
before source delivery; target ownership for those gestures remains required.
Malformed metadata and unrepresentable/unsupported button identities fail before
source delivery. Legacy MouseWheel is unchanged. Complete scroll consumer policy
and native qualification remain prerequisites to owned-popup factory admission.

A real keyboardless native popup has no GLFW character callback slots and retains
its actual Form's keyboard owner. Unknown non-GLFW keyboard providers still fail
closed. The existing GLFW full-scalar character ownership/ordering is unchanged.

## Authored coverage and limits

- 50 backend adapter cases cover original metadata, double precision, supported
  buttons, atomic rejection, explicit phased Scroll failure, callback/generation retirement,
  later-generation admission, character errors, cleanup, provider selection and ABI.
  The six additional cases retain phase-less vectors/units/stream generations and
  reject malformed scroll metadata before coordinate or character callbacks.
- Four additional cases compose the adapter with the actual drag service/router:
  terminal-before-character reentry, Leave versus Cancel, once-only old-target Leave
  and preserved primary/cleanup exceptions. The existing 20 cases remain (24 total).
- Three canonical source cases compose this same adapter with a real ContextMenuStrip,
  hosted Panel, capture and Form focus; they cover Leave/Cancel and callback disposal.
  The typed test provider is not an application factory or native input substitute.
- Twenty-one additional canonical cases connect provider counts to source click pairs,
  real DataGridView cell/header actions and hosted TextBox notifications, retaining
  style, handle, cancellation and callback ownership.

The unchanged full suites remain, with backend minimum 218 and canonical minimum
869. The native-pointer canonical selector requires all 24 cases, retains the
two-minute bound and rejects skipped tests.
The earlier pointer-only implementation was authored without local execution.
The subsequent bounded scroll work ran all 50 adapter, 24 drag-cancellation and
24 canonical pointer cases successfully, plus its 19 new source scroll cases;
see the linked scroll note for the exact source/dependency scope. Exact-head hosted
compilation/execution, original example/Showcase interaction, complete native scroll,
owned factory admission and platform desktop qualification remain required.
