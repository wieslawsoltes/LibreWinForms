#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

configuration="${LIBREWINFORMS_CONFIGURATION:-Release}"
package_version="${LIBREWINFORMS_SOURCE_FIRST_PACKAGE_VERSION:-0.1.0-source-first}"
sdk_package_version="${LIBREWINFORMS_SOURCE_FIRST_SDK_PACKAGE_VERSION:-0.1.0-source-first-sdk}"
progpu_package_version="${LIBREWINFORMS_PROGPU_PACKAGE_VERSION:-0.1.0-preview.62}"
package_output="${LIBREWINFORMS_SOURCE_FIRST_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/source-first}"
package_file="${package_output}/LibreWinForms.System.Windows.Forms.${package_version}.nupkg"
sdk_package_file="${package_output}/LibreWinForms.Sdk.${sdk_package_version}.nupkg"
smoke_root="$(mktemp -d -t librewinforms-source-package-smoke.XXXXXXXX)"
trap 'rm -rf "${smoke_root}"' EXIT

mkdir -p "${package_output}"

"${dotnet}" pack \
  "${repo_root}/packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj" \
  --configuration "${configuration}" \
  --output "${package_output}" \
  -p:PackageVersion="${package_version}" \
  -p:Version="${package_version}" \
  -p:LibreWinFormsReferenceMode=Project \
  -p:LibreWinFormsUseProGpuSystemDrawing=true \
  -p:LibreWinFormsProGpuPackageVersion="${progpu_package_version}" \
  -p:ContinuousIntegrationBuild=true

"${dotnet}" pack \
  "${repo_root}/src/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj" \
  --configuration "${configuration}" \
  --output "${package_output}" \
  -p:PackageVersion="${sdk_package_version}" \
  -p:Version="${sdk_package_version}" \
  -p:ContinuousIntegrationBuild=true

if [[ ! -f "${package_file}" ]]; then
  echo "Canonical source-first package was not produced: ${package_file}" >&2
  exit 1
fi

if [[ ! -f "${sdk_package_file}" ]]; then
  echo "Source-first SDK package was not produced: ${sdk_package_file}" >&2
  exit 1
fi

required_entries=(
  "lib/net11.0/System.Windows.Forms.dll"
  "lib/net11.0/System.Windows.Forms.xml"
  "lib/net11.0/System.Windows.Forms.Primitives.dll"
  "lib/net11.0/System.Windows.Forms.Primitives.xml"
  "lib/net11.0/System.Private.Windows.Core.dll"
  "lib/net11.0/System.Private.Windows.Core.xml"
  "lib/net11.0/Accessibility.dll"
  "lib/net11.0/LibreWinForms.Platform.dll"
  "lib/net11.0/LibreWinForms.Platform.xml"
  "ref/net11.0/System.Windows.Forms.dll"
  "ref/net11.0/System.Windows.Forms.Primitives.dll"
  "ref/net11.0/System.Private.Windows.Core.dll"
  "ref/net11.0/Accessibility.dll"
  "ref/net11.0/LibreWinForms.Platform.dll"
)

package_entries="$(unzip -Z1 "${package_file}")"
for required_entry in "${required_entries[@]}"; do
  if ! grep -Fxq "${required_entry}" <<<"${package_entries}"; then
    echo "Canonical source-first package is missing ${required_entry}." >&2
    exit 1
  fi
done

if grep -Eq '^lib/net11\.0/(ProGPU\.|System\.Drawing\.Common)' <<<"${package_entries}"; then
  echo "Canonical source-first package embeds ProGPU dependencies instead of declaring package dependencies." >&2
  exit 1
fi

implementation_hash="$(unzip -p "${package_file}" lib/net11.0/System.Windows.Forms.dll | sha256sum | cut -d' ' -f1)"
canonical_hash="$(sha256sum "${repo_root}/artifacts/bin/System.Windows.Forms/${configuration}/net11.0/System.Windows.Forms.dll" | cut -d' ' -f1)"
if [[ "${implementation_hash}" != "${canonical_hash}" ]]; then
  echo "Packed System.Windows.Forms.dll is not the current canonical build output." >&2
  exit 1
fi

platform_implementation_hash="$(unzip -p "${package_file}" lib/net11.0/LibreWinForms.Platform.dll | sha256sum | cut -d' ' -f1)"
platform_canonical_hash="$(sha256sum "${repo_root}/artifacts/bin/LibreWinForms.Platform/${configuration}/net11.0/LibreWinForms.Platform.dll" | cut -d' ' -f1)"
if [[ "${platform_implementation_hash}" != "${platform_canonical_hash}" ]]; then
  echo "Packed LibreWinForms.Platform.dll is not the current source build output." >&2
  exit 1
fi

