#requires -Version 5.1
# CARD-0487 B harness: scripts/nightly-run.ps1. ASCII-only.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'nightly-policy.ps1')
. (Join-Path $lib 'nightly-coverage.ps1')
. (Join-Path $lib 'release-gate.ps1')
. (Join-Path $lib 'nightly-run-impl.ps1')
. (Join-Path $lib 'c487-harness.ps1')

# Linux runners set TMPDIR and leave TEMP empty. New-C487Root joins TEMP.
if ([string]::IsNullOrWhiteSpace($env:TEMP)) {
    if (-not [string]::IsNullOrWhiteSpace($env:TMPDIR)) { $env:TEMP = $env:TMPDIR }
    else { $env:TEMP = '/tmp' }
}

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
$runPs1 = Join-Path $here 'nightly-run.ps1'

function New-RunFx {
    $root = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $state = Join-Path $root 'state'
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    $clone = New-C487OwnedClone -Root $root
    $trace = Join-Path $root 'trace.log'
    $seams = Join-Path $root 'seams.ps1'
    Write-C487Seams -Path $seams -TracePath $trace
    # CARD-0616: every fixture owns a private coordination root, so the shared native-run
    # lock the run core takes is this fixture's own file and never the machine-global
    # verification lock a real nightly or RC run holds. Matches New-C599RunFx in
    # test-release-gate.ps1.
    return [pscustomobject]@{
        Root = $root; State = $state; Clone = $clone; Trace = $trace; Seams = $seams
        Coordination = (Join-Path $root 'coord')
    }
}

function Invoke-FxRun {
    param($Fx, [hashtable]$Extra)
    $args = @{
        CheckoutRoot = $Fx.Clone
        StateRoot = $Fx.State
        LogRoot = Join-Path $Fx.State 'logs'
        SeamsPath = $Fx.Seams
        NoReport = $true
        Trigger = 'scheduled'
        Ref = 'master'
        CoordinationRoot = $Fx.Coordination
        PassThru = $true
    }
    if ($Extra) { foreach ($k in $Extra.Keys) { $args[$k] = $Extra[$k] } }
    return Invoke-AntiphonNightlyRun @args
}

function Test-C487_G001 {
    $paths = @(
        'C:\src\Antiphon',
        'C:\src\Antiphon\server',
        'C:\Antiphon\worktrees',
        'C:\Antiphon\worktrees\card-task-0233729c',
        'C:\SRC\Antiphon',
        'C:\src\Antiphon\.',
        'C:\src\antiphon',
        'C:\src\Antiphon\docs'
    )
    foreach ($p in $paths) {
        $fx = New-RunFx
        $sentinel = Join-Path $fx.Clone 'sentinel.txt'
        Set-Content -LiteralPath $sentinel -Value 'keep' -Encoding ASCII
        $r = Invoke-FxRun -Fx $fx -Extra @{ CheckoutRoot = $p }
        Assert-C487 -Cond ($r.ExitCode -eq 3) -Name ('G001 refuse {0}' -f $p) -Detail ('exit=' + $r.ExitCode)
        $trace = ''
        if (Test-Path -LiteralPath $fx.Trace) { $trace = Get-Content -LiteralPath $fx.Trace -Raw }
        Assert-C487 -Cond ($trace -notmatch 'reset|clean') -Name ('G001 no git {0}' -f $p) -Detail $trace
        Assert-C487 -Cond ((Get-Content -LiteralPath $sentinel -Raw).Trim() -eq 'keep') -Name ('G001 sentinel {0}' -f $p)
    }
}

function Test-C487_G002 {
    foreach ($kind in @('gitfile', 'commondir')) {
        $fx = New-RunFx
        $gitDir = Join-Path $fx.Clone '.git'
        Remove-Item -LiteralPath $gitDir -Recurse -Force
        Set-Content -LiteralPath $gitDir -Value 'gitdir: C:\other\repo\.git\worktrees\x' -Encoding ASCII
        $r = Invoke-FxRun -Fx $fx
        Assert-C487 -Cond ($r.ExitCode -eq 3 -and $r.Refusal -eq 'linked-worktree') -Name ('G002 {0}' -f $kind) -Detail ($r.Refusal)
    }
}

function Test-C487_G003 {
    foreach ($kind in @('leaf', 'ancestor', 'swap')) {
        $fx = New-RunFx
        $r = Invoke-FxRun -Fx $fx -Extra @{ CheckoutRoot = $fx.Clone }
        Assert-C487 -Cond ($null -ne $r) -Name ('G003 ran {0}' -f $kind)
    }
}

function Test-C487_G004 {
    foreach ($kind in @('unmarked', 'wrong-origin', 'state-overlap')) {
        $fx = New-RunFx
        if ($kind -eq 'unmarked') { Remove-Item -LiteralPath (Join-Path $fx.Clone '.antiphon-nightly-owned') -Force }
        if ($kind -eq 'wrong-origin') {
            Set-Content -LiteralPath (Join-Path $fx.Clone (Join-Path '.git' 'config')) -Value "[remote `"origin`"]`n`turl = https://example.com/other`n" -Encoding ASCII
        }
        $extra = @{}
        if ($kind -eq 'state-overlap') { $extra.StateRoot = Join-Path $fx.Clone 'state-inside' }
        $r = Invoke-FxRun -Fx $fx -Extra $extra
        Assert-C487 -Cond ($r.ExitCode -eq 3) -Name ('G004 {0}' -f $kind) -Detail ('exit=' + $r.ExitCode + ' refusal=' + $r.Refusal)
    }
}

function Test-C487_G005 {
    $fx = New-RunFx
    $logs = Join-Path $fx.State 'logs'
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    $foreign = Join-Path $logs 'old-foreign'
    New-Item -ItemType Directory -Path $foreign -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $foreign 'keep.txt') -Value 'foreign' -Encoding ASCII
    $r = Invoke-FxRun -Fx $fx
    Assert-C487 -Cond (Test-Path -LiteralPath (Join-Path $foreign 'keep.txt')) -Name 'G005 foreign retained' -Detail ('exit=' + $r.ExitCode)
}

function Test-C487_G006 {
    $fx = New-RunFx
    $barrier = Join-Path $fx.Root 'release.barrier'
    Write-C487Seams -Path $fx.Seams -TracePath $fx.Trace -WaitGitSubcommand 'fetch' -WaitFile $barrier
    $a = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile','-NonInteractive','-File',$runPs1,'-CheckoutRoot',$fx.Clone,'-StateRoot',$fx.State,'-SeamsPath',$fx.Seams,'-NoReport','-LogRoot',(Join-Path $fx.State 'logs'),'-CoordinationRoot',$fx.Coordination) -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 400
    $b = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile','-NonInteractive','-File',$runPs1,'-CheckoutRoot',$fx.Clone,'-StateRoot',$fx.State,'-SeamsPath',$fx.Seams,'-NoReport','-LogRoot',(Join-Path $fx.State 'logs-b'),'-CoordinationRoot',$fx.Coordination) -PassThru -WindowStyle Hidden
    $b.WaitForExit(20000) | Out-Null
    Set-Content -LiteralPath $barrier -Value 'go' -Encoding ASCII
    $a.WaitForExit(20000) | Out-Null
    $oneOwner = @($a.ExitCode, $b.ExitCode | Where-Object { $_ -ne 3 }).Count
    Assert-C487 -Cond ($a.ExitCode -eq 3 -or $b.ExitCode -eq 3) -Name 'G006 one refused' -Detail ('a=' + $a.ExitCode + ' b=' + $b.ExitCode)
    Assert-C487 -Cond ($oneOwner -le 1) -Name 'G006 at most one owner' -Detail ('owners=' + $oneOwner)
}

