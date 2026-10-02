# CARD-0849 frozen offline rolling roster. No network or live runner access.
param([ValidateSet('all', 'retired-start', 'cleared-offline-start', 'host-race', 'host-absence', 'host-recovery', 'host-saved', 'cleanup-failure')][string]$Only = 'all', [switch]$AlreadyAtSha, [switch]$KeepTemp)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$driver = Join-Path $PSScriptRoot 'deploy-server2.ps1'
$http = Join-Path $PSScriptRoot 'fixtures/c727-fake-http.ps1'
$verify = Join-Path $PSScriptRoot 'fixtures/c727-fake-verify.ps1'
$front = Join-Path $PSScriptRoot 'verify-card0849-caches.ps1'
$sha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$tempRoot = Join-Path $root ('.antiphon/c849-rolling-' + [guid]::NewGuid().ToString('N'))
$ownsTempRoot = -not $env:C973_TEST_ROOT
$success = $false
if ($env:C973_TEST_ROOT) { $tempRoot = $env:C973_TEST_ROOT }
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
$script:assertions = 0
$script:invocations = 0
$script:groups = 0

function Test-C973Jq {
    # Probe the shell that actually executes the marker reader, once per run.
    # The offline driver may stub absence or inject an application to probe.
    if ($env:C973_TEST_JQ_PROBE -eq 'missing') { return $false }
    try {
        $shell = if ($env:C973_JQ_PROBE_SHELL) { $env:C973_JQ_PROBE_SHELL }
            elseif ($IsWindows) { 'wsl' } else { 'bash' }
        $application = Get-Command $shell -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $application) { return $false }
        if ($IsWindows -and -not $env:C973_JQ_PROBE_SHELL) {
            $null = & $application.Source -e bash -c 'command -v jq' 2>$null
        } else {
            $null = & $application.Source -c 'command -v jq' 2>$null
        }
        return $LASTEXITCODE -eq 0
    } catch {
        # Missing executables, launch errors and native-command exceptions are absence.
        return $false
    }
}
$hasJq = Test-C973Jq
Write-Output "C973_JQ_PROBE available=$hasJq"

function Assert-C727 {
    param([bool]$Condition, [string]$Name)
    $script:assertions++
    if (-not $Condition) { throw "FAIL $Name" }
}
function Complete-Group {
    param([int]$Number, [string]$Name)
    $script:groups++
    Write-Output "PASS T-$Number $Name"
}
function Run-C727 {
    param([string]$Scenario, [string]$Phase = 'all', [hashtable]$Set = @{}, [bool]$TokenPresent = $true,
        [string]$SavedDonor = '', [switch]$UseMarker)
    $script:invocations++
    $dir = Join-Path $tempRoot ($Scenario + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $statePath = Join-Path $dir 'state.json'
    $tracePath = Join-Path $dir 'trace.jsonl'
    $tokenPath = Join-Path $dir 'operator-token'
    $sentinel = 'SENTINEL_C727_OPERATOR_TOKEN_1234567890'
    if ($TokenPresent) { Set-Content -LiteralPath $tokenPath -Value $sentinel -NoNewline }
    $state = [ordered]@{
        scenario = $Scenario; sha = $sha; tempDeployed = $false; oldDeployed = $false
        oldDraining = $false; tempDraining = $false; tempRetiredAt = $null; tempContainer = $false; tempOffline = $false
        tempRedirectTo = 'server2'; tempRetireWhenIdle = $true
        faultRunner = ''; faultField = ''; faultKind = ''; faultValue = $null; failVerify = ''
    }
    if ($UseMarker) {
        $markerPath = Join-Path $dir 'seed-accepted'
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/c973-cold-seed-accepted.txt') -Destination $markerPath
        $state['markerPath'] = $markerPath
        $state['markerVariant'] = 'valid'
        $state['seedImageAvailable'] = $true
    }
    foreach ($key in $Set.Keys) { $state[$key] = $Set[$key] }
    if ($state.tempDeployed -and -not $Set.ContainsKey('tempContainer')) { $state.tempContainer = $true }
    $state | ConvertTo-Json -Compress | Set-Content -LiteralPath $statePath
    $psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile', '-File', $driver, '-Rolling', '-Sha', $sha, '-Phase', $Phase, '-WaitIdleMinutes', '1')) {
        [void]$psi.ArgumentList.Add($arg)
    }
    if ($SavedDonor) {
        [void]$psi.ArgumentList.Add('-SavedDonor')
        [void]$psi.ArgumentList.Add($SavedDonor)
    }
    $psi.Environment['ANTIPHON_OPERATOR_TOKEN_FILE'] = $tokenPath
    $psi.Environment['C727_TEST_HTTP_STUB'] = $http
    $psi.Environment['C727_TEST_VERIFY_STUB'] = $verify
    $psi.Environment['C727_TEST_STATE'] = $statePath
    $psi.Environment['C727_TEST_TRACE'] = $tracePath
    $psi.Environment['C727_TEST_WAIT_MS'] = '100'
    $psi.Environment['C727_TEST_POLL_MS'] = '5'
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    $trace = if (Test-Path -LiteralPath $tracePath) { @(Get-Content -LiteralPath $tracePath | ForEach-Object { $_ | ConvertFrom-Json }) } else { @() }
    $finalState = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
    return [pscustomobject]@{ Exit = $proc.ExitCode; Out = $stdout + $stderr; Trace = $trace; Sentinel = $sentinel; State = $finalState; StatePath = $statePath; TracePath = $tracePath }
}
function Cases { param($Run) return @($Run.Trace | Where-Object kind -eq 'case') }
function Posts { param($Run) return @($Run.Trace | Where-Object { $_.kind -eq 'http' -and $_.method -eq 'POST' }) }
function Has-Case { param($Run, [string]$Name) return @((Cases $Run) | Where-Object name -eq $Name).Count -gt 0 }
function Final-TempStatus {
    param($Run)
    $psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    foreach ($arg in @('-NoProfile', '-File', $http, '-Method', 'GET', '-RunnerId', 'server2-temp', '-Suffix', '/status')) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $psi.Environment['C727_TEST_STATE'] = $Run.StatePath
    $psi.Environment['C727_TEST_TRACE'] = $Run.TracePath
    $proc = [System.Diagnostics.Process]::Start($psi)
    $output = $proc.StandardOutput.ReadToEnd()
    $proc.WaitForExit()
    if ($proc.ExitCode -ne 0) { throw 'FinalTempStatusFailed' }
    return $output | ConvertFrom-Json
}

