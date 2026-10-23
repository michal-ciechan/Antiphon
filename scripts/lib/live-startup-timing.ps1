# ASCII-only for Windows PowerShell 5.1. Fixed labels; no payload/exception serialization.
$script:LiveStartupTimingRunId = [guid]::NewGuid().ToString('N')
function Start-LiveStartupPhase {
    param([Parameter(Mandatory)][ValidateSet('docker-info','docker-network','container-start','restore','dependencies','build','health-wait')][string]$Operation)
    @{ Operation = $Operation; Clock = [Diagnostics.Stopwatch]::StartNew(); Closed = $false }
}
function Stop-LiveStartupPhase {
    param([Parameter(Mandatory)][hashtable]$Phase, [bool]$Succeeded = $false)
    if ($Phase.Closed) { return }
    $Phase.Closed = $true
    try {
        $operation = [string]$Phase.Operation
        if ($operation -notin @('docker-info','docker-network','container-start','restore','dependencies','build','health-wait')) { return }
        $name = switch ($operation) {
            'health-wait' { 'network-loopback' }
            { $_ -in @('build','restore','dependencies') } { 'process-wait' }
            default { 'fixture-setup' }
        }
        $row = [ordered]@{
            version = 1; runId = $script:LiveStartupTimingRunId; producerPid = $PID
            phase = $name; operation = $operation
            elapsedMs = $Phase.Clock.Elapsed.TotalMilliseconds
            outcome = $(if ($Succeeded) { 'completed' } else { 'failed-or-canceled' })
        }
        Write-Host ('LIVE_TIMING ' + ($row | ConvertTo-Json -Compress))
    } catch { } # A telemetry sink cannot replace the measured result.
}
function Invoke-LiveStartupPhase {
    param(
        [Parameter(Mandatory)][ValidateSet('docker-info','docker-network','container-start','restore','dependencies','build','health-wait')][string]$Operation,
        [Parameter(Mandatory)][scriptblock]$Action
    )
    $phase = Start-LiveStartupPhase -Operation $Operation
    $succeeded = $false
    try { & $Action; $succeeded = $? }
    finally { Stop-LiveStartupPhase -Phase $phase -Succeeded $succeeded }
}
