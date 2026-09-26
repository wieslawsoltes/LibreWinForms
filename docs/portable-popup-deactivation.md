# Portable dropdown owner deactivation

Portable canonical `ContextMenuStrip` and `MenuStrip` dropdown chains now retain
their typed containing `Form` and observe its confirmed `Deactivate` event.
Source ownership is captured before `Opening`; only an ownerless dropdown uses
the confirmed `Form.ActiveForm`. No native handle is reinterpreted as a form.

The native modal filter and the portable owner callback share the original
bounded active-dropdown close loop. Its initial count, current active entry,
`AutoClose` admission, `AppFocusChange` reason, and cancelable source lifecycle
remain authoritative. A canceled active leaf may receive another close attempt
within the original count. The portable path does not start the Win32 modal
filter or claim keyboard, capture, or outside-click parity from this change.

An opening transaction defers owner-loss dismissal until the entire nested
opening chain has completed. Separate membership and close-admission lists keep
persistent (`AutoClose=false`) children owned without admitting them to automatic
dismissal. A canceled or failed root opening can leave an actually visible child;
that child retains its owner until its own accepted close or disposal. The same
rule covers a child canceling an explicit parent close. Reopening replaces the
old owner subscription exactly once. Root disposal releases the complete chain.
Exceptions from user opening callbacks remain errors and are not replaced by
another closing callback during exception unwinding; visible members retain
their owner for subsequent deactivation.

Initial persistent-dropdown handle creation, live `AutoClose` changes, and
showing a top-level dropdown route through the existing typed portable window
`TopMost` operation. Current dropdown handles are logical controls, not native
popup windows: this prevents accidental USER32 calls or changing the owner's
topmost state, but does not implement native popup topmost. The native Win32 path
is unchanged. A failed hide retains ownership if the menu is still visible.

`CanonicalPopupActivationTests.cs` exercises actual canonical controls through
the typed headless input seam. Seven of the original eight cases failed before
the change: six missing owner-deactivation behaviors and one `USER32` call from
`AutoClose=false` on macOS. The expanded 30-case matrix includes nested callbacks,
cancellation, exceptions, persistent children, reopening, disposal, and initial
and live logical-handle safety. An intermediate native-topmost assertion exposed
the still-missing native popup window and is not claimed as fixed by this source
change. The source-first minimum is 210: the prerequisite's 180 cases plus all
30 popup cases, with no removed pre-existing cases or relaxed assertions.
The complete source lifecycle run passed 210/210 with zero skips on macOS ARM64
after a successful source-graph build (616 existing warnings, zero errors), using
the repository SDK's preview-runtime roll-forward policy and net10.0 target.

This is source lifecycle implementation, not actual Windows/Linux/macOS popup
placement, nonactivation, outside-click, keyboard, native modal-input, or pixel
qualification. Those native UX/UI checks remain tracked by ProGPU issue #197.
