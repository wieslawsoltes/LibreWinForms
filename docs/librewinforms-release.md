# LibreWinForms Release

LibreWinForms preview releases publish the portable WinForms package set:

- `LibreWinForms.Sdk`
- `LibreWinForms.System.Windows.Forms`
- `LibreWinForms.WindowsFormsIntegration`

Run the local package lane:

```bash
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.1 ./eng/librewinforms-pack.sh
```

Additional MSBuild properties can be passed after the script name, for example when validating the standalone clone against a local LibreWPF artifact root.

The package lane writes:

- `.nupkg` files for each public package.
- `librewinforms-preview-packages-<version>.json` with package sizes and SHA-256 hashes.
- `librewinforms-preview-<version>.tar.gz` plus `.sha256`.
- A bundle `README.md` and `NuGet.config`.

Before packing, the lane removes current-version package, manifest, bundle, checksum, README, and NuGet.config artifacts from the output directory. After packing, it fails if any expected package is missing or if any unexpected current-version package artifact is present.

The GitHub release workflow runs the same package lane and publishes to NuGet.org when `NUGET_API_KEY` is configured and the workflow is invoked with publishing enabled or a `librewinforms-v*` tag is pushed.
