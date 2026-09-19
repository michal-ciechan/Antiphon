#requires -Version 5.1
<#!
.SYNOPSIS
    Preview, status, release and revoke CARD-0459 worktree residue.

    ASCII-only: parses under pwsh 7 and Windows PowerShell 5.1.
    Never trusts client-supplied paths, remotes or terminal status.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Preview', 'Status', 'Release', 'Revoke', 'BatchRelease')]
    [string]$Action,

    [string]$BaseUrl = 'http://127.0.0.1:17202',
    [string]$ProjectId,
    [string]$BoardId,
    [string]$TaskId,
    [string]$RetirementId,
    [string]$ExpectedTaskRevision,
    [string]$SourceSha,
    [string]$ReportDigest,
    [string]$Reason,
    [string]$ManifestPath,
    [string]$RunId,
    [int]$Page = 0,
    [string]$Token
)

$ErrorActionPreference = 'Stop'

function Invoke-ResidueJson {
    param([string]$Method, [string]$Url, $Body)
    $headers = @{}
    if ($Token) { $headers['X-Antiphon-Task-Token'] = $Token }
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Compress -Depth 8
        return Invoke-RestMethod -Method $Method -Uri $Url -Headers $headers -ContentType 'application/json' -Body $json
    }
    return Invoke-RestMethod -Method $Method -Uri $Url -Headers $headers
}

$root = $BaseUrl.TrimEnd('/')
switch ($Action) {
    'Preview' {
        $payload = @{}
        if ($ProjectId) { $payload.projectId = $ProjectId }
        if ($BoardId) { $payload.boardId = $BoardId }
        Invoke-ResidueJson -Method Post -Url "$root/api/agent-tasks/worktree-residue/preview" -Body $payload
    }
    'Status' {
        if (-not $RunId) { throw 'Status requires -RunId' }
        Invoke-ResidueJson -Method Get -Url "$root/api/agent-tasks/worktree-residue/runs/${RunId}?page=$Page"
    }
    'Release' {
        if (-not $TaskId) { throw 'Release requires a full -TaskId' }
        if ($TaskId.Length -ne 36) { throw 'Release requires a full task GUID, not a short id' }
        if (-not $ExpectedTaskRevision) { throw 'Release requires -ExpectedTaskRevision' }
        if (-not $SourceSha) { throw 'Release requires -SourceSha' }
        if (-not $Reason) { throw 'Release requires -Reason' }
        $payload = @{
            expectedTaskRevision = $ExpectedTaskRevision
            sourceSha = $SourceSha
            reportDigest = $ReportDigest
            noFurtherWorkspaceUse = $true
            reason = $Reason
        }
        Invoke-ResidueJson -Method Post -Url "$root/api/agent-tasks/$TaskId/worktree-retirement" -Body $payload
    }
    'Revoke' {
        if (-not $TaskId -or -not $RetirementId) { throw 'Revoke requires -TaskId and -RetirementId' }
        Invoke-ResidueJson -Method Delete -Url "$root/api/agent-tasks/$TaskId/worktree-retirement/$RetirementId"
    }
    'BatchRelease' {
        if (-not $ManifestPath) { throw 'BatchRelease requires -ManifestPath' }
        $items = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $failures = @()
        foreach ($item in @($items)) {
            if (-not $item.taskId -or $item.taskId.Length -ne 36) {
                $failures += "invalid-id:$($item.taskId)"
                continue
            }
            try {
                $payload = @{
                    expectedTaskRevision = $item.expectedTaskRevision
                    sourceSha = $item.sourceSha
                    reportDigest = $item.reportDigest
                    noFurtherWorkspaceUse = $true
                    reason = [string]$item.reason
                }
                Invoke-ResidueJson -Method Post -Url "$root/api/agent-tasks/$($item.taskId)/worktree-retirement" -Body $payload | Out-Null
            }
            catch {
                $failures += "$($item.taskId):$($_.Exception.Message)"
            }
        }
        if ($failures.Count -gt 0) {
            Write-Output ($failures -join '; ')
            exit 1
        }
    }
}
