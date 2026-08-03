#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

package_version="${LIBREWINFORMS_DEV_PACKAGE_VERSION:-0.1.0-preview.41}"
bridge_version="${LIBREWINFORMS_BRIDGE_PACKAGE_VERSION:-${package_version}}"
package_output="${LIBREWINFORMS_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
bridge_output="${LIBREWINFORMS_BRIDGE_PACKAGE_OUTPUT:-${repo_root}/../../artifacts/packages/Release/NonShipping}"

required_packages=(
  "${package_output}/LibreWinForms.System.Windows.Forms.${package_version}.nupkg"
  "${package_output}/LibreWinForms.WindowsFormsIntegration.${package_version}.nupkg"
  "${package_output}/LibreWinForms.Sdk.${package_version}.nupkg"
  "${bridge_output}/LibreWPF.Sdk.${bridge_version}.nupkg"
)

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
    <ProGpuWpfUsePortableWinFormsCompat>true</ProGpuWpfUsePortableWinFormsCompat>
    <ProGpuWpfUseLibreWinForms>true</ProGpuWpfUseLibreWinForms>
    <ProGpuWpfLibreWinFormsPackageVersion>${package_version}</ProGpuWpfLibreWinFormsPackageVersion>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="${repo_root}/src/LibreWinForms.Portable/LibreWinForms.SdkSmoke/Program.cs" Link="Program.cs" />
  </ItemGroup>
</Project>
EOF

project="${project_dir}/LibreWinForms.PackageSmoke.csproj"
export NUGET_PACKAGES="${LIBREWINFORMS_SMOKE_NUGET_PACKAGES:-${work_root}/nuget}"
"${dotnet}" restore "${project}" --configfile "${project_dir}/NuGet.config" --force --no-cache
"${dotnet}" build "${project}" --configuration Release --no-restore

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
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>LibreWinForms.SdkTemplate</RootNamespace>
    <Nullable>enable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.Compatibility" Version="7.0.3" />
  </ItemGroup>
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

sdk_template_dll="${sdk_template_dir}/bin/Release/net10.0/LibreWinForms.SdkTemplate.dll"
if [[ ! -f "${sdk_template_dll}" ]]; then
  echo "LibreWinForms SDK template smoke executable was not produced: ${sdk_template_dll}" >&2
  exit 1
fi

modes=(
  --run-form
  --run-thread-loop
  --run-dialog
  --run-modeless-owner
  --run-designer
  --run-message-box
  --run-checkables
  --run-listview
  --run-custom-paint
  --run-paint-surface-retirement
  --run-render-allocation
  --run-layout-allocation
  --run-create-graphics
  --run-text-renderer
  --run-keyboard
  --run-classdiagram
  --run-hexeditor-host
  --run-cross-framework-drag
  --run-native-popup
  --run-host-double-click
)
if [[ -n "${LIBREWINFORMS_SMOKE_MODES:-}" ]]; then
  read -r -a modes <<<"${LIBREWINFORMS_SMOKE_MODES}"
fi

for mode in "${modes[@]}"; do
  "${dotnet}" "${smoke_dll}" "${mode}"
done

echo "LibreWinForms package-mode SDK smoke succeeded for ${package_version}."
