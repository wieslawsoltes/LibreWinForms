# LibreWinForms Release

LibreWinForms preview releases publish the portable WinForms package set:

- `LibreWinForms.Sdk`
- `LibreWinForms.System.Windows.Forms`
- `LibreWinForms.WindowsFormsIntegration`

Run the local package lane:

```bash
LIBREWINFORMS_DEV_PACKAGE_VERSION=0.1.0-preview.1 ./eng/librewinforms-pack.sh
```

The package lane writes:

- `.nupkg` files for each public package.
- `librewinforms-preview-packages-<version>.json` with package sizes and SHA-256 hashes.
- `librewinforms-preview-<version>.tar.gz` plus `.sha256`.
- A bundle `README.md` and `NuGet.config`.

The GitHub release workflow runs the same package lane and publishes to NuGet.org when `NUGET_API_KEY` is configured and the workflow is invoked with publishing enabled or a `librewinforms-v*` tag is pushed.
