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
    $gate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'lib/release-gate.ps1'))
    Note ($gate.Contains("coverageScope -cne 'Full'")) 'gate-credit-refuses-non-full-scope'
    foreach ($name in @('publish-release.ps1','promote-release.ps1')) {
        $path=Join-Path $PSScriptRoot $name
        if (Test-Path -LiteralPath $path) {
            Note (-not ([IO.File]::ReadAllText($path).Contains('rc-dirty-order'))) ($name + '-has-no-dirty-order-caller')
        }
    }
    if (Test-Path -LiteralPath $library) {
        $body=[IO.File]::ReadAllText($library)
        Note (-not $body.Contains('publish-release')) 'library-does-not-publish'
        Note (-not $body.Contains('Resolve-NightlyStoppedSeedOrder')) 'full-seed-function-not-replaced'
        Note (-not $body.Contains('nightly-tests-impl')) 'library-does-not-pack'
        Note (-not $body.Contains('FixProven')) 'library-does-not-honor-fix-proven'
    }
    $seed=Join-Path $PSScriptRoot 'lib/nightly-stopped-run-seed.ps1'
    Note (([IO.File]::ReadAllText($seed)).Contains('function Resolve-NightlyStoppedSeedOrder')) 'full-seed-function-remains'
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
        default { throw 'unknown-case' }
    }
} catch { Note $false ('unexpected:' + $_.Exception.Message) }
Assert-C487 ($errors.Count -eq 0) ("DO $Case.contract") ($errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
