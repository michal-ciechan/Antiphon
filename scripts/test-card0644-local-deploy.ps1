#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0644 V-6: local AppHost deployment is admitted by exact commit, and the
    shared-stack state (locks, wrapper PID, dashboard URL, wrapper log) lives in
    the main worktree whichever checkout the entry point runs from.

.DESCRIPTION
    Every scenario builds a scratch bare origin + main clone + linked worktree
    (scripts/lib/card0644-apphost-fixture.ps1) carrying copies of the real
    restart-apphost.ps1, deploy-local.ps1, dev-aspire.ps1 and apphost-common.ps1,
    and runs those real entry points with inert seams that record teardown,
    launch and deploy effects. Nothing touches live ports, processes, Docker,
    scheduled tasks or this checkout's logs/.

    -Phase changes verdict accounting only, never the inputs:
      Green       every named scenario must pass (plus the CARD-0273 guard and
                  CARD-0495 server-version harnesses it runs); exit 0.
      RedWitness  pre-fix run: every designated scenario must fail on its
                  behavioural assertion and MainMatchesOrigin must pass; exits 1
                  when that witness holds, 2 when it does not.
    ASCII-only for pwsh 7 and Windows PowerShell 5.1.
#>
param(
    [ValidateSet('RedWitness', 'Green')][string]$Phase = 'Green',
    [string]$Scenario = ''
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'card0644-apphost-fixture.ps1')

$script:OtherSha = 'a' * 40
$script:Checks = $null
$script:Results = [ordered]@{}

# Scenarios the old blanket guard / per-root state must fail. MainMatchesOrigin is the
# one pre-existing behaviour that already held.
$script:RedDesignated = @(
    'LinkedMatchesOrigin', 'MismatchNoEffects', 'MissingOriginNoEffects', 'BadExpectedNoEffects',
    'ExplicitExpectedMatches', 'AllowWorktreeOverride', 'OverrideCannotBypassExpected', 'ParentPassesFrozenSha',
    'CrossRootRestartLock', 'CrossRootLaunchLock', 'ParentChildHandoff', 'VersionMismatchKeepsLock',
    'HeadMovementKeepsLock', 'HealthyWrongShaIsNotSuccess', 'TrackedEditsWarn'
)

function Check {
    param([bool]$Condition, [string]$Label, $Detail = '')
    if (-not $Condition) {
        $text = [string]$Detail
        if ($text.Length -gt 600) { $text = $text.Substring(0, 600) + '...' }
        $script:Checks.Add(('{0} :: {1}' -f $Label, $text.Replace("`n", ' | ')))
    }
}

function Get-StatePath {
    param([string]$Root, [string]$Leaf)
    return Join-Path (Join-Path $Root 'logs') $Leaf
}

function Get-LastVerdict {
    param($Result)
    $lines = @($Result.Text -split "`n" | Where-Object { $_ -match 'DEPLOY VERDICT:' })
    if ($lines.Count -eq 0) { return '' }
    return ([string]$lines[-1]).Trim()
}

function Assert-NoEffects {
    param($Result, [string]$Label)
    Check ($Result.ExitCode -eq 3) "$Label exit 3" ("exit=$($Result.ExitCode); $($Result.Text)")
    Check ((Get-C644EffectCount $Result) -eq 0) "$Label zero teardown/launch/deploy effects" (($Result.Trace | ForEach-Object { $_.kind }) -join ',')
}

function Assert-NoLocks {
    param($Fixture, [string]$Label)
    foreach ($root in $Fixture.Main, $Fixture.Linked) {
        foreach ($leaf in 'apphost.restart.lock', 'apphost.launch.lock') {
            Check (-not (Test-Path -LiteralPath (Get-StatePath $root $leaf))) "$Label leaves no $leaf" (Get-StatePath $root $leaf)
        }
    }
}

function Assert-DevBodyInMain {
    param($Result, $Fixture, [string]$Label)
    $bodies = @($Result.Trace | Where-Object { $_.kind -eq 'dev-body' })
    Check ($bodies.Count -eq 1) "$Label dev-aspire launch body ran once" ("dev-body=$($bodies.Count); $($Result.Text) $($Result.ChildText)")
    if ($bodies.Count -ge 1) {
        $expected = Join-Path $Fixture.MainGit 'logs'
        Check (Test-C644SamePath $bodies[0].lockDir $expected) "$Label launch lock lives under the main worktree" ("lockDir=$($bodies[0].lockDir); expected=$expected")
        Check ([bool]$bodies[0].lockHeld) "$Label launch lock held while launching" ($bodies[0] | ConvertTo-Json -Compress)
    }
}

