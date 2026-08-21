#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
configuration="${CONFIGURATION:-Release}"
progpu_root="${repo_root}/external/ProGPU"

if [[ ! -f "${progpu_root}/src/System.Drawing.Common/System.Drawing.Common.csproj" ]]; then
  echo "The external/ProGPU submodule is not initialized. Run git submodule update --init --recursive." >&2
  exit 1
fi

echo "Building canonical System.Windows.Forms source graph."
"${repo_root}/eng/common/dotnet.sh" build \
  "${repo_root}/src/System.Windows.Forms/System.Windows.Forms.csproj" \
  --configuration "${configuration}" \
  --nologo

echo "Building canonical System.Windows.Forms against source-built ProGPU System.Drawing."
"${repo_root}/eng/common/dotnet.sh" build \
  "${repo_root}/src/System.Windows.Forms/System.Windows.Forms.csproj" \
  --configuration "${configuration}" \
  --nologo \
  -p:LibreWinFormsUseProGpuSystemDrawing=true \
  -p:LibreWinFormsReferenceMode=Project

echo "Verifying ProGPU System.Drawing API debt and focused quality gates."
(
  cd "${progpu_root}"
  ./eng/progpu-verify-system-drawing-api.sh
  dotnet test src/System.Drawing.Common.Tests/System.Drawing.Common.Tests.csproj \
    --configuration "${configuration}" \
    --nologo
)

echo "Building the current comparison lane from the ProGPU submodule rather than NuGet."
"${repo_root}/eng/common/dotnet.sh" build \
  "${repo_root}/src/LibreWinForms.Portable/LibreWinForms.System.Windows.Forms/LibreWinForms.System.Windows.Forms.csproj" \
  --configuration "${configuration}" \
  --nologo \
  -p:LibreWinFormsReferenceMode=Project

echo "Source-first shadow validation succeeded."
