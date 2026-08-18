[CmdletBinding()]
param(
    [switch]$SkipRestore,
    [switch]$SkipTests,
    [switch]$SkipFfmpeg,
    [switch]$RequireInstaller
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $PSScriptRoot "CursorPocket.Native.sln"
$appProject = Join-Path $PSScriptRoot "CursorPocket.App\CursorPocket.App.csproj"
$testsProject = Join-Path $PSScriptRoot "CursorPocket.Tests\CursorPocket.Tests.csproj"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishRoot = Join-Path $artifactsRoot "CursorPocket-win-x64"
$portableArchive = Join-Path $artifactsRoot "CursorPocket-portable-win-x64.zip"

function Resolve-Dotnet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $local = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $local) { return $local }
    throw ".NET 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0"
}

function Remove-ArtifactPath([string]$path) {
    $resolvedArtifacts = [IO.Path]::GetFullPath($artifactsRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedTarget = [IO.Path]::GetFullPath($path)
    if (-not $resolvedTarget.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the repository artifacts directory: $resolvedTarget"
    }
    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

$dotnet = Resolve-Dotnet
if (-not $SkipFfmpeg) {
    & (Join-Path $repoRoot "tools\fetch_ffmpeg.ps1")
}
if (-not $SkipRestore) {
    & $dotnet restore $solution -p:RuntimeIdentifier=win-x64
    if ($LASTEXITCODE -ne 0) { throw "Native dependency restore failed." }
}
if (-not $SkipTests) {
    & $dotnet test $testsProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Native tests failed." }
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
Remove-ArtifactPath $publishRoot
if (Test-Path -LiteralPath $portableArchive) { Remove-Item -LiteralPath $portableArchive -Force }

& $dotnet publish $appProject -c Release -r win-x64 --self-contained true --no-restore `
    -p:PublishTrimmed=false -p:PublishReadyToRun=false -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw "Native publish failed." }

# Unpackaged WinUI publish can omit compiled XAML resources even though they
# are present in TargetDir. An installer without these files starts and then
# exits with XamlParseException, so stage and verify them explicitly.
$targetDir = (& $dotnet msbuild $appProject -nologo -getProperty:TargetDir `
    -p:Configuration=Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64).Trim()
if (-not (Test-Path -LiteralPath $targetDir)) {
    throw "Native build output was not found: $targetDir"
}
Get-ChildItem -LiteralPath $targetDir -Filter "*.xbf" -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $publishRoot $_.Name) -Force
}
Get-ChildItem -LiteralPath $targetDir -Filter "*.pri" -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $publishRoot $_.Name) -Force
}
$compiledAssets = Join-Path $targetDir "Assets"
if (Test-Path -LiteralPath $compiledAssets) {
    Copy-Item -LiteralPath $compiledAssets -Destination $publishRoot -Recurse -Force
}
$requiredWinUiResources = @("App.xbf", "MainWindow.xbf", "MainPage.xbf", "CursorPocket.pri", "Assets\AppIcon.ico")
foreach ($resource in $requiredWinUiResources) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $resource))) {
        throw "Published WinUI resource is missing: $resource"
    }
}

$ffmpegRoot = Join-Path $repoRoot "third_party\ffmpeg"
Copy-Item -LiteralPath (Join-Path $ffmpegRoot "bin\ffmpeg.exe") -Destination (Join-Path $publishRoot "ffmpeg.exe") -Force
Copy-Item -LiteralPath (Join-Path $ffmpegRoot "LICENSE.txt") -Destination (Join-Path $publishRoot "FFMPEG-LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "THIRD_PARTY_NOTICES.md") -Destination (Join-Path $publishRoot "THIRD_PARTY_NOTICES.md") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $publishRoot "README.md") -Force

$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
$licenseRoot = Join-Path $publishRoot "licenses"
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
$packageNotices = @(
    @{ Package = "microsoft.windowsappsdk"; Version = "2.4.0"; Files = @("license.txt", "NOTICE.txt") },
    @{ Package = "communitytoolkit.mvvm"; Version = "8.4.2"; Files = @("License.md", "ThirdPartyNotices.txt") },
    @{ Package = "naudio"; Version = "2.2.1"; Files = @("license.txt") },
    @{ Package = "system.drawing.common"; Version = "8.0.20"; Files = @("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT") }
)
foreach ($packageNotice in $packageNotices) {
    foreach ($file in $packageNotice.Files) {
        $source = Join-Path $nugetRoot "$($packageNotice.Package)\$($packageNotice.Version)\$file"
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing package notice: $source" }
        $destinationName = "$($packageNotice.Package)-$($packageNotice.Version)-$($file.Replace('.txt', '').Replace('.TXT', '').Replace('.md', ''))" + [IO.Path]::GetExtension($file)
        Copy-Item -LiteralPath $source -Destination (Join-Path $licenseRoot $destinationName) -Force
    }
}

Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $portableArchive -CompressionLevel Optimal

$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$isccPath = if ($iscc) { $iscc.Source } else { $null }
if (-not $isccPath) {
    $knownIsccPaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    )
    $isccPath = $knownIsccPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ($isccPath) {
    & $isccPath (Join-Path $PSScriptRoot "installer\CursorPocket.iss")
    if ($LASTEXITCODE -ne 0) { throw "The CursorPocket installer build failed." }
}
elseif ($RequireInstaller) {
    throw "Inno Setup 6 is required to create the installer artifact."
}
else {
    Write-Warning "Inno Setup is not installed; the portable ZIP is ready, but no installer was created."
}

Write-Host "Native CursorPocket artifacts are ready in $artifactsRoot" -ForegroundColor Green