function Invoke-Scenario {
    param([string]$Name, [scriptblock]$Body)
    if ($Scenario -and $Scenario -ne $Name) { return }
    $script:Checks = New-Object System.Collections.Generic.List[string]
    $fx = $null
    $started = Get-Date
    try {
        $fx = New-C644Fixture
        $fx | Add-Member -NotePropertyName MainGit -NotePropertyValue (Get-C644GitPath $fx.Main)
        $fx | Add-Member -NotePropertyName LinkedGit -NotePropertyValue (Get-C644GitPath $fx.Linked)
        & $Body $fx
    } catch {
        $script:Checks.Add(('harness exception :: {0}' -f $_.Exception.Message))
    } finally {
        Remove-C644Fixture $fx
    }
    $elapsed = [int]((Get-Date) - $started).TotalMilliseconds
    $failures = @($script:Checks)
    $script:Results[$Name] = $failures
    if ($failures.Count -eq 0) { Write-Host ('PASS {0} ({1} ms)' -f $Name, $elapsed) }
    else {
        Write-Host ('FAIL {0} ({1} ms)' -f $Name, $elapsed)
        foreach ($f in $failures) { Write-Host ('    - ' + $f) }
    }
}

Invoke-Scenario 'LinkedMatchesOrigin' {
    param($fx)
    $r = Invoke-C644Entry $fx restart $fx.Linked @('-NoBuild', '-TimeoutSec', '5')
    Check ($r.ExitCode -eq 0) 'restart from matching linked root exits 0' ("exit=$($r.ExitCode); $($r.Text)")
    Check ($r.Text.Contains($fx.OriginSha)) 'restart names the admitted SHA' $r.Text
    Assert-DevBodyInMain $r $fx 'restart'
    $d = Invoke-C644Entry $fx dev $fx.Linked @('-NoBuild', '-NoBrowser')
    Check ($d.ExitCode -eq 0) 'direct dev-aspire from matching linked root exits 0' ("exit=$($d.ExitCode); $($d.Text)")
    Assert-DevBodyInMain $d $fx 'dev'
    $p = Invoke-C644Entry $fx deploy $fx.Linked @('-NoBuild', '-TimeoutSec', '5')
    Check ($p.ExitCode -eq 0 -and (Get-LastVerdict $p) -eq 'DEPLOY VERDICT: ok') 'deploy from matching linked root is ok' ("exit=$($p.ExitCode); $($p.Text)")
    Check ((Get-C644Count $p 'launch') -eq 1) 'deploy restarted the stack once' (($p.Trace | ForEach-Object { $_.kind }) -join ',')
    Assert-NoLocks $fx 'linked entries'
}

Invoke-Scenario 'MainMatchesOrigin' {
    param($fx)
    $r = Invoke-C644Entry $fx restart $fx.Main @('-NoBuild', '-TimeoutSec', '5')
    Check ($r.ExitCode -eq 0) 'restart from matching main root exits 0' ("exit=$($r.ExitCode); $($r.Text)")
    Check ($r.Text.Contains($fx.OriginSha)) 'restart names the admitted SHA' $r.Text
    Check ((Get-C644Count $r 'launch') -eq 1) 'restart launched once' (($r.Trace | ForEach-Object { $_.kind }) -join ',')
    $d = Invoke-C644Entry $fx dev $fx.Main @('-NoBuild', '-NoBrowser')
    Check ($d.ExitCode -eq 0 -and (Get-C644Count $d 'dev-body') -eq 1) 'direct dev-aspire from matching main root launches' ("exit=$($d.ExitCode); $($d.Text)")
    Assert-NoLocks $fx 'main entries'
}

