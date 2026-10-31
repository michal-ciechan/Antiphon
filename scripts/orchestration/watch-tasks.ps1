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
    [string]$Api = 'http://localhost:17202',
    [ValidateRange(1, 300)][int]$PollSeconds = 45,
    [ValidateRange(1, 29)][int]$MaxMinutes = 28,
    [switch]$Once)
$ErrorActionPreference = 'Stop'
$seen = @{}
$first = $true
$scope = "projectId=$ProjectId"
if ($PSCmdlet.ParameterSetName -eq 'Board') { $scope = "boardId=$BoardId" }
if (Test-Path -LiteralPath $StateFile) {
    # Refuse a damaged or mismatched state instead of silently losing transitions.
    $saved = Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
    if ($saved.scope -ne $scope) { throw 'Watcher state scope differs; use a separate StateFile.' }
    foreach ($key in @($saved.seen)) { $seen[[string]$key] = $true }
    $first = $false
}
$deadline = [datetime]::UtcNow.AddMinutes($MaxMinutes)
do {
    try {
        $reply = Invoke-RestMethod -Uri "$($Api.TrimEnd('/'))/api/agent-tasks?$scope" -TimeoutSec 20
        if ($null -eq $reply -or -not $reply.PSObject.Properties['items']) { throw 'Task list envelope has no items.' }
        foreach ($task in @($reply.items)) {
            $id = [string]$task.id
            $status = [string]$task.status
            if (-not $id -or $status -notin @('Succeeded', 'Failed', 'Blocked', 'Canceled')) { continue }
            $key = "$id|$status"
            if (-not $seen.ContainsKey($key)) {
                if (-not $first) { "TASK-$($status.ToUpperInvariant()) $id $($task.role) $($task.title)" }
                $seen[$key] = $true
            }
        }
        @{ scope = $scope; seen = @($seen.Keys) } | ConvertTo-Json -Compress | Set-Content -LiteralPath $StateFile -Encoding ASCII
        $first = $false
    } catch { "TASK-WATCH-ERROR $($_.Exception.Message)" }
    if ($Once -or [datetime]::UtcNow -ge $deadline) { break }
    Start-Sleep -Seconds $PollSeconds
} while ([datetime]::UtcNow -lt $deadline)
'watch-tasks window ended; re-arm'