function Assert-RetiredStart {
    param([bool]$AlreadyAtSha = $false)
    $t = Run-C727 -Scenario "retired-start-$AlreadyAtSha" -Phase deploy-temp -Set @{
        tempDraining = $true; tempRetiredAt = '2026-09-27T10:00:00Z'; tempContainer = $false; tempOffline = $true; tempDeployed = $AlreadyAtSha
    }
    $c = Cases $t; $p = Posts $t
    Assert-C727 ($t.Exit -eq 0) 'T-18 retired registration succeeds'
    Assert-C727 ((@($c | ForEach-Object name) -join ',') -eq 'runner-cache-seed,deploy-temp-runner,verify-runner-caches') 'T-18 retired host order'
    Assert-C727 ($p.Count -eq 3 -and $p[0].suffix -eq '/drain/clear' -and $p[1].suffix -eq '/drain' -and
        $p[1].body.redirectTo -eq 'server2' -and $p[1].body.retireWhenIdle -eq $false -and $p[2].suffix -eq '/drain/clear') 'T-18 retired clear and safe hold'
    Assert-C727 ([array]::IndexOf($t.Trace, $p[0]) -lt [array]::IndexOf($t.Trace, $p[1]) -and
        [array]::IndexOf($t.Trace, $p[1]) -lt [array]::IndexOf($t.Trace, $c[0]) -and
        [array]::IndexOf($t.Trace, $p[2]) -gt [array]::IndexOf($t.Trace, $c[2])) 'T-18 retired order and verification gate'
    Assert-C727 ($p[0].body.reason -match 'CARD-0948' -and $t.State.tempDeployed -and
        -not $t.State.tempDraining -and -not $t.State.tempRetiredAt) 'T-18 retired evidence and accepting state'
    $final = Final-TempStatus $t
    Assert-C727 ($final.dispatchEligible -and $final.acceptingNewWork -and $final.buildVersion -eq $sha -and
        -not $final.draining -and -not $final.retiredAt) 'T-18 retired final status and SHA'
}

