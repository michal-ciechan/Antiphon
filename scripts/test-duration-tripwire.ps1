# CARD-0110 S6: report Antiphon.Tests TRX entries >= 5s that are not on the allowlist.
# Usage: pwsh -File scripts/test-duration-tripwire.ps1 -Trx path\to\run.trx
param(
    [Parameter(Mandatory = $true)]
    [string] $Trx,
    [string] $Allowlist = $(Join-Path $PSScriptRoot '..\tests\Antiphon.Tests\slow-tests-allowlist.txt')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Trx)) { throw "TRX not found: $Trx" }
if (-not (Test-Path $Allowlist)) { throw "Allowlist not found: $Allowlist" }

$entries = Get-Content $Allowlist | ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith('#') }
[xml]$doc = Get-Content -Raw $Trx
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$nsm = $doc.DocumentElement.NamespaceURI
if ($nsm) { $ns.AddNamespace('t', $nsm) }
$nodes = if ($nsm) {
    $doc.SelectNodes('//t:UnitTestResult', $ns)
} else {
    $doc.SelectNodes('//UnitTestResult')
}
$hits = @()
foreach ($n in $nodes) {
    $name = $n.GetAttribute('testName')
    $durText = $n.GetAttribute('duration')
    $dur = [TimeSpan]::Zero
    if (-not [TimeSpan]::TryParse($durText, [ref]$dur)) { continue }
    if ($dur.TotalSeconds -lt 5) { continue }
    $allowed = $false
    foreach ($e in $entries) {
        if ($name.IndexOf($e, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $allowed = $true; break }
    }
    if (-not $allowed) {
        $hits += ('{0:n1}s  {1}' -f $dur.TotalSeconds, $name)
    }
}
if ($hits.Count -eq 0) {
    Write-Output 'SLOW-TEST TRIPWIRE: 0 unlisted tests >= 5s'
    exit 0
}
Write-Output ("SLOW-TEST TRIPWIRE: {0} unlisted tests >= 5s" -f $hits.Count)
$hits | ForEach-Object { Write-Output $_ }
exit 1
