# SteamAPI Desktop - Windows Release Packaging Script
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$solutionRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $solutionRoot "artifacts\publish\SteamAPI.Desktop-v0.1.0-win-x64"
$releasesDir = Join-Path $solutionRoot "artifacts\releases"
$zipPath = Join-Path $releasesDir "SteamAPI-Desktop-v0.1.0-win-x64.zip"
$shaPath = $zipPath + ".sha256"

Write-Host "Creating target directories..."
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null

Write-Host "Publishing SteamAPI.Desktop (Release, .NET 10, self-contained win-x64)..."
dotnet publish (Join-Path $solutionRoot "desktop\SteamAPI.Desktop\SteamAPI.Desktop.csproj") -c Release -r win-x64 --self-contained true -o $publishDir

Write-Host "Publishing companion SteamAPI backend to api subfolder (self-contained win-x64)..."
$apiPublishDir = Join-Path $publishDir "api"
dotnet publish (Join-Path $solutionRoot "SteamAPI\SteamAPI.csproj") -c Release -r win-x64 --self-contained true -o $apiPublishDir

Write-Host "Creating portable ZIP archive..."
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Computing SHA-256 checksum..."
$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
"$hash  SteamAPI-Desktop-v0.1.0-win-x64.zip" | Out-File -FilePath $shaPath -Encoding utf8

Write-Host "--------------------------------------------------"
Write-Host "SteamAPI Desktop Package Created Successfully!"
Write-Host "Archive:  $zipPath"
Write-Host "Checksum: $hash"
Write-Host "--------------------------------------------------"
