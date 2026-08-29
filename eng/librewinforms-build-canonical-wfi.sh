#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
librewpf_root="${LIBREWINFORMS_CANONICAL_WFI_SOURCE_ROOT:-}"
expected_librewpf_commit="${LIBREWINFORMS_CANONICAL_WFI_EXPECTED_COMMIT:-}"
package_output="${LIBREWINFORMS_CANONICAL_WFI_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/CanonicalWfiSource}"
package_version="${LIBREWINFORMS_DEV_PACKAGE_VERSION:-0.1.0-preview.45}"
progpu_package_version="${LIBREWINFORMS_PROGPU_PACKAGE_VERSION:-0.1.0-preview.62}"

if [[ -z "${librewpf_root}" || ! -f "${librewpf_root}/eng/progpu-wpf-canonical-winforms-integration.sh" ]]; then
  echo "Set LIBREWINFORMS_CANONICAL_WFI_SOURCE_ROOT to a LibreWPF checkout containing the canonical WFI source gate." >&2
  exit 1
fi

if [[ -z "${expected_librewpf_commit}" ]]; then
  echo "Set LIBREWINFORMS_CANONICAL_WFI_EXPECTED_COMMIT to the immutable LibreWPF source commit being qualified." >&2
  exit 1
fi

librewpf_commit="$(git -C "${librewpf_root}" rev-parse HEAD)"
if [[ "${librewpf_commit}" != "${expected_librewpf_commit}" ]]; then
  echo "Canonical WFI source checkout ${librewpf_commit} does not match expected LibreWPF commit ${expected_librewpf_commit}." >&2
  exit 1
fi

librewinforms_commit="$(git -C "${repo_root}" rev-parse HEAD)"
progpu_commit="$(git -C "${repo_root}/external/ProGPU" rev-parse HEAD)"
if [[ "$(git -C "${repo_root}" ls-tree HEAD external/ProGPU | awk '{ print $3 }')" != "${progpu_commit}" ]]; then
  echo "Initialize external/ProGPU at the LibreWinForms gitlink before building canonical WFI." >&2
  exit 1
fi

PROGPU_WPF_CANONICAL_LIBREWINFORMS_ROOT="${repo_root}" \
PROGPU_WPF_CANONICAL_PROGPU_ROOT="${repo_root}/external/ProGPU" \
PROGPU_WPF_CANONICAL_EXPECTED_LIBREWINFORMS_COMMIT="${librewinforms_commit}" \
PROGPU_WPF_CANONICAL_WINFORMS_PACKAGE_OUTPUT="${package_output}" \
PROGPU_WPF_CANONICAL_WINFORMS_PACKAGE_VERSION="${package_version}" \
PROGPU_WPF_CANONICAL_PROGPU_PACKAGE_VERSION="${progpu_package_version}" \
PROGPU_WPF_RUN_DRAWING_QUALITY_GATES="${LIBREWINFORMS_CANONICAL_WFI_RUN_DRAWING_QUALITY_GATES:-0}" \
  "${librewpf_root}/eng/progpu-wpf-canonical-winforms-integration.sh"

wfi_package="${package_output}/LibreWinForms.WindowsFormsIntegration.${package_version}.nupkg"
if [[ ! -f "${wfi_package}" ]]; then
  echo "Canonical LibreWPF source gate did not produce ${wfi_package}." >&2
  exit 1
fi

echo "Canonical WFI source package qualified at LibreWPF ${librewpf_commit}, LibreWinForms ${librewinforms_commit}, and ProGPU ${progpu_commit}."
