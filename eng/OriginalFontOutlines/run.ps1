$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $IsWindows) {
    throw 'Disposable hosted Windows required.'
}
$root = Join-Path $env:RUNNER_TEMP 'font-outline-original'
New-Item -ItemType Directory -Path $root -ErrorAction Stop | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'reference') (Join-Path $root 'reference-source') -Recurse
Set-Content (Join-Path $root 'global.json') '{"sdk":{"version":"10.0.201","rollForward":"disable"}}'
$fontRoot = Join-Path $env:RUNNER_TEMP 'text-raster-font-inputs'
if (Test-Path $fontRoot) { throw 'Refusing to adopt existing font-transfer files.' }
Push-Location $root
try {
    dotnet build reference-source/TextRasterReference.csproj -c Release -r win-arm64 --nologo *> build.log
    if ($LASTEXITCODE -ne 0) { throw 'Original reference failed to compile.' }
    Get-ChildItem reference-source -Recurse -File | Get-FileHash -Algorithm SHA256 |
        ConvertTo-Json -Depth 5 | Set-Content payload-hashes.json
    $exe = Join-Path $root 'reference-source/bin/Release/net10.0-windows/win-arm64/TextRasterReference.exe'
    $evidence = Join-Path $root 'original'
    $child = Start-Process $exe -ArgumentList @('"' + $evidence + '"') -NoNewWindow -PassThru `
        -RedirectStandardOutput (Join-Path $root 'stdout.log') -RedirectStandardError (Join-Path $root 'stderr.log')
    try {
        if (-not $child.WaitForExit(60000)) {
            $child.Kill($true); $child.WaitForExit()
            throw 'Original process exceeded its unchanged 60-second bound.'
        }
        $child.Refresh()
        if ($child.ExitCode -ne 0) { throw "Original process failed with $($child.ExitCode)." }
    } finally { $child.Dispose() }
} finally {
    if (Test-Path $fontRoot) { Remove-Item $fontRoot -Recurse -Force }
    Pop-Location
}
