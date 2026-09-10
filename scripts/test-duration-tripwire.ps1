# CARD-0110 S6 / CARD-0475 S6: report Antiphon.Tests TRX entries >= 5s that are not on the allowlist.
# Join UnitTestResult.testId to TestDefinitions/UnitTest/TestMethod@className (never display name).
# Allowlist entries match the simple or fully-qualified class name exactly (OrdinalIgnoreCase).
# Usage: pwsh -File scripts/test-duration-tripwire.ps1 -Trx path\to\run.trx
param(
    [Parameter(Mandatory = $true)]
    [string] $Trx,
    [string] $Allowlist = $(Join-Path $PSScriptRoot '..\tests\Antiphon.Tests\slow-tests-allowlist.txt')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Trx)) { throw "TRX not found: $Trx" }
if (-not (Test-Path $Allowlist)) { throw "Allowlist not found: $Allowlist" }

function Write-Invalid([string] $message) {
    Write-Output ("SLOW-TEST TRIPWIRE: invalid input: {0}" -f $message)
    exit 2
}

$entries = @(Get-Content $Allowlist | ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith('#') })

try {
    [xml]$doc = Get-Content -Raw $Trx
} catch {
    Write-Invalid ("malformed XML: {0}" -f $_.Exception.Message)
}

$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$nsm = $doc.DocumentElement.NamespaceURI
if ($nsm) { $ns.AddNamespace('t', $nsm) }

function Select-Ns($xpath) {
    if ($nsm) { return $doc.SelectNodes($xpath, $ns) }
    return $doc.SelectNodes(($xpath -replace 't:', ''))
}

$definitions = @{}
foreach ($unit in (Select-Ns '//t:TestDefinitions/t:UnitTest')) {
    $id = $unit.GetAttribute('id')
    if (-not $id) { continue }
    $method = $null
    foreach ($child in $unit.ChildNodes) {
        if ($child.LocalName -eq 'TestMethod') { $method = $child; break }
    }
    $className = $null
    $methodName = $null
    if ($method) {
        $className = $method.GetAttribute('className')
        $methodName = $method.GetAttribute('name')
    }
    $definitions[$id] = @{ ClassName = $className; MethodName = $methodName }
}

function Get-SimpleName([string] $className) {
    if ([string]::IsNullOrWhiteSpace($className)) { return $className }
    $parts = $className.Split('.')
    return $parts[$parts.Length - 1]
}

function Test-Allowlisted([string] $full, [string] $simple) {
    foreach ($e in $entries) {
        if ($full -and [string]::Equals($full, $e, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        if ($simple -and [string]::Equals($simple, $e, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

$results = @(Select-Ns '//t:Results/t:UnitTestResult')
if ($results.Count -eq 0) {
    $results = @(Select-Ns '//t:UnitTestResult')
}

$hits = @()
$unresolved = @()
$classStats = @{}
$rowCount = 0

foreach ($n in $results) {
    $rowCount++
    $testId = $n.GetAttribute('testId')
    $display = $n.GetAttribute('testName')
    $durText = $n.GetAttribute('duration')
    if ([string]::IsNullOrWhiteSpace($durText)) {
        Write-Invalid ("missing duration for testId={0} testName={1}" -f $testId, $display)
    }
    $dur = [TimeSpan]::Zero
    if (-not [TimeSpan]::TryParse($durText, [ref]$dur)) {
        Write-Invalid ("unparsable duration '{0}' for testId={1} testName={2}" -f $durText, $testId, $display)
    }
    if ($dur.TotalSeconds -lt 0) {
        Write-Invalid ("negative duration '{0}' for testId={1} testName={2}" -f $durText, $testId, $display)
    }

    $def = $null
    if ($testId -and $definitions.ContainsKey($testId)) { $def = $definitions[$testId] }
    $full = if ($def) { [string]$def.ClassName } else { $null }
    $method = if ($def) { [string]$def.MethodName } else { $null }
    $simple = Get-SimpleName $full
    $identity = if ($full) { '{0}.{1}' -f $full, $method } else { $display }

    $key = if ($full) { $full } else { '<unresolved>' }
    if (-not $classStats.ContainsKey($key)) {
        $classStats[$key] = @{ Expanded = 0; Slow = 0; BodySeconds = 0.0 }
    }
    $classStats[$key].Expanded++
    $classStats[$key].BodySeconds += $dur.TotalSeconds

    $resolved = $def -and -not [string]::IsNullOrWhiteSpace($full) -and -not [string]::IsNullOrWhiteSpace($method)
    if (-not $resolved) {
        $unresolved += ('unresolved testId={0} testName={1}' -f $testId, $display)
    }

    if ($dur.TotalSeconds -lt 5) { continue }

    $classStats[$key].Slow++
    $allowed = $resolved -and (Test-Allowlisted $full $simple)
    if (-not $allowed) {
        $hits += ('{0:n3}s  {1}  {2}' -f $dur.TotalSeconds, $identity, $display)
    }
}

Write-Output ("SLOW-TEST TRIPWIRE: expanded rows={0}" -f $rowCount)
foreach ($k in ($classStats.Keys | Sort-Object)) {
    $s = $classStats[$k]
    Write-Output ("CLASS {0}: expanded={1} slow-rows={2} body-seconds={3} (summed TRX body time, not wall-clock duration)" -f `
        $k, $s.Expanded, $s.Slow, $s.BodySeconds.ToString('0.000', [Globalization.CultureInfo]::InvariantCulture))
}

if ($unresolved.Count -gt 0) {
    Write-Output ("SLOW-TEST TRIPWIRE: {0} unresolved identit{1}" -f $unresolved.Count, $(if ($unresolved.Count -eq 1) { 'y' } else { 'ies' }))
    $unresolved | ForEach-Object { Write-Output $_ }
}

if ($hits.Count -eq 0 -and $unresolved.Count -eq 0) {
    Write-Output 'SLOW-TEST TRIPWIRE: 0 unlisted tests >= 5s'
    exit 0
}

if ($hits.Count -gt 0) {
    Write-Output ("SLOW-TEST TRIPWIRE: {0} unlisted tests >= 5s" -f $hits.Count)
    $hits | ForEach-Object { Write-Output $_ }
}
exit 1
