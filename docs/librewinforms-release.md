# LibreWinForms Release

LibreWinForms preview releases publish the portable WinForms package set:

- `LibreWinForms.Sdk`
- `LibreWinForms.System.Windows.Forms`
- `LibreWinForms.WindowsFormsIntegration`

Run the local package lane:

```bash
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.33 ./eng/librewinforms-pack.sh
```

LibreWinForms depends on `LibreWPF.Transport` and `LibreWPF.ProGPU` from its matching LibreWPF release, plus `LibreWPF.Interop` and `ProGPU.System.Drawing.Common` from the immutable ProGPU version pinned by that release. CI and release workflows check out the immutable `librewpf-v<version>` tag, download its three published LibreWPF packages, verify their package identity/version/repository commit against that checkout, and pass the staged local feed through `LIBREWINFORMS_RESTORE_SOURCES`.

For local validation against unpublished bridge packages:

```bash
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.33 \
LIBREWINFORMS_BRIDGE_PACKAGE_VERSION=0.1.0-preview.33 \
LIBREWINFORMS_PROGPU_PACKAGE_VERSION=0.1.0-preview.29 \
LIBREWINFORMS_RESTORE_SOURCES=/path/to/wpf/artifacts/packages/Release/NonShipping%3Bhttps://api.nuget.org/v3/index.json \
./eng/librewinforms-pack.sh
```

Additional MSBuild properties can be passed after the script name, for example when validating the standalone clone against a local LibreWPF artifact root:

```bash
LIBREWINFORMS_BRIDGE_PACKAGE_VERSION=0.1.0-preview.33 \
LIBREWINFORMS_PROGPU_PACKAGE_VERSION=0.1.0-preview.29 \
LIBREWINFORMS_RESTORE_SOURCES=/path/to/wpf/artifacts/packages/Release/NonShipping%3Bhttps://api.nuget.org/v3/index.json \
./eng/librewinforms-pack.sh -p:LibreWpfManagedAssemblyRoot=/path/to/wpf/artifacts/bin/
```

The package lane writes:

- `.nupkg` files for each public package.
- `librewinforms-preview-packages-<version>.json` with package sizes and SHA-256 hashes.
- `librewinforms-preview-<version>.tar.gz` plus `.sha256`.
- A bundle `README.md` and `NuGet.config`.

Before packing, the lane removes current-version package, manifest, bundle, checksum, README, and NuGet.config artifacts from the output directory. After packing, it fails if any expected package is missing or if any unexpected current-version package artifact is present.

The package lane also uses an isolated NuGet cache at `artifacts/nuget/librewinforms-pack` by default and evicts the current LibreWinForms and bridge package versions before restore. Set `LIBREWINFORMS_NUGET_PACKAGES` when a different cache location is required. This keeps local, CI, and release runs from accidentally compiling against an older same-version `LibreWPF.Interop`, `LibreWPF.Transport`, or ProGPU bridge package from the user/global NuGet cache.

Release packing sets `LIBREWINFORMS_REQUIRE_CLEAN=1`, supplies the ProGPU strong-name key explicitly, and records the exact LibreWinForms, LibreWPF bridge, and ProGPU commits in the package manifest. The release workflow resolves an immutable `bridge_ref`; by default it uses `librewpf-v<bridge_version>`, while a manual rehearsal can pass an exact LibreWPF commit. This prevents a tag rerun from silently rebuilding against a later bridge branch tip.

Both CI and release run the standalone control/dispatcher/drag-drop/tree behavior executable and then restore a fresh package-only `LibreWPF.Sdk` consumer. The package smoke executes form, owned-dialog, designer, typed message-box, checkable-control, ListView, custom-paint, and retained owner-draw modes before artifacts can be published. Consuming the immutable LibreWPF release bundle keeps its Windows RID-specific PresentationCore payload intact; rebuilding the bridge on a macOS runner would not produce that Windows text runtime.

After NuGet indexing, dispatch `LibreWinForms Public Package Smoke` for the published version. It restores only from NuGet.org and builds the unchanged WinForms template shape (`LibreWinForms.Sdk`, `net10.0`, `UseWindowsForms`, `ApplicationConfiguration.Initialize`) on Ubuntu and macOS.

The GitHub release workflow runs the same bridge bootstrap and package lane, then publishes to NuGet.org when `NUGET_API_KEY` is configured and the workflow is invoked with publishing enabled or a `librewinforms-v*` tag is pushed. Tag-triggered releases create a GitHub prerelease through `gh release create --generate-notes`, attaching the NuGet packages, manifest, bundle, checksum, README, and local-feed `NuGet.config`.

Publish the immutable ProGPU package version and then the LibreWPF bridge packages that pin it before publishing LibreWinForms so downstream restores can resolve the dependency closure from NuGet.org. The active release branch is `librewinforms-progpu-port`; use `librewinforms-v<version>` tags for public preview releases from that branch.
