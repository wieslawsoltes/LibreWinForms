# Provider-native click pairs in canonical Forms

Acceptance actions: native double-clicks on the Forms example's DataGridView cells
and headers, and click notifications from an actual TextBox hosted in a dropdown.
This connects the existing native pointer adapter to source control behavior. It
does not select a native window factory or change ordinary Silk input.

## Source ownership and event order

The native view can contain several canonical controls. Its raw click count alone
does not prove that a second press belongs to the same control. The source retains
the exact source and target handles, target identity and button from an eligible
completed release. Consecutive positive provider counts continue a pair only while
those identities remain live. Each source-local pair uses counts one then two;
longer native sequences can start another pair without rewriting the raw metadata.
For example, A receiving native count one and B receiving counts two and three
produces B's own single then double click. Missing/reset counts or a changed target
start a new pair. Count zero is unclassified single input and cannot seed a pair.
No timer, distance threshold or platform preference is guessed in this layer.

The real source receives MouseDown with the selected count. On an eligible release,
Control retains its StandardClick and StandardDoubleClick policy: single Click and
MouseClick, or DoubleClick and MouseDoubleClick, precede MouseUp. Double-click
notification arguments have count two; MouseUp keeps the original count one.
StandardDoubleClick is sampled after MouseDown callbacks, as in the original
WndProc; a later style change does not reclassify the accepted press.
The native packet and its count remain available unchanged in LibreInputEvent.

The source retires old history before down callbacks and publishes replacement
history only after successful release callbacks and cleanup. Outside releases,
validation rejection, exceptions, cancellation and retired handles cannot seed a
new pair. A nested press owns its new capture/state; the old release cannot clear
it or finish delivering its old callback sequence. Leave continues to retire only
hover, while Cancel retires press/capture as before.

## Hosted editors

TextBoxBase deliberately disables standard click styles and emits its own click
notifications from virtual OnMouseUp. A native-only release scope supplies its
classification and eligibility to that existing handler. It is restored in finally
and does not use the native Windows double-click flag as portable retained state.
Current-release checks between public callbacks stop an obsolete tail. A native
release without an eligible press cannot fabricate editor click notifications.
Derived handlers, native Windows and nonnative source releases retain their own
paths. TextBox's notifications keep its canonical MouseUp argument count of one.

This is source notification delivery, not implementation of native EDIT word
selection, RichTextBox document selection or all multiple-click editing behavior.

## Coverage and remaining integration

Twenty-one authored source cases compose the actual NativePointerInput adapter and
typed provider with real controls. They cover all five buttons, event order,
standard styles, consecutive counts and target changes, Leave/Cancel, handle
replacement, callback failure/reentry, actual DataGridView cell/header callbacks,
hosted TextBox notifications and cancelled editor releases. The existing three
canonical native-pointer cases remain. The existing selector now requires all 24
without skips and with the same two-minute deadline; the full canonical minimum
is 869 and the backend minimum remains 218. No local build, test or native/VM
execution is claimed by these authored cases.

The ordinary pinned Silk provider reports its DoubleClick callback after MouseDown
returns. Exact down-time classification needs a provider seam; this connection
does not delay old mouse events, create another timing policy or claim that path
is fixed. See the pinned [Silk mouse implementation](https://github.com/dotnet/Silk.NET/blob/94605142f7b7bd6e69c9201e8e721d245c69eb7e/src/Input/Silk.NET.Input.Common/Internals/MouseImplementationBase.cs).
Native scroll/legacy MouseWheel policy, owned-popup factory admission and actual
application/desktop qualification on each supported platform remain required.
