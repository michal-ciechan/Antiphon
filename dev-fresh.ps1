#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts dev-fresh` (scripts/lib/Antiphon.Nightly/DevStack.cs): the nuclear reset. After a
    typed 'yes' (or -Yes) it stops the server and client, drops the postgres volume (ALL DATA LOST), deletes the worktrees
    under C:\Antiphon\worktrees and starts the stack again (dev-start).

    Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept so the runbooks' `.\dev-fresh.ps1` still
    works. Agents run `scripts/antiphon dev-fresh`; Windows runbooks `scripts\antiphon.cmd dev-fresh`.

    -Yes is --yes; the prompt reads this console. The command's exit code is returned (0 done or aborted, 1 failed); 9
    when the CLI cannot be built or started (one stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
param([switch]$Yes)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($Yes) { $arguments += '--yes' }
exit (Invoke-AntiphonCli -Command 'dev-fresh' -Arguments $arguments)