function Assert-ClearedOfflineStart {
    param([bool]$Draining, [bool]$RetireWhenIdle, [bool]$AlreadyAtSha = $false)
    $t = Run-C727 -Scenario "cleared-offline-$Draining-$RetireWhenIdle-$AlreadyAtSha" -Phase deploy-temp -Set @{
        tempContainer = $false; tempOffline = $true; tempDraining = $Draining; tempRetireWhenIdle = $RetireWhenIdle; tempDeployed = $AlreadyAtSha
    }
    $c = Cases $t; $p = Posts $t
    Assert-C727 ($t.Exit -eq 0) 'T-19 cleared offline registration succeeds'
    Assert-C727 ((@($c | ForEach-Object name) -join ',') -eq 'runner-cache-seed,deploy-temp-runner,verify-runner-caches') 'T-19 cleared host order'
    $needsHold = -not $Draining -or $RetireWhenIdle
    $clear = if ($needsHold) { $p[1] } else { $p[0] }
    Assert-C727 ($p.Count -eq $(if ($needsHold) { 2 } else { 1 }) -and
        (-not $needsHold -or ($p[0].suffix -eq '/drain' -and $p[0].body.redirectTo -eq 'server2' -and
            $p[0].body.retireWhenIdle -eq $false)) -and $clear.suffix -eq '/drain/clear') 'T-19 cleared safe hold'
    Assert-C727 ((-not $needsHold -or [array]::IndexOf($t.Trace, $p[0]) -lt [array]::IndexOf($t.Trace, $c[0])) -and
        [array]::IndexOf($t.Trace, $clear) -gt [array]::IndexOf($t.Trace, $c[2])) 'T-19 cleared verification gate'
    Assert-C727 ($t.State.tempDeployed -and -not $t.State.tempDraining -and
        -not $t.State.tempRetiredAt) 'T-19 cleared accepting state'
    $final = Final-TempStatus $t
    Assert-C727 ($final.dispatchEligible -and $final.acceptingNewWork -and $final.buildVersion -eq $sha -and
        -not $final.draining -and -not $final.retiredAt) 'T-19 cleared final status and SHA'
}

function Assert-HostRace {
    $t = Run-C727 -Scenario retire-race -Phase deploy-temp -Set @{ tempDraining = $true; tempRetireWhenIdle = $true }
    Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('TempRunnerRetiredDuringHold')) 'T-21 retirement stamp refuses before seed'
    Assert-C727 ((Cases $t).Count -eq 0 -and $t.State.tempRetiredAt) 'T-21 race no build or seed'
    Assert-C727 ($t.State.tempDraining -and -not $t.State.tempRetireWhenIdle) 'T-21 race holds admission'
    $retired = @{ tempDraining = $true; tempRetiredAt = '2026-09-27T10:00:00Z'; tempContainer = $false }
    $t = Run-C727 -Scenario old-invalid -Phase deploy-temp -Set ($retired + @{ oldDraining = $true })
    Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('OldRunnerRedirectNotEligible')) 'T-21 invalid redirect refuses'
    Assert-C727 ((Posts $t).Count -eq 0 -and $t.State.tempRetiredAt) 'T-21 no retirement clear'
    Assert-C727 ((Cases $t).Count -eq 0) 'T-21 invalid redirect no host mutation'
    $t = Run-C727 -Scenario hold-post-fails -Phase deploy-temp -Set $retired
    Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('RunnerApiFailed')) 'T-21 fallible hold refuses'
    Assert-C727 (-not $t.State.tempContainer -and (Cases $t).Count -eq 0) 'T-21 clear gap no container to register'
    Assert-C727 (@($t.Trace | Where-Object kind -eq 'census').Count -eq 1) 'T-21 absence checked before clear'
    Complete-Group 21 'retirement race and clear gap'
}
function Assert-HostAbsence {
    foreach ($variant in @('existing', 'census-failed')) {
        $t = Run-C727 -Scenario $variant -Phase deploy-temp -Set @{ tempDraining = $true; tempRetiredAt = '2026-09-27T10:00:00Z'; tempContainer = ($variant -eq 'existing'); tempOffline = $true }
        $diagnosis = if ($variant -eq 'existing') { 'TempContainersRemain' } else { 'TempContainerCensusUnavailable' }
        Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains($diagnosis)) "T-22 $variant named refusal"
        Assert-C727 ((Posts $t).Count -eq 0 -and $t.State.tempRetiredAt) "T-22 $variant keeps retirement"
        Assert-C727 ((Cases $t).Count -eq 0) "T-22 $variant no seed or replacement"
    }
    Complete-Group 22 'host absence preflight'
}
function Assert-HostRecovery {
    $t = Run-C727 -Scenario recovering -Phase deploy-temp -Set @{ tempDeployed = $true; tempContainer = $true }
    Assert-C727 ($t.Exit -eq 0) 'T-23 same SHA recovery waits successfully'
    Assert-C727 ((Cases $t).Count -eq 1 -and (Cases $t)[0].name -eq 'verify-runner-caches') 'T-23 no reseed or replacement'
    Assert-C727 ((Posts $t).Count -eq 0) 'T-23 no admission mutation during recovery'
    Complete-Group 23 'same SHA online recovery'
}
function Assert-HostSaved {
    $t = Run-C727 -Scenario saved-prerequisite -Phase deploy-temp -SavedDonor '/fixture/saved.tar'
    Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('TempSavedDonorRequiresMaintenance')) 'T-24 saved donor explicit maintenance prerequisite'
    Assert-C727 ((Cases $t).Count -eq 0) 'T-24 no seed'
    Assert-C727 ((Posts $t).Count -eq 0) 'T-24 no state mutation'
    Complete-Group 24 'saved donor rollout prerequisite'
}

