$ErrorActionPreference = "Stop"

# Regenerates the bundled camera replacement backgrounds. Deliberately quiet,
# dark, and gradient-only — photographic or decorative backgrounds are outside
# the instrument aesthetic (DESIGN.md). Output is committed; run this only when
# changing the set.

Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outputDir = Join-Path $projectRoot "native\CursorPocket.App\Assets\Backgrounds"
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$width = 1280
$height = 720

# name, top color, bottom color (dark neutrals with a hint of hue)
$backgrounds = @(
    @("graphite", [System.Drawing.Color]::FromArgb(255, 34, 38, 42), [System.Drawing.Color]::FromArgb(255, 16, 18, 21)),
    @("slate", [System.Drawing.Color]::FromArgb(255, 30, 39, 48), [System.Drawing.Color]::FromArgb(255, 13, 18, 24)),
    @("moss", [System.Drawing.Color]::FromArgb(255, 26, 40, 34), [System.Drawing.Color]::FromArgb(255, 11, 19, 16))
)

foreach ($background in $backgrounds) {
    $name = $background[0]
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
    # A slight diagonal keeps the gradient from reading as a flat band behind the person.
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $background[1], $background[2], 105.0)
    $graphics.FillRectangle($brush, $rect)
    $brush.Dispose()
    $graphics.Dispose()
    $path = Join-Path $outputDir "$name.png"
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Host "Wrote $path"
}
