#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts recover-repository-children`
    (scripts/lib/Antiphon.Nightly/RepositoryChildRecovery.cs).

    Unit 23 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept only because
    scripts/cutover-postgres-server2.ps1 starts this file by path (the repository preview before
    StopOnly), and the owner keeps that one-off as PowerShell (unit 55). Operators run
    `scripts/antiphon recover-repository-children` (Windows: scripts\antiphon.cmd ...) directly.

    The parameters are the script's: -Repository (preview), plus -Execute and
    -ConfirmDescendantsExited. The command's exit code is returned (0 clear, 3 refused or not
    clear); 9 when the CLI cannot be built or started (one stderr line).
    ASCII-only: parseable under Windows PowerShell 5.1.
#>
param(
    [Parameter(Mandatory)][string]$Repository,
    [switch]$Execute,
    [switch]$ConfirmDescendantsExited
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @('--repository', $Repository)
if ($Execute) { $arguments += '--execute' }
if ($ConfirmDescendantsExited) { $arguments += '--confirm-descendants-exited' }
exit (Invoke-AntiphonCli -Command 'recover-repository-children' -Arguments $arguments)
