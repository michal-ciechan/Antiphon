# CARD-0849 frozen offline rolling roster. No network or live runner access.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$driver = Join-Path $PSScriptRoot 'deploy-server2.ps1'
$http = Join-Path $PSScriptRoot 'fixtures/c727-fake-http.ps1'
$verify = Join-Path $PSScriptRoot 'fixtures/c727-fake-verify.ps1'
$front = Join-Path $PSScriptRoot 'verify-card0849-caches.ps1'
$sha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$tempRoot = Join-Path $root ('.antiphon/c849-rolling-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
$script:assertions = 0
$script:invocations = 0
$script:groups = 0

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
    param([string]$Scenario, [string]$Phase = 'all', [hashtable]$Set = @{}, [bool]$TokenPresent = $true)
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
        oldDraining = $false; tempDraining = $false; tempRetiredAt = $null
        faultRunner = ''; faultField = ''; faultKind = ''; faultValue = $null; failVerify = ''
    }
    foreach ($key in $Set.Keys) { $state[$key] = $Set[$key] }
    $state | ConvertTo-Json -Compress | Set-Content -LiteralPath $statePath
    $psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile', '-File', $driver, '-Rolling', '-Sha', $sha, '-Phase', $Phase, '-WaitIdleMinutes', '1')) {
        [void]$psi.ArgumentList.Add($arg)
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
    return [pscustomobject]@{ Exit = $proc.ExitCode; Out = $stdout + $stderr; Trace = $trace; Sentinel = $sentinel }
}
function Cases { param($Run) return @($Run.Trace | Where-Object kind -eq 'case') }
function Posts { param($Run) return @($Run.Trace | Where-Object { $_.kind -eq 'http' -and $_.method -eq 'POST' }) }
function Has-Case { param($Run, [string]$Name) return @((Cases $Run) | Where-Object name -eq $Name).Count -gt 0 }

try {
    $t1 = Run-C727 -Scenario happy
    $c = Cases $t1; $p = Posts $t1
    Assert-C727 ($t1.Exit -eq 0) 'T-1 exit'
    Assert-C727 ((@($c | ForEach-Object { "$($_.name):$($_.runnerId)" }) -join ',') -eq 'runner-cache-seed:,deploy-temp-runner:,verify-runner-caches:server2-temp,deploy-parent:,verify-runner-caches:server2,retire-temp-runner:') 'T-1 host order/targets'
    Assert-C727 ($p.Count -eq 5) 'T-1 post count'
    Assert-C727 ($p[0].runnerId -eq 'server2-temp' -and $p[0].suffix -eq '/drain' -and $p[0].body.redirectTo -eq 'server2' -and $p[0].body.retireWhenIdle -eq $true -and [array]::IndexOf($t1.Trace, $p[0]) -lt [array]::IndexOf($t1.Trace, $c[0])) 'T-1 hold before seed'
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
    Assert-C727 ($p.Count -eq 1 -and $p[0].suffix -eq '/drain/clear' -and [array]::IndexOf($t.Trace, $p[0]) -gt [array]::IndexOf($t.Trace, $c[2])) 'T-8 hold until verified'
    Assert-C727 (@($p | Where-Object runnerId -eq 'server2').Count -eq 0) 'T-8 no main post'
    Complete-Group 8 'held fresh temp'

    $t = Run-C727 -Scenario retired -Phase deploy-temp -Set @{ tempDraining = $true; tempRetiredAt = '2026-09-27T10:00:00Z' }
    $c = Cases $t; $p = Posts $t
    Assert-C727 ($t.Exit -eq 0) 'T-9 exit'
    Assert-C727 ((@($c | ForEach-Object name) -join ',') -eq 'runner-cache-seed,deploy-temp-runner,verify-runner-caches') 'T-9 seed/replacement/verify'
    Assert-C727 ($p.Count -eq 1 -and $p[0].suffix -eq '/drain/clear') 'T-9 one clear'
    Assert-C727 ([array]::IndexOf($t.Trace, $p[0]) -gt [array]::IndexOf($t.Trace, $c[2])) 'T-9 clear after verify'
    Assert-C727 (@($p | Where-Object runnerId -eq 'server2').Count -eq 0) 'T-9 main untouched'
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
                $set = $base.Clone(); $set.faultRunner = $runner; $set.faultField = $field; $set.faultKind = $kind; $set.faultValue = $null
                $t = Run-C727 -Scenario "unknown-$phase-$field-$kind" -Phase $phase -Set $set
                Assert-C727 ($t.Exit -eq 2 -and $t.Out.Contains('RunnerCounterUnknown')) "T-12 $phase $field $kind verdict"
                Assert-C727 (@(Cases $t | Where-Object { $_.name -in @('runner-cache-seed', 'deploy-temp-runner', 'deploy-parent', 'retire-temp-runner') }).Count -eq 0) "T-12 $phase $field $kind no destructive host"
                Assert-C727 (@(Posts $t | Where-Object { $_.suffix -eq '/drain/clear' -or $_.runnerId -eq 'server2' }).Count -eq 0) "T-12 $phase $field $kind no clear/main drain"
            }
        }
    }
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

    if ($script:groups -ne 16 -or $script:invocations -ne 44 -or $script:assertions -ne 143) {
        throw "Frozen roster mismatch groups=$script:groups invocations=$script:invocations assertions=$script:assertions"
    }
    Write-Output 'C849_ROLLING groups=16 invocations=44 assertions=143 failures=0'
    Write-Output 'V-32 assertions=143 failures=0'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine("C849_ROLLING groups=$script:groups invocations=$script:invocations assertions=$script:assertions failures=1")
    exit 1
}
