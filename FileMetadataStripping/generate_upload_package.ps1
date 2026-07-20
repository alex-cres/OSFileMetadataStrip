# Publishes the library for linux-x64 (the ODC container runtime) and packages
# it into ExternalLibrary.zip ready for upload to ODC Portal.
#
# ODC Portal limit: the ZIP must not exceed 90 MB.
# The linux-x64 targeted publish is used deliberately to include only the
# native binaries required by the ODC runtime and avoid bundling Windows/macOS
# natives from Magick.NET, which would push the ZIP well over the limit.
#
# Usage (from the repo root or the FileMetadataStripping/ folder):
#   .\FileMetadataStripping\generate_upload_package.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectDir = $PSScriptRoot                    # FileMetadataStripping/
$repoRoot   = Split-Path $projectDir -Parent
$publishDir = Join-Path $projectDir "bin\Release\net10.0\linux-x64\publish"
$zipPath    = Join-Path $repoRoot "ExternalLibrary.zip"
$limitMB    = 90

Write-Host "Publishing for linux-x64..." -ForegroundColor Cyan
Push-Location $projectDir
try {
    dotnet publish -c Release -r linux-x64 --no-self-contained
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
} finally {
    Pop-Location
}

Write-Host "Packaging $publishDir -> $zipPath ..." -ForegroundColor Cyan
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

$sizeMB = [Math]::Round((Get-Item $zipPath).Length / 1MB, 2)
if ($sizeMB -gt $limitMB) {
    Write-Host "ZIP is $sizeMB MB - exceeds the $limitMB MB ODC Portal limit!" -ForegroundColor Red
    Write-Host "Investigate the largest contributors:" -ForegroundColor Yellow
    Write-Host "  [System.IO.Compression.ZipFile]::OpenRead('$zipPath').Entries | Sort-Object Length -Descending | Select-Object -First 20 | Format-Table Name, @{N='MB';E={[math]::Round(`$_.Length/1MB,2)}}" -ForegroundColor Yellow
    exit 1
} else {
    Write-Host "ZIP created: $zipPath  ($sizeMB MB / $limitMB MB limit)" -ForegroundColor Green
    Write-Host "Upload this file to ODC Portal -> External Logic." -ForegroundColor Green
}
