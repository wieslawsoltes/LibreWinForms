# Shared native popup dependency

The ProGPU pin includes owned Cocoa popup surfaces, their explicit native input
provider, and neutral native pointer transport contracts. LibreWPF's canonical
Forms integration must use this exact same ProGPU commit as LibreWinForms; the
cross-repository identity check remains unchanged.

The shared revision also fixes System.Drawing clip queries whose intermediate
float inverse overflows although the final capture-to-current mapping is finite.
It preserves clip ownership and ordinary arithmetic, rejects unrepresentable
mappings, and matches Microsoft Windows x64/ARM64 rejection of the three recursive
SVG fixtures. This drawing repair does not enable native source input or change
the popup admission policy.

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
by this dependency update. The dependency PR requires its producer and integration
CI to pass; complete source admission and native UI qualification remain separate
feature/release work, not a claim made by a dependency-only merge.

Release/package staging still requires a complete successful Build for the exact
pinned ProGPU commit. Failed, canceled or incomplete producer runs cannot supply
qualified packages, even if individual artifact-producing jobs succeeded.
