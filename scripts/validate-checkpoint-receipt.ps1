#requires -Version 7.0
<#
.SYNOPSIS
    CARD-0835 offline receipt qualification: one receipt against the exact source SHA it must certify.

    pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence <report.json|source.json> -ExpectedSourceSha <sha> `
        [-Rows CP-1,CP-2] [-ExpectedRepeat N]

    Thin launcher of the checkpoint tool's validate verb (tools/Antiphon.Checkpoints/Report/ReceiptQualification.cs),
    unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, run as `antiphon-scripts checkpoints validate`
    (the tool built from this checkout). Kept because the server's CheckpointReceipts:ValidatorScript setting names this
    file and starts it with pwsh -File. A report.json (the tool's runs and its row verb) or a source.json written by
    run-checkpoint.ps1 before unit 20 is read; a relative -Evidence is this location's. Prints
    `CHECKPOINT SOURCE VALID source=<sha> rows=<n>` or `CHECKPOINT SOURCE INVALID reason=<reason>`.

    Exit codes: 0 valid; 2 incomplete, dirty, changed, unknown or conflicting facts, or an unreadable receipt; 9 the CLI
    or the tool could not be built or started (one stderr line).

    Owner: docs/testing-and-build.md, "Checkpoint manifest (CARD-0585)". ASCII-only.
#>
param(
    [Parameter(Mandatory = $true)] [string]$Evidence,
    [Parameter(Mandatory = $true)] [string]$ExpectedSourceSha,
    [string[]]$Rows,
    [int]$ExpectedRepeat = 1
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')

$location = (Get-Location -PSProvider FileSystem).ProviderPath
$validate = New-Object System.Collections.Generic.List[string]
$validate.AddRange([string[]]@('validate', '--evidence', [IO.Path]::GetFullPath($Evidence, $location), '--expected-source-sha', $ExpectedSourceSha,
    '--expected-repeat', $ExpectedRepeat.ToString([Globalization.CultureInfo]::InvariantCulture)))
foreach ($token in @($Rows)) { if (-not [string]::IsNullOrEmpty([string]$token)) { $validate.Add('--rows'); $validate.Add([string]$token) } }
exit (Invoke-AntiphonCli -Command 'checkpoints' -Verbatim $validate.ToArray())
