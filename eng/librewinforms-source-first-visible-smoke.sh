#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/eng/common/dotnet.sh"
package_source="${LIBREWINFORMS_SOURCE_FIRST_PACKAGE_SOURCE:-${repo_root}/artifacts/packages/source-first}"
smoke_source="${repo_root}/packaging/LibreWinForms.Sdk.SourceFirstVisibleSmoke"
smoke_root="$(mktemp -d -t librewinforms-source-first-visible.XXXXXXXX)"
smoke_project="${smoke_root}/LibreWinForms.Sdk.SourceFirstVisibleSmoke.csproj"
smoke_config="${smoke_root}/NuGet.config"

cleanup() {
  rm -rf "${smoke_root}"
}
trap cleanup EXIT

if [[ ! -f "${package_source}/LibreWinForms.Sdk.0.1.0-source-first-sdk.nupkg" ]]; then
  echo "Source-first SDK package was not found in ${package_source}." >&2
  exit 1
fi

cp "${smoke_source}/LibreWinForms.Sdk.SourceFirstVisibleSmoke.csproj" "${smoke_root}/"
cp "${smoke_source}/Program.cs" "${smoke_root}/"
cp "${repo_root}/NuGet.config" "${smoke_config}"
"${dotnet}" nuget add source "${package_source}" \
  --name LibreWinFormsSourceFirstVisible \
  --configfile "${smoke_config}"

NUGET_PACKAGES="${smoke_root}/packages" "${dotnet}" restore \
  "${smoke_project}" \
  --configfile "${smoke_config}" \
  --force \
  --no-cache
NUGET_PACKAGES="${smoke_root}/packages" "${dotnet}" build \
  "${smoke_project}" \
  --configuration Release \
  --no-restore

run_command=(
  "${dotnet}" run
  --project "${smoke_project}"
  --configuration Release
  --no-build
  --no-restore
)

if [[ "$(uname -s)" == "Linux" ]]; then
  if ! command -v xvfb-run >/dev/null 2>&1; then
    echo "xvfb-run is required for the Linux visible-window smoke." >&2
    exit 1
  fi
  xvfb-run -a "${run_command[@]}"
else
  "${run_command[@]}"
fi

echo "Source-first visible package smoke succeeded."