Invoke-Scenario 'MismatchNoEffects' {
    param($fx)
    $mainHead = New-C644Commit $fx.Main 'local-only main commit'
    foreach ($entry in 'restart', 'deploy', 'dev') {
        $args1 = if ($entry -eq 'dev') { @('-NoBuild', '-NoBrowser') } else { @('-NoBuild', '-TimeoutSec', '5') }
        $r = Invoke-C644Entry $fx $entry $fx.Main $args1
        Assert-NoEffects $r "stale main $entry"
        Check ($r.Text.Contains($fx.OriginSha) -and $r.Text.Contains($mainHead)) "stale main $entry names expected and actual SHA" $r.Text
        Check ($r.Text.Contains($fx.MainGit)) "stale main $entry names the source root" $r.Text
        if ($entry -eq 'deploy') { Check ((Get-LastVerdict $r) -like 'DEPLOY VERDICT: refused*') 'stale main deploy verdict is refused' $r.Text }
    }
    $linkedHead = New-C644Commit $fx.Linked 'unlanded linked commit'
    $l = Invoke-C644Entry $fx restart $fx.Linked @('-NoBuild', '-TimeoutSec', '5')
    Assert-NoEffects $l 'unlanded linked restart'
    Check ($l.Text.Contains($fx.OriginSha) -and $l.Text.Contains($linkedHead)) 'unlanded linked restart names expected and actual SHA' $l.Text
    Assert-NoLocks $fx 'mismatch'
}

Invoke-Scenario 'MissingOriginNoEffects' {
    param($fx)
    Invoke-C644Git $fx.Main update-ref -d refs/remotes/origin/master | Out-Null
    foreach ($entry in 'restart', 'deploy', 'dev') {
        $args1 = if ($entry -eq 'dev') { @('-NoBuild', '-NoBrowser') } else { @('-NoBuild', '-TimeoutSec', '5') }
        $r = Invoke-C644Entry $fx $entry $fx.Main $args1
        Assert-NoEffects $r "missing origin $entry"
        Check ($r.Text -match 'origin/master') "missing origin $entry names the expected ref" $r.Text
    }
    Assert-NoLocks $fx 'missing origin'
}

Invoke-Scenario 'BadExpectedNoEffects' {
    param($fx)
    foreach ($bad in 'abc123', ('a' * 39)) {
        foreach ($entry in 'restart', 'deploy', 'dev') {
            $args1 = if ($entry -eq 'dev') { @('-NoBuild', '-NoBrowser', '-ExpectedSha', $bad) } else { @('-NoBuild', '-TimeoutSec', '5', '-ExpectedSha', $bad) }
            $r = Invoke-C644Entry $fx $entry $fx.Main $args1
            Assert-NoEffects $r "bad expected '$bad' $entry"
            Check ($r.Text.Contains($bad)) "bad expected '$bad' $entry names the supplied value" $r.Text
        }
    }
    Assert-NoLocks $fx 'bad expected'
}

Invoke-Scenario 'ExplicitExpectedMatches' {
    param($fx)
    $linkedHead = New-C644Commit $fx.Linked 'release candidate'
    Set-C644Config $fx @{ versionSha = $linkedHead }
    $r = Invoke-C644Entry $fx restart $fx.Linked @('-NoBuild', '-TimeoutSec', '5', '-ExpectedSha', $linkedHead)
    Check ($r.ExitCode -eq 0) 'explicit matching SHA restart exits 0 though HEAD != origin/master' ("exit=$($r.ExitCode); $($r.Text)")
    Check ($r.Text.Contains($linkedHead)) 'restart names the explicit SHA' $r.Text
    Assert-DevBodyInMain $r $fx 'explicit restart'
    $legacy = Invoke-C644Entry $fx restart $fx.Linked @('-NoBuild', '-TimeoutSec', '5', '-ExpectedServerSha', $linkedHead)
    Check ($legacy.ExitCode -eq 0) '-ExpectedServerSha remains an alias' ("exit=$($legacy.ExitCode); $($legacy.Text)")
    $p = Invoke-C644Entry $fx deploy $fx.Linked @('-NoBuild', '-TimeoutSec', '5', '-ExpectedSha', $linkedHead)
    Check ($p.ExitCode -eq 0 -and (Get-LastVerdict $p) -eq 'DEPLOY VERDICT: ok') 'explicit matching SHA deploy is ok' ("exit=$($p.ExitCode); $($p.Text)")
    Assert-NoLocks $fx 'explicit'
}