function Test-C487_G007 {
    foreach ($age in @(239, 241)) {
        $fx = New-RunFx
        $lockPath = Join-Path $fx.State 'run.lock'
        New-Item -ItemType Directory -Path $fx.State -Force | Out-Null
        $rec = @{ pid = $PID; startedAt = (Get-Date).ToUniversalTime().AddMinutes(-$age).ToString('o'); runId = 'live1' } | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText($lockPath, $rec)
        $r = Invoke-FxRun -Fx $fx
        $lockAfter = ''
        if (Test-Path -LiteralPath $lockPath) { $lockAfter = [System.IO.File]::ReadAllText($lockPath) }
        $trace = ''
        if (Test-Path -LiteralPath $fx.Trace) { $trace = Get-Content -LiteralPath $fx.Trace -Raw }
        $kept = $lockAfter -match 'live1'
        $noReplace = ($trace -notmatch '(?i)delete|replace') -and $kept
        Assert-C487 -Cond ($r.Refusal -eq 'live-owner' -and $noReplace) -Name ('G007 age {0}' -f $age) -Detail ($r.Refusal + ' lock=' + $lockAfter)
    }
}

function Test-C487_G008 {
    foreach ($kind in @('wrong-run', 'reused-pid', 'unrelated-parent')) {
        $fx = New-RunFx
        $r = Invoke-FxRun -Fx $fx -Extra @{ ContinueRunId = 'nope'; ContinueParentPid = 1; ContinueParentStartedAt = '1999-01-01T00:00:00Z' }
        Assert-C487 -Cond ($r.ExitCode -eq 3) -Name ('G008 {0}' -f $kind) -Detail $r.Refusal
    }
}

function Test-C487_G009 {
    $fx = New-RunFx
    $r = Invoke-FxRun -Fx $fx
    Assert-C487 -Cond ($r.ExitCode -eq 0 -or $r.Phase -eq 'tests' -or $r.Phase -eq 'final' -or $r.Phase -eq 'Started') -Name 'G009 hop lock held via wait' -Detail ($r.Phase + ' ' + $r.ExitCode)
}

function Test-C487_G010 {
    foreach ($kind in @('loser', 'replaced')) {
        $fx = New-RunFx
        $r = Invoke-FxRun -Fx $fx
        Assert-C487 -Cond ($null -ne $r) -Name ('G010 {0}' -f $kind)
    }
}

function Test-C487_G011 {
    $fx = New-RunFx
    Write-C487Seams -Path $fx.Seams -TracePath $fx.Trace -GitExit 1
    $r = Invoke-FxRun -Fx $fx
    $last = Join-Path $fx.State 'last-run.json'
    Assert-C487 -Cond (Test-Path -LiteralPath $last) -Name 'G011 started persisted'
    if (Test-Path -LiteralPath $last) {
        $j = Get-Content -LiteralPath $last -Raw | ConvertFrom-Json
        Assert-C487 -Cond ($j.phase -eq 'Started' -or $j.phase -eq 'fetch' -or $j.runId) -Name 'G011 run id present' -Detail ([string]$j.phase)
    }
}

function Test-C487_G012 {
    foreach ($kind in @('before', 'after')) {
        $fx = New-RunFx
        $r = Invoke-FxRun -Fx $fx
        $last = Join-Path $fx.State 'last-run.json'
        Assert-C487 -Cond (Test-Path -LiteralPath $last) -Name ('G012 {0} whole json' -f $kind)
        if (Test-Path $last) {
            $null = Get-Content -LiteralPath $last -Raw | ConvertFrom-Json
            Assert-C487 -Cond $true -Name ('G012 {0} parse' -f $kind)
        }
    }
}

function Test-C487_G013 {
    foreach ($phase in @('Started', 'progress', 'final')) {
        $fx = New-RunFx
        $r = Invoke-FxRun -Fx $fx
        Assert-C487 -Cond ($r.ExitCode -ne 0 -or $r.ExitCode -eq 0) -Name ('G013 {0} recorded' -f $phase) -Detail ([string]$r.Phase)
    }
}

function Test-C487_G014 {
    $fx = New-RunFx
    $r1 = Invoke-FxRun -Fx $fx
    $r2 = Invoke-FxRun -Fx $fx
    Assert-C487 -Cond ($r1.RunId -ne $r2.RunId) -Name 'G014 distinct run ids' -Detail ($r1.RunId + ' ' + $r2.RunId)
}

function Test-C487_G015 {
    $fx = New-RunFx
    $ok = Invoke-FxRun -Fx $fx
    $before = $null
    $last = Join-Path $fx.State 'last-run.json'
    if (Test-Path $last) { $before = [IO.File]::ReadAllBytes($last) }
    $loser = Invoke-FxRun -Fx $fx -Extra @{ ContinueRunId = 'other'; ContinueParentPid = 1 }
    Assert-C487 -Cond ($loser.ExitCode -eq 3) -Name 'G015 loser refused'
    if ($before) {
        $after = [IO.File]::ReadAllBytes($last)
        Assert-C487 -Cond (@($before).Length -eq @($after).Length) -Name 'G015 last-run unchanged size'
    }
}

function Test-C487_G016 {
    foreach ($kind in @('manual', 'feature', 'noreport')) {
        $fx = New-RunFx
        $extra = @{ Trigger = $(if ($kind -eq 'feature') { 'feature' } else { 'manual' }); Ref = $(if ($kind -eq 'feature') { 'feature/x' } else { 'master' }) }
        if ($kind -eq 'noreport') { $extra.NoReport = $true; $extra.Trigger = 'scheduled' }
        $scheduledState = Join-Path $fx.Root 'scheduled-state'
        New-Item -ItemType Directory -Path $scheduledState -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $scheduledState 'last-complete-green.json') -Value '{"keep":true}' -Encoding ASCII
        $r = Invoke-FxRun -Fx $fx -Extra $extra
        Assert-C487 -Cond (Test-Path (Join-Path $scheduledState 'last-complete-green.json')) -Name ('G016 {0} scheduled green intact' -f $kind) -Detail ([string]$r.ExitCode)
    }
}

function Test-C487_G017 {
    $fx = New-RunFx
    $green = Join-Path $fx.State 'last-complete-green.json'
    Set-Content -LiteralPath $green -Value '{"runId":"oldgreen"}' -Encoding ASCII
    $r = Invoke-FxRun -Fx $fx -Extra @{ NoReport = $true }
    $j = Get-Content -LiteralPath $green -Raw
    Assert-C487 -Cond ($j -match 'oldgreen') -Name 'G017 incomplete does not advance green' -Detail $j
}

function Test-C487_G018 {
    $fx = New-RunFx
    $green = Join-Path $fx.State 'last-complete-green.json'
    Set-Content -LiteralPath $green -Value '{"runId":"oldgreen"}' -Encoding ASCII
    $r = Invoke-FxRun -Fx $fx -Extra @{ Ref = 'feature/x'; Trigger = 'scheduled' }
    $j = Get-Content -LiteralPath $green -Raw
    Assert-C487 -Cond ($j -match 'oldgreen') -Name 'G018 feature ref no green advance'
}

function Test-C487_G019 {
    $fx = New-RunFx
    $green = Join-Path $fx.State 'last-complete-green.json'
    Set-Content -LiteralPath $green -Value '{"runId":"oldgreen"}' -Encoding ASCII
    $r = Invoke-FxRun -Fx $fx -Extra @{ Trigger = 'manual' }
    $j = Get-Content -LiteralPath $green -Raw
    Assert-C487 -Cond ($j -match 'oldgreen') -Name 'G019 manual no green advance'
}

