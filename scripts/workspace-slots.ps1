# Runner workspace slots (CARD-0824 S1).
# ASCII-only: this script must parse under Windows PowerShell 5.1.
#
#   workspace-slots.ps1 cold-reset -Runner server2 [-Slot ws-03,ws-01] -Reason "why"
#   workspace-slots.ps1 cold-reset -AllRunners -Reason "rc qualification rc/<date> <sha12>"
#
# cold-reset calls POST /api/session-runners/{runnerId}/workspace-slots/cold-reset with the
# operator token from the owner-only file the server creates at startup (default
# %LOCALAPPDATA%\Antiphon\operator-token; override with ANTIPHON_OPERATOR_TOKEN_FILE). The token
# is never printed. Without -Slot every slot of the runner is targeted. A Free slot is
# cold-reset at once; a leased slot only records the request as pending and runs it inside its
# release, so the call never waits for leased slots. -AllRunners targets every runner whose
# capabilities advertise workspaceSlotV1. The request id and each slot's outcome are printed.
# Exit 0 when every runner answered, 1 when any call failed.
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('cold-reset')]
    [string]$Verb,

    [string]$Runner,

    [switch]$AllRunners,

    [string[]]$Slot,

    [Parameter(Mandatory = $true)]
    [string]$Reason,

    [string]$Requester
)

$ErrorActionPreference = 'Stop'

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

if ($AllRunners -and -not [string]::IsNullOrWhiteSpace($Runner)) {
    Write-Error 'workspace_slots_target: give -Runner or -AllRunners, not both.'
    exit 2
}
if (-not $AllRunners -and [string]::IsNullOrWhiteSpace($Runner)) {
    Write-Error 'workspace_slots_target: give -Runner <name> or -AllRunners.'
    exit 2
}
if ($AllRunners -and $Slot) {
    Write-Error 'workspace_slots_target: -Slot names one runner''s slots; use it with -Runner.'
    exit 2
}

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}
$path = $env:ANTIPHON_OPERATOR_TOKEN_FILE
if ([string]::IsNullOrWhiteSpace($path)) {
    $path = Join-Path (Join-Path $env:LOCALAPPDATA 'Antiphon') 'operator-token'
}
if (-not (Test-Path -LiteralPath $path)) {
    Write-Error "Operator token file not found at $path. The server creates it at startup; run this as the account the server runs under."
    exit 2
}
$token = (Get-Content -LiteralPath $path -Raw).Trim()
if ([string]::IsNullOrEmpty($token)) {
    Write-Error "Operator token file at $path is empty."
    exit 2
}
$headers['X-Antiphon-Operator-Token'] = $token

if ([string]::IsNullOrWhiteSpace($Requester)) {
    $Requester = 'workspace-slots.ps1'
    if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_ID)) { $Requester = 'task ' + $env:ANTIPHON_TASK_ID }
}

$runners = @()
if ($AllRunners) {
    $listed = Invoke-RestMethod -Method Get -Uri ($api + '/api/session-runners') -Headers $headers
    foreach ($row in @($listed)) {
        $features = @()
        if ($null -ne $row.features) { $features = @($row.features) }
        if ($features -contains 'workspaceSlotV1') { $runners += [string]$row.runnerId }
    }
    if ($runners.Count -eq 0) {
        Write-Output 'no runner advertises workspaceSlotV1; nothing to cold-reset'
        exit 0
    }
}
else {
    $runners = @($Runner)
}

$failed = $false
foreach ($runnerId in $runners) {
    $body = @{ all = (-not $Slot); reason = $Reason; requester = $Requester }
    if ($Slot) { $body['slots'] = @($Slot) }
    $uri = $api + '/api/session-runners/' + [uri]::EscapeDataString($runnerId) + '/workspace-slots/cold-reset'
    try {
        $answer = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType 'application/json' `
            -Body ($body | ConvertTo-Json -Depth 4)
        Write-Output ("runner={0} request={1}" -f $runnerId, $answer.requestId)
        foreach ($outcome in @($answer.slots)) {
            Write-Output ("  {0} {1}" -f $outcome.slotId, $outcome.outcome)
        }
    }
    catch {
        $failed = $true
        Write-Output ("runner={0} failed: {1}" -f $runnerId, $_.Exception.Message)
    }
}

if ($failed) { exit 1 }
exit 0
