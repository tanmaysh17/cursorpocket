[CmdletBinding()]
param(
    [switch]$SkipRestore,
    [switch]$SkipTests,
    [switch]$SkipFfmpeg,
    [switch]$SkipModels,
    [switch]$SkipInstaller,
    [switch]$SkipPortableArchive,
    [switch]$SkipMsix,
    [switch]$RequireInstaller,
    [switch]$RequireMsix
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $PSScriptRoot "CursorPocket.Native.sln"
$appProject = Join-Path $PSScriptRoot "CursorPocket.App\CursorPocket.App.csproj"
$testsProject = Join-Path $PSScriptRoot "CursorPocket.Tests\CursorPocket.Tests.csproj"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishRoot = Join-Path $artifactsRoot "CursorPocket-win-x64"
$symbolsRoot = Join-Path $artifactsRoot "CursorPocket-symbols"
$portableArchive = Join-Path $artifactsRoot "CursorPocket-portable-win-x64.zip"
$msixPath = Join-Path $artifactsRoot "CursorPocket-x64.msix"
$msixStaging = Join-Path $artifactsRoot ".msix-staging"

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
if (-not $SkipModels) {
    & (Join-Path $repoRoot "tools\fetch_models.ps1")
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
Remove-ArtifactPath $symbolsRoot
if (Test-Path -LiteralPath $portableArchive) { Remove-Item -LiteralPath $portableArchive -Force }
if (Test-Path -LiteralPath $msixPath) { Remove-Item -LiteralPath $msixPath -Force }
Remove-ArtifactPath $msixStaging

# ReadyToRun precompiles the managed code that WinUI startup would otherwise JIT
# method by method, which is the bulk of cold-start time for an unpackaged app.
# Trimming stays off: this publish also stages XAML resources and reflection-driven
# WinForms/System.Drawing paths that a trimmer cannot see.
& $dotnet publish $appProject -c Release -r win-x64 --self-contained true --no-restore `
    -p:PublishTrimmed=false -p:PublishReadyToRun=true -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw "Native publish failed." }

# Keep diagnostic symbols as a private CI artifact, never inside the friend-facing
# installer. Static import libraries and non-English framework satellites are build
# payload, not runtime dependencies for this English-only release.
New-Item -ItemType Directory -Path $symbolsRoot -Force | Out-Null
Get-ChildItem -LiteralPath $publishRoot -Filter "*.pdb" -File -Recurse | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $symbolsRoot $_.Name) -Force
    Remove-Item -LiteralPath $_.FullName -Force
}
Get-ChildItem -LiteralPath $publishRoot -Filter "*.lib" -File -Recurse | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Force
}
Get-ChildItem -LiteralPath $publishRoot -Directory | ForEach-Object {
    try {
        $culture = [Globalization.CultureInfo]::GetCultureInfo($_.Name)
        if ($culture.Name -notin @("en", "en-US")) {
            Remove-ArtifactPath $_.FullName
        }
    }
    catch [Globalization.CultureNotFoundException] {
        # Assets, licenses, and runtime folders are not cultures and stay intact.
    }
}

# Unpackaged WinUI publish can omit compiled XAML resources even though they
# are present in TargetDir. An installer without these files starts and then
# exits with XamlParseException, so stage and verify them explicitly.
$targetDir = (& $dotnet msbuild $appProject -nologo -getProperty:TargetDir `
    -p:Configuration=Release -p:RuntimeIdentifier=win-x64).Trim()
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
$requiredWinUiResources = @("App.xbf", "MainWindow.xbf", "MainPage.xbf", "OnboardingPage.xbf", "AnnotationWindow.xbf", "PinnedCaptureWindow.xbf", "CursorPocket.pri", "Assets\AppIcon.ico", "Assets\TrayReady.ico", "Assets\TrayRecording.ico", "Assets\CursorPocketLogo.png", "Assets\Backgrounds\graphite.png", "Assets\Backgrounds\slate.png", "Assets\Backgrounds\moss.png")
foreach ($resource in $requiredWinUiResources) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $resource))) {
        throw "Published WinUI resource is missing: $resource"
    }
}

$ffmpegRoot = Join-Path $repoRoot "third_party\ffmpeg"
Copy-Item -LiteralPath (Join-Path $ffmpegRoot "bin\ffmpeg.exe") -Destination (Join-Path $publishRoot "ffmpeg.exe") -Force
Copy-Item -LiteralPath (Join-Path $ffmpegRoot "LICENSE.txt") -Destination (Join-Path $publishRoot "FFMPEG-LICENSE.txt") -Force

