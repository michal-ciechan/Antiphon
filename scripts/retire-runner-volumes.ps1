# CARD-1609. Observation-only audited Close. No volume delete or drain command.
# Public manual Preview/Apply use the same protected host gate as retire-temp.
#requires -Version 7
[CmdletBinding()]
param(
    [switch]$Close, [switch]$DryRun, [switch]$Apply, [switch]$Preview, [switch]$Force,
    [string]$OperationId = '', [string]$ExpectedJournalDigest = '', [string]$ReasonFile = '',
    [string]$AuthorityFile = '', [string]$Sha = '',
    [switch]$Manual, [switch]$Template, [switch]$Resume,
    [string]$RetiredRunnerId = '', [string]$ProjectId = '',
    [string[]]$VolumeName = @(), [string]$ExpectedProofDigest = '',
    [string]$AttestationFile = '', [string]$EvidenceDirectory = ''
)
$ErrorActionPreference = 'Stop'

function Fail-Close([string]$Reason) {
    [Console]::Error.WriteLine("TempRetirementCloseRefused: $Reason")
    exit 2
}


function Test-OperationId([string]$Value) {
    return $Value -cmatch '^c1008[0-9a-f]{32}$'
}


function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}


function Test-Reparse([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $info = [System.IO.FileInfo]::new($Path)
    return (($info.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}


function Get-CloseAuthority {
    . (Join-Path $PSScriptRoot 'lib/runner-operator-token.ps1')
    try { $null = Get-RunnerOperatorToken }
    catch { Fail-Close 'Authority' }
    # A task token alone is never manual authority. A commissioned delegate
    # still uses the operator credential and an explicit structured authority.
    if (-not [string]::IsNullOrWhiteSpace($AuthorityFile)) {
        if (-not (Test-Path -LiteralPath $AuthorityFile -PathType Leaf) -or (Test-Reparse $AuthorityFile)) { Fail-Close 'Authority' }
        try { $grant = [IO.File]::ReadAllText($AuthorityFile) | ConvertFrom-Json }
        catch { Fail-Close 'Authority' }
        if ($grant.schema -ne 1 -or [string]$grant.actor -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$' -or
            [string]$grant.authorityRef -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$' -or
            [string]$grant.operationId -cne $OperationId) { Fail-Close 'Authority' }
        return [pscustomobject]@{ Actor = [string]$grant.actor; Authority = [string]$grant.authorityRef }
    }
    return [pscustomobject]@{ Actor = 'operator'; Authority = 'operator-token' }
}


function Invoke-Server2Bash([string]$Script, [int]$TimeoutMs = 30000) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'ssh'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    foreach ($token in @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15', 'mc@server2', 'bash', '-s')) {
        [void]$psi.ArgumentList.Add($token)
    }
    $proc = [System.Diagnostics.Process]::Start($psi)
    try {
        $proc.StandardInput.Write($Script)
        $proc.StandardInput.Close()
        $stdout = $proc.StandardOutput.ReadToEndAsync()
        $stderr = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit($TimeoutMs)) { $proc.Kill($true); $proc.WaitForExit(); Fail-Close 'JournalUnknown' }
        $text = $stdout.GetAwaiter().GetResult()
        [void]$stderr.GetAwaiter().GetResult()
        if ($proc.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($text)) { Fail-Close 'JournalUnknown' }
        return $text
    }
    finally { $proc.Dispose() }
}


function New-C1609CloseScript {
    param([string]$OperationId, [string]$ExpectedProofDigest, [string]$Reason, [string]$Actor, [string]$Authority)
    if (-not (Test-OperationId $OperationId) -or $ExpectedProofDigest -cnotmatch '^[0-9a-f]{64}$') { Fail-Close 'Context' }
    if ([string]::IsNullOrWhiteSpace($Reason) -or $Reason.Length -gt 4096) { Fail-Close 'ReasonFile' }
    $reason = $Reason.Trim()
    $coordinator = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'c1008-retirement.sh'))
    $remoteSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'c590-remote.sh'))
    $functions = foreach ($name in @('c1008_volume', 'c1008_rollout_lock', 'c1008_lock', 'c849_lock')) {
        $match = [regex]::Match($remoteSource, '(?ms)^' + $name + '\(\) \{\n.*?^\}')
        if (-not $match.Success) { Fail-Close 'CurrentTooling' }
        $match.Value
    }
    $packet = $coordinator + "`n" + ($functions -join "`n")
    $packetBytes = [Text.Encoding]::UTF8.GetBytes($packet)
    $packetBase64 = [Convert]::ToBase64String($packetBytes)
    $toolingDigest = Get-Sha256Hex $packetBytes
    $reason64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($reason))
    if ($actor -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$' -or
        $authority -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._:/@-]{0,255}$') { Fail-Close 'Authority' }
    $remote = @"
