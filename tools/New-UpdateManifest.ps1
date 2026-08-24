[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,
    [Parameter(Mandatory)]
    [string]$Tag,
    [string]$OutputPath = "artifacts/update.json",
    [string]$Repository = "tanmaysh17/cursorpocket"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedInstaller = [IO.Path]::GetFullPath((Join-Path (Get-Location) $InstallerPath))
if (-not (Test-Path -LiteralPath $resolvedInstaller -PathType Leaf)) {
    throw "Installer not found: $resolvedInstaller"
}

[xml]$versionProps = Get-Content -LiteralPath (Join-Path $repoRoot "native\Version.props") -Raw
$version = [string]$versionProps.Project.PropertyGroup.CursorPocketVersion
if ($version.Contains('-')) {
    throw "Public update manifests require a stable version. Current version is '$version'."
}
if ($Tag -ne "v$version") {
    throw "Release tag '$Tag' does not match native/Version.props version '$version'."
}

$hash = (Get-FileHash -LiteralPath $resolvedInstaller -Algorithm SHA256).Hash.ToUpperInvariant()
$file = Get-Item -LiteralPath $resolvedInstaller
$manifest = [ordered]@{
    version = $version
    installer_url = "https://github.com/$Repository/releases/download/$Tag/CursorPocket-Setup-x64.exe"
    sha256 = $hash
    size_bytes = $file.Length
    minimum_windows_version = "10.0.19041"
    release_notes_url = "https://github.com/$Repository/releases/tag/$Tag"
    published_at = [DateTimeOffset]::UtcNow.ToString("o")
}

$resolvedOutput = [IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedOutput) -Force | Out-Null
$manifest | ConvertTo-Json | Set-Content -LiteralPath $resolvedOutput -Encoding utf8NoBOM
"$hash  CursorPocket-Setup-x64.exe" | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $resolvedOutput) "SHA256SUMS.txt") -Encoding ascii
