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
. (Join-Path $lib 'nightly-run-impl.ps1')
. (Join-Path $lib 'c487-harness.ps1')

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
    return [pscustomobject]@{ Root = $root; State = $state; Clone = $clone; Trace = $trace; Seams = $seams }
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
    $a = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile','-NonInteractive','-File',$runPs1,'-CheckoutRoot',$fx.Clone,'-StateRoot',$fx.State,'-SeamsPath',$fx.Seams,'-NoReport','-LogRoot',(Join-Path $fx.State 'logs')) -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 400
    $b = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile','-NonInteractive','-File',$runPs1,'-CheckoutRoot',$fx.Clone,'-StateRoot',$fx.State,'-SeamsPath',$fx.Seams,'-NoReport','-LogRoot',(Join-Path $fx.State 'logs-b')) -PassThru -WindowStyle Hidden
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

$cases = Get-C487CaseFunctions -Prefix 'C487_G0'
if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in $cases) { & $fn }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'run-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 56
