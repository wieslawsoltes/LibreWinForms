# LibreWinForms Release

LibreWinForms preview releases publish one canonical WinForms package set:

- `LibreWinForms.Sdk`
- `LibreWinForms.System.Windows.Forms`
- `LibreWinForms.ProGPU`
- `LibreWinForms.WindowsFormsIntegration`
- `LibreWinForms.ApplicationIsolation`

The optional isolation launcher is included in the source-first package inventory,
manifest and release bundle. It is not yet published to NuGet; adding it to this
inventory does not itself publish a release or qualify visible application UI.

The same bundle contains the complete ProGPU drawing closure built
from the pinned submodule, including `ProGPU.Backend.Dawn` and
`ProGPU.Backend.Native`, plus the explicitly selected `ProGPU.Backend.Dx12`
compiler runtime from the same exact producer. It never publishes
`LibreWinForms.Compatibility.System.Windows.Forms`.

## Canonical WFI source handoff

`WindowsFormsIntegration` belongs to LibreWPF, so qualify its real reference
and runtime source before packing LibreWinForms:

```bash
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.70 \
LIBREWINFORMS_PROGPU_PACKAGE_VERSION=0.1.0-preview.70 \
LIBREWINFORMS_CANONICAL_WFI_SOURCE_ROOT=/path/to/LibreWPF \
LIBREWINFORMS_CANONICAL_WFI_EXPECTED_COMMIT=<librewpf-commit> \
./eng/librewinforms-build-canonical-wfi.sh
```

The helper requires an exact LibreWPF commit, supplies this LibreWinForms
checkout and its pinned ProGPU submodule through explicit source-root
contracts, builds both canonical WFI assemblies, and packages them with an
exact dependency on canonical Forms. Normal application development remains
NuGet-based; this source handoff is a release qualification path.

Pack the release from a separate canonical-WFI output directory:

```bash
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.70 \
LIBREWINFORMS_PROGPU_PACKAGE_VERSION=0.1.0-preview.70 \
LIBREWINFORMS_CANONICAL_WFI_PACKAGE_SOURCE=/path/to/LibreWPF/artifacts/packages/CanonicalWinForms \
LIBREWINFORMS_CANONICAL_WFI_COMMIT=<librewpf-commit> \
./eng/librewinforms-pack.sh
```

The packer targets canonical Forms and the backend at `net10.0`, which remains
consumable by later .NET TFMs and matches WFI's qualified target. It rejects a
WFI package missing `ref/net10.0` or `lib/net10.0`, a compatibility Forms
dependency, a different LibreWPF or LibreWinForms repository commit, a
different ProGPU drawing version, or a different generated managed contract.
The release output therefore cannot
contain two Forms identities.

## Package and provenance gates

