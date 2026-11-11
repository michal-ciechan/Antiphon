#requires -Version 7.0
# CARD-1838. Last Merge step before DerivedImpactIndexTests and the push.
# Calls DerivedImpactIndexBuilder.Build and DerivedImpactIndexBuilder.Write
# (scripts/regenerate-derived-impact-index/Program.cs). Prints whether
# tests/derived-impact-index.json bytes changed. Commits nothing.
# Exit 0 when the bytes changed or stayed the same. Exit 1 when the build fails.
# Merge does not pass -NoBuild. -NoBuild runs an already-built driver.
param(
    [string]$Repo,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest

$Checkout = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($Repo)) { $Repo = $Checkout }
$Repo = [IO.Path]::GetFullPath($Repo)
$Project = Join-Path $PSScriptRoot 'regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj'
if (-not [IO.File]::Exists($Project)) { Write-Error "missing $Project"; exit 1 }

if (-not $NoBuild) {
    $build = @('build', $Project, '-p:UseAppHost=false', '--nologo')
    $cap = [Environment]::GetEnvironmentVariable('ANTIPHON_BUILD_SLOT_MAXCPUCOUNT')
    if (-not [string]::IsNullOrWhiteSpace($cap)) {
        $build += ('-maxcpucount:' + $cap)
    }
    & dotnet @build
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

& dotnet run --project $Project --no-build -p:UseAppHost=false -- $Repo
if ($LASTEXITCODE -ne 0) { exit 1 }
exit 0
