#requires -Version 5.1
<#
.SYNOPSIS
Observe new terminal/Blocked task states in one project or board; re-arm with the same state file.
.DESCRIPTION
Read-only HTTP polling. Writes only the caller-owned StateFile. First successful poll
baselines existing results. ASCII-only, Windows PowerShell 5.1 safe; -Once is for smoke checks.
#>
[CmdletBinding(DefaultParameterSetName = 'Project')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Project')][guid]$ProjectId,
    [Parameter(Mandatory = $true, ParameterSetName = 'Board')][guid]$BoardId,
    [Parameter(Mandatory = $true)][string]$StateFile,
    [string]$Api = $(if ($env:ANTIPHON_API) { $env:ANTIPHON_API } else { 'http://localhost:17202' }),
    [ValidateRange(1, 300)][int]$PollSeconds = 45,
    [ValidateRange(1, 29)][int]$MaxMinutes = 28,
    [switch]$Once)
$ErrorActionPreference = 'Stop'
$lastStatus = @{}
$headers = @{}
if ($env:ANTIPHON_TASK_TOKEN) { $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN }
$first = $true
$scope = "projectId=$ProjectId"
if ($PSCmdlet.ParameterSetName -eq 'Board') { $scope = "boardId=$BoardId" }
if (Test-Path -LiteralPath $StateFile) {
    # Refuse a damaged or mismatched state instead of silently losing transitions.
    $saved = Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
    if ($saved.scope -ne $scope) { throw 'Watcher state scope differs; use a separate StateFile.' }
    if ($saved.PSObject.Properties['lastStatus']) {
        foreach ($entry in $saved.lastStatus.PSObject.Properties) { $lastStatus[$entry.Name] = [string]$entry.Value }
        $first = $false
    } elseif ($saved.PSObject.Properties['seen']) {
        # Legacy pairs have no ordering, so their latest status cannot be recovered.
        'TASK-STATE-UPGRADE baselining legacy seen-pair state'
    } else { throw 'Watcher state has no lastStatus map.' }
}
$deadline = [datetime]::UtcNow.AddMinutes($MaxMinutes)
do {
    try {
        $reply = Invoke-RestMethod -Uri "$($Api.TrimEnd('/'))/api/agent-tasks?$scope" -Headers $headers -TimeoutSec 20
        if ($null -eq $reply -or -not $reply.PSObject.Properties['items']) { throw 'Task list envelope has no items.' }
        foreach ($task in @($reply.items)) {
            $id = [string]$task.id
            $status = [string]$task.status
            if (-not $id -or -not $status) { continue }
            if (-not $lastStatus.ContainsKey($id) -or $lastStatus[$id] -ne $status) {
                if (-not $first -and $status -in @('Succeeded', 'Failed', 'Blocked', 'Canceled')) { "TASK-$($status.ToUpperInvariant()) $id $($task.role) $($task.title)" }
                $lastStatus[$id] = $status
            }
        }
        @{ scope = $scope; lastStatus = $lastStatus } | ConvertTo-Json -Compress | Set-Content -LiteralPath $StateFile -Encoding ASCII
        $first = $false
    } catch { "TASK-WATCH-ERROR $($_.Exception.Message)" }
    if ($Once -or [datetime]::UtcNow -ge $deadline) { break }
    $remainingMs = [Math]::Max(0, [Math]::Floor(($deadline - [datetime]::UtcNow).TotalMilliseconds))
    if ($remainingMs -gt 0) { Start-Sleep -Milliseconds ([int][Math]::Min($PollSeconds * 1000, $remainingMs)) }
} while ([datetime]::UtcNow -lt $deadline)
'watch-tasks window ended; re-arm'
