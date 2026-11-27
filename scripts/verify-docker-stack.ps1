# CARD-0590 stack verifier. -Case selects one literal checkpoint or guard.
param(
    [Parameter(Mandatory = $true)][string]$Case,
    [Parameter(Mandatory = $true)][string]$Manifest,
    [switch]$RefreshClaudeToken
)
# Unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md (W10 part 1): a thin launcher of the antiphon-scripts
# verify-docker-stack command (scripts/lib/Antiphon.Nightly/DockerStackVerify.cs), which since then holds this script's head (the
# manifest checks and the C994 and C1008 context guards) and the live server2 bridge of scripts/c590-real.ps1 (C590Bridge.cs), as
# well as the literal checkpoints and guards it took in unit 47. The deploy-server2 command runs the verifier in its own process.
# Since unit 67 the verify-card0849-caches and retire-runner-volumes commands run the verifier in process too. This file stays
# because a one-off starts it by path: the finished CARD-0590 checkpoint entry of c590-real.ps1 (-Cp), kept as PowerShell.
# Exit 9: the CLI could not be built or started (one stderr line).
$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @('--case', $Case, '--manifest', $Manifest)
if ($RefreshClaudeToken) { $arguments += '--refresh-claude-token' }
exit (Invoke-AntiphonCli -Command 'verify-docker-stack' -Arguments $arguments)
