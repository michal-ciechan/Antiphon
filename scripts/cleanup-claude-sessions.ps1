#requires -Version 5.1
<#
.SYNOPSIS
    Report (default) or archive/delete stale Claude Code remote-control sessions via the
    code-sessions REST API (CARD-0144). Default is a dry run; -Execute acts; -Action Archive
    (reversible) is the default and -Action Delete is not what a scheduled run should use.

    Recurring cleanup is a Windmill schedule on server2 (u/lndcobra/claude_session_cleanup),
    not a local Windows Scheduled Task - do not add one. scripts/install-cleanup-task.ps1 is
    the superseded local-task installer for a different job and must not gain a sibling.

    Unit 22 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: a Windows
    PowerShell 5.1 launcher kept because that schedule starts this file by path. The cleanup is
    C# (scripts/lib/Antiphon.Nightly/ClaudeSessionCleanup.cs) run as `antiphon-scripts
    cleanup-claude-sessions` through scripts/lib/antiphon-cli.ps1: a warm build of this
    checkout (a no-op when nothing changed), run from a copy. The parameters, their defaults
    and the exit codes are unchanged (0 done, 1 the list could not be read, 2 no or stale
    credentials, 3 truncated list, 4 over -MaxPerRun, 5 a mutation failed); 9 means the CLI
    could not be built or started (one stderr line), and an unparseable -Now is now a usage
    error (2). The fixture seams -HttpShim and -PassThru are gone: the cases run in process
    (ClaudeSessionCleanupTests). The access token is never printed, logged or written.

    ASCII-only: parseable under Windows PowerShell 5.1.
#>
[CmdletBinding()]
param(
    [int]$OlderThanDays = 7,
    [ValidateSet('Archive', 'Delete')]
    [string]$Action = 'Archive',
    [switch]$Execute,
    [int]$MaxPerRun = 10,
    [switch]$SkipUnread,
    [bool]$RequireDisconnected = $true,
    [string]$ReportPath = '',
    [string]$InputJson = '',
    [string]$LiveBridgesJson = '',
    [string]$SessionsDir = '',
    [string]$CredentialsPath = '',
    [string]$Now = ''
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$require = 'false'
if ($RequireDisconnected) { $require = 'true' }
$arguments = @(
    '--older-than-days', [string]$OlderThanDays,
    '--action', $Action,
    '--max-per-run', [string]$MaxPerRun,
    '--require-disconnected', $require,
    '--report-path', $ReportPath,
    '--input-json', $InputJson,
    '--live-bridges-json', $LiveBridgesJson,
    '--sessions-dir', $SessionsDir,
    '--credentials-path', $CredentialsPath,
    '--now', $Now
)
if ($Execute) { $arguments += '--execute' }
if ($SkipUnread) { $arguments += '--skip-unread' }
exit (Invoke-AntiphonCli -Command 'cleanup-claude-sessions' -Arguments $arguments)
