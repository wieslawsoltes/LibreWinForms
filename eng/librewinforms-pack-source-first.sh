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
progpu_package_version="${LIBREWINFORMS_PROGPU_PACKAGE_VERSION:-0.1.0-preview.56}"
package_output="${LIBREWINFORMS_SOURCE_FIRST_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/source-first}"
package_file="${package_output}/LibreWinForms.System.Windows.Forms.${package_version}.nupkg"
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

if [[ ! -f "${package_file}" ]]; then
  echo "Canonical source-first package was not produced: ${package_file}" >&2
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
  "ref/net11.0/System.Windows.Forms.dll"
  "ref/net11.0/System.Windows.Forms.Primitives.dll"
  "ref/net11.0/System.Private.Windows.Core.dll"
  "ref/net11.0/Accessibility.dll"
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

echo "Canonical source-first package validated: ${package_file}"
echo "System.Windows.Forms SHA-256: ${implementation_hash}"
echo "Fresh-cache canonical package consumer validated with warnings treated as errors."