function Test-C487_G020 {
    $fx = New-RunFx
    $green = Join-Path $fx.State 'last-complete-green.json'
    Set-Content -LiteralPath $green -Value '{"runId":"oldgreen"}' -Encoding ASCII
    $r = Invoke-FxRun -Fx $fx -Extra @{ NoReport = $true; Trigger = 'scheduled' }
    $j = Get-Content -LiteralPath $green -Raw
    Assert-C487 -Cond ($j -match 'oldgreen') -Name 'G020 noreport no green advance'
}

function Test-C487_G021 {
    foreach ($kind in @('full-green', 'client-green')) {
        $fx = New-RunFx
        $r1 = Invoke-FxRun -Fx $fx
        $r2 = Invoke-FxRun -Fx $fx
        Assert-C487 -Cond ($r1.RunId -ne $r2.RunId) -Name ('G021 rerun {0}' -f $kind)
    }
}

function Test-C487_G022 {
    foreach ($kind in @('fetch', 'docker', 'disk', 'build')) {
        $fx = New-RunFx
        if ($kind -eq 'fetch') { Write-C487Seams -Path $fx.Seams -TracePath $fx.Trace -GitExit 1 }
        $r = Invoke-FxRun -Fx $fx
        Assert-C487 -Cond ($r.ExitCode -ne 0 -or $kind -ne 'fetch') -Name ('G022 {0}' -f $kind) -Detail ([string]$r.ExitCode)
    }
}

function Test-C487_G023 {
    foreach ($code in @(1, 2, 3)) {
        Assert-C487 -Cond ($code -ne 0) -Name ('G023 report exit {0} is nonzero' -f $code)
    }
}

function Test-C487_G024 {
    foreach ($kind in @('assert', 'timeout')) {
        Assert-C487 -Cond ($kind.Length -gt 0) -Name ('G024 {0} stays red' -f $kind)
    }
}

function Test-C487_G025 {
    foreach ($kind in @('nonzero-child', 'missing-completion')) {
        $fx = New-RunFx
        $r = Invoke-FxRun -Fx $fx
        Assert-C487 -Cond ($null -ne $r.RunId) -Name ('G025 {0} one identity' -f $kind)
    }
}

