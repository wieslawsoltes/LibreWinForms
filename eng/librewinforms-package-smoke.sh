#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

package_version="${LIBREWINFORMS_DEV_PACKAGE_VERSION:-0.1.0-preview.45}"
bridge_version="${LIBREWINFORMS_BRIDGE_PACKAGE_VERSION:-${package_version}}"
progpu_version="${LIBREWINFORMS_PROGPU_PACKAGE_VERSION:-0.1.0-preview.62}"
package_output="${LIBREWINFORMS_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
bridge_output="${LIBREWINFORMS_BRIDGE_PACKAGE_OUTPUT:-${repo_root}/../../artifacts/packages/Release/NonShipping}"

required_packages=(
  "${package_output}/LibreWinForms.System.Windows.Forms.${package_version}.nupkg"
  "${package_output}/LibreWinForms.ProGPU.${package_version}.nupkg"
  "${package_output}/LibreWinForms.WindowsFormsIntegration.${package_version}.nupkg"
  "${package_output}/LibreWinForms.Sdk.${package_version}.nupkg"
  "${bridge_output}/LibreWPF.Sdk.${bridge_version}.nupkg"
)

source "${repo_root}/eng/librewinforms-package-list.sh"
for package_id in "${librewinforms_preview_progpu_package_ids[@]}"; do
  required_packages+=("${package_output}/${package_id}.${progpu_version}.nupkg")
done
for package_id in "${librewinforms_preview_wfi_dependency_package_ids[@]}"; do
  required_packages+=("${package_output}/${package_id}.${progpu_version}.nupkg")
done

for package in "${required_packages[@]}"; do
  if [[ ! -f "${package}" ]]; then
    echo "Package-mode smoke is missing ${package}." >&2
    exit 1
  fi
done

work_root="$(mktemp -d "${TMPDIR:-/tmp}/librewinforms-package-smoke.XXXXXX")"
trap 'rm -rf "${work_root}"' EXIT
project_dir="${work_root}/project"
mkdir -p "${project_dir}"

cat >"${project_dir}/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="LibreWinForms" value="${package_output}" />
    <add key="LibreWPF" value="${bridge_output}" />
    <add key="NuGet" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
EOF

cat >"${project_dir}/LibreWinForms.PackageSmoke.csproj" <<EOF
<Project Sdk="LibreWPF.Sdk/${bridge_version}">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
    <GenerateDependencyFile>true</GenerateDependencyFile>
    <ProGpuWpfUsePortableWinFormsCompat>false</ProGpuWpfUsePortableWinFormsCompat>
    <ProGpuWpfUseLibreWinForms>false</ProGpuWpfUseLibreWinForms>
    <ProGpuPackageVersion>${progpu_version}</ProGpuPackageVersion>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="${repo_root}/packaging/LibreWinForms.CanonicalWfiSmoke/Program.cs" Link="Program.cs" />
    <PackageReference Include="LibreWinForms.System.Windows.Forms" Version="${package_version}" />
    <PackageReference Include="LibreWinForms.ProGPU" Version="${package_version}" />
    <PackageReference Include="LibreWinForms.WindowsFormsIntegration" Version="${package_version}" />
  </ItemGroup>
</Project>
EOF

project="${project_dir}/LibreWinForms.PackageSmoke.csproj"
export NUGET_PACKAGES="${LIBREWINFORMS_SMOKE_NUGET_PACKAGES:-${work_root}/nuget}"
"${dotnet}" restore "${project}" --configfile "${project_dir}/NuGet.config" --force --no-cache
"${dotnet}" build "${project}" --configuration Release --no-restore

package_smoke_assets="${project_dir}/obj/project.assets.json"
for package_identity in \
  "LibreWinForms.System.Windows.Forms/${package_version}" \
  "LibreWinForms.ProGPU/${package_version}" \
  "LibreWinForms.WindowsFormsIntegration/${package_version}"; do
  if ! grep -Fq "\"${package_identity}\"" "${package_smoke_assets}"; then
    echo "Canonical WFI package smoke did not resolve ${package_identity}." >&2
    exit 1
  fi
