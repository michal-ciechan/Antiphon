# CARD-0527. Commit named paths through the gated server primitive. ASCII-only.
#
# Usage:
#   pwsh -NoProfile -File scripts/task-commit.ps1 -Paths a.md,b.md -MessageFile msg.txt
#   ANTIPHON_TASK_TOKEN and ANTIPHON_API must be set (the delegate environment).
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Paths,

    [Parameter(Mandatory = $true)]
    [string]$MessageFile
)

$ErrorActionPreference = 'Stop'

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

$token = $env:ANTIPHON_TASK_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Error 'ANTIPHON_TASK_TOKEN is required.'
    exit 1
}

$taskId = $env:ANTIPHON_TASK_ID
if ([string]::IsNullOrWhiteSpace($taskId)) {
    Write-Error 'ANTIPHON_TASK_ID is required.'
    exit 1
}

if (-not (Test-Path -LiteralPath $MessageFile)) {
    Write-Error "-MessageFile '$MessageFile' does not exist."
    exit 1
}

$message = Get-Content -LiteralPath $MessageFile -Raw -Encoding UTF8
if ($null -eq $message) { $message = '' }

$flat = @()
foreach ($item in $Paths) {
    foreach ($part in ($item -split ',')) {
        $trim = $part.Trim()
        if ($trim) { $flat += $trim }
    }
}

$body = @{ paths = $flat; message = $message }
$json = $body | ConvertTo-Json -Depth 6 -Compress
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
$headers = @{ 'X-Antiphon-Task-Token' = $token }

try {
    $result = Invoke-RestMethod -Method POST -Uri "$api/api/agent-tasks/$taskId/commit" `
        -Headers $headers -Body $bytes -ContentType 'application/json; charset=utf-8'
    Write-Output ("committed {0} {1}" -f $result.sha, (($result.files) -join ', '))
}
catch {
    $detail = $_.ErrorDetails.Message
    if ([string]::IsNullOrWhiteSpace($detail)) { $detail = $_.Exception.Message }
    Write-Error "commit refused: $detail"
    exit 1
}