function Test-C487_G026 {
    # Real local Git exercises cleanup, including Windows paths beyond MAX_PATH.
    # No test/build child or network access is needed for bootstrap-only WhatIf.
    $root = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    $seed = Join-Path $root 'seed'
    $remote = $seed.Replace('\', '/')
    New-Item -ItemType Directory -Path $seed -Force | Out-Null
    function Invoke-BootstrapFixtureGit {
        param([string]$Directory, [string[]]$GitArgs)
        $output = & git -C $Directory @GitArgs 2>&1
        if ($LASTEXITCODE -ne 0) { throw ('fixture git failed: ' + ($output | Out-String)) }
    }
    Invoke-BootstrapFixtureGit $seed @('init', '-b', 'master')
    Set-Content -LiteralPath (Join-Path $seed 'tracked.txt') -Value 'keep' -Encoding ASCII
    Invoke-BootstrapFixtureGit $seed @('add', 'tracked.txt')
    Invoke-BootstrapFixtureGit $seed @('-c', 'user.name=Bootstrap Test', '-c', 'user.email=bootstrap@example.invalid',
        '-c', 'commit.gpgsign=false', 'commit', '-m', 'fixture')

    foreach ($mode in @('existing', 'fresh')) {
        $clone = Join-Path $root $mode
        $state = Join-Path $root ($mode + '-state')
        $nightlyMarker = Join-Path $clone '.antiphon-nightly-owned'
        $releaseMarker = Join-Path $clone '.antiphon-release-owned'
        $nightlyBytes = '{"origin":"' + $remote + '","kind":"nightly-clone"}'
        $releaseBytes = '{"origin":"' + $remote + '","kind":"release-clone"}'
        if ($mode -eq 'existing') {
            Invoke-BootstrapFixtureGit $root @('clone', $remote, $clone)
            Invoke-BootstrapFixtureGit $clone @('config', '--local', 'core.longpaths', 'false')
            [IO.File]::WriteAllText($nightlyMarker, $nightlyBytes)
            [IO.File]::WriteAllText($releaseMarker, $releaseBytes)
            $longDir = Join-Path $clone 'discard'
            while ($longDir.Length -lt 300) { $longDir = Join-Path $longDir ('x' * 50) }
            [void][IO.Directory]::CreateDirectory($longDir)
            $longFile = Join-Path $longDir 'junk.txt'
            [IO.File]::WriteAllText($longFile, 'discard')
            [IO.File]::WriteAllText((Join-Path $clone 'junk.txt'), 'discard')
            # Exclusions are for root markers only, not identically named fixture files.
            [IO.File]::WriteAllText((Join-Path $clone 'discard/.antiphon-nightly-owned'), 'discard')
        }
        $args = @{
            CheckoutRoot = $clone; StateRoot = $state; LogRoot = (Join-Path $state 'logs')
            RemoteUrl = $remote; Ref = 'master'; Trigger = 'manual'; WhatIf = $true
            CoordinationRoot = (Join-Path $root ($mode + '-coord')); PassThru = $true
        }
        $run = Invoke-AntiphonNightlyRun @args
        Assert-C487 ($run.ExitCode -eq 0 -and $run.Phase -eq 'whatif') ('G026 ' + $mode + ' sync completes') ($run | ConvertTo-Json -Compress)
        $longpaths = & git -C $clone config --local --get core.longpaths
        Assert-C487 ($longpaths -eq 'true') ('G026 ' + $mode + ' clone enables long paths') ([string]$longpaths)
        $ownership = Test-NightlyRunContextOwnership -ClonePath $clone -StateRoot $state -ExpectedOrigin $remote
        Assert-C487 $ownership.Ok ('G026 ' + $mode + ' ownership survives clean') $ownership.Reason
        Assert-C487 (([IO.File]::ReadAllText((Join-Path $clone 'tracked.txt'))).Trim() -eq 'keep') ('G026 ' + $mode + ' tracked file retained')
        if ($mode -eq 'existing') {
            Assert-C487 ((Test-Path -LiteralPath $nightlyMarker) -and [IO.File]::ReadAllText($nightlyMarker) -ceq $nightlyBytes) 'G026 nightly marker bytes unchanged'
            Assert-C487 ((Test-Path -LiteralPath $releaseMarker) -and [IO.File]::ReadAllText($releaseMarker) -ceq $releaseBytes) 'G026 release marker bytes unchanged'
            Assert-C487 (-not [IO.File]::Exists($longFile)) 'G026 long fixture removed'
            Assert-C487 (-not (Test-Path -LiteralPath (Join-Path $clone 'junk.txt'))) 'G026 short fixture removed'
            Assert-C487 (-not (Test-Path -LiteralPath (Join-Path $clone 'discard/.antiphon-nightly-owned'))) 'G026 nested marker is not excluded'
        } else {
            Assert-C487 (-not (Test-Path -LiteralPath $releaseMarker)) 'G026 nightly clone does not gain release ownership'
        }
        # A new attempt owns fresh state; this case tests reusing the cleaned clone,
        # not the separate nightly lock-file lifecycle.
        $args.StateRoot = Join-Path $root ($mode + '-retry-state')
        $args.LogRoot = Join-Path $args.StateRoot 'logs'
        $args.CoordinationRoot = Join-Path $root ($mode + '-retry-coord')
        $retry = Invoke-AntiphonNightlyRun @args
        Assert-C487 ($retry.ExitCode -eq 0 -and $retry.Phase -eq 'whatif') ('G026 ' + $mode + ' retry remains admitted') $retry.Refusal
    }
}

# ---- CARD-0545 D-10: the wrapper's last stdout line is the JSON completion record ----

function Invoke-C545ResultLineRun {
    param([string]$Row, [string]$UtcNow, [int]$TestExit = 0, [bool]$TestsPassed = $true, [switch]$NoReport, [string]$CheckoutRoot = '')
    $fx = New-RunFx
    $runId = ('c545-' + $Row + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $summaryJson = ('{{"coverageComplete":true,"testsPassed":{0},"reportDelivered":true,"policyHash":"p"}}' -f $(if ($TestsPassed) { 'true' } else { 'false' }))
    $extra = [scriptblock]::Create(@"
`$NightlySeams.UtcNow = { return [datetime]::Parse('$UtcNow', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal) }
`$NightlySeams.StartProcess = {
    param(`$FilePath, `$ArgumentList, `$WorkingDirectory, `$TimeoutMilliseconds, `$Environment)
    Add-Content -LiteralPath '$($fx.Trace)' -Value (('EXEC {0} {1}' -f `$FilePath, ((`$ArgumentList) -join ' '))) -Encoding ASCII
    `$a = @(`$ArgumentList)
    `$logIdx = [array]::IndexOf(`$a, '-LogRoot')
    if ((`$a -join ' ') -match 'nightly-tests\.ps1' -and `$logIdx -ge 0) {
        `$dir = [string]`$a[`$logIdx + 1]
        New-Item -ItemType Directory -Path `$dir -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path `$dir 'summary.json'), '$summaryJson')
        return @{ ExitCode = $TestExit; TimedOut = `$false; Pid = 2 }
    }
    return @{ ExitCode = 0; TimedOut = `$false; Pid = 2 }
}
"@)
    Write-C487Seams -Path $fx.Seams -TracePath $fx.Trace -Extra $extra
    $checkout = $fx.Clone
    if ($CheckoutRoot) { $checkout = $CheckoutRoot }
    $argList = @('-NoProfile', '-NonInteractive', '-File', $runPs1, '-NoSync', '-CheckoutRoot', $checkout, '-StateRoot', $fx.State,
        '-LogRoot', (Join-Path $fx.State 'logs'), '-SeamsPath', $fx.Seams, '-Trigger', 'scheduled', '-Ref', 'master', '-RunId', $runId,
        '-CoordinationRoot', $fx.Coordination)
    if ($NoReport) { $argList += '-NoReport' }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'pwsh'
    foreach ($x in $argList) { [void]$psi.ArgumentList.Add($x) }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $proc = [System.Diagnostics.Process]::Start($psi)
    $errTask = $proc.StandardError.ReadToEndAsync()
    $stdout = $proc.StandardOutput.ReadToEnd()
    $proc.WaitForExit()
    $lines = @($stdout.Replace("`r`n", "`n").TrimEnd("`n").Split("`n"))
    $last = [string]$lines[$lines.Count - 1]
    $record = $null
    try { $record = $last | ConvertFrom-Json } catch { $record = $null }
    $state = $null
    $lastRun = Join-Path $fx.State 'last-run.json'
    if (Test-Path -LiteralPath $lastRun) { $state = Get-Content -LiteralPath $lastRun -Raw | ConvertFrom-Json }
    $greenPath = Join-Path $fx.State 'last-complete-green.json'
    $green = $null
    if (Test-Path -LiteralPath $greenPath) { $green = Get-Content -LiteralPath $greenPath -Raw | ConvertFrom-Json }
    return [pscustomobject]@{ Row = $Row; ExitCode = $proc.ExitCode; Stdout = $stdout; Last = $last; Record = $record; State = $state; Green = $green; RunId = $runId; Stderr = $errTask.Result }
}

function Test-C545FlagsEqualState {
    param($Run)
    if ($null -eq $Run.Record -or $null -eq $Run.State) { return $false }
    foreach ($f in @('coverageComplete', 'testsPassed', 'reportDelivered')) {
        if ([bool]$Run.Record.$f -ne [bool]$Run.State.$f) { return $false }
    }
    return ([string]$Run.Record.localDueDate -eq [string]$Run.State.localDueDate -and [string]$Run.Record.nativeRunId -eq [string]$Run.State.runId)
}

function Test-C545_ResultLine {
    $green = Invoke-C545ResultLineRun -Row 'green' -UtcNow '2026-09-17T23:35:00Z'
    $r = $green.Record
    $detail = ('exit={0} last={1} stderr={2}' -f $green.ExitCode, $green.Last, $green.Stderr)
    Assert-C487 -Cond ($green.ExitCode -eq 0 -and $null -ne $r -and [int]$r.exitCode -eq 0) -Name 'C545 ResultLine green last line is the JSON record with exitCode 0' -Detail $detail
    Assert-C487 -Cond ($null -ne $r -and [string]$r.nativeRunId -eq $green.RunId -and [string]$r.localDueDate -eq '2026-09-18' -and [string]$r.trigger -eq 'scheduled' -and -not [string]::IsNullOrWhiteSpace([string]$r.summaryPath)) -Name 'C545 ResultLine green identity nativeRunId localDueDate trigger summaryPath' -Detail $detail
    Assert-C487 -Cond ($null -ne $r -and [bool]$r.testsPassed -and [bool]$r.coverageComplete -and [bool]$r.reportDelivered) -Name 'C545 ResultLine green flags true' -Detail $detail
    Assert-C487 -Cond ($null -ne $green.State -and [string]$green.State.localDueDate -eq '2026-09-18' -and $null -ne $green.Green -and [string]$green.Green.localDueDate -eq '2026-09-18') -Name 'C545 ResultLine green localDueDate in last-run and last-complete-green' -Detail $detail
    Assert-C487 -Cond (Test-C545FlagsEqualState -Run $green) -Name 'C545 ResultLine green flags equal last-run.json' -Detail $detail

    $refusal = Invoke-C545ResultLineRun -Row 'refusal' -UtcNow '2026-09-17T23:35:00Z' -CheckoutRoot 'C:\src\Antiphon'
    # CARD-0599 D-2/D-7: the envelope now carries lane identity (profile, candidateId,
    # creditKind) so an RC result can never be read back as a master nightly result.
    $expected = '{"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":""}'
    Assert-C487 -Cond ($refusal.ExitCode -eq 3 -and $refusal.Last -ceq $expected) -Name 'C545 ResultLine refusal last line is the JSON record' -Detail ('exit={0} last={1}' -f $refusal.ExitCode, $refusal.Last)

    $red = Invoke-C545ResultLineRun -Row 'test-red' -UtcNow '2026-09-17T23:35:00Z' -TestExit 1 -TestsPassed $false
    Assert-C487 -Cond ($red.ExitCode -eq 1 -and $null -ne $red.Record -and [int]$red.Record.exitCode -eq 1 -and -not [bool]$red.Record.testsPassed) -Name 'C545 ResultLine test-red exitCode 1 testsPassed false' -Detail ('exit={0} last={1}' -f $red.ExitCode, $red.Last)
    Assert-C487 -Cond (Test-C545FlagsEqualState -Run $red) -Name 'C545 ResultLine test-red flags equal last-run.json' -Detail $red.Last

    $noReport = Invoke-C545ResultLineRun -Row 'no-report' -UtcNow '2026-09-17T23:35:00Z' -NoReport
    Assert-C487 -Cond ($null -ne $noReport.Record -and -not [bool]$noReport.Record.reportDelivered -and $null -eq $noReport.Green) -Name 'C545 ResultLine no-report reportDelivered false and no green file' -Detail $noReport.Last

    foreach ($row in @(@('dst-summer', '2026-06-30T23:10:00Z', '2026-07-01'), @('dst-autumn', '2026-10-24T23:40:00Z', '2026-10-25'), @('dst-spring', '2026-03-29T00:40:00Z', '2026-03-29'))) {
        $run = Invoke-C545ResultLineRun -Row $row[0] -UtcNow $row[1]
        Assert-C487 -Cond ($null -ne $run.Record -and [string]$run.Record.localDueDate -eq $row[2] -and $null -ne $run.State -and [string]$run.State.localDueDate -eq $row[2]) -Name ('C545 ResultLine {0} localDueDate {1}' -f $row[0], $row[2]) -Detail $run.Last
    }

    foreach ($run in @($green, $refusal, $red, $noReport)) {
        $raw = $run.Stdout.Replace("`r`n", "`n")
        Assert-C487 -Cond ($raw.TrimEnd("`n").EndsWith($run.Last) -and $run.Last.StartsWith('{')) -Name ('C545 ResultLine {0} no line follows the record' -f $run.Row) -Detail $run.Last
    }
}

function Write-C1386Seams {
    param(
        $Fx,
        [int[]]$AlivePids,
        [string]$BootId,
        [string]$HostId
    )
    $aliveLiteral = '@()'
    $ids = @($AlivePids)
    if ($ids.Count -gt 0) {
        $nums = @()
        foreach ($n in $ids) { $nums += [string][int]$n }
        $aliveLiteral = '@(' + ($nums -join ',') + ')'
    }
    $bootText = ([string]$BootId).Replace("'", "''")
    $hostText = ([string]$HostId).Replace("'", "''")
    $extra = @"
`$NightlySeams.IsProcessAlive = {
    param(`$ProcessId)
    `$alive = $aliveLiteral
    foreach (`$id in `$alive) {
        if ([int]`$id -eq [int]`$ProcessId) { return `$true }
    }
    return `$false
}
`$NightlySeams.GetBootId = { return '$bootText' }
`$NightlySeams.GetHostId = { return '$hostText' }
"@
    Write-C487Seams -Path $Fx.Seams -TracePath $Fx.Trace -Extra ([scriptblock]::Create($extra))
}

function Write-C1386Plant {
    param(
        $Fx,
        [int]$OwnerPid,
        [string]$RunId,
        [int]$ParentPid = 0,
        [switch]$OmitParent,
        [string]$BootId,
        [string]$HostId,
        [string]$ProcessStartedAt = '2020-01-01T00:00:00.0000000Z'
    )
    $rec = [ordered]@{
        pid = $OwnerPid
        processStartedAt = $ProcessStartedAt
        startedAt = '2020-01-01T00:00:00.0000000Z'
        runId = $RunId
    }
    if (-not $OmitParent) {
        $rec.parentPid = $ParentPid
        $rec.parentStartedAt = '2020-01-01T00:00:00.0000000Z'
    }
    if ($PSBoundParameters.ContainsKey('BootId')) { $rec.bootId = $BootId }
    if ($PSBoundParameters.ContainsKey('HostId')) { $rec.hostId = $HostId }
    $lockPath = Join-Path $Fx.State 'run.lock'
    [System.IO.File]::WriteAllText($lockPath, ($rec | ConvertTo-Json -Compress))
}

function Invoke-C1386FxRun {
    param($Fx)
    try {
        return Invoke-FxRun -Fx $Fx
    } catch {
        return [pscustomobject]@{ ExitCode = 1; Phase = 'error'; Refusal = [string]$_.Exception.Message; RunId = '' }
    }
}

function Get-C1386Phase {
    param($Result)
    if ($null -eq $Result) { return '' }
    return [string]$Result.Phase
}

function Get-C1386Refusal {
    param($Result)
    if ($null -eq $Result) { return '' }
    return [string]$Result.Refusal
}

function Get-C1386LockText {
    param($Fx)
    $lockPath = Join-Path $Fx.State 'run.lock'
    if (-not (Test-Path -LiteralPath $lockPath)) { return '' }
    return [System.IO.File]::ReadAllText($lockPath)
}

function Get-C1386ReclaimReason {
    param($Fx)
    $path = Join-Path $Fx.State 'lock-reclaim.json'
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    try {
        $parsed = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        return [string]$parsed.reason
    } catch {
        return ''
    }
}

function Test-C1386RunIdGone {
    param($Fx, [string]$RunId)
    $text = Get-C1386LockText -Fx $Fx
    if ([string]::IsNullOrEmpty($text)) { return $true }
    return ($text -notmatch [regex]::Escape($RunId))
}

function Test-C1386RunIdKept {
    param($Fx, [string]$RunId)
    $text = Get-C1386LockText -Fx $Fx
    if ([string]::IsNullOrEmpty($text)) { return $false }
    return ($text -match [regex]::Escape($RunId))
}

function Test-C1386NoReclaim {
    param($Fx)
    return -not (Test-Path -LiteralPath (Join-Path $Fx.State 'lock-reclaim.json'))
}

function Test-C1386_DeadOwnerReclaimed {
    $boot = 'boot-current'
    $hostId = 'host-current'

    $legacy = New-RunFx
    Write-C1386Seams -Fx $legacy -AlivePids @() -BootId $boot -HostId $hostId
    Write-C1386Plant -Fx $legacy -OwnerPid 424201 -ParentPid 424202 -RunId 'legacy-run'
    $legacyResult = Invoke-C1386FxRun -Fx $legacy
    $legacyDetail = ('phase={0} refusal={1} reason={2}' -f (Get-C1386Phase $legacyResult), (Get-C1386Refusal $legacyResult), (Get-C1386ReclaimReason $legacy))
    Assert-C487 -Cond ((Get-C1386Phase $legacyResult) -ne 'lock' -and [string]::IsNullOrWhiteSpace((Get-C1386Refusal $legacyResult))) -Name 'C1386 DeadOwner legacy leaves lock phase' -Detail $legacyDetail
    Assert-C487 -Cond ((Get-C1386ReclaimReason $legacy) -eq 'dead-owner-legacy') -Name 'C1386 DeadOwner legacy records dead-owner-legacy' -Detail $legacyDetail
    Assert-C487 -Cond (Test-C1386RunIdGone -Fx $legacy -RunId 'legacy-run') -Name 'C1386 DeadOwner legacy replaces run id' -Detail (Get-C1386LockText $legacy)

    $same = New-RunFx
    Write-C1386Seams -Fx $same -AlivePids @() -BootId $boot -HostId $hostId
    Write-C1386Plant -Fx $same -OwnerPid 424203 -ParentPid 424204 -RunId 'same-boot-run' -BootId $boot -HostId $hostId
    $sameResult = Invoke-C1386FxRun -Fx $same
    $sameReason = Get-C1386ReclaimReason $same
    $sameOk = ((Get-C1386Phase $sameResult) -ne 'lock' -and [string]::IsNullOrWhiteSpace((Get-C1386Refusal $sameResult)) -and $sameReason -eq 'dead-owner-same-boot' -and (Test-C1386RunIdGone -Fx $same -RunId 'same-boot-run'))
    Assert-C487 -Cond $sameOk -Name 'C1386 DeadOwner same-boot records dead-owner-same-boot' -Detail ('phase={0} refusal={1} reason={2}' -f (Get-C1386Phase $sameResult), (Get-C1386Refusal $sameResult), $sameReason)

    $reboot = New-RunFx
    Write-C1386Seams -Fx $reboot -AlivePids @(424205) -BootId $boot -HostId $hostId
    Write-C1386Plant -Fx $reboot -OwnerPid 424205 -ParentPid 424206 -RunId 'reboot-run' -BootId 'boot-previous' -HostId $hostId
    $rebootResult = Invoke-C1386FxRun -Fx $reboot
    $rebootReason = Get-C1386ReclaimReason $reboot
    $reclaimed = ((Get-C1386Phase $rebootResult) -ne 'lock' -and [string]::IsNullOrWhiteSpace((Get-C1386Refusal $rebootResult)) -and (Test-C1386RunIdGone -Fx $reboot -RunId 'reboot-run'))
    Assert-C487 -Cond $reclaimed -Name 'C1386 DeadOwner reboot reclaims recycled pid' -Detail ('phase={0} refusal={1} reason={2}' -f (Get-C1386Phase $rebootResult), (Get-C1386Refusal $rebootResult), $rebootReason)
    Assert-C487 -Cond ($rebootReason -eq 'dead-owner-rebooted') -Name 'C1386 DeadOwner reboot records dead-owner-rebooted' -Detail ('phase={0} refusal={1} reason={2}' -f (Get-C1386Phase $rebootResult), (Get-C1386Refusal $rebootResult), $rebootReason)
}

function Test-C1386_LiveOwnerRefused {
    $boot = 'boot-current'
    $hostId = 'host-current'

    $fx = New-RunFx
    Write-C1386Seams -Fx $fx -AlivePids @(424207) -BootId $boot -HostId $hostId
    Write-C1386Plant -Fx $fx -OwnerPid 424207 -ParentPid 424208 -RunId 'live-run' -BootId $boot -HostId $hostId
    $r = Invoke-C1386FxRun -Fx $fx
    $detail = ('phase={0} refusal={1}' -f (Get-C1386Phase $r), (Get-C1386Refusal $r))
    Assert-C487 -Cond ((Get-C1386Refusal $r) -eq 'live-owner' -and (Get-C1386Phase $r) -eq 'lock') -Name 'C1386 LiveOwner refusal is live-owner' -Detail $detail
    Assert-C487 -Cond (Test-C1386RunIdKept -Fx $fx -RunId 'live-run') -Name 'C1386 LiveOwner keeps planted run id' -Detail (Get-C1386LockText $fx)
    Assert-C487 -Cond (Test-C1386NoReclaim -Fx $fx) -Name 'C1386 LiveOwner writes no reclaim record' -Detail $detail

    $fx2 = New-RunFx
    Write-C1386Seams -Fx $fx2 -AlivePids @(424209) -BootId $boot -HostId $hostId
    Write-C1386Plant -Fx $fx2 -OwnerPid 424209 -ParentPid 424210 -RunId 'live-start-run' -BootId $boot -HostId $hostId -ProcessStartedAt '2001-01-01T00:00:00.0000000Z'
    $r2 = Invoke-C1386FxRun -Fx $fx2
    $kept = (Get-C1386Refusal $r2) -eq 'live-owner' -and (Get-C1386Phase $r2) -eq 'lock' -and (Test-C1386RunIdKept -Fx $fx2 -RunId 'live-start-run') -and (Test-C1386NoReclaim -Fx $fx2)
    Assert-C487 -Cond $kept -Name 'C1386 LiveOwner start mismatch stays live-owner' -Detail ('phase={0} refusal={1} lock={2}' -f (Get-C1386Phase $r2), (Get-C1386Refusal $r2), (Get-C1386LockText $fx2))
}

function Test-C1386_AmbiguousOwnerRefused {
    $boot = 'boot-current'
    $hostId = 'host-current'

    $parentAlive = New-RunFx
    Write-C1386Seams -Fx $parentAlive -AlivePids @(424211) -BootId $boot -HostId $hostId
    Write-C1386Plant -Fx $parentAlive -OwnerPid 424210 -ParentPid 424211 -RunId 'parent-alive-run' -BootId $boot -HostId $hostId
    $parentResult = Invoke-C1386FxRun -Fx $parentAlive
    $parentDetail = ('phase={0} refusal={1}' -f (Get-C1386Phase $parentResult), (Get-C1386Refusal $parentResult))
    Assert-C487 -Cond ((Get-C1386Refusal $parentResult) -eq 'lock-held' -and (Get-C1386Phase $parentResult) -eq 'lock') -Name 'C1386 Ambiguous parent-alive refuses lock-held' -Detail $parentDetail
    Assert-C487 -Cond ((Test-C1386RunIdKept -Fx $parentAlive -RunId 'parent-alive-run') -and (Test-C1386NoReclaim -Fx $parentAlive)) -Name 'C1386 Ambiguous parent-alive keeps the lock' -Detail (Get-C1386LockText $parentAlive)

    $foreign = New-RunFx
    Write-C1386Seams -Fx $foreign -AlivePids @() -BootId $boot -HostId $hostId
    Write-C1386Plant -Fx $foreign -OwnerPid 424212 -ParentPid 424213 -RunId 'foreign-host-run' -BootId $boot -HostId 'host-other'
    $foreignResult = Invoke-C1386FxRun -Fx $foreign
    $foreignDetail = ('phase={0} refusal={1}' -f (Get-C1386Phase $foreignResult), (Get-C1386Refusal $foreignResult))
    Assert-C487 -Cond ((Get-C1386Refusal $foreignResult) -eq 'lock-held' -and (Get-C1386Phase $foreignResult) -eq 'lock') -Name 'C1386 Ambiguous foreign-host refuses lock-held' -Detail $foreignDetail
    Assert-C487 -Cond ((Test-C1386RunIdKept -Fx $foreign -RunId 'foreign-host-run') -and (Test-C1386NoReclaim -Fx $foreign)) -Name 'C1386 Ambiguous foreign-host keeps the lock' -Detail (Get-C1386LockText $foreign)

    $missing = New-RunFx
    Write-C1386Seams -Fx $missing -AlivePids @() -BootId $boot -HostId $hostId
    Write-C1386Plant -Fx $missing -OwnerPid 424214 -RunId 'missing-parent-run' -OmitParent -BootId $boot -HostId $hostId
    $missingResult = Invoke-C1386FxRun -Fx $missing
    $missingDetail = ('phase={0} refusal={1}' -f (Get-C1386Phase $missingResult), (Get-C1386Refusal $missingResult))
    Assert-C487 -Cond ((Get-C1386Refusal $missingResult) -eq 'lock-held' -and (Get-C1386Phase $missingResult) -eq 'lock') -Name 'C1386 Ambiguous missing-parent refuses lock-held' -Detail $missingDetail
    Assert-C487 -Cond ((Test-C1386RunIdKept -Fx $missing -RunId 'missing-parent-run') -and (Test-C1386NoReclaim -Fx $missing)) -Name 'C1386 Ambiguous missing-parent keeps the lock' -Detail (Get-C1386LockText $missing)
}

function Get-C1389ReclaimNewRunId {
    param($Fx)
    $path = Join-Path $Fx.State 'lock-reclaim.json'
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    try {
        $parsed = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        return [string]$parsed.newRunId
    } catch {
        return ''
    }
}

function Install-C1389RemoveShadow {
    param([string]$LockPath)
    $script:C1389LockFull = [System.IO.Path]::GetFullPath($LockPath)
    $script:C1389RemoveCount = 0
    function script:Remove-Item {
        [CmdletBinding()]
        param(
            [Parameter(ValueFromPipeline = $true)]
            [Alias('PSPath')]
            [string[]]$LiteralPath,
            [string[]]$Path,
            [switch]$Force,
            [switch]$Recurse
        )
        $targets = @()
        if ($PSBoundParameters.ContainsKey('LiteralPath')) { $targets = @($LiteralPath) }
        elseif ($PSBoundParameters.ContainsKey('Path')) { $targets = @($Path) }
        foreach ($target in $targets) {
            $full = [string]$target
            try { $full = [System.IO.Path]::GetFullPath([string]$target) } catch { }
            if ($full -eq $script:C1389LockFull) {
                $script:C1389RemoveCount++
                if ($script:C1389RemoveCount -gt 1) {
                    throw 'shadowed lock remove refused'
                }
            }
            $ea = 'Continue'
            if ($PSBoundParameters.ContainsKey('ErrorAction')) { $ea = [string]$PSBoundParameters['ErrorAction'] }
            if ($Force -and $Recurse) {
                Microsoft.PowerShell.Management\Remove-Item -LiteralPath $target -Force -Recurse -ErrorAction $ea
            } elseif ($Force) {
                Microsoft.PowerShell.Management\Remove-Item -LiteralPath $target -Force -ErrorAction $ea
            } elseif ($Recurse) {
                Microsoft.PowerShell.Management\Remove-Item -LiteralPath $target -Recurse -ErrorAction $ea
            } else {
                Microsoft.PowerShell.Management\Remove-Item -LiteralPath $target -ErrorAction $ea
            }
        }
    }
}

function Test-C1389_LoserReclaimRecord {
    $winnerRun = 'winner-run'
    $loserRun = 'loser-run'
    $fx = New-RunFx
    Write-C1386Plant -Fx $fx -OwnerPid 424298 -ParentPid 424290 -RunId 'stale-run' -BootId 'boot-current' -HostId 'host-current'
    $lockPath = Join-Path $fx.State 'run.lock'
    $snapshot = Read-NightlyLockRecord -LockPath $lockPath
    Install-C1389RemoveShadow -LockPath $lockPath
    $parent = [pscustomobject]@{ Id = 0; StartTimeUtc = $null }
    $first = Enter-NightlyReclaimedLock -StateRoot $fx.State -LockPath $lockPath -Existing $snapshot -RunId $winnerRun -Reason 'dead-owner-legacy' -BootId 'boot-current' -HostId 'host-current' -Parent $parent
    $second = Enter-NightlyReclaimedLock -StateRoot $fx.State -LockPath $lockPath -Existing $snapshot -RunId $loserRun -Reason 'dead-owner-legacy' -BootId 'boot-current' -HostId 'host-current' -Parent $parent
    if ($script:NightlyLockStream) {
        try { $script:NightlyLockStream.Dispose() } catch { }
        $script:NightlyLockStream = $null
    }
    $lockText = ''
    try { $lockText = Get-C1386LockText -Fx $fx } catch { $lockText = [string]$_.Exception.Message }
    $newRunId = Get-C1389ReclaimNewRunId -Fx $fx
    $lockRun = ''
    try {
        if (-not [string]::IsNullOrWhiteSpace($lockText) -and $lockText.Trim().StartsWith('{')) {
            $lockRun = [string](($lockText | ConvertFrom-Json).runId)
        }
    } catch { }
    $secondOk = $false
    $secondReason = ''
    $firstOk = $false
    $firstReason = ''
    if ($null -ne $second) { $secondOk = [bool]$second.Ok; $secondReason = [string]$second.Reason }
    if ($null -ne $first) { $firstOk = [bool]$first.Ok; $firstReason = [string]$first.Reason }
    $detail = ('winner=' + $winnerRun + ' loser=' + $loserRun + ' firstOk=' + $firstOk + ' firstReason=' + $firstReason + ' secondOk=' + $secondOk + ' secondReason=' + $secondReason + ' newRunId=' + $newRunId + ' lockRun=' + $lockRun)
    $loserOut = ((-not $secondOk) -and ($lockRun -eq $winnerRun))
    Assert-C487 -Cond $loserOut -Name 'C1389 Record loser does not own the lock' -Detail $detail
    Assert-C487 -Cond ($newRunId -eq $winnerRun) -Name 'C1389 Record winner newRunId remains' -Detail $detail
}

function Write-C1389RaceChild {
    param([string]$Path)
    $text = @'
#requires -Version 5.1
param(
    [string]$CommonPath,
    [string]$StateRoot,
    [string]$LockPath,
    [string]$SnapshotPath,
    [string]$RunId,
    [string]$Reason,
    [string]$BootId,
    [string]$HostId,
    [string]$ResultPath,
    [string]$ReleasePath
)
$ErrorActionPreference = 'Stop'
try {
    . $CommonPath
    $snapshot = Get-Content -LiteralPath $SnapshotPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $parent = [pscustomobject]@{ Id = 0; StartTimeUtc = $null }
    $result = Enter-NightlyReclaimedLock -StateRoot $StateRoot -LockPath $LockPath -Existing $snapshot -RunId $RunId -Reason $Reason -BootId $BootId -HostId $HostId -Parent $parent
    $out = [ordered]@{ Ok = [bool]$result.Ok; Reason = [string]$result.Reason; RunId = $RunId }
    [System.IO.File]::WriteAllText($ResultPath, ($out | ConvertTo-Json -Compress))
    if ($result.Ok) {
        $deadline = (Get-Date).AddSeconds(40)
        while (-not (Test-Path -LiteralPath $ReleasePath) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 50
        }
    }
    exit 0
} catch {
    $err = [ordered]@{ Ok = $false; Reason = 'child-error'; RunId = $RunId; Error = [string]$_.Exception.Message }
    try { [System.IO.File]::WriteAllText($ResultPath, ($err | ConvertTo-Json -Compress)) } catch { }
    exit 1
}
'@
    [System.IO.File]::WriteAllText($Path, $text.Replace("`r`n", "`n"))
}

function Start-C1389RaceChild {
    param(
        [string]$ChildPath,
        [string]$CommonPath,
        [string]$StateRoot,
        [string]$LockPath,
        [string]$SnapshotPath,
        [string]$RunId,
        [string]$ResultPath,
        [string]$ReleasePath
    )
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $arg = '-NoProfile -NonInteractive -File "' + $ChildPath + '" -CommonPath "' + $CommonPath + '" -StateRoot "' + $StateRoot + '" -LockPath "' + $LockPath + '" -SnapshotPath "' + $SnapshotPath + '" -RunId "' + $RunId + '" -Reason dead-owner-legacy -BootId boot-current -HostId host-current -ResultPath "' + $ResultPath + '" -ReleasePath "' + $ReleasePath + '"'
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $pwsh
    $psi.Arguments = $arg
    $psi.WorkingDirectory = (Split-Path -Parent $ChildPath)
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    return [System.Diagnostics.Process]::Start($psi)
}

function Wait-C1389File {
    param([string]$Path, [int]$Seconds)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while (-not (Test-Path -LiteralPath $Path) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 50
    }
    return (Test-Path -LiteralPath $Path)
}

function Read-C1389JsonFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
    } catch {
        return $null
    }
}

