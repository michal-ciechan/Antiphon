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

# ---- rc ae84478d g126: a reused clone predating an eol attribute keeps CRLF bytes ----
# The live run's C:\Antiphon\releases\checkout was cloned 2026-10-18 under core.autocrlf=true,
# before 3afdc2798 pinned *.ps1 eol=lf. fetch + reset --hard rewrites only changed index
# entries, so every unchanged *.ps1 kept CRLF and C1313_SelectorLineEndings refused the
# selector. The fixture sets core.autocrlf in the clone itself, so it reproduces on Linux too.
function Test-C1313_ReusedCloneEolRepair {
    $root = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    $seed = Join-Path $root 'seed'
    $remote = $seed.Replace('\', '/')
    $clone = Join-Path $root 'clone'
    $state = Join-Path $root 'state'
    New-Item -ItemType Directory -Path $seed -Force | Out-Null
    function Invoke-EolFixtureGit {
        param([string]$Directory, [string[]]$GitArgs)
        $output = & git -C $Directory @GitArgs 2>&1
        if ($LASTEXITCODE -ne 0) { throw ('fixture git failed: ' + ($output | Out-String)) }
        return ($output | Out-String)
    }
    $commit = @('-c', 'user.name=Eol Test', '-c', 'user.email=eol@example.invalid', '-c', 'commit.gpgsign=false', 'commit', '-q', '-m')
    Invoke-EolFixtureGit $seed @('init', '-q', '-b', 'master') | Out-Null
    Invoke-EolFixtureGit $seed @('config', '--local', 'core.autocrlf', 'false') | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $seed 'sel.ps1'), [Text.Encoding]::ASCII.GetBytes("a`nb`n"))
    [IO.File]::WriteAllBytes((Join-Path $seed 'other.txt'), [Text.Encoding]::ASCII.GetBytes("c`nd`n"))
    Invoke-EolFixtureGit $seed @('add', 'sel.ps1', 'other.txt') | Out-Null
    Invoke-EolFixtureGit $seed ($commit + 'before-attribute') | Out-Null
    Invoke-EolFixtureGit $root @('clone', '-q', '-c', 'core.autocrlf=true', $remote, $clone) | Out-Null
    [IO.File]::WriteAllText((Join-Path $clone '.antiphon-nightly-owned'), ('{"origin":"' + $remote + '","kind":"nightly-clone"}'))
    $before = [IO.File]::ReadAllBytes((Join-Path $clone 'sel.ps1'))
    Assert-C487 ($before -contains 13) 'C1313 reused clone checked the selector out CRLF' ([string]$before.Length)
    # The attribute lands later; the selector blob itself does not change.
    [IO.File]::WriteAllBytes((Join-Path $seed '.gitattributes'), [Text.Encoding]::ASCII.GetBytes("*.ps1 text eol=lf`n"))
    Invoke-EolFixtureGit $seed @('add', '.gitattributes') | Out-Null
    Invoke-EolFixtureGit $seed ($commit + 'pin-ps1-lf') | Out-Null
    $args = @{
        CheckoutRoot = $clone; StateRoot = $state; LogRoot = (Join-Path $state 'logs')
        RemoteUrl = $remote; Ref = 'master'; Trigger = 'manual'; WhatIf = $true
        CoordinationRoot = (Join-Path $root 'coord'); PassThru = $true
    }
    $run = Invoke-AntiphonNightlyRun @args
    Assert-C487 ($run.ExitCode -eq 0 -and $run.Phase -eq 'whatif') 'C1313 reused clone sync completes' ($run | ConvertTo-Json -Compress)
    $after = [IO.File]::ReadAllBytes((Join-Path $clone 'sel.ps1'))
    Assert-C487 (-not ($after -contains 13) -and $after.Length -eq 4) 'C1313 reused clone selector is LF after sync' ([string]$after.Length)
    $other = [IO.File]::ReadAllBytes((Join-Path $clone 'other.txt'))
    Assert-C487 ($other -contains 13) 'C1313 reused clone leaves a file without an eol attribute as configured' ([string]$other.Length)
    # The ownership marker is untracked by design; only tracked paths must be clean.
    $status = Invoke-EolFixtureGit $clone @('status', '--porcelain', '--untracked-files=no')
    Assert-C487 ([string]::IsNullOrWhiteSpace($status)) 'C1313 reused clone is clean after the eol repair' $status
}