# Segmentation model sidecar for camera background effects: same pinned-hash
# pattern as FFmpeg. The app degrades gracefully without it, but a build must
# never quietly ship without it.
$modelsRoot = Join-Path $repoRoot "third_party\models"
Copy-Item -LiteralPath (Join-Path $modelsRoot "selfie_segmenter.onnx") -Destination (Join-Path $publishRoot "selfie_segmenter.onnx") -Force
Copy-Item -LiteralPath (Join-Path $modelsRoot "LICENSE-selfie_segmenter.txt") -Destination (Join-Path $publishRoot "SELFIE-SEGMENTER-LICENSE.txt") -Force
foreach ($effectArtifact in @("selfie_segmenter.onnx", "onnxruntime.dll")) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $effectArtifact))) {
        throw "Published camera-effects artifact is missing: $effectArtifact"
    }
}
Copy-Item -LiteralPath (Join-Path $repoRoot "THIRD_PARTY_NOTICES.md") -Destination (Join-Path $publishRoot "THIRD_PARTY_NOTICES.md") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $publishRoot "README.md") -Force

$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
$licenseRoot = Join-Path $publishRoot "licenses"
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
$packageNotices = @(
    @{ Package = "microsoft.windowsappsdk"; Version = "2.4.0"; Files = @("license.txt", "NOTICE.txt") },
    @{ Package = "communitytoolkit.mvvm"; Version = "8.4.2"; Files = @("License.md", "ThirdPartyNotices.txt") },
    @{ Package = "naudio"; Version = "2.2.1"; Files = @("license.txt") },
    @{ Package = "system.drawing.common"; Version = "8.0.20"; Files = @("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT") },
    @{ Package = "microsoft.ml.onnxruntime"; Version = "1.29.0"; Files = @("LICENSE", "ThirdPartyNotices.txt") },
    @{ Package = "microsoft.ml.onnxruntime.managed"; Version = "1.29.0"; Files = @("LICENSE.txt") }
)
foreach ($packageNotice in $packageNotices) {
    foreach ($file in $packageNotice.Files) {
        $source = Join-Path $nugetRoot "$($packageNotice.Package)\$($packageNotice.Version)\$file"
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing package notice: $source" }
        $destinationName = "$($packageNotice.Package)-$($packageNotice.Version)-$($file.Replace('.txt', '').Replace('.TXT', '').Replace('.md', ''))" + [IO.Path]::GetExtension($file)
        Copy-Item -LiteralPath $source -Destination (Join-Path $licenseRoot $destinationName) -Force
    }
}

$makeAppx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter makeappx.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $makeAppx) {
    # The SDK build-tools package already restored by the native project carries
    # MakeAppx. Developer machines do not need a separate full Windows SDK install.
    $makeAppx = Get-ChildItem $nugetRoot -Filter makeappx.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'microsoft\.windows\.sdk\.buildtools.*\\x64\\makeappx\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
}
if (-not $SkipMsix -and $makeAppx) {
    Copy-Item -LiteralPath $publishRoot -Destination $msixStaging -Recurse
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "packaging\AppxManifest.xml") -Destination (Join-Path $msixStaging "AppxManifest.xml") -Force
    # MakeAppx validates the exact manifest paths before resource qualification is
    # evaluated. Keep the scale-qualified files for Windows and add base-name aliases
    # for the three paths declared by the hand-authored full-trust manifest.
    foreach ($logo in @("Square150x150Logo", "Square44x44Logo", "Wide310x150Logo", "SplashScreen")) {
        $qualifiedLogo = Join-Path $msixStaging "Assets\$logo.scale-200.png"
        $manifestLogo = Join-Path $msixStaging "Assets\$logo.png"
        if (-not (Test-Path -LiteralPath $qualifiedLogo)) {
            throw "MSIX logo is missing: $qualifiedLogo"
        }
        Copy-Item -LiteralPath $qualifiedLogo -Destination $manifestLogo -Force
    }
    & $makeAppx.FullName pack /d $msixStaging /p $msixPath /o
    if ($LASTEXITCODE -ne 0) { throw "MSIX packaging failed." }
    Remove-ArtifactPath $msixStaging
}
elseif (-not $SkipMsix -and $RequireMsix) {
    throw "The Windows SDK makeappx.exe is required to create the MSIX artifact."
}
elseif (-not $SkipMsix) {
    Write-Warning "makeappx.exe is not installed; no MSIX was created."
}

if (-not $SkipPortableArchive) {
    Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $portableArchive -CompressionLevel Optimal
}

$iscc = if ($SkipInstaller) { $null } else { Get-Command ISCC.exe -ErrorAction SilentlyContinue }
$isccPath = if ($iscc) { $iscc.Source } else { $null }
if (-not $SkipInstaller -and -not $isccPath) {
    $knownIsccPaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    )
    $isccPath = $knownIsccPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $SkipInstaller -and $isccPath) {
    & (Join-Path $PSScriptRoot "build-installer.ps1") -IsccPath $isccPath
}
elseif ($RequireInstaller) {
    throw "Inno Setup 6 is required to create the installer artifact."
}
elseif (-not $SkipInstaller) {
    Write-Warning "Inno Setup is not installed; no installer was created."
}

Write-Host "Native CursorPocket artifacts are ready in $artifactsRoot" -ForegroundColor Green
