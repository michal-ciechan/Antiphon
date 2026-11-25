#requires -Version 5.1
<#
.SYNOPSIS
    Desktop entry for the daily plain snapshot. Runs the dump, then posts one status line.

    Thin launcher for `antiphon-scripts postgres-snapshot-job` (scripts/lib/Antiphon.Nightly/PostgresSnapshotJob.cs), unit 53 of
    docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md. Kept because Windmill's u/lndcobra/antiphon_postgres_snapshot
    (07:50, desktop tag, SSH bridge) starts the runner copy, C:\Users\lndco\antiphon-snapshot-runner\invoke-postgres-snapshot.ps1,
    by path; the job definition is unchanged. The command reads the one word in snapshot-target next to this runner copy
    (desktop or server2; anything else fails closed with reason=target-invalid and still posts the FAIL line), resolves the
    pinned postgres-snapshot.ps1 (its four source literals and SOURCE-SHA beside this file), runs it as a Windows PowerShell
    child, prints its lines, writes job-status\db-backup.json and posts the line. Its exit code is returned; 9 when the CLI
    cannot be built or started (one stderr line, nothing posted).

    The checkout's scripts/lib/antiphon-cli.ps1 runs the command; the runner copy (no checkout around it) runs C:\src\Antiphon's.
    Flip and roll back the word only through Set-SnapshotTarget, dot-sourced from the runner copy (the job does not run):
    powershell -NoProfile -Command ". C:\Users\lndco\antiphon-snapshot-runner\invoke-postgres-snapshot.ps1; Set-SnapshotTarget desktop"
    It must print SNAPSHOT TARGET desktop (`scripts\antiphon.cmd postgres-snapshot-target --word desktop --runner-dir <dir>` is
    the same command). ASCII-only: parseable under Windows PowerShell 5.1. No secrets on stdout.
#>
$ErrorActionPreference = 'Continue'
$cli = Join-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'lib') 'antiphon-cli.ps1'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { $cli = 'C:\src\Antiphon\scripts\lib\antiphon-cli.ps1' }
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
    [Console]::Error.WriteLine('antiphon-cli: no scripts\lib\antiphon-cli.ps1 above this file or in C:\src\Antiphon')
    if ($MyInvocation.InvocationName -eq '.') { return }
    exit 9
}
. $cli

function Set-SnapshotTarget {
    # The command prints SNAPSHOT TARGET <word> (or the refusal); the answer is the word, or '' when it was not written.
    param([string]$Word, [string]$Dir = '')
    if ([string]::IsNullOrWhiteSpace($Dir)) { $Dir = $PSScriptRoot }
    $code = Invoke-AntiphonCli -Command 'postgres-snapshot-target' -Verbatim @('--word', $Word, '--runner-dir', $Dir)
    if ($code -ne 0) { return '' }
    return $Word
}

if ($MyInvocation.InvocationName -eq '.') { return }
exit (Invoke-AntiphonCli -Command 'postgres-snapshot-job' -Arguments @('--runner-dir', $PSScriptRoot))
