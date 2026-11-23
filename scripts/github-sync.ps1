#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts github-sync` (scripts/lib/Antiphon.Nightly/GithubSync.cs):
    trigger Antiphon's bidirectional GitHub Issues <-> cards sync (CARD-0166 S7, CARD-0171).

    Unit 25 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept only because
    Windmill on server2 starts this file by path as u/lndcobra/antiphon_github_sync, every 3 hours
    (`0 0 */3 * * *`, Europe/London, desktop tag, SSH bridge) with -Notify. Delete it when that job
    runs the command. Agents and operators run `scripts/antiphon github-sync` (Windows:
    scripts\antiphon.cmd github-sync) directly.

    The parameters are the script's: -BoardId <guid> (else every board whose tracker is not
    Internal), -BaseUrl (default http://localhost:17202) and -Notify. The command's exit code is
    returned (0 every board synced cleanly; 1 the request failed or a board reported an error);
    9 when the CLI cannot be built or started (one stderr line). ASCII-only: parseable under
    Windows PowerShell 5.1.
#>
param(
    [Guid]$BoardId,
    [string]$BaseUrl = 'http://localhost:17202',
    [switch]$Notify
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @('--base-url', $BaseUrl)
if ($PSBoundParameters.ContainsKey('BoardId')) { $arguments = @('--board-id', $BoardId.ToString()) + $arguments }
if ($Notify) { $arguments += '--notify' }
exit (Invoke-AntiphonCli -Command 'github-sync' -Arguments $arguments)
