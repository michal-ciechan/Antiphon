# Drain or clear a phone-home runner (CARD-0727).
# ASCII-only: this script must parse under Windows PowerShell 5.1.
#
#   runner-drain.ps1 status [-RunnerId server2]
#   runner-drain.ps1 drain  -Reason "why" [-RunnerId server2] [-RedirectTo server2-temp] [-RetireWhenIdle]
#   runner-drain.ps1 clear  -Reason "why" [-RunnerId server2]
#
# drain and clear send the operator token from the owner-only file the server creates at
# startup (default %LOCALAPPDATA%\Antiphon\operator-token; override with
# ANTIPHON_OPERATOR_TOKEN_FILE). The token is never printed. A status that is not HTTP 200,
# including 404, is not eligible.
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('status', 'drain', 'clear')]
    [string]$Verb,

    [string]$RunnerId = 'server2',

    [string]$Reason,

    [string]$RedirectTo,

    [switch]$RetireWhenIdle
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

function Get-ResponseStatus {
    param($ErrorRecord)
    $response = $ErrorRecord.Exception.Response
    if ($null -eq $response) { return $null }
    return [int]$response.StatusCode
}

function Invoke-RunnerApi {
    param([string]$Method, [string]$Path, $Body)
    $uri = "$api$Path"
    try {
        if ($null -eq $Body) {
            $response = Invoke-WebRequest -Method $Method -Uri $uri -Headers $headers -UseBasicParsing -ErrorAction Stop
        }
        else {
            $json = $Body | ConvertTo-Json -Compress
            $response = Invoke-WebRequest -Method $Method -Uri $uri -Headers $headers -ContentType 'application/json' -Body $json -UseBasicParsing -ErrorAction Stop
        }
    }
    catch {
        $status = Get-ResponseStatus $_
        if ($status -eq 404) {
            throw "Runner '$RunnerId' is not eligible (HTTP 404)."
        }
        if ($null -eq $status) { throw }
        throw "Runner '$RunnerId' request failed with HTTP $status."
    }

    if ([int]$response.StatusCode -ne 200) {
        throw "Runner '$RunnerId' is not eligible (HTTP $([int]$response.StatusCode))."
    }
    if ([string]::IsNullOrWhiteSpace($response.Content)) {
        Write-Output ("HTTP {0}" -f [int]$response.StatusCode)
        return
    }
    Write-Output $response.Content
}

switch ($Verb) {
    'status' {
        Invoke-RunnerApi -Method Get -Path "/api/session-runners/$RunnerId/status"
    }
    'drain' {
        if ([string]::IsNullOrWhiteSpace($Reason)) { throw 'drain requires -Reason' }
        Add-OperatorToken
        $body = @{ reason = $Reason; retireWhenIdle = [bool]$RetireWhenIdle }
        if (-not [string]::IsNullOrWhiteSpace($RedirectTo)) { $body.redirectTo = $RedirectTo }
        Invoke-RunnerApi -Method Post -Path "/api/session-runners/$RunnerId/drain" -Body $body
    }
    'clear' {
        if ([string]::IsNullOrWhiteSpace($Reason)) { throw 'clear requires -Reason' }
        Add-OperatorToken
        $body = @{ reason = $Reason }
        Invoke-RunnerApi -Method Post -Path "/api/session-runners/$RunnerId/drain/clear" -Body $body
    }
}
