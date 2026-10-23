# Fixed operation vocabulary. No arguments, paths, payloads or exception text in telemetry.
function Invoke-LiveStartupPhase {
    param(
        [Parameter(Mandatory)][ValidateSet('docker-info','docker-network','container-start','restore','dependencies','build','health-wait')][string]$Operation,
        [Parameter(Mandatory)][scriptblock]$Action
    )
    & $Action
}