Invoke-Scenario 'AllowWorktreeOverride' {
    param($fx)
    $linkedHead = New-C644Commit $fx.Linked 'unlanded but deliberately deployed'
    Set-C644Config $fx @{ versionSha = $linkedHead }
    $r = Invoke-C644Entry $fx restart $fx.Linked @('-NoBuild', '-TimeoutSec', '5', '-AllowWorktree')
    Check ($r.ExitCode -eq 0) '-AllowWorktree admits the linked root at its HEAD' ("exit=$($r.ExitCode); $($r.Text)")
    Check ($r.Text -match 'AllowWorktree' -and $r.Text.Contains($linkedHead) -and $r.Text.Contains($fx.OriginSha)) 'override names itself, the admitted HEAD and origin/master' $r.Text
    Assert-DevBodyInMain $r $fx 'override restart'
    $launch = @($r.Trace | Where-Object { $_.kind -eq 'launch' } | Select-Object -First 1)
    Check ($launch.Count -eq 1 -and $launch[0].args.Contains($linkedHead)) 'override freezes the admitted HEAD for the child' ($launch | ConvertTo-Json -Compress)
    Assert-NoLocks $fx 'override'
}

Invoke-Scenario 'OverrideCannotBypassExpected' {
    param($fx)
    New-C644Commit $fx.Linked 'unlanded' | Out-Null
    foreach ($entry in 'restart', 'deploy', 'dev') {
        $args1 = if ($entry -eq 'dev') { @('-NoBuild', '-NoBrowser', '-AllowWorktree', '-ExpectedSha', $script:OtherSha) } else { @('-NoBuild', '-TimeoutSec', '5', '-AllowWorktree', '-ExpectedSha', $script:OtherSha) }
        $r = Invoke-C644Entry $fx $entry $fx.Linked $args1
        Assert-NoEffects $r "override+mismatched expected $entry"
        Check ($r.Text.Contains($script:OtherSha)) "override+mismatched expected $entry names the expected SHA" $r.Text
    }
    Assert-NoLocks $fx 'override mismatch'
}

Invoke-Scenario 'ParentPassesFrozenSha' {
    param($fx)
    $p = Invoke-C644Entry $fx deploy $fx.Linked @('-NoBuild', '-TimeoutSec', '5')
    Check ($p.ExitCode -eq 0 -and (Get-LastVerdict $p) -eq 'DEPLOY VERDICT: ok') 'deploy ok' ("exit=$($p.ExitCode); $($p.Text)")
    Check ($p.Text -match ('equals -ExpectedSha ' + $fx.OriginSha)) 'restart child was admitted by the frozen -ExpectedSha from deploy' $p.Text
    $launch = @($p.Trace | Where-Object { $_.kind -eq 'launch' } | Select-Object -First 1)
    Check ($launch.Count -eq 1 -and $launch[0].args -match ('-ExpectedSha ' + $fx.OriginSha)) 'restart passes the frozen SHA to dev-aspire' ($launch | ConvertTo-Json -Compress)
    $body = @($p.Trace | Where-Object { $_.kind -eq 'dev-body' } | Select-Object -First 1)
    Check ($body.Count -eq 1 -and $body[0].args -match ('ExpectedSha=' + $fx.OriginSha)) 'dev-aspire received the frozen SHA' ($body | ConvertTo-Json -Compress)
    Assert-NoLocks $fx 'frozen sha'
}

Invoke-Scenario 'CrossRootRestartLock' {
    param($fx)
    $lock = Get-StatePath $fx.Main 'apphost.restart.lock'
    Set-C644Lock $lock $PID
    $before = Get-Content -LiteralPath $lock -Raw
    foreach ($extra in @(@(), @('-AllowWorktree'))) {
        $r = Invoke-C644Entry $fx restart $fx.Linked (@('-NoBuild', '-TimeoutSec', '5') + $extra)
        Assert-NoEffects $r ("linked restart {0} under main restart lock" -f ($extra -join ' '))
        Check ($r.Text -match 'another restart is in flight') 'refusal names the in-flight restart' $r.Text
    }
    Check ((Get-Content -LiteralPath $lock -Raw) -eq $before) 'main restart lock untouched' $lock
    Check (-not (Test-Path -LiteralPath (Get-StatePath $fx.Linked 'apphost.restart.lock'))) 'no per-linked restart lock' ''
}

