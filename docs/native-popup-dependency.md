# Shared native popup dependency

The ProGPU pin includes owned Cocoa popup surfaces, their explicit native input
provider, and neutral native pointer transport contracts. LibreWPF's canonical
Forms integration must use this exact same ProGPU commit as LibreWinForms; the
cross-repository identity check remains unchanged.

The owned provider now explicitly tags AppKit scroll-phase semantics. Both source
hosts must carry that protocol with the original phase bits; the process OS does
not identify a packet's protocol. Existing constructor/deconstruction signatures
remain available. This dependency update does not yet interpret scroll phases or
admit the Forms source input provider.

Updating this dependency does not select the owned Cocoa factory in Forms.
Native window creation must still wait for the actual live typed owner when a
popup handle is created before ownership is assigned. Source button/capture,
keyboard and scroll delivery must consume the native provider before admission.
No automatic modality, source input parity or native UI qualification is claimed
by this dependency update. The dependent PR remains draft until its producer and
integration requirements are complete.

Release/package staging still requires a complete successful Build for the exact
pinned ProGPU commit. Failed, canceled or incomplete producer runs cannot supply
qualified packages, even if individual artifact-producing jobs succeeded.
