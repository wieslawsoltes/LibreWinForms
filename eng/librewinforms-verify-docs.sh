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
require_text README.md "LibreWinForms.Sdk/0.1.0-preview.45"
require_text README.md "LibreWinForms.System.Windows.Forms"
require_text README.md "LibreWinForms.WindowsFormsIntegration"
require_text README.md "### Bridge Packages"
require_text README.md "LibreWPF.Transport"
require_text README.md "ProGPU.System.Drawing.Common"
require_text README.md "## Original Upstream README"
require_text docs/librewinforms-release.md "LibreWinForms.Sdk"
require_text docs/librewinforms-release.md "LIBREWINFORMS_BRIDGE_PACKAGE_VERSION"
require_text docs/librewinforms-release.md "LIBREWINFORMS_PROGPU_PACKAGE_VERSION"
require_text docs/librewinforms-release.md "0.1.0-preview.45"
require_text docs/librewinforms-release.md "gh release create --generate-notes"
require_text docs/librewinforms-release.md "librewinforms-v<version>"
require_text README.md "fails if a stale or unexpected current-version"
require_text README.md "Release order matters"
require_text docs/librewinforms-release.md "unexpected current-version package artifact"
require_text eng/librewinforms-package-list.sh "LibreWinForms.System.Windows.Forms"
require_text eng/librewinforms-package-list.sh "LibreWinForms.ProGPU"
require_text eng/librewinforms-package-list.sh "LibreWinForms.Compatibility.System.Windows.Forms"
require_text eng/librewinforms-package-list.sh "LibreWinForms.WindowsFormsIntegration"
require_text eng/librewinforms-package-list.sh "LibreWinForms.Sdk"
require_text NuGet.config "https://api.nuget.org/v3/index.json"
require_text eng/librewinforms-fetch-librewpf-packages.sh "librewpf-v"
require_text eng/librewinforms-fetch-librewpf-packages.sh 'commit=\"${bridge_commit}\"'
require_text src/LibreWinForms.Portable/Directory.Build.props "<PackageProjectUrl>https://github.com/wieslawsoltes/winforms</PackageProjectUrl>"
require_text src/LibreWinForms.Portable/Directory.Build.props "<RepositoryUrl>https://github.com/wieslawsoltes/winforms</RepositoryUrl>"
require_text src/LibreWinForms.Portable/Directory.Build.props "<PackageTags>librewinforms;progpu;silk.net;winforms;cross-platform</PackageTags>"
require_text src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj "<PackageId>LibreWinForms.Compatibility.System.Windows.Forms</PackageId>"
require_text src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj "<Description>Transitional LibreWinForms portable System.Windows.Forms compatibility surface"
require_text src/LibreWinForms.Portable/LibreWinForms.WindowsFormsIntegration/LibreWinForms.WindowsFormsIntegration.csproj "<PackageId>LibreWinForms.WindowsFormsIntegration</PackageId>"
require_text src/LibreWinForms.Portable/LibreWinForms.WindowsFormsIntegration/LibreWinForms.WindowsFormsIntegration.csproj "<Description>LibreWinForms portable WindowsFormsIntegration host surface"
require_text src/LibreWinForms.Portable/LibreWinForms.WindowsFormsIntegration/LibreWinForms.WindowsFormsIntegration.csproj "<PackageTags>librewinforms;windowsformsintegration;librewpf;progpu;cross-platform</PackageTags>"
require_text src/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj "<PackageId>LibreWinForms.Sdk</PackageId>"
require_text src/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj "<Description>SDK package for cross-platform WinForms applications"
require_text src/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj "<PackageTags>librewinforms;winforms;sdk;progpu;silk.net;cross-platform;source-built</PackageTags>"
require_text src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj "<PackageReadmeFile>README.md</PackageReadmeFile>"
require_text src/LibreWinForms.Portable/LibreWinForms.WindowsFormsIntegration/LibreWinForms.WindowsFormsIntegration.csproj "<PackageReadmeFile>README.md</PackageReadmeFile>"
require_text src/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj "<PackageReadmeFile>README.md</PackageReadmeFile>"
require_text src/LibreWinForms.Sdk/Sdk/Sdk.props '<LibreWinFormsUseCanonicalRuntime Condition="'\''$(LibreWinFormsUseCanonicalRuntime)'\'' == '\'''\''">true</LibreWinFormsUseCanonicalRuntime>'
require_text src/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj 'LibreWinForms.Sdk.Versions.props'
require_text src/LibreWinForms.Sdk/targets/LibreWinForms.Sdk.targets "global::LibreWinForms.ProGPU.ProGpuPlatform.Register()"
require_text src/LibreWinForms.Sdk/targets/LibreWinForms.Sdk.targets 'ProjectReference Include="$(LibreWinFormsSourceRoot)src/System.Windows.Forms/System.Windows.Forms.csproj"'
require_text src/LibreWinForms.Sdk/targets/LibreWinForms.Sdk.targets 'ProjectReference Include="$(LibreWinFormsSourceRoot)src/LibreWinForms.ProGPU/LibreWinForms.ProGPU.csproj"'
require_text src/LibreWinForms.Sdk/targets/LibreWinForms.Sdk.targets 'AfterTargets="IncludeTransitiveProjectReferences"'
require_text src/LibreWinForms.Sdk/targets/LibreWinForms.Sdk.targets "<ProjectReference Include=\"@(_LibreWinFormsCanonicalProjectReference->'%(FullPath)')\" />"
require_text src/LibreWinForms.Sdk/targets/LibreWinForms.Sdk.targets 'PackageReference Include="LibreWinForms.System.Windows.Forms" Version="$(LibreWinFormsCanonicalPackageVersion)"'
require_text src/LibreWinForms.Sdk/targets/LibreWinForms.Sdk.targets 'PackageReference Include="LibreWinForms.ProGPU" Version="$(LibreWinFormsProGpuBackendPackageVersion)"'
require_text packaging/LibreWinForms.ProGPU/LibreWinForms.ProGPU.Package.csproj '<PackageId>LibreWinForms.ProGPU</PackageId>'
require_text packaging/LibreWinForms.ProGPU/LibreWinForms.ProGPU.Package.csproj '<CentralPackageTransitivePinningEnabled>false</CentralPackageTransitivePinningEnabled>'
require_text packaging/LibreWinForms.ProGPU/LibreWinForms.ProGPU.Package.csproj '<PackageVersion Include="ProGPU.System.Drawing.Common" Version="$(LibreWinFormsProGpuPackageVersion)" />'
require_text packaging/LibreWinForms.ProGPU/LibreWinForms.ProGPU.Package.csproj '<PackageReference Include="ProGPU.System.Drawing.Common" />'
require_text packaging/LibreWinForms.ProGPU/LibreWinForms.ProGPU.Package.csproj '-p:NetCurrent=&quot;$(TargetFramework)&quot;'
require_text packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj 'System.Private.Windows.GdiPlus.dll'
require_text packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj 'System.Windows.Forms.Design.dll'
require_text packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj '<PackageReference Include="System.CodeDom" />'
require_text packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj '-p:NetCurrent=&quot;$(TargetFramework)&quot;'
require_text packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj '-p:SystemCodeDomPackageVersion=&quot;$(LibreWinFormsPortableSupportPackageVersion)&quot;'
require_text packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj '<LibreWinFormsPortableSupportPackageVersion Condition='
require_text packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj '<PackageVersion Update="System.Resources.Extensions" Version="$(LibreWinFormsPortableSupportPackageVersion)" />'
require_text packaging/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj 'LibreWinFormsSkipCanonicalPackageBuild'
require_text Directory.Build.props '<LibreWinFormsPortableNetCoreAppRefVersion Condition="'\''$(LibreWinFormsPortableNetCoreAppRefVersion)'\'' == '\'''\''">10.0.5</LibreWinFormsPortableNetCoreAppRefVersion>'
require_text Directory.Build.props '<LibreWinFormsPortableSupportPackageVersion Condition="'\''$(LibreWinFormsPortableSupportPackageVersion)'\'' == '\'''\''">10.0.10</LibreWinFormsPortableSupportPackageVersion>'
require_text eng/librewinforms-pack-source-first.sh 'PROGPU_PACKAGE_GROUP=drawing-runtime'
require_text docs/librewinforms/api-compatibility-gap-analysis.md 'exact source-package closure'
require_text docs/librewinforms/source-first-cross-platform-plan.md 'Exact package-mode checkpoint'
require_text packaging/LibreWinForms.Sdk.SourceFirstSmoke/LibreWinForms.Sdk.SourceFirstSmoke.csproj '<Project Sdk="LibreWinForms.Sdk/0.1.0-source-first-sdk">'
require_text packaging/LibreWinForms.Sdk.SourceFirstVisibleSmoke/LibreWinForms.Sdk.SourceFirstVisibleSmoke.csproj '<LibreWinFormsReferenceMode>Package</LibreWinFormsReferenceMode>'
require_text packaging/LibreWinForms.Sdk.SourceFirstVisibleSmoke/Program.cs 'Application.Run(form)'
require_text .github/workflows/librewinforms-ci.yml 'Visible canonical package (${{ matrix.os }})'
require_text .github/workflows/librewinforms-ci.yml './eng/librewinforms-source-first-visible-smoke.sh'
require_text .github/workflows/librewinforms-ci.yml 'libglfw3'
require_text .github/workflows/librewinforms-ci.yml 'LIBREWINFORMS_VISIBLE_DOTNET: dotnet'
require_text eng/librewinforms-source-first-visible-smoke.sh 'LIBREWINFORMS_VISIBLE_DOTNET'
require_text eng/librewinforms-source-first-visible-smoke.sh 'cygpath -w'
require_text packaging/LibreWinForms.Sdk.CompatibilitySmoke/Program.cs "namespace LibreWinForms.SdkSmoke;"
if [[ -e src/LibreWinForms.Portable/LibreWinForms.Sdk/LibreWinForms.Sdk.csproj ]]; then
  echo "LibreWinForms.Sdk project must remain outside the frozen Portable source tree." >&2
  exit 1