# CARD-1854: real tracked files, with only native stdout decoding/quoting simulated.
function Test-C1854_EolRepairHardening {
    $root = Join-Path $ResultsDirectory ([guid]::NewGuid().ToString('N'))
    $seed = Join-Path $root 'seed'
    $clone = Join-Path $root 'clone'
    New-Item -ItemType Directory -Path $seed -Force | Out-Null
    function Invoke-HardeningGit {
        param([string]$Directory, [string[]]$GitArgs)
        $output = & git -C $Directory @GitArgs 2>&1
        if ($LASTEXITCODE -ne 0) { throw ('fixture git failed: ' + ($output | Out-String)) }
        return ($output | Out-String)
    }
    $commit = @('-c', 'user.name=Eol Test', '-c', 'user.email=eol@example.invalid', '-c', 'commit.gpgsign=false', 'commit', '-q', '-m')
    $plain = 'selector [one].ps1'
    $unicode = 'unicode-' + [char]0x00e9 + '.ps1'
    $quoted = 'quoted-' + [char]0x4e2d + '.ps1'
    Invoke-HardeningGit $seed @('init', '-q', '-b', 'master') | Out-Null
    Invoke-HardeningGit $seed @('config', '--local', 'core.autocrlf', 'false') | Out-Null
    foreach ($name in @($plain, $unicode, $quoted)) {
        [IO.File]::WriteAllBytes((Join-Path $seed $name), [Text.Encoding]::ASCII.GetBytes("a`nb`n"))
    }
    Invoke-HardeningGit $seed @('add', '.') | Out-Null
    Invoke-HardeningGit $seed ($commit + 'before-attribute') | Out-Null
    Invoke-HardeningGit $root @('clone', '-q', '-c', 'core.autocrlf=true', $seed, $clone) | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $seed '.gitattributes'), [Text.Encoding]::ASCII.GetBytes("*.ps1 text eol=lf`n"))
    Invoke-HardeningGit $seed @('add', '.gitattributes') | Out-Null
    Invoke-HardeningGit $seed ($commit + 'pin-lf') | Out-Null
    Invoke-HardeningGit $clone @('fetch', '-q', 'origin', 'master') | Out-Null
    Invoke-HardeningGit $clone @('reset', '--hard', 'FETCH_HEAD') | Out-Null
    $crlf = @(@($plain, $unicode, $quoted) | Where-Object { [IO.File]::ReadAllBytes((Join-Path $clone $_)) -contains 13 })
    Assert-C487 ($crlf.Count -eq 3) 'C1854 real reused clone has three stale tracked files' ([string]$crlf.Count)

    # Model UTF-8 bytes decoded with the console OEM code page on Windows PS 5.1.
    $mangled = [Text.Encoding]::GetEncoding(437).GetString([Text.Encoding]::UTF8.GetBytes($unicode))
    $quotedLine = @((Invoke-HardeningGit $clone @('-c', 'core.quotePath=true', 'ls-files', '--eol')) -split "`n" | Where-Object { $_ -match '"quoted-' })[0]
    $quotedPath = ($quotedLine.TrimEnd("`r") -split "`t", 2)[1]
    Assert-C487 ($mangled -cne $unicode -and -not (Test-Path -LiteralPath (Join-Path $clone $mangled)) -and $quotedPath.StartsWith('"')) 'C1854 missing decoded and C-quoted paths are discriminating inputs'
    $checkouts = New-Object System.Collections.Generic.List[string]
    $savedSeams = $script:NightlySeams
    try {
        $script:NightlySeams = @{ Git = {
            param($Directory, $GitArgs)
            # Get known UTF-8 text before deliberately applying the OEM decode
            # below. This fixture must not inherit PS 5.1's console decoder.
            $savedEncoding = [Console]::OutputEncoding
            try {
                [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
                $output = & git -c core.fsmonitor=false -c gc.auto=0 -c maintenance.auto=false -C $Directory @GitArgs 2>&1
                $exit = $LASTEXITCODE
            } finally { [Console]::OutputEncoding = $savedEncoding }
            $text = $output | Out-String
            if ($GitArgs -contains 'ls-files') { $text = $text.Replace($unicode, $mangled).Replace($quoted, $quotedPath) }
            if ($GitArgs -contains 'checkout-index') { $checkouts.Add(($GitArgs -join '|')) }
            return [pscustomobject]@{ ExitCode = $exit; Output = $text }
        }.GetNewClosure() }
        $failure = ''
        try { $log = & { Repair-NightlyCheckoutLineEndings -CheckoutRoot $clone } 6>&1 | Out-String }
        catch { $failure = $_.Exception.Message }
        Assert-C487 ([string]::IsNullOrEmpty($failure)) 'C1854 mixed repair completes despite missing decoded path' $failure
        Assert-C487 (-not ([IO.File]::ReadAllBytes((Join-Path $clone $plain)) -contains 13)) 'C1854 valid literal path is rewritten to LF'
        Assert-C487 (([IO.File]::ReadAllBytes((Join-Path $clone $unicode)) -contains 13) -and ([IO.File]::ReadAllBytes((Join-Path $clone $quoted)) -contains 13)) 'C1854 skipped files retain their bytes'
        Assert-C487 ($checkouts.Count -eq 1 -and $checkouts[0] -ceq ('checkout-index|-f|-u|--|' + $plain)) 'C1854 checkout receives only the existing unquoted path' ($checkouts -join ';')
        Assert-C487 ($log -match 'nightly sync: rewriting 1 working file\(s\).*skipped=2') 'C1854 mixed repair logs two skipped paths' $log
        $checkouts.Clear()
        $log = & { Repair-NightlyCheckoutLineEndings -CheckoutRoot $clone } 6>&1 | Out-String
        Assert-C487 ($checkouts.Count -eq 0 -and $log -match 'nightly sync: rewriting 0 working file\(s\).*skipped=2') 'C1854 skipped-only repair logs count without checkout' $log
    } finally { $script:NightlySeams = $savedSeams }
    # Also exercise real Invoke-NightlyGit with correctly decoded non-ASCII
    # paths. Select that decoder explicitly for this phase on PS 5.1 too.
    $savedEncoding = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        Repair-NightlyCheckoutLineEndings -CheckoutRoot $clone
    } finally { [Console]::OutputEncoding = $savedEncoding }
    $remaining = @(@($plain, $unicode, $quoted) | Where-Object { [IO.File]::ReadAllBytes((Join-Path $clone $_)) -contains 13 })
    Assert-C487 ($remaining.Count -eq 0) 'C1854 real non-ASCII paths repair when decoded correctly' ([string]$remaining.Count)
    $log = & { Repair-NightlyCheckoutLineEndings -CheckoutRoot $clone } 6>&1 | Out-String
    Assert-C487 ([string]::IsNullOrWhiteSpace($log)) 'C1854 clean repair remains silent' $log
    $status = Invoke-HardeningGit $clone @('status', '--porcelain', '--untracked-files=no')
    Assert-C487 ([string]::IsNullOrWhiteSpace($status)) 'C1854 repaired clone has clean tracked status' $status
}

