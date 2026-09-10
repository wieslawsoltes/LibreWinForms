# LibreWinForms Release

LibreWinForms preview releases publish one canonical WinForms package set:

- `LibreWinForms.Sdk`
- `LibreWinForms.System.Windows.Forms`
- `LibreWinForms.ProGPU`
- `LibreWinForms.WindowsFormsIntegration`

The same bundle contains the exact ten-package ProGPU drawing closure built
from the pinned submodule. It never publishes
`LibreWinForms.Compatibility.System.Windows.Forms`.

## Canonical WFI source handoff

`WindowsFormsIntegration` belongs to LibreWPF, so qualify its real reference
and runtime source before packing LibreWinForms:

```bash
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.45 \
LIBREWINFORMS_PROGPU_PACKAGE_VERSION=0.1.0-preview.62 \
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
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.45 \
LIBREWINFORMS_PROGPU_PACKAGE_VERSION=0.1.0-preview.62 \
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

ProGPU owns drawing API compatibility, correctness, allocation, and focused
performance gates. LibreWinForms owns canonical package provenance and managed-contract equality and
WinForms lifecycle coverage; LibreWPF owns mixed WPF/WinForms hosting; and
SharpDevelop remains the real downstream integration driver.

## CI and publishing

`LibreWinForms Build` checks out canonical LibreWPF WFI source, runs the source
handoff, stages the immutable LibreWPF SDK feed, packs, smoke-tests, and uploads
the bundle. `LibreWinForms Release` accepts a `canonical_wfi_ref` plus the
LibreWPF SDK `bridge_ref`; release rehearsals should use exact commits, while
coordinated tags use `librewpf-v<version>`.

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
