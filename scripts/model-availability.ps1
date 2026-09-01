# Mark a model/kind unavailable (or clear that hold) without composing HTTP by hand.
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1, which reads a
# no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
#
# Verbs:
#   model-availability.ps1 get   [-Json]
#   model-availability.ps1 hold  -Kind ClaudeCode -Model fable [-Until 2026-09-04T00:00:00Z] [-Reason r]
#   model-availability.ps1 hold  -Kind ClaudeCode -Model * -Until 2026-09-04T00:00:00Z
#   model-availability.ps1 clear -Kind ClaudeCode -Model fable
#
# -Until is ISO-8601 UTC: a trailing Z or a numeric offset is required. Naive local timestamps
# are refused here rather than guessed. Omit -Until for an open-ended hold (until DELETE).
# Alias * is a kind-wide hold (OR'd with per-alias rows). This is not a card.ps1 overload.
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('get', 'hold', 'clear')]
    [string]$Verb = 'get',

    [ValidateSet('ClaudeCode', 'Grok', 'Codex')]
    [string]$Kind,

    [string]$Model,

    [string]$Until,

    [string]$Reason,

    [switch]$Json
)

$ErrorActionPreference = 'Stop'

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}

function Invoke-Antiphon {
    param([string]$Method, [string]$Path, $Body, [switch]$NoContent)
    $uri = "$api$Path"
    try {
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 8 -Compress
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
            $params = @{
                Method      = $Method
                Uri         = $uri
                Headers     = $headers
                Body        = $bytes
                ContentType = 'application/json; charset=utf-8'
            }
            if ($NoContent) {
                Invoke-RestMethod @params | Out-Null
                return $null
            }
            return Invoke-RestMethod @params
        }
        if ($NoContent) {
            Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers | Out-Null
            return $null
        }
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }
    catch {
        $raw = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($raw)) { $raw = $_.Exception.Message }
        $parsed = $null
        try { $parsed = $raw | ConvertFrom-Json } catch { $parsed = $null }
        if ($null -ne $parsed -and $parsed.detail) {
            $lines = @($parsed.detail)
            if ($parsed.code) { $lines += ("code {0}" -f $parsed.code) }
            Write-Error ("Antiphon {0} {1} failed: {2}" -f $Method, $Path, ($lines -join [Environment]::NewLine))
        }
        else {
            Write-Error "Antiphon $Method $Path failed: $raw"
        }
        exit 1
    }
}

function Assert-UtcOffset {
    param([string]$Value)
    if ($Value -notmatch '(Z|[+-][0-9]{2}:[0-9]{2})$') {
        Write-Error 'Until must be ISO-8601 UTC with a Z or numeric offset (e.g. 2026-09-04T00:00:00Z). Naive local timestamps are refused.'
        exit 1
    }
}

function Format-HoldLine {
    param($Hold)
    $until = if ($null -eq $Hold.disabledUntil -or [string]::IsNullOrWhiteSpace([string]$Hold.disabledUntil)) {
        'until cleared'
    }
    else {
        "until $($Hold.disabledUntil)"
    }
    $reason = $Hold.reason
    '{0}  {1}  {2}  {3}  {4}' -f $Hold.kind, $Hold.modelAlias, $Hold.source, $until, $reason
}

switch ($Verb) {
    'get' {
        $snapshot = Invoke-Antiphon -Method GET -Path '/api/model-availability'
        if ($Json) {
            $snapshot | ConvertTo-Json -Depth 8
            break
        }
        $holds = @($snapshot.holds)
        if ($holds.Count -eq 0) {
            Write-Output 'All models available.'
        }
        else {
            Write-Output 'holds:'
            foreach ($hold in $holds) {
                Write-Output ('  ' + (Format-HoldLine -Hold $hold))
            }
        }
        $available = @($snapshot.available) -join ', '
        Write-Output ("available: {0}" -f $available)
    }
    'hold' {
        if ([string]::IsNullOrWhiteSpace($Kind) -or [string]::IsNullOrWhiteSpace($Model)) {
            Write-Error 'hold requires -Kind and -Model.'
            exit 1
        }
        if (-not [string]::IsNullOrWhiteSpace($Until)) {
            Assert-UtcOffset -Value $Until
        }
        $body = @{}
        if (-not [string]::IsNullOrWhiteSpace($Until)) { $body['disabledUntil'] = $Until }
        if (-not [string]::IsNullOrWhiteSpace($Reason)) { $body['reason'] = $Reason }
        if ($body.Count -eq 0) { $body['reason'] = 'manual hold' }
        $encoded = [uri]::EscapeDataString($Model)
        $hold = Invoke-Antiphon -Method PUT -Path ("/api/model-availability/{0}/{1}" -f $Kind, $encoded) -Body $body
        Write-Output (Format-HoldLine -Hold $hold)
    }
    'clear' {
        if ([string]::IsNullOrWhiteSpace($Kind) -or [string]::IsNullOrWhiteSpace($Model)) {
            Write-Error 'clear requires -Kind and -Model.'
            exit 1
        }
        $encoded = [uri]::EscapeDataString($Model)
        Invoke-Antiphon -Method DELETE -Path ("/api/model-availability/{0}/{1}" -f $Kind, $encoded) -NoContent
    }
}