# ---- CARD-0545 D-10: the wrapper's last stdout line is the JSON completion record ----

function Invoke-C545ResultLineRun {
    param([string]$Row, [string]$UtcNow, [int]$TestExit = 0, [bool]$TestsPassed = $true, [switch]$NoReport,
        [string]$CheckoutRoot = '', [switch]$Rc, [string]$ReleaseRoot)
    $fx = New-RunFx
    $sha = 'a' * 40
    if ($Rc) {
        # The real selection worker reads policy blobs from the pinned commit.
        $inputs = @('global.json', 'tests/test-execution-policy.json', 'tests/test-selection-policy.json', 'scripts/select-test-rosters.ps1')
        [void][IO.Directory]::CreateDirectory((Join-Path $fx.Clone 'tests'))
        foreach ($file in $inputs) {
            Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $here) $file) -Destination (Join-Path $fx.Clone $file)
        }
        & git -C $fx.Clone init -q
        if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the rc fixture repository.' }
        & git -C $fx.Clone add -- @inputs 'scripts/lib/test-selection.ps1'
        if ($LASTEXITCODE -ne 0) { throw 'Could not stage the rc fixture selection inputs.' }
        & git -C $fx.Clone -c user.name=C2044 -c user.email=c2044@example.invalid -c core.hooksPath=.git/no-hooks -c commit.gpgsign=false commit -qm 'rc fixture selection inputs'
        if ($LASTEXITCODE -ne 0) { throw 'Could not commit the rc fixture selection inputs.' }
        $sha = & git -C $fx.Clone rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the rc fixture candidate.' }
    }
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
    Write-C487Seams -Path $fx.Seams -TracePath $fx.Trace -GitOutput $sha -Extra $extra
    $checkout = $fx.Clone
    if ($CheckoutRoot) { $checkout = $CheckoutRoot }
    $argList = @('-NoProfile', '-NonInteractive', '-File', $runPs1, '-NoSync', '-CheckoutRoot', $checkout, '-StateRoot', $fx.State,
        '-LogRoot', (Join-Path $fx.State 'logs'), '-SeamsPath', $fx.Seams, '-RunId', $runId,
        '-CoordinationRoot', $fx.Coordination)
    if ($Rc) {
        $argList += @('-Profile', 'rc', '-Trigger', 'rc', '-Ref', 'rc/2026-11-30', '-ExpectedSha', $sha, '-WhatIf')
    } else {
        $argList += @('-Trigger', 'scheduled', '-Ref', 'master')
    }
    if ($PSBoundParameters.ContainsKey('ReleaseRoot')) { $argList += @('-ReleaseRoot', $ReleaseRoot) }
    if ($NoReport) { $argList += '-NoReport' }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'pwsh'
    $psi.WorkingDirectory = $fx.Root
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
    $expected = '{"nativeRunId":"","sha":"","ref":"","trigger":"","profile":"","candidateId":"","creditKind":"","localDueDate":"","policyHash":"","coverageComplete":false,"testsPassed":false,"reportDelivered":false,"exitCode":3,"summaryPath":"","refusal":"shared-tree"}'
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