set -euo pipefail
umask 077
SERVER2_ROOT='/home/mc/antiphon-server2'
C1008_OPERATION='$OperationId'
C1008_PROJECT=antiphon-runner-temp
C1008_LOCK_HELD=0
C849_LOCK_HELD=0
LANE=host
C1609_CLOSE_TOOLING_DIGEST='$toolingDigest'
eval "`$(printf '%s' '$packetBase64' | base64 -d)"
require_lane() { [ "`$1" = host ]; }
write_result() { printf 'TempRetirementCloseRefused: %s\n' "`$2" >&2; exit "`$3"; }
c1609_custody "`$SERVER2_ROOT" dir || exit 2
for p in "`$SERVER2_ROOT/locks" "`$SERVER2_ROOT/locks/rollout.lock" "`$SERVER2_ROOT/locks/cache-maintenance.lock"; do
  if [ -e "`$p" ] || [ -L "`$p" ]; then
    if [ -d "`$p" ]; then c1609_custody "`$p" dir; else c1609_custody "`$p" file; fi || exit 2
  fi
done
C1609_CLOSE_DOCKER_EXECUTABLE="`$(command -v docker)"
[[ "`$C1609_CLOSE_DOCKER_EXECUTABLE" = /* ]] || exit 2
docker() { command timeout --kill-after=2s 10s "`$C1609_CLOSE_DOCKER_EXECUTABLE" "`$@"; }
c1008_lock
c1609_close_host '$ExpectedProofDigest' '$actor' '$authority' '$reason64'
"@
    $remote = "timeout --kill-after=5s 75s bash <<'C1609_CLOSE_PACKET_EOF'`n" + $remote + "`nC1609_CLOSE_PACKET_EOF`n"
    return $remote
}

function Invoke-C1609ManualEntry {
    if ($PSVersionTable.PSVersion.Major -lt 7) { Fail-Close 'PowerShell7Required' }
    if ($Force) { Fail-Close 'ForceRefused' }
    $modeCount = 0
    foreach ($flag in @($Close, $DryRun, $Apply, $Preview)) { if ($flag) { $modeCount++ } }
    if ($modeCount -ne 1) { Fail-Close 'ModeExclusive' }
    # Retire the old local-file test bypass as well as the census-only Preview.
    if ($env:C1609_RETIREMENT_ROOT -or $env:C1609_VOLUME_CENSUS_FILE) { Fail-Close 'LocalJournalOverrideRefused' }
    if (-not $Close) {
        . (Join-Path $PSScriptRoot 'c1609-manual.ps1')
        Invoke-C1609ManualRetirement
        return
    }
    if ($Manual -or $Template -or $Resume) { Fail-Close 'ModeExclusive' }
    if (-not (Test-OperationId $OperationId)) { Fail-Close 'OperationId' }
    if ($ExpectedJournalDigest -cnotmatch '^[0-9a-f]{64}$') { Fail-Close 'Digest' }
    if ([string]::IsNullOrWhiteSpace($ReasonFile) -or -not (Test-Path -LiteralPath $ReasonFile -PathType Leaf) -or
        (Test-Reparse $ReasonFile)) { Fail-Close 'ReasonFile' }
    $reason = [IO.File]::ReadAllText($ReasonFile).Trim()
    if ([string]::IsNullOrWhiteSpace($reason) -or $reason.Length -gt 4096) { Fail-Close 'ReasonFile' }
    $auth = Get-CloseAuthority
    $remote = New-C1609CloseScript -OperationId $OperationId -ExpectedProofDigest $ExpectedJournalDigest -Reason $reason -Actor $auth.Actor -Authority $auth.Authority
    try {
        $text = Invoke-Server2Bash -Script $remote -TimeoutMs 90000
        Write-Output $text.Trim()
        exit 0
    }
    catch { Fail-Close 'JournalUnknown' }
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-C1609ManualEntry }
