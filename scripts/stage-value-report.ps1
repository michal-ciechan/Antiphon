#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0272 S4. Read GET /api/stage-outcomes and print the per-stage hit-rate-vs-cost table:
    Rebase, Verify, Cleanup, Review, FollowUp, Deploy - runs, Found, Clean, Skipped, Failed,
    Unreported, hit %, USD spent, USD per finding, server seconds.

    HTTP goes through Invoke-Antiphon, the same helper card.ps1 uses (problem-details surfaced
    on failure, no token sent unless ANTIPHON_TASK_TOKEN is set), injectable via -HttpShim
    (param($Method, $Uri, $Headers, $Body)) for the fixture tests in test-stage-value-report.ps1.

    ASCII-only on purpose - parses under Windows PowerShell 5.1.

.PARAMETER Since
    ISO-8601 lower bound on RecordedAt (inclusive). Omitted = no lower bound.

.PARAMETER Until
    ISO-8601 upper bound on RecordedAt (inclusive). Omitted = no upper bound.

.PARAMETER Stage
    One of Rebase, Verify, Cleanup, Review, FollowUp, Deploy. Omitted = every stage.

.PARAMETER Card
    A card in any form it gets written down (CARD-0272, card-272, '#272', 272, or its guid).
    Resolved through GET /api/cards/{id} the same way card.ps1 resolves it. Omitted = every card.

.PARAMETER Json
    Print the raw StageOutcomeListDto (rows + summary) instead of the table.

.PARAMETER IncludeSuperseded
    Include every row, not just the latest per (task, stage). Matches ?latestOnly=false.

.PARAMETER Api
    Antiphon API base. Default $env:ANTIPHON_API or http://localhost:17202.

.PARAMETER HttpShim
    Injectable HTTP surface for tests. Scriptblock signature: param($Method, $Uri, $Headers, $Body).

.PARAMETER PassThru
    Return the parsed StageOutcomeListDto instead of just printing. For in-process tests.
#>
param(
    [string]$Since = '',
    [string]$Until = '',
    [ValidateSet('Rebase', 'Verify', 'Cleanup', 'Review', 'FollowUp', 'Deploy')]
    [string]$Stage = '',
    [string]$Card = '',
    [switch]$Json,
    [switch]$IncludeSuperseded,
    [string]$Api = '',
    [scriptblock]$HttpShim,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

if ([string]::IsNullOrWhiteSpace($Api)) { $Api = $env:ANTIPHON_API }
if ([string]::IsNullOrWhiteSpace($Api)) { $Api = 'http://localhost:17202' }
$Api = $Api.TrimEnd('/')

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}

function Invoke-Antiphon {
    param([string]$Method, [string]$Path, $Body)
    $uri = "$Api$Path"
    if ($HttpShim) {
        return & $HttpShim -Method $Method -Uri $uri -Headers $headers -Body $Body
    }
    try {
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 8 -Compress
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -Body $bytes `
                -ContentType 'application/json; charset=utf-8'
        }
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }
    catch {
        # Surface the server's own message, same as card.ps1: it names the field/ceiling/reason.
        $raw = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($raw)) { $raw = $_.Exception.Message }
        $parsed = $null
        try { $parsed = $raw | ConvertFrom-Json } catch { $parsed = $null }
        if ($null -ne $parsed -and $parsed.detail) {
            Write-Error ("Antiphon {0} {1} failed: {2}" -f $Method, $Path, $parsed.detail)
        }
        else {
            Write-Error "Antiphon $Method $Path failed: $raw"
        }
        exit 1
    }
}

function Get-CheckoutRoot {
    try {
        $toplevel = & git -C $PWD.Path rev-parse --show-toplevel 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($toplevel)) {
            return ([string]$toplevel).Trim()
        }
    }
    catch {
        # Outside a repo, or git missing: fall through to $PWD.
    }
    return $PWD.Path
}

function Resolve-CardId {
    param([string]$CardRef)
    if ([string]::IsNullOrWhiteSpace($CardRef)) { return $null }
    $cwd = Get-CheckoutRoot
    $path = "/api/cards/{0}?cwd={1}" -f `
        [uri]::EscapeDataString($CardRef.Trim()), [uri]::EscapeDataString($cwd)
    $resolved = Invoke-Antiphon -Method GET -Path $path
    return [string]$resolved.id
}

$cardId = Resolve-CardId -CardRef $Card

$queryParts = @()
if (-not [string]::IsNullOrWhiteSpace($Since)) { $queryParts += ('since={0}' -f [uri]::EscapeDataString($Since)) }
if (-not [string]::IsNullOrWhiteSpace($Until)) { $queryParts += ('until={0}' -f [uri]::EscapeDataString($Until)) }
if (-not [string]::IsNullOrWhiteSpace($Stage)) { $queryParts += ('stage={0}' -f [uri]::EscapeDataString($Stage)) }
if (-not [string]::IsNullOrWhiteSpace($cardId)) { $queryParts += ('cardId={0}' -f [uri]::EscapeDataString($cardId)) }
if ($IncludeSuperseded) { $queryParts += 'latestOnly=false' }

$queryString = ''
if ($queryParts.Count -gt 0) { $queryString = '?' + ($queryParts -join '&') }

$result = Invoke-Antiphon -Method GET -Path "/api/stage-outcomes$queryString"

if ($Json) {
    $result | ConvertTo-Json -Depth 8
    if ($PassThru) { return $result }
    exit 0
}

$summary = @($result.summary)
if ($summary.Count -eq 0) {
    Write-Output 'No stage outcomes in range.'
    if ($PassThru) { return $result }
    exit 0
}

function Format-Percent {
    param($Value)
    if ($null -eq $Value) { return '-' }
    return ('{0}%' -f $Value)
}

function Format-Usd {
    param($Value)
    if ($null -eq $Value) { return '-' }
    return ('${0:N2}' -f [decimal]$Value)
}

$header = 'Stage', 'Runs', 'Found', 'Clean', 'Skipped', 'Failed', 'Unreported', 'Hit%', 'UsdSpent', 'Usd/Finding', 'ServerSecs'
$lines = New-Object 'System.Collections.Generic.List[object]'
foreach ($row in $summary) {
    $lines.Add([ordered]@{
        Stage       = [string]$row.stage
        Runs        = [int]$row.runs
        Found       = [int]$row.found
        Clean       = [int]$row.clean
        Skipped     = [int]$row.skipped
        Failed      = [int]$row.failed
        Unreported  = [int]$row.unreported
        'Hit%'      = Format-Percent $row.hitPercent
        UsdSpent    = Format-Usd $row.usdSpent
        'Usd/Finding' = Format-Usd $row.usdPerFinding
        ServerSecs  = [int]$row.serverSecs
    })
}

$widths = @{}
foreach ($col in $header) {
    $widths[$col] = $col.Length
    foreach ($line in $lines) {
        $cellLen = [string]$line[$col]
        if ($cellLen.Length -gt $widths[$col]) { $widths[$col] = $cellLen.Length }
    }
}

function Write-Row {
    param($Cells)
    $parts = foreach ($col in $header) { ([string]$Cells[$col]).PadRight($widths[$col]) }
    Write-Output ($parts -join '  ')
}

$headerCells = [ordered]@{}
foreach ($col in $header) { $headerCells[$col] = $col }
Write-Row $headerCells
$rule = foreach ($col in $header) { ('-' * $widths[$col]) }
Write-Output ($rule -join '  ')
foreach ($line in $lines) { Write-Row $line }

if ($PassThru) { return $result }
exit 0
