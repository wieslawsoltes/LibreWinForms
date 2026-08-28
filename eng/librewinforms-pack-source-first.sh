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
sdk_package_version="${LIBREWINFORMS_SOURCE_FIRST_SDK_PACKAGE_VERSION:-${package_version}}"
backend_package_version="${LIBREWINFORMS_SOURCE_FIRST_BACKEND_PACKAGE_VERSION:-${package_version}}"
progpu_package_version="${LIBREWINFORMS_SOURCE_FIRST_PROGPU_PACKAGE_VERSION:-${package_version}}"
package_output="${LIBREWINFORMS_SOURCE_FIRST_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/source-first}"
package_file="${package_output}/LibreWinForms.System.Windows.Forms.${package_version}.nupkg"
sdk_package_file="${package_output}/LibreWinForms.Sdk.${sdk_package_version}.nupkg"
backend_package_file="${package_output}/LibreWinForms.ProGPU.${backend_package_version}.nupkg"
progpu_drawing_package_file="${package_output}/ProGPU.System.Drawing.Common.${progpu_package_version}.nupkg"
smoke_root="$(mktemp -d -t librewinforms-source-package-smoke.XXXXXXXX)"
trap 'rm -rf "${smoke_root}"' EXIT

mkdir -p "${package_output}"

PROGPU_CONFIGURATION="${configuration}" \
PROGPU_PACKAGE_VERSION="${progpu_package_version}" \
PROGPU_PACKAGE_OUTPUT="${package_output}" \
PROGPU_PACKAGE_GROUP=drawing-runtime \
  "${repo_root}/external/ProGPU/eng/progpu-pack.sh"
progpu_drawing_source_hash="$(sha256sum "${repo_root}/external/ProGPU/src/System.Drawing.Common/bin/${configuration}/net10.0/System.Drawing.Common.dll" | cut -d' ' -f1)"

canonical_pack_config="${smoke_root}/canonical-NuGet.config"
cp "${repo_root}/NuGet.config" "${canonical_pack_config}"
"${dotnet}" nuget add source "${package_output}" \
  --name LibreWinFormsPinnedProGpu \
  --configfile "${canonical_pack_config}"
NUGET_PACKAGES="${smoke_root}/canonical-packages" "${dotnet}" restore \
  "${repo_root}/packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj" \
  --configfile "${canonical_pack_config}" \
  --force \
  --no-cache \
  -p:LibreWinFormsProGpuPackageVersion="${progpu_package_version}"
NUGET_PACKAGES="${smoke_root}/canonical-packages" "${dotnet}" pack \
  "${repo_root}/packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj" \
  --configuration "${configuration}" \
  --output "${package_output}" \
  --no-restore \
  -p:PackageVersion="${package_version}" \
  -p:Version="${package_version}" \
  -p:LibreWinFormsReferenceMode=Project \
  -p:LibreWinFormsUseProGpuSystemDrawing=true \
  -p:LibreWinFormsProGpuPackageVersion="${progpu_package_version}" \
  -p:ContinuousIntegrationBuild=true

canonical_hash="$(sha256sum "${repo_root}/artifacts/bin/System.Windows.Forms/${configuration}/net11.0/System.Windows.Forms.dll" | cut -d' ' -f1)"
platform_canonical_hash="$(sha256sum "${repo_root}/artifacts/bin/LibreWinForms.Platform/${configuration}/net11.0/LibreWinForms.Platform.dll" | cut -d' ' -f1)"

backend_pack_config="${smoke_root}/backend-NuGet.config"
cp "${repo_root}/NuGet.config" "${backend_pack_config}"
"${dotnet}" nuget add source "${package_output}" \
  --name LibreWinFormsSourceFirstBackend \
  --configfile "${backend_pack_config}"
NUGET_PACKAGES="${smoke_root}/backend-packages" "${dotnet}" restore \
  "${repo_root}/packaging/LibreWinForms.ProGPU/LibreWinForms.ProGPU.Package.csproj" \
  --configfile "${backend_pack_config}" \
  --force \
  --no-cache \
  -p:LibreWinFormsCanonicalPackageVersion="${package_version}" \
  -p:LibreWinFormsProGpuPackageVersion="${progpu_package_version}"
