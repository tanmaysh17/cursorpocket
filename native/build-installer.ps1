[CmdletBinding()]
param(
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"
$versionPropsPath = Join-Path $PSScriptRoot "Version.props"
[xml]$versionProps = Get-Content -LiteralPath $versionPropsPath -Raw
$displayVersion = [string]$versionProps.Project.PropertyGroup.CursorPocketVersion
$fileVersion = [string]$versionProps.Project.PropertyGroup.CursorPocketFileVersion
if ([string]::IsNullOrWhiteSpace($displayVersion) -or [string]::IsNullOrWhiteSpace($fileVersion)) {
    throw "CursorPocketVersion and CursorPocketFileVersion are required in native/Version.props."
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $IsccPath = $command.Source }
}
if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $knownPaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    )
    $IsccPath = $knownPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw "Inno Setup 6 is required to create the installer artifact."
}

& $IsccPath "/DMyAppVersion=$displayVersion" "/DMyAppFileVersion=$fileVersion" (Join-Path $PSScriptRoot "installer\CursorPocket.iss")
if ($LASTEXITCODE -ne 0) { throw "The CursorPocket installer build failed." }
