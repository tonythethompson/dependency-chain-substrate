# Install or update the global `dcs` tool from a GitHub Release .nupkg.
# Usage: .\scripts\install-dcs-from-release.ps1 [-Version 0.1.1] [-Repo tonythethompson/dependency-chain-substrate]

param(
    [string]$Version = "0.1.1",
    [string]$Repo = "tonythethompson/dependency-chain-substrate"
)

$ErrorActionPreference = "Stop"
$PackageId = "DependencyChainSubstrate.Cli"
$DownloadDir = Join-Path $env:TEMP "dcs-nupkg-$Version"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required. Install from https://cli.github.com/"
}

New-Item -ItemType Directory -Force -Path $DownloadDir | Out-Null
Write-Host "Downloading $PackageId $Version from GitHub Release v$Version ..."
gh release download "v$Version" -R $Repo -p "*.nupkg" -D $DownloadDir --clobber

$nupkg = Get-ChildItem -Path $DownloadDir -Filter "*.nupkg" | Select-Object -First 1
if (-not $nupkg) {
    throw "No .nupkg found after download in $DownloadDir"
}

$installed = dotnet tool list --global | Select-String -Pattern "dependencychainsubstrate\.cli" -Quiet
$verb = if ($installed) { "update" } else { "install" }

Write-Host "Running: dotnet tool $verb --global $PackageId --add-source $DownloadDir --version $Version"
dotnet tool $verb --global $PackageId --add-source $DownloadDir --version $Version

Write-Host ""
Write-Host "Installed:"
dotnet tool list --global | Select-String -Pattern "dependencychainsubstrate|dcs"
Write-Host ""
Write-Host "Run: dcs --help"
