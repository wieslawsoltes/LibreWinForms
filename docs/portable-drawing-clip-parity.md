# Portable drawing clip integration

The source graph pins ProGPU `3dc02026028c4ba42ddeef9f2a4232f660c51a9e`,
retaining the earlier pipeline-startup changes and adding the captured drawing
clip coordinate-frame and exact atlas/device pixel-mapping fixes from ProGPU
#220. Native and managed paths retain the existing source shapes, clip frames,
curve/Boolean semantics, alpha and fractional-mapping behavior. Renderer,
compiler, adapter defaults and application deadlines are unchanged.

The whole upstream [Build 36436941579](https://github.com/wieslawsoltes/ProGPU/actions/runs/36436941579)
completed successfully: all 49 jobs, including both Windows drawing references
and the independent native package consumer groups. Artifact
`progpu-packages-linux-x64` (10979158407) is version `0.1.0-preview.3240.ci`;
its downloaded archive SHA-256 is
`633a24ca80693b49c306b675b1aef43f84faaa811a4588c1bf0dd09833f0bd90`.
The separately produced native package archive (10979671898) has SHA-256
`1642bb6b641477d7b9fa2ef74ebd7944ef5cee531c8fa18b698a3081aa952829`.
The package sets are kept separate; same-ID/version packages are not overwritten
or silently mixed between artifacts.

The existing LibreWinForms producer builds the pinned source graph and retains
all source, package, visible-application, SDK/analyzer, isolation and platform
gates. Its whole successful Build and exact package/application checks are still
required before qualification. An upstream successful Build permits staging;
it does not qualify a downstream source pin or all popup appearance.

## Windows popup comparison

The paired Windows run in `popup-qualified-clips.KeB5vjsy` completed all fourteen
input phases for both the Microsoft reference and portable application. Visual
inspection of the portable menu capture now shows the previously missing
`More` label. The tooltip capture retains the complete `Popup interaction tooltip`
text after the Forms tooltip-margin fix. The original application, input sequence,
60-second per-case deadline and renderer/compiler/adapter defaults were unchanged.

This diagnostic copied ten managed ProGPU DLLs from the successful producer's
verified packages into a private copy of the preceding tooltip-fixed candidate.
Each package's repository commit, ID/version, dependency graph and original
application/SDK/payload hashes were checked. The original `.deps.json` was not
rewritten; this is **not** qualification of a newly restored Forms package graph.
No native C++ runtime was inserted. Runner exit zero means phase capture completed;
the application exit code one records harness-owned termination, not normal exit.

The receipt records `diagnosticOnly=true`, `qualified=false` and
`payloadsUnchanged=true`. Driver SHA-256:
`7bdec05fab501b5588209e701c8eb3bf559cadff57f0ec4c1916f14e3b32460e`.
Retained original BMP hashes:

- `06-menu.bmp`: `bf1c3e3bc0527735ee46de00778a3d080e85c5c883562ef916c033013ab3f667`
- `14-tooltip.bmp`: `be0bc55731f67cc18a373455931f16c9a5a3c16aa05a56e740d0265179409aed`

This resolves the missing-label observation for this Windows candidate, not all
theme, placement, DPI or cross-platform popup parity. The source-pin producer
Build and complete package/application validation remain required.

ProGPU's three separate recursive SVG exception mismatches and WPF ARM64
pending-compute/resize failure remain independent release gates.
