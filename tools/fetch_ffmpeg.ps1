$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$thirdPartyRoot = Join-Path $projectRoot "third_party\ffmpeg"
$binaryDir = Join-Path $thirdPartyRoot "bin"
$cacheDir = Join-Path $thirdPartyRoot "cache"
$destinationExe = Join-Path $binaryDir "ffmpeg.exe"
$destinationLicense = Join-Path $thirdPartyRoot "LICENSE.txt"

$archiveName = "ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-8.1.zip"
$archiveUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-08-17-13-05/$archiveName"
$archiveSha256 = "BDA492675BDB354AC55F93B96AF2DBB35BABEF7DE264C37D4FF83E022831B19D"
$executableSha256 = "E8E106D6F6A4166747FBD7374FBF47FFC4D2DD883520C3558FEEAC0281A2712D"

function Assert-FfmpegCapabilities([string]$executablePath) {
    $version = (& $executablePath -hide_banner -version 2>&1 | Out-String).ToLowerInvariant()
    if ($version.Contains("--enable-gpl") -or $version.Contains("--enable-nonfree")) {
        throw "The pinned FFmpeg executable is not an LGPL-only build."
    }
    $devices = (& $executablePath -hide_banner -devices 2>&1 | Out-String).ToLowerInvariant()
    $filters = (& $executablePath -hide_banner -filters 2>&1 | Out-String).ToLowerInvariant()
    $encoders = (& $executablePath -hide_banner -encoders 2>&1 | Out-String).ToLowerInvariant()
    $muxers = (& $executablePath -hide_banner -muxers 2>&1 | Out-String).ToLowerInvariant()
    $checks = @(
        @("DirectShow input", $devices.Contains("dshow")),
        @("GDI screen input", $devices.Contains("gdigrab")),
        @("Desktop Duplication filter", $filters.Contains("ddagrab")),
        @("Media Foundation H.264 encoder", $encoders.Contains("h264_mf")),
        @("AAC encoder", $encoders.Contains("aac")),
        @("MP4 muxer", $muxers.Contains("mp4"))
    )
    foreach ($check in $checks) {
        if (-not $check[1]) {
            throw "The pinned FFmpeg executable is missing: $($check[0])."
        }
    }
}

if (Test-Path -LiteralPath $destinationExe) {
    $existingHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $destinationExe).Hash
    if ($existingHash -eq $executableSha256 -and (Test-Path -LiteralPath $destinationLicense)) {
        Assert-FfmpegCapabilities $destinationExe
        Write-Host "Pinned LGPL FFmpeg is ready." -ForegroundColor Green
        return
    }
}

New-Item -ItemType Directory -Path $binaryDir -Force | Out-Null
New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
$archivePath = Join-Path $cacheDir $archiveName
if (-not (Test-Path -LiteralPath $archivePath)) {
    Write-Host "Downloading the pinned LGPL FFmpeg build…" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath
}

$actualArchiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash
if ($actualArchiveHash -ne $archiveSha256) {
    throw "FFmpeg archive checksum mismatch. Expected $archiveSha256 but received $actualArchiveHash."
}

$extractDir = Join-Path $cacheDir ("extract-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $extractDir | Out-Null
Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDir
$sourceExe = Get-ChildItem -LiteralPath $extractDir -Recurse -File -Filter "ffmpeg.exe" |
    Select-Object -First 1
$sourceLicense = Get-ChildItem -LiteralPath $extractDir -Recurse -File -Filter "LICENSE.txt" |
    Select-Object -First 1
if (-not $sourceExe -or -not $sourceLicense) {
    throw "The verified FFmpeg archive did not contain the expected executable and license."
}

$actualExecutableHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourceExe.FullName).Hash
if ($actualExecutableHash -ne $executableSha256) {
    throw "FFmpeg executable checksum mismatch. Expected $executableSha256 but received $actualExecutableHash."
}
Copy-Item -LiteralPath $sourceExe.FullName -Destination $destinationExe -Force
Copy-Item -LiteralPath $sourceLicense.FullName -Destination $destinationLicense -Force
Assert-FfmpegCapabilities $destinationExe
Write-Host "Pinned LGPL FFmpeg is ready." -ForegroundColor Green
