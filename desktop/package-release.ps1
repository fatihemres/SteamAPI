# SteamAPI Desktop - Windows Release Packaging Script
# Produces:
#   1. PRIMARY:  artifacts/releases/SteamAPI-Desktop-Setup-v0.1.0-win-x64.exe (+ .sha256)
#   2. OPTIONAL: artifacts/releases/SteamAPI-Desktop-v0.1.0-win-x64.zip (+ .sha256)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$solutionRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $solutionRoot "artifacts\publish\SteamAPI.Desktop-v0.1.0-win-x64"
$releasesDir = Join-Path $solutionRoot "artifacts\releases"
$zipPath = Join-Path $releasesDir "SteamAPI-Desktop-v0.1.0-win-x64.zip"
$zipShaPath = $zipPath + ".sha256"
$installerPath = Join-Path $releasesDir "SteamAPI-Desktop-Setup-v0.1.0-win-x64.exe"
$installerShaPath = $installerPath + ".sha256"
$issPath = Join-Path $PSScriptRoot "installer\SteamAPI.Desktop.iss"

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

Write-Host "Computing ZIP SHA-256 checksum..."
$zipHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
"$zipHash  SteamAPI-Desktop-v0.1.0-win-x64.zip" | Out-File -FilePath $zipShaPath -Encoding utf8

Write-Host "Resolving Inno Setup 6 compiler (ISCC.exe)..."
function Find-ISCC {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($cand in $candidates) {
        if (Test-Path $cand) { return $cand }
    }
    # On GitHub Actions Windows runner, bootstrap via chocolatey if missing
    $choco = Get-Command "choco" -ErrorAction SilentlyContinue
    if ($choco) {
        Write-Host "ISCC not found in standard paths. Installing Inno Setup via Chocolatey..."
        & choco install innosetup --no-progress -y | Out-Null
        foreach ($cand in $candidates) {
            if (Test-Path $cand) { return $cand }
        }
    }
    throw "Inno Setup compiler (ISCC.exe) could not be located. Please install Inno Setup 6."
}

$isccPath = Find-ISCC
Write-Host "Using Inno Setup compiler: $isccPath"

Write-Host "Compiling Windows Installer (Inno Setup 6)..."
if (Test-Path $installerPath) { Remove-Item -Force $installerPath }
& "$isccPath" "/DSourceDir=$publishDir" "/DOutputDir=$releasesDir" "$issPath"
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $installerPath)) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Computing Installer SHA-256 checksum..."
$installerHash = (Get-FileHash -Path $installerPath -Algorithm SHA256).Hash
"$installerHash  SteamAPI-Desktop-Setup-v0.1.0-win-x64.exe" | Out-File -FilePath $installerShaPath -Encoding utf8

Write-Host "--------------------------------------------------"
Write-Host "SteamAPI Desktop Releases Generated Successfully!"
Write-Host "PRIMARY Installer: $installerPath"
Write-Host "Checksum:          $installerHash"
Write-Host "OPTIONAL Portable: $zipPath"
Write-Host "Checksum:          $zipHash"
Write-Host "--------------------------------------------------"
