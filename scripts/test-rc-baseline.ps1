#requires -Version 7.0
param([string]$Case,[string]$ResultsDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/lib/c487-harness.ps1"
. "$PSScriptRoot/lib/rc-admission.ps1"
$library=Join-Path $PSScriptRoot 'lib/rc-baseline.ps1'
if (Test-Path -LiteralPath $library) { . $library } else {
    # Existing behavior: no baseline picker; every request remains Full.
    function Get-RcReleaseBaseline { param($RepositoryRoot,$CandidateSha,$CurrentContext,$Pointers,$RecordedBaseline,$HistoryPath,$HistoryDigest,$UtcNow)
        @{selected=$null;reason='baseline-absent';forceFull=$true;fullAnchor=$null;gateCredit=$false;reasons=@('baseline-absent')} }
}
if (-not $ResultsDirectory) { $ResultsDirectory=Join-Path ([IO.Path]::GetTempPath()) ('bl-'+[guid]::NewGuid().ToString('N')) }
$root=Join-Path $ResultsDirectory 'fixture'
[void][IO.Directory]::CreateDirectory($root)
function Git([string[]]$a) { Invoke-C487FixtureGit $root $a }
function Put([string]$path,[string]$value) {
    $full=Join-Path $root $path
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $full)); [IO.File]::WriteAllText($full,$value)
}
function Clone($v) { ConvertFrom-Json -AsHashtable -InputObject (ConvertTo-Json -Depth 60 -InputObject $v) }
$errors=[Collections.Generic.List[string]]::new()
function Note($ok,[string]$label) { if (-not $ok) { $errors.Add($label) } }
[void](Git @('init','-q','-b','main'))
[void](Git @('config','user.name','Fixture')); [void](Git @('config','user.email','fixture@example.invalid'))
Put 'scripts/lib/rc-input-owners.json' (@{schemaVersion=1;suites=@(@{id='server';project='tests/Server/Server.csproj'});roots=@(@{path='scripts/**';consumers=@('*');reason='harness'})} | ConvertTo-Json -Depth 8)
Put 'tests/Server/Server.csproj' '<Project />'
Put 'tests/Server/Input.cs' 'test input'
$savedCommitterDate=$env:GIT_COMMITTER_DATE
$env:GIT_COMMITTER_DATE='2026-11-10T12:00:00Z'
[void](Git @('add','--all')); [void](Git @('commit','-qm','base'))
$old=Git @('rev-parse','HEAD')
$env:GIT_COMMITTER_DATE='2026-11-11T12:00:00Z'
[void](Git @('commit','--allow-empty','-qm','next'))
$new=Git @('rev-parse','HEAD')
$env:GIT_COMMITTER_DATE='2026-11-13T12:00:00Z'
[void](Git @('commit','--allow-empty','-qm','candidate'))
$candidate=Git @('rev-parse','HEAD')
$env:GIT_COMMITTER_DATE=$savedCommitterDate
[void](Git @('branch','release/1.0.0',$old))
[void](Git @('branch','release/1.0.1',$new))
[void](Git @('branch','release/1.0.2',$new))
# Real sealed S5b source/build/discovery/launch/TRX/anchor corpus, shared by these cases.
$a=& "$PSScriptRoot/test-rc-admission-reader.ps1" -ResultsDirectory (Join-Path $ResultsDirectory 'old') -RepositoryRoot $root -SourceSha $old -FixtureOnly
$b=& "$PSScriptRoot/test-rc-admission-reader.ps1" -ResultsDirectory (Join-Path $ResultsDirectory 'new') -RepositoryRoot $root -SourceSha $new -FixtureOnly
function Pointer($argsValue,[string]$ref) { @{refName=$ref;sourceSha=$argsValue.ExpectedSha;path=$argsValue.Path;digest=$argsValue.ExpectedDigest;evidenceRoot=$argsValue.EvidenceRoot} }
$po=Pointer $a 'refs/heads/release/1.0.0'; $pn=Pointer $b 'refs/heads/release/1.0.1'; $pt=Pointer $b 'refs/heads/release/1.0.2'
$rc=Pointer $b $new
$context=Clone $a.CurrentContext
$historyPath=Join-Path $ResultsDirectory 'current-history.json'
$history=@{format='antiphon-rc-baseline-history-v1';candidateSha=$candidate;complete=$true;failures=@()}
function SaveHistory { [IO.File]::WriteAllText($historyPath,(ConvertTo-Json -Depth 40 $history)); $script:historyDigest=Get-RcAdmissionFileDigest $historyPath }
SaveHistory
function Pick($pointers,$recorded=$null) {
    Get-RcReleaseBaseline -RepositoryRoot $root -CandidateSha $candidate -CurrentContext $context -Pointers $pointers -RecordedBaseline $recorded -HistoryPath $historyPath -HistoryDigest $historyDigest -UtcNow ([DateTimeOffset]'2026-11-13T12:00:00Z')
}
function Refused($d,[string]$reason) { Note ($null -eq $d.selected -and $d.forceFull -and $d.reasons -ccontains $reason) $reason }
try {
    switch ($Case) {
        'AncestorReleaseThenSameLineFallback' {
            $d=Pick @($rc,$po,$pt,$pn)
            Note ($d.selected.refName -ceq $pn.refName -and $d.selected.sourceSha -ceq $new -and $d.reason -ceq 'ancestor-release') 'release-precedence-and-ordinal-tie'
            $reverse=Pick @($pn,$pt,$po,$rc)
            Note ($reverse.selected.refName -ceq $pn.refName) 'input-order-independent'
            $d=Pick @($pn,$po,$rc) $po
            Note ($d.selected.sourceSha -ceq $old -and $d.reason -ceq 'recorded-release') 'explicit-release-precedence'
            $d=Pick @($rc)
            Note ($d.selected.sourceSha -ceq $new -and $d.reason -ceq 'same-line-candidate') 'same-line-fallback'
            Note ($d.fullAnchor.sourceSha -ceq $new -and $d.forceFull -and -not $d.gateCredit -and $d.reasons -ccontains 'release-graph-incomplete') 'anchor-and-inherited-full-gate'
            # Later failure facts survive an older explicit green baseline.
            $history.failures=@(@{sourceSha=$candidate;identity='Example.Tests/Check';outcome='Failed'})
            SaveHistory
            $d=Pick @($po,$rc) $po
            Note ($d.selected.sourceSha -ceq $old -and $d.failures.Count -eq 1 -and $d.failures[0].sourceSha -ceq $candidate -and $d.reasons -ccontains 'post-baseline-failures' -and $d.forceFull) 'later-failure-retained'
        }
        'CrossPlatformMovedAndCorruptRefRefuse' {
            $context.platform='linux'; Refused (Pick @($po)) 'baseline-platform-mismatch'; $context.platform='windows'
            [void](Git @('branch','-f','release/1.0.0',$new))
            Refused (Pick @($po,$rc) $po) 'baseline-ref-moved'
            [void](Git @('branch','-f','release/1.0.0',$old))
            $corrupt=Clone $po; $corrupt.digest='f'*64
            Refused (Pick @($corrupt,$rc)) 'admission-seal-mismatch'
            $conflict=Clone $po; $conflict.digest='e'*64
            Refused (Pick @($po,$conflict)) 'baseline-pointer-conflict'
            [IO.File]::AppendAllText($a.Path,' ')
            Refused (Pick @($po,$rc)) 'admission-seal-mismatch'
            Refused (Pick @($rc) $po) 'baseline-recorded-pointer-missing'
        }
        'MissingBaselineIsFull' {
            Refused (Pick @()) 'baseline-absent'
            # A real orphan commit with valid Git identity is unrelated, never a fallback.
            $tree=Git @('rev-parse',($old+'^{tree}'))
            $orphan=Git @('commit-tree',$tree,'-m','unrelated')
            [void](Git @('branch','release/9.0.0',$orphan))
            $unrelated=Clone $po; $unrelated.refName='refs/heads/release/9.0.0'; $unrelated.sourceSha=$orphan
            Refused (Pick @($unrelated)) 'baseline-absent'
            $context.rcLine='rc/2026-11-14'; Refused (Pick @($rc)) 'baseline-absent'; $context.rcLine='rc/2026-11-13'
            $history.complete=$false; SaveHistory
            Refused (Pick @($po)) 'baseline-history-unproven'
        }
        default { throw 'unknown baseline case' }
    }
    Assert-C487 ($errors.Count -eq 0) ('BL '+$Case+'.contract') ($errors -join ';')
} catch { Write-C487Fail ('BL '+$Case+'.contract') $_.Exception.Message }
Write-Host ('RESULT passed={0} failed={1}' -f $script:C487Passed,$script:C487Failed)
if ($script:C487Failed) { exit 1 }
exit 0
