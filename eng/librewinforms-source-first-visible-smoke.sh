#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${LIBREWINFORMS_VISIBLE_DOTNET:-}"
if [[ -z "${dotnet}" ]]; then
  if [[ -x "${repo_root}/.dotnet/dotnet" ]]; then
    dotnet="${repo_root}/.dotnet/dotnet"
  else
    dotnet="dotnet"
  fi
fi
package_source="${LIBREWINFORMS_SOURCE_FIRST_PACKAGE_SOURCE:-${repo_root}/artifacts/packages/source-first}"
package_version="${LIBREWINFORMS_SOURCE_FIRST_PACKAGE_VERSION:-0.1.0-source-first}"
sdk_package_version="${LIBREWINFORMS_SOURCE_FIRST_SDK_PACKAGE_VERSION:-${package_version}}"
backend_package_version="${LIBREWINFORMS_SOURCE_FIRST_BACKEND_PACKAGE_VERSION:-${package_version}}"
smoke_source="${repo_root}/packaging/LibreWinForms.Sdk.SourceFirstVisibleSmoke"
smoke_root="$(mktemp -d -t librewinforms-source-first-visible.XXXXXXXX)"
smoke_project="${smoke_root}/LibreWinForms.Sdk.SourceFirstVisibleSmoke.csproj"
smoke_config="${smoke_root}/NuGet.config"
smoke_packages="${smoke_root}/packages"

cleanup() {
  rm -rf "${smoke_root}"
}
trap cleanup EXIT

if [[ ! -f "${package_source}/LibreWinForms.Sdk.${sdk_package_version}.nupkg" ]]; then
  echo "Source-first SDK package was not found in ${package_source}." >&2
  exit 1
fi

sed \
  -e "s#LibreWinForms.Sdk/0.1.0-source-first-sdk#LibreWinForms.Sdk/${sdk_package_version}#" \
  -e "s#<LibreWinFormsCanonicalPackageVersion>0.1.0-source-first</LibreWinFormsCanonicalPackageVersion>#<LibreWinFormsCanonicalPackageVersion>${package_version}</LibreWinFormsCanonicalPackageVersion>#" \
  -e "s#<LibreWinFormsProGpuBackendPackageVersion>0.1.0-source-first-backend</LibreWinFormsProGpuBackendPackageVersion>#<LibreWinFormsProGpuBackendPackageVersion>${backend_package_version}</LibreWinFormsProGpuBackendPackageVersion>#" \
  "${smoke_source}/LibreWinForms.Sdk.SourceFirstVisibleSmoke.csproj" \
  >"${smoke_project}"
cp "${smoke_source}/Program.cs" "${smoke_root}/"
cp "${repo_root}/NuGet.config" "${smoke_config}"

dotnet_package_source="${package_source}"
dotnet_smoke_project="${smoke_project}"
dotnet_smoke_config="${smoke_config}"
dotnet_smoke_packages="${smoke_packages}"
case "$(uname -s)" in
  MINGW*|MSYS*)
    dotnet_package_source="$(cygpath -w "${package_source}")"
    dotnet_smoke_project="$(cygpath -w "${smoke_project}")"
    dotnet_smoke_config="$(cygpath -w "${smoke_config}")"
    dotnet_smoke_packages="$(cygpath -w "${smoke_packages}")"
    ;;
esac

"${dotnet}" nuget add source "${dotnet_package_source}" \
  --name LibreWinFormsSourceFirstVisible \
  --configfile "${dotnet_smoke_config}"

NUGET_PACKAGES="${dotnet_smoke_packages}" "${dotnet}" restore \
  "${dotnet_smoke_project}" \
  --configfile "${dotnet_smoke_config}" \
  --force \
  --no-cache
NUGET_PACKAGES="${dotnet_smoke_packages}" "${dotnet}" build \
  "${dotnet_smoke_project}" \
  --configuration Release \
  --no-restore

run_command=(
  "${dotnet}" run
  --project "${dotnet_smoke_project}"
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
