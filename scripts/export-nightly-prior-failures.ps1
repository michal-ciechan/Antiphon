#requires -Version 5.1
# Explicit export from closed retained evidence. ASCII-only; no live services.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$RunDirectory,
      [Parameter(Mandatory = $true)][string]$OutputPath,
      [switch]$PassThru)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/nightly-failure-history.ps1')
try {
    Export-NightlyPriorFailures -RunDirectory $RunDirectory -OutputPath $OutputPath -PassThru:$PassThru
} catch {
    # Refusal is separate from the immutable history; never publish complete=true
    # or overwrite a source/output file after a validation error.
    [ordered]@{format='antiphon-nightly-prior-failures-error-v1';complete=$false;reason=$_.Exception.Message} | ConvertTo-Json -Compress
    exit 2
}
