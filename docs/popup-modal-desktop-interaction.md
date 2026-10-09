# Real modal popup desktop evidence

The Windows, macOS and X11 desktop drivers accept an explicit `--modal` option.
It selects a separate modal scenario, not extra phases appended to the
ordinary fourteen-phase run. All existing 60-second process/driver deadlines,
per-image limits, 128 MiB process image budget, physical-input guards, geometry
assertions and PID ownership remain. No native input, build or validation has
been executed for this implementation checkpoint.

Use the same qualified package preparation and executable/helper arguments as
the ordinary driver, adding only `--modal`. The shared source is rehashed during
preparation; old prepared binaries fail the source identity gate. Platform
selection is explicit:

| Driver | Actual application options |
| --- | --- |
| Windows Microsoft and portable pair | `--modal-dialog` |
| macOS portable | `--modal-dialog --libre-native-modal-sessions` |
| Linux portable X11 | `--modal-dialog` |

No Cocoa switch is passed on Windows or Linux; invalid native-modal combinations
fail before process launch. The Microsoft process remains the original Windows
reference. No provider/default/pin change or automatic modality is introduced.

The runner clicks the observed owner button that calls real `ShowDialog(this)`.
It admits only the event/snapshot's exact `modal-<32 lowercase hex GUID>` direct
child directory. Directories and files must be regular/non-link, canonical and
bounded; Windows reparse points are rejected. The child must publish the same
PID, exact child title and sequence-matched immutable snapshot filename. Input
receipts retain the active observation path and the original snapshot hash.
This switches the existing session's observer, never its deadline or image budget.

The child scenario exercises context/menu cascades with separate Escape and
command paths, ComboBox Escape and committed selection, and a new actual tooltip
window. Each step observes the disabled owner's text, selection, popup state and
input/command counts remaining unchanged. Only the child's actual close button
finishes the dialog. The owner must return `OK`, regain its original observed
source active control/focus and input-enabled state, and accept a fresh editor
pointer action. Native child visibility must end; source events and the actual
return are retained before runner-owned process cleanup.

Public `Control.Enabled` is preserved as its original observation, **not** used
as native modal-blocking proof. Microsoft disables native windows separately
from that managed property. Windows inventory records actual `IsWindowEnabled`.
The portable source adds a read-only existing-`ILibreWindow.Enabled` observation,
checking exact handle/registry/platform identity before and after the getter.
macOS/Linux use that explicit source input policy; it is not a claim that an
ordinary Cocoa window is natively blocked. Unchanged owner event counts are
recorded independently. The modal-only owner now has a larger real source layout
and an `owner-input-guard` button below the smaller centered child. Ordinary
fourteen-phase source layout and input remain unchanged. Original modal phases
`m01` through `m15` retain their names/actions; two additions record
`m02-owner-blocked` and `m16-owner-guard` within the same deadline/image budget.

`blocked_owner_pointer(pid, owner_native_window_dict, target_rectangle)` is the
shared physical-input helper for both source hosts. It takes only a fresh exact
owner native record and observed source guard rectangle. Windows walks actual
native Z-order, because WindowFromPoint skips disabled windows; it does not use
that skip to accept a foreign destination. X11 uses actual root stacking and WM
frame/client ancestry. macOS uses current CG front-to-back inventory plus the
existing independently paired typed native content geometry/coordinate policy.
All reject an overlapping higher window, including another owned window, and
unknown/stale frames. Both pre-movement and pre-click proof are retained. No
window is moved, focused, enabled or hidden by this helper; no synthetic managed
notification is used. Physical modifier/button and foreground-PID checks remain.

The source modal input-policy check is a separate precondition, not proof of
delivery. The injected owner down/up pair must leave every owner guard event and
the retained owner input state unchanged through subsequent original modal
phases. After real dialog close and the unchanged owner-editor positive control,
the exact same guard receives real down, up and click once. Covered, missing or
changed geometry fails evidence instead of manufacturing a blocked click.
Receipts remain `qualified=false`; actual desktop execution is still pending.

macOS validates both independently paired source/native sidecars against one
current CG inventory. It rejects aliases across logical handles, native windows,
content views and CG numbers before merging the exact geometry. No source-bound
fallback, scale fitting, stale child sidecar, second event poll, title-derived
native identity or observer-driven UI mutation is permitted. The optional typed
geometry observer itself is unchanged. X11 retains actual PID/native client
enumeration and its existing compositor/Wayland limitations.

Offline controls are authored in the existing desktop/macOS harness test files
for final hosted execution. Original Windows/native pixels, real input delivery,
native modal release and whole application/package qualification remain pending.

The additional `eng/tests/test_modal_owner_input.py` authors nine offline controls:
native exposure, both owned/foreign obstruction, half-open edge contact with
full-target visibility, stale/unknown/aliased/nonfinite identity rejection,
disabled Win32 hit-test independence, late obstruction, held physical input,
unverified Cocoa identity and real X11 stacking order. Existing modal controls
retain all original fifteen named phases and separately require both additions.
No offline test, syntax check, application build or platform execution was run.