nuspec="$(unzip -p "${package_file}" LibreWinForms.System.Windows.Forms.nuspec)"
if ! grep -Fq 'id="ProGPU.System.Drawing.Common"' <<<"${nuspec}"; then
  echo "Canonical source-first package does not declare ProGPU.System.Drawing.Common." >&2
  exit 1
fi

cp -R "${repo_root}/packaging/LibreWinForms.System.Windows.Forms.Smoke/." "${smoke_root}/"
cp "${repo_root}/NuGet.config" "${smoke_root}/NuGet.config"
"${dotnet}" nuget add source "${package_output}" \
  --name LibreWinFormsSourceFirst \
  --configfile "${smoke_root}/NuGet.config"

NUGET_PACKAGES="${smoke_root}/packages" "${dotnet}" restore \
  "${smoke_root}/LibreWinForms.System.Windows.Forms.Smoke.csproj" \
  --configfile "${smoke_root}/NuGet.config" \
  -p:SourceFirstPackageVersion="${package_version}"

NUGET_PACKAGES="${smoke_root}/packages" "${dotnet}" build \
  "${smoke_root}/LibreWinForms.System.Windows.Forms.Smoke.csproj" \
  --configuration "${configuration}" \
  --no-restore \
  -p:SourceFirstPackageVersion="${package_version}"

sdk_smoke_project="${repo_root}/packaging/LibreWinForms.Sdk.SourceFirstSmoke/LibreWinForms.Sdk.SourceFirstSmoke.csproj"
sdk_smoke_config="${smoke_root}/sdk-NuGet.config"
sdk_smoke_properties=(
  -p:LibreWinFormsUseCanonicalRuntime=true
  -p:LibreWinFormsUseProGpuSystemDrawing=true
  -p:LibreWinFormsReferenceMode=Project
  -p:MicrosoftNETCoreAppRefPackageVersion=
)

cp "${repo_root}/NuGet.config" "${sdk_smoke_config}"
"${dotnet}" nuget add source "${package_output}" \
  --name LibreWinFormsSourceFirstSdk \
  --configfile "${sdk_smoke_config}"

NUGET_PACKAGES="${smoke_root}/sdk-packages" "${dotnet}" restore \
  "${sdk_smoke_project}" \
  --configfile "${sdk_smoke_config}" \
  --force \
  --no-cache \
  "${sdk_smoke_properties[@]}"

NUGET_PACKAGES="${smoke_root}/sdk-packages" "${dotnet}" build \
  "${sdk_smoke_project}" \
  --configuration "${configuration}" \
  --no-restore \
  "${sdk_smoke_properties[@]}"

sdk_smoke_output="${repo_root}/artifacts/bin/LibreWinForms.Sdk.SourceFirstSmoke/${configuration}/net11.0"
sdk_smoke_deps="${sdk_smoke_output}/LibreWinForms.Sdk.SourceFirstSmoke.deps.json"
sdk_smoke_drawing="${sdk_smoke_output}/System.Drawing.Common.dll"
source_drawing="${repo_root}/external/ProGPU/src/System.Drawing.Common/bin/${configuration}/net10.0/System.Drawing.Common.dll"

if [[ ! -f "${sdk_smoke_deps}" ]] || ! grep -Fq '"System.Drawing.Common": "10.0.0.0"' "${sdk_smoke_deps}"; then
  echo "Source-first SDK smoke dependency manifest does not select ProGPU System.Drawing.Common." >&2
  exit 1
fi

if grep -Fq '"System.Drawing.Common/11.0.0-dev"' "${sdk_smoke_deps}"; then
  echo "Source-first SDK smoke dependency manifest retains the official Windows drawing project." >&2
  exit 1
fi

sdk_smoke_drawing_hash="$(sha256sum "${sdk_smoke_drawing}" | cut -d' ' -f1)"
source_drawing_hash="$(sha256sum "${source_drawing}" | cut -d' ' -f1)"
if [[ "${sdk_smoke_drawing_hash}" != "${source_drawing_hash}" ]]; then
  echo "Source-first SDK smoke output does not contain the exact ProGPU submodule drawing assembly." >&2
  exit 1
fi

NUGET_PACKAGES="${smoke_root}/sdk-packages" "${dotnet}" run \
  --project "${sdk_smoke_project}" \
  --configuration "${configuration}" \
  --no-build \
  --no-restore \
  "${sdk_smoke_properties[@]}"

echo "Canonical source-first package validated: ${package_file}"
echo "Source-first SDK package validated: ${sdk_package_file}"
echo "System.Windows.Forms SHA-256: ${implementation_hash}"
echo "LibreWinForms.Platform SHA-256: ${platform_implementation_hash}"
echo "SDK smoke System.Drawing.Common SHA-256: ${sdk_smoke_drawing_hash}"
echo "Fresh-cache canonical package consumer validated with warnings treated as errors."
echo "Fresh-cache source-first SDK project-mode consumer built and ran with the ProGPU bootstrap."
