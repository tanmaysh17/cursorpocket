param(
    [switch]$SkipTaskbarPin,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceExe = Join-Path $projectRoot "dist\CursorPocket.exe"
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "Build CursorPocket first: powershell -ExecutionPolicy Bypass -File .\build.ps1"
}

$installDir = Join-Path $env:LOCALAPPDATA "Programs\CursorPocket"
$installedExe = Join-Path $installDir "CursorPocket.exe"
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenuDir "CursorPocket.lnk"
$managedPaths = @($sourceExe, $installedExe)

Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -eq "CursorPocket.exe" -and
        $_.ExecutablePath -in $managedPaths
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
    }

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = "$installedExe,0"
$shortcut.Description = "Capture screenshots, audio, selected text, and webpages"
$shortcut.Save()

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
New-Item -Path $runKey -Force | Out-Null
New-ItemProperty `
    -Path $runKey `
    -Name "CursorPocket" `
    -Value ('"{0}"' -f $installedExe) `
    -PropertyType String `
    -Force | Out-Null

$taskbarPinned = $false
if (-not $SkipTaskbarPin) {
    try {
        $shellApp = New-Object -ComObject Shell.Application
        $shortcutFolder = $shellApp.Namespace($startMenuDir)
        $shortcutItem = $shortcutFolder.ParseName("CursorPocket.lnk")
        $pinVerb = $shortcutItem.Verbs() |
            Where-Object { $_.Name.Replace("&", "") -match "^Pin to taskbar$" } |
            Select-Object -First 1
        if ($pinVerb) {
            $pinVerb.DoIt()
            $taskbarPinned = $true
        }
    } catch {
        $taskbarPinned = $false
    }
}

if (-not $NoLaunch) {
    Start-Process -FilePath $installedExe -WorkingDirectory $installDir -WindowStyle Hidden
}

Write-Host ""
Write-Host "CursorPocket installed: $installedExe" -ForegroundColor Cyan
Write-Host "Starts with Windows: enabled" -ForegroundColor Green
Write-Host "Notification-area icon: enabled while CursorPocket is running" -ForegroundColor Green
if ($taskbarPinned) {
    Write-Host "Taskbar shortcut: pinned" -ForegroundColor Green
} else {
    Write-Host "Taskbar shortcut: Windows requires a manual pin" -ForegroundColor Yellow
    Write-Host "Open Start, search CursorPocket, right-click it, and choose Pin to taskbar."
}
Write-Host "Windows may initially place the green tray icon under the ^ overflow menu."
