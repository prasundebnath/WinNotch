# WinNotch - Build and Package Script
# Usage: .\build_and_package.ps1
# Output: dist\WinNotch.exe (standalone) and optionally dist\WinNotchSetup.exe

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  WinNotch - Build and Package" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: dotnet publish
Write-Host "[1/3] Publishing self-contained single-file EXE..." -ForegroundColor Yellow

$publishArgs = @(
    "publish"
    "-c", "Release"
    "-r", "win-x64"
    "--self-contained", "true"
    "-p:PublishSingleFile=true"
    "-p:EnableCompressionInSingleFile=true"
    "-p:IncludeNativeLibrariesForSelfExtract=true"
    "-p:DebugType=none"
    "-p:DebugSymbols=false"
)

Push-Location $projectRoot
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] dotnet publish failed." -ForegroundColor Red
    exit 1
}
Pop-Location

$publishDir = Join-Path $projectRoot "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
$exePath    = Join-Path $publishDir "WinNotch.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "[ERROR] Published EXE not found at: $exePath" -ForegroundColor Red
    exit 1
}

$sizeBytes = (Get-Item $exePath).Length
$sizeMB = [math]::Round($sizeBytes / 1048576, 1)
Write-Host ""
Write-Host "[OK] Published EXE: $exePath (${sizeMB} MB)" -ForegroundColor Green

# Step 2: Copy standalone EXE to dist\
Write-Host ""
Write-Host "[2/3] Copying standalone EXE to dist\ ..." -ForegroundColor Yellow

$distDir = Join-Path $projectRoot "dist"
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

Copy-Item $exePath (Join-Path $distDir "WinNotch.exe") -Force
Write-Host "[OK] Standalone EXE copied to dist\WinNotch.exe" -ForegroundColor Green

# Step 3: Inno Setup (optional)
Write-Host ""
Write-Host "[3/3] Looking for Inno Setup compiler..." -ForegroundColor Yellow

$isccPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
)

$iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host "    Found: $iscc" -ForegroundColor DarkGray
    $issFile = Join-Path $projectRoot "WinNotchSetup.iss"
    & $iscc $issFile
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Inno Setup compilation failed." -ForegroundColor Red
        exit 1
    }
    Write-Host "[OK] Installer created: dist\WinNotchSetup.exe" -ForegroundColor Green
} else {
    Write-Host "[SKIP] Inno Setup not found - skipping installer packaging." -ForegroundColor DarkYellow
    Write-Host "       Install from: https://jrsoftware.org/isdl.php" -ForegroundColor DarkYellow
    Write-Host "       Then re-run this script to also get dist\WinNotchSetup.exe" -ForegroundColor DarkYellow
}

# Done
Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Done!" -ForegroundColor Cyan
Write-Host "  Standalone EXE : dist\WinNotch.exe" -ForegroundColor White
if ($iscc) {
    Write-Host "  Installer       : dist\WinNotchSetup.exe" -ForegroundColor White
}
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""
