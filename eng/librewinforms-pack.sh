#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

package_output="${LIBREWINFORMS_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${LIBREWINFORMS_DEV_PACKAGE_VERSION:-0.1.0-preview.1}"
configuration="${LIBREWINFORMS_CONFIGURATION:-Release}"
restore_sources="${LIBREWINFORMS_RESTORE_SOURCES:-}"
mkdir -p "${package_output}"

pack_project() {
  local project="$1"
  local package_id="$2"
  rm -f \
    "${package_output}/${package_id}.${dev_package_version}.nupkg" \
    "${package_output}/${package_id}.${dev_package_version}.snupkg"

  local args=(
    pack "${repo_root}/${project}"
    -c "${configuration}"
    -o "${package_output}"
    -v:minimal
    -p:Version="${dev_package_version}"
    -p:PackageVersion="${dev_package_version}"
    -p:LibreWinFormsVersion="${dev_package_version}"
    -p:LibreWinFormsBridgePackageVersion="${LIBREWINFORMS_BRIDGE_PACKAGE_VERSION:-${dev_package_version}}"
  )

  if [[ -n "${restore_sources}" ]]; then
    args+=("-p:RestoreSources=${restore_sources}")
  fi

  "${dotnet}" "${args[@]}"
}

pack_project "src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj" "LibreWinForms.System.Windows.Forms"
pack_project "src/LibreWinForms.Portable/LibreWinForms.WindowsFormsIntegration/LibreWinForms.WindowsFormsIntegration.csproj" "LibreWinForms.WindowsFormsIntegration"
pack_project "src/LibreWinForms.Portable/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj" "LibreWinForms.Sdk"

"${repo_root}/eng/librewinforms-verify-docs.sh"
"${repo_root}/eng/librewinforms-preview-release-bundle.sh"

echo "LibreWinForms packages written to ${package_output}."
