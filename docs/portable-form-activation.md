# Portable form activation ownership

The portable source uses the existing typed native `FocusGained` and `FocusLost`
events to update canonical `Form.Active`, including its validation/focus policy
and public `Activated` / `Deactivate` events. `Form.Activate()` requests native
activation; the request alone does not publish `Form.ActiveForm`.

A confirmed successor first takes ownership, then deactivates the previous
source. A delayed loss from the previous source cannot clear the successor or
its keyboard modifier state. Duplicate focus events do not repeat activation or
control focus notifications. Hide and handle destruction release activation,
including the source focus flag needed to admit a later confirmed reopening.

Callbacks may hide, dispose, or confirm another source reentrantly. Superseded
transitions cannot publish stale activation or focus notifications. A throwing
previous-owner deactivation callback remains an error; it does not leave an
unconfirmed successor published, or discard an independently confirmed owner.

`CanonicalFormActivationTests.cs` exercises real canonical forms and text boxes
through the typed headless native-event seam. The original eight cases failed
against main `66bf443275466a2303d60e1fa5e2daa4b7e7dce6`; the expanded matrix also
covers preselected children, callback exceptions, and late modifier-state loss.
The final complete lifecycle run passed 180/180 with no skips on macOS ARM64
using the source-built `net10.0` graph and the repository SDK's .NET 11 preview
runtime roll-forward policy. The source-first gate retains its existing tests
and raises its minimum from 166 to 180. Two existing fixtures now supply actual
typed activation confirmation for inferred modal ownership and expect the
canonical initial child focus before subsequent mouse input.

This is a source lifecycle prerequisite for popup ownership, not qualification
of native popup placement, capture, dismissal, or modal input on any platform.
It adds no MDI support, synthetic HWND activation messages, native component
manager calls, or assumed native activation success. Actual native application
acceptance remains separate. The intermittent original DataGridView Enter
failure is unresolved and is not attributed to this independent defect.