NUGET_PACKAGES="${smoke_root}/backend-packages" "${dotnet}" pack \
  "${repo_root}/packaging/LibreWinForms.ProGPU/LibreWinForms.ProGPU.Package.csproj" \
  --configuration "${configuration}" \
  --output "${package_output}" \
  --no-restore \
  -p:PackageVersion="${backend_package_version}" \
  -p:Version="${backend_package_version}" \
  -p:LibreWinFormsCanonicalPackageVersion="${package_version}" \
  -p:LibreWinFormsProGpuPackageVersion="${progpu_package_version}" \
  -p:ContinuousIntegrationBuild=true

"${dotnet}" pack \
  "${repo_root}/src/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj" \
  --configuration "${configuration}" \
  --output "${package_output}" \
  -p:PackageVersion="${sdk_package_version}" \
  -p:Version="${sdk_package_version}" \
  -p:LibreWinFormsCanonicalPackageVersion="${package_version}" \
  -p:LibreWinFormsProGpuBackendPackageVersion="${backend_package_version}" \
  -p:LibreWinFormsProGpuPackageVersion="${progpu_package_version}" \
  -p:ContinuousIntegrationBuild=true

if [[ ! -f "${package_file}" ]]; then
  echo "Canonical source-first package was not produced: ${package_file}" >&2
  exit 1
fi

if [[ ! -f "${sdk_package_file}" ]]; then
  echo "Source-first SDK package was not produced: ${sdk_package_file}" >&2
  exit 1
fi

if [[ ! -f "${backend_package_file}" ]]; then
  echo "Source-first ProGPU backend package was not produced: ${backend_package_file}" >&2
  exit 1
fi

if [[ ! -f "${progpu_drawing_package_file}" ]]; then
  echo "Pinned-source ProGPU drawing package was not produced: ${progpu_drawing_package_file}" >&2
  exit 1
fi

sdk_version_props="$(unzip -p "${sdk_package_file}" Sdk/LibreWinForms.Sdk.Versions.props)"
if ! grep -Fq "<LibreWinFormsPackagedRuntimeVersion>${package_version}</LibreWinFormsPackagedRuntimeVersion>" <<<"${sdk_version_props}" \
  || ! grep -Fq "<LibreWinFormsPackagedProGpuBackendVersion>${backend_package_version}</LibreWinFormsPackagedProGpuBackendVersion>" <<<"${sdk_version_props}" \
  || ! grep -Fq "<LibreWinFormsPackagedProGpuVersion>${progpu_package_version}</LibreWinFormsPackagedProGpuVersion>" <<<"${sdk_version_props}"; then
  echo "Source-first SDK package does not carry its exact runtime/backend/ProGPU version closure." >&2
  exit 1
fi

