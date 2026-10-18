$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $IsWindows) {
    throw 'Disposable hosted Windows required.'
}
python (Join-Path $PSScriptRoot 'prepare_pair.py')
if ($LASTEXITCODE -ne 0) { throw 'Exact producer preparation failed.' }
$root = Join-Path $env:RUNNER_TEMP 'font-outline-pair'
$fontRoot = Join-Path $env:RUNNER_TEMP 'text-raster-font-inputs'
if (Test-Path $fontRoot) { throw 'Refusing to adopt existing font-transfer files.' }
$receipt = Get-Content (Join-Path $root 'producer.json') -Raw | ConvertFrom-Json
$env:NUGET_PACKAGES = Join-Path $root 'nuget'
Push-Location $root
try {
    dotnet build reference-source/TextRasterReference.csproj -c Release -r win-arm64 --nologo *> reference-build.log
    if ($LASTEXITCODE -ne 0) { throw 'Original reference compilation failed.' }
    dotnet build native-source/NativeFontOutlineProbe.csproj -c Release -r win-arm64 --nologo "-p:ProducerVersion=$($receipt.version)" *> native-build.log
    if ($LASTEXITCODE -ne 0) { throw 'Native package consumer compilation failed.' }
    Get-ChildItem reference-source,native-source -Recurse -File | Get-FileHash -Algorithm SHA256 |
        ConvertTo-Json -Depth 5 | Set-Content payload-hashes.json
    function Invoke-Bounded([string]$exe, [string[]]$arguments, [string]$name) {
        $child = Start-Process $exe -ArgumentList $arguments -NoNewWindow -PassThru `
            -RedirectStandardOutput (Join-Path $root "$name-stdout.log") -RedirectStandardError (Join-Path $root "$name-stderr.log")
        try {
            if (-not $child.WaitForExit(60000)) {
                $child.Kill($true); $child.WaitForExit()
                throw "$name exceeded the original 60-second controller bound."
            }
            $child.Refresh()
            if ($child.ExitCode -ne 0) { throw "$name failed with $($child.ExitCode)." }
        } finally { $child.Dispose() }
    }
    $original = Join-Path $root 'original'; $native = Join-Path $root 'native'
    Invoke-Bounded (Join-Path $root 'reference-source/bin/Release/net10.0-windows/win-arm64/TextRasterReference.exe') @('"' + $original + '"') 'original'
    Invoke-Bounded (Join-Path $root 'native-source/bin/Release/net10.0/win-arm64/NativeFontOutlineProbe.exe') @('"' + $native + '"', '"' + $original + '"') 'native'
} finally {
    if (Test-Path $fontRoot) { Remove-Item $fontRoot -Recurse -Force }
    Pop-Location
}
