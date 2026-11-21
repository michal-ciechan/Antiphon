#requires -Version 5.1
<#
.SYNOPSIS
    Windows-side local readiness evaluator (CARD-0487 S3, CARD-0545 D-9).

    Windmill path: u/lndcobra/antiphon_nightly_readiness on the desktop tag every
    30 minutes. It is not the independent monitor: outage detection and notification
    belong to the nightly watchdog (docs/nightly-watchdog.md), whose snapshot this
    evaluator folds into Health and last-monitor.json.

    W2-nightly S05: a Windows PowerShell 5.1 launcher. The evaluator is C#
    (scripts/lib/Antiphon.Nightly/NightlyHealth*.cs) run as `antiphon-scripts nightly-health`
    through scripts/lib/antiphon-cli.ps1: a warm build of this checkout (a no-op when nothing
    changed), run from a copy. The parameters, the Windmill command line and the exit code are
    unchanged: 0 healthy, 1 unhealthy, 2 usage; 9 when the CLI cannot be built or started (one
    stderr line; last-monitor.json is not written, goes stale and fails readiness closed).
    Nothing is printed to stdout. -PassThru returns the result record (ExitCode, Health,
    Notification, Receipt, RecordedAt, Identity) read back from the CLI's --result-file.
    -SeamsPath stays for binding only: seams are C# objects the in-process tests build
    (NightlyHealthTests), so a non-empty value refuses with exit 3 (seams-path-unsupported).

    ASCII-only. Credentials stay in managed stores; this script never logs a token.
#>
param(
    [string]$StateRoot = 'C:\Antiphon\nightly',
    [string]$SeamsPath = '',
    [string]$ExpectedScriptHash = '',
    [string]$ExpectedPolicyHash = '',
    [string]$AuthorizedDestination = '',
    [string]$RepositoryPath = '',
    [string]$ProjectId = '',
    [string]$WatchdogSnapshotUrl = '',
    [string]$ExpectedWatchdogInstanceId = '',
    [string]$ReadinessConfigPath = '',
    [switch]$PassThru
)

$ErrorActionPreference = 'Continue'
if (-not [string]::IsNullOrWhiteSpace($SeamsPath)) {
    [Console]::Error.WriteLine('nightly-health: seams-path-unsupported')
    if ($PassThru) { return [pscustomobject]@{ ExitCode = 3; Refusal = 'seams-path-unsupported' } }
    exit 3
}

. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @(
    '--state-root', $StateRoot,
    '--expected-script-hash', $ExpectedScriptHash,
    '--expected-policy-hash', $ExpectedPolicyHash,
    '--authorized-destination', $AuthorizedDestination,
    '--repository-path', $RepositoryPath,
    '--project-id', $ProjectId,
    '--watchdog-snapshot-url', $WatchdogSnapshotUrl,
    '--expected-watchdog-instance-id', $ExpectedWatchdogInstanceId,
    '--readiness-config-path', $ReadinessConfigPath
)
$resultFile = ''
if ($PassThru) {
    $resultFile = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'antiphon-nightly-health-' + [guid]::NewGuid().ToString('N') + '.json')
}
$code = Invoke-AntiphonCli -Command 'nightly-health' -Arguments $arguments -ResultFile $resultFile
if (-not $PassThru) { exit $code }

$result = $null
try {
    if ([IO.File]::Exists($resultFile)) { $result = [IO.File]::ReadAllText($resultFile) | ConvertFrom-Json }
} catch {
    $result = $null
} finally {
    try { if ([IO.File]::Exists($resultFile)) { [IO.File]::Delete($resultFile) } } catch { }
}
if ($null -eq $result) {
    # No record (exit 9, or a CLI that failed before writing): the same names, nothing evaluated.
    $result = [pscustomobject]@{ ExitCode = $code; Health = $null; Notification = $null; Receipt = $false; RecordedAt = ''; Identity = $null }
}
return $result
