$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $IsWindows) {
    throw 'Disposable hosted Windows required.'
}
$prepared = $PSScriptRoot
$receipt = Get-Content (Join-Path $prepared 'producer.json') -Raw | ConvertFrom-Json
if ($receipt.wholeBuildSuccess -ne $true -or $receipt.head -notmatch '^[0-9a-f]{40}$' -or
    $receipt.artifactSha256 -notmatch '^[0-9a-f]{64}$' -or $receipt.artifactId -le 0) {
    throw 'A complete exact-head producer receipt is required before package use.'
}
$root = Join-Path $env:RUNNER_TEMP 'text-pair'
New-Item -ItemType Directory -Path $root -ErrorAction Stop | Out-Null
$feed = New-Item -ItemType Directory -Path (Join-Path $root 'feed')
$archive = Join-Path $root 'producer.zip'
Invoke-WebRequest -Uri "https://api.github.com/repos/wieslawsoltes/LibreWinForms/actions/artifacts/$($receipt.artifactId)/zip" `
    -Headers @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json' } -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $receipt.artifactSha256) {
    throw 'Producer artifact changed.'
}
# Source-first package artifacts are a flat immutable package closure.
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    if ($zip.Entries.Count -ne $receipt.packages.Count) { throw 'Package inventory count changed.' }
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName -ne $entry.Name -or -not $entry.Name.EndsWith('.nupkg') -or -not $seen.Add($entry.Name)) {
            throw 'Unexpected package archive member.'
        }
        $expected = @($receipt.packages | Where-Object name -CEQ $entry.Name)
        if ($expected.Count -ne 1) { throw 'Unexpected package identity.' }
        $target = Join-Path $feed.FullName $entry.Name
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        if ((Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected[0].sha256) {
            throw 'Package bytes changed.'
        }
    }
} finally { $zip.Dispose() }
Copy-Item (Join-Path $prepared 'reference') (Join-Path $root 'reference-source') -Recurse
Copy-Item (Join-Path $prepared 'portable') (Join-Path $root 'portable-source') -Recurse
Set-Content (Join-Path $root 'global.json') '{"sdk":{"version":"10.0.201","rollForward":"disable"}}'
$env:NUGET_PACKAGES = Join-Path $root 'nuget'
$escapedFeed = [Security.SecurityElement]::Escape($feed.FullName)
$config = @"
<configuration><packageSources><clear/><add key="producer" value="$escapedFeed"/><add key="NuGet" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="producer"><package pattern="ProGPU.*"/><package pattern="LibreWinForms.*"/></packageSource><packageSource key="NuGet"><package pattern="*"/></packageSource></packageSourceMapping></configuration>
"@
Set-Content (Join-Path $root 'NuGet.config') $config
Push-Location $root
try {
    dotnet build reference-source/TextRasterReference.csproj -c Release -r win-arm64 --nologo *> reference-build.log
    if ($LASTEXITCODE -ne 0) { throw 'Original reference did not compile.' }
    dotnet build portable-source/PortableTextRaster.csproj -c Release -r win-arm64 --nologo *> portable-build.log
    if ($LASTEXITCODE -ne 0) { throw 'Portable package consumer did not compile.' }
    Get-ChildItem reference-source,portable-source -Recurse -File | Get-FileHash -Algorithm SHA256 |
        ConvertTo-Json -Depth 5 | Set-Content payload-hashes.json
    Copy-Item (Join-Path $prepared 'producer.json') producer.json
    function Invoke-Bounded([string]$executable, [string[]]$arguments, [string]$name) {
        $child = Start-Process $executable -ArgumentList $arguments -NoNewWindow -PassThru `
            -RedirectStandardOutput (Join-Path $root "$name-stdout.log") -RedirectStandardError (Join-Path $root "$name-stderr.log")
        if (-not $child.WaitForExit(60000)) {
            $child.Kill($true); $child.WaitForExit()
            throw "$name exceeded its original 60-second controller deadline."
        }
        $child.Refresh()
        if ($child.ExitCode -ne 0) { throw "$name exited $($child.ExitCode)." }
    }
    $original = Join-Path $root 'original'
    $portable = Join-Path $root 'portable'
    $fontRoot = Join-Path $env:RUNNER_TEMP 'text-raster-font-inputs'
    if (Test-Path $fontRoot) { throw 'Refusing to adopt an existing font-transfer directory.' }
    try {
        Invoke-Bounded (Join-Path $root 'reference-source/bin/Release/net10.0-windows/win-arm64/TextRasterReference.exe') @('"' + $original + '"') 'original'
        Invoke-Bounded (Join-Path $root 'portable-source/bin/Release/net10.0/win-arm64/PortableTextRaster.exe') @('"' + $portable + '"', '"' + $original + '"') 'portable'
    } finally {
        # Exact same-run native font bytes never leave this ephemeral runner.
        if (Test-Path $fontRoot) { Remove-Item $fontRoot -Recurse -Force }
    }
} finally { Pop-Location }
