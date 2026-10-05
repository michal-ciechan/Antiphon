# Offline CARD-0727 host-case fixture. Reads only the test's non-secret manifest fields.
param([string]$Case, [string]$Manifest)
$ErrorActionPreference = 'Stop'
$state = Get-Content -Raw -LiteralPath $env:C727_TEST_STATE | ConvertFrom-Json
if ($Case -eq 'rollout-lock') {
    Add-Content -LiteralPath $env:C727_TEST_TRACE -Value '{"kind":"lock-request"}'
    $lock = $null
    try {
        if ($state.rolloutLockFile) {
            do {
                try { $lock = [IO.File]::Open([string]$state.rolloutLockFile, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
                catch { Start-Sleep -Milliseconds 20 }
            } while ($null -eq $lock)
        }
        [Console]::WriteLine('C1008_LOCKED')
        [Console]::Out.Flush()
        [void][Console]::ReadLine()
    } finally { if ($null -ne $lock) { $lock.Dispose() } }
    exit 0
}
if ($Case -eq 'recycle-discover') {
    if ($state.incompleteRecycle) { Write-Output 'c100800000000000000000000000000000001' }
    if ($state.discoveryError) { exit 2 }
    exit 0
}
$request = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
if ($request.sourceSha -cne $state.sha -or $request.runId -notmatch '^c727[0-9a-f]{12}$') { exit 2 }
if ($Case -eq 'host-jq-prerequisite') {
    if ($request.mode -notin @('check', 'provision') -or
        $request.phase -notin @('deploy-temp', 'drain-old', 'redeploy-old', 'drain-temp', 'retire-temp', 'check-host-jq', 'provision-host-jq')) { exit 2 }
    $outcome = 'healthy'
    if ($state.hostJqSequence -and $state.hostJqSequence.Count) {
        $outcome = [string]$state.hostJqSequence[0]
        $state.hostJqSequence = @($state.hostJqSequence | Select-Object -Skip 1)
        $state | ConvertTo-Json -Compress -Depth 30 | Set-Content -LiteralPath $env:C727_TEST_STATE
    }
    Add-Content -LiteralPath $env:C727_TEST_TRACE -Value (@{
        kind='prerequisite'; name=$Case; mode=$request.mode; phase=$request.phase;
        selectedPhase=$request.selectedPhase; sourceSha=$request.sourceSha; runId=$request.runId;
        evidenceRoot=$request.evidenceRoot; outcome=$outcome
    } | ConvertTo-Json -Compress)
    switch ($outcome) {
        'missing' { [Console]::Error.WriteLine('HostJqMissing'); exit 2 }
        'invalid' { [Console]::Error.WriteLine('HostJqInvalid'); exit 2 }
        'ssh-failed' { exit 255 }
        'malformed-proof' { Write-Output '{'; exit 0 }
        'receipt-write-failed' {
            New-Item -ItemType Directory -Path (Join-Path $request.evidenceRoot ("host-jq-$($request.phase).json")) | Out-Null
        }
        'healthy' { }
        default { exit 2 }
    }
    @{ schema=1; lane='host'; mode=$request.mode; lookupPath='/usr/local/bin/jq'; path='/usr/local/bin/jq';
        version='jq-1.7.1'; digest='5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5';
        uid=0; gid=0; permissions='755'; trueExit=0; falseExit=1; installed=$false; outcome='existing' } |
        ConvertTo-Json -Compress
    exit 0
}
if ($Case -eq 'verify-runner-caches' -and $request.runnerId -notin @('server2', 'server2-temp')) { exit 2 }
$savedDonor = if ($request.PSObject.Properties.Name -contains 'savedDonor') { [string]$request.savedDonor } else { '' }
if ($Case -eq 'temp-project-absent') {
    Add-Content -LiteralPath $env:C727_TEST_TRACE -Value '{"kind":"census","name":"temp-project-absent"}'
    if ($state.scenario -eq 'census-failed' -or $state.censusError) { exit 2 }
    if ($state.tempContainer) { Write-Output 'fixture-temp-container' }
    exit 0
}
$entry = [ordered]@{ kind = 'case'; name = $Case; runnerId = $request.runnerId; sourceSha = $request.sourceSha; savedDonor = $savedDonor }
if ($state.scenario -eq 'c1008') { $entry['recycle'] = $request.recycle; $entry['tempRetiredAt'] = $request.tempRetiredAt }
if ($Case -eq 'retire-temp-containers') {
    $context = $request.tempContainerCleanup
    $entry['cleanup'] = $context
    $entry['clockMs'] = $state.clockMs
    Add-Content -LiteralPath $env:C727_TEST_TRACE -Value ($entry | ConvertTo-Json -Compress -Depth 20)
    $dest = Join-Path $request.evidenceRoot $Case
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    $running = $state.tempRunning -or ($state.tempContainer -and $state.scenario -eq 'c1008' -and -not $state.tempExited)
    if ($state.cleanupElapsedMs) { $state.clockMs += $state.cleanupElapsedMs }
    if ($state.cleanupSequence -and $state.cleanupSequence.Count) {
        $running = [bool]$state.cleanupSequence[0]
        $state.cleanupSequence = @($state.cleanupSequence | Select-Object -Skip 1)
    }
    if ($running -or $state.cleanupFail) {
        $diagnosis = if ($running) { 'TempContainerStillRunning' } else { 'TempContainerRemoveFailed' }
        @{ accepted=$false; diagnosis=$diagnosis; exit=2 } | ConvertTo-Json | Set-Content (Join-Path $dest 'c590-result.json')
        $state | ConvertTo-Json -Compress -Depth 30 | Set-Content $env:C727_TEST_STATE
        exit 2
    }
    $candidates = if ($state.tempContainer) { @(@{Id=('1'*64);service='session-runner'}) } else { @() }
    $receipt = [ordered]@{ schema=1; sourceSha=$request.sourceSha; runId=$request.runId; operationId=$context.operationId;
        project=$context.project; projectId=$context.projectId; retiredAt=$context.retiredAt; dryRun=$context.dryRun;
        candidates=@($candidates); removals=@(); finalCensus=@(); outcome=$(if($context.dryRun){'preview'}else{'completed'});
        image=('sha256:' + ('a'*64)) }
    if ($state.receiptMismatch) { $receipt.sourceSha = 'b'*40 }
    if (-not $state.receiptMissing) {
        $receiptPath = Join-Path $dest 'temp-containers.json'
        $receipt | ConvertTo-Json -Compress -Depth 30 | Set-Content -Encoding utf8 $receiptPath
        (Get-FileHash $receiptPath -Algorithm SHA256).Hash | Set-Content (Join-Path $dest 'temp-containers.sha256')
    }
    if (-not $context.dryRun) { $state.tempContainer = $false }
    if ($state.driftAfterCleanup) { $state.statuses.'server2-temp'.sessions = 1 }
    $state | ConvertTo-Json -Compress -Depth 30 | Set-Content $env:C727_TEST_STATE
    if ($state.receiptCopyFail) { exit 2 }
    exit 0
}

Add-Content -LiteralPath $env:C727_TEST_TRACE -Value ($entry | ConvertTo-Json -Compress -Depth 20)
if ($Case -eq 'verify-runner-caches' -and $state.failVerify -eq $request.runnerId) { exit 1 }
if ($state.scenario -eq 'c1008' -and $Case -eq 'deploy-parent') {
    if ($state.failDeploy) { exit 2 }
    $state.statuses.server2.available = $true
    $state.statuses.server2.dispatchEligible = $true
    $state.statuses.server2.runnerSessions = 0
    $state.statuses.server2.buildVersion = $state.sha
    if ($state.wrongDeploySha) { $state.statuses.server2.buildVersion = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' }
}
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
        $state | ConvertTo-Json -Compress -Depth 30 | Set-Content -LiteralPath $env:C727_TEST_STATE
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
