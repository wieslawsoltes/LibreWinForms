param(
    [Parameter(Mandatory)][ValidateSet("x64", "arm64")][string] $Architecture,
    [Parameter(Mandatory)][string] $OutputDirectory
)
$ErrorActionPreference = "Stop"
if (Test-Path -LiteralPath $OutputDirectory) { throw "Debugger output must be fresh: $OutputDirectory" }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
# Same installed Visual Studio discovery as ProGPU's Windows native build.
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio/Installer/vswhere.exe"
$component = if ($Architecture -eq "arm64") { "Microsoft.VisualStudio.Component.VC.Tools.ARM64" } else { "Microsoft.VisualStudio.Component.VC.Tools.x86.x64" }
$install = & $vswhere -latest -products * -requires $component -property installationPath | Select-Object -First 1
if (!$install) { throw "Required installed Visual Studio C++ tools are missing." }
Import-Module (Join-Path $install "Common7/Tools/Microsoft.VisualStudio.DevShell.dll")
Enter-VsDevShell -VsInstallPath $install -SkipAutomaticLocation -DevCmdArguments "-arch=$Architecture -host_arch=x64" | Out-Null
$PSNativeCommandUseErrorActionPreference = $false
Push-Location $OutputDirectory
try {
    & cl.exe /nologo /std:c++20 /EHsc /W4 /WX /O2 /MT (Join-Path $PSScriptRoot "native-stack.cpp") /Fe:GridNativeStack.exe /link dbghelp.lib
    if ($LASTEXITCODE -ne 0) { throw "Native stack helper compilation failed: $LASTEXITCODE" }
    & ./GridNativeStack.exe --self-test
    if ($LASTEXITCODE -ne 0) { throw "Native stack callback controls failed: $LASTEXITCODE" }
}
finally { Pop-Location }
& python (Join-Path $PSScriptRoot "../tests/test_original_grid_native_stack.py") --native-tool (Join-Path $OutputDirectory "GridNativeStack.exe")
if ($LASTEXITCODE -ne 0) { throw "Native stack ownership controls failed: $LASTEXITCODE" }
