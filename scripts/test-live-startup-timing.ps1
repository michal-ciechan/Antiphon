$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/live-startup-timing.ps1')
$observations = @(& {
    Invoke-LiveStartupPhase -Operation restore -Action { 'return-sentinel' }
    try { Invoke-LiveStartupPhase -Operation health-wait -Action { throw 'secret-canary' } }
    catch { if ($_.Exception.Message -ne 'secret-canary') { throw } }
} 6>&1)
$rows = @($observations | ForEach-Object { $_.ToString() } | Where-Object { $_.StartsWith('LIVE_TIMING ') } | ForEach-Object { $_.Substring(12) | ConvertFrom-Json })
if ($rows.Count -ne 2) { throw "timing assertion: expected two phase records, got $($rows.Count)" }
if (@($observations | Where-Object { $_ -ceq 'return-sentinel' }).Count -ne 1) { throw 'return value changed' }
if ($rows[0].outcome -ne 'completed' -or $rows[1].outcome -ne 'failed-or-canceled') { throw 'outcome assertion failed' }
if ($rows[0].phase -ne 'process-wait' -or $rows[1].phase -ne 'network-loopback') { throw 'phase vocabulary assertion failed' }
if ($rows[0].elapsedMs -lt 0 -or $rows[1].elapsedMs -lt 0 -or -not $rows[0].runId) { throw 'clock/correlation assertion failed' }
if (($rows | ConvertTo-Json -Compress) -match 'secret-canary|return-sentinel') { throw 'payload entered timing record' }
Write-Host 'C1564 script-timing: 6 assertions passed; 2 measured phases; original value/exception preserved'
