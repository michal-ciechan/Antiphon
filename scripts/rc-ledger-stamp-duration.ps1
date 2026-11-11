#requires -Version 5.1
# CARD-1842 S6. Copy an rc ledger to a new path with durations from timing sidecars and
# TRX. -IncludeHost is the only path that writes antiphon-rc-ledger-v2 and a chunk host;
# without it the output is v1 with no host property. The host is the caller's argument,
# never the machine name. Refuses an existing output path. No nightly caller (CARD-1836).
# ASCII-only. Windows PowerShell 5.1.
param(
    [Parameter(Mandatory = $true)][string]$LedgerPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string[]]$TimingPath = @(),
    [string[]]$TrxPath = @(),
    [switch]$IncludeHost,
    [string]$HostId,
    [string]$Os
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/rc-ledger.ps1')
if ([System.IO.File]::Exists($OutputPath)) { throw 'ledger-exists' }
if (-not $IncludeHost -and (-not [string]::IsNullOrEmpty($HostId) -or -not [string]::IsNullOrEmpty($Os))) { throw 'ledger-host: -HostId and -Os need -IncludeHost' }
if ($IncludeHost) {
    if (-not (Test-RcRequiredText $HostId)) { throw 'ledger-host: hostId' }
    if ($Os -cne 'windows' -and $Os -cne 'linux') { throw 'ledger-host: os' }
}
$document = Read-RcLedger -Path $LedgerPath
$document = Set-RcLedgerDurations -Document $document -TimingPath $TimingPath -TrxPath $TrxPath
if ($IncludeHost) {
    $document.format = 'antiphon-rc-ledger-v2'
    $document = Set-RcLedgerHost -Document $document -HostId $HostId -Os $Os
}
Write-RcLedger -Path $OutputPath -Document $document
