# Explicit pane inspection/refusal. Ordinary Stop still detaches attached panes.
# Protocol-20 backends cannot safely execute disposal; inspect reports that blocker.
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('inspect', 'dispose', 'status')][string]$Verb,
    [string]$PaneId,
    [string]$ExpectedSessionId,
    [string]$ExpectedNativeSessionId,
    [string]$PreviewId,
    [string]$OperationId,
    [string]$Reason,
    [string]$ReasonFile,
    [switch]$Execute,
    [switch]$Json
)
$ErrorActionPreference = 'Stop'

function Require-Id([string]$Value, [string]$Name) {
    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParseExact($Value, 'D', [ref]$parsed) -or $parsed -eq [Guid]::Empty) {
        throw "$Name requires a full nonempty UUID."
    }
    return $parsed.ToString('D')
}

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')
$path = '/api/herdr/pane-disposals'
$method = 'POST'
$body = $null
switch ($Verb) {
    'inspect' {
        if ($PaneId.Length -gt 128 -or $PaneId -cnotmatch '\Aw[0-9A-Za-z]+:p[0-9A-Za-z]+\z') {
            throw 'PaneId requires one exact workspace-qualified pane ID.'
        }
        if (-not $ExpectedSessionId -and -not $ExpectedNativeSessionId) {
            throw 'ExpectedSessionId or ExpectedNativeSessionId is required.'
        }
        $body = @{ paneId = $PaneId }
        if ($ExpectedSessionId) { $body.expectedSessionId = Require-Id $ExpectedSessionId 'ExpectedSessionId' }
        if ($ExpectedNativeSessionId) { $body.expectedNativeSessionId = Require-Id $ExpectedNativeSessionId 'ExpectedNativeSessionId' }
        $path += '/preview'
    }
    'dispose' {
        $body = @{
            previewId = Require-Id $PreviewId 'PreviewId'
            operationId = Require-Id $OperationId 'OperationId'
        }
        if ($ReasonFile) {
            if ($Reason) { throw 'Use Reason or ReasonFile, not both.' }
            $Reason = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $ReasonFile).Path)
        }
        if ([string]::IsNullOrWhiteSpace($Reason) -or $Reason.Length -gt 4096) {
            throw 'A nonblank Reason or ReasonFile of at most 4096 characters is required.'
        }
        $body.reason = $Reason
        if (-not $Execute) {
            @{ dryRun = $true; method = $method; path = $path; body = $body } | ConvertTo-Json -Depth 8
            exit 0
        }
    }
    'status' {
        $path += '/' + (Require-Id $OperationId 'OperationId')
        $method = 'GET'
    }
}

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}
$argsForRequest = @{ Method = $method; Uri = "$api$path"; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 30 }
if ($null -ne $body) {
    $argsForRequest.ContentType = 'application/json; charset=utf-8'
    $argsForRequest.Body = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 8 -Compress))
}
$failed = $false
try {
    $response = Invoke-WebRequest @argsForRequest
    $result = $response.Content | ConvertFrom-Json
}
catch {
    $failed = $true
    # Never print the request/headers or an arbitrary transport exception.
    try { $result = $_.ErrorDetails.Message | ConvertFrom-Json }
    catch { $result = @{ code = 'herdr_disposal_transport_failed'; detail = 'Request failed. Read status using the same OperationId; do not automatically retry disposal.' } }
    if ($null -eq $result) { $result = @{ code = 'herdr_disposal_transport_failed' } }
}
if ($Json) { $result | ConvertTo-Json -Depth 16 }
elseif ($Verb -eq 'inspect' -and -not $failed) {
    Write-Output ("Pane {0}, tab {1}, workspace {2}; eligible={3}; guardAvailable={4}" -f $result.paneId, $result.tabId, $result.workspaceId, $result.eligible, $result.guardAvailable)
    Write-Output ("Preview {0}, expires {1}; blockers: {2}" -f $result.previewId, $result.expiresAtUtc, ($result.blockers -join ', '))
    Write-Output 'Foreground observations are incomplete. No process termination is authorized.'
}
else { $result | ConvertTo-Json -Depth 16 }
if ($failed) { exit 1 }
