# CARD-0330 S4: read the output-distiller ledger.
#
# Usage:
#   pwsh -File scripts/distiller.ps1 -Stats [-Since 7d]
#   pwsh -File scripts/distiller.ps1 -List [-Flagged] [-Since 7d]
[CmdletBinding(DefaultParameterSetName = 'Stats')]
param(
    [Parameter(ParameterSetName = 'Stats', Mandatory = $true)]
    [switch]$Stats,

    [Parameter(ParameterSetName = 'List', Mandatory = $true)]
    [switch]$List,

    [Parameter(ParameterSetName = 'List')]
    [switch]$Flagged,

    [Parameter(ParameterSetName = 'Stats')]
    [Parameter(ParameterSetName = 'List')]
    [string]$Since
)

$ErrorActionPreference = 'Stop'
$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

function Convert-Since([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    $trimmed = $value.Trim()
    if ($trimmed -match '^(\d+)d$') {
        return (Get-Date).ToUniversalTime().AddDays(-[int]$Matches[1]).ToString('o')
    }
    return $trimmed
}

$query = @{}
$sinceIso = Convert-Since $Since
if ($sinceIso) { $query['since'] = $sinceIso }

function Join-Query($pairs) {
    $bits = @()
    foreach ($key in $pairs.Keys) {
        $bits += ('{0}={1}' -f [uri]::EscapeDataString($key), [uri]::EscapeDataString([string]$pairs[$key]))
    }
    if ($bits.Count -eq 0) { return '' }
    return '?' + ($bits -join '&')
}

try {
    if ($PSCmdlet.ParameterSetName -eq 'Stats') {
        $dto = Invoke-RestMethod -Method GET -Uri ($api + '/api/distillations/stats' + (Join-Query $query))
        Write-Output ("total={0} cost=${1} fullReadRate={2}" -f $dto.total, $dto.costUsd, $dto.fullReadRate)
        if ($dto.byOutcome) {
            Write-Output 'byOutcome:'
            $dto.byOutcome.PSObject.Properties | ForEach-Object { Write-Output ("  {0} {1}" -f $_.Name, $_.Value) }
        }
        if ($dto.byFeedback) {
            Write-Output 'byFeedback:'
            $dto.byFeedback.PSObject.Properties | ForEach-Object { Write-Output ("  {0} {1}" -f $_.Name, $_.Value) }
        }
        if ($dto.topMissingAnchorClasses) {
            Write-Output 'topMissing:'
            foreach ($row in $dto.topMissingAnchorClasses) { Write-Output ("  {0}" -f $row) }
        }
        return
    }

    $dto = Invoke-RestMethod -Method GET -Uri ($api + '/api/distillations' + (Join-Query $query))
    if ($Flagged) {
        $dto = @($dto | Where-Object { $_.feedback -eq 'LostInformation' -or $_.feedback -eq 'Noisy' })
    }
    foreach ($row in $dto) {
        $flag = if ($row.feedback -and $row.feedback -ne 'None') { $row.feedback } else { '-' }
        Write-Output ("{0}  {1}  {2}->{3}  {4}  flag={5}" -f $row.taskShortId, $row.outcome, $row.rawChars, $row.distilledChars, $row.bundleStamp, $flag)
    }
}
catch {
    $detail = $_.ErrorDetails.Message
    if ([string]::IsNullOrWhiteSpace($detail)) { $detail = $_.Exception.Message }
    Write-Error "distiller.ps1 failed: $detail"
    exit 1
}
