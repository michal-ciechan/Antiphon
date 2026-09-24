# List or force-release phone-home runner seats (CARD-0653).
# ASCII-only: this script must parse under Windows PowerShell 5.1.
#
#   runner-slots.ps1 list [-RunnerId server2]
#   runner-slots.ps1 release -SessionId <guid> -Reason "why"
#   runner-slots.ps1 release-orphans -Reason "why"
#
# The release verbs send the operator token from the owner-only file the server creates at
# startup (default %LOCALAPPDATA%\Antiphon\operator-token; override with
# ANTIPHON_OPERATOR_TOKEN_FILE). The token is never printed.
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('list', 'release', 'release-orphans')]
    [string]$Verb,

    [string]$RunnerId = 'server2',

    [string]$SessionId,

    [string]$Reason
)

$ErrorActionPreference = 'Stop'

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}

function Add-OperatorToken {
    $path = $env:ANTIPHON_OPERATOR_TOKEN_FILE
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = Join-Path (Join-Path $env:LOCALAPPDATA 'Antiphon') 'operator-token'
    }
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Operator token file not found at $path. The server creates it at startup; run this as the account the server runs under."
    }
    $token = (Get-Content -LiteralPath $path -Raw).Trim()
    if ([string]::IsNullOrEmpty($token)) { throw "Operator token file at $path is empty." }
    $headers['X-Antiphon-Operator-Token'] = $token
}

function Invoke-RunnerApi {
    param([string]$Method, [string]$Path, $Body)
    $uri = "$api$Path"
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }
    $json = $Body | ConvertTo-Json -Compress
    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType 'application/json' -Body $json
}

switch ($Verb) {
    'list' {
        Invoke-RunnerApi -Method Get -Path "/api/session-runners/$RunnerId/slots" | ConvertTo-Json -Depth 6
    }
    'release' {
        if ([string]::IsNullOrWhiteSpace($SessionId)) { throw 'release requires -SessionId' }
        if ([string]::IsNullOrWhiteSpace($Reason)) { throw 'release requires -Reason' }
        Add-OperatorToken
        $body = @{ reason = $Reason }
        Invoke-RunnerApi -Method Post -Path "/api/session-runners/$RunnerId/slots/$SessionId/release" -Body $body | ConvertTo-Json -Depth 4
    }
    'release-orphans' {
        if ([string]::IsNullOrWhiteSpace($Reason)) { throw 'release-orphans requires -Reason' }
        Add-OperatorToken
        $body = @{ reason = $Reason }
        Invoke-RunnerApi -Method Post -Path "/api/session-runners/$RunnerId/slots/release-orphans" -Body $body | ConvertTo-Json -Depth 4
    }
}
