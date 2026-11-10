#requires -Version 5.1
# CARD-1410: refresh scripts/lib/nightly-class-costs.json from nightly TRX files.
# CARD-1842: keep each row source and record hostId, os, and measuredRunId.
# -Os must be windows or linux. When it matches the file platform, seconds update.
# A different OS is stored as otherSample and leaves seconds and platform in place.
# File platform changes only when -SetPlatform is passed and every stored primary os agrees.
# ASCII-only. Each class row is the sum of its TRX result durations and its
# result count; classes absent from the TRX keep their committed row. The sum
# of durations is serial time, so it never under-states a parallel launch.
# One-liner:
#   pwsh -NoProfile -File scripts/refresh-nightly-class-costs.ps1 -TrxPath (Get-ChildItem <run>\logs\*\antiphon-*.trx).FullName -HostId <host> -Os <windows|linux> -MeasuredRunId <run>
param(
    [Parameter(Mandatory = $true)][string[]]$TrxPath,
    [string]$OutputPath = '',
    [string]$Source = '',
    [Parameter(Mandatory = $true)][string]$HostId,
    [Parameter(Mandatory = $true)][string]$Os,
    [Parameter(Mandatory = $true)][string]$MeasuredRunId,
    [switch]$SetPlatform
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($HostId)) { throw 'class-cost-host' }
if ([string]::IsNullOrWhiteSpace($MeasuredRunId)) { throw 'class-cost-run' }
if ($Os -cne 'windows' -and $Os -cne 'linux') { throw 'class-cost-os' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $PSScriptRoot (Join-Path 'lib' 'nightly-class-costs.json') }

function ConvertTo-ClassCostNode($Value) {
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $copy = [ordered]@{}
        foreach ($prop in @($Value.PSObject.Properties)) {
            $copy[$prop.Name] = ConvertTo-ClassCostNode $prop.Value
        }
        return $copy
    }
    return $Value
}

$rows = @{}
$doc = $null
if (Test-Path -LiteralPath $OutputPath) {
    $doc = [System.IO.File]::ReadAllText($OutputPath) | ConvertFrom-Json
    if ([string]$doc.format -cne 'antiphon-nightly-class-costs-v1') { throw 'class-cost-format' }
    if ($null -eq $doc.classes) { throw 'class-cost-format' }
    foreach ($row in @($doc.classes.PSObject.Properties)) {
        $rows[[string]$row.Name] = ConvertTo-ClassCostNode $row.Value
    }
}
$platform = ''
if ($null -ne $doc -and $null -ne $doc.PSObject.Properties['platform']) { $platform = [string]$doc.platform }
if ([string]::IsNullOrWhiteSpace($platform)) { $platform = $Os }
$sameOs = ($Os -ceq $platform)

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
    $measured = [ordered]@{
        nodes = [int]$fresh[$class].nodes
        seconds = [Math]::Max(0.1, [Math]::Round([double]$fresh[$class].seconds, 1))
        basis = 'trx'
        hostId = $HostId
        os = $Os
        measuredRunId = $MeasuredRunId
    }
    if ($sameOs) {
        $prior = $null
        if ($rows.ContainsKey($class)) { $prior = $rows[$class] }
        $row = [ordered]@{
            nodes = $measured.nodes
            seconds = $measured.seconds
            basis = 'trx'
        }
        if ($null -ne $prior -and $prior.Contains('source') -and $null -ne $prior['source']) {
            $row['source'] = [string]$prior['source']
        }
        $row['hostId'] = $HostId
        $row['os'] = $Os
        $row['measuredRunId'] = $MeasuredRunId
        if ($null -ne $prior) {
            foreach ($name in @($prior.Keys)) {
                if (@('nodes', 'seconds', 'basis', 'source', 'hostId', 'os', 'measuredRunId') -contains $name) { continue }
                $row[$name] = $prior[$name]
            }
        }
        $rows[$class] = $row
    } else {
        if (-not $rows.ContainsKey($class)) { continue }
        $row = $rows[$class]
        $row['otherSample'] = $measured
        $rows[$class] = $row
    }
}
if ($SetPlatform) {
    $agree = ($rows.Count -gt 0)
    foreach ($name in @($rows.Keys)) {
        $stored = ''
        if ($rows[$name].Contains('os')) { $stored = [string]$rows[$name]['os'] }
        if ($stored -cne $Os) { $agree = $false; break }
    }
    if ($agree) { $platform = $Os }
}
$classes = [ordered]@{}
foreach ($class in (@($rows.Keys) | Sort-Object)) { $classes[$class] = $rows[$class] }
$out = [ordered]@{
    format = 'antiphon-nightly-class-costs-v1'
    platform = $platform
    source = $(if ($Source) { $Source } else { 'TRX durations: ' + ((@($TrxPath) | ForEach-Object { [IO.Path]::GetFileName($_) }) -join ', ') })
    refresh = 'pwsh -NoProfile -File scripts/refresh-nightly-class-costs.ps1 -TrxPath (Get-ChildItem <run>\logs\*\antiphon-*.trx).FullName -HostId <host> -Os <windows|linux> -MeasuredRunId <run>'
    classes = $classes
}
[System.IO.File]::WriteAllText($OutputPath, (($out | ConvertTo-Json -Depth 8) + [Environment]::NewLine))
Write-Host ('refreshed {0} classes from {1} TRX files -> {2}' -f $fresh.Count, @($TrxPath).Count, $OutputPath)