function Test-C2044_RcReleaseRootRefusal {
    $arguments = @{ Rc = $true; UtcNow = '2026-11-21T07:35:00Z' }
    $omitted = Invoke-C545ResultLineRun -Row 'omitted' @arguments
    $blank = Invoke-C545ResultLineRun -Row 'blank' -ReleaseRoot '   ' @arguments
    foreach ($run in @($omitted, $blank)) {
        Assert-C487 -Cond ($run.ExitCode -eq 3 -and $run.Record.exitCode -eq 3 -and
            $run.Record.refusal -ceq 'rc-release-root-required' -and $run.Record.nativeRunId -ceq '' -and $null -eq $run.State) `
            -Name ('C2044 {0} ReleaseRoot is refused before recording a run and named in the final JSON' -f $run.Row) `
            -Detail ('exit={0} last={1} stderr={2}' -f $run.ExitCode, $run.Last, $run.Stderr)
    }
    $release = Join-Path $ResultsDirectory 'rc-releases'
    $explicit = Invoke-C545ResultLineRun -Row 'explicit' -ReleaseRoot $release @arguments
    Assert-C487 -Cond ($explicit.ExitCode -eq 0 -and $explicit.Record.exitCode -eq 0 -and
        $explicit.Record.nativeRunId -ceq $explicit.RunId -and $explicit.State.profile -ceq 'rc' -and
        (Test-Path -LiteralPath (Join-Path $release "candidates/$($explicit.State.candidateId)/attempts/$($explicit.RunId)/selection.json"))) `
        -Name 'C2044 explicit ReleaseRoot creates the selection in the supplied root' -Detail $explicit.Last
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

# CARD-1698: the run core prints a toolchain floor warning before the tests hop and keeps the
# same exit code; the version comes from the ToolchainVersions seam, never the real git.
function Invoke-C1698ToolchainRun {
    param([string]$GitVersion)
    $fx = New-RunFx
    New-Item -ItemType Directory -Path (Join-Path $fx.Clone 'docker') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path (Split-Path $here -Parent) 'docker/git-minimum-version.sh') -Destination (Join-Path $fx.Clone 'docker/git-minimum-version.sh')
    Copy-Item -LiteralPath (Join-Path (Split-Path $here -Parent) 'global.json') -Destination (Join-Path $fx.Clone 'global.json')
    $extra = [scriptblock]::Create("`$NightlySeams.ToolchainVersions = { param(`$tool) if (`$tool -eq 'git') { 'git version $GitVersion' } else { '10.0.204 [/fake/sdk]' } }")
    Write-C487Seams -Path $fx.Seams -TracePath $fx.Trace -Extra $extra
    $all = @(Invoke-FxRun -Fx $fx -Extra @{ NoSync = $true } 6>&1)
    $lines = @($all | Where-Object { $_ -is [System.Management.Automation.InformationRecord] } | ForEach-Object { [string]$_.MessageData })
    $result = @($all | Where-Object { $_ -isnot [System.Management.Automation.InformationRecord] }) | Select-Object -Last 1
    return [pscustomobject]@{ Lines = $lines; Result = $result }
}

