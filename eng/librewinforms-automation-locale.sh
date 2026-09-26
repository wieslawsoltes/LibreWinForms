#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
locale_dotnet="${LIBREWINFORMS_LOCALE_DOTNET:-dotnet}"
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

# This builds the actual private Core and its existing signed test assembly.
# No renderer, native COM server, or desktop runtime is needed for ABI recording.
"${locale_dotnet}" build \
  "${repo_root}/src/System.Private.Windows.Core/tests/System.Private.Windows.Core.Tests/System.Private.Windows.Core.Tests.csproj" \
  --configuration Release --nologo --verbosity quiet -m:1 \
  -p:TargetFrameworks=net10.0 -p:NetCurrent=net10.0 \
  -p:LibreWinFormsUseProGpuSystemDrawing=true \
  -p:LibreWinFormsReferenceMode=Project -p:MicrosoftNETCoreAppRefPackageVersion=

"${locale_dotnet}" \
  "${repo_root}/artifacts/bin/System.Private.Windows.Core.Tests/Release/net10.0/System.Private.Windows.Core.Tests.dll" \
  --filter-class Windows.Win32.System.Com.Tests.IDispatchLocaleTests \
  --minimum-expected-tests 8 --timeout 30s --no-progress