Invoke-Scenario 'CrossRootLaunchLock' {
    param($fx)
    $lock = Get-StatePath $fx.Main 'apphost.launch.lock'
    Set-C644Lock $lock $PID
    $before = Get-Content -LiteralPath $lock -Raw
    foreach ($extra in @(@(), @('-AllowWorktree'))) {
        $d = Invoke-C644Entry $fx dev $fx.Linked (@('-NoBuild', '-NoBrowser') + $extra)
        Assert-NoEffects $d ("linked dev-aspire {0} under main launch lock" -f ($extra -join ' '))
        $r = Invoke-C644Entry $fx restart $fx.Linked (@('-NoBuild', '-TimeoutSec', '5') + $extra)
        Assert-NoEffects $r ("linked restart {0} under main launch lock" -f ($extra -join ' '))
        Check ($r.Text -match 'launch is in flight') 'restart refusal names the in-flight launch' $r.Text
    }
    Check ((Test-Path -LiteralPath $lock) -and (Get-Content -LiteralPath $lock -Raw) -eq $before) 'main launch lock untouched' $lock
    Check (-not (Test-Path -LiteralPath (Get-StatePath $fx.Linked 'apphost.launch.lock'))) 'no per-linked launch lock' ''
}

Invoke-Scenario 'ParentChildHandoff' {
    param($fx)
    $r = Invoke-C644Entry $fx restart $fx.Linked @('-NoBuild', '-TimeoutSec', '5')
    Check ($r.ExitCode -eq 0) 'restart exits 0 after handing off to its own dev-aspire child' ("exit=$($r.ExitCode); $($r.Text)")
    $childExit = @($r.Trace | Where-Object { $_.kind -eq 'child-exit' })
    Check ($childExit.Count -eq 1 -and [int]$childExit[0].code -eq 0) 'the child launcher was admitted under the parent restart lock' ("$($childExit | ConvertTo-Json -Compress) $($r.ChildText)")
    Assert-DevBodyInMain $r $fx 'handoff'
    Assert-NoLocks $fx 'handoff'

    $lock = Get-StatePath $fx.Main 'apphost.restart.lock'
    Set-C644Lock $lock $PID
    $direct = Invoke-C644Entry $fx dev $fx.Main @('-NoBuild', '-NoBrowser')
    Assert-NoEffects $direct 'direct dev-aspire while another restart holds the lock'
    Check ($direct.Text -match 'restart') 'direct refusal names the restart lock' $direct.Text
    $wrongOwner = Invoke-C644Entry $fx dev $fx.Main @('-NoBuild', '-NoBrowser', '-RestartOwnerPid', '1')
    Assert-NoEffects $wrongOwner 'dev-aspire naming a different restart owner'
}

Invoke-Scenario 'VersionMismatchKeepsLock' {
    param($fx)
    Set-C644Config $fx @{ versionSha = ('b' * 40) }
    $r = Invoke-C644Entry $fx restart $fx.Linked @('-NoBuild', '-TimeoutSec', '2')
    Check ($r.ExitCode -eq 5) 'wrong loaded SHA exits 5' ("exit=$($r.ExitCode); $($r.Text)")
    Check ($r.Text.Contains($fx.OriginSha) -and $r.Text.Contains('b' * 40)) 'names expected and observed SHA' $r.Text
    Check (Test-Path -LiteralPath (Get-StatePath $fx.Main 'apphost.restart.lock')) 'restart lock retained in the main worktree' ''
    Check (-not (Test-Path -LiteralPath (Get-StatePath $fx.Linked 'apphost.restart.lock'))) 'no restart lock under the linked root' ''
    Check ((Get-C644Count $r 'child-stop') -eq 0) 'child left running' ''
}

Invoke-Scenario 'HeadMovementKeepsLock' {
    param($fx)
    Set-C644Config $fx @{ commitOnHeadCall = 2 }
    $r = Invoke-C644Entry $fx restart $fx.Linked @('-NoBuild', '-TimeoutSec', '2')
    Check ($r.ExitCode -eq 5) 'HEAD moving during observation exits 5' ("exit=$($r.ExitCode); $($r.Text)")
    Check ($r.Text -match 'checkout moved') 'names the moved checkout' $r.Text
    Check ((Get-C644Count $r 'head-moved') -eq 1) 'the seam moved HEAD once' (($r.Trace | ForEach-Object { $_.kind }) -join ',')
    Check (Test-Path -LiteralPath (Get-StatePath $fx.Main 'apphost.restart.lock')) 'restart lock retained in the main worktree' ''
}

