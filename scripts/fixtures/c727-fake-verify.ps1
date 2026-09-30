# Offline CARD-0727 host-case fixture. Reads only the test's non-secret manifest fields.
param([string]$Case, [string]$Manifest)
$ErrorActionPreference = 'Stop'
$state = Get-Content -Raw -LiteralPath $env:C727_TEST_STATE | ConvertFrom-Json
if ($Case -eq 'deploy-temp-runner') { $state.tempDeployed = $true }
elseif ($Case -eq 'deploy-parent') { $state.oldDeployed = $true }
elseif ($Case -notin @('runner-cache-seed', 'verify-runner-caches', 'retire-temp-runner')) { exit 2 }
$state | ConvertTo-Json -Compress | Set-Content -LiteralPath $env:C727_TEST_STATE
Add-Content -LiteralPath $env:C727_TEST_TRACE -Value (([ordered]@{ kind = 'case'; name = $Case }) | ConvertTo-Json -Compress)
exit 0
