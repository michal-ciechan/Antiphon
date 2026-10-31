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
    $items=@(Get-Item -LiteralPath $run)+@(Get-ChildItem -LiteralPath $run -Recurse -Force)
    $items | ForEach-Object {
        $hash='directory';if(-not $_.PSIsContainer){$hash=(Get-FileHash -LiteralPath $_.FullName).Hash}
        $_.FullName+'|'+$_.LastWriteTimeUtc.Ticks+'|'+$hash
    } | Sort-Object
}
function Check([bool]$Ok,[string]$Detail=''){Assert-C487 $Ok ('SS '+$Case+'.contract') $Detail}
function Write-Seed($Seed,$Path){[IO.File]::WriteAllText($Path,($Seed|ConvertTo-Json -Depth 40))}
function Snapshot-Tree([string[]]$Roots){
    $items=New-Object System.Collections.Generic.List[string]
    foreach($tree in $Roots){
        $items.Add((Get-Item -LiteralPath $tree).FullName+'|dir')
        foreach($item in @(Get-ChildItem -LiteralPath $tree -Recurse -Force)){
            $hash='directory'
            if(-not $item.PSIsContainer){$hash=(Get-FileHash -LiteralPath $item.FullName).Hash}
            [void]$items.Add($item.FullName+'|'+$item.LastWriteTimeUtc.Ticks+'|'+$hash)
        }
    }
    return (($items|Sort-Object) -join "`n")
}
function New-SeedSiblingLayout([string]$Logs,[string]$RunId,[string]$RunName){
    $runDir=Join-Path $Logs $RunName
    $timing=Join-Path $Logs ('timing-'+$RunId)
    [void][IO.Directory]::CreateDirectory($runDir)
    [void][IO.Directory]::CreateDirectory($timing)
    foreach($file in @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'fixtures/nightly/stopped-seed') -File)){
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $runDir $file.Name)
    }
    foreach($plan in @(Get-ChildItem -LiteralPath $runDir -Filter 'groups-planned*.json' -File)){
        $text=[IO.File]::ReadAllText($plan.FullName).Replace('"runId": "synthetic-stopped"',('"runId": "'+$RunId+'"'))
        [IO.File]::WriteAllText((Join-Path $timing $plan.Name),$text)
        Remove-Item -LiteralPath $plan.FullName
    }
    [IO.File]::WriteAllText((Join-Path $timing 'groups-planned-index.json'),'{"version":1}')
    return [pscustomobject]@{Run=$runDir;Timing=$timing;RunId=$RunId}
}
function Write-SeedSummary([string]$RunDir,[string]$TimingRoot,[string]$RunId,[string]$Sha){
    $header=[ordered]@{startedAt='2026-10-30T08:30:00Z';completedAt='2026-10-30T12:30:00Z';sha=$Sha;runId=$RunId;selection=[ordered]@{timingChunks='decoy'};timingRoot=$TimingRoot}
    $text=($header|ConvertTo-Json -Compress).TrimEnd('}')
    $text=$text+',"timingChunks": INVALID}'
    [IO.File]::WriteAllText((Join-Path $RunDir 'summary.json'),$text)
}
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
            $probePath=Join-Path $run 'owner.json';$probeBytes=[IO.File]::ReadAllBytes($probePath);$probeTime=[IO.File]::GetLastWriteTimeUtc($probePath)
            $probeBefore=@(Snapshot);$changed=[byte[]]$probeBytes.Clone();$changed[0]=$changed[0] -bxor 1
            try{
                [IO.File]::WriteAllBytes($probePath,$changed);[IO.File]::SetLastWriteTimeUtc($probePath,$probeTime)
                $detectsBytes=($probeBefore -join "`n") -cne (@(Snapshot) -join "`n")
            }finally{[IO.File]::WriteAllBytes($probePath,$probeBytes);[IO.File]::SetLastWriteTimeUtc($probePath,$probeTime)}
            Check ($detectsBytes -and ($before -join "`n") -ceq ($after -join "`n") -and ($before -join "`n") -ceq (@(Snapshot) -join "`n") -and $refused -and @($doc.chunks|Where-Object {$_.suite -ceq 'antiphon' -and $_.chunk -ceq 'profile-4' -and @($_.classes) -ccontains 'Synthetic.antiphon.Torn'}).Count -eq 1 -and @($doc.chunks|Where-Object completed).Count -eq 11 -and @($doc.chunks|Where-Object {-not $_.completed}).Count -eq 10) 'input bytes/mtimes, closed versus torn and never-started, output containment'
        }
        'FailedFirstMapping' {
            $inputRecord=Ingress -Accept;$schedule=Schedule $inputRecord
            $ids=@($schedule.entries.chunk)
            $unmapped=$inputRecord.History.PSObject.Copy()
            $resolved=Resolve-NightlyPriorFailures $unmapped $suites Full
            # The pure order must actually use known ages, independently of canonical position.
            $hours=@{'antiphon'=1;'session-runner'=5;'pty-host'=2;'agents-pty'=4;'e2e'=3}
            foreach($row in $inputRecord.History.chunks){if($row.chunk -match '(group|split)-2$'){$row.lastRunAtUtc=$now.AddHours(-$hours[$row.suite]).ToString('o')}}
            $aged=Schedule $inputRecord
            $oldest=(@($aged.entries[0..4].suite) -join ',')
            Check ($oldest -ceq 'session-runner,agents-pty,e2e,pty-host,antiphon' -and ($ids[0..4] -join ',') -ceq 'new-Red,new-Red,new-Red,new-Red,new-Red' -and ($ids[20..24] -join ',') -ceq 'new-Passed,new-Passed,new-Passed,new-Passed,new-Passed' -and ($ids[5..8] -join ',') -ceq 'new-Never,new-New,new-Never,new-New' -and @($inputRecord.History.members).Count -eq 6 -and @($resolved.members).Count -eq 5 -and @($resolved.unresolved).Count -eq 1) ($ids -join ',')
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
            Check (@($seed.failedTests).Count -eq 6 -and @($seed.failedTests|Where-Object {$_.outcome -eq 'Timeout' -and $_.className -eq 'Synthetic.pty_host.Red' -and $_.method -eq 'Run' -and $_.chunk -eq 'all-group-2'}).Count -eq 1 -and @($seed.failedTests|Where-Object outcome -eq 'Aborted').Count -eq 1) 'Failed/Error/Timeout/Aborted must retain class, method, suite and old chunk'
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
                $parameterBlock=$command.ScriptBlock.Ast.Body.ParamBlock
                if($null -eq $parameterBlock -or $parameterBlock.Extent.Text -notmatch 'switch.*AcceptCrossCandidateSeed'){throw 'wrapper fixture requires a real parameter block'}
                $body=$parameterBlock.Extent.Text
                $fake='function '+$function+' { '+$body+'; return [pscustomobject]@{Accepted=[bool]$AcceptCrossCandidateSeed;Source=$SeedSourceCandidateId;Path=$PriorFailuresPath;ExitCode=0} }'
                [IO.File]::WriteAllText((Join-Path $lib ('nightly-'+$kind+'-impl.ps1')),$fake)
                $received=& (Join-Path $wrapperRoot ('nightly-'+$kind+'.ps1')) -PriorFailuresPath $seedPath -AcceptCrossCandidateSeed -SeedSourceCandidateId rc-synthetic-old -PassThru
                $wrappers=$wrappers -and $received.Accepted -and $received.Source -ceq 'rc-synthetic-old' -and $received.Path -ceq $seedPath
            }
            Check ($selfHop -and $executorHop -and $wrappers -and [IO.Directory]::Exists((Join-Path $state 'history'))) ('sync='+$selfHop+' nosync='+$executorHop+' wrappers='+$wrappers)
        }
        'SiblingTimingFromSummary' {
            $runId='d47848cce896471db1cc8ca346707cad'
            $sha='a'*40
            $layout=New-SeedSiblingLayout (Join-Path $root 'logs') $runId '2026-10-30-0824-d47848cc'
            Write-SeedSummary $layout.Run $layout.Timing $runId $sha
            $out=Join-Path $root 'sibling-seed.json'
            $before=Snapshot-Tree @($layout.Run,$layout.Timing)
            $export=Join-Path $PSScriptRoot 'export-nightly-stopped-run-seed.ps1'
            & pwsh -NoProfile -File $export -RunDirectory $layout.Run -OutputPath $out -SourceCandidateId 'rc-synthetic-old' -SourceSha $sha
            $exitOk=($LASTEXITCODE -eq 0 -and [IO.File]::Exists($out))
            $after=Snapshot-Tree @($layout.Run,$layout.Timing)
            $doc=$null;$torn=$false
            if($exitOk){
                $doc=Read-NightlyStoppedRunSeed $out
                $torn=(@($doc.chunks|Where-Object {$_.suite -ceq 'antiphon' -and $_.chunk -ceq 'profile-4' -and @($_.classes) -ccontains 'Synthetic.antiphon.Torn'}).Count -eq 1 -and $doc.sourceRunId -ceq $runId)
            }
            $plansInRun=@(Get-ChildItem -LiteralPath $layout.Run -Filter 'groups-planned*.json' -File).Count
            Check ($exitOk -and $torn -and $plansInRun -eq 0 -and $before -ceq $after) ('operator export from the run directory discovers summary timingRoot; exit='+$LASTEXITCODE)
        }
        'StoppedTimingRootAndRefusals' {
            $runId='d47848cce896471db1cc8ca346707cad'
            $sha='a'*40
            $explicit=New-SeedSiblingLayout (Join-Path $root 'explicit-logs') $runId '2026-10-30-0824-d47848cc'
            Remove-Item -LiteralPath (Join-Path $explicit.Run 'summary.json') -ErrorAction SilentlyContinue
            $explicitOut=Join-Path $root 'explicit-seed.json'
            $explicitOk=$false
            try {
                [void](Export-NightlyStoppedRunSeed -RunDirectory $explicit.Run -OutputPath $explicitOut -TimingRoot $explicit.Timing -SourceCandidateId 'rc-synthetic-old' -SourceSha $sha -PassThru)
                $explicitDoc=Read-NightlyStoppedRunSeed $explicitOut
                $explicitOk=($explicitDoc.sourceRunId -ceq $runId -and @($explicitDoc.chunks).Count -gt 0)
            } catch { $explicitOk=$false }
            $auto=New-SeedSiblingLayout (Join-Path $root 'auto-logs') $runId '2026-10-30-0824-d47848cc'
            Remove-Item -LiteralPath (Join-Path $auto.Run 'owner.json')
            $autoOut=Join-Path $root 'auto-seed.json'
            $autoOk=$false
            try {
                [void](Export-NightlyStoppedRunSeed -RunDirectory $auto.Run -OutputPath $autoOut -SourceCandidateId 'rc-synthetic-old' -SourceSha $sha -PassThru)
                $autoDoc=Read-NightlyStoppedRunSeed $autoOut
                $autoOk=($autoDoc.sourceRunId -ceq $runId)
            } catch { $autoOk=$false }
            function Refuse-Timing([scriptblock]$Arrange,[string]$Like){
                $id=[guid]::NewGuid().ToString('N')
                $lay=New-SeedSiblingLayout (Join-Path $root ('refuse-'+$id)) $runId '2026-10-30-0824-d47848cc'
                & $Arrange $lay
                $msg=''
                try { [void](Export-NightlyStoppedRunSeed -RunDirectory $lay.Run -OutputPath (Join-Path $root ('refuse-'+$id+'.json')) -SourceCandidateId 'rc-synthetic-old' -SourceSha $sha -PassThru) }
                catch { $msg=$_.Exception.Message }
                return ($msg -like $Like)
            }
            $outside=Refuse-Timing { param($lay)
                $elsewhere=Join-Path $root ('outside-timing-'+$lay.RunId)
                [void][IO.Directory]::CreateDirectory($elsewhere)
                Write-SeedSummary $lay.Run $elsewhere $lay.RunId $sha
            } '*seed-timing-outside*'
            $symlink=Refuse-Timing { param($lay)
                $real=Join-Path ([IO.Path]::GetDirectoryName($lay.Timing)) ('timing-real-'+$lay.RunId)
                Move-Item -LiteralPath $lay.Timing -Destination $real
                New-Item -ItemType SymbolicLink -Path $lay.Timing -Target $real | Out-Null
                Write-SeedSummary $lay.Run $lay.Timing $lay.RunId $sha
            } '*evidence-link*'
            $other=Refuse-Timing { param($lay)
                $wrong=Join-Path ([IO.Path]::GetDirectoryName($lay.Timing)) 'timing-ffffffffffffffffffffffffffffffff'
                [void][IO.Directory]::CreateDirectory($wrong)
                Write-SeedSummary $lay.Run $wrong $lay.RunId $sha
            } '*seed-timing-run-mismatch*'
            Check ($explicitOk -and $autoOk -and $outside -and $symlink -and $other) ('explicit='+$explicitOk+' auto='+$autoOk+' outside='+$outside+' symlink='+$symlink+' other='+$other)
        }
        'ChunkUnfinishedTolerance' {
            $sha='a'*40
            $timing=Join-Path $run 'antiphon-all-group-1.timing.json'
            $lock=[IO.File]::Open($timing,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
            $lockedOk=$false;$lockedDetail=''
            try {
                $lockedOut=Join-Path $root 'locked-seed.json'
                try {
                    [void](Export-NightlyStoppedRunSeed -RunDirectory $run -OutputPath $lockedOut -PassThru)
                    $lockedDoc=Read-NightlyStoppedRunSeed $lockedOut
                    $row=@($lockedDoc.chunks|Where-Object {$_.suite -ceq 'antiphon' -and $_.chunk -ceq 'all-group-1'})[0]
                    $lockedOk=(-not $row.completed -and [string]$row.unfinishedReason -like '*timing*')
                    $lockedDetail=[string]$row.unfinishedReason
                } catch { $lockedDetail=$_.Exception.Message }
            } finally { $lock.Dispose() }
            $trx=Join-Path $run 'antiphon-all-group-2.trx'
            $trxBytes=[IO.File]::ReadAllBytes($trx)
            $script:NightlySeedReadBarrier={ param($Path) if($Path -ceq $trx){ [IO.File]::AppendAllText($Path,' ') } }
            $changedOk=$false;$changedDetail=''
            try {
                $changedOut=Join-Path $root 'changed-seed.json'
                try {
                    [void](Export-NightlyStoppedRunSeed -RunDirectory $run -OutputPath $changedOut -PassThru)
                    $changedDoc=Read-NightlyStoppedRunSeed $changedOut
                    $row=@($changedDoc.chunks|Where-Object {$_.suite -ceq 'antiphon' -and $_.chunk -ceq 'all-group-2'})[0]
                    $kept=@($changedDoc.failedTests|Where-Object {$_.suite -ceq 'antiphon' -and $_.chunk -ceq 'all-group-2'}).Count
                    $changedOk=(-not $row.completed -and [string]$row.unfinishedReason -like '*trx*' -and $kept -eq 0)
                    $changedDetail=[string]$row.unfinishedReason+' kept='+$kept
                } catch { $changedDetail=$_.Exception.Message }
            } finally { $script:NightlySeedReadBarrier=$null; [IO.File]::WriteAllBytes($trx,$trxBytes) }
            $passed=Join-Path $run 'antiphon-all-group-1.trx'
            $passedText=[IO.File]::ReadAllText($passed)
            $open=$passedText.Replace('<ResultSummary outcome="Completed"><Counters total="1" executed="1" /></ResultSummary>','')
            $openOk=$false;$openDetail=''
            try {
                [IO.File]::WriteAllText($passed,$open)
                $openOut=Join-Path $root 'open-seed.json'
                [void](Export-NightlyStoppedRunSeed -RunDirectory $run -OutputPath $openOut -PassThru)
                $openDoc=Read-NightlyStoppedRunSeed $openOut
                $row=@($openDoc.chunks|Where-Object {$_.suite -ceq 'antiphon' -and $_.chunk -ceq 'all-group-1'})[0]
                $openOk=(-not $row.completed)
                $openDetail='completed='+[string]$row.completed
            } catch { $openDetail=$_.Exception.Message }
            finally { [IO.File]::WriteAllText($passed,$passedText) }
            $denied=Join-Path $run 'antiphon-all-group-2.timing.json'
            $deniedMode=[IO.File]::GetUnixFileMode($denied)
            $deniedOk=$false;$deniedDetail=''
            try {
                [IO.File]::SetUnixFileMode($denied,[IO.UnixFileMode]::None)
                $deniedOut=Join-Path $root 'denied-seed.json'
                try {
                    [void](Export-NightlyStoppedRunSeed -RunDirectory $run -OutputPath $deniedOut -PassThru)
                    $deniedDoc=Read-NightlyStoppedRunSeed $deniedOut
                    $row=@($deniedDoc.chunks|Where-Object {$_.suite -ceq 'antiphon' -and $_.chunk -ceq 'all-group-2'})[0]
                    $deniedOk=(-not $row.completed -and [string]$row.unfinishedReason -like '*timing*')
                    $deniedDetail=[string]$row.unfinishedReason
                } catch { $deniedDetail=$_.Exception.Message }
            } finally { [IO.File]::SetUnixFileMode($denied,$deniedMode) }
            $tamper=New-SeedSiblingLayout (Join-Path $root 'tamper-logs') 'd47848cce896471db1cc8ca346707cad' '2026-10-30-0824-d47848cc'
            Write-SeedSummary $tamper.Run $tamper.Timing 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee' $sha
            $tamperMsg=''
            try { [void](Export-NightlyStoppedRunSeed -RunDirectory $tamper.Run -OutputPath (Join-Path $root 'tamper-seed.json') -SourceCandidateId 'rc-synthetic-old' -SourceSha $sha -PassThru) }
            catch { $tamperMsg=$_.Exception.Message }
            $tamperOk=($tamperMsg -like '*seed-timing-run-mismatch*' -or $tamperMsg -like '*seed-plan-run-mismatch*' -or $tamperMsg -like '*seed-summary*')
            Check ($lockedOk -and $changedOk -and $openOk -and $deniedOk -and $tamperOk) ('locked='+$lockedDetail+'; changed='+$changedDetail+'; open='+$openDetail+'; denied='+$deniedDetail+'; tamper='+$tamperMsg)
        }
        'LinkCacheRefusesReplacement' {
            $fileLink=Join-Path $run 'linked-file.txt'
            New-Item -ItemType SymbolicLink -Path $fileLink -Target (Join-Path $run 'owner.json') | Out-Null
            $fileMsg=''
            try { [void](Export-NightlyStoppedRunSeed -RunDirectory $run -OutputPath (Join-Path $root 'file-link-seed.json') -PassThru) }
            catch { $fileMsg=$_.Exception.Message }
            finally { Remove-Item -LiteralPath $fileLink -Force }
            $parent=Join-Path $root 'link-parent'
            $real=Join-Path $parent 'real-run'
            [void][IO.Directory]::CreateDirectory($real)
            foreach($file in @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'fixtures/nightly/stopped-seed') -File)){
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $real $file.Name)
            }
            $linkedRun=Join-Path $parent 'linked-run'
            New-Item -ItemType SymbolicLink -Path $linkedRun -Target $real | Out-Null
            $parentMsg=''
            try { [void](Export-NightlyStoppedRunSeed -RunDirectory $linkedRun -OutputPath (Join-Path $root 'parent-link-seed.json') -PassThru) }
            catch { $parentMsg=$_.Exception.Message }
            $swap=Join-Path $root 'swap-dir'
            [void][IO.Directory]::CreateDirectory($swap)
            $leaf=Join-Path $swap 'a.txt'
            [IO.File]::WriteAllText($leaf,'stable')
            Assert-NightlyHistoryNoLink $swap
            $held=Join-Path $root 'swap-real'
            Move-Item -LiteralPath $swap -Destination $held
            New-Item -ItemType SymbolicLink -Path $swap -Target $held | Out-Null
            $swapMsg=''
            try { Assert-NightlyHistoryFileStable (Join-Path $swap 'a.txt') }
            catch { $swapMsg=$_.Exception.Message }
            Check ($fileMsg -like '*evidence-link*' -and $parentMsg -like '*evidence-link*' -and $swapMsg -like '*evidence-link*') ('file='+$fileMsg+'; parent='+$parentMsg+'; swap='+$swapMsg)
        }
        default {throw 'unknown case'}
    }
} catch {Check $false $_.Exception.Message}
[IO.File]::WriteAllText((Join-Path $root 'performance.json'),([ordered]@{case=$Case;wallSeconds=$watch.Elapsed.TotalSeconds;fixture='one synthetic directory per method';IO='included in body, per-call attribution unknown';database='not-used';HTTP='not-used';CLI='harness process only';buildSlot='recorded by parent';remainder='unknown'}|ConvertTo-Json))
Complete-C487Harness -ResultsDirectory $root -ExpectedRows 1
