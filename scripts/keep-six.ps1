# Read keep-six status, declare a hold, or acknowledge a reminder.
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1, which reads a
# no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
#
# Verbs:
#   keep-six.ps1
#       Read GET /api/orchestrators/keep-six. This does not create a task.
#   keep-six.ps1 -Hold -Agent <guid> -Reason <text> -Until <iso8601>
#       POST /api/orchestrators/{agent}/keep-six/hold. Reason is 1 to 200 characters.
#       Until is in the future and at most 7 days ahead.
#   keep-six.ps1 -ClearHold -Agent <guid>
#       DELETE /api/orchestrators/{agent}/keep-six/hold.
#   keep-six.ps1 -Ack -Agent <guid>
#       POST /api/orchestrators/{agent}/keep-six/ack. The pending reminder stays pending.
#
# -Hold, -ClearHold, and -Ack are the verbs. Hold, clear, and ack send
# X-Antiphon-Task-Token from ANTIPHON_TASK_TOKEN. The token is a header only.
# It is never written to stdout or stderr. This script does not create a task.
[CmdletBinding(DefaultParameterSetName = 'Status')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Hold')]
    [switch]$Hold,

    [Parameter(Mandatory = $true, ParameterSetName = 'Ack')]
    [switch]$Ack,

    [Parameter(Mandatory = $true, ParameterSetName = 'ClearHold')]
    [switch]$ClearHold,

    [Parameter(Mandatory = $true, ParameterSetName = 'Hold')]
    [Parameter(Mandatory = $true, ParameterSetName = 'Ack')]
    [Parameter(Mandatory = $true, ParameterSetName = 'ClearHold')]
    [string]$Agent,

    [Parameter(Mandatory = $true, ParameterSetName = 'Hold')]
    [string]$Reason,

    [Parameter(Mandatory = $true, ParameterSetName = 'Hold')]
    [string]$Until
)

$ErrorActionPreference = 'Stop'

function Fail-Local {
    param([string]$Message)
    Write-Error $Message
    exit 1
}

function Test-GuidText {
    param([string]$Value)
    $parsed = [guid]::Empty
    return [guid]::TryParse($Value, [ref]$parsed)
}

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

$token = $env:ANTIPHON_TASK_TOKEN
$headers = @{}
if ($PSCmdlet.ParameterSetName -ne 'Status') {
    if ([string]::IsNullOrWhiteSpace($token)) {
        Fail-Local 'ANTIPHON_TASK_TOKEN is required for hold, clear, and ack.'
    }
    $headers['X-Antiphon-Task-Token'] = $token
}
elseif (-not [string]::IsNullOrWhiteSpace($token)) {
    $headers['X-Antiphon-Task-Token'] = $token
}

function Invoke-Api {
    param([string]$Method, [string]$Path, [string]$Body)
    $uri = "$api$Path"
    try {
        if ($null -ne $Body) {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -Body $bytes -ContentType 'application/json; charset=utf-8'
        }
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }
    catch {
        $raw = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($raw)) { $raw = $_.Exception.Message }
        Write-Error ("Antiphon {0} {1} failed: {2}" -f $Method, $Path, $raw)
        exit 1
    }
}

function Write-Json {
    param($Value)
    Write-Output ($Value | ConvertTo-Json -Depth 30 -Compress)
}

if ($PSCmdlet.ParameterSetName -eq 'Status') {
    Write-Json (Invoke-Api 'GET' '/api/orchestrators/keep-six' $null)
    exit 0
}

if (-not (Test-GuidText $Agent)) {
    Fail-Local 'agent must be a GUID.'
}

if ($PSCmdlet.ParameterSetName -eq 'Ack') {
    Write-Json (Invoke-Api 'POST' ("/api/orchestrators/$Agent/keep-six/ack") $null)
    exit 0
}

if ($PSCmdlet.ParameterSetName -eq 'ClearHold') {
    Write-Json (Invoke-Api 'DELETE' ("/api/orchestrators/$Agent/keep-six/hold") $null)
    exit 0
}

$reasonText = $Reason.Trim()
if ($reasonText.Length -lt 1 -or $reasonText.Length -gt 200) {
    Fail-Local 'reason is required and must be at most 200 characters.'
}

$parsedUntil = [datetimeoffset]::MinValue
if (-not [datetimeoffset]::TryParse($Until, [ref]$parsedUntil)) {
    Fail-Local 'until must be a date and time.'
}
$untilUtc = $parsedUntil.ToUniversalTime()
$now = [datetimeoffset]::UtcNow
if ($untilUtc -le $now -or $untilUtc -gt $now.AddDays(7)) {
    Fail-Local 'until must be in the future and at most 7 days ahead.'
}

$reasonJson = ConvertTo-Json -InputObject $reasonText -Compress
$untilJson = ConvertTo-Json -InputObject ($untilUtc.ToString('yyyy-MM-ddTHH:mm:ssZ')) -Compress
$body = '{"reason":' + $reasonJson + ',"until":' + $untilJson + '}'
Write-Json (Invoke-Api 'POST' ("/api/orchestrators/$Agent/keep-six/hold") $body)
exit 0
