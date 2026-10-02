# Offline CARD-0727 host-case fixture. Reads only the test's non-secret manifest fields.
param([string]$Case, [string]$Manifest)
$ErrorActionPreference = 'Stop'
$state = Get-Content -Raw -LiteralPath $env:C727_TEST_STATE | ConvertFrom-Json
$request = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
if ($request.sourceSha -cne $state.sha -or $request.runId -notmatch '^c727[0-9a-f]{12}$') { exit 2 }
if ($Case -eq 'verify-runner-caches' -and $request.runnerId -notin @('server2', 'server2-temp')) { exit 2 }
$savedDonor = if ($request.PSObject.Properties.Name -contains 'savedDonor') { [string]$request.savedDonor } else { '' }
$entry = [ordered]@{ kind = 'case'; name = $Case; runnerId = $request.runnerId; sourceSha = $request.sourceSha; savedDonor = $savedDonor }
Add-Content -LiteralPath $env:C727_TEST_TRACE -Value ($entry | ConvertTo-Json -Compress)
if ($Case -eq 'verify-runner-caches' -and $state.failVerify -eq $request.runnerId) { exit 1 }
if ($Case -eq 'runner-cache-seed' -and $state.tempContainer -and $state.tempOffline) { exit 1 }
if ($state.PSObject.Properties.Name -contains 'markerPath' -and $Case -in @('deploy-parent', 'verify-runner-caches')) {
    # Real cold reader + Docker boundary fake. deploy-parent retires the seed image
    # after its ready check, exactly as the live 12:06Z redeploy did.
    $reader = if ($Case -eq 'deploy-parent') { 'case_deploy_parent' } else { 'case_verify_runner_caches' }
    $repo = Split-Path -Parent $PSScriptRoot
    $seedAvailable = if ($state.seedImageAvailable) { '1' } else { '0' }
    $variant = [string]$state.markerVariant
    $paths = @((Join-Path $PSScriptRoot 'c973-marker-reader.sh'), (Join-Path $repo 'c590-remote.sh'), [string]$state.markerPath)
    if ($IsWindows) {
        # The production reader is Linux-only. Use the same WSL lane as its TUnit fixtures.
        $linuxPaths = foreach ($path in $paths) {
            $converted = & wsl -e wslpath -u $path
            if ($LASTEXITCODE -ne 0) { throw 'C973 WSL fixture path conversion failed' }
            ([string]$converted).Trim()
        }
        $result = & wsl -e bash $linuxPaths[0] $linuxPaths[1] $reader $linuxPaths[2] $seedAvailable '1' $variant
    } else {
        $result = & bash $paths[0] $paths[1] $reader $paths[2] $seedAvailable '1' $variant
    }
    $code = $LASTEXITCODE
    Add-Content -LiteralPath $env:C727_TEST_TRACE -Value (@{
        kind='marker'; name=$Case; seedImageAvailable=[bool]$state.seedImageAvailable; markerKind='cold'; exit=$code
    } | ConvertTo-Json -Compress)
    if ($code -ne 0) { $result | Write-Output; exit $code }
}
if ($Case -eq 'deploy-temp-runner') {
    # Register refuses a retired id. An idle draining runner with retireWhenIdle
    # can also be retired by the service while the new container connects.
    if (-not $state.tempRetiredAt -and $state.tempDraining -and $state.tempRetireWhenIdle) {
        $state.tempRetiredAt = '2026-09-27T10:00:00Z'
    }
    if ($state.tempRetiredAt) {
        Add-Content -LiteralPath $env:C727_TEST_TRACE -Value '{"kind":"registration","result":"RunnerRetired"}'
        $state | ConvertTo-Json -Compress | Set-Content -LiteralPath $env:C727_TEST_STATE
        exit 1
    }
    $state.tempDeployed = $true
    $state.tempOffline = $false
    $state.tempContainer = $true
}
elseif ($Case -eq 'deploy-parent') {
    $state.oldDeployed = $true
    if ($state.PSObject.Properties.Name -contains 'seedImageAvailable') { $state.seedImageAvailable = $false }
}
elseif ($Case -notin @('runner-cache-seed', 'verify-runner-caches', 'retire-temp-runner')) { exit 2 }
$state | ConvertTo-Json -Compress | Set-Content -LiteralPath $env:C727_TEST_STATE
exit 0
