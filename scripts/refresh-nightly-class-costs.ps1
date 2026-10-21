#requires -Version 5.1
# CARD-1410: refresh scripts/lib/nightly-class-costs.json from nightly TRX files.
# ASCII-only. Each class row is the sum of its TRX result durations and its
# result count; classes absent from the TRX keep their committed row. The sum
# of durations is serial time, so it never under-states a parallel launch.
# One-liner:
#   pwsh -NoProfile -File scripts/refresh-nightly-class-costs.ps1 -TrxPath (Get-ChildItem <run>\logs\*\antiphon-*.trx).FullName
param(
    [Parameter(Mandatory = $true)][string[]]$TrxPath,
    [string]$OutputPath = '',
    [string]$Source = ''
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $PSScriptRoot (Join-Path 'lib' 'nightly-class-costs.json') }
$rows = @{}
if (Test-Path -LiteralPath $OutputPath) {
    $doc = [System.IO.File]::ReadAllText($OutputPath) | ConvertFrom-Json
    if ([string]$doc.format -cne 'antiphon-nightly-class-costs-v1') { throw 'class-cost-format' }
    foreach ($row in @($doc.classes.PSObject.Properties)) { $rows[[string]$row.Name] = [ordered]@{ nodes = [int]$row.Value.nodes; seconds = [double]$row.Value.seconds; basis = [string]$row.Value.basis } }
}
$fresh = @{}
foreach ($path in @($TrxPath)) {
    [xml]$trx = [System.IO.File]::ReadAllText($path)
    $classById = @{}
    foreach ($test in @($trx.TestRun.TestDefinitions.UnitTest)) {
        $name = [string]$test.TestMethod.className
        $paren = $name.IndexOf('(')
        if ($paren -ge 0) { $name = $name.Substring(0, $paren) }
        $classById[[string]$test.id] = $name
    }
    foreach ($result in @($trx.TestRun.Results.UnitTestResult)) {
        $class = $classById[[string]$result.testId]
        if ([string]::IsNullOrWhiteSpace($class) -or [string]::IsNullOrWhiteSpace([string]$result.duration)) { continue }
        if (-not $fresh.ContainsKey($class)) { $fresh[$class] = [ordered]@{ nodes = 0; seconds = 0.0 } }
        $fresh[$class].nodes = [int]$fresh[$class].nodes + 1
        $fresh[$class].seconds = [double]$fresh[$class].seconds + [TimeSpan]::Parse([string]$result.duration).TotalSeconds
    }
}
foreach ($class in @($fresh.Keys)) {
    $rows[$class] = [ordered]@{ nodes = [int]$fresh[$class].nodes; seconds = [Math]::Max(0.1, [Math]::Round([double]$fresh[$class].seconds, 1)); basis = 'trx' }
}
$classes = [ordered]@{}
foreach ($class in (@($rows.Keys) | Sort-Object)) { $classes[$class] = $rows[$class] }
$out = [ordered]@{
    format = 'antiphon-nightly-class-costs-v1'
    platform = $(if ([IO.Path]::DirectorySeparatorChar -eq '\') { 'windows' } else { 'linux' })
    source = $(if ($Source) { $Source } else { 'TRX durations: ' + ((@($TrxPath) | ForEach-Object { [IO.Path]::GetFileName($_) }) -join ', ') })
    refresh = 'pwsh -NoProfile -File scripts/refresh-nightly-class-costs.ps1 -TrxPath (Get-ChildItem <run>\logs\*\antiphon-*.trx).FullName'
    classes = $classes
}
[System.IO.File]::WriteAllText($OutputPath, (($out | ConvertTo-Json -Depth 5) + [Environment]::NewLine))
Write-Host ('refreshed {0} classes from {1} TRX files -> {2}' -f $fresh.Count, @($TrxPath).Count, $OutputPath)
