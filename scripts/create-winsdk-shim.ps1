# Creates the `localwinsdk` shim used by OutlookMeetingDock.csproj so the
# project builds on machines where the Windows SDK 10.0.26100 is not
# installed. The Command Palette Extensions Toolkit 0.12 requires the
# net10.0-windows10.0.26100.0 TFM, which normally pulls in that SDK; the shim
# reuses the platform props, union metadata and packaging tools from an older
# installed SDK, renamed to the 10.0.26100.0 version.
#
# Run once from the repository root:
#   powershell -ExecutionPolicy Bypass -File scripts\create-winsdk-shim.ps1

param(
    # SDK version to copy the tooling from (any installed UAP SDK works).
    [string]$SourceVersion = "10.0.22000.0"
)

$ErrorActionPreference = "Stop"

$kitsRoot = "C:\Program Files (x86)\Windows Kits\10"
$sourceBin = Join-Path $kitsRoot "bin\$SourceVersion\x64"
$sourcePlatform = Join-Path $kitsRoot "Platforms\UAP\$SourceVersion"
$sourceUnionMetadata = Join-Path $kitsRoot "UnionMetadata\$SourceVersion"
$sourceUapProps = Join-Path $kitsRoot "DesignTime\CommonConfiguration\Neutral\UAP\$SourceVersion\UAP.props"

foreach ($path in @($sourceBin, $sourcePlatform, $sourceUnionMetadata, $sourceUapProps)) {
    if (-not (Test-Path $path)) {
        Write-Error "Source not found: $path. Install a Windows SDK (any 10.0.x with UAP support) or pass -SourceVersion."
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$shim = Join-Path $repoRoot "localwinsdk"
$targetVersion = "10.0.26100.0"

$platformDir = Join-Path $shim "Platforms\UAP\$targetVersion"
$uapPropsDir = Join-Path $shim "DesignTime\CommonConfiguration\Neutral\UAP\$targetVersion"
$binDir = Join-Path $shim "bin\$targetVersion\x64"
$unionMetadataDir = Join-Path $shim "UnionMetadata\$targetVersion"

New-Item -ItemType Directory -Force -Path $platformDir, $uapPropsDir, $binDir, $unionMetadataDir | Out-Null

Copy-Item "$sourcePlatform\*" $platformDir -Force
(Get-Content $sourceUapProps) -replace [regex]::Escape($SourceVersion), $targetVersion |
    Set-Content (Join-Path $uapPropsDir "UAP.props")

# Union metadata (Windows.winmd) for the CsWinRT -input override.
Copy-Item "$sourceUnionMetadata\*" $unionMetadataDir -Force

# Packaging tools. MakePri needs the MrmSupport.dll sidecar; signtool and
# makeappx are invoked directly by the MSIX targets.
foreach ($file in @("makepri.exe", "makeappx.exe", "signtool.exe", "mrmsupport.dll",
                    "Microsoft.Windows.Build.Appx.AppxPackaging.dll.manifest")) {
    Copy-Item (Join-Path $sourceBin $file) $binDir -Force
}

Write-Host "Shim SDK created at $shim"
