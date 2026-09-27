# Popup native geometry evidence

The optional portable popup observer records actual native geometry through
`SilkWindowService.TryGetNativeGeometrySnapshot`, on the existing source UI timer.
It never creates handles, changes focus, performs input or substitutes source
bounds for an unavailable native result. The main form and four public menu
popup objects are observable; the private native ComboBox/ToolTip surfaces are
not fabricated from their logical control handles.

Preparation opts in with `--native-geometry`. Both consumer projects retain the
same `Program.cs`; only Portable compiles the helper. Runtime collection also
requires `LIBREWINFORMS_POPUP_NATIVE_GEOMETRY=1`. The immutable sidecar is written
before the source snapshot with the same PID/sequence. Existing Windows and X11
consumers remain unchanged when the diagnostic is disabled.

`eng/librewinforms-popup-native-geometry.py` compares a source snapshot, its
matching native sidecar, and an independently captured CoreGraphics window list.
Every visible public window must have a unique source/native/content-view
identity, a same-PID independently correlated window number, and an exact native
frame match. Main-window title must match too. The AppKit number is not assumed
to be a global CoreGraphics identity without this correlation.

Native content and frame rectangles stay separate, in raw top-left desktop
points. The recorded source coordinate mode and actual DPI/framebuffer scales
apply the existing `LibreWindowCoordinates` policy, including midpoint rounding
away from zero. Native backing scale must independently equal framebuffer scale.
No ratio is fitted to source/native sizes; no titlebar height, source correction,
coordinate clamp or frame-as-client fallback is allowed. Wrong source geometry
therefore fails even when the independent frame matches.

The reader does not inject input or capture pixels. Its result remains
`qualified=false`; matching files alone cannot prove a currently live process,
fresh desktop state, source painting, interaction, or package provenance. Extra
same-PID native windows remain explicit unmatched inventory. A desktop driver
must still bind captures to its own live child, maintain the original deadline,
verify foreground/point ownership, and collect real interaction/pixel evidence.

The 25 offline reader cases pass, covering stale/malformed snapshots, independent
identity/frame mismatch, unavailable and logical handles, alias rejection,
explicit coordinate policies, nonfinite/overflowed values, bounded input and
unmatched native surfaces. Duplicate JSON identities, non-integer/out-of-range
wire identities and missing titles fail explicitly. The shared/X11 suites also
pass (17/22), including the optional observer preparation controls.
Observer, service and actual application/package qualification remain separate.

The actual shared Program and optional observer also compiled together on macOS
ARM64 against the real canonical Forms and newly source-built geometry service
assemblies, with zero warnings/errors. That source composition retained the
unaltered SDK-generated configuration/bootstrap and actual Forms analyzers.
It did not launch an application and does not qualify an installed package;
the independent Package build below must still pass.

The existing source-first pack-and-consume job now also invokes
`eng/librewinforms-popup-observer-package.py` unconditionally. It prepares the
unchanged shared `Program.cs` with the portable-only observer, restores a fresh
Package-mode cache from the exact newly produced archives, and builds only the
Portable project. MSBuild post-Build item queries must include the observer and
the actual SDK-generated `ApplicationConfiguration` and canonical bootstrap;
merely staging an optional partial method does not satisfy this check. Source
project references, changed shared bytes, wrong package versions, or restored
archive/output DLL hash mismatches fail the gate.

This additive consumer never starts an application, requests native geometry,
injects input, or captures the desktop. Its receipt always records
`guiExecuted=false` and `qualified=false`, even after compilation succeeds.
Existing SDK Project/Package smokes, desktop scenario, offline cases, and
deadlines remain unchanged. Each new restore/build command has a 300-second
bound. Fresh receipts and command logs are retained by an always-upload artifact
named `popup-observer-package-build`; failures are not retried.

The added offline verifier controls use synthetic files only. The ProGPU
native-geometry dependency is pinned to merged PR198 after its complete CI passed.
An actual successful Package build must now compile against the newly packed
dependency; absent APIs must produce compiler errors, not a stub, conditional CI
opt-out, or a native UI qualification claim.
