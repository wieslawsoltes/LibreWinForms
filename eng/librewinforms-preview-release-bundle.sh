#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_output="${LIBREWINFORMS_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${LIBREWINFORMS_DEV_PACKAGE_VERSION:-0.1.0-preview.45}"
sdk_sample_target_framework="${LIBREWINFORMS_SDK_SAMPLE_TARGET_FRAMEWORK:-net10.0}"
manifest_path="${LIBREWINFORMS_PREVIEW_PACKAGE_MANIFEST:-${package_output}/librewinforms-preview-packages-${dev_package_version}.json}"
bundle_output="${LIBREWINFORMS_PREVIEW_RELEASE_BUNDLE:-${package_output}/librewinforms-preview-${dev_package_version}.tar.gz}"
sidecar_output="${LIBREWINFORMS_PREVIEW_RELEASE_BUNDLE_SHA256:-${bundle_output}.sha256}"
release_readme_path="${LIBREWINFORMS_PREVIEW_RELEASE_README:-${package_output}/README.md}"
release_nuget_config_path="${LIBREWINFORMS_PREVIEW_RELEASE_NUGET_CONFIG:-${package_output}/NuGet.config}"
source "${repo_root}/eng/librewinforms-package-list.sh"

file_sha256() {
  local file="$1"
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "${file}" | awk '{print $1}'
  else
    sha256sum "${file}" | awk '{print $1}'
  fi
}

"${repo_root}/eng/librewinforms-preview-package-manifest.sh"

mkdir -p "$(dirname "${bundle_output}")" "$(dirname "${sidecar_output}")" "$(dirname "${release_readme_path}")" "$(dirname "${release_nuget_config_path}")"
rm -f "${bundle_output}" "${sidecar_output}"

cat >"${release_readme_path}" <<README
# LibreWinForms Preview ${dev_package_version}

This preview bundle contains the package set for running WinForms-shaped applications through LibreWinForms packages.

## Contents

- \`librewinforms-preview-packages-${dev_package_version}.json\` records the exact package list, source commit, package sizes, and SHA-256 hashes.
- \`LibreWinForms.Sdk.${dev_package_version}.nupkg\` is the custom MSBuild SDK package.
- \`LibreWinForms.System.Windows.Forms.${dev_package_version}.nupkg\` provides the portable System.Windows.Forms API surface.
- \`LibreWinForms.WindowsFormsIntegration.${dev_package_version}.nupkg\` provides the portable WindowsFormsIntegration bridge used by LibreWPF-hosted WinForms content.

Verify the archive with the adjacent checksum file:

\`\`\`bash
shasum -a 256 -c librewinforms-preview-${dev_package_version}.tar.gz.sha256
\`\`\`

Use the extracted directory as a local NuGet source, or copy the bundled \`NuGet.config\` next to your solution. Then switch a project to the custom SDK:

\`\`\`xml
<Project Sdk="LibreWinForms.Sdk/${dev_package_version}">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>${sdk_sample_target_framework}</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
\`\`\`
README

cat >"${release_nuget_config_path}" <<NUGET
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="librewinforms-preview" value="." />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
NUGET

archive_entries=(
  "$(basename "${release_readme_path}")"
  "$(basename "${release_nuget_config_path}")"
  "$(basename "${manifest_path}")"
)

for package_id in "${librewinforms_preview_package_ids[@]}"; do
  package_name="${package_id}.${dev_package_version}.nupkg"
  package_file="${package_output}/${package_name}"
  if [[ ! -f "${package_file}" ]]; then
    echo "Missing package ${package_file}." >&2
    exit 1
  fi
  archive_entries+=("${package_name}")
done

(
  cd "${package_output}"
  COPYFILE_DISABLE=1 tar -czf "${bundle_output}" "${archive_entries[@]}"
)

expected_entries="$(printf '%s\n' "${archive_entries[@]}")"
actual_entries="$(tar -tzf "${bundle_output}")"
if [[ "${actual_entries}" != "${expected_entries}" ]]; then
  echo "Preview release bundle entries do not match the expected manifest/package set." >&2
  exit 1
fi

bundle_sha256="$(file_sha256 "${bundle_output}")"
printf '%s  %s\n' "${bundle_sha256}" "$(basename "${bundle_output}")" >"${sidecar_output}"

echo "LibreWinForms preview release bundle written to ${bundle_output}."
echo "LibreWinForms preview release bundle SHA-256 written to ${sidecar_output}."
