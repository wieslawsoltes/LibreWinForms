# Real modal popup desktop evidence

The Windows, macOS and X11 desktop drivers accept an explicit `--modal` option.
It selects a separate fifteen-phase scenario, not extra phases appended to the
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
recorded independently. Overlapping owner/child rectangles are never reinterpreted
as a successful blocked-owner pointer test; this scenario makes no such claim.

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