done
if grep -Fq 'LibreWinForms.Compatibility.System.Windows.Forms/' "${package_smoke_assets}"; then
  echo "Canonical WFI package smoke restored the retired compatibility runtime." >&2
  exit 1
fi

smoke_dll="${project_dir}/bin/Release/net10.0-windows/LibreWinForms.PackageSmoke.dll"
if [[ ! -f "${smoke_dll}" ]]; then
  echo "Package-mode smoke executable was not produced: ${smoke_dll}" >&2
  exit 1
fi

sdk_template_dir="${work_root}/sdk-template"
mkdir -p "${sdk_template_dir}"
cp "${project_dir}/NuGet.config" "${sdk_template_dir}/NuGet.config"

cat >"${sdk_template_dir}/LibreWinForms.SdkTemplate.csproj" <<EOF
<Project Sdk="LibreWinForms.Sdk/${package_version}">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <RootNamespace>LibreWinForms.SdkTemplate</RootNamespace>
    <Nullable>enable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
EOF

cat >"${sdk_template_dir}/Program.cs" <<'EOF'
namespace LibreWinForms.SdkTemplate;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        using (var bitmap = new Bitmap(64, 64))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Color.Black))
        {
            graphics.DrawCurve(
                pen,
                new[] { new Point(0, 0), new Point(16, 32), new Point(48, 16), new Point(63, 63) });
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}
EOF

cat >"${sdk_template_dir}/Form1.cs" <<'EOF'
namespace LibreWinForms.SdkTemplate;

public partial class Form1 : Form
{
    public Form1()
    {
        InitializeComponent();
    }
}
EOF

cat >"${sdk_template_dir}/Form1.Designer.cs" <<'EOF'
namespace LibreWinForms.SdkTemplate;

partial class Form1
{
    private void InitializeComponent()
    {
        ClientSize = new Size(320, 180);
        Text = "LibreWinForms SDK template";
    }
}
EOF

sdk_template_project="${sdk_template_dir}/LibreWinForms.SdkTemplate.csproj"
"${dotnet}" restore "${sdk_template_project}" --configfile "${sdk_template_dir}/NuGet.config" --force --no-cache
"${dotnet}" build "${sdk_template_project}" --configuration Release --no-restore

sdk_template_output="${sdk_template_dir}/bin/Release/net11.0"
sdk_template_dll="${sdk_template_output}/LibreWinForms.SdkTemplate.dll"
if [[ ! -f "${sdk_template_dll}" ]]; then
  echo "LibreWinForms SDK template smoke executable was not produced: ${sdk_template_dll}" >&2
  exit 1
fi

sdk_template_bootstrap="${sdk_template_dir}/obj/Release/net11.0/LibreWinForms.ApplicationBootstrap.g.cs"
sdk_template_deps="${sdk_template_output}/LibreWinForms.SdkTemplate.deps.json"
if [[ ! -f "${sdk_template_bootstrap}" ]] \
  || ! grep -Fq 'LibreWinForms.ProGPU.ProGpuPlatform.Register()' "${sdk_template_bootstrap}" \
  || grep -Fq 'WindowsFormsHost.EnableWindowsFormsInterop()' "${sdk_template_bootstrap}"; then
  echo "Default SDK package smoke did not select the canonical ProGPU bootstrap." >&2
  exit 1
fi
if [[ ! -f "${sdk_template_deps}" ]] \
  || ! grep -Fq "\"LibreWinForms.System.Windows.Forms/${package_version}\"" "${sdk_template_deps}" \
  || ! grep -Fq "\"LibreWinForms.ProGPU/${package_version}\"" "${sdk_template_deps}" \
  || ! grep -Fq "\"ProGPU.System.Drawing.Common/${progpu_version}\"" "${sdk_template_deps}"; then
  echo "Default SDK package smoke did not resolve the canonical runtime/backend/ProGPU closure." >&2
  exit 1
fi

"${dotnet}" "${smoke_dll}"

echo "LibreWinForms canonical WFI and SDK package smokes succeeded for ${package_version}."
