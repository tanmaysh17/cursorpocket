$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $projectRoot

$venvPython = Join-Path $projectRoot ".venv\Scripts\python.exe"
if (-not (Test-Path -LiteralPath $venvPython)) {
    python -m venv .venv
}

& powershell -NoProfile -ExecutionPolicy Bypass -File tools\fetch_ffmpeg.ps1
if ($LASTEXITCODE -ne 0) {
    throw "FFmpeg preparation failed with exit code $LASTEXITCODE."
}

& $venvPython -m pip install --disable-pip-version-check -r requirements-build.txt
if ($LASTEXITCODE -ne 0) {
    throw "Dependency installation failed with exit code $LASTEXITCODE."
}
& $venvPython tools\make_icon.py
if ($LASTEXITCODE -ne 0) {
    throw "Icon generation failed with exit code $LASTEXITCODE."
}
& $venvPython -m PyInstaller `
    --noconfirm `
    --clean `
    --onefile `
    --windowed `
    --name CursorPocket `
    --add-data "assets\cursorpocket-logo.png;assets" `
    --icon assets\cursorpocket.ico `
    main.py
if ($LASTEXITCODE -ne 0) {
    throw "PyInstaller failed with exit code $LASTEXITCODE. Close any running CursorPocket instance and rebuild."
}

Copy-Item -LiteralPath "third_party\ffmpeg\bin\ffmpeg.exe" -Destination "dist\ffmpeg.exe" -Force
Copy-Item -LiteralPath "third_party\ffmpeg\LICENSE.txt" -Destination "dist\FFMPEG-LICENSE.txt" -Force
Copy-Item -LiteralPath "THIRD_PARTY_NOTICES.md" -Destination "dist\THIRD_PARTY_NOTICES.md" -Force

Write-Host ""
Write-Host "Built: $projectRoot\dist\CursorPocket.exe" -ForegroundColor Cyan
Write-Host "Video sidecar: $projectRoot\dist\ffmpeg.exe" -ForegroundColor Cyan