The preview.70 source refresh pins ProGPU
`2236345c688a60d40e7a40c0ab02713b58833d4e`, the exact revision whose complete
[Build 38994881510](https://github.com/wieslawsoltes/ProGPU/actions/runs/38994881510)
passed all 59 jobs, with all 68 PR checks passing. One ARM64 drawings job required
a single bounded rerun after its SDK-installer PowerShell process crashed before
product compilation; the original failed log is retained.
This producer includes the earlier snapshot, chart-series, pipeline-cache and
Win32 button-packet fixes plus automatic ordered GPU queries for actual owned
D3D12/DXC devices. Both Windows architectures retain all 210 complete query
records and full result buffers against the independent original reference,
alongside the existing FXC controls. ProGPU #390 remains pending original WPF
modal/Gallery acceptance; these query results do not qualify desktop behavior.
Native staging selects this exact successful producer.

The canonical WFI lane uses LibreWPF `beef4c0b83571448fa5bf897fd66f68f3077fdb1`,
which passed all 16 jobs in
[Build 38938632765](https://github.com/wieslawsoltes/LibreWPF/actions/runs/38938632765)
and the original portable dispatcher package click test. Its tree matches merge
`8de5e06584e7672ed9ef8c31f2827d41f312f651`. The Forms package lane must qualify
this combined source graph before publication; upstream passes alone do not
qualify the new Forms packages or close the independent desktop issues.

The package lane writes:

- `.nupkg` files for each public package and the ProGPU drawing closure;
- the qualified `LibreWPF.Interop` and `ProGPU.DirectX` packages required by
  canonical WFI at the same pinned ProGPU source version;
- `librewinforms-preview-packages-<version>.json` with source commits, package
  sizes, and SHA-256 hashes;
- `librewinforms-preview-<version>.tar.gz` plus `.sha256`; and
- a bundle `README.md` and local-feed `NuGet.config`.

Before packing, the lane removes expected current-version artifacts. It then
fails on any missing package or unexpected current-version package artifact;
a stale compatibility package is an error, not something the release silently
deletes. The manifest records the exact LibreWinForms, canonical LibreWPF WFI,
and ProGPU commits.

The fresh-cache package smoke uses a matching `LibreWPF.Sdk` feed only for WPF
runtime/reference assets. It disables that SDK's automatic WinForms selection,
adds canonical Forms/backend/WFI explicitly, rejects the compatibility package
from `project.assets.json`, constructs a real `WindowsFormsHost`, and checks
all three runtime assembly identities. A second fresh-cache `LibreWinForms.Sdk`
consumer builds the unchanged `net11.0` template and verifies the exact ProGPU
drawing closure.

The SDK framework contract additionally builds minimal `net10.0` and `net11.0`
applications from the freshly produced SDK. It compares the SDK's declared
runtime TFM with actual Forms/backend assets, preserves explicit initialization
opt-outs, and inspects the Project reference graph's selected `NetCurrent`.
These authored controls are described in [SDK framework policy](sdk-runtime-framework.md);
their addition does not qualify a new package or change the pinned build SDK.

ProGPU owns drawing API compatibility, correctness, allocation, and focused
performance gates. LibreWinForms owns canonical package provenance and managed-contract equality and
WinForms lifecycle coverage; LibreWPF owns mixed WPF/WinForms hosting; and
SharpDevelop remains the real downstream integration driver.

## CI and publishing

Both package-producing jobs in `LibreWinForms Build` stage the pinned ProGPU
native payload before packing. The source-first job checks out only the existing
LibreWPF staging helper at immutable commit
`fd51401e94b3dbe1fec3d960626d8fe41f1adb4a`, verifies that helper checkout and the
actual ProGPU checkout against Forms' recorded gitlink, and invokes the helper
unchanged. Staging requires a complete successful `Build` for that exact ProGPU
commit, never just a successful native job or an artifact from another revision.
The destination is the Forms-owned `external/ProGPU/artifacts/progpu-native/package`,
which the native package project already consumes; no package runtime override,
duplicated runtime registry or synthetic native payload is introduced. The visible
matrix consumes the resulting package artifact and does not repack it.

`SourceFirstNativeStagingTests` executes the workflow's actual staging body with
recording-only local stubs, checking identity, ordering and failure propagation.
These offline controls do not download runtimes or qualify the producer Build,
package consumers or native UI.

`LibreWinForms Build` checks out canonical LibreWPF WFI source, runs the source
handoff, stages the immutable LibreWPF SDK feed, packs, smoke-tests, and uploads
the bundle. `LibreWinForms Release` accepts a `canonical_wfi_ref` plus the
LibreWPF SDK `bridge_ref`; release rehearsals should use exact commits, while
coordinated tags use `librewpf-v<version>`. An aligned tag also derives the
ProGPU drawing package version from its own version unless an explicit
`progpu_version` override is supplied. The canonical WFI and bridge refs both
default to the matching immutable LibreWPF tag; a source-branch default must
not silently enter a public tag release.

Release packaging uses the same Linux display dependencies and `xvfb-run -a`
as the successful Build package lane. SDK initialization reads the actual
monitor DPI, so canonical WFI, source-first packing and package smoke all run
with that display available; the release must not bypass initialization or
replace monitor state to make a headless job pass.

If a workflow-only correction is needed after a tag was created, dispatch the
corrected workflow with `source_tag=librewinforms-v<version>` and the matching
`version`. Checkout and package provenance remain at that existing tag; a
missing tag, different checkout or version mismatch fails before packaging.
Recovery also requires whole successful nine-job `LibreWinForms Build` runs
for both the original source commit and the corrected workflow commit before
any package upload or publication. The attached
`librewinforms-release-qualification.json` records both runs. All ordinary
source, package, native staging and size gates still execute. `publish=true`
publishes the result and creates the release at the original tag without moving
it. Offline identity controls do not qualify the recovery execution itself.

After NuGet indexing, dispatch `LibreWinForms Public Package Smoke`. It restores
only from NuGet.org and builds the unchanged `LibreWinForms.Sdk`, `net11.0`,
`UseWindowsForms`, and `ApplicationConfiguration.Initialize` shape on Ubuntu
and macOS.

The release workflow publishes to NuGet.org when `NUGET_API_KEY` is configured
and publishing is requested or a `librewinforms-v*` tag is pushed. Tag releases
create a GitHub prerelease with `gh release create --generate-notes`, attaching
the packages, manifest, bundle, checksum, README, and `NuGet.config`.

The active release branch is `librewinforms-progpu-port`; use
`librewinforms-v<version>` tags for public previews only after canonical WFI and
SharpDevelop qualification pass against the exact package closure.

The coordinated preview.67 native closure includes `ProGPU.Native.Sdk` alongside
`ProGPU.Backend.Native`. Keep their exact package versions and original static
archives, private dependency receipts, and notices together in the feed. The
release checks every package size before uploading any package; preview.66
stopped during ProGPU publication and its partial published history is retained.