fi
if [[ -e src/LibreWinForms.Portable/LibreWinForms.SdkSmoke/Program.cs ]]; then
  echo "LibreWinForms SDK compatibility smoke must remain outside the frozen Portable source tree." >&2
  exit 1
fi
require_text .github/workflows/librewinforms-ci.yml "LibreWinForms Build"
require_text .github/workflows/librewinforms-ci.yml "Stage immutable LibreWPF bridge packages"
require_text .github/workflows/librewinforms-ci.yml "librewpf-v0.1.0-preview.45"
require_text .github/workflows/librewinforms-ci.yml "LIBREWINFORMS_PROGPU_PACKAGE_VERSION"
require_text .github/workflows/librewinforms-ci.yml "LibreWinFormsReferenceMode=Package"
require_text .github/workflows/librewinforms-ci.yml 'restore_sources="${GITHUB_WORKSPACE}/wpf-bridge/artifacts/packages/Release/NonShipping;https://api.nuget.org/v3/index.json"'
require_text .github/workflows/librewinforms-ci.yml '-p:LibreWinFormsBridgePackageVersion="${LIBREWINFORMS_BRIDGE_PACKAGE_VERSION}"'
require_text .github/workflows/librewinforms-ci.yml '-p:LibreWinFormsProGpuPackageVersion="${LIBREWINFORMS_COMPATIBILITY_PROGPU_PACKAGE_VERSION}"'
require_text eng/librewinforms-package-smoke.sh '<ProGpuPackageVersion>${compatibility_progpu_version}</ProGpuPackageVersion>'
require_text .github/workflows/librewinforms-ci.yml '-p:RestoreSources="${restore_sources}"'
require_text .github/workflows/librewinforms-ci.yml "Run package-mode SDK smoke"
require_text .github/workflows/librewinforms-ci.yml 'src/test/compatibility/LibreWinForms.Portable.Tests/LibreWinForms.Portable.Tests.csproj'
require_text .github/workflows/librewinforms-docs.yml "LibreWinForms Docs"
require_text .github/workflows/librewinforms-docs.yml "docs/**"
require_text .github/workflows/librewinforms-public-package-smoke.yml "LibreWinForms Public Package Smoke"
require_text .github/workflows/librewinforms-public-package-smoke.yml '<Project Sdk="LibreWinForms.Sdk/${LIBREWINFORMS_VERSION}">'
require_text .github/workflows/librewinforms-public-package-smoke.yml "<TargetFramework>net11.0</TargetFramework>"
require_text .github/workflows/librewinforms-public-package-smoke.yml "ApplicationConfiguration.Initialize()"
require_text .github/workflows/librewinforms-public-package-smoke.yml "ubuntu-24.04"
require_text .github/workflows/librewinforms-public-package-smoke.yml "macos-15"
require_text .github/workflows/librewinforms-release.yml "LibreWinForms Release"
require_text .github/workflows/librewinforms-release.yml "LIBREWINFORMS_BRIDGE_PACKAGE_VERSION"
require_text .github/workflows/librewinforms-release.yml "LIBREWINFORMS_PROGPU_PACKAGE_VERSION"
require_text .github/workflows/librewinforms-release.yml "LIBREWINFORMS_BRIDGE_REF"
require_text .github/workflows/librewinforms-release.yml "Stage immutable LibreWPF bridge packages"
require_text .github/workflows/librewinforms-release.yml "LibreWinFormsReferenceMode=Package"
require_text .github/workflows/librewinforms-release.yml 'restore_sources="${GITHUB_WORKSPACE}/wpf-bridge/artifacts/packages/Release/NonShipping;https://api.nuget.org/v3/index.json"'
require_text .github/workflows/librewinforms-release.yml '-p:LibreWinFormsBridgePackageVersion="${LIBREWINFORMS_BRIDGE_PACKAGE_VERSION}"'
require_text .github/workflows/librewinforms-release.yml '-p:LibreWinFormsProGpuPackageVersion="${LIBREWINFORMS_COMPATIBILITY_PROGPU_PACKAGE_VERSION}"'
require_text .github/workflows/librewinforms-release.yml '-p:RestoreSources="${restore_sources}"'
require_text .github/workflows/librewinforms-release.yml "Run package-mode SDK smoke"
require_text .github/workflows/librewinforms-release.yml 'src/test/compatibility/LibreWinForms.Portable.Tests/LibreWinForms.Portable.Tests.csproj'
require_text .github/workflows/librewinforms-release.yml "librewinforms-v*"
require_text .github/workflows/librewinforms-release.yml "refs/tags/librewinforms-v"
require_text .github/workflows/librewinforms-release.yml "Create GitHub Release"
require_text .github/workflows/librewinforms-release.yml "gh release create"
require_text .github/workflows/librewinforms-release.yml "--generate-notes"
require_text .github/workflows/librewinforms-release.yml "if-no-files-found: error"
require_text src/test/compatibility/LibreWinForms.Portable.Tests/LibreWinForms.Portable.Tests.csproj 'Condition="'\''$(LibreWinFormsReferenceMode)'\'' == '\'''\''">Project'
require_text src/test/compatibility/LibreWinForms.Portable.Tests/LibreWinForms.Portable.Tests.csproj 'AdditionalProperties="LibreWinFormsReferenceMode=$(LibreWinFormsReferenceMode);LibreWinFormsBridgePackageVersion=$(LibreWinFormsBridgePackageVersion);LibreWinFormsProGpuPackageVersion=$(LibreWinFormsProGpuPackageVersion)"'
if [[ -e src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms.Tests/LibreWinForms.System.Windows.Forms.Tests.csproj ]]; then
  echo "LibreWinForms compatibility tests must remain outside the frozen Portable source tree." >&2
  exit 1
fi

for package_id in "${librewinforms_preview_package_ids[@]}"; do
  require_text README.md "| \`${package_id}\` |"
  require_text docs/librewinforms-release.md "\`${package_id}\`"
done

echo "LibreWinForms docs verified."
