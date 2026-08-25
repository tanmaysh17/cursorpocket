param(
    [switch]$SkipTaskbarPin,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $projectRoot "artifacts\CursorPocket-win-x64"
$sourceExe = Join-Path $sourceRoot "CursorPocket.exe"
if (-not (Test-Path -LiteralPath $sourceExe) -or -not (Test-Path -LiteralPath (Join-Path $sourceRoot "ffmpeg.exe"))) {
    throw "Build CursorPocket first: powershell -ExecutionPolicy Bypass -File .\native\build-native.ps1"
}

$installDir = Join-Path $env:LOCALAPPDATA "Programs\CursorPocket"
$installedExe = Join-Path $installDir "CursorPocket.exe"
$installedIcon = Join-Path $installDir "Assets\AppIcon.ico"
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenuDir "CursorPocket.lnk"
$managedPaths = @($sourceExe, $installedExe)

$managedProcesses = Get-CimInstance Win32_Process |
    Where-Object {
        $_.Name -eq "CursorPocket.exe" -and
        $_.ExecutablePath -in $managedPaths
    }
$managedProcessIds = @($managedProcesses | ForEach-Object { $_.ProcessId })
$managedProcessIds | ForEach-Object {
    Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue
}
$managedProcessIds | ForEach-Object {
    Wait-Process -Id $_ -Timeout 10 -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
$copyDeadline = [DateTime]::UtcNow.AddSeconds(5)
while ($true) {
    try {
        Copy-Item -Path (Join-Path $sourceRoot "*") -Destination $installDir -Recurse -Force
        break
    } catch [System.IO.IOException] {
        if ([DateTime]::UtcNow -ge $copyDeadline) {
            throw
        }
        Start-Sleep -Milliseconds 250
    }
}

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = "$installedIcon,0"
$shortcut.Description = "Capture screenshots, screen walkthroughs, audio, selected text, and webpages"
$shortcut.Save()

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
New-Item -Path $runKey -Force | Out-Null
New-ItemProperty `
    -Path $runKey `
    -Name "CursorPocket" `
    -Value ('"{0}" --background' -f $installedExe) `
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
Write-Host "Windows may initially place the CursorPocket tray icon under the ^ overflow menu."
