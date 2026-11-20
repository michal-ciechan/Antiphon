#requires -Version 7.0
# CARD-2009: a renamed literal owner path must not silently disable its row.
param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'lib/rc-input-manifest.ps1')
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$json=[IO.File]::ReadAllText((Join-Path $root 'scripts/lib/rc-input-owners.json'))
$missing=@(Get-RcMissingOwnerLiteralPaths -RepositoryRoot $root -MapJson $json)
if ($missing.Count -gt 0) {
    foreach ($line in $missing) { Write-Output $line }
    exit 1
}
Write-Output 'missing-literal-path=0'
exit 0
