#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts dev-upgrade` (scripts/lib/Antiphon.Nightly/DevStack.cs): backup, stop, git pull, a
    stamped Release build of the server, npm install, then dev-start.

    Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept so the runbooks' `.\dev-upgrade.ps1` still
    works. Agents run `scripts/antiphon dev-upgrade`; Windows runbooks `scripts\antiphon.cmd dev-upgrade`.

    -NoBrowser is --no-browser (passed to dev-start). The command's exit code is returned (0 upgraded, 1 failed); 9 when
    the CLI cannot be built or started (one stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
param([switch]$NoBrowser)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($NoBrowser) { $arguments += '--no-browser' }
exit (Invoke-AntiphonCli -Command 'dev-upgrade' -Arguments $arguments)
