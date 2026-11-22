#requires -Version 7.0
param([string]$RequestPath, [string]$ResponsePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-selection.ps1')
try {
    $request = Get-Content -LiteralPath $RequestPath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
    $value = New-ReleaseSelectionAttemptCore $request
    [IO.File]::WriteAllText($ResponsePath, (ConvertTo-Json @{ok=$true;value=$value} -Depth 40))
} catch {
    # A compiled refusal (TestSelection, the policy readers) arrives wrapped: the response carries its reason.
    [IO.File]::WriteAllText($ResponsePath, (ConvertTo-Json @{ok=$false;error=(Get-NightlyRefusalText $_)} -Depth 5))
    exit 2
}
