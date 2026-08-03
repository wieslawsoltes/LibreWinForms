#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
bridge_checkout="${LIBREWINFORMS_BRIDGE_CHECKOUT:-${repo_root}/wpf-bridge}"
bridge_version="${LIBREWINFORMS_BRIDGE_PACKAGE_VERSION:-0.1.0-preview.41}"
bridge_ref="${LIBREWINFORMS_BRIDGE_REF:-librewpf-v${bridge_version}}"
package_output="${LIBREWINFORMS_BRIDGE_PACKAGE_OUTPUT:-${bridge_checkout}/artifacts/packages/Release/NonShipping}"
release_url="${LIBREWINFORMS_BRIDGE_RELEASE_URL:-https://github.com/wieslawsoltes/wpf/releases/download/librewpf-v${bridge_version}}"

if [[ ! -d "${bridge_checkout}/.git" && ! -f "${bridge_checkout}/.git" ]]; then
  echo "LibreWPF checkout was not found at ${bridge_checkout}." >&2
  exit 1
fi

bridge_commit="$(git -C "${bridge_checkout}" rev-parse HEAD)"
resolved_ref="$(git -C "${bridge_checkout}" rev-parse "${bridge_ref}^{commit}" 2>/dev/null || true)"
if [[ -z "${resolved_ref}" || "${resolved_ref}" != "${bridge_commit}" ]]; then
  echo "LibreWPF checkout ${bridge_commit} does not match ${bridge_ref} (${resolved_ref:-missing})." >&2
  exit 1
fi

package_ids=(
  LibreWPF.Transport
  LibreWPF.ProGPU
  LibreWPF.Sdk
)

mkdir -p "${package_output}"

for package_id in "${package_ids[@]}"; do
  package_file="${package_output}/${package_id}.${bridge_version}.nupkg"
  nuspec_file="${package_id}.nuspec"

  rm -f "${package_file}"
  curl --fail --location --retry 5 --retry-all-errors \
    --output "${package_file}" \
    "${release_url}/${package_id}.${bridge_version}.nupkg"

  nuspec="$(unzip -p "${package_file}" "${nuspec_file}")"
  if ! grep -qF "<id>${package_id}</id>" <<<"${nuspec}"; then
    echo "${package_file} does not declare package ID ${package_id}." >&2
    exit 1
  fi
  if ! grep -qF "<version>${bridge_version}</version>" <<<"${nuspec}"; then
    echo "${package_file} does not declare version ${bridge_version}." >&2
    exit 1
  fi
  if ! grep -qF "url=\"https://github.com/wieslawsoltes/wpf\" commit=\"${bridge_commit}\"" <<<"${nuspec}"; then
    echo "${package_file} does not record LibreWPF commit ${bridge_commit}." >&2
    exit 1
  fi
done

echo "Staged immutable LibreWPF ${bridge_version} packages from ${bridge_ref} at ${bridge_commit}."
