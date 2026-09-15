# Commit only explicit task paths through the server's ignore gate. Never pushes.
[CmdletBinding()]
param(
    [string]$Task = $env:ANTIPHON_TASK_ID,
    [Parameter(Mandatory = $true)][string[]]$Paths,
    [Parameter(Mandatory = $true)][string]$MessageFile
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Task)) { throw 'Task id is required (-Task or ANTIPHON_TASK_ID).' }
if ([string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) { throw 'ANTIPHON_TASK_TOKEN is required.' }
$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$message = Get-Content -LiteralPath $MessageFile -Raw -Encoding UTF8
$body = @{ paths = @($Paths); message = $message } | ConvertTo-Json -Depth 5 -Compress
try {
    $result = Invoke-RestMethod -Method POST -Uri ($api.TrimEnd('/') + '/api/agent-tasks/' + [uri]::EscapeDataString($Task) + '/commit') `
        -Headers @{ 'X-Antiphon-Task-Token' = $env:ANTIPHON_TASK_TOKEN } `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    $result | ConvertTo-Json -Depth 5
}
catch {
    $detail = $_.ErrorDetails.Message
    if ([string]::IsNullOrWhiteSpace($detail)) { $detail = $_.Exception.Message }
    [Console]::Error.WriteLine($detail)
    exit 1
}