required_entries=(
  "lib/net11.0/System.Windows.Forms.dll"
  "lib/net11.0/System.Windows.Forms.xml"
  "lib/net11.0/System.Windows.Forms.Primitives.dll"
  "lib/net11.0/System.Windows.Forms.Primitives.xml"
  "lib/net11.0/System.Private.Windows.Core.dll"
  "lib/net11.0/System.Private.Windows.Core.xml"
  "lib/net11.0/System.Private.Windows.GdiPlus.dll"
  "lib/net11.0/System.Private.Windows.GdiPlus.xml"
  "lib/net11.0/Accessibility.dll"
  "lib/net11.0/LibreWinForms.Platform.dll"
  "lib/net11.0/LibreWinForms.Platform.xml"
  "ref/net11.0/System.Windows.Forms.dll"
  "ref/net11.0/System.Windows.Forms.Primitives.dll"
  "ref/net11.0/System.Private.Windows.Core.dll"
  "ref/net11.0/System.Private.Windows.GdiPlus.dll"
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
if [[ "${implementation_hash}" != "${canonical_hash}" ]]; then
  echo "Packed System.Windows.Forms.dll is not the current canonical build output." >&2
  exit 1
fi

platform_implementation_hash="$(unzip -p "${package_file}" lib/net11.0/LibreWinForms.Platform.dll | sha256sum | cut -d' ' -f1)"
if [[ "${platform_implementation_hash}" != "${platform_canonical_hash}" ]]; then
  echo "Packed LibreWinForms.Platform.dll is not the current source build output." >&2
  exit 1
fi

nuspec="$(unzip -p "${package_file}" LibreWinForms.System.Windows.Forms.nuspec)"
if ! grep -Fq "id=\"ProGPU.System.Drawing.Common\" version=\"${progpu_package_version}\"" <<<"${nuspec}"; then
  echo "Canonical source-first package does not declare the exact pinned-source ProGPU.System.Drawing.Common package." >&2
  exit 1
fi

progpu_drawing_package_hash="$(unzip -p "${progpu_drawing_package_file}" lib/net10.0/System.Drawing.Common.dll | sha256sum | cut -d' ' -f1)"
if [[ "${progpu_drawing_package_hash}" != "${progpu_drawing_source_hash}" ]]; then
  echo "Pinned-source ProGPU drawing package does not contain the exact submodule assembly." >&2
  exit 1
fi

backend_entries="$(unzip -Z1 "${backend_package_file}")"
for required_entry in \
  "lib/net11.0/LibreWinForms.ProGPU.dll" \
  "lib/net11.0/LibreWinForms.ProGPU.xml" \
  "ref/net11.0/LibreWinForms.ProGPU.dll"; do
  if ! grep -Fxq "${required_entry}" <<<"${backend_entries}"; then
    echo "Source-first ProGPU backend package is missing ${required_entry}." >&2
    exit 1
  fi
done

if grep -Eq '^lib/net11\.0/(ProGPU\.|System\.Drawing\.Common|LibreWinForms\.Platform)' <<<"${backend_entries}"; then
  echo "Source-first ProGPU backend package embeds dependency assemblies." >&2
  exit 1
fi

backend_nuspec="$(unzip -p "${backend_package_file}" LibreWinForms.ProGPU.nuspec)"
if ! grep -Fq "id=\"LibreWinForms.System.Windows.Forms\" version=\"${package_version}\"" <<<"${backend_nuspec}" \
  || ! grep -Fq "id=\"ProGPU.System.Drawing.Common\" version=\"${progpu_package_version}\"" <<<"${backend_nuspec}"; then
  echo "Source-first ProGPU backend package does not declare the exact canonical runtime and pinned-source drawing dependencies." >&2
  exit 1
fi

backend_implementation_hash="$(unzip -p "${backend_package_file}" lib/net11.0/LibreWinForms.ProGPU.dll | sha256sum | cut -d' ' -f1)"
backend_source_hash="$(sha256sum "${repo_root}/artifacts/bin/LibreWinForms.ProGPU/${configuration}/net11.0/LibreWinForms.ProGPU.dll" | cut -d' ' -f1)"
if [[ "${backend_implementation_hash}" != "${backend_source_hash}" ]]; then
  echo "Packed LibreWinForms.ProGPU.dll is not the current source build output." >&2
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

sdk_smoke_source="${repo_root}/packaging/LibreWinForms.Sdk.SourceFirstSmoke"
sdk_smoke_root="${smoke_root}/sdk-project"
sdk_smoke_project="${sdk_smoke_root}/LibreWinForms.Sdk.SourceFirstSmoke.csproj"
sdk_smoke_config="${sdk_smoke_root}/NuGet.config"
sdk_smoke_properties=(
  -p:LibreWinFormsSourceRoot="${repo_root}/"
  -p:LibreWinFormsUseCanonicalRuntime=true
  -p:LibreWinFormsUseProGpuSystemDrawing=true
  -p:LibreWinFormsReferenceMode=Project
  -p:MicrosoftNETCoreAppRefPackageVersion=
)

mkdir -p "${sdk_smoke_root}"
sed "s#LibreWinForms.Sdk/0.1.0-source-first-sdk#LibreWinForms.Sdk/${sdk_package_version}#" \
  "${sdk_smoke_source}/LibreWinForms.Sdk.SourceFirstSmoke.csproj" \
  >"${sdk_smoke_project}"
cp "${sdk_smoke_source}/Program.cs" "${sdk_smoke_root}/"
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

sdk_smoke_output="${sdk_smoke_root}/bin/${configuration}/net11.0"
sdk_smoke_deps="${sdk_smoke_output}/LibreWinForms.Sdk.SourceFirstSmoke.deps.json"
sdk_smoke_drawing="${sdk_smoke_output}/System.Drawing.Common.dll"
sdk_smoke_bootstrap="${sdk_smoke_root}/obj/${configuration}/net11.0/LibreWinForms.ApplicationBootstrap.g.cs"
source_drawing="${repo_root}/external/ProGPU/src/System.Drawing.Common/bin/${configuration}/net10.0/System.Drawing.Common.dll"

if [[ ! -f "${sdk_smoke_bootstrap}" ]] || ! grep -Fq 'LibreWinForms.ProGPU.ProGpuPlatform.Register()' "${sdk_smoke_bootstrap}"; then
  echo "Source-first SDK smoke did not generate the canonical ProGPU bootstrap." >&2
  exit 1
fi

if grep -Fq 'WindowsFormsHost.EnableWindowsFormsInterop()' "${sdk_smoke_bootstrap}"; then
  echo "Source-first SDK smoke generated the compatibility WindowsFormsIntegration bootstrap." >&2
  exit 1
fi

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

sdk_package_smoke_root="${smoke_root}/sdk-package-project"
sdk_package_smoke_project="${sdk_package_smoke_root}/LibreWinForms.Sdk.SourceFirstSmoke.csproj"
sdk_package_smoke_config="${sdk_package_smoke_root}/NuGet.config"
sdk_package_smoke_properties=(
  -p:LibreWinFormsUseCanonicalRuntime=true
  -p:LibreWinFormsUseProGpuSystemDrawing=true
  -p:LibreWinFormsReferenceMode=Package
  -p:LibreWinFormsCanonicalPackageVersion="${package_version}"
  -p:LibreWinFormsProGpuBackendPackageVersion="${backend_package_version}"
  -p:MicrosoftNETCoreAppRefPackageVersion=
)

mkdir -p "${sdk_package_smoke_root}"
sed "s#LibreWinForms.Sdk/0.1.0-source-first-sdk#LibreWinForms.Sdk/${sdk_package_version}#" \
  "${sdk_smoke_source}/LibreWinForms.Sdk.SourceFirstSmoke.csproj" \
  >"${sdk_package_smoke_project}"
cp "${sdk_smoke_source}/Program.cs" "${sdk_package_smoke_root}/"
cp "${repo_root}/NuGet.config" "${sdk_package_smoke_config}"
"${dotnet}" nuget add source "${package_output}" \
  --name LibreWinFormsSourceFirstSdkPackages \
  --configfile "${sdk_package_smoke_config}"

NUGET_PACKAGES="${smoke_root}/sdk-package-packages" "${dotnet}" restore \
  "${sdk_package_smoke_project}" \
  --configfile "${sdk_package_smoke_config}" \
  --force \
  --no-cache \
  "${sdk_package_smoke_properties[@]}"
NUGET_PACKAGES="${smoke_root}/sdk-package-packages" "${dotnet}" build \
  "${sdk_package_smoke_project}" \
  --configuration "${configuration}" \
  --no-restore \
  "${sdk_package_smoke_properties[@]}"

sdk_package_smoke_output="${sdk_package_smoke_root}/bin/${configuration}/net11.0"
sdk_package_smoke_deps="${sdk_package_smoke_output}/LibreWinForms.Sdk.SourceFirstSmoke.deps.json"
sdk_package_smoke_bootstrap="${sdk_package_smoke_root}/obj/${configuration}/net11.0/LibreWinForms.ApplicationBootstrap.g.cs"
sdk_package_smoke_drawing="${sdk_package_smoke_output}/System.Drawing.Common.dll"
sdk_package_smoke_backend="${sdk_package_smoke_output}/LibreWinForms.ProGPU.dll"
sdk_package_drawing_source="${smoke_root}/sdk-package-packages/progpu.system.drawing.common/${progpu_package_version}/lib/net10.0/System.Drawing.Common.dll"

if [[ ! -f "${sdk_package_smoke_bootstrap}" ]] || ! grep -Fq 'LibreWinForms.ProGPU.ProGpuPlatform.Register()' "${sdk_package_smoke_bootstrap}"; then
  echo "Source-first SDK package-mode smoke did not generate the canonical ProGPU bootstrap." >&2
  exit 1
fi
if grep -Fq 'WindowsFormsHost.EnableWindowsFormsInterop()' "${sdk_package_smoke_bootstrap}"; then
  echo "Source-first SDK package-mode smoke generated the compatibility WindowsFormsIntegration bootstrap." >&2
  exit 1
fi
if [[ ! -f "${sdk_package_smoke_deps}" ]] \
  || ! grep -Fq "\"LibreWinForms.ProGPU/${backend_package_version}\"" "${sdk_package_smoke_deps}" \
  || ! grep -Fq "\"LibreWinForms.System.Windows.Forms/${package_version}\"" "${sdk_package_smoke_deps}" \
  || ! grep -Fq "\"ProGPU.System.Drawing.Common/${progpu_package_version}\"" "${sdk_package_smoke_deps}" \
  || ! grep -Fq '"assemblyVersion": "10.0.0.0"' "${sdk_package_smoke_deps}"; then
  echo "Source-first SDK package-mode dependency manifest does not select the canonical runtime, backend, and pinned-source ProGPU drawing closure." >&2
  exit 1
fi
if grep -Fq '"System.Drawing.Common/11.0.0-dev"' "${sdk_package_smoke_deps}"; then
  echo "Source-first SDK package-mode dependency manifest retains the official Windows drawing project." >&2
  exit 1
fi

sdk_package_smoke_backend_hash="$(sha256sum "${sdk_package_smoke_backend}" | cut -d' ' -f1)"
if [[ "${sdk_package_smoke_backend_hash}" != "${backend_implementation_hash}" ]]; then
  echo "Source-first SDK package-mode output does not contain the packed LibreWinForms.ProGPU backend." >&2
  exit 1
fi
sdk_package_smoke_drawing_hash="$(sha256sum "${sdk_package_smoke_drawing}" | cut -d' ' -f1)"
sdk_package_drawing_source_hash="$(sha256sum "${sdk_package_drawing_source}" | cut -d' ' -f1)"
if [[ "${sdk_package_smoke_drawing_hash}" != "${sdk_package_drawing_source_hash}" ]] \
  || [[ "${sdk_package_smoke_drawing_hash}" != "${progpu_drawing_source_hash}" ]]; then
  echo "Source-first SDK package-mode output does not contain the exact pinned-source ProGPU drawing payload." >&2
  exit 1
fi

NUGET_PACKAGES="${smoke_root}/sdk-package-packages" "${dotnet}" run \
  --project "${sdk_package_smoke_project}" \
  --configuration "${configuration}" \
  --no-build \
  --no-restore \
  "${sdk_package_smoke_properties[@]}"

echo "Canonical source-first package validated: ${package_file}"
echo "Source-first ProGPU backend package validated: ${backend_package_file}"
echo "Source-first SDK package validated: ${sdk_package_file}"
echo "System.Windows.Forms SHA-256: ${implementation_hash}"
echo "LibreWinForms.Platform SHA-256: ${platform_implementation_hash}"
echo "LibreWinForms.ProGPU SHA-256: ${backend_implementation_hash}"
echo "SDK smoke System.Drawing.Common SHA-256: ${sdk_smoke_drawing_hash}"
echo "SDK package-mode System.Drawing.Common SHA-256: ${sdk_package_smoke_drawing_hash}"
echo "Fresh-cache canonical package consumer validated with warnings treated as errors."
echo "Fresh-cache source-first SDK project-mode consumer built and ran with the ProGPU bootstrap."
echo "Fresh-cache source-first SDK package-mode consumer built and ran with canonical packages and the pinned-source ProGPU bootstrap."
