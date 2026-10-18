<#
.SYNOPSIS
  Downloads the pinned libmpv build into lib/ and verifies its SHA-256.

.DESCRIPTION
  The pin lives in scripts/libmpv.json. It points at an LGPL build (mpv -Dgpl=false, FFmpeg without
  --enable-gpl/--enable-nonfree), so the same DLL is fine for development, CI and releases.
  With an empty "tag" the latest matching release is resolved and the values to pin are printed;
  use that only to bump the pin, never in a release build.

.EXAMPLE
  pwsh scripts/get-libmpv.ps1
#>
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..' 'lib'),
    [switch]$Force,
    # Release builds: refuse to resolve "latest" when the pin is missing.
    [switch]$RequirePin
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is many times slower with the progress bar

$pin = Get-Content (Join-Path $PSScriptRoot 'libmpv.json') -Raw | ConvertFrom-Json
$dll = Join-Path $Destination 'libmpv-2.dll'
if ((Test-Path $dll) -and -not $Force) { Write-Host "libmpv already present: $dll (use -Force to replace)"; return }

$tag = $pin.tag; $asset = $pin.asset
if (-not $tag) {
    if ($RequirePin) { throw 'scripts/libmpv.json has no pinned tag/asset/sha256' }
    $headers = @{ 'User-Agent' = 'nuvora-build' }
    if ($env:GH_TOKEN) { $headers.Authorization = "Bearer $env:GH_TOKEN" }
    $release = Invoke-RestMethod "https://api.github.com/repos/$($pin.repo)/releases/latest" -Headers $headers
    $match = $release.assets | Where-Object { $_.name -match $pin.assetPattern } | Select-Object -First 1
    if (-not $match) { throw "No asset matching '$($pin.assetPattern)' in $($pin.repo)@$($release.tag_name): $($release.assets.name -join ', ')" }
    $tag = $release.tag_name; $asset = $match.name
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) "libmpv-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory $tmp | Out-Null
try {
    $archive = Join-Path $tmp $asset
    $url = "https://github.com/$($pin.repo)/releases/download/$tag/$asset"
    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $archive
    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()

    if ($pin.sha256) {
        if ($hash -ne $pin.sha256.ToLowerInvariant()) { throw "SHA-256 mismatch for ${asset}: expected $($pin.sha256), got $hash" }
    } else {
        $msg = "libmpv is not pinned. Pin it in scripts/libmpv.json: tag=$tag asset=$asset sha256=$hash"
        if ($env:GITHUB_ACTIONS) { Write-Host "::warning::$msg" } else { Write-Warning $msg }
    }

    # 7-Zip is on the GitHub runners; Windows 11's bsdtar reads .7z as well.
    $out = Join-Path $tmp 'x'
    if (Get-Command 7z -ErrorAction SilentlyContinue) { 7z e $archive libmpv-2.dll "-o$out" -y | Out-Null }
    else { New-Item -ItemType Directory $out | Out-Null; tar -xf $archive -C $out libmpv-2.dll }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $out 'libmpv-2.dll'))) { throw "libmpv-2.dll not found in $asset" }

    New-Item -ItemType Directory $Destination -Force | Out-Null
    Copy-Item (Join-Path $out 'libmpv-2.dll') $dll -Force
    Write-Host "libmpv-2.dll ($tag) -> $dll"
} finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