Invoke-Scenario 'HealthyWrongShaIsNotSuccess' {
    param($fx)
    Set-C644Config $fx @{ versionSha = $script:OtherSha }
    $p = Invoke-C644Entry $fx deploy $fx.Linked @('-NoBuild', '-TimeoutSec', '2')
    Check ($p.ExitCode -eq 1) 'deploy exits 1' ("exit=$($p.ExitCode); $($p.Text)")
    Check ((Get-LastVerdict $p) -eq 'DEPLOY VERDICT: failed restart-apphost.ps1 exited 5') 'healthy but wrong SHA is a failed deploy' $p.Text
    Check ((Get-C644Count $p 'health') -ge 1) 'health was observed' ''
    Check (-not (Test-Path -LiteralPath (Join-Path $fx.Linked 'verify-invoked.txt'))) 'verify-dev-stack not invoked' ''
    Check ((Get-C644Count $p 'migrations') -eq 0) 'migration check not reached' ''
}

Invoke-Scenario 'TrackedEditsWarn' {
    param($fx)
    Add-Content -LiteralPath (Join-Path $fx.Linked 'README.md') -Value 'dirty' -Encoding ASCII
    $r = Invoke-C644Entry $fx restart $fx.Linked @('-NoBuild', '-TimeoutSec', '5')
    Check ($r.ExitCode -eq 0) 'tracked edits do not refuse a matching HEAD' ("exit=$($r.ExitCode); $($r.Text)")
    Check ($r.Text -match 'tracked edits') 'tracked-edit limitation is printed' $r.Text
    Assert-NoLocks $fx 'tracked edits'
}

# Underlying harnesses: their own scenario output and counts are preserved verbatim.
$components = [ordered]@{}
if (-not $Scenario) {
    foreach ($component in 'test-apphost-main-worktree-guard.ps1', 'test-apphost-server-version.ps1') {
        Write-Host ''
        Write-Host ('--- {0} ---' -f $component)
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot $component) 2>&1 | ForEach-Object { Write-Host $_.ToString() }
        $components[$component] = $LASTEXITCODE
    }
}

$names = @($script:Results.Keys)
$failed = @($names | Where-Object { $script:Results[$_].Count -gt 0 })
$passed = @($names | Where-Object { $script:Results[$_].Count -eq 0 })
Write-Host ''
Write-Host ('CARD-0644 local deploy ({0}): {1} scenarios executed, {2} passed, {3} failed' -f $Phase, $names.Count, $passed.Count, $failed.Count)
foreach ($c in $components.Keys) { Write-Host ('  component {0}: exit {1}' -f $c, $components[$c]) }

if ($Phase -eq 'Green') {
    $componentRed = @($components.Keys | Where-Object { $components[$_] -ne 0 })
    if ($failed.Count -eq 0 -and $componentRed.Count -eq 0 -and ($Scenario -or $names.Count -eq 16)) {
        Write-Host 'CARD-0644 LOCAL DEPLOY EXIT CODE: 0  (PASS)'
        exit 0
    }
    Write-Host ('failed scenarios: {0}; red components: {1}' -f ($failed -join ', '), ($componentRed -join ', '))
    Write-Host 'CARD-0644 LOCAL DEPLOY EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}

$unwitnessed = @($script:RedDesignated | Where-Object { $names -contains $_ -and $script:Results[$_].Count -eq 0 })
$unexpected = @($failed | Where-Object { $script:RedDesignated -notcontains $_ })
$harnessErrors = @($failed | Where-Object { @($script:Results[$_] | Where-Object { $_ -like 'harness exception*' }).Count -gt 0 })
Write-Host ('red witness: designated failed {0}/{1}; designated but passed: [{2}]; unexpected failures: [{3}]; harness exceptions: [{4}]' -f `
    (@($script:RedDesignated | Where-Object { $failed -contains $_ }).Count), $script:RedDesignated.Count, ($unwitnessed -join ', '), ($unexpected -join ', '), ($harnessErrors -join ', '))
if ($unwitnessed.Count -eq 0 -and $unexpected.Count -eq 0 -and $harnessErrors.Count -eq 0 -and $names.Count -eq 16) {
    Write-Host 'CARD-0644 LOCAL DEPLOY RED WITNESS EXIT CODE: 1  (designated pre-fix failures observed)'
    exit 1
}
Write-Host 'CARD-0644 LOCAL DEPLOY RED WITNESS EXIT CODE: 2  (witness incomplete)'
exit 2
