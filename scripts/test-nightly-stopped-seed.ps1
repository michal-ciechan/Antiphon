#requires -Version 5.1
param([string]$Case='', [string]$ResultsDirectory='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'lib/nightly-common.ps1')
. (Join-Path $PSScriptRoot 'lib/nightly-failure-history.ps1')
. (Join-Path $PSScriptRoot 'lib/nightly-dispatch.ps1')
. (Join-Path $PSScriptRoot 'lib/nightly-tests-impl.ps1')
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
$root=New-C487Root $ResultsDirectory
$watch=[Diagnostics.Stopwatch]::StartNew()
$run=Join-Path $root 'synthetic-run'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/nightly/stopped-seed') -Destination $run -Recurse
# Refresh fixture timestamps as a single fixed clock; fixture membership stays synthetic.
$now=[datetime]::UtcNow
foreach($file in @(Get-ChildItem -LiteralPath $run -Filter '*.json')){
    $text=[IO.File]::ReadAllText($file.FullName).Replace('2026-10-30T08:30:00Z',$now.AddHours(-4).ToString('o'))
    [IO.File]::WriteAllText($file.FullName,$text)
}
function Snapshot {
    @((Get-Item -LiteralPath $run),(Get-ChildItem -LiteralPath $run -Recurse -Force)) | ForEach-Object {
        $hash='directory';if(-not $_.PSIsContainer){$hash=(Get-FileHash -LiteralPath $_.FullName).Hash}
        $_.FullName+'|'+$_.LastWriteTimeUtc.Ticks+'|'+$hash
    } | Sort-Object
}
function Check([bool]$Ok,[string]$Detail=''){Assert-C487 $Ok ('SS '+$Case+'.contract') $Detail}
function Write-Seed($Seed,$Path){[IO.File]::WriteAllText($Path,($Seed|ConvertTo-Json -Depth 40))}
try {
    $before=@(Snapshot)
    $seedPath=Join-Path $root 'seed.json'
    if(-not (Get-Command Export-NightlyStoppedRunSeed -ErrorAction SilentlyContinue)){throw 'stopped-run builder missing'}
    $built=Export-NightlyStoppedRunSeed -RunDirectory $run -OutputPath $seedPath -PassThru
    $after=@(Snapshot)
    $suites=@(foreach($id in @('antiphon','session-runner','pty-host','agents-pty','e2e')){
        $prefix='Synthetic.'+$id.Replace('-','_')+'.'
        $chunks=@(foreach($name in @('Passed','Never','Red','Torn','New')){
            [pscustomobject]@{id=('new-'+$name);classes=@($prefix+$name);nodes=@()}
        })
        $nodes=@(foreach($name in @('Passed','Never','Red','Torn','New')){
            [pscustomobject]@{Uid=($prefix+$name);ClassName=($prefix+$name);Method='Run';Type=$name;Categories=@()}
        })
        [pscustomobject]@{id=$id;chunks=$chunks;nodes=$nodes;exclusions=@()}
    })
    function Ingress($Path=$seedPath,[switch]$Accept,[string]$Source='rc-synthetic-old'){
        Initialize-NightlyDispatchHistory -Path $Path -CandidateSha ('b'*40) -AcceptCrossCandidateSeed:$Accept -SeedSourceCandidateId $Source
    }
    function Schedule($InputRecord){
        $members=@();$unresolved=@()
        if($InputRecord.History){$resolved=Resolve-NightlyPriorFailures $InputRecord.History $suites Full;$members=@($resolved.members);$unresolved=@($resolved.unresolved)}
        New-NightlyDispatchSchedule -PreparedSuites $suites -PriorityMembers $members -Unresolved $unresolved -PreviousRun $InputRecord.PreviousRun -CandidateSha ('b'*40) -NowUtc $now
    }
    switch($Case){
        'BuilderReadOnly' {
            $doc=Read-NightlyStoppedRunSeed $seedPath
            $bad=Join-Path $run 'forbidden.json';$refused=$false
            try {Export-NightlyStoppedRunSeed -RunDirectory $run -OutputPath $bad}catch{$refused=$_.Exception.Message -like '*output-inside-source*'}
            Check (($before -join "`n") -ceq ($after -join "`n") -and ($before -join "`n") -ceq (@(Snapshot) -join "`n") -and $refused -and @($doc.chunks|Where-Object completed).Count -eq 10 -and @($doc.chunks|Where-Object {-not $_.completed}).Count -eq 10) 'input bytes/mtimes, closed versus torn and never-started, output containment'
        }
        'FailedFirstMapping' {
            $inputRecord=Ingress -Accept;$schedule=Schedule $inputRecord
            $ids=@($schedule.entries.chunk)
            $unmapped=$inputRecord.History.PSObject.Copy()
            $unmapped.members+=@([pscustomobject]@{suite='antiphon';className='Synthetic.Removed.Red';observations=@();reasons=@('failed')})
            $resolved=Resolve-NightlyPriorFailures $unmapped $suites Full
            # The pure order must actually use known ages, independently of canonical position.
            $hours=@{'antiphon'=1;'session-runner'=5;'pty-host'=2;'agents-pty'=4;'e2e'=3}
            foreach($row in $inputRecord.History.chunks){if($row.chunk -match '(group|split)-2$'){$row.lastRunAtUtc=$now.AddHours(-$hours[$row.suite]).ToString('o')}}
            $aged=Schedule $inputRecord
            $oldest=(@($aged.entries[0..4].suite) -join ',')
            Check ($oldest -ceq 'session-runner,agents-pty,e2e,pty-host,antiphon' -and ($ids[0..4] -join ',') -ceq 'new-Red,new-Red,new-Red,new-Red,new-Red' -and ($ids[20..24] -join ',') -ceq 'new-Passed,new-Passed,new-Passed,new-Passed,new-Passed' -and ($ids[5..8] -join ',') -ceq 'new-Never,new-New,new-Never,new-New' -and @($resolved.members).Count -eq 5 -and @($resolved.unresolved).Count -eq 1) ($ids -join ',')
        }
        'AcknowledgementRequired' {
            $missing=Ingress;$wrong=Ingress -Accept -Source 'rc-wrong';$accepted=Ingress -Accept
            $schedule=Schedule $missing
            Check ($null -eq $missing.History -and $missing.FallbackReason -eq 'seed-acknowledgement-required' -and $null -eq $wrong.History -and $wrong.FallbackReason -eq 'seed-source-candidate-mismatch' -and $null -ne $accepted.History -and $schedule.entries[0].chunk -eq 'new-Passed') 'explicit acknowledgement and exact source candidate binding'
        }
        'HashMustVerify' {
            $seed=[IO.File]::ReadAllText($seedPath)|ConvertFrom-Json
            $seed.payload=$seed.payload.Replace('Synthetic.antiphon.Red','Synthetic.antiphon.Other')
            $bad=Join-Path $root 'tampered.json';Write-Seed $seed $bad
            $received=Ingress -Path $bad -Accept
            Check ($null -eq $received.History -and $received.FallbackReason -eq 'seed-hash-mismatch' -and (Schedule $received).entries[0].chunk -eq 'new-Passed') $received.FallbackReason
        }
        'TimeoutIsFailure' {
            $seed=Read-NightlyStoppedRunSeed $seedPath
            Check (@($seed.failedTests).Count -eq 5 -and @($seed.failedTests|Where-Object {$_.outcome -eq 'Timeout' -and $_.className -eq 'Synthetic.pty_host.Red' -and $_.method -eq 'Run' -and $_.chunk -eq 'all-group-2'}).Count -eq 1 -and @($seed.failedTests|Where-Object outcome -eq 'Aborted').Count -eq 1) 'Failed/Error/Timeout/Aborted must retain class, method, suite and old chunk'
        }
        'ScheduleConservesAllChunks' {
            $inputRecord=Ingress -Accept;$ok=$true
            for($trial=0;$trial -lt 64;$trial++){
                $random=[Random]::new(1514+$trial)
                foreach($s in $suites){$s.chunks=@($s.chunks|Sort-Object {$random.Next()})}
                $schedule=Schedule $inputRecord
                $expected=@(foreach($s in $suites){foreach($c in $s.chunks){$s.id+'/'+$c.id}})|Sort-Object
                $actual=@($schedule.entries|ForEach-Object {$_.suite+'/'+$_.chunk})|Sort-Object
                $ok=$ok -and ($expected -join ',') -ceq ($actual -join ',') -and $actual.Count -eq 25
            }
            # Execute the same canonical objects through the production serial boundary.
            $script:trace=@();$script:reds=0;$script:NightlyTestsOutcome='green'
            function Invoke-NightlyScheduledChunk($ExecutionSuiteState,$ExecutionChunk){$script:trace+=($ExecutionSuiteState.id+'/'+$ExecutionChunk.id);if($ExecutionChunk.id -eq 'new-New'){$script:reds++;Set-NightlyOutcome 'TESTS'}}
            function Complete-NightlySuiteExecution($CompletionSuiteState){}
            . Invoke-NightlyPreparedSchedule $schedule
            $directOk=($script:trace.Count -eq 25 -and $script:reds -eq 5 -and $script:NightlyTestsOutcome -eq 'TESTS')
            # Consume the validated ingress through the real coordinator too. Dropping
            # its PreviousRun argument must lose unfinished/passed order and go red.
            function Prepare-NightlySuiteExecution($ExecutionSuiteId){return @($suites|Where-Object id -ceq $ExecutionSuiteId)[0]}
            $npmOk=$true;$dotnetOk=$true;$lintBlocksPipeline=$false
            $Sha='b'*40;$Attempt=1;$invocationId='synthetic-coordinator';$LogRoot=$root
            $script:trace=@();$script:reds=0;$script:NightlyTestsOutcome='green'
            . Invoke-NightlyDispatchCoordinator -DispatchSuites @($suites.id) -DispatchHistory $inputRecord.History -DispatchPreviousRun $inputRecord.PreviousRun
            $coordinatorOrder=(@($script:trace[0..4]|ForEach-Object {($_ -split '/')[1]}) -join ',') -ceq 'new-Red,new-Red,new-Red,new-Red,new-Red'
            $coordinatorOrder=$coordinatorOrder -and (@($script:trace[20..24]|ForEach-Object {($_ -split '/')[1]}) -join ',') -ceq 'new-Passed,new-Passed,new-Passed,new-Passed,new-Passed'
            Check ($ok -and $directOk -and $coordinatorOrder -and $script:trace.Count -eq 25 -and $script:reds -eq 5 -and $script:NightlyTestsOutcome -eq 'TESTS') '64 random permutations and fresh red on previously unseen work; no inherited green/skip credit'
        }
        'InvalidSeedFallsBack' {
            $old=[IO.File]::ReadAllText($seedPath)|ConvertFrom-Json
            $old.createdAtUtc=$now.AddDays(-15).ToString('o');$old.contentHash=Get-NightlyStoppedSeedHash $old
            $stale=Join-Path $root 'stale.json';Write-Seed $old $stale
            $received=Ingress -Path $stale -Accept
            $ancient=[IO.File]::ReadAllText($seedPath)|ConvertFrom-Json
            $body=$ancient.payload|ConvertFrom-Json;$body.sourceObservedAtUtc=$now.AddDays(-15).ToString('o')
            $ancient.payload=$body|ConvertTo-Json -Depth 35 -Compress;$ancient.contentHash=Get-NightlyStoppedSeedHash $ancient
            $ancientPath=Join-Path $root 'ancient.json';Write-Seed $ancient $ancientPath
            $oldSource=Ingress -Path $ancientPath -Accept
            $malformed=Join-Path $root 'malformed.json';[IO.File]::WriteAllText($malformed,'{')
            $bad=Ingress -Path $malformed -Accept
            $good=Ingress -Accept
            foreach($s in $suites){foreach($c in $s.chunks){$c.classes=@('Synthetic.Gone.Class')}}
            $unmapped=Schedule $good
            Check ($oldSource.FallbackReason -eq 'seed-stale' -and $null -eq $bad.History -and (Schedule $bad).entries.Count -eq 25 -and $received.FallbackReason -eq 'seed-stale' -and $null -eq $received.History -and $unmapped.orderingReason -eq 'seed-plan-unmapped' -and $unmapped.entries[0].chunk -eq 'new-Passed' -and $unmapped.entries.Count -eq 25) 'stale or wholly unmappable seed restores complete original plan'
        }
        'PublicHopsForwardAcknowledgement' {
            . (Join-Path $PSScriptRoot 'lib/nightly-run-impl.ps1')
            $fxRoot=Join-Path $root 'hops';[void][IO.Directory]::CreateDirectory($fxRoot)
            $clone=New-C487OwnedClone $fxRoot
            $state=Join-Path $fxRoot 'state';$trace=Join-Path $fxRoot 'trace.log';$seams=Join-Path $fxRoot 'seams.ps1'
            Write-C487Seams -Path $seams -TracePath $trace -GitOutput ('b'*40)
            $runArgs=@{CheckoutRoot=$clone;StateRoot=$state;LogRoot=(Join-Path $fxRoot 'logs');SeamsPath=$seams;NoReport=$true;Trigger='manual';Ref='master';CoordinationRoot=(Join-Path $fxRoot 'coord');PassThru=$true;PriorFailuresPath=$seedPath;AcceptCrossCandidateSeed=$true;SeedSourceCandidateId='rc-synthetic-old'}
            $hop=Invoke-AntiphonNightlyRun @runArgs
            $text=[IO.File]::ReadAllText($trace)
            $selfHop=$text -match 'EXEC.*nightly-run.ps1.*-AcceptCrossCandidateSeed.*-SeedSourceCandidateId rc-synthetic-old'
            $runArgs.NoSync=$true;$runArgs.RunId='synthetic-new-nosync'
            $direct=Invoke-AntiphonNightlyRun @runArgs
            $text=[IO.File]::ReadAllText($trace)
            $executorHop=$text -match 'EXEC.*nightly-tests.ps1.*-AcceptCrossCandidateSeed.*-SeedSourceCandidateId rc-synthetic-old'
            # Exercise both real wrapper parameter blocks and splats, replacing only
            # their final implementation function in a private copied fixture.
            $wrappers=$true
            foreach($kind in @('run','tests')){
                $wrapperRoot=Join-Path $fxRoot ('wrapper-'+$kind);$lib=Join-Path $wrapperRoot 'lib';[void][IO.Directory]::CreateDirectory($lib)
                Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('nightly-'+$kind+'.ps1')) -Destination $wrapperRoot
                foreach($name in @('nightly-common','nightly-policy','nightly-coverage','release-gate','release-selection')){[IO.File]::WriteAllText((Join-Path $lib ($name+'.ps1')),'')}
                $function='Invoke-AntiphonNightly'+$(if($kind -eq 'run'){'Run'}else{'Tests'})
                $command=Get-Command $function
                $body=$command.ScriptBlock.Ast.ParamBlock.Extent.Text
                $fake='function '+$function+' { '+$body+'; return [pscustomobject]@{Accepted=[bool]$AcceptCrossCandidateSeed;Source=$SeedSourceCandidateId;Path=$PriorFailuresPath;ExitCode=0} }'
                [IO.File]::WriteAllText((Join-Path $lib ('nightly-'+$kind+'-impl.ps1')),$fake)
                $received=& (Join-Path $wrapperRoot ('nightly-'+$kind+'.ps1')) -PriorFailuresPath $seedPath -AcceptCrossCandidateSeed -SeedSourceCandidateId rc-synthetic-old -PassThru
                $wrappers=$wrappers -and $received.Accepted -and $received.Source -ceq 'rc-synthetic-old' -and $received.Path -ceq $seedPath
            }
            Check ($selfHop -and $executorHop -and $wrappers -and [IO.Directory]::Exists((Join-Path $state 'history'))) ('sync='+$selfHop+' nosync='+$executorHop+' wrappers='+$wrappers)
        }
        default {throw 'unknown case'}
    }
} catch {Check $false $_.Exception.Message}
[IO.File]::WriteAllText((Join-Path $root 'performance.json'),([ordered]@{case=$Case;wallSeconds=$watch.Elapsed.TotalSeconds;fixture='one synthetic directory per method';IO='included in body, per-call attribution unknown';database='not-used';HTTP='not-used';CLI='harness process only';buildSlot='recorded by parent';remainder='unknown'}|ConvertTo-Json))
Complete-C487Harness -ResultsDirectory $root -ExpectedRows 1
