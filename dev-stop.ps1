#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts dev-stop` (scripts/lib/Antiphon.Nightly/DevStack.cs): stop the simple-mode server
    (17281) and client (17282). Postgres stays up unless -IncludePostgres (docker compose down).

    Unit 37 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept so the runbooks' `.\dev-stop.ps1` still
    works. Agents run `scripts/antiphon dev-stop`; Windows runbooks `scripts\antiphon.cmd dev-stop`.

    -IncludePostgres is --include-postgres. The command's exit code is returned (0); 9 when the CLI cannot be built or
    started (one stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
param([switch]$IncludePostgres)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'scripts') 'lib') 'antiphon-cli.ps1')
$arguments = @()
if ($IncludePostgres) { $arguments += '--include-postgres' }
exit (Invoke-AntiphonCli -Command 'dev-stop' -Arguments $arguments)
