#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_output="${LIBREWINFORMS_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
canonical_wfi_package_source="${LIBREWINFORMS_CANONICAL_WFI_PACKAGE_SOURCE:-}"
canonical_wfi_commit="${LIBREWINFORMS_CANONICAL_WFI_COMMIT:-}"
dev_package_version="${LIBREWINFORMS_DEV_PACKAGE_VERSION:-0.1.0-preview.45}"
progpu_package_version="${LIBREWINFORMS_PROGPU_PACKAGE_VERSION:-0.1.0-preview.62}"
configuration="${LIBREWINFORMS_CONFIGURATION:-Release}"
require_clean="${LIBREWINFORMS_REQUIRE_CLEAN:-0}"
source "${repo_root}/eng/librewinforms-package-list.sh"

mkdir -p "${package_output}"

if [[ "${require_clean}" == "1" && -n "$(git -C "${repo_root}" status --porcelain --untracked-files=normal)" ]]; then
  echo "LibreWinForms release packing requires a clean source tree." >&2
  exit 1
fi

clean_release_artifacts() {
  rm -f \
    "${package_output}/librewinforms-preview-packages-${dev_package_version}.json" \
    "${package_output}/librewinforms-preview-${dev_package_version}.tar.gz" \
    "${package_output}/librewinforms-preview-${dev_package_version}.tar.gz.sha256" \
    "${package_output}/README.md" \
    "${package_output}/NuGet.config"

  local package_id
  for package_id in "${librewinforms_preview_package_ids[@]}"; do
    rm -f \
      "${package_output}/${package_id}.${dev_package_version}.nupkg" \
      "${package_output}/${package_id}.${dev_package_version}.snupkg"
  done
  for package_id in "${librewinforms_preview_progpu_package_ids[@]}"; do
    rm -f \
      "${package_output}/${package_id}.${progpu_package_version}.nupkg" \
      "${package_output}/${package_id}.${progpu_package_version}.snupkg"
  done
  for package_id in "${librewinforms_preview_wfi_dependency_package_ids[@]}"; do
    rm -f \
      "${package_output}/${package_id}.${progpu_package_version}.nupkg" \
      "${package_output}/${package_id}.${progpu_package_version}.snupkg"
  done
}

is_expected_package_file() {
  local package_name
  package_name="$(basename "$1")"

  local package_id
  for package_id in "${librewinforms_preview_package_ids[@]}"; do
    if [[ "${package_name}" == "${package_id}.${dev_package_version}.nupkg" ||
          "${package_name}" == "${package_id}.${dev_package_version}.snupkg" ]]; then
      return 0
    fi
  done
  for package_id in "${librewinforms_preview_progpu_package_ids[@]}"; do
    if [[ "${package_name}" == "${package_id}.${progpu_package_version}.nupkg" ||
          "${package_name}" == "${package_id}.${progpu_package_version}.snupkg" ]]; then
      return 0
    fi
  done
  for package_id in "${librewinforms_preview_wfi_dependency_package_ids[@]}"; do
    if [[ "${package_name}" == "${package_id}.${progpu_package_version}.nupkg" ||
          "${package_name}" == "${package_id}.${progpu_package_version}.snupkg" ]]; then
      return 0
    fi
  done

  return 1
}

