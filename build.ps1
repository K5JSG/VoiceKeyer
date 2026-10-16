<#
.SYNOPSIS
    Builds Voice Keyer.

.DESCRIPTION
    Runs the tests, publishes a self-contained single-file exe (no .NET runtime
    needed on the target PC) together with the Piper speech engine and the two
    stock voices (male and female) it ships with, and, if Inno Setup is installed, compiles the installer.

    Output:
        publish\VoiceKeyer.exe, publish\piper\, publish\voices\   the published app
        dist\VoiceKeyer-v<version>-setup.exe                      the installer

.PARAMETER Version
    Version stamped into the exe and the installer filename. Defaults to
    <Version> in src\VoiceKeyer\VoiceKeyer.csproj.

.PARAMETER SkipInstaller
    Publish only; do not build the installer.

.PARAMETER SkipTests
    Do not run the tests first.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Version 1.1.0
#>

[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipInstaller,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$solution = Join-Path $root "VoiceKeyer.slnx"
$project = Join-Path $root "src\VoiceKeyer\VoiceKeyer.csproj"
$publishDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"

if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "No -Version given and no <Version> found in $project." }
}

Write-Host ""
Write-Host "Voice Keyer - build v$Version" -ForegroundColor Cyan
Write-Host ("=" * 40)

# --- 1. Test ---------------------------------------------------------------

if (-not $SkipTests) {
    Write-Host ""
    Write-Host "Running tests..." -ForegroundColor Yellow
    dotnet test $solution -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

# --- 2. Publish ------------------------------------------------------------

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host ""
Write-Host "Publishing self-contained exe..." -ForegroundColor Yellow

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:Version=$Version `
    -p:FileVersion="$Version.0" `
    -p:AssemblyVersion="$Version.0" `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $publishDir "VoiceKeyer.exe"
if (-not (Test-Path $exe)) { throw "Published exe not found at $exe" }

# The installer ships the exe plus the piper\ and voices\ folders. A native DLL
# left next to the exe (WPF's UI Automation needs four) would be missing on an
# installed PC and crash the app at startup. IncludeNativeLibrariesForSelfExtract
# bundles them; this fails the build if one ever slips out again.
$looseDlls = Get-ChildItem $publishDir -Filter *.dll
if ($looseDlls) { throw "Loose DLLs next to the exe would not be installed: $($looseDlls.Name -join ', ')" }

$stockVoices = "en_US-joe-medium", "en_US-ljspeech-medium"   # male, female: both freely redistributable (CC0 / public domain)
foreach ($needed in @("piper\piper.exe") + ($stockVoices | ForEach-Object { "voices\$_.onnx"; "voices\$_.onnx.json" })) {
    if (-not (Test-Path (Join-Path $publishDir $needed))) { throw "Missing from the published app: $needed" }
}

# A personal trained voice must never ship. Only the stock Piper voices may be in the package.
$extra = Get-ChildItem (Join-Path $publishDir "voices") -File | Where-Object { $n = $_.Name; -not ($stockVoices | Where-Object { $n -like "$_.onnx*" }) }
if ($extra) { throw "Unexpected files in voices\ (a personal voice must never be packaged): $($extra.Name -join ', ')" }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "  $exe  ($sizeMb MB)" -ForegroundColor Green

# --- 3. Installer ------------------------------------------------------------

if ($SkipInstaller) {
    Write-Host ""
    Write-Host "Skipping installer (-SkipInstaller)." -ForegroundColor DarkGray
    exit 0
}

$iscc = @(
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup was not found, so no installer was built." -ForegroundColor Yellow
    Write-Host "Install it from https://jrsoftware.org/isdl.php and run this again," -ForegroundColor Yellow
    Write-Host "or run publish\VoiceKeyer.exe from the publish folder." -ForegroundColor Yellow
    exit 0
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Get-ChildItem -Path $distDir -Filter "VoiceKeyer-*-setup.exe" -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

Write-Host ""
Write-Host "Building installer..." -ForegroundColor Yellow

& $iscc "/DMyAppVersion=$Version" (Join-Path $root "installer\VoiceKeyer.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

$setup = Join-Path $distDir "VoiceKeyer-v$Version-setup.exe"
if (-not (Test-Path $setup)) { throw "Installer not found at $setup" }

$setupMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host ""
Write-Host "  $setup  ($setupMb MB)" -ForegroundColor Green
Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
Write-Host ""
