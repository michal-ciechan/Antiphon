#requires -Version 5.1
# CARD-1842: write tests/Shared/expected-duration-roster.json from scripts/lib/nightly-class-costs.json.
# ASCII-only. The roster is a projection, not a second store: each class copies the cost
# row's seconds and basis, and hostId, os, and measuredRunId (null when the row has none).
# ExpectedDurationSyncTests fails until this is re-run after a class-cost refresh.
# One-liner:
#   pwsh -NoProfile -File scripts/export-expected-duration-roster.ps1
param(
    [string]$CostPath = '',
    [string]$OutputPath = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($CostPath)) { $CostPath = Join-Path $PSScriptRoot (Join-Path 'lib' 'nightly-class-costs.json') }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $repo (Join-Path 'tests' (Join-Path 'Shared' 'expected-duration-roster.json')) }

$doc = [System.IO.File]::ReadAllText($CostPath) | ConvertFrom-Json
if ([string]$doc.format -cne 'antiphon-nightly-class-costs-v1') { throw 'class-cost-format' }
if ($null -eq $doc.classes) { throw 'class-cost-format' }
if ([string]::IsNullOrWhiteSpace([string]$doc.platform)) { throw 'class-cost-platform' }

function Get-OptionalString($Row, [string]$Name) {
    $prop = $Row.PSObject.Properties[$Name]
    if ($null -eq $prop -or $null -eq $prop.Value) { return $null }
    return [string]$prop.Value
}

$costFile = [System.IO.Path]::GetFullPath($CostPath)
$repoPrefix = [System.IO.Path]::GetFullPath($repo).TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
if ($costFile.StartsWith($repoPrefix, [StringComparison]::Ordinal)) { $costFile = $costFile.Substring($repoPrefix.Length) }
$costFile = $costFile -replace '\\', '/'

$names = [string[]]@($doc.classes.PSObject.Properties | ForEach-Object { $_.Name })
[Array]::Sort($names, [StringComparer]::Ordinal)
$classes = [ordered]@{}
foreach ($name in $names) {
    $row = $doc.classes.$name
    if ($null -eq $row.seconds -or [string]::IsNullOrWhiteSpace([string]$row.basis)) { throw ('class-cost-row: ' + $name) }
    $classes[$name] = [ordered]@{
        seconds = $row.seconds
        basis = [string]$row.basis
        hostId = Get-OptionalString $row 'hostId'
        os = Get-OptionalString $row 'os'
        measuredRunId = Get-OptionalString $row 'measuredRunId'
    }
}
$out = [ordered]@{
    format = 'antiphon-expected-duration-roster-v1'
    platform = [string]$doc.platform
    costFile = $costFile
    export = 'pwsh -NoProfile -File scripts/export-expected-duration-roster.ps1'
    classes = $classes
}
$json = ($out | ConvertTo-Json -Depth 8) -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($OutputPath, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('exported {0} classes -> {1}' -f $classes.Count, $OutputPath)
