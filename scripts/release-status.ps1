#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0599 D-9: answer whether a running build is a published release.

    Reads /api/version from the service under test, then looks the running SHA up
    by EXACT match against the published release manifests. It returns the
    running SHA and capabilities plus either the matched release name/URL or
    'unreleased integration build'. Missing GitHub/publication data is reported
    as 'unknown', never as the latest release.

    An ancestor tag, or simply the newest release, is not the identity of a newer
    running SHA. This script performs no restart, deploy, checkout or kill.

    ASCII-only on purpose - parseable under Windows PowerShell 5.1.
#>
param(
    [string]$ReleaseRoot = 'C:\Antiphon\releases',
    [string]$VersionUrl = 'http://127.0.0.1:17202/api/version',
    [string]$Sha = '',
    [string]$SeamsPath = '',
    [switch]$PassThru
)

$ErrorActionPreference = 'Continue'

$lib = Join-Path $PSScriptRoot 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'release-gate.ps1')

function Get-ReleaseGateRunningVersion {
    param([string]$VersionUrl)
    if ($script:ReleaseGateSeams -and $script:ReleaseGateSeams.Version) {
        return $script:ReleaseGateSeams.Version.Invoke($VersionUrl)
    }
    try {
        $body = Invoke-RestMethod -Uri $VersionUrl -Method Get -TimeoutSec 10 -ErrorAction Stop
        return [pscustomobject]@{ Ok = $true; Body = $body }
    } catch {
        return [pscustomobject]@{ Ok = $false; Body = $null; Error = $_.Exception.Message }
    }
}

function Invoke-AntiphonReleaseStatus {
    param([string]$ReleaseRoot, [string]$VersionUrl, [string]$Sha, [string]$SeamsPath)
    Import-ReleaseGateSeams -SeamsPath $SeamsPath
    $status = [ordered]@{
        sha = ''
        capabilities = @()
        state = 'unknown'
        tag = ''
        releaseUrl = ''
        detail = ''
    }
    if ([string]::IsNullOrWhiteSpace($Sha)) {
        $version = Get-ReleaseGateRunningVersion -VersionUrl $VersionUrl
        if (-not $version.Ok -or $null -eq $version.Body) {
            $status.state = 'unknown'
            $status.detail = 'version endpoint unavailable'
            return [pscustomobject]$status
        }
        $status.sha = [string]$version.Body.version
        if ($version.Body.capabilities) { $status.capabilities = @($version.Body.capabilities) }
    } else {
        $status.sha = $Sha
    }

    $journalPath = Join-Path $ReleaseRoot 'publications.json'
    if (-not (Test-Path -LiteralPath $journalPath)) {
        $status.state = 'unknown'
        $status.detail = 'no publication journal'
        return [pscustomobject]$status
    }
    $journal = Get-ReleaseGatePublicationJournal -Path $journalPath
    foreach ($row in @($journal.reservations)) {
        if (-not [bool]$row.published) { continue }
        # EXACT sha match only. An ancestor tag is not this build's identity.
        if ([string]::Equals([string]$row.sha, $status.sha, [StringComparison]::OrdinalIgnoreCase)) {
            $status.state = 'released'
            $status.tag = [string]$row.tag
            $status.releaseUrl = [string]$row.releaseUrl
            return [pscustomobject]$status
        }
    }
    $status.state = 'unreleased integration build'
    $status.detail = 'no published release manifest names this sha'
    return [pscustomobject]$status
}

$invokeResult = Invoke-AntiphonReleaseStatus -ReleaseRoot $ReleaseRoot -VersionUrl $VersionUrl -Sha $Sha -SeamsPath $SeamsPath
if ($PassThru) { return $invokeResult }
Write-Host (ConvertTo-Json -InputObject $invokeResult -Compress)
exit 0
