#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${repo_root}/eng/librewinforms-package-list.sh"

require_text() {
  local file="$1"
  local text="$2"
  if ! grep -qF -- "${text}" "${repo_root}/${file}"; then
    echo "Expected '${text}' in ${file}." >&2
    exit 1
  fi
}

require_text README.md "# LibreWinForms ProGPU Port"
require_text README.md "## Getting Started: Switch From WinForms To LibreWinForms"
require_text README.md "## NuGet Packages"
require_text README.md "default GitHub branch is \`librewinforms-progpu-port\`"
require_text README.md "LibreWinForms.Sdk"
require_text README.md "LibreWinForms.Sdk/0.1.0-preview.15"
require_text README.md "LibreWinForms.System.Windows.Forms"
require_text README.md "LibreWinForms.WindowsFormsIntegration"
require_text README.md "### Bridge Packages"
require_text README.md "LibreWPF.Transport"
require_text README.md "ProGPU.System.Drawing.Common"
require_text README.md "## Original Upstream README"
require_text docs/librewinforms-release.md "LibreWinForms.Sdk"
require_text docs/librewinforms-release.md "LIBREWINFORMS_BRIDGE_PACKAGE_VERSION"
require_text docs/librewinforms-release.md "0.1.0-preview.15"
require_text docs/librewinforms-release.md "gh release create --generate-notes"
require_text docs/librewinforms-release.md "librewinforms-v<version>"
require_text README.md "fails if a stale or unexpected current-version"
require_text README.md "Release order matters"
require_text docs/librewinforms-release.md "unexpected current-version package artifact"
require_text eng/librewinforms-package-list.sh "LibreWinForms.System.Windows.Forms"
require_text eng/librewinforms-package-list.sh "LibreWinForms.WindowsFormsIntegration"
require_text eng/librewinforms-package-list.sh "LibreWinForms.Sdk"
require_text NuGet.config "https://api.nuget.org/v3/index.json"
require_text src/LibreWinForms.Portable/Directory.Build.props "<PackageProjectUrl>https://github.com/wieslawsoltes/winforms</PackageProjectUrl>"
require_text src/LibreWinForms.Portable/Directory.Build.props "<RepositoryUrl>https://github.com/wieslawsoltes/winforms</RepositoryUrl>"
require_text src/LibreWinForms.Portable/Directory.Build.props "<PackageTags>librewinforms;progpu;silk.net;winforms;cross-platform</PackageTags>"
require_text src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj "<PackageId>LibreWinForms.System.Windows.Forms</PackageId>"
require_text src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj "<Description>LibreWinForms portable System.Windows.Forms API surface"
require_text src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj "<PackageTags>librewinforms;winforms;progpu;silk.net;cross-platform</PackageTags>"
require_text src/LibreWinForms.Portable/LibreWinForms.WindowsFormsIntegration/LibreWinForms.WindowsFormsIntegration.csproj "<PackageId>LibreWinForms.WindowsFormsIntegration</PackageId>"
require_text src/LibreWinForms.Portable/LibreWinForms.WindowsFormsIntegration/LibreWinForms.WindowsFormsIntegration.csproj "<Description>LibreWinForms portable WindowsFormsIntegration host surface"
require_text src/LibreWinForms.Portable/LibreWinForms.WindowsFormsIntegration/LibreWinForms.WindowsFormsIntegration.csproj "<PackageTags>librewinforms;windowsformsintegration;librewpf;progpu;cross-platform</PackageTags>"
require_text src/LibreWinForms.Portable/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj "<PackageId>LibreWinForms.Sdk</PackageId>"
require_text src/LibreWinForms.Portable/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj "<Description>SDK package for cross-platform WinForms applications"
require_text src/LibreWinForms.Portable/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj "<PackageTags>librewinforms;winforms;sdk;progpu;silk.net;cross-platform</PackageTags>"
require_text src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj "<PackageReadmeFile>README.md</PackageReadmeFile>"
require_text src/LibreWinForms.Portable/LibreWinForms.WindowsFormsIntegration/LibreWinForms.WindowsFormsIntegration.csproj "<PackageReadmeFile>README.md</PackageReadmeFile>"
require_text src/LibreWinForms.Portable/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj "<PackageReadmeFile>README.md</PackageReadmeFile>"
require_text .github/workflows/librewinforms-ci.yml "LibreWinForms Build"
require_text .github/workflows/librewinforms-ci.yml "Build LibreWPF bridge packages"
require_text .github/workflows/librewinforms-ci.yml "LibreWinFormsReferenceMode=Package"
require_text .github/workflows/librewinforms-ci.yml 'restore_sources="${GITHUB_WORKSPACE}/wpf-bridge/artifacts/packages/Release/NonShipping;https://api.nuget.org/v3/index.json"'
require_text .github/workflows/librewinforms-ci.yml '-p:LibreWinFormsBridgePackageVersion="${LIBREWINFORMS_BRIDGE_PACKAGE_VERSION}"'
require_text .github/workflows/librewinforms-ci.yml '-p:RestoreSources="${restore_sources}"'
require_text .github/workflows/librewinforms-ci.yml "Run package-mode SDK smoke"
require_text .github/workflows/librewinforms-docs.yml "LibreWinForms Docs"
require_text .github/workflows/librewinforms-docs.yml "docs/**"
require_text .github/workflows/librewinforms-release.yml "LibreWinForms Release"
require_text .github/workflows/librewinforms-release.yml "LIBREWINFORMS_BRIDGE_PACKAGE_VERSION"
require_text .github/workflows/librewinforms-release.yml "LIBREWINFORMS_BRIDGE_REF"
require_text .github/workflows/librewinforms-release.yml "LibreWinFormsReferenceMode=Package"
require_text .github/workflows/librewinforms-release.yml 'restore_sources="${GITHUB_WORKSPACE}/wpf-bridge/artifacts/packages/Release/NonShipping;https://api.nuget.org/v3/index.json"'
require_text .github/workflows/librewinforms-release.yml '-p:LibreWinFormsBridgePackageVersion="${LIBREWINFORMS_BRIDGE_PACKAGE_VERSION}"'
require_text .github/workflows/librewinforms-release.yml '-p:RestoreSources="${restore_sources}"'
require_text .github/workflows/librewinforms-release.yml "Run package-mode SDK smoke"
require_text .github/workflows/librewinforms-release.yml "librewinforms-v*"
require_text .github/workflows/librewinforms-release.yml "refs/tags/librewinforms-v"
require_text .github/workflows/librewinforms-release.yml "Create GitHub Release"
require_text .github/workflows/librewinforms-release.yml "gh release create"
require_text .github/workflows/librewinforms-release.yml "--generate-notes"
require_text .github/workflows/librewinforms-release.yml "if-no-files-found: error"
require_text src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms.Tests/LibreWinForms.System.Windows.Forms.Tests.csproj 'Condition="'\''$(LibreWinFormsReferenceMode)'\'' == '\'''\''">Project'
require_text src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms.Tests/LibreWinForms.System.Windows.Forms.Tests.csproj 'AdditionalProperties="LibreWinFormsReferenceMode=$(LibreWinFormsReferenceMode);LibreWinFormsBridgePackageVersion=$(LibreWinFormsBridgePackageVersion)"'

for package_id in "${librewinforms_preview_package_ids[@]}"; do
  require_text README.md "| \`${package_id}\` |"
  require_text docs/librewinforms-release.md "\`${package_id}\`"
done

echo "LibreWinForms docs verified."
