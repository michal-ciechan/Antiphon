#requires -Version 7.0
<#
.SYNOPSIS
    Summarize a test trace file written by ANTIPHON_TEST_TRACE_FILE (tests/Shared/TestTrace.cs).
    pwsh -NoProfile -File scripts/summarize-test-trace.ps1 -Path trace.jsonl [-Top 25] [-Test <substring>]
.DESCRIPTION
    Each line of the file is one completed span (OpenTelemetry-style: traceId, spanId, parentId,
    source, name, start, durationMs, tags). TUnit starts one trace per test, so a trace is a test.
    Prints:
      1. Tests (root spans) by duration.
      2. Span names by total and self time across the run. Self time is the span's duration minus
         its direct children's durations (floored at zero), so nested work is not counted twice.
         Children that overlap in parallel can make self time an underestimate.
      3. With -Test, the span tree of the matching test(s), children in start order.
    Producing a trace:
      $env:ANTIPHON_TEST_TRACE_FILE = "$PWD\trace.jsonl"
      tests\Antiphon.Tests\bin\Debug\net9.0\Antiphon.Tests.exe --treenode-filter "/*/*/<Class>/*"
    The file is appended to; delete it between runs you want to keep apart. ASCII-only.
#>
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int]$Top = 25,
    [string]$Test,
    [double]$MinMs = 1
)
$ErrorActionPreference = 'Stop'

$spans = [System.Collections.Generic.List[object]]::new()
foreach ($line in [IO.File]::ReadLines((Resolve-Path -LiteralPath $Path).Path)) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try { $spans.Add(($line | ConvertFrom-Json)) } catch { }
}
if ($spans.Count -eq 0) { throw "No spans in $Path." }

$byId = @{}
foreach ($s in $spans) { $byId["$($s.TraceId)/$($s.SpanId)"] = $s }
$children = @{}
foreach ($s in $spans) {
    if ($null -eq $s.ParentId) { continue }
    $key = "$($s.TraceId)/$($s.ParentId)"
    if (-not $children.ContainsKey($key)) { $children[$key] = [System.Collections.Generic.List[object]]::new() }
    $children[$key].Add($s)
}
function Get-Self($s) {
    $key = "$($s.TraceId)/$($s.SpanId)"
    $sum = 0.0
    if ($children.ContainsKey($key)) { foreach ($c in $children[$key]) { $sum += [double]$c.DurationMs } }
    return [math]::Max(0.0, [double]$s.DurationMs - $sum)
}

$roots = @($spans | Where-Object { $null -eq $_.ParentId -or -not $byId.ContainsKey("$($_.TraceId)/$($_.ParentId)") })
Write-Host ("{0} spans, {1} traces, {2} sources: {3}" -f $spans.Count, ($spans.TraceId | Sort-Object -Unique).Count,
    ($spans.Source | Sort-Object -Unique).Count, (($spans.Source | Sort-Object -Unique) -join ', '))

Write-Host "`n== Root spans (tests) by duration =="
$roots | Sort-Object { -[double]$_.DurationMs } | Select-Object -First $Top |
    ForEach-Object { "{0,10:N1} ms  {1,-12} {2}" -f [double]$_.DurationMs, $_.Source, $_.Name } | Write-Host

Write-Host "`n== Span names by self time (all traces) =="
$agg = @{}
foreach ($s in $spans) {
    $name = "$($s.Source) | $($s.Name)"
    if (-not $agg.ContainsKey($name)) { $agg[$name] = [pscustomobject]@{ Name = $name; Count = 0; Total = 0.0; Self = 0.0; Max = 0.0 } }
    $a = $agg[$name]; $a.Count++; $a.Total += [double]$s.DurationMs; $a.Self += (Get-Self $s)
    if ([double]$s.DurationMs -gt $a.Max) { $a.Max = [double]$s.DurationMs }
}
"{0,7} {1,12} {2,12} {3,10}  {4}" -f 'count', 'self ms', 'total ms', 'max ms', 'source | span' | Write-Host
$agg.Values | Sort-Object { -$_.Self } | Select-Object -First $Top |
    ForEach-Object { "{0,7} {1,12:N0} {2,12:N0} {3,10:N0}  {4}" -f $_.Count, $_.Self, $_.Total, $_.Max, $_.Name } | Write-Host

if ($Test) {
    function Write-Tree($s, [int]$depth) {
        if ([double]$s.DurationMs -lt $MinMs -and $depth -gt 0) { return }
        $tags = ''
        if ($s.Tags) { $tags = ' {' + (($s.Tags.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ' ') + '}' }
        Write-Host ("{0}{1,9:N1} ms (self {2,8:N1})  {3}{4}" -f ('  ' * $depth), [double]$s.DurationMs, (Get-Self $s), $s.Name, $tags)
        $key = "$($s.TraceId)/$($s.SpanId)"
        if ($children.ContainsKey($key)) {
            foreach ($c in ($children[$key] | Sort-Object { [datetime]$_.Start })) { Write-Tree $c ($depth + 1) }
        }
    }
    foreach ($r in ($roots | Where-Object { $_.Name -like "*$Test*" })) {
        Write-Host "`n== Trace: $($r.Name) =="
        Write-Tree $r 0
    }
}
