# Portable TextBox pointer default ordering

Application action: click or drag within a plain TextBox, including an editor in
a dropdown, and inspect or replace its selection from an overridden `OnMouseDown`
or `OnMouseMove`, or from a public mouse handler. Previously portable TextBox
raised those notifications before applying its own caret mutation. Handlers saw
the previous selection, and their deliberate selection/capture changes could be
overwritten after returning.

The existing native source contract is in `Control.WmMouseDown` and
`Control.WmMouseMove`: when `ControlStyles.UserMouse` is absent, `DefWndProc` runs
before virtual mouse notification. Native EDIT therefore owns default selection
before application callbacks. This fix connects portable retained-editor defaults
at that same source-dispatch boundary. Moving code merely above `base.OnMouseDown`
inside TextBox would still run after a derived override and is not sufficient.

## Ownership and behavior

Control's narrow internal default-processing methods are no-ops for other
controls. Plain TextBox consumes its existing retained layout and source UTF-16
selection, with the original viewport, Shift anchor and read-only selection
semantics. The `UserMouse` style skips default processing. Protected `OnMouse*`
methods remain notifications; calling one is not equivalent to receiving input.

A typed, allocation-free dispatch context carries the exact receiving and target
handles and input, press and capture generations. Retained layout/text/selection
identity is checked around provider calls and callbacks. A canceled event,
recreated handle, replaced capture or nested press cannot resume the old default
or publish its old notification. Actual handler exceptions propagate; completed
default selection is not rolled back. Handlers may set a different selection or
release capture without a later editor default overwriting their choice.

Native click metadata and ordinary portable input use the same default boundary.
No timer, click-count approximation, wheel conversion or window factory is added.
This is source control flow with constant-size lifetime checks and the existing
retained hit-test cost, not a renderer, shaping or performance qualification.
Native Windows source behavior and unrelated controls remain unchanged.

## Focused coverage

Sixteen fresh-process facts use actual canonical controls, source input dispatch
and the existing ProGPU retained-layout adapter. They cover native and ordinary
down ordering, drag ordering, derived/public handlers, `UserMouse`, notification-
only calls, exceptions, Shift/read-only selection, and reentry through actual
layout creation, hit testing, disposal and selection callbacks. Provider probes wrap real layout operations;
they do not replace hit geometry with fabricated text positions.

Existing retained-editor pointer helpers now send real source input instead of
invoking protected notifications. Existing assertions and all previous suites
remain. Hosted CI requires all sixteen focused facts with no skips and the
existing two-minute bound; the complete canonical minimum rises from 869 to 885.
The process-isolation helper still requires exactly one fact per child, with its
original deadline. No local build, test, native window or VM run is claimed.

This does not implement double-click word selection, word-granularity dragging,
RichTextBox/MaskedTextBox editing, IME, legacy-wheel compatibility or native popup
factory admission. Those remaining contracts and final desktop application
qualification are not inferred from passing source tests.
