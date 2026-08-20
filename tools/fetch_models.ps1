$ErrorActionPreference = "Stop"

# Fetches the person-segmentation model used by camera background blur and
# replacement, pinned by hash exactly like the FFmpeg sidecar. The model is the
# MediaPipe Selfie Segmenter ONNX export (Apache-2.0):
# https://huggingface.co/onnx-community/mediapipe_selfie_segmentation

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$modelsRoot = Join-Path $projectRoot "third_party\models"
$destinationModel = Join-Path $modelsRoot "selfie_segmenter.onnx"
$destinationLicense = Join-Path $modelsRoot "LICENSE-selfie_segmenter.txt"

$modelUrl = "https://huggingface.co/onnx-community/mediapipe_selfie_segmentation/resolve/main/onnx/model.onnx"
$modelSha256 = "3241AC4AD8AA35BDAF33946776DB29F7C283A413AA0B0DACB9483594B4531AAD"
$licenseUrl = "https://www.apache.org/licenses/LICENSE-2.0.txt"
$licenseSha256 = "CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30"

if ((Test-Path -LiteralPath $destinationModel) -and (Test-Path -LiteralPath $destinationLicense)) {
    $existingHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $destinationModel).Hash
    if ($existingHash -eq $modelSha256) {
        Write-Host "Pinned segmentation model is ready." -ForegroundColor Green
        return
    }
}

New-Item -ItemType Directory -Path $modelsRoot -Force | Out-Null

Write-Host "Downloading the pinned segmentation model…" -ForegroundColor Cyan
$temporaryModel = "$destinationModel.download"
Invoke-WebRequest -Uri $modelUrl -OutFile $temporaryModel
$actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporaryModel).Hash
if ($actualHash -ne $modelSha256) {
    Remove-Item -LiteralPath $temporaryModel -Force
    throw "Segmentation model checksum mismatch. Expected $modelSha256 but received $actualHash."
}
Move-Item -LiteralPath $temporaryModel -Destination $destinationModel -Force

$temporaryLicense = "$destinationLicense.download"
Invoke-WebRequest -Uri $licenseUrl -OutFile $temporaryLicense
$actualLicenseHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporaryLicense).Hash
if ($actualLicenseHash -ne $licenseSha256) {
    Remove-Item -LiteralPath $temporaryLicense -Force
    throw "License text checksum mismatch. Expected $licenseSha256 but received $actualLicenseHash."
}
Move-Item -LiteralPath $temporaryLicense -Destination $destinationLicense -Force
Write-Host "Pinned segmentation model is ready." -ForegroundColor Green
