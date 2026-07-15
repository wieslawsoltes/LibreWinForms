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
dev_package_version="${LIBREWINFORMS_DEV_PACKAGE_VERSION:-0.1.0-preview.15}"
bridge_package_version="${LIBREWINFORMS_BRIDGE_PACKAGE_VERSION:-${dev_package_version}}"
configuration="${LIBREWINFORMS_CONFIGURATION:-Release}"
restore_sources="${LIBREWINFORMS_RESTORE_SOURCES:-}"
nuget_packages="${LIBREWINFORMS_NUGET_PACKAGES:-${NUGET_PACKAGES:-${repo_root}/artifacts/nuget/librewinforms-pack}}"
strong_name_key_file="${LIBREWINFORMS_STRONG_NAME_KEY_FILE:-}"
require_clean="${LIBREWINFORMS_REQUIRE_CLEAN:-0}"
source "${repo_root}/eng/librewinforms-package-list.sh"
extra_pack_args=("$@")
mkdir -p "${package_output}"
export NUGET_PACKAGES="${nuget_packages}"

if [[ "${require_clean}" == "1" && -n "$(git -C "${repo_root}" status --porcelain --untracked-files=normal)" ]]; then
  echo "LibreWinForms release packing requires a clean source tree." >&2
  exit 1
fi

if [[ -n "${strong_name_key_file}" && ! -f "${strong_name_key_file}" ]]; then
  echo "LibreWinForms strong-name key was not found: ${strong_name_key_file}" >&2
  exit 1
fi

bridge_package_ids=(
  LibreWPF.Interop
  LibreWPF.ProGPU
  LibreWPF.Transport
  ProGPU.Backend
  ProGPU.Compute
  ProGPU.DirectX
  ProGPU.Scene
  ProGPU.SkiaSharp
  ProGPU.System.Drawing.Common
  ProGPU.Text
  ProGPU.Transpiler
  ProGPU.Vector
)

package_cache_id() {
  printf '%s' "$1" | tr '[:upper:]' '[:lower:]'
}

clean_restore_cache() {
  local package_id
  local package_cache_path

  for package_id in "${bridge_package_ids[@]}"; do
    package_cache_path="${NUGET_PACKAGES}/$(package_cache_id "${package_id}")/${bridge_package_version}"
    rm -rf "${package_cache_path}"
  done

  for package_id in "${librewinforms_preview_package_ids[@]}"; do
    package_cache_path="${NUGET_PACKAGES}/$(package_cache_id "${package_id}")/${dev_package_version}"
    rm -rf "${package_cache_path}"
  done
}

clean_release_artifacts() {
  rm -f \
    "${package_output}/librewinforms-preview-packages-${dev_package_version}.json" \
    "${package_output}/librewinforms-preview-${dev_package_version}.tar.gz" \
    "${package_output}/librewinforms-preview-${dev_package_version}.tar.gz.sha256" \
    "${package_output}/README.md" \
    "${package_output}/NuGet.config"

  for package_id in "${librewinforms_preview_package_ids[@]}"; do
    rm -f \
      "${package_output}/${package_id}.${dev_package_version}.nupkg" \
      "${package_output}/${package_id}.${dev_package_version}.snupkg"
  done
}

is_expected_package_file() {
  local package_file="$1"
  local package_name
  package_name="$(basename "${package_file}")"

  for package_id in "${librewinforms_preview_package_ids[@]}"; do
    if [[ "${package_name}" == "${package_id}.${dev_package_version}.nupkg" ||
          "${package_name}" == "${package_id}.${dev_package_version}.snupkg" ]]; then
      return 0
    fi
  done

  return 1
}

verify_package_outputs() {
  local package_id
  for package_id in "${librewinforms_preview_package_ids[@]}"; do
    if [[ ! -f "${package_output}/${package_id}.${dev_package_version}.nupkg" ]]; then
      echo "Missing package ${package_output}/${package_id}.${dev_package_version}.nupkg." >&2
      exit 1
    fi
  done

  local package_file
  shopt -s nullglob
  for package_file in "${package_output}"/*.${dev_package_version}.nupkg "${package_output}"/*.${dev_package_version}.snupkg; do
    if ! is_expected_package_file "${package_file}"; then
      echo "Unexpected current-version package artifact: ${package_file}." >&2
      exit 1
    fi
  done
  shopt -u nullglob
}

pack_project() {
  local project="$1"
  local package_id="$2"

  local args=(
    pack "${repo_root}/${project}"
    -c "${configuration}"
    -o "${package_output}"
    -v:minimal
    -p:Version="${dev_package_version}"
    -p:PackageVersion="${dev_package_version}"
    -p:LibreWinFormsVersion="${dev_package_version}"
    -p:LibreWinFormsBridgePackageVersion="${bridge_package_version}"
    -p:ContinuousIntegrationBuild=true
  )

  if [[ -n "${strong_name_key_file}" ]]; then
    args+=(
      -p:SignAssembly=true
      -p:AssemblyOriginatorKeyFile="${strong_name_key_file}"
    )
  fi

  if [[ -n "${restore_sources}" ]]; then
    args+=("-p:RestoreSources=${restore_sources}")
  fi

  if [[ "${#extra_pack_args[@]}" -gt 0 ]]; then
    args+=("${extra_pack_args[@]}")
  fi

  "${dotnet}" "${args[@]}"
}

clean_release_artifacts
clean_restore_cache

pack_project "src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj" "LibreWinForms.System.Windows.Forms"
pack_project "src/LibreWinForms.Portable/LibreWinForms.WindowsFormsIntegration/LibreWinForms.WindowsFormsIntegration.csproj" "LibreWinForms.WindowsFormsIntegration"
pack_project "src/LibreWinForms.Portable/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj" "LibreWinForms.Sdk"

verify_package_outputs
"${repo_root}/eng/librewinforms-verify-docs.sh"
"${repo_root}/eng/librewinforms-preview-release-bundle.sh"

echo "LibreWinForms packages written to ${package_output}."
