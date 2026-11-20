#requires -Version 7.0
param([string]$Case,[string]$ResultsDirectory)
$ErrorActionPreference='Stop'
$env:GIT_TERMINAL_PROMPT='0'
$env:GIT_EDITOR='true'
. "$PSScriptRoot/lib/c487-harness.ps1"
. "$PSScriptRoot/lib/test-selection.ps1"
$library=Join-Path $PSScriptRoot 'lib/rc-dirty-order.ps1'
if (Test-Path -LiteralPath $library) { . $library } else {
    # Old default: a Full attempt launches the whole universe in input order.
    # A same-tip pass, or a caller FixProven flag, drops the obligation.
    # Containment is not consulted. That is the behavior V-10 must fail against.
    function Get-DoProp($Object,[string]$Name) {
        if ($null -eq $Object) { return $null }
        if ($Object -is [Collections.IDictionary]) { if ($Object.Contains($Name)) { return $Object[$Name] }; return $null }
        $prop=$Object.PSObject.Properties[$Name]
        if ($null -eq $prop) { return $null }
        return $prop.Value
    }
    function Get-RcDirtyLaunchOrder {
        param($Obligations,$Attempts,$Fixes,$RepositoryRoot,$CandidateSha)
        $launch=New-Object System.Collections.Generic.List[object]
        foreach ($row in @($Obligations)) {
            $key=[string](Get-DoProp $row 'key')
            $drop=$false
            foreach ($attempt in @($Attempts)) {
                $sha=[string](Get-DoProp $attempt 'sourceSha')
                $proven=(Get-DoProp $attempt 'fixProven') -eq $true
                foreach ($result in @(Get-DoProp $attempt 'results')) {
                    if ($null -eq $result) { continue }
                    if ([string](Get-DoProp $result 'key') -cne $key) { continue }
                    if ($proven) { $drop=$true }
                    $outcome=[string](Get-DoProp $result 'outcome')
                    if (($outcome -ceq 'passed' -or $outcome -ceq 'expected-skip') -and $sha -ceq [string]$CandidateSha) { $drop=$true }
                }
            }
            if ($drop) { continue }
            $launch.Add([pscustomobject]@{ key=$key; band='failed'; disposition=[string](Get-DoProp $row 'disposition') })
        }
        return [pscustomobject]@{ format='antiphon-rc-dirty-order-v1'; launch=$launch.ToArray(); omitted=@();
            gateCredit=$false; effectiveMode='Full'; trustTransition=$false; shadow=$true; activation='off' }
    }
}
if (-not $ResultsDirectory) { $ResultsDirectory=Join-Path ([IO.Path]::GetTempPath()) ('do-' + [guid]::NewGuid().ToString('N')) }
[void][IO.Directory]::CreateDirectory($ResultsDirectory)
$errors=[Collections.Generic.List[string]]::new()
function Note($ok,[string]$label) { if (-not $ok) { $errors.Add($label) } }
function Row([string]$key,[string]$disposition) { @{ key=$key; disposition=$disposition; reasons=@('digest-mismatch') } }
function Attempt([int]$sequence,[string]$sha,[string]$key,[string]$outcome,[bool]$fixProven=$false,[string]$digest='same') {
    @{ sequence=$sequence; sourceSha=$sha; fixProven=$fixProven; results=@(@{ key=$key; outcome=$outcome; inputDigest=$digest }) }
}
function HasKey($order,[string]$key) {
    foreach ($row in @($order.launch)) { if ([string]$row.key -ceq $key) { return $true } }
    return $false
}
function BandOf($order,[string]$key) {
    foreach ($row in @($order.launch)) { if ([string]$row.key -ceq $key) { return [string]$row.band } }
    return ''
}
function LaunchText($order) {
    $parts=New-Object System.Collections.Generic.List[string]
    foreach ($item in @($order.launch)) { $parts.Add(([string]$item.key) + ':' + [string]$item.band + ':' + [string]$item.disposition) }
    return ($parts -join ',')
}
function Omitted($order,[string]$key) {
    foreach ($item in @($order.omitted)) { if ([string]$item -ceq $key) { return $true } }
    return $false
}
function New-DoRepo([string]$root) {
    [void][IO.Directory]::CreateDirectory($root)
    function G([string[]]$arguments) { Invoke-C487FixtureGit $root $arguments }
    [void](G @('init','-q','-b','main'))
    [void](G @('config','user.name','Fixture'))
    [void](G @('config','user.email','fixture@example.invalid'))
    [IO.File]::WriteAllText((Join-Path $root 'note'), "base`n")
    [void](G @('add','note')); [void](G @('commit','-qm','base'))
    $base=G @('rev-parse','HEAD')
    [void](G @('checkout','-q','-b','fixline'))
    [IO.File]::WriteAllText((Join-Path $root 'note'), "fixed`n")
    [void](G @('add','note')); [void](G @('commit','-qm','fix'))
    $fix=G @('rev-parse','HEAD')
    [void](G @('checkout','-q','-b','other',$base))
    [IO.File]::WriteAllText((Join-Path $root 'note'), "other`n")
    [void](G @('add','note')); [void](G @('commit','-qm','other'))
    $other=G @('rev-parse','HEAD')
    return @{ base=$base; fix=$fix; other=$other }
}
function Refuses([scriptblock]$body,[string]$reason) {
    try { & $body | Out-Null; Note $false ('accepted:'+ $reason) }
    catch { Note ($_.Exception.Message.Contains($reason)) ('wrong-refusal:'+ $reason + ':' + $_.Exception.Message) }
}
function Assert-OffSwitch($order) {
    Note ($order.gateCredit -eq $false) 'gate-credit-stays-false'
    Note ($order.effectiveMode -ceq 'Full') 'result-effective-mode-full'
    Note ($order.trustTransition -eq $false) 'no-trust-transition'
    Note ($order.activation -ceq 'off' -and $order.shadow -eq $true) 'activation-off'
    foreach ($name in @('release-cut.ps1','nightly-run.ps1','nightly-tests.ps1')) {
        $text=[IO.File]::ReadAllText((Join-Path $PSScriptRoot $name))
        Note ($text.Contains("ValidateSet('Full', 'Shadow', 'ChangeAware')")) ($name + '-modes-unchanged')
        Note (-not $text.Contains('rc-dirty-order')) ($name + '-has-no-dirty-order-caller')
        Note (-not $text.Contains('Incremental')) ($name + '-has-no-incremental-mode')
    }
    $selection=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/release-selection.ps1'))
    Note ($selection.Contains("effectiveMode='Full'")) 'release-selection-effective-mode-full'
    $compiled=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/Antiphon.Scripts/ReleaseSelection.cs'))
    Note ($compiled.Contains('("effectiveMode", "Full")')) 'compiled-selector-effective-mode-full'
    Note (-not $compiled.Contains('Incremental')) 'compiled-selector-has-no-incremental'
    # The compiled credit verdict refuses an rc green whose explicit coverage scope is not Full.
    . "$PSScriptRoot/lib/nightly-core.ps1"; $null=Test-NightlyCoreAvailable
    $scoped=[ordered]@{ profile='rc'; trigger='rc'; ref='rc/2026-10-18'; coverageComplete=$true; testsPassed=$true; reportDelivered=$true; exitCode=0
        runId='r'; policyHash='p'; sha=('a'*40); expectedSha=('a'*40); candidateId='rc-2026-10-18-aaaaaaaaaaaa'; coverageScope='Shadow' }
    $shadow=[Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict([pscustomobject]$scoped)
    $scoped.coverageScope='Full'
    $full=[Antiphon.Nightly.ReleaseGateCredit]::CreditVerdict([pscustomobject]$scoped)
    Note (-not $shadow.Ok -and @($shadow.Reasons) -contains 'full-credit-scope-required' -and $full.Ok) 'gate-credit-refuses-non-full-scope'
    foreach ($name in @('publish-release.ps1','promote-release.ps1')) {
        $path=Join-Path $PSScriptRoot $name
        if (Test-Path -LiteralPath $path) {
            Note (-not ([IO.File]::ReadAllText($path).Contains('rc-dirty-order'))) ($name + '-has-no-dirty-order-caller')
        }
    }
    if (Test-Path -LiteralPath $library) {
        $body=[IO.File]::ReadAllText($library)
        Note (-not $body.Contains('publish-release')) 'library-does-not-publish'
        Note (-not $body.Contains('Resolve-NightlyStoppedSeedOrder') -and -not $body.Contains('StoppedSeedOrder')) 'full-seed-function-not-replaced'
        Note (-not $body.Contains('nightly-tests-impl')) 'library-does-not-pack'
        Note (-not $body.Contains('FixProven')) 'library-does-not-honor-fix-proven'
    }
    # The full stopped-seed order is compiled (DispatchSchedule.StoppedSeedOrder in the nightly core).
    $seed=Join-Path $PSScriptRoot 'lib/Antiphon.Nightly/DispatchSchedule.cs'
    Note (([IO.File]::ReadAllText($seed)).Contains('public static PSObject StoppedSeedOrder(')) 'full-seed-function-remains'
}
try {
    switch ($Case) {
        'OnlyDirtyAndSampleLaunch' {
            $rows=@(
                (Row 'dirty' 'Dirty'),
                (Row 'sample' 'Sample'),
                (Row 'sample-failed' 'Sample'),
                (Row 'carry' 'Carry'))
            $attempts=@(
                (Attempt 1 'old' 'sample' 'passed'),
                (Attempt 2 'old' 'sample-failed' 'failed'),
                (Attempt 3 'old' 'carry' 'passed'))
            $order=Get-RcDirtyLaunchOrder -Obligations $rows -Attempts $attempts -Fixes @() -RepositoryRoot '' -CandidateSha 'candidate-not-in-history'
            $present=(HasKey $order 'dirty') -and (HasKey $order 'sample') -and (HasKey $order 'sample-failed') -and -not (HasKey $order 'carry')
            Note $present 'only-dirty-and-sample-launch'
            $keys=New-Object System.Collections.Generic.List[string]
            foreach ($item in @($order.launch)) { $keys.Add([string]$item.key) }
            Note (($keys | Select-Object -Unique).Count -eq $keys.Count) 'launch-keys-unique'
            Note ((HasKey $order 'sample') -and (HasKey $order 'sample-failed') -and (BandOf $order 'sample-failed') -ceq 'failed') 'sample-membership-preserved'
            Refuses { Get-RcDirtyLaunchOrder -Obligations @($rows[0],$rows[0]) -Attempts @() -Fixes @() -RepositoryRoot '' -CandidateSha '' } 'duplicate-obligation'
            Assert-OffSwitch $order
        }
        'OpenFailurePrecedesChanged' {
            $rows=@(
                (Row 'sample' 'Sample'),
                (Row 'changed' 'Dirty'),
                (Row 'not-run' 'Dirty'),
                (Row 'interrupted' 'Dirty'),
                (Row 'failed' 'Dirty'))
            $attempts=@(
                (Attempt 1 'old-sha' 'sample' 'passed'),
                (Attempt 2 'old-sha' 'changed' 'passed'),
                (Attempt 3 'old-sha' 'interrupted' 'interrupted'),
                (Attempt 4 'old-sha' 'failed' 'failed'))
            $order=Get-RcDirtyLaunchOrder -Obligations $rows -Attempts $attempts -Fixes @() -RepositoryRoot '' -CandidateSha 'candidate-not-in-history'
            $index=@{}
            $i=0
            foreach ($item in @($order.launch)) { $index[[string]$item.key]=$i; $i++ }
            $ordered=($index['failed'] -lt $index['interrupted']) -and ($index['interrupted'] -lt $index['not-run']) -and
                ($index['not-run'] -lt $index['changed']) -and ($index['changed'] -lt $index['sample'])
            Note $ordered 'open-failure-precedes-changed'
            Note ((BandOf $order 'failed') -ceq 'failed' -and (BandOf $order 'interrupted') -ceq 'interrupted' -and
                (BandOf $order 'not-run') -ceq 'not-run' -and (BandOf $order 'changed') -ceq 'changed' -and
                (BandOf $order 'sample') -ceq 'sample') 'band-names'
            Assert-OffSwitch $order
        }
        'SameTipPassCannotClearFailure' {
            $repo=Join-Path $ResultsDirectory 'repo'
            [void][IO.Directory]::CreateDirectory($repo)
            function G([string[]]$arguments) { Invoke-C487FixtureGit $repo $arguments }
            [void](G @('init','-q','-b','main'))
            [void](G @('config','user.name','Fixture'))
            [void](G @('config','user.email','fixture@example.invalid'))
            [IO.File]::WriteAllText((Join-Path $repo 'note'), "base`n")
            [void](G @('add','note')); [void](G @('commit','-qm','base'))
            $base=G @('rev-parse','HEAD')
            [void](G @('checkout','-q','-b','fixline'))
            [IO.File]::WriteAllText((Join-Path $repo 'note'), "fixed`n")
            [void](G @('add','note')); [void](G @('commit','-qm','fix'))
            $fix=G @('rev-parse','HEAD')
            [void](G @('checkout','-q','-b','other',$base))
            [IO.File]::WriteAllText((Join-Path $repo 'note'), "other`n")
            [void](G @('add','note')); [void](G @('commit','-qm','other'))
            $other=G @('rev-parse','HEAD')
            [void](G @('checkout','-q','-b','picked',$base))
            [IO.File]::WriteAllText((Join-Path $repo 'note'), "ported`n")
            [void](G @('add','note'))
            [void](G @('commit','-qm','port','-m',('(cherry picked from commit ' + $fix + ')')))
            $pick=G @('rev-parse','HEAD')
            [void](G @('checkout','-q','-b','child',$fix))
            [void](G @('commit','--allow-empty','-qm','child'))
            $child=G @('rev-parse','HEAD')
            $rows=@(
                (Row 'same' 'Dirty'),
                (Row 'proven' 'Dirty'),
                (Row 'digest' 'Dirty'),
                (Row 'later-no-fix' 'Dirty'),
                (Row 'later-fix' 'Dirty'),
                (Row 'present' 'Dirty'),
                (Row 'cherry' 'Dirty'))
            $attempts=@(
                (Attempt 1 $base 'same' 'failed'),
                (Attempt 2 $base 'same' 'passed'),
                (Attempt 3 $base 'proven' 'failed' $true),
                (Attempt 4 $base 'digest' 'failed'),
                (Attempt 5 $base 'digest' 'passed' $false 'other-digest'),
                (Attempt 6 $base 'later-no-fix' 'failed'),
                (Attempt 7 $other 'later-no-fix' 'passed'),
                (Attempt 8 $base 'later-fix' 'failed'),
                (Attempt 9 $fix 'later-fix' 'passed'),
                (Attempt 10 $fix 'present' 'failed'),
                (Attempt 11 $child 'present' 'passed'),
                (Attempt 12 $base 'cherry' 'failed'),
                (Attempt 13 $pick 'cherry' 'passed'))
            $fixes=@(
                @{ key='later-no-fix'; fixSha=$fix },
                @{ key='later-fix'; fixSha=$fix },
                @{ key='present'; fixSha=$fix },
                @{ key='cherry'; fixSha=$fix })
            $order=Get-RcDirtyLaunchOrder -Obligations $rows -Attempts $attempts -Fixes $fixes -RepositoryRoot $repo -CandidateSha $base
            Note ((HasKey $order 'same') -and (BandOf $order 'same') -ceq 'failed') 'same-tip-pass-stays-failed'
            Note ((HasKey $order 'proven') -and (BandOf $order 'proven') -ceq 'failed') 'fix-proven-stays-failed'
            Note ((HasKey $order 'digest') -and (BandOf $order 'digest') -ceq 'failed') 'digest-only-stays-failed'
            Note ((HasKey $order 'later-no-fix') -and (BandOf $order 'later-no-fix') -ceq 'failed') 'later-unrelated-green-stays-failed'
            Note ((HasKey $order 'later-fix') -and (BandOf $order 'later-fix') -ceq 'changed') 'later-contained-fix-leaves-failed'
            Note ((HasKey $order 'present') -and (BandOf $order 'present') -ceq 'failed') 'fix-present-at-failure-stays-failed'
            Note ((HasKey $order 'cherry') -and (BandOf $order 'cherry') -ceq 'changed') 'cherry-pick-fix-leaves-failed'
            Note ($order.gateCredit -eq $false -and $order.trustTransition -eq $false -and $order.effectiveMode -ceq 'Full') 'discharged-still-has-no-gate-credit'
            Assert-OffSwitch $order
        }
        'CaseDistinctKeysStaySeparate' {
            $upper='Suite/FooTests'
            $lower='Suite/fooTests'
            $rows=@(
                (Row $upper 'Dirty'),
                (Row $lower 'Sample'))
            $attempts=@(
                (Attempt 1 'old-sha' $upper 'failed'),
                (Attempt 2 'old-sha' $lower 'passed'))
            $order=Get-RcDirtyLaunchOrder -Obligations $rows -Attempts $attempts -Fixes @() -RepositoryRoot '' -CandidateSha 'candidate-not-in-history'
            $text=LaunchText $order
            Note ($text -ceq ($upper + ':failed:Dirty,' + $lower + ':sample:Sample')) ('case-distinct-launch:' + $text)
            $repo=Join-Path $ResultsDirectory 'case-repo'
            $built=New-DoRepo $repo
            $fixRows=@(
                (Row $upper 'Dirty'),
                (Row $lower 'Dirty'))
            $fixAttempts=@(
                (Attempt 1 $built.base $upper 'failed'),
                (Attempt 2 $built.base $lower 'failed'),
                (Attempt 3 $built.other $upper 'passed'),
                (Attempt 4 $built.fix $lower 'passed'))
            $fixes=@(@{ key=$lower; fixSha=$built.fix })
            $fixed=Get-RcDirtyLaunchOrder -Obligations $fixRows -Attempts $fixAttempts -Fixes $fixes -RepositoryRoot $repo -CandidateSha $built.base
            $fixedText=LaunchText $fixed
            Note ($fixedText -ceq ($upper + ':failed:Dirty,' + $lower + ':changed:Dirty')) ('case-distinct-fix:' + $fixedText)
            Assert-OffSwitch $order
        }
        'CarryWithOpenFailureRefuses' {
            $rows=@(
                (Row 'k' 'Carry'),
                (Row 'd' 'Dirty'))
            $same=@(
                (Attempt 1 'sha-a' 'k' 'failed'),
                (Attempt 2 'sha-a' 'k' 'passed' $true))
            Refuses { Get-RcDirtyLaunchOrder -Obligations $rows -Attempts $same -Fixes @() -RepositoryRoot '' -CandidateSha 'candidate-not-in-history' } 'carry-with-open-failure'
            $interrupted=@((Attempt 1 'sha-a' 'k' 'interrupted'))
            Refuses { Get-RcDirtyLaunchOrder -Obligations $rows -Attempts $interrupted -Fixes @() -RepositoryRoot '' -CandidateSha '' } 'carry-with-open-failure'
            $later=@(
                (Attempt 1 'sha-a' 'k' 'failed'),
                (Attempt 2 'sha-b' 'k' 'passed' $true))
            Refuses { Get-RcDirtyLaunchOrder -Obligations $rows -Attempts $later -Fixes @() -RepositoryRoot '' -CandidateSha '' } 'carry-with-open-failure'
            $cleanAttempts=@(
                (Attempt 1 'old' 'k' 'passed'),
                (Attempt 2 'old' 'd' 'passed'))
            $clean=Get-RcDirtyLaunchOrder -Obligations $rows -Attempts $cleanAttempts -Fixes @() -RepositoryRoot '' -CandidateSha 'candidate-not-in-history'
            Note ((-not (HasKey $clean 'k')) -and (Omitted $clean 'k') -and (HasKey $clean 'd') -and ((BandOf $clean 'd') -ceq 'changed')) 'clean-carry-stays-omitted'
            $repo=Join-Path $ResultsDirectory 'carry-repo'
            $built=New-DoRepo $repo
            $dischargedAttempts=@(
                (Attempt 1 $built.base 'k' 'failed'),
                (Attempt 2 $built.fix 'k' 'passed' $true),
                (Attempt 3 $built.base 'd' 'passed'))
            $dischargedFixes=@(@{ key='k'; fixSha=$built.fix })
            $discharged=Get-RcDirtyLaunchOrder -Obligations $rows -Attempts $dischargedAttempts -Fixes $dischargedFixes -RepositoryRoot $repo -CandidateSha $built.base
            $dischargedText=LaunchText $discharged
            Note ((-not (HasKey $discharged 'k')) -and (Omitted $discharged 'k') -and ($dischargedText -ceq 'd:changed:Dirty')) ('discharged-carry-stays-omitted:' + $dischargedText)
            Assert-OffSwitch $clean
        }
        'UnknownShaRefuses' {
            $repo=Join-Path $ResultsDirectory 'unknown-repo'
            $built=New-DoRepo $repo
            $unknown='deadbeefdeadbeefdeadbeefdeadbeefdeadbeef'
            Note (($unknown -cne $built.base) -and ($unknown -cne $built.fix) -and ($unknown -cne $built.other)) 'unknown-sha-is-not-a-fixture-commit'
            $fixRows=@((Row 'fix-unknown' 'Dirty'))
            $fixAttempts=@(
                (Attempt 1 $built.base 'fix-unknown' 'failed'),
                (Attempt 2 $built.other 'fix-unknown' 'passed'))
            $fixList=@(@{ key='fix-unknown'; fixSha=$unknown })
            Refuses { Get-RcDirtyLaunchOrder -Obligations $fixRows -Attempts $fixAttempts -Fixes $fixList -RepositoryRoot $repo -CandidateSha $built.base } 'repo-sha-unresolved'
            $greenRows=@((Row 'green-unknown' 'Dirty'))
            $greenAttempts=@(
                (Attempt 1 $built.base 'green-unknown' 'failed'),
                (Attempt 2 $unknown 'green-unknown' 'passed'))
            $greenFixes=@(@{ key='green-unknown'; fixSha=$built.fix })
            Refuses { Get-RcDirtyLaunchOrder -Obligations $greenRows -Attempts $greenAttempts -Fixes $greenFixes -RepositoryRoot $repo -CandidateSha $built.base } 'repo-sha-unresolved'
            $heldRows=@((Row 'held' 'Dirty'))
            $heldAttempts=@(
                (Attempt 1 $built.base 'held' 'failed'),
                (Attempt 2 $built.other 'held' 'passed'))
            $heldFixes=@(@{ key='held'; fixSha=$built.fix })
            $held=Get-RcDirtyLaunchOrder -Obligations $heldRows -Attempts $heldAttempts -Fixes $heldFixes -RepositoryRoot $repo -CandidateSha $built.base
            Note ((HasKey $held 'held') -and ((BandOf $held 'held') -ceq 'failed')) 'known-uncontained-fix-stays-failed'
            Assert-OffSwitch $held
        }
        default { throw 'unknown-case' }
    }
} catch { Note $false ('unexpected:' + $_.Exception.Message) }
Assert-C487 ($errors.Count -eq 0) ("DO $Case.contract") ($errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