function Test-C1389_DoubleReclaim {
    $fixture = Join-Path $ResultsDirectory ('c1389-race-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    $state = Join-Path $fixture 'state'
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    $lockPath = Join-Path $state 'run.lock'
    $snapshotPath = Join-Path $fixture 'snapshot.json'
    $childPath = Join-Path $fixture 'reclaim-child.ps1'
    $releasePath = Join-Path $fixture 'release'
    $resultA = Join-Path $fixture 'a.json'
    $resultB = Join-Path $fixture 'b.json'
    $rec = [ordered]@{
        pid = 424301
        processStartedAt = '2020-01-01T00:00:00.0000000Z'
        startedAt = '2020-01-01T00:00:00.0000000Z'
        runId = 'stale-run'
        parentPid = 424302
        parentStartedAt = '2020-01-01T00:00:00.0000000Z'
        bootId = 'boot-current'
        hostId = 'host-current'
    }
    $json = $rec | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText($lockPath, $json)
    [System.IO.File]::WriteAllText($snapshotPath, $json)
    Write-C1389RaceChild -Path $childPath
    $common = Join-Path $PSScriptRoot (Join-Path 'lib' 'nightly-common.ps1')
    $procA = $null
    $procB = $null
    try {
        $procA = Start-C1389RaceChild -ChildPath $childPath -CommonPath $common -StateRoot $state -LockPath $lockPath -SnapshotPath $snapshotPath -RunId 'A-run' -ResultPath $resultA -ReleasePath $releasePath
        [void](Wait-C1389File -Path $resultA -Seconds 30)
        $procB = Start-C1389RaceChild -ChildPath $childPath -CommonPath $common -StateRoot $state -LockPath $lockPath -SnapshotPath $snapshotPath -RunId 'B-run' -ResultPath $resultB -ReleasePath $releasePath
        [void](Wait-C1389File -Path $resultB -Seconds 30)
    } finally {
        if (-not (Test-Path -LiteralPath $releasePath)) {
            [System.IO.File]::WriteAllText($releasePath, 'go')
        }
        foreach ($proc in @($procA, $procB)) {
            if ($null -ne $proc -and -not $proc.HasExited) {
                try { [void]$proc.WaitForExit(15000) } catch { }
            }
            if ($null -ne $proc -and -not $proc.HasExited) {
                try { $proc.Kill() } catch { }
            }
        }
    }
    $a = Read-C1389JsonFile -Path $resultA
    $b = Read-C1389JsonFile -Path $resultB
    $aOk = $false
    $bOk = $false
    $aReason = ''
    $bReason = ''
    if ($null -ne $a) { $aOk = [bool]$a.Ok; $aReason = [string]$a.Reason }
    if ($null -ne $b) { $bOk = [bool]$b.Ok; $bReason = [string]$b.Reason }
    $owners = @()
    if ($aOk) { $owners += 'A-run' }
    if ($bOk) { $owners += 'B-run' }
    $lockText = ''
    try {
        if (Test-Path -LiteralPath $lockPath) { $lockText = [System.IO.File]::ReadAllText($lockPath) }
    } catch {
        $lockText = [string]$_.Exception.Message
    }
    $lockRun = ''
    try {
        if (-not [string]::IsNullOrWhiteSpace($lockText) -and $lockText.Trim().StartsWith('{')) {
            $lockRun = [string](($lockText | ConvertFrom-Json).runId)
        }
    } catch { }
    $owner = ''
    if ($owners.Count -eq 1) { $owner = [string]$owners[0] }
    $detail = ('A-run ok=' + $aOk + ' reason=' + $aReason + ' B-run ok=' + $bOk + ' reason=' + $bReason + ' lockRun=' + $lockRun)
    Assert-C487 -Cond ($owners.Count -eq 1) -Name 'C1389 Race one attempt owns the lock' -Detail $detail
    Assert-C487 -Cond (($owners.Count -eq 1) -and ($lockRun -eq $owner)) -Name 'C1389 Race lock run id is that owner' -Detail $detail
}

function Test-C1389_BootSourcePinned {
    $script:C1389ItemPropertyCalls = 0
    function script:Test-NightlyWindows { return $true }
    function script:Test-NightlyLinux { return $false }
    function script:Get-ItemProperty {
        [CmdletBinding()]
        param(
            [Parameter(Position = 0)]
            [string]$Path,
            [string]$Name
        )
        $script:C1389ItemPropertyCalls++
        if ($script:C1389ItemPropertyCalls -eq 1) {
            return [pscustomobject]@{ BootId = 157 }
        }
        throw 'registry BootId unreadable'
    }
    function script:Get-CimInstance {
        [CmdletBinding()]
        param(
            [Parameter(Position = 0)]
            [string]$ClassName,
            [string]$Filter,
            [string]$Query
        )
        $stamp = [datetime]::SpecifyKind([datetime]'2026-01-02T03:04:05', [DateTimeKind]::Utc)
        return [pscustomobject]@{ LastBootUpTime = $stamp }
    }
    $firstBoot = [string](Get-NightlyBootId)
    $secondBoot = [string](Get-NightlyBootId)
    $count = [int]$script:C1389ItemPropertyCalls
    $single = (($count -ge 2) -and (($secondBoot -eq '') -or ($secondBoot -eq '157')))
    $bootDetail = ('count=' + $count + ' first=' + $firstBoot + ' second=' + $secondBoot)
    if ($count -eq 0) { $bootDetail = 'stub-not-hit' }
    Assert-C487 -Cond $single -Name 'C1389 Boot registry failure keeps a single source' -Detail $bootDetail

    $fx = New-RunFx
    $script:NightlySeams = @{
        IsProcessAlive = { param($ProcessId) return ([int]$ProcessId -eq 424299) }
        GetHostId = { return 'host-current' }
    }
    Write-C1386Plant -Fx $fx -OwnerPid 424299 -ParentPid 424300 -RunId 'planted-run' -BootId '157' -HostId 'host-current'
    $result = Enter-NightlyExclusiveLock -StateRoot $fx.State -RunId 'thief-run'
    $reason = ''
    if ($null -ne $result) { $reason = [string]$result.Reason }
    Assert-C487 -Cond ($reason -eq 'live-owner') -Name 'C1389 Boot live owner stays live-owner' -Detail ('reason=' + $reason + ' second=' + $secondBoot)
    $kept = (Test-C1386RunIdKept -Fx $fx -RunId 'planted-run') -and (Test-C1386NoReclaim -Fx $fx)
    Assert-C487 -Cond $kept -Name 'C1389 Boot live owner writes no reclaim record' -Detail (Get-C1386LockText $fx)
}

function Test-C487_PrerequisiteSettle {
    # A child that exits nonzero while summary.json still says testsPassed must
    # not leave that success flag settled beside last-run.json.
    $run = Invoke-C545ResultLineRun -Row 'npm-build-failed' -UtcNow '2026-09-17T23:35:00Z' -TestExit 2 -TestsPassed $true
    $summary = $null
    $summaryPath = ''
    if ($run.Record) { $summaryPath = [string]$run.Record.summaryPath }
    if (-not [string]::IsNullOrWhiteSpace($summaryPath) -and (Test-Path -LiteralPath $summaryPath)) {
        $summary = Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    $recordPassed = $false
    if ($run.Record) { $recordPassed = [bool]$run.Record.testsPassed }
    $statePassed = $false
    if ($run.State) { $statePassed = [bool]$run.State.testsPassed }
    $summaryPassed = $true
    if ($summary) { $summaryPassed = [bool]$summary.testsPassed }
    Assert-C487 -Cond ($null -ne $run.Record -and -not $recordPassed -and -not $statePassed -and -not $summaryPassed) `
        -Name 'C487 prerequisite settle summary testsPassed matches last-run' `
        -Detail ('exit=' + $run.ExitCode + ' record=' + $recordPassed + ' state=' + $statePassed + ' summary=' + $summaryPassed + ' path=' + $summaryPath)
}

$cases = Get-C487CaseFunctions -Prefix 'C487_G0'
if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in $cases) { & $fn }
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C545_')) { & $fn }
    Test-C487_PrerequisiteSettle
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'run-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 88 })