try {
    if ($Only -eq 'cleanup-failure') { throw 'C946 requested cleanup failure' }
    switch ($Only) {
        host-race { Assert-HostRace; $success = $true; exit 0 }
        host-absence { Assert-HostAbsence; $success = $true; exit 0 }
        host-recovery { Assert-HostRecovery; $success = $true; exit 0 }
        host-saved { Assert-HostSaved; $success = $true; exit 0 }
    }
    if ($Only -eq 'retired-start') { Assert-RetiredStart -AlreadyAtSha $AlreadyAtSha; Write-Output 'PASS T-18 retired start'; $success = $true; exit 0 }
    if ($Only -eq 'cleared-offline-start') { Assert-ClearedOfflineStart -Draining $true -RetireWhenIdle $true -AlreadyAtSha $AlreadyAtSha; Write-Output 'PASS T-19 cleared offline start'; $success = $true; exit 0 }
    if ($hasJq) {
        $t = Run-C727 -UseMarker -Scenario cold-marker-pruned -Phase redeploy-old -Set @{ oldDraining = $true }
        Assert-C727 ($t.Exit -eq 0) ('T-20 pruned cold seed image verifies before admission: ' + $t.Out)
        $markers = @($t.Trace | Where-Object kind -eq 'marker')
        Assert-C727 ($markers.Count -eq 2 -and $markers[0].seedImageAvailable -and -not $markers[1].seedImageAvailable -and
            $markers[1].markerKind -eq 'cold' -and $markers[1].exit -eq 0) 'T-20 parent prunes seed image before real cold verification'
        Assert-C727 (@(Posts $t | Where-Object suffix -eq '/drain/clear').Count -eq 1) 'T-20 verified old admission clears'
        foreach ($variant in @('missing', 'malformed', 'foreign-marker')) {
            $t = Run-C727 -UseMarker -Scenario "cold-$variant" -Phase redeploy-old -Set @{
                oldDraining = $true; oldDeployed = $true; seedImageAvailable = $false; markerVariant = $variant
            }
            Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('HostCaseFailed verify-runner-caches exit=2')) "T-20 $variant refuses"
            Assert-C727 ((Posts $t).Count -eq 0 -and $t.State.oldDraining) "T-20 $variant holds drain"
        }
        Complete-Group 20 'cold marker survives parent image cleanup'
    } else {
        Write-Output 'C973_SKIPPED jq-missing: T-20 marker-reader groups need jq (CARD-0927)'
    }
    $t1 = Run-C727 -Scenario happy
    $c = Cases $t1; $p = Posts $t1
    Assert-C727 ($t1.Exit -eq 0) 'T-1 exit'
    Assert-C727 ((@($c | ForEach-Object { "$($_.name):$($_.runnerId)" }) -join ',') -eq 'runner-cache-seed:,deploy-temp-runner:,verify-runner-caches:server2-temp,deploy-parent:,verify-runner-caches:server2,retire-temp-runner:') 'T-1 host order/targets'
    Assert-C727 ($p.Count -eq 5) 'T-1 post count'
    Assert-C727 ($p[0].runnerId -eq 'server2-temp' -and $p[0].suffix -eq '/drain' -and $p[0].body.redirectTo -eq 'server2' -and $p[0].body.retireWhenIdle -eq $false -and [array]::IndexOf($t1.Trace, $p[0]) -lt [array]::IndexOf($t1.Trace, $c[0])) 'T-1 hold before seed'
    Assert-C727 ($p[1].runnerId -eq 'server2-temp' -and $p[1].suffix -eq '/drain/clear' -and [array]::IndexOf($t1.Trace, $p[1]) -gt [array]::IndexOf($t1.Trace, $c[2])) 'T-1 temp verify before clear'
    Assert-C727 ($p[2].runnerId -eq 'server2' -and $p[2].suffix -eq '/drain' -and $p[2].body.redirectTo -eq 'server2-temp' -and $p[2].body.retireWhenIdle -eq $false) 'T-1 main drain'
    Assert-C727 ($p[3].runnerId -eq 'server2' -and $p[3].suffix -eq '/drain/clear' -and [array]::IndexOf($t1.Trace, $p[3]) -gt [array]::IndexOf($t1.Trace, $c[4])) 'T-1 main verify before clear'
    Assert-C727 ($p[4].runnerId -eq 'server2-temp' -and $p[4].suffix -eq '/drain' -and $p[4].body.redirectTo -eq 'server2' -and $p[4].body.retireWhenIdle -eq $true) 'T-1 retirement drain'
    Complete-Group 1 'happy all phases'

    foreach ($scenario in @('missing', 'ineligible')) {
        $t = Run-C727 -Scenario $scenario -Phase deploy-temp
        $diag = if ($scenario -eq 'missing') { 'TempRunnerStatusMissing' } else { 'TempRunnerNotEligible' }
        Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains($diag)) "T-2 $scenario verdict"
        Assert-C727 ($scenario -ne 'missing' -or (Cases $t).Count -eq 0) "T-2 $scenario missing no host"
        Assert-C727 ((Posts $t).Count -eq 0 -or ($scenario -eq 'ineligible' -and @(Posts $t | Where-Object runnerId -eq 'server2').Count -eq 0 -and -not (Has-Case $t 'verify-runner-caches'))) "T-2 $scenario no unsafe admission"
    }
    Complete-Group 2 'missing/ineligible temp'

    $t = Run-C727 -Scenario busy -Phase drain-old -Set @{ tempDeployed = $true }
    Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('OldRunnerStillBusy')) 'T-3 busy verdict'
    Assert-C727 (-not (Has-Case $t 'deploy-parent')) 'T-3 no replacement'
    Assert-C727 ((Posts $t).Count -eq 1 -and (Posts $t)[0].suffix -eq '/drain') 'T-3 hold retained'
    Complete-Group 3 'main busy'

    $t = Run-C727 -Scenario rerun -Phase drain-temp -Set @{ tempDeployed = $true; oldDeployed = $true; tempDraining = $true; tempRetiredAt = '2026-09-27T10:00:00Z' }
    Assert-C727 ($t.Exit -eq 0) 'T-4 exit'
    Assert-C727 ((Posts $t).Count -eq 0) 'T-4 no duplicate post'
    Complete-Group 4 'drain-temp rerun'

    $t = Run-C727 -Scenario happy -Phase deploy-temp -TokenPresent $false
    Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('OperatorTokenMissing')) 'T-5 token refusal'
    Assert-C727 ($t.Trace.Count -eq 0) 'T-5 no activity'
    Complete-Group 5 'missing token'

    Assert-C727 (-not $t1.Out.Contains($t1.Sentinel) -and -not ((Get-ChildItem -Path $tempRoot -Filter '*.jsonl' -Recurse | Get-Content -ErrorAction SilentlyContinue) -join '').Contains($t1.Sentinel)) 'T-6 token custody'
    $ascii = @($driver, $front, $http, $verify) | ForEach-Object { [System.IO.File]::ReadAllBytes($_) } | Where-Object { $_ -gt 127 }
    Assert-C727 (@($ascii).Count -eq 0) 'T-6 ASCII'
    Complete-Group 6 'output custody'

    foreach ($field in @('sessions', 'runnerSessions', 'queuedTasks')) {
        $t = Run-C727 -Scenario "busy-temp-$field" -Phase deploy-temp -Set @{ faultRunner = 'server2-temp'; faultField = $field; faultKind = 'value'; faultValue = 1 }
        Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('RunnerBusy')) "T-7 $field verdict"
        Assert-C727 (-not (Has-Case $t 'runner-cache-seed') -and -not (Has-Case $t 'deploy-temp-runner')) "T-7 $field no seed"
        Assert-C727 ((Posts $t).Count -eq 0) "T-7 $field no clear/drain"
    }
    Complete-Group 7 'busy temp seed'

    $t = Run-C727 -Scenario held-fresh -Phase deploy-temp -Set @{ tempDraining = $true }
    $c = Cases $t; $p = Posts $t
    Assert-C727 ($t.Exit -eq 0) 'T-8 exit'
    Assert-C727 ($c.Count -eq 3 -and $c[0].name -eq 'runner-cache-seed' -and $c[1].name -eq 'deploy-temp-runner') 'T-8 seed/start'
    Assert-C727 ($c[2].name -eq 'verify-runner-caches' -and $c[2].runnerId -eq 'server2-temp' -and $c[2].sourceSha -eq $sha) 'T-8 verify target'
    Assert-C727 ($p.Count -eq 2 -and $p[0].suffix -eq '/drain' -and $p[0].body.retireWhenIdle -eq $false -and
        $p[1].suffix -eq '/drain/clear' -and [array]::IndexOf($t.Trace, $p[1]) -gt [array]::IndexOf($t.Trace, $c[2])) 'T-8 hold until verified'
    Assert-C727 (@($p | Where-Object runnerId -eq 'server2').Count -eq 0) 'T-8 no main post'
    Complete-Group 8 'held fresh temp'

    $t = Run-C727 -Scenario retired -Phase deploy-temp -Set @{ tempDraining = $true; tempRetiredAt = '2026-09-27T10:00:00Z'; tempContainer = $false }
    $c = Cases $t; $p = Posts $t
    Assert-C727 ($t.Exit -eq 0) 'T-9 exit'
    Assert-C727 ((@($c | ForEach-Object name) -join ',') -eq 'runner-cache-seed,deploy-temp-runner,verify-runner-caches') 'T-9 seed/replacement/verify'
    Assert-C727 ($p.Count -eq 3 -and $p[0].suffix -eq '/drain/clear' -and
        $p[1].suffix -eq '/drain' -and $p[1].body.retireWhenIdle -eq $false -and
        $p[2].suffix -eq '/drain/clear') 'T-9 rearm and final clear'
    Assert-C727 ([array]::IndexOf($t.Trace, $p[2]) -gt [array]::IndexOf($t.Trace, $c[2])) 'T-9 clear after verify'
    Assert-C727 (@($p | Where-Object runnerId -eq 'server2').Count -eq 0) 'T-9 main untouched'
    $t = Run-C727 -Scenario retired-container-present -Phase deploy-temp -Set @{ tempDraining = $true; tempRetiredAt = '2026-09-27T10:00:00Z'; tempContainer = $true; tempOffline = $true }
    Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('TempContainersRemain')) 'T-9 leftover container refused'
    Assert-C727 ((Cases $t).Count -eq 0) 'T-9 no replacement after host refusal'
    Assert-C727 ((Posts $t).Count -eq 0 -and $t.State.tempRetiredAt) 'T-9 retirement held after host refusal'
    Complete-Group 9 'retired temp reactivation'

    foreach ($variant in @('main', 'temp')) {
        $set = if ($variant -eq 'main') { @{ oldDeployed = $true; oldDraining = $true } } else { @{ tempDeployed = $true } }
        $phase = if ($variant -eq 'main') { 'redeploy-old' } else { 'deploy-temp' }
        $t = Run-C727 -Scenario "same-$variant" -Phase $phase -Set $set
        $c = Cases $t; $p = Posts $t
        Assert-C727 ($t.Exit -eq 0) "T-10 $variant exit"
        Assert-C727 ($c.Count -eq 1 -and $c[0].name -eq 'verify-runner-caches' -and $c[0].runnerId -eq $(if ($variant -eq 'main') { 'server2' } else { 'server2-temp' })) "T-10 $variant only verify"
        Assert-C727 ($c[0].sourceSha -eq $sha) "T-10 $variant SHA"
        Assert-C727 ($p.Count -eq $(if ($variant -eq 'main') { 1 } else { 0 }) -and ($variant -ne 'main' -or ($p[0].suffix -eq '/drain/clear' -and [array]::IndexOf($t.Trace, $p[0]) -gt [array]::IndexOf($t.Trace, $c[0])))) "T-10 $variant clear gate"
    }
    Complete-Group 10 'same SHA verification'

    foreach ($variant in @('main', 'temp')) {
        $set = if ($variant -eq 'main') { @{ oldDraining = $true; failVerify = 'server2' } } else { @{ failVerify = 'server2-temp' } }
        $phase = if ($variant -eq 'main') { 'redeploy-old' } else { 'deploy-temp' }
        $t = Run-C727 -Scenario "smoke-red-$variant" -Phase $phase -Set $set
        Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('HostCaseFailed verify-runner-caches')) "T-11 $variant verdict"
        Assert-C727 (@(Posts $t | Where-Object suffix -eq '/drain/clear').Count -eq 0) "T-11 $variant held"
        Assert-C727 ($variant -ne 'temp' -or @(Posts $t | Where-Object runnerId -eq 'server2').Count -eq 0) "T-11 $variant no next phase"
    }
    Complete-Group 11 'smoke red'

    foreach ($phase in @('deploy-temp', 'redeploy-old', 'retire-temp')) {
        $runner = if ($phase -eq 'redeploy-old') { 'server2' } else { 'server2-temp' }
        $base = if ($phase -eq 'redeploy-old') { @{ oldDraining = $true } } elseif ($phase -eq 'retire-temp') { @{ tempDraining = $true; tempRetiredAt = '2026-09-27T10:00:00Z' } } else { @{} }
        foreach ($field in @('sessions', 'runnerSessions', 'queuedTasks')) {
            foreach ($kind in @('omitted', 'null')) {
                $set = $base.Clone(); $set.faultRunner = $runner; $set.faultField = $field; $set.faultKind = $kind;
                if ($phase -eq 'deploy-temp') { $set.tempContainer = $true } $set.faultValue = $null
                $t = Run-C727 -Scenario "unknown-$phase-$field-$kind" -Phase $phase -Set $set
                Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('RunnerCounterUnknown')) "T-12 $phase $field $kind verdict"
                Assert-C727 (@(Cases $t | Where-Object { $_.name -in @('runner-cache-seed', 'deploy-temp-runner', 'deploy-parent', 'retire-temp-runner') }).Count -eq 0) "T-12 $phase $field $kind no destructive host"
                Assert-C727 (@(Posts $t | Where-Object { $_.suffix -eq '/drain/clear' -or $_.runnerId -eq 'server2' }).Count -eq 0) "T-12 $phase $field $kind no clear/main drain"
            }
        }
    }
    $t = Run-C727 -Scenario unknown-deploy-temp-runnerSessions-garbage -Phase deploy-temp -Set @{ faultRunner = 'server2-temp'; faultField = 'runnerSessions'; faultKind = 'value'; faultValue = 'garbage' }
    Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('RunnerCounterUnknown')) 'T-12 live garbage verdict'
    Assert-C727 (-not (Has-Case $t 'runner-cache-seed') -and -not (Has-Case $t 'deploy-temp-runner')) 'T-12 live garbage no host'
    Assert-C727 ((Posts $t).Count -eq 0) 'T-12 live garbage no post'
    Complete-Group 12 'unknown counters'

    foreach ($field in @('runnerSessions', 'queuedTasks')) {
        $number = if ($field -eq 'runnerSessions') { 13 } else { 14 }
        foreach ($phase in @('redeploy-old', 'retire-temp')) {
            $runner = if ($phase -eq 'redeploy-old') { 'server2' } else { 'server2-temp' }
            $set = if ($phase -eq 'redeploy-old') { @{ oldDraining = $true } } else { @{ tempDraining = $true; tempRetiredAt = '2026-09-27T10:00:00Z' } }
            $set.faultRunner = $runner; $set.faultField = $field; $set.faultKind = 'value'; $set.faultValue = 1
            $t = Run-C727 -Scenario "busy-$phase-$field" -Phase $phase -Set $set
            Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('RunnerBusy')) "T-$number $phase verdict"
            Assert-C727 (@(Cases $t | Where-Object { $_.name -in @('deploy-parent', 'retire-temp-runner') }).Count -eq 0) "T-$number $phase no replacement"
            Assert-C727 (@(Posts $t | Where-Object suffix -eq '/drain/clear').Count -eq 0) "T-$number $phase no clear"
        }
        Complete-Group $number "$field busy"
    }

    foreach ($variant in @('sessions', 'retiredAt', 'draining')) {
        $set = @{ tempDraining = $true; tempRetiredAt = '2026-09-27T10:00:00Z' }
        if ($variant -eq 'sessions') { $set.faultRunner = 'server2-temp'; $set.faultField = 'sessions'; $set.faultKind = 'value'; $set.faultValue = 1 }
        if ($variant -eq 'retiredAt') { $set.tempRetiredAt = $null }
        if ($variant -eq 'draining') { $set.tempDraining = $false }
        $t = Run-C727 -Scenario "retire-$variant" -Phase retire-temp -Set $set
        $diag = if ($variant -eq 'sessions') { 'RunnerBusy' } else { 'TempRunnerNotRetired' }
        Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains($diag)) "T-15 $variant verdict"
        Assert-C727 (-not (Has-Case $t 'retire-temp-runner')) "T-15 $variant no retire"
        Assert-C727 ((Posts $t).Count -eq 0) "T-15 $variant hold unchanged"
    }
    Complete-Group 15 'retirement proof'

    foreach ($variant in @('temp-conflict', 'main-conflict', 'temp-503', 'main-503')) {
        $phase = if ($variant.StartsWith('temp')) { 'deploy-temp' } else { 'redeploy-old' }
        $runner = if ($variant.StartsWith('temp')) { 'server2-temp' } else { 'server2' }
        $set = if ($phase -eq 'deploy-temp') { @{ tempDraining = $true } } else { @{ oldDraining = $true } }
        $set.faultRunner = $runner
        if ($variant.EndsWith('503')) { $set.faultKind = '503' }
        else { $set.faultField = 'redirectTo'; $set.faultKind = 'value'; $set.faultValue = 'wrong-runner' }
        $t = Run-C727 -Scenario $variant -Phase $phase -Set $set
        $diag = if ($variant.EndsWith('503')) { 'RunnerApiFailed' } elseif ($variant.StartsWith('temp')) { 'TempRunnerDrainConflict' } else { 'OldRunnerDrainConflict' }
        Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains($diag)) "T-16 $variant verdict"
        Assert-C727 (@(Cases $t | Where-Object { $_.name -in @('runner-cache-seed', 'deploy-temp-runner', 'deploy-parent', 'retire-temp-runner') }).Count -eq 0) "T-16 $variant no destructive host"
        Assert-C727 ((Posts $t).Count -eq 0) "T-16 $variant hold unchanged"
    }
    Complete-Group 16 'conflicts/unavailable'

    $retired = @{ tempDraining = $true; tempRetiredAt = '2026-09-27T10:00:00Z'; tempContainer = $false; tempOffline = $true }
    $saved = '/home/mc/runner-cache-donor/temp-runner-cache.tar'
    $t = Run-C727 -Scenario 'retired-saved' -Phase deploy-temp -Set $retired -SavedDonor $saved
    $seedCase = @(Cases $t | Where-Object name -eq 'runner-cache-seed')
    Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('TempSavedDonorRequiresMaintenance')) 'T-17 saved phase names prerequisite'
    Assert-C727 ($seedCase.Count -eq 0) 'T-17 no seed before maintenance'
    Assert-C727 ((Posts $t).Count -eq 0 -and -not (Has-Case $t 'deploy-temp-runner')) 'T-17 no deployment or clear'
    $t = Run-C727 -Scenario 'retired-no-source' -Phase deploy-temp -Set ($retired + @{
        faultRunner = 'server2'; faultField = 'sessions'; faultKind = 'value'; faultValue = 1; oldDraining = $false
    })
    $seedCase = @(Cases $t | Where-Object name -eq 'runner-cache-seed')
    Assert-C727 ($t.Exit -eq 0 -and $seedCase.Count -eq 1 -and -not $seedCase[0].savedDonor) 'T-17 source never guessed'
    Assert-C727 (Has-Case $t 'deploy-temp-runner') 'T-17 marker reuse remains possible'
    Assert-C727 ((Posts $t | Where-Object runnerId -eq 'server2').Count -eq 0 -and
        -not (Has-Case $t 'deploy-parent')) 'T-17 cold marker reuse leaves busy main untouched'
    Complete-Group 17 'saved donor transport'

    Assert-RetiredStart
    Assert-RetiredStart -AlreadyAtSha $true
    Complete-Group 18 'retired start registration'

    foreach ($draining in @($false, $true)) {
        foreach ($retireWhenIdle in @($false, $true)) {
            Assert-ClearedOfflineStart -Draining $draining -RetireWhenIdle $retireWhenIdle
        }
    }
    Assert-ClearedOfflineStart -Draining $true -RetireWhenIdle $true -AlreadyAtSha $true
    Complete-Group 19 'cleared offline start variants'

    Assert-HostRace
    Assert-HostAbsence
    Assert-HostRecovery
    Assert-HostSaved

    $expectedGroups = if ($hasJq) { 24 } else { 23 }
    $expectedInvocations = if ($hasJq) { 66 } else { 62 }
    $expectedAssertions = if ($hasJq) { 227 } else { 218 }
    if ($script:groups -ne $expectedGroups -or $script:invocations -ne $expectedInvocations -or $script:assertions -ne $expectedAssertions) {
        throw "Frozen roster mismatch groups=$script:groups invocations=$script:invocations assertions=$script:assertions"
    }
    Write-Output "C849_ROLLING groups=$script:groups invocations=$script:invocations assertions=$script:assertions failures=0"
    Write-Output "V-32 assertions=$script:assertions failures=0"
    $success = $true
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine("C849_ROLLING groups=$script:groups invocations=$script:invocations assertions=$script:assertions failures=1")
    exit 1
}
finally {
    if ($ownsTempRoot) {
        if ($success -and -not $KeepTemp) {
            $ownedRoot = [IO.Path]::GetFullPath($tempRoot)
            $parent = [IO.Path]::GetFullPath((Join-Path $root '.antiphon'))
            $item = Get-Item -LiteralPath $ownedRoot -ErrorAction Stop
            if ([string]::IsNullOrWhiteSpace($tempRoot) -or (Split-Path -Parent $ownedRoot) -ne $parent -or
                (Split-Path -Leaf $ownedRoot) -cnotmatch '^c849-rolling-[0-9a-f]{32}$' -or
                ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'C849TempRootInvalid' }
            Remove-Item -LiteralPath $ownedRoot -Recurse -Force
            Write-Output "C849_TEMP removed=$ownedRoot"
        } else {
            Write-Output "C849_TEMP kept=$tempRoot"
        }
    }
}