stage_canonical_wfi_package() {
  if [[ -z "${canonical_wfi_package_source}" ]]; then
    echo "Set LIBREWINFORMS_CANONICAL_WFI_PACKAGE_SOURCE to the output of the qualified LibreWPF canonical WFI source gate." >&2
    exit 1
  fi
  if [[ -z "${canonical_wfi_commit}" ]]; then
    echo "Set LIBREWINFORMS_CANONICAL_WFI_COMMIT to the exact LibreWPF source commit that produced canonical WFI." >&2
    exit 1
  fi
  if [[ "${canonical_wfi_package_source%/}" == "${package_output%/}" ]]; then
    echo "Canonical WFI source packages must be staged outside the LibreWinForms release output so their qualified Forms payload can be compared." >&2
    exit 1
  fi

  local wfi_package="${canonical_wfi_package_source}/LibreWinForms.WindowsFormsIntegration.${dev_package_version}.nupkg"
  local qualified_forms_package="${canonical_wfi_package_source}/LibreWinForms.System.Windows.Forms.${dev_package_version}.nupkg"
  local release_forms_package="${package_output}/LibreWinForms.System.Windows.Forms.${dev_package_version}.nupkg"
  local package_file
  local package_id
  for package_file in "${wfi_package}" "${qualified_forms_package}" "${release_forms_package}"; do
    if [[ ! -f "${package_file}" ]]; then
      echo "Canonical WFI handoff is missing ${package_file}." >&2
      exit 1
    fi
  done

  local wfi_entries
  wfi_entries="$(unzip -Z1 "${wfi_package}")"
  local expected_entry
  for expected_entry in \
    "lib/net10.0/WindowsFormsIntegration.dll" \
    "ref/net10.0/WindowsFormsIntegration.dll"; do
    if ! grep -Fxq "${expected_entry}" <<<"${wfi_entries}"; then
      echo "Canonical WFI package is missing ${expected_entry}." >&2
      exit 1
    fi
  done

  local wfi_nuspec
  wfi_nuspec="$(unzip -p "${wfi_package}" '*.nuspec')"
  if ! grep -Fq "dependency id=\"LibreWinForms.System.Windows.Forms\" version=\"${dev_package_version}\"" <<<"${wfi_nuspec}"; then
    echo "Canonical WFI package does not depend on the exact canonical Forms version ${dev_package_version}." >&2
    exit 1
  fi
  if grep -Fq "LibreWinForms.Compatibility.System.Windows.Forms" <<<"${wfi_nuspec}"; then
    echo "Canonical WFI package still depends on the retired compatibility Forms identity." >&2
    exit 1
  fi
  if ! grep -Fq "commit=\"${canonical_wfi_commit}\"" <<<"${wfi_nuspec}"; then
    echo "Canonical WFI package does not record expected LibreWPF commit ${canonical_wfi_commit}." >&2
    exit 1
  fi

  local qualified_forms_nuspec
  local release_forms_commit
  qualified_forms_nuspec="$(unzip -p "${qualified_forms_package}" '*.nuspec')"
  release_forms_commit="$(git -C "${repo_root}" rev-parse HEAD)"
  if ! grep -Fq "commit=\"${release_forms_commit}\"" <<<"${qualified_forms_nuspec}"; then
    echo "Canonical WFI was not qualified against LibreWinForms commit ${release_forms_commit}." >&2
    exit 1
  fi
  if ! grep -Fq "dependency id=\"ProGPU.System.Drawing.Common\" version=\"${progpu_package_version}\"" <<<"${qualified_forms_nuspec}"; then
    echo "Canonical WFI was not qualified against ProGPU drawing version ${progpu_package_version}." >&2
    exit 1
  fi

  # The upstream WinForms graph is not currently byte reproducible: independent
  # ContinuousIntegrationBuild invocations at the same commit can emit different
  # PE metadata while preserving the same managed contract. Compare the exact
  # source/dependency provenance above and the deterministic generated API
  # documentation below instead of treating incidental PE bytes as provenance.
  local contract_entry
  local qualified_contract_hash
  local release_contract_hash
  for contract_entry in \
    "lib/net10.0/System.Windows.Forms.xml" \
    "lib/net10.0/System.Windows.Forms.Design.xml" \
    "lib/net10.0/System.Windows.Forms.Primitives.xml" \
    "lib/net10.0/System.Private.Windows.Core.xml" \
    "lib/net10.0/LibreWinForms.Platform.xml"; do
    qualified_contract_hash="$(unzip -p "${qualified_forms_package}" "${contract_entry}" | sha256sum | cut -d' ' -f1)"
    release_contract_hash="$(unzip -p "${release_forms_package}" "${contract_entry}" | sha256sum | cut -d' ' -f1)"
    if [[ "${qualified_contract_hash}" != "${release_contract_hash}" ]]; then
      echo "Canonical WFI was qualified against a different managed contract at ${contract_entry}." >&2
      exit 1
    fi
  done

  local dependency_package
  for package_id in "${librewinforms_preview_wfi_dependency_package_ids[@]}"; do
    dependency_package="${canonical_wfi_package_source}/${package_id}.${progpu_package_version}.nupkg"
    if [[ ! -f "${dependency_package}" ]]; then
      echo "Canonical WFI handoff is missing ${dependency_package}." >&2
      exit 1
    fi
    cp "${dependency_package}" "${package_output}/${package_id}.${progpu_package_version}.nupkg"
  done
  cp "${wfi_package}" "${package_output}/LibreWinForms.WindowsFormsIntegration.${dev_package_version}.nupkg"
}

verify_package_outputs() {
  local package_id
  for package_id in "${librewinforms_preview_package_ids[@]}"; do
    if [[ ! -f "${package_output}/${package_id}.${dev_package_version}.nupkg" ]]; then
      echo "Missing package ${package_output}/${package_id}.${dev_package_version}.nupkg." >&2
      exit 1
    fi
  done
  for package_id in "${librewinforms_preview_progpu_package_ids[@]}"; do
    if [[ ! -f "${package_output}/${package_id}.${progpu_package_version}.nupkg" ]]; then
      echo "Missing package ${package_output}/${package_id}.${progpu_package_version}.nupkg." >&2
      exit 1
    fi
  done
  for package_id in "${librewinforms_preview_wfi_dependency_package_ids[@]}"; do
    if [[ ! -f "${package_output}/${package_id}.${progpu_package_version}.nupkg" ]]; then
      echo "Missing package ${package_output}/${package_id}.${progpu_package_version}.nupkg." >&2
      exit 1
    fi
  done

  local package_file
  shopt -s nullglob
  for package_file in \
    "${package_output}"/*."${dev_package_version}".nupkg \
    "${package_output}"/*."${dev_package_version}".snupkg \
    "${package_output}"/*."${progpu_package_version}".nupkg \
    "${package_output}"/*."${progpu_package_version}".snupkg; do
    if ! is_expected_package_file "${package_file}"; then
      echo "Unexpected current-version package artifact: ${package_file}." >&2
      exit 1
    fi
  done
  shopt -u nullglob
}

clean_release_artifacts

LIBREWINFORMS_CONFIGURATION="${configuration}" \
LIBREWINFORMS_SOURCE_FIRST_PACKAGE_VERSION="${dev_package_version}" \
LIBREWINFORMS_SOURCE_FIRST_SDK_PACKAGE_VERSION="${dev_package_version}" \
LIBREWINFORMS_SOURCE_FIRST_BACKEND_PACKAGE_VERSION="${dev_package_version}" \
LIBREWINFORMS_SOURCE_FIRST_PROGPU_PACKAGE_VERSION="${progpu_package_version}" \
LIBREWINFORMS_SOURCE_FIRST_PACKAGE_OUTPUT="${package_output}" \
  "${repo_root}/eng/librewinforms-pack-source-first.sh"

stage_canonical_wfi_package
verify_package_outputs
"${repo_root}/eng/librewinforms-verify-docs.sh"
"${repo_root}/eng/librewinforms-preview-release-bundle.sh"

echo "LibreWinForms canonical packages written to ${package_output}."
