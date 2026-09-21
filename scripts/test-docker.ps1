# CARD-0590 foreground test entry. Groups: small, backend, all.
param(
    [string]$Group = '',
    [string]$Manifest = '',
    [switch]$ThrowawayStack
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'c590-command.ps1')

if (-not $Manifest -or -not (Test-Path -LiteralPath $Manifest)) {
    Write-Error 'Manifest is required'
    exit 2
}
$m = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
$allowed = @('small', 'backend', 'all')
if ($allowed -notcontains $Group) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'UnknownGroup' -ExitCode 2
}

$task = $m.task
if ($null -eq $task -or -not [bool]$task.readable) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'TaskBindingUnavailable' -ExitCode 2
}
if ($null -ne $task.sourceLandingOperationId -and [string]$task.sourceLandingOperationId -ne '') {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'SourcedTask' -ExitCode 2
}

function Test-InsideCheckout {
    param([string]$PathValue, [string]$RootValue, [string]$Canonical)
    $candidate = if ($Canonical) { $Canonical } else { [System.IO.Path]::GetFullPath($PathValue) }
    $root = [System.IO.Path]::GetFullPath($RootValue).TrimEnd('\', '/')
    $normalized = $candidate.TrimEnd('\', '/')
    if ($normalized.Length -ge $root.Length -and $normalized.Substring(0, $root.Length).Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
        if ($normalized.Length -eq $root.Length) { return $true }
        $next = $normalized[$root.Length]
        if ($next -eq '\' -or $next -eq '/') { return $true }
    }
    return $false
}

$canonical = ''
if ($m.PSObject.Properties.Name -contains 'canonicalPath' -and $m.canonicalPath) { $canonical = [string]$m.canonicalPath }
$source = [string]$m.sourceRoot
if ($source -match '(?i)(^|[\\/])verification([\\/]|$)') {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'VerificationPath' -ExitCode 2
}
if (-not (Test-InsideCheckout -PathValue $source -RootValue ([string]$m.checkoutRoot) -Canonical $canonical)) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'OutsideCheckout' -ExitCode 2
}

if (-not [bool]$m.socket.present) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'MissingSocket' -ExitCode 2
}
$groups = @($m.socket.groups | ForEach-Object { [string]$_ })
if ($groups -notcontains [string]$m.socket.gid) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'SocketGroupMismatch' -ExitCode 2
}
if (-not [bool]$m.dbProbeOk) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'UnreachableDatabase' -ExitCode 2
}

$child = Invoke-C590 -Exe 'checkpoint' -ArgumentList @($Group)
$exit = [int]$m.childExit
if (-not [bool]$m.reportPresent) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'MissingReport' -ExitCode 2
}
if ([string]$m.report.runId -ne [string]$m.runId) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'StaleResult' -ExitCode 2
}
if ([int]$m.report.executed -eq 0) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'ZeroExecuted' -ExitCode 2
}
$expected = @($m.expectedClasses)
$actual = @($m.report.classes)
foreach ($name in $expected) {
    if ($actual -notcontains $name) {
        Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis ('MissingClass ' + $name) -ExitCode 2
    }
}
if ([int]$m.report.skipped -gt 0) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'UnexpectedSkip' -ExitCode 2
}
if ([int]$m.report.failed -gt 0 -and $exit -eq 0) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'ResultExitMismatch' -ExitCode 2
}
if ([string]$m.artifactDigest -ne [string]$m.copiedDigest) {
    Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $false -Diagnosis 'DigestMismatch' -ExitCode 2
}

Invoke-C590 -Exe 'docker' -ArgumentList @('cp', 'export')
Invoke-C590 -Exe 'hash' -ArgumentList @('artifacts')
Invoke-C590 -Exe 'commit-manifest' -ArgumentList @('result-ready')
Invoke-C590 -Exe 'docker' -ArgumentList @('rm', 'container')
if ($exit -ne 0) { exit $exit }
Write-C590Result -EvidenceRoot $m.evidenceRoot -Accepted $true -Diagnosis '' -ExitCode 0
