#requires -Version 5.1
<#
.SYNOPSIS
    Report (default) or delete clearly attributable Codex test residue. The state_5.sqlite reader
    is read-only; the only mutation path is `codex delete --force <thread-id>`.

    Recurring cleanup is a Windmill schedule on server2
    (u/lndcobra/antiphon_codex_residue_cleanup), Monday 09:30 Europe/London. It is report-only
    until the user explicitly changes the rollout. Do not add a Windows Scheduled Task.

    Unit 21 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: a Windows
    PowerShell 5.1 launcher kept because that schedule starts this file by path. The cleanup is
    C# (scripts/lib/Antiphon.Nightly/CodexTestResidue.cs) run as `antiphon-scripts
    cleanup-codex-test-residue` through scripts/lib/antiphon-cli.ps1: a warm build of this
    checkout (a no-op when nothing changed), run from a copy. The parameters, their defaults and
    the exit codes are unchanged (0 done, 1 the threads could not be read, 4 over -MaxPerRun,
    5 a delete failed); 2 is a usage error and 9 means the CLI could not be built or started
    (one stderr line). The fixture seams -CodexShim and -PassThru are gone: the cases run in
    process (CodexTestResidueTests).

    Default is dry-run. -Execute is deliberately opt-in. -MaxPerRun refuses rather than truncates.
    -InputJson classifies a saved threads dump and forces -Execute off.

    ASCII-only: parseable under Windows PowerShell 5.1.
#>
[CmdletBinding()]
param(
    [int]$OlderThanHours = 24,
    [switch]$Execute,
    [int]$MaxPerRun = 10,
    [string]$ReportPath = '',
    [string]$InputJson = '',
    [string]$CodexHome = '',
    [string]$StateDbPath = '',
    [string]$CodexExe = 'codex',
    [string]$LiveCwdsJson = '',
    [string]$LockIdsJson = '',
    [string]$Now = ''
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @(
    '--older-than-hours', [string]$OlderThanHours,
    '--max-per-run', [string]$MaxPerRun,
    '--report-path', $ReportPath,
    '--input-json', $InputJson,
    '--codex-home', $CodexHome,
    '--state-db-path', $StateDbPath,
    '--codex-exe', $CodexExe,
    '--live-cwds-json', $LiveCwdsJson,
    '--lock-ids-json', $LockIdsJson,
    '--now', $Now
)
if ($Execute) { $arguments += '--execute' }
exit (Invoke-AntiphonCli -Command 'cleanup-codex-test-residue' -Arguments $arguments)