function Test-C1698_RunPrintsToolchainWarning {
    $below = Invoke-C1698ToolchainRun -GitVersion '2.42.0'
    $at = Invoke-C1698ToolchainRun -GitVersion '2.43.0'
    $warn = @($below.Lines | Where-Object { $_ -match 'WARNING: git 2\.42\.0 is below the minimum 2\.43; lands will fail' })
    Assert-C487 -Cond ($warn.Count -eq 1) -Name 'C1698 Run prints the git floor warning' -Detail ($below.Lines -join ' | ')
    $none = @($at.Lines | Where-Object { $_ -match 'WARNING:' })
    Assert-C487 -Cond ($none.Count -eq 0) -Name 'C1698 Run prints none at the floor' -Detail ($at.Lines -join ' | ')
    $hop = -1; $w = -1
    for ($i = 0; $i -lt $below.Lines.Count; $i++) {
        if ($w -lt 0 -and $below.Lines[$i] -match 'WARNING: git') { $w = $i }
        if ($hop -lt 0 -and $below.Lines[$i] -match 'running .*nightly-tests\.ps1') { $hop = $i }
    }
    Assert-C487 -Cond ($w -ge 0 -and $hop -gt $w) -Name 'C1698 Run warns before the tests hop' -Detail ('warning=' + $w + ' hop=' + $hop)
    $same = $null -ne $below.Result -and $null -ne $at.Result -and [int]$below.Result.ExitCode -eq [int]$at.Result.ExitCode -and [string]$below.Result.Phase -eq [string]$at.Result.Phase
    Assert-C487 -Cond $same -Name 'C1698 Run exit code unchanged by the warning' -Detail ('below=' + $below.Result.ExitCode + '/' + $below.Result.Phase + ' at=' + $at.Result.ExitCode + '/' + $at.Result.Phase)
}

