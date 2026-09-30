# Offline CARD-0727 host-case fixture. Reads only the test's non-secret manifest fields.
param([string]$Case, [string]$Manifest)
$ErrorActionPreference = 'Stop'
$state = Get-Content -Raw -LiteralPath $env:C727_TEST_STATE | ConvertFrom-Json
$request = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
if ($request.sourceSha -cne $state.sha -or $request.runId -notmatch '^c727[0-9a-f]{12}$') { exit 2 }
if ($Case -eq 'verify-runner-caches' -and $request.runnerId -notin @('server2', 'server2-temp')) { exit 2 }
$entry = [ordered]@{ kind = 'case'; name = $Case; runnerId = $request.runnerId; sourceSha = $request.sourceSha }
Add-Content -LiteralPath $env:C727_TEST_TRACE -Value ($entry | ConvertTo-Json -Compress)
if ($Case -eq 'verify-runner-caches' -and $state.failVerify -eq $request.runnerId) { exit 1 }
if ($Case -eq 'deploy-temp-runner') { $state.tempDeployed = $true; $state.tempRetiredAt = $null }
elseif ($Case -eq 'deploy-parent') { $state.oldDeployed = $true }
elseif ($Case -notin @('runner-cache-seed', 'verify-runner-caches', 'retire-temp-runner')) { exit 2 }
$state | ConvertTo-Json -Compress | Set-Content -LiteralPath $env:C727_TEST_STATE
exit 0
