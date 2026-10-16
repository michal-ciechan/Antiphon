#requires -Version 7.0
[CmdletBinding()]
param(
    [string[]]$Roots,
    [ValidateSet('roster', 'class', 'category')][string]$GroupBy = 'class',
    [int]$Top = 20,
    [switch]$Json,
    [string]$SelectionPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/test-timings.ps1')
$report = Get-TestTimingReport -Roots $Roots -GroupBy $GroupBy
$ranked = @($report.groups | Sort-Object -Property @{ Expression = { if ($null -eq $_.median) { [double]::MinValue } else { [double]$_.median } } } -Descending)
if ($Top -gt 0 -and $ranked.Count -gt $Top) { $ranked = @($ranked | Select-Object -First $Top) }
$report.groups = @($ranked)
if ($SelectionPath) {
    $selection = Get-Content -LiteralPath $SelectionPath -Raw | ConvertFrom-Json
    $augmented = Add-TestTimingEstimates -Selection $selection -Report $report
    $text = $augmented | ConvertTo-Json -Depth 8 -Compress
} else {
    $text = $report | ConvertTo-Json -Depth 8 -Compress
}
if ($Json -or $SelectionPath) { [Console]::Out.WriteLine($text) } else { Write-Output $text }
exit 0
