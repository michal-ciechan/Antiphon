#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts dev-aspire` (scripts/lib/Antiphon.Nightly/DevAspire.cs): start the desktop Aspire
    AppHost (dashboard mode) in the background. It refuses a linked worktree (CARD-0273) and a phone-home configuration,
    holds logs/apphost.launch.lock, checks Docker, brings up the dev compose services, restores and npm-installs (not with
    -NoBuild), then builds the AppHost stamped with HEAD and runs it detached (output in logs/apphost.log), waits up to 90 s
    for the dashboard (logs/apphost-dashboard-url.txt) and opens it (not with -NoBrowser). It does not stop a running
    AppHost: restart one with `scripts\antiphon.cmd restart-apphost`.

    Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept so the runbooks' `.\dev-aspire.ps1` still
    works. Agents run `scripts/antiphon dev-aspire`; Windows runbooks `scripts\antiphon.cmd dev-aspire`.

    -NoBuild, -NoBrowser and -AllowWorktree are --no-build, --no-browser and --allow-worktree. The command's exit code
    is returned (0 launched, 1 failed, 3 refused); 9 when the CLI cannot be built or started (one stderr line).
    ASCII-only: parseable under Windows PowerShell 5.1.
#>
param([switch]$NoBuild, [switch]$NoBrowser, [switch]$AllowWorktree)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($NoBuild) { $arguments += '--no-build' }
if ($NoBrowser) { $arguments += '--no-browser' }
if ($AllowWorktree) { $arguments += '--allow-worktree' }
exit (Invoke-AntiphonCli -Command 'dev-aspire' -Arguments $arguments)
