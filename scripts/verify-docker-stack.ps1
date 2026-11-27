# CARD-0590 stack verifier. -Case selects one literal checkpoint or guard.
param(
    [Parameter(Mandatory = $true)][string]$Case,
    [Parameter(Mandatory = $true)][string]$Manifest,
    [switch]$RefreshClaudeToken
)
$ErrorActionPreference = 'Stop'
# Copy before dot-sourcing c590-real.ps1: that script's param block runs in this scope and
# would clear a same-named switch.
$c737RefreshClaudeToken = [bool]$RefreshClaudeToken
. (Join-Path $PSScriptRoot 'c590-command.ps1')
if (-not (Test-Path -LiteralPath $Manifest)) { Write-Error 'Manifest is required'; exit 2 }
$rawManifest = Get-Content -Raw -LiteralPath $Manifest
. (Join-Path $PSScriptRoot 'c590-real.ps1')
# Raw timestamp validation must precede PowerShell's automatic DateTime conversion.
Assert-C994RawManifest -Json $rawManifest
$m = $rawManifest | ConvertFrom-Json
if ($Case -ceq 'retire-temp-containers' -or $m.PSObject.Properties.Name -ccontains 'tempContainerCleanup') {
    Assert-C994BridgeContext -Context $m.tempContainerCleanup -Case $Case
}
$root = [string]$m.evidenceRoot
# Validate at the verifier boundary as well, before an intercepted transport can
# bypass the live bridge's independent context check.
if ($m.PSObject.Properties.Name -contains 'recycle') {
    . (Join-Path $PSScriptRoot 'c590-real.ps1')
    Assert-C1008BridgeContext -Context $m.recycle -Case $Case
}
if (-not $env:ANTIPHON_C590_STUB) {
    . (Join-Path $PSScriptRoot 'c590-real.ps1')
    $script:C628RefreshClaudeToken = $c737RefreshClaudeToken
    if (Test-C590LiveCase -Case $Case) {
        Invoke-C590LiveCase -Case $Case -Manifest $m -RawManifest $rawManifest
    }
}

# Unit 47 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md (W8 slice B): everything past the live bridge above
# (stub mode, or a case c590-real.ps1 does not route to server2) is the antiphon-scripts verify-docker-stack command
# (scripts/lib/Antiphon.Nightly/DockerStackVerify.cs): the same literal checkpoints and guards, c590-result.json, DIAGNOSIS= line
# and exit codes. This file stays because the deploy-server2 command (unit 65), c590-real.ps1, c1609-manual.ps1 and
# verify-card0849-caches.ps1 start it by path; the head above is unchanged. Exit 9: the CLI could not be built or started (one stderr line).
$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
exit (Invoke-AntiphonCli -Command 'verify-docker-stack' -Arguments @('--case', $Case, '--manifest', $Manifest))