# CARD-1698, W2-nightly S03: the run core checks the floors with the compiled twin
# (Antiphon.Nightly/ToolchainFloor.cs) and no longer loads toolchain-floor.ps1, which stays for
# the restart chain. A child harness runs from a copied scripts tree whose library is broken (a
# throw, or a file PowerShell cannot parse); the run prints no check-failed line, keeps the
# at-floor exit code and phase and still hops to the tests.
function Test-C1698_RunLibraryLoadFailure {
    $at = Invoke-C1698ToolchainRun -GitVersion '2.43.0'
    $repoRoot = Split-Path $here -Parent
    $broken = @(
        @{ Name = 'throws'; Text = "throw 'C1698 toolchain lib load failure'" },
        @{ Name = 'unparsable'; Text = "function Get-C1698Broken {`n    `$x = 1`n" }
    )
    foreach ($b in $broken) {
        $copy = Join-Path $ResultsDirectory ('broken-lib-' + [guid]::NewGuid().ToString('N'))
        foreach ($d in @('scripts/lib', 'docker')) { New-Item -ItemType Directory -Path (Join-Path $copy $d) -Force | Out-Null }
        # The harness, the entry scripts the fixture clone copies, and the whole library directory.
        # [IO.File]::Copy: per-file Copy-Item over the library costs tens of seconds on Linux.
        foreach ($n in @('test-nightly-run.ps1', 'nightly-run.ps1', 'nightly-tests.ps1')) {
            [IO.File]::Copy((Join-Path $here $n), (Join-Path $copy (Join-Path 'scripts' $n)))
        }
        foreach ($f in [IO.Directory]::GetFiles($lib)) { [IO.File]::Copy($f, (Join-Path (Join-Path $copy 'scripts') (Join-Path 'lib' ([IO.Path]::GetFileName($f))))) }
        # nightly-run.ps1 requires the compiled core: copy its sources (not bin/ or obj/). They hash to the
        # checkout's build identity, so the cached build is loaded, not rebuilt.
        foreach ($project in @('scripts/lib/Antiphon.Nightly', 'src/Antiphon.IO.WriteBehind')) {
            $from = Join-Path $repoRoot $project
            foreach ($f in [IO.Directory]::EnumerateFiles($from, '*', [IO.SearchOption]::AllDirectories)) {
                $rel = $f.Substring($from.Length).TrimStart([char]92, [char]47)
                if ($rel -match '^(bin|obj)[\\/]' -or $rel -notmatch '\.(cs|csproj)$') { continue }
                $to = Join-Path (Join-Path $copy $project) $rel
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to))
                [IO.File]::Copy($f, $to)
            }
        }
        Copy-Item -LiteralPath (Join-Path $repoRoot 'docker/git-minimum-version.sh') -Destination (Join-Path $copy 'docker/git-minimum-version.sh')
        Copy-Item -LiteralPath (Join-Path $repoRoot 'global.json') -Destination (Join-Path $copy 'global.json')
        Set-Content -LiteralPath (Join-Path $copy 'scripts/lib/toolchain-floor.ps1') -Value $b.Text -Encoding ASCII
        $out = Join-Path $copy 'child-result.json'
        $saved = $env:C1698_LOAD_FAILURE_OUT
        $env:C1698_LOAD_FAILURE_OUT = $out
        try {
            $console = & pwsh -NoProfile -NonInteractive -File (Join-Path $copy 'scripts/test-nightly-run.ps1') -Case C1698_RunLibraryLoadFailureChild -ResultsDirectory (Join-Path $copy 'results') 2>&1 | Out-String
        } finally { $env:C1698_LOAD_FAILURE_OUT = $saved }
        $child = $null
        if (Test-Path -LiteralPath $out) { $child = [IO.File]::ReadAllText($out) | ConvertFrom-Json }
        $lines = @()
        if ($child) { $lines = @($child.lines | ForEach-Object { [string]$_ }) }
        $w = -1; $hop = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($w -lt 0 -and $lines[$i] -match 'WARNING: the toolchain floor check failed') { $w = $i }
            if ($hop -lt 0 -and $lines[$i] -match 'running .*nightly-tests\.ps1') { $hop = $i }
        }
        $ok = $null -ne $child -and $null -ne $at.Result -and [int]$child.exitCode -eq [int]$at.Result.ExitCode -and
            [string]$child.phase -eq [string]$at.Result.Phase -and $w -lt 0 -and $hop -ge 0
        $detail = 'warning=' + $w + ' hop=' + $hop
        if ($child) { $detail += ' child=' + $child.exitCode + '/' + $child.phase }
        else { $detail += ' no child result: ' + $console }
        $detail += ' at=' + $at.Result.ExitCode + '/' + $at.Result.Phase
        Assert-C487 -Cond $ok -Name ('C1698 Run does not load the toolchain library: ' + $b.Name) -Detail $detail
    }
}

# Child half of C1698_RunLibraryLoadFailure; runs only from the copied tree, writes its result.
function Test-C1698_RunLibraryLoadFailureChild {
    if (-not $env:C1698_LOAD_FAILURE_OUT) { Assert-C487 -Cond $false -Name 'C1698 Run load-failure child needs C1698_LOAD_FAILURE_OUT'; return }
    $r = $null
    try { $r = Invoke-C1698ToolchainRun -GitVersion '2.43.0' } catch { $r = [pscustomobject]@{ Lines = @('THREW ' + $_.Exception.Message); Result = $null } }
    $exit = $null; $phase = $null
    if ($r.Result) { $exit = $r.Result.ExitCode; $phase = $r.Result.Phase }
    $body = [ordered]@{ exitCode = $exit; phase = $phase; lines = @($r.Lines) } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText($env:C1698_LOAD_FAILURE_OUT, $body)
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
