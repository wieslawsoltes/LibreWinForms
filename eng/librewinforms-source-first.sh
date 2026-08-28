#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
configuration="${CONFIGURATION:-Release}"
progpu_root="${repo_root}/external/ProGPU"

run_test_project() {
  local project="$1"
  local minimum_tests="$2"
  shift 2

  "${repo_root}/eng/common/dotnet.sh" build \
    "${project}" \
    --configuration "${configuration}" \
    --nologo \
    -t:Rebuild \
    -p:MicrosoftNETCoreAppRefPackageVersion= \
    "$@"
  "${repo_root}/eng/common/dotnet.sh" run \
    --project "${project}" \
    --configuration "${configuration}" \
    --no-build \
    -- \
    --minimum-expected-tests "${minimum_tests}"
}

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

echo "Testing typed platform contracts and the ProGPU/Silk.NET loop foundation."
run_test_project \
  "${repo_root}/src/LibreWinForms.Platform/tests/LibreWinForms.Platform.Tests.csproj" \
  27
run_test_project \
  "${repo_root}/src/LibreWinForms.ProGPU/tests/LibreWinForms.ProGPU.Tests.csproj" \
  20

echo "Testing unchanged canonical Application.Run(Form) against a typed headless backend."
run_test_project \
  "${repo_root}/src/test/integration/LibreWinForms.CanonicalLifecycle.Tests/LibreWinForms.CanonicalLifecycle.Tests.csproj" \
  41 \
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
