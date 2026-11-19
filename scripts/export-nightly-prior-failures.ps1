#requires -Version 5.1
# Explicit export from closed retained evidence. ASCII-only; no live services.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$RunDirectory,
      [Parameter(Mandatory = $true)][string]$OutputPath,
      [switch]$PassThru)
$ErrorActionPreference = 'Stop'
# One compiled call (lib/Antiphon.Nightly/PriorFailureHistory.cs Export).
try {
    . (Join-Path $PSScriptRoot 'lib/nightly-core.ps1')
    Assert-NightlyCoreAvailable 'export-nightly-prior-failures'
    $history = [Antiphon.Nightly.PriorFailureHistory]::Export($RunDirectory, $OutputPath)
} catch {
    # Refusal is separate from the immutable history; never publish complete=true
    # or overwrite a source/output file after a validation error.
    [ordered]@{format='antiphon-nightly-prior-failures-error-v1';complete=$false;reason=(Get-NightlyRefusalText $_)} | ConvertTo-Json -Compress
    exit 2
}
Write-Host ('HISTORY SHA256 ' + $history.Digest)
if ($PassThru) { return $history }
