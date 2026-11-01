$ErrorActionPreference = "Stop"

# Repository paths
$RepoRoot = $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "AmbientVenGen.Desktop\AmbientVenGen.Desktop.csproj"

$ArtifactsRoot = Join-Path $RepoRoot "artifacts"
$PublishDir = Join-Path $ArtifactsRoot "publish-win-x64"
$ReleaseDir = Join-Path $ArtifactsRoot "release"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " AmbientVenGen - Release Builder"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check project file
if (-not (Test-Path $ProjectPath)) {
    throw "Project file not found: $ProjectPath"
}

# Read version from csproj
[xml]$ProjectXml = Get-Content $ProjectPath -Raw
$VersionNode = $ProjectXml.SelectSingleNode("/Project/PropertyGroup/Version")

if ($null -eq $VersionNode -or [string]::IsNullOrWhiteSpace($VersionNode.InnerText)) {
    throw "Version is not specified in the project file. Add <Version>1.0.0</Version>."
}

$Version = $VersionNode.InnerText.Trim()

if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "Invalid version format: $Version"
}

Write-Host "Version: $Version" -ForegroundColor Green
Write-Host "Target:  Windows x64" 
Write-Host "Mode:    Framework-dependent"
Write-Host "Output:  Single executable"
Write-Host ""

# Check .NET SDK
dotnet --version | Out-Null

if ($LASTEXITCODE -ne 0) {
    throw ".NET SDK was not found. Install the .NET 10 SDK."
}

# Prepare output directories
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

if (Test-Path $ReleaseDir) {
    Remove-Item $ReleaseDir -Recurse -Force
}

New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

# Publish
Write-Host "Publishing application..." -ForegroundColor Cyan
Write-Host ""

dotnet publish $ProjectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $PublishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed."
}

# Find executable
$Executables = @(Get-ChildItem $PublishDir -Filter "*.exe" -File)

if ($Executables.Count -ne 1) {
    throw "Expected exactly one executable, found $($Executables.Count)."
}

# Copy executable to release directory
$ExeName = "AmbientVenGen-v$Version-win-x64.exe"
$FinalExe = Join-Path $ReleaseDir $ExeName

Copy-Item $Executables[0].FullName $FinalExe -Force

# Result
$FileInfo = Get-Item $FinalExe
$SizeMB = [math]::Round($FileInfo.Length / 1MB, 2)

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Release successfully created!"
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Version: $Version"
Write-Host "File:    $FinalExe"
Write-Host "Size:    $SizeMB MB"
Write-Host ""
Write-Host "You can now upload this file to GitHub Releases."
Write-Host ""