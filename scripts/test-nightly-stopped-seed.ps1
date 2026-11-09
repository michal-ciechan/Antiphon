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
    $ownerPath=Join-Path $runDir 'owner.json'
    if([IO.File]::Exists($ownerPath)){
        $ownerText=[IO.File]::ReadAllText($ownerPath).Replace('"runId": "synthetic-stopped"',('"runId": "'+$RunId+'"'))
        [IO.File]::WriteAllText($ownerPath,$ownerText)
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
# Unix modes where they exist. Elsewhere a current-user deny ACL, or a counted not-applicable.
# $script:NightlySeedPlatformSeam.UnixModesSupported = $false forces the Windows branch.
$script:SeedCustodyNotApplicableLine='SEED-CUSTODY not-applicable: Windows ACL custody is not checkable on this platform'
function Test-SeedUnixModesSupported {
    $seam=$script:NightlySeedPlatformSeam
    if($seam -and $seam.Contains('UnixModesSupported')){return [bool]$seam.UnixModesSupported}
    return ($env:OS -cne 'Windows_NT')
}
function Write-SeedCustodyNotApplicable {
    Write-Host $script:SeedCustodyNotApplicableLine
    $script:NightlySeedCustodyNote=$script:SeedCustodyNotApplicableLine
    $script:NightlySeedCustodyNotApplicable=1+[int]$script:NightlySeedCustodyNotApplicable
}
function Use-SeedUnixFileMode([string]$Path,$Mode){
    if(-not (Test-SeedUnixModesSupported)){throw 'SEED-CUSTODY unix-mode-on-unsupported-platform'}
    $script:NightlySeedUnixModeCalls=1+[int]$script:NightlySeedUnixModeCalls
    if($null -eq $Mode){return [IO.File]::GetUnixFileMode($Path)}
    [IO.File]::SetUnixFileMode($Path,$Mode)
}
function Deny-SeedWindowsCurrentUserRead([string]$Path){
    try {
        $idType=[type]::GetType('System.Security.Principal.WindowsIdentity, System.Security.Principal.Windows')
        $ruleType=[type]::GetType('System.Security.AccessControl.FileSystemAccessRule, System.IO.FileSystem.AccessControl')
        $extType=[type]::GetType('System.IO.FileSystemAclExtensions, System.IO.FileSystem.AccessControl')
        $sectionsType=[type]::GetType('System.Security.AccessControl.AccessControlSections, System.Security.AccessControl')
        $rightsType=[type]::GetType('System.Security.AccessControl.FileSystemRights, System.IO.FileSystem.AccessControl')
        $accessType=[type]::GetType('System.Security.AccessControl.AccessControlType, System.IO.FileSystem.AccessControl')
        if(-not ($idType -and $ruleType -and $extType -and $sectionsType -and $rightsType -and $accessType)){return $null}
        $current=$idType.GetMethod('GetCurrent',[type[]]@()).Invoke($null,@())
        $sid=$current.User
        if(-not $sid){return $null}
        $file=[IO.FileInfo]::new($Path)
        $sections=[enum]::Parse($sectionsType,'Access')
        $get=$null;$set=$null
        foreach($method in $extType.GetMethods()){
            $parameters=$method.GetParameters()
            if($parameters.Count -ne 2 -or $parameters[0].ParameterType -ne [IO.FileInfo]){continue}
            if($method.Name -ceq 'GetAccessControl'){$get=$method}
            elseif($method.Name -ceq 'SetAccessControl'){$set=$method}
        }
        if(-not $get -or -not $set){return $null}
        $acl=$get.Invoke($null,@($file,$sections))
        $rights=[enum]::Parse($rightsType,'Read')
        $deny=[enum]::Parse($accessType,'Deny')
        $rule=$null
        foreach($ctor in $ruleType.GetConstructors()){
            if($ctor.GetParameters().Count -eq 3){$rule=$ctor.Invoke(@($sid,$rights,$deny));break}
        }
        if(-not $rule){return $null}
        if([string]$rule.IdentityReference.Value -cne [string]$sid.Value){throw 'SEED-CUSTODY acl-not-current-user'}
        $acl.AddAccessRule($rule)
        $set.Invoke($null,@($file,$acl))
        $readable=$true
        try{[void][IO.File]::ReadAllBytes($Path)}catch{$readable=$false}
        if($readable){
            [void]$acl.RemoveAccessRule($rule)
            $set.Invoke($null,@($file,$acl))
            throw 'SEED-CUSTODY windows-acl-did-not-deny-current-user'
        }
        return { [void]$acl.RemoveAccessRule($rule); $set.Invoke($null,@($file,$acl)) }.GetNewClosure()
    } catch {
        $inner=$_.Exception
        while($inner.InnerException){$inner=$inner.InnerException}
        if($inner -is [PlatformNotSupportedException] -or $inner.Message -like '*not supported on this platform*'){return $null}
        if($inner.Message -like 'SEED-CUSTODY*'){throw}
        throw
    }
}
function Enter-SeedUnreadableTiming([string]$Path){
    if(Test-SeedUnixModesSupported){
        $script:NightlySeedDenialBranch='unix-mode'
        $mode=Use-SeedUnixFileMode $Path $null
        Use-SeedUnixFileMode $Path ([IO.UnixFileMode]::None)
        return { Use-SeedUnixFileMode $Path $mode }.GetNewClosure()
    }
    $script:NightlySeedDenialBranch='windows-acl'
    $script:NightlySeedWindowsBranchRan=$true
    $seam=$script:NightlySeedPlatformSeam
    if($seam -and $seam.WindowsAcl){
        $applied=& $seam.WindowsAcl $Path
        if($applied -and $applied.Restore){return $applied.Restore}
        Write-SeedCustodyNotApplicable
        return $null
    }
    $restore=Deny-SeedWindowsCurrentUserRead $Path
    if($restore){return $restore}
    Write-SeedCustodyNotApplicable
    return $null
}
function Write-MergeSeed {
    param([string]$Path,[string]$RunId,[string]$Candidate,[string]$Sha,[string]$Observed,[string]$Created,[object[]]$Chunks,[object[]]$Failed,[object[]]$Evidence)
    if($null -eq $Failed -or @($Failed).Count -eq 0){$Failed=New-Object object[] 0}
    if($null -eq $Evidence -or @($Evidence).Count -eq 0){$Evidence=New-Object object[] 0}
    $payload=[ordered]@{sourceObservedAtUtc=$Observed;chunks=$Chunks;failedTests=$Failed;evidence=$Evidence}
    $envelope=[ordered]@{format='antiphon-nightly-stopped-seed-v1';seedKind='operator-seed-from-stopped-run';sourceRunId=$RunId;sourceCandidateId=$Candidate;sourceSha=$Sha;createdAtUtc=$Created;payload=($payload|ConvertTo-Json -Depth 30 -Compress);contentHash=''}
    $envelope.contentHash=Get-NightlyStoppedSeedHash $envelope
    $utf8=New-Object System.Text.UTF8Encoding $false
    [IO.File]::WriteAllText($Path,($envelope|ConvertTo-Json -Depth 35),$utf8)
}
function New-MergeChunk([string]$Suite,[string]$Chunk,[string[]]$Classes,[bool]$Completed,[bool]$Started,[string]$Last){
    return [ordered]@{suite=$Suite;chunk=$Chunk;classes=@($Classes);completed=$Completed;started=$Started;lastRunAtUtc=$Last}
}
function New-MergeFail([string]$Suite,[string]$Chunk,[string]$Class,[string]$Method,[string]$Outcome){
    return [ordered]@{suite=$Suite;chunk=$Chunk;className=$Class;method=$Method;outcome=$Outcome}
}
function Invoke-MergeNightlySeeds {
    param([string[]]$Seeds,[string]$Decision='',[string[]]$Proven=@(),[string]$Repo='',[string]$OutputPath,[string]$ReportPath,[switch]$Strict)
    $scriptPath=Join-Path $PSScriptRoot 'merge-nightly-seeds.ps1'
    if(-not [IO.File]::Exists($scriptPath)){throw 'merge-nightly-seeds.ps1 missing'}
    $argv=New-Object System.Collections.Generic.List[string]
    [void]$argv.Add('-NoProfile');[void]$argv.Add('-File');[void]$argv.Add($scriptPath)
    foreach($seedPath in @($Seeds)){[void]$argv.Add('-Seed');[void]$argv.Add($seedPath)}
    if(-not [string]::IsNullOrWhiteSpace($Decision)){[void]$argv.Add('-Decision');[void]$argv.Add($Decision)}
    foreach($row in @($Proven)){if(-not [string]::IsNullOrWhiteSpace($row)){[void]$argv.Add('-Proven');[void]$argv.Add($row)}}
    if(-not [string]::IsNullOrWhiteSpace($Repo)){[void]$argv.Add('-Repo');[void]$argv.Add($Repo)}
    if($Strict){[void]$argv.Add('-Strict')}
    [void]$argv.Add('-OutputPath');[void]$argv.Add($OutputPath)
    [void]$argv.Add('-ReportPath');[void]$argv.Add($ReportPath)
    $text=& pwsh @($argv.ToArray()) 2>&1 | Out-String
    return [pscustomobject]@{Code=$LASTEXITCODE;Text=$text}
}
function Read-MergeReport([string]$Path){return ([IO.File]::ReadAllText($Path)|ConvertFrom-Json)}
function Find-MergeIdentity($Report,[string]$Method){return @($Report.identities|Where-Object {[string]$_.method -ceq $Method})[0]}
function Test-MergeFileBytes([string]$Left,[string]$Right){
    $a=[IO.File]::ReadAllBytes($Left);$b=[IO.File]::ReadAllBytes($Right)
    if($a.Length -ne $b.Length){return $false}
    for($i=0;$i -lt $a.Length;$i++){if($a[$i] -ne $b[$i]){return $false}}
    return $true
}
function New-MergeGitRepo([string]$Repo){
    # Invoke-TestSelectionGit is loaded at script scope before the switch. A dot-source
    # inside this function would vanish on return, and the commits run after that.
    if(-not (Get-Command Invoke-TestSelectionGit -ErrorAction SilentlyContinue)){throw 'Invoke-TestSelectionGit missing'}
    [void][IO.Directory]::CreateDirectory($Repo)
    Invoke-C487FixtureGit $Repo @('init','-b','master')
    Invoke-C487FixtureGit $Repo @('config','user.email','merge-seed@example.com')
    Invoke-C487FixtureGit $Repo @('config','user.name','merge-seed')
}
function Add-MergeGitCommit([string]$Repo,[string]$Name,[string]$Content,[string]$Title,[string]$Body){
    $utf8=New-Object System.Text.UTF8Encoding $false
    [IO.File]::WriteAllText((Join-Path $Repo $Name),$Content,$utf8)
    [void](Invoke-C487FixtureGit $Repo @('add','--',$Name))
    if([string]::IsNullOrWhiteSpace($Body)){[void](Invoke-C487FixtureGit $Repo @('commit','-m',$Title))}
    else{[void](Invoke-C487FixtureGit $Repo @('commit','-m',$Title,'-m',$Body))}
    return (Invoke-C487FixtureGit $Repo @('rev-parse','HEAD'))
}
function Invoke-MergeChunkOrder($Doc,$Chunks,$Now){
    $suiteNodes=New-Object System.Collections.Generic.List[object]
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($chunk in @($Chunks)){
        $class=[string]@($chunk.classes)[0]
        $key=$class+'/Run'
        if($seen.Add($key)){[void]$suiteNodes.Add([pscustomobject]@{Uid=$key;ClassName=$class;Method='Run';Type='T';Categories=@()})}
    }
    $suite=@([pscustomobject]@{id='antiphon';chunks=@($Chunks);nodes=@($suiteNodes.ToArray());exclusions=@()})
    $resolved=Resolve-NightlyPriorFailures $Doc $suite Full
    return New-NightlyDispatchSchedule -PreparedSuites $suite -PriorityMembers @($resolved.members) -PreviousRun $Doc -CandidateSha ('b'*40) -NowUtc $Now
}
try {
    if($Case -in @('CumulativeMergeProvenDrop','CumulativeMergeUnfixedGreen','CumulativeMergeCherryPick')){
        . (Join-Path $PSScriptRoot 'lib/test-selection.ps1')
    }
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
            # Each lane resumes at its torn chunk, then its tail (new-New), then wraps to new-Never.
            Check ($oldest -ceq 'session-runner,agents-pty,e2e,pty-host,antiphon' -and ($ids[0..4] -join ',') -ceq 'new-Red,new-Red,new-Red,new-Red,new-Red' -and ($ids[20..24] -join ',') -ceq 'new-Passed,new-Passed,new-Passed,new-Passed,new-Passed' -and ($ids[5..9] -join ',') -ceq 'new-Torn,new-Torn,new-Torn,new-Torn,new-Torn' -and ($ids[10..13] -join ',') -ceq 'new-New,new-Never,new-New,new-Never' -and @($inputRecord.History.members).Count -eq 6 -and @($resolved.members).Count -eq 5 -and @($resolved.unresolved).Count -eq 1) ($ids -join ',')
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
            $rankedBefore=Resolve-NightlyPriorFailures $good.History $suites Full
            $wanted=@(foreach($s in $suites){foreach($c in @($s.chunks)){$s.id+'/'+$c.id}})
            foreach($s in $suites){foreach($c in $s.chunks){$c.classes=@('Synthetic.Gone.Class')}}
            $unmapped=New-NightlyDispatchSchedule -PreparedSuites $suites -PriorityMembers @($rankedBefore.members) -Unresolved @($rankedBefore.unresolved) -PreviousRun $good.PreviousRun -CandidateSha ('b'*40) -NowUtc $now
            $unmappedOrder=@($unmapped.entries|ForEach-Object {$_.suite+'/'+$_.chunk})
            $unmappedOriginal=((($unmappedOrder -join ',') -ceq ($wanted -join ',')) -and @($unmapped.entries|Where-Object priorFailure).Count -gt 0)
            Check ($oldSource.FallbackReason -eq 'seed-stale' -and $null -eq $bad.History -and (Schedule $bad).entries.Count -eq 25 -and $received.FallbackReason -eq 'seed-stale' -and $null -eq $received.History -and $unmapped.orderingReason -eq 'seed-plan-unmapped' -and $unmappedOriginal -and $unmapped.entries.Count -eq 25) 'stale or wholly unmappable seed restores complete original plan'
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
            $directRoot=Join-Path $root 'direct-link-logs'
            $directLay=New-SeedSiblingLayout $directRoot $runId '2026-10-30-0824-d47848cc'
            $realTiming=Join-Path ([IO.Path]::GetDirectoryName($directLay.Timing)) ('timing-real-'+$runId)
            Move-Item -LiteralPath $directLay.Timing -Destination $realTiming
            New-Item -ItemType SymbolicLink -Path $directLay.Timing -Target $realTiming | Out-Null
            $directMsg=''
            try { Resolve-NightlyStoppedTimingRoot $directLay.Run $directLay.Timing $runId | Out-Null }
            catch { $directMsg=$_.Exception.Message }
            $directOk=($directMsg -like '*evidence-link*')
            Check ($explicitOk -and $autoOk -and $outside -and $symlink -and $other -and $directOk) ('explicit='+$explicitOk+' auto='+$autoOk+' outside='+$outside+' symlink='+$symlink+' other='+$other+' direct='+$directMsg)
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
            $script:NightlySeedPlatformSeam=$null
            $script:NightlySeedUnixModeCalls=0
            $script:NightlySeedWindowsBranchRan=$false
            $script:NightlySeedCustodyNotApplicable=0
            $script:NightlySeedCustodyNote=''
            $script:NightlySeedDenialBranch=''
            $deniedOk=$false;$deniedDetail=''
            $deniedRestore=$null
            try {
                $deniedRestore=Enter-SeedUnreadableTiming $denied
                if($deniedRestore){
                    $deniedOut=Join-Path $root 'denied-seed.json'
                    try {
                        [void](Export-NightlyStoppedRunSeed -RunDirectory $run -OutputPath $deniedOut -PassThru)
                        $deniedDoc=Read-NightlyStoppedRunSeed $deniedOut
                        $row=@($deniedDoc.chunks|Where-Object {$_.suite -ceq 'antiphon' -and $_.chunk -ceq 'all-group-2'})[0]
                        $deniedOk=(-not $row.completed -and [string]$row.unfinishedReason -like '*timing*')
                        $deniedDetail=[string]$row.unfinishedReason+' branch='+$script:NightlySeedDenialBranch
                    } catch { $deniedDetail=$_.Exception.Message }
                } else {
                    $deniedOk=($script:NightlySeedDenialBranch -ceq 'windows-acl' -and [int]$script:NightlySeedCustodyNotApplicable -ge 1 -and $script:NightlySeedCustodyNote -ceq $script:SeedCustodyNotApplicableLine)
                    $deniedDetail='not-applicable count='+[int]$script:NightlySeedCustodyNotApplicable
                }
            } finally { if($deniedRestore){ & $deniedRestore } }
            $windowsOk=$true;$windowsDetail='native-windows'
            $naOk=$true;$naDetail='native-windows'
            if($script:NightlySeedDenialBranch -ceq 'unix-mode'){
                $savedCalls=[int]$script:NightlySeedUnixModeCalls
                $script:NightlySeedWindowsBranchRan=$false
                $script:NightlySeedPlatformSeam=@{
                    UnixModesSupported=$false
                    WindowsAcl={
                        param($Target)
                        $prior=[IO.File]::GetUnixFileMode($Target)
                        [IO.File]::SetUnixFileMode($Target,[IO.UnixFileMode]::None)
                        return @{ Applied=$true; Restore={ [IO.File]::SetUnixFileMode($Target,$prior) }.GetNewClosure() }
                    }
                }
                $windowsRestore=$null
                try {
                    $windowsRestore=Enter-SeedUnreadableTiming $denied
                    if(-not $script:NightlySeedWindowsBranchRan){ throw 'windows-branch-did-not-run' }
                    if([int]$script:NightlySeedUnixModeCalls -ne $savedCalls){ throw 'unix-mode-used-on-windows-branch' }
                    if($script:NightlySeedDenialBranch -cne 'windows-acl'){ throw 'windows-branch-not-selected' }
                    if(-not $windowsRestore){ throw 'windows-custody-not-applied' }
                    $windowsOut=Join-Path $root 'windows-denied-seed.json'
                    [void](Export-NightlyStoppedRunSeed -RunDirectory $run -OutputPath $windowsOut -PassThru)
                    $windowsDoc=Read-NightlyStoppedRunSeed $windowsOut
                    $row=@($windowsDoc.chunks|Where-Object {$_.suite -ceq 'antiphon' -and $_.chunk -ceq 'all-group-2'})[0]
                    $windowsOk=(-not $row.completed -and [string]$row.unfinishedReason -like '*timing*')
                    $windowsDetail=[string]$row.unfinishedReason
                } catch { $windowsOk=$false; $windowsDetail=$_.Exception.Message }
                finally { if($windowsRestore){ & $windowsRestore }; $script:NightlySeedPlatformSeam=$null }
                $script:NightlySeedWindowsBranchRan=$false
                $naBefore=[int]$script:NightlySeedCustodyNotApplicable
                $script:NightlySeedPlatformSeam=@{ UnixModesSupported=$false }
                $naRestore=$null;$naThrew=$false;$naPrinted=$false
                try {
                    $captured=@(Enter-SeedUnreadableTiming $denied 6>&1)
                    foreach($item in $captured){ if([string]$item -ceq $script:SeedCustodyNotApplicableLine){ $naPrinted=$true } }
                    $naRestore=$captured | Where-Object { $_ -is [scriptblock] } | Select-Object -First 1
                } catch { $naThrew=$true; $naDetail=$_.Exception.Message }
                finally { if($naRestore){ & $naRestore }; $script:NightlySeedPlatformSeam=$null }
                $naOk=(-not $naThrew -and $naPrinted -and $script:NightlySeedWindowsBranchRan -and [int]$script:NightlySeedCustodyNotApplicable -eq ($naBefore+1) -and $script:NightlySeedCustodyNote -ceq $script:SeedCustodyNotApplicableLine)
                if(-not $naThrew){ $naDetail='printed='+$naPrinted+' count='+[int]$script:NightlySeedCustodyNotApplicable }
            }
            $tamper=New-SeedSiblingLayout (Join-Path $root 'tamper-logs') 'd47848cce896471db1cc8ca346707cad' '2026-10-30-0824-d47848cc'
            Write-SeedSummary $tamper.Run $tamper.Timing 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee' $sha
            $tamperMsg=''
            try { [void](Export-NightlyStoppedRunSeed -RunDirectory $tamper.Run -OutputPath (Join-Path $root 'tamper-seed.json') -SourceCandidateId 'rc-synthetic-old' -SourceSha $sha -PassThru) }
            catch { $tamperMsg=$_.Exception.Message }
            $tamperOk=($tamperMsg -like '*seed-timing-run-mismatch*' -or $tamperMsg -like '*seed-plan-run-mismatch*' -or $tamperMsg -like '*seed-summary*')
            Check ($lockedOk -and $changedOk -and $openOk -and $deniedOk -and $tamperOk -and $windowsOk -and $naOk) ('locked='+$lockedDetail+'; changed='+$changedDetail+'; open='+$openDetail+'; denied='+$deniedDetail+'; tamper='+$tamperMsg+'; windows='+$windowsDetail+'; na='+$naDetail)
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
        'DigestGuardLeavesSeedOrder' {
            function Test-Permutation($Schedule,$Prepared){
                $expected=@(foreach($s in $Prepared){foreach($c in @($s.chunks)){$s.id+'/'+$c.id}})|Sort-Object
                $actual=@($Schedule.entries|ForEach-Object {$_.suite+'/'+$_.chunk})|Sort-Object
                return ($expected.Count -eq $actual.Count -and @($actual|Select-Object -Unique).Count -eq $actual.Count -and ($expected -join ',') -ceq ($actual -join ','))
            }
            $inputRecord=Ingress -Accept
            $seed=$inputRecord.PreviousRun
            $kept=Schedule $inputRecord
            $cross=New-NightlyDispatchSchedule -PreparedSuites $suites -PreviousRun $seed -CandidateSha ('c'*40) -NowUtc $now
            $bare=$seed.PSObject.Copy();$bare.PSObject.Properties.Remove('seedKind')
            $ranked=Resolve-NightlyPriorFailures $seed $suites Full
            $members=@($ranked.members)
            $canonicalOrder=@(foreach($s in $suites){foreach($c in @($s.chunks)){$s.id+'/'+$c.id}})
            function Test-Original($Schedule){
                $actual=@($Schedule.entries|ForEach-Object {$_.suite+'/'+$_.chunk})
                return (@($Schedule.entries|Where-Object priorFailure).Count -gt 0 -and ($actual -join ',') -ceq ($canonicalOrder -join ','))
            }
            $missingMarker=New-NightlyDispatchSchedule -PreparedSuites $suites -PriorityMembers $members -PreviousRun $bare -CandidateSha ('b'*40) -NowUtc $now
            $foreign=$seed.PSObject.Copy();$foreign.seedKind='operator-seed-forged'
            $foreignSchedule=New-NightlyDispatchSchedule -PreparedSuites $suites -PriorityMembers $members -PreviousRun $foreign -CandidateSha ('b'*40) -NowUtc $now
            $tiny=@([pscustomobject]@{id='antiphon';chunks=@([pscustomobject]@{id='Ordinary';classes=@('C.B');nodes=@()},[pscustomobject]@{id='Priority';classes=@('C.A');nodes=@()});nodes=@();exclusions=@()})
            $plain=New-NightlyDispatchSchedule -PreparedSuites $tiny -NowUtc $now
            $forgedChunks=[object[]]@(
                [pscustomobject]@{suite='antiphon';chunk='seed-ordinary';classes=[object[]]@('C.B');completed=$true;started=$true;lastRunAtUtc=$now.AddHours(-2).ToString('o')},
                [pscustomobject]@{suite='antiphon';chunk='seed-priority';classes=[object[]]@('C.A');completed=$true;started=$true;lastRunAtUtc=$now.AddHours(-3).ToString('o')}
            )
            $forgedFailed=[object[]]@(
                [pscustomobject]@{suite='antiphon';chunk='seed-ordinary';className='C.B';method='Run';outcome='Failed'},
                [pscustomobject]@{suite='antiphon';chunk='seed-ordinary';className='C.B';method='RunAgain';outcome='Failed'}
            )
            $normal=[pscustomobject]@{
                schemaVersion=1;liveness='closed';sha=('b'*40);completedAt=$now.AddHours(-1).ToString('o');canonicalPlanDigest=$plain.canonicalPlanDigest
                seedKind='operator-seed-from-stopped-run';sourceSha=('b'*40);sourceRunId='synthetic-forged';sourceObservedAtUtc=$now.AddHours(-2).ToString('o')
                suites=@([pscustomobject]@{id='antiphon';chunk='Priority';result='FAIL';exitCode=1},[pscustomobject]@{id='antiphon';chunk='Ordinary';result='pass';exitCode=0})
                chunks=$forgedChunks;failedTests=$forgedFailed
            }
            if($normal.chunks -isnot [array] -or $normal.failedTests -isnot [array] -or $normal.chunks.Count -lt 2){ throw 'forged normal record lost its arrays' }
            $forged=New-NightlyDispatchSchedule -PreparedSuites $tiny -PreviousRun $normal -CandidateSha ('b'*40) -NowUtc $now
            $stripped=$normal.PSObject.Copy();$stripped.PSObject.Properties.Remove('canonicalPlanDigest')
            $forgedMissing=New-NightlyDispatchSchedule -PreparedSuites $tiny -PreviousRun $stripped -CandidateSha ('b'*40) -NowUtc $now
            $noSha=$seed.PSObject.Copy();$noSha.PSObject.Properties.Remove('sourceSha')
            $shaSchedule=New-NightlyDispatchSchedule -PreparedSuites $suites -PriorityMembers $members -PreviousRun $noSha -CandidateSha ('b'*40) -NowUtc $now
            $noRun=$seed.PSObject.Copy();$noRun.PSObject.Properties.Remove('sourceRunId')
            $runSchedule=New-NightlyDispatchSchedule -PreparedSuites $suites -PriorityMembers $members -PreviousRun $noRun -CandidateSha ('b'*40) -NowUtc $now
            $hit=@($seed.chunks|Where-Object { @($_.classes).Count -gt 0 })[0]
            $regrouped=@([pscustomobject]@{
                id='antiphon'
                chunks=@(
                    [pscustomobject]@{id='all-group-1';classes=@('Synthetic.antiphon.Passed','Synthetic.antiphon.Never');nodes=@()},
                    [pscustomobject]@{id='held-red';classes=@('Synthetic.antiphon.Red');nodes=@()}
                )
                nodes=@(
                    [pscustomobject]@{Uid='Synthetic.antiphon.Passed';ClassName='Synthetic.antiphon.Passed';Method='Run';Type='Passed';Categories=@()},
                    [pscustomobject]@{Uid='Synthetic.antiphon.Never';ClassName='Synthetic.antiphon.Never';Method='Run';Type='Never';Categories=@()},
                    [pscustomobject]@{Uid='Synthetic.antiphon.Red';ClassName='Synthetic.antiphon.Red';Method='Run';Type='Red';Categories=@()}
                )
                exclusions=@()
            })
            $regroupMembers=@((Resolve-NightlyPriorFailures $seed $regrouped Full).members)
            $regroup=New-NightlyDispatchSchedule -PreparedSuites $regrouped -PriorityMembers $regroupMembers -PreviousRun $seed -CandidateSha ('b'*40) -NowUtc $now
            $same=@([pscustomobject]@{id=$hit.suite;chunks=@([pscustomobject]@{id=$hit.chunk;classes=@($hit.classes);nodes=@()});nodes=@();exclusions=@()})
            $sameSchedule=New-NightlyDispatchSchedule -PreparedSuites $same -PreviousRun $seed -CandidateSha ('b'*40) -NowUtc $now
            $ok=$kept.orderingReason -like 'operator-seed-order*' -and @($kept.entries[0..4].chunk)[0] -ceq 'new-Red' -and (Test-Permutation $kept $suites)
            $ok=$ok -and $cross.orderingReason -like 'operator-seed-order*' -and (Test-Permutation $cross $suites)
            $ok=$ok -and $missingMarker.orderingReason -ceq 'previous-run-plan-digest-missing' -and (Test-Original $missingMarker)
            $ok=$ok -and $foreignSchedule.orderingReason -ceq 'operator-seed-kind-foreign' -and (Test-Original $foreignSchedule)
            $ok=$ok -and $forged.orderingReason -ceq 'previous-run-order' -and $forged.entries[0].chunk -ceq 'Priority' -and (Test-Permutation $forged $tiny)
            $ok=$ok -and $forgedMissing.orderingReason -ceq 'previous-run-plan-digest-missing' -and $forgedMissing.entries[0].chunk -ceq 'Ordinary' -and (Test-Permutation $forgedMissing $tiny)
            $ok=$ok -and $shaSchedule.orderingReason -ceq 'seed-candidate-missing' -and (Test-Original $shaSchedule)
            $ok=$ok -and $runSchedule.orderingReason -ceq 'seed-run-missing' -and (Test-Original $runSchedule)
            $ok=$ok -and $regroup.orderingReason -like 'operator-seed-order*' -and $regroup.entries[0].chunk -ceq 'held-red' -and $regroup.entries[0].priorFailure -and $regroup.entries.Count -eq 2 -and (Test-Permutation $regroup $regrouped)
            $ok=$ok -and $sameSchedule.orderingReason -like 'operator-seed-order*' -and (Test-Permutation $sameSchedule $same)
            Check $ok ('kept='+$kept.orderingReason+'; missing='+$missingMarker.orderingReason+'; foreign='+$foreignSchedule.orderingReason+'; forged='+$forged.orderingReason+'/'+$forged.entries[0].chunk+'; forgedMissing='+$forgedMissing.orderingReason+'/'+$forgedMissing.entries[0].chunk+'; sha='+$shaSchedule.orderingReason+'; run='+$runSchedule.orderingReason+'; regroup='+$regroup.orderingReason+'/'+$regroup.entries[0].chunk+'; same='+$sameSchedule.orderingReason)
        }
        'RealPackerRegroupKeepsFailedFirst' {
            $alpha='Antiphon.Tests.PackWitness.Alpha'
            $beta='Antiphon.Tests.PackWitness.Beta'
            $gamma='Antiphon.Tests.PackWitness.Gamma'
            $never='Antiphon.Tests.PackWitness.NeverSeen'
            $delta='Antiphon.Tests.PackWitness.Delta'
            $zeta='Antiphon.Tests.PackWitness.Zeta'
            $stamp=$now.AddHours(-2).ToString('o')
            $script:packSeedN=0
            function Convert-Counts($Counts){
                $nodes=New-Object System.Collections.Generic.List[object]
                $leaves=New-Object System.Collections.Generic.List[object]
                $i=0
                foreach($name in @($Counts.Keys|Sort-Object)){
                    $i++
                    $type=([string]$name).Split('.')[-1]
                    for($n=1;$n -le [int]$Counts[$name];$n++){
                        [void]$nodes.Add([pscustomobject]@{ClassName=[string]$name;Method=('M'+$n);Type=$type;Categories=@();Uid=([string]$name+'/'+$n)})
                    }
                    [void]$leaves.Add([pscustomobject]@{id=('all-profile-'+$i);classes=@([string]$name);exactFilter='';nodes=@()})
                }
                return @(ConvertTo-NightlyProfileLaunchGroups -Leaves @($leaves.ToArray()) -DiscoveryNodes @($nodes.ToArray()))
            }
            function Write-PackSeed($Rows,$Failed){
                $payload=[ordered]@{sourceObservedAtUtc=$stamp;chunks=@($Rows);failedTests=@($Failed)}
                $envelope=[ordered]@{format='antiphon-nightly-stopped-seed-v1';seedKind='operator-seed-from-stopped-run';sourceRunId='synthetic-packer';sourceCandidateId='rc-synthetic-old';sourceSha=('c'*40);createdAtUtc=$stamp;payload=($payload|ConvertTo-Json -Depth 20 -Compress);contentHash=''}
                $envelope.contentHash=Get-NightlyStoppedSeedHash $envelope
                $script:packSeedN++
                $path=Join-Path $root ('pack-seed-'+$script:packSeedN+'.json')
                [IO.File]::WriteAllText($path,($envelope|ConvertTo-Json -Depth 12))
                return Read-NightlyStoppedRunSeed $path
            }
            function New-Suite($Groups){
                # A group with 'methods' is a split chunk like the real packer's: its own disjoint node list.
                $chunks=@(foreach($g in @($Groups)){
                    $chunkNodes=@()
                    if($g.PSObject.Properties.Name -contains 'methods'){$chunkNodes=@(foreach($m in @($g.methods)){[pscustomobject]@{Uid=([string]$g.classes[0]+'/'+$m);ClassName=[string]$g.classes[0];Method=[string]$m;Type=([string]$g.classes[0]).Split('.')[-1];Categories=@()}})}
                    [pscustomobject]@{id=[string]$g.id;classes=@($g.classes);nodes=$chunkNodes}
                })
                $nodes=@(foreach($g in @($Groups)){
                    if($g.PSObject.Properties.Name -contains 'methods'){foreach($m in @($g.methods)){[pscustomobject]@{Uid=([string]$g.classes[0]+'/'+$m);ClassName=[string]$g.classes[0];Method=[string]$m;Type=([string]$g.classes[0]).Split('.')[-1];Categories=@()}}}
                    else{foreach($c in @($g.classes)){[pscustomobject]@{Uid=[string]$c;ClassName=[string]$c;Method='Run';Type=([string]$c).Split('.')[-1];Categories=@()}}}
                })
                return @([pscustomobject]@{id='antiphon';chunks=$chunks;nodes=$nodes;exclusions=@()})
            }
            function Order-Plan($Doc,$Groups){
                $prepared=New-Suite $Groups
                $resolved=Resolve-NightlyPriorFailures $Doc $prepared Full
                return New-NightlyDispatchSchedule -PreparedSuites $prepared -PriorityMembers @($resolved.members) -PreviousRun $Doc -CandidateSha ('b'*40) -NowUtc $now
            }
            $oldCounts=@{$alpha=1700;$beta=100;$gamma=100}
            $newCounts=@{$alpha=1851;$beta=100;$gamma=100}
            $old=@(Convert-Counts $oldCounts)
            $packed=@(Convert-Counts $newCounts)
            $oldMethods=0;foreach($v in $oldCounts.Values){$oldMethods+=[int]$v}
            $newMethods=0;foreach($v in $newCounts.Values){$newMethods+=[int]$v}
            $moved=$false
            foreach($group in @($old)){
                $other=@($packed|Where-Object { $_.id -ceq $group.id })
                $left=((@($group.classes)|ForEach-Object {[string]$_}|Sort-Object) -join ',')
                $right=$(if($other.Count -eq 1){((@($other[0].classes)|ForEach-Object {[string]$_}|Sort-Object) -join ',')}else{''})
                if($left -cne $right){$moved=$true}
            }
            $betaOld=[string]@($old|Where-Object { @($_.classes) -ccontains $beta })[0].id
            $rows=New-Object System.Collections.Generic.List[object]
            foreach($group in @($old)){[void]$rows.Add([ordered]@{suite='antiphon';chunk=[string]$group.id;classes=@($group.classes);completed=$true;started=$true;lastRunAtUtc=$stamp})}
            [void]$rows.Add([ordered]@{suite='antiphon';chunk='seed-only';classes=@($zeta);completed=$true;started=$true;lastRunAtUtc=$stamp})
            $failed=@(
                [ordered]@{suite='antiphon';chunk=$betaOld;className=$beta;method='M1';outcome='Failed'},
                [ordered]@{suite='antiphon';chunk=$betaOld;className=$beta;method='M2';outcome='Failed'}
            )
            $doc=Write-PackSeed $rows.ToArray() $failed
            $planned=@($packed)+@([pscustomobject]@{id='never-added';classes=@($never)})
            $betaNew=[string]@($planned|Where-Object { @($_.classes) -ccontains $beta })[0].id
            $schedule=Order-Plan $doc $planned
            $again=Order-Plan $doc $planned
            $ids=@($schedule.entries.chunk)
            $againIds=@($again.entries.chunk)
            $expected=@($planned|ForEach-Object {'antiphon/'+$_.id})|Sort-Object
            $actual=@($schedule.entries|ForEach-Object {$_.suite+'/'+$_.chunk})|Sort-Object
            $packOk=($moved -and ($newMethods-$oldMethods) -eq 151 -and $schedule.orderingReason -like 'operator-seed-order*' -and $ids[0] -ceq $betaNew -and $schedule.entries[0].priorFailure -and $ids -cnotcontains 'seed-only' -and $ids -ccontains 'never-added' -and ($ids -join ',') -ceq ($againIds -join ',') -and ($expected -join ',') -ceq ($actual -join ','))
            $splitGroups=@(
                [pscustomobject]@{id='passed-only';classes=@($alpha)},
                [pscustomobject]@{id='mixed';classes=@($beta,$gamma)},
                [pscustomobject]@{id='split-a';classes=@($beta);methods=@('M1')},
                [pscustomobject]@{id='split-b';classes=@($beta);methods=@('M2','M3')},
                [pscustomobject]@{id='split-c';classes=@($beta);methods=@('M4')},
                [pscustomobject]@{id='unfinished';classes=@($delta)},
                [pscustomobject]@{id='never';classes=@($never)}
            )
            $splitRows=@(
                [ordered]@{suite='antiphon';chunk='old-alpha';classes=@($alpha);completed=$true;started=$true;lastRunAtUtc=$stamp},
                [ordered]@{suite='antiphon';chunk='old-beta';classes=@($beta);completed=$true;started=$true;lastRunAtUtc=$stamp},
                [ordered]@{suite='antiphon';chunk='old-gamma';classes=@($gamma);completed=$true;started=$true;lastRunAtUtc=$stamp},
                [ordered]@{suite='antiphon';chunk='old-delta';classes=@($delta);completed=$false;started=$true;lastRunAtUtc=$stamp},
                [ordered]@{suite='antiphon';chunk='old-zeta';classes=@($zeta);completed=$true;started=$true;lastRunAtUtc=$stamp}
            )
            $splitFailed=@(
                [ordered]@{suite='antiphon';chunk='old-beta';className=$beta;method='M1';outcome='Failed'},
                [ordered]@{suite='antiphon';chunk='old-beta';className=$beta;method='M2';outcome='Failed'}
            )
            $splitDoc=Write-PackSeed $splitRows $splitFailed
            $split=Order-Plan $splitDoc $splitGroups
            $splitIds=@($split.entries.chunk) -join ','
            $byId=@{}
            foreach($entry in @($split.entries)){$byId[$entry.chunk]=$entry}
            # mixed has no node list, so it holds the whole failed class and leads. split-a/b/c carry disjoint
            # node lists like the real packer's split chunks: a and b hold failed M1/M2 and lead, c holds only M4
            # and follows as a sibling. The torn (unfinished) chunk resumes the lane before the never-seen one.
            $ruleOk=($split.orderingReason -like 'operator-seed-order*' -and $splitIds -ceq 'mixed,split-a,split-b,unfinished,never,split-c,passed-only' -and $byId['mixed'].priorFailure -and $byId['split-a'].priorFailure -and $byId['split-b'].priorFailure -and -not $byId['unfinished'].priorFailure -and -not $byId['never'].priorFailure -and -not $byId['passed-only'].priorFailure -and @($split.entries|ForEach-Object {$_.chunk}) -cnotcontains 'old-zeta')
            Check ($packOk -and $ruleOk) ('pack='+$schedule.orderingReason+'/'+($ids -join ',')+'; moved='+$moved+'; delta='+($newMethods-$oldMethods)+'; beta='+$betaOld+'->'+$betaNew+'; split='+$split.orderingReason+'/'+$splitIds)
        }
        'FailedMethodBands' {
            $script:bandN=0
            $stamp=$now.AddHours(-3).ToString('o')
            $openStamp=$now.AddHours(-2).ToString('o')
            function Write-BandSeed($Rows,$Failed){
                $payload=[ordered]@{sourceObservedAtUtc=$stamp;chunks=@($Rows);failedTests=@($Failed)}
                $envelope=[ordered]@{format='antiphon-nightly-stopped-seed-v1';seedKind='operator-seed-from-stopped-run';sourceRunId='synthetic-method';sourceCandidateId='rc-synthetic-old';sourceSha=('c'*40);createdAtUtc=$stamp;payload=($payload|ConvertTo-Json -Depth 20 -Compress);contentHash=''}
                $envelope.contentHash=Get-NightlyStoppedSeedHash $envelope
                $script:bandN++
                $path=Join-Path $root ('band-seed-'+$script:bandN+'.json')
                [IO.File]::WriteAllText($path,($envelope|ConvertTo-Json -Depth 12))
                return Read-NightlyStoppedRunSeed $path
            }
            function New-BandChunk([string]$Id,[string]$Class,[string[]]$Methods,[bool]$OmitNodes){
                $chunk=[pscustomobject]@{id=$Id;classes=@($Class)}
                if(-not $OmitNodes){
                    $nodes=@(foreach($method in @($Methods)){[pscustomobject]@{Uid=($Class+'/'+$method);ClassName=$Class;Method=$method;Type='T';Categories=@()}})
                    $chunk|Add-Member -NotePropertyName nodes -NotePropertyValue $nodes
                }
                return $chunk
            }
            function Invoke-BandOrder($Doc,$Chunks){
                $suiteNodes=New-Object System.Collections.Generic.List[object]
                $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach($chunk in @($Chunks)){
                    $class=[string]@($chunk.classes)[0]
                    $chunkNodes=@()
                    if(($chunk.PSObject.Properties.Name -contains 'nodes') -and $null -ne $chunk.nodes){$chunkNodes=@($chunk.nodes)}
                    if($chunkNodes.Count -eq 0){
                        $key=$class+'/Run'
                        if($seen.Add($key)){[void]$suiteNodes.Add([pscustomobject]@{Uid=$key;ClassName=$class;Method='Run';Type='T';Categories=@()})}
                    }else{
                        foreach($node in @($chunkNodes)){if($seen.Add([string]$node.Uid)){[void]$suiteNodes.Add($node)}}
                    }
                }
                $suite=@([pscustomobject]@{id='antiphon';chunks=@($Chunks);nodes=@($suiteNodes.ToArray());exclusions=@()})
                $resolved=Resolve-NightlyPriorFailures $Doc $suite Full
                return New-NightlyDispatchSchedule -PreparedSuites $suite -PriorityMembers @($resolved.members) -PreviousRun $Doc -CandidateSha ('b'*40) -NowUtc $now
            }
            function ConvertTo-BandIds($Schedule){return (@($Schedule.entries|ForEach-Object {$_.chunk}) -join ',')}
            $split='Synthetic.Method.Split'
            $torn='Synthetic.Method.Torn'
            $ok='Synthetic.Method.Ok'
            $chunks=@(
                (New-BandChunk 's0' $split @('M0') $false),
                (New-BandChunk 's1' $split @('M1') $false),
                (New-BandChunk 's2' $split @('M2') $false),
                (New-BandChunk 's3' $split @('M3') $false),
                (New-BandChunk 's4' $split @('M4(case)') $false),
                (New-BandChunk 's5' $split @('M5') $false),
                (New-BandChunk 'whole' $split @() $false),
                (New-BandChunk 'torn' $torn @() $false),
                (New-BandChunk 'never' 'Synthetic.Method.Never' @() $false),
                (New-BandChunk 'ok' $ok @() $false)
            )
            $rows=@(
                [ordered]@{suite='antiphon';chunk='seed-split';classes=@($split);completed=$true;started=$true;lastRunAtUtc=$stamp},
                [ordered]@{suite='antiphon';chunk='seed-torn';classes=@($torn);completed=$false;started=$true;lastRunAtUtc=$openStamp},
                [ordered]@{suite='antiphon';chunk='seed-ok';classes=@($ok);completed=$true;started=$true;lastRunAtUtc=$stamp}
            )
            $failed=@(
                [ordered]@{suite='antiphon';chunk='seed-split';className=$split;method='M1';outcome='Failed'},
                [ordered]@{suite='antiphon';chunk='seed-split';className=$split;method='M4';outcome='Failed'}
            )
            $doc=Write-BandSeed $rows $failed
            $splitIds=ConvertTo-BandIds (Invoke-BandOrder $doc $chunks)
            # The torn chunk resumes the lane, so it runs before the never-seen chunk.
            $splitOk=($splitIds -ceq 's1,s4,whole,torn,never,s0,s2,s3,s5,ok')
            $red='Synthetic.Method.Red'
            $missingChunks=@(
                (New-BandChunk 'bare' $red @() $true),
                (New-BandChunk 'sib' $red @('M9') $false),
                (New-BandChunk 'open' $torn @() $false),
                (New-BandChunk 'ok' $ok @() $false)
            )
            $missingRows=@(
                [ordered]@{suite='antiphon';chunk='seed-red';classes=@($red);completed=$true;started=$true;lastRunAtUtc=$stamp},
                [ordered]@{suite='antiphon';chunk='seed-torn';classes=@($torn);completed=$false;started=$true;lastRunAtUtc=$openStamp},
                [ordered]@{suite='antiphon';chunk='seed-ok';classes=@($ok);completed=$true;started=$true;lastRunAtUtc=$stamp}
            )
            $missingFailed=@(
                [ordered]@{suite='antiphon';chunk='seed-red';className=$red;method='Run';outcome='Failed'},
                [ordered]@{suite='antiphon';chunk='seed-red';className=$red;method='RunAgain';outcome='Timeout'}
            )
            $missingDoc=Write-BandSeed $missingRows $missingFailed
            $missingIds=ConvertTo-BandIds (Invoke-BandOrder $missingDoc $missingChunks)
            $missingOk=($missingIds -ceq 'bare,open,sib,ok')
            $other='Synthetic.Method.Other'
            $vanishedChunks=@(
                (New-BandChunk 'r0' $red @('M0') $false),
                (New-BandChunk 'r1' $red @('M1') $false),
                (New-BandChunk 'r2' $red @('M2') $false),
                (New-BandChunk 'other' $other @() $false),
                (New-BandChunk 'open' $torn @() $false)
            )
            $vanishedRows=@(
                [ordered]@{suite='antiphon';chunk='seed-red';classes=@($red);completed=$true;started=$true;lastRunAtUtc=$stamp},
                [ordered]@{suite='antiphon';chunk='seed-other';classes=@($other);completed=$true;started=$true;lastRunAtUtc=$stamp},
                [ordered]@{suite='antiphon';chunk='seed-torn';classes=@($torn);completed=$false;started=$true;lastRunAtUtc=$openStamp}
            )
            $vanishedFailed=@(
                [ordered]@{suite='antiphon';chunk='seed-red';className=$red;method='Gone';outcome='Failed'},
                [ordered]@{suite='antiphon';chunk='seed-other';className=$other;method='Run';outcome='Error'}
            )
            $vanishedDoc=Write-BandSeed $vanishedRows $vanishedFailed
            $vanishedIds=ConvertTo-BandIds (Invoke-BandOrder $vanishedDoc $vanishedChunks)
            $vanishedOk=($vanishedIds -ceq 'other,open,r0,r1,r2')
            $movedChunks=@(
                (New-BandChunk 'old' $split @('M0') $false),
                (New-BandChunk 'new' $split @('M1','M2') $false)
            )
            $movedRows=@([ordered]@{suite='antiphon';chunk='old';classes=@($split);completed=$true;started=$true;lastRunAtUtc=$stamp})
            $movedFailed=@(
                [ordered]@{suite='antiphon';chunk='old';className=$split;method='M1';outcome='Failed'},
                [ordered]@{suite='antiphon';chunk='old';className=$split;method='M2';outcome='Aborted'}
            )
            $movedDoc=Write-BandSeed $movedRows $movedFailed
            $movedIds=ConvertTo-BandIds (Invoke-BandOrder $movedDoc $movedChunks)
            $movedOk=($movedIds -ceq 'new,old')
            $blank=$doc.PSObject.Copy();$blank.PSObject.Properties.Remove('sourceSha')
            $blankSchedule=Invoke-BandOrder $blank $chunks
            $badSha=$doc.PSObject.Copy();$badSha.sourceSha='G'*40
            $badSchedule=Invoke-BandOrder $badSha $chunks
            $noRun=$doc.PSObject.Copy();$noRun.PSObject.Properties.Remove('sourceRunId')
            $noRunSchedule=Invoke-BandOrder $noRun $chunks
            $canonical=@($chunks|ForEach-Object {$_.id}) -join ','
            $corruptOk=($blankSchedule.orderingReason -ceq 'seed-candidate-missing' -and (ConvertTo-BandIds $blankSchedule) -ceq $canonical -and $badSchedule.orderingReason -ceq 'seed-candidate-malformed' -and (ConvertTo-BandIds $badSchedule) -ceq $canonical -and $noRunSchedule.orderingReason -ceq 'seed-run-missing' -and (ConvertTo-BandIds $noRunSchedule) -ceq $canonical)
            Check ($splitOk -and $missingOk -and $vanishedOk -and $movedOk -and $corruptOk) ('split='+$splitIds+'; missing='+$missingIds+'; vanished='+$vanishedIds+'; moved='+$movedIds+'; blank='+$blankSchedule.orderingReason+'; sha='+$badSchedule.orderingReason+'; run='+$noRunSchedule.orderingReason)
        }
        'RealPackerFailedMethodFollows' {
            $class='Antiphon.Tests.PackWitness.LandProbe'
            $unfinished='Antiphon.Tests.PackWitness.Delta'
            $never='Antiphon.Tests.OtherPack.NeverSeen'
            $stamp=$now.AddHours(-3).ToString('o')
            function New-ProbeNodes([string]$Bare,[string[]]$Methods,[string[]]$Categories){
                $type=($Bare.Split('.')[-1])
                $nodes=New-Object System.Collections.Generic.List[object]
                foreach($method in @($Methods)){
                    [void]$nodes.Add([pscustomobject]@{ClassName=$Bare;Method=[string]$method;Type=$type;Categories=@($Categories);Uid=($Bare+'/'+$method)})
                }
                return @($nodes.ToArray())
            }
            function Convert-ProbePlan([object[]]$Nodes,[string[]]$Classes){
                $leaves=@(foreach($name in @($Classes)){[pscustomobject]@{id=('leaf-'+$name.Split('.')[-1]);classes=@($name);exactFilter='';nodes=@()}})
                return @(ConvertTo-NightlyProfileLaunchGroups -Leaves $leaves -DiscoveryNodes $Nodes)
            }
            function Write-ProbeSeed($Rows,$Failed){
                $payload=[ordered]@{sourceObservedAtUtc=$stamp;chunks=@($Rows);failedTests=@($Failed)}
                $envelope=[ordered]@{format='antiphon-nightly-stopped-seed-v1';seedKind='operator-seed-from-stopped-run';sourceRunId='synthetic-packer-method';sourceCandidateId='rc-synthetic-old';sourceSha=('c'*40);createdAtUtc=$stamp;payload=($payload|ConvertTo-Json -Depth 20 -Compress);contentHash=''}
                $envelope.contentHash=Get-NightlyStoppedSeedHash $envelope
                $path=Join-Path $root 'packer-method-seed.json'
                [IO.File]::WriteAllText($path,($envelope|ConvertTo-Json -Depth 12))
                return Read-NightlyStoppedRunSeed $path
            }
            $oldMethods=@(0..319|ForEach-Object { 'M'+$_.ToString('000') })
            $oldMethods[200]='M200(case)'
            $added=@(0..19|ForEach-Object { 'A'+$_.ToString('00') })
            $newMethods=@($added)+@($oldMethods)
            $oldNodes=@(New-ProbeNodes $class $oldMethods @('Slow'))+(New-ProbeNodes $unfinished @('Run') @())+(New-ProbeNodes $never @('Run') @())
            $newNodes=@(New-ProbeNodes $class $newMethods @('Slow'))+(New-ProbeNodes $unfinished @('Run') @())+(New-ProbeNodes $never @('Run') @())
            $classes=@($class,$unfinished,$never)
            $oldPlan=@(Convert-ProbePlan $oldNodes $classes)
            $newPlan=@(Convert-ProbePlan $newNodes $classes)
            function Find-MethodChunk($Plan,[string]$Method){
                foreach($group in @($Plan)){
                    foreach($node in @($group.nodes)){
                        $name=[string]$node.Method
                        $open=$name.IndexOf('(')
                        if($open -gt 0){$name=$name.Substring(0,$open)}
                        if($name -ceq $Method){return [string]$group.id}
                    }
                }
                return ''
            }
            $oldId=Find-MethodChunk $oldPlan 'M200'
            $newId=Find-MethodChunk $newPlan 'M200'
            $methodChunks=@(foreach($group in @($newPlan)){
                $hit=$false
                foreach($node in @($group.nodes)){
                    $name=[string]$node.Method
                    $open=$name.IndexOf('(')
                    if($open -gt 0){$name=$name.Substring(0,$open)}
                    if($name -ceq 'M200'){$hit=$true}
                }
                if($hit){[string]$group.id}
            })
            $rows=@(
                [ordered]@{suite='antiphon';chunk=$oldId;classes=@($class);completed=$true;started=$true;lastRunAtUtc=$stamp},
                [ordered]@{suite='antiphon';chunk='seed-delta';classes=@($unfinished);completed=$false;started=$true;lastRunAtUtc=$stamp}
            )
            $failed=@(
                [ordered]@{suite='antiphon';chunk=$oldId;className=$class;method='M200';outcome='Failed'},
                [ordered]@{suite='antiphon';chunk=$oldId;className=$class;method='M200(case)';outcome='Failed'}
            )
            $doc=Write-ProbeSeed $rows $failed
            $suiteChunks=@(foreach($group in @($newPlan)){[pscustomobject]@{id=[string]$group.id;classes=@($group.classes);nodes=@($group.nodes)}})
            $suiteNodes=New-Object System.Collections.Generic.List[object]
            foreach($node in @($newNodes)){[void]$suiteNodes.Add($node)}
            $suite=@([pscustomobject]@{id='antiphon';chunks=$suiteChunks;nodes=@($suiteNodes.ToArray());exclusions=@()})
            $resolved=Resolve-NightlyPriorFailures $doc $suite Full
            $schedule=New-NightlyDispatchSchedule -PreparedSuites $suite -PriorityMembers @($resolved.members) -PreviousRun $doc -CandidateSha ('b'*40) -NowUtc $now
            $again=New-NightlyDispatchSchedule -PreparedSuites $suite -PriorityMembers @($resolved.members) -PreviousRun $doc -CandidateSha ('b'*40) -NowUtc $now
            $ids=@($schedule.entries.chunk)
            $againIds=@($again.entries.chunk)
            $lead=@($ids[0..($methodChunks.Count-1)])
            $leadOk=((@($lead|Sort-Object) -join ',') -ceq ((@($methodChunks)|Sort-Object) -join ',') -and $ids[0] -ceq $newId)
            $sibling=''
            foreach($group in @($newPlan)){
                if(@($group.classes) -ccontains $class -and $methodChunks -cnotcontains [string]$group.id){$sibling=[string]$group.id;break}
            }
            $openId=[string]@($newPlan|Where-Object { @($_.classes) -ccontains $unfinished })[0].id
            $neverId=[string]@($newPlan|Where-Object { @($_.classes) -ccontains $never })[0].id
            $openAt=[array]::IndexOf($ids,$openId)
            $neverAt=[array]::IndexOf($ids,$neverId)
            $siblingAt=[array]::IndexOf($ids,$sibling)
            $moved=($oldId -cne $newId -and -not [string]::IsNullOrWhiteSpace($oldId) -and $methodChunks.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace($sibling))
            $packOk=($moved -and $leadOk -and $openAt -eq $methodChunks.Count -and $neverAt -eq ($openAt+1) -and $siblingAt -gt $openAt -and ($ids -join ',') -ceq ($againIds -join ',') -and $schedule.orderingReason -like 'operator-seed-order*')
            Check $packOk ('old='+$oldId+'; new='+$newId+'; lead='+($lead -join ',')+'; openAt='+$openAt+'; sibling='+$sibling+'@'+$siblingAt+'; reason='+$schedule.orderingReason+'; ids='+($ids -join ','))
        }
        'RealShapeFailedMethodBand' {
            $stamp=$now.AddHours(-4).ToString('o')
            $openStamp=$now.AddHours(-1).ToString('o')
            $passStamp=$now.AddHours(-2).ToString('o')
            $remote='Antiphon.Tests.SeedShape.RemoteSplit'
            $land='Antiphon.E2E.SeedShape.LandSplit'
            $lanes=[ordered]@{antiphon=638;e2e=75;'agents-pty'=9;'markdown-pdf'=11;'session-runner'=53;'pty-host'=53;messaging=1;observability=1}
            $suites=New-Object System.Collections.Generic.List[object]
            $seedRows=New-Object System.Collections.Generic.List[object]
            $failed=New-Object System.Collections.Generic.List[object]
            $failedIds=New-Object System.Collections.Generic.List[string]
            $suiteNodes=New-Object System.Collections.Generic.List[object]
            function Add-ShapeNode([string]$Class,[string]$Method){
                [void]$suiteNodes.Add([pscustomobject]@{Uid=($Class+'/'+$Method);ClassName=$Class;Method=$Method;Type='T';Categories=@()})
            }
            function Add-ShapeChunk([string]$Lane,[string]$Id,[string]$Class,[string]$Kind,[string]$Method,[bool]$Split){
                $nodes=@()
                if($Split){
                    $nodes=@([pscustomobject]@{Uid=($Class+'/'+$Method);ClassName=$Class;Method=$Method;Type='T';Categories=@()})
                    Add-ShapeNode $Class $Method
                }elseif($Kind -ceq 'fail'){
                    Add-ShapeNode $Class 'Run'
                }
                $completed=($Kind -cne 'open')
                $when=$(if($Kind -ceq 'open'){$openStamp}elseif($Kind -ceq 'pass'){$passStamp}else{$stamp})
                [void]$seedRows.Add([ordered]@{suite=$Lane;chunk=$Id;classes=@($Class);completed=$completed;started=$true;lastRunAtUtc=$when})
                if($Kind -ceq 'fail'){
                    [void]$failed.Add([ordered]@{suite=$Lane;chunk=$Id;className=$Class;method=$Method;outcome='Failed'})
                    [void]$failedIds.Add($Id)
                }
                return [pscustomobject]@{id=$Id;classes=@($Class);nodes=$nodes}
            }
            foreach($lane in @($lanes.Keys)){
                $count=[int]$lanes[$lane]
                $chunks=New-Object System.Collections.Generic.List[object]
                $n=0
                if($lane -ceq 'antiphon'){
                    for($i=0;$i -lt 11;$i++){[void]$chunks.Add((Add-ShapeChunk $lane ('fail-antiphon-'+$i) ('Antiphon.Tests.SeedShape.Fail'+$i) 'fail' 'Run' $false));$n++}
                    for($i=0;$i -lt 181;$i++){
                        $kind=$(if($i -eq 10 -or $i -eq 20){'fail'}else{'sibling'})
                        [void]$chunks.Add((Add-ShapeChunk $lane ('rs-'+$i) $remote $kind ('M'+$i) $true));$n++
                    }
                    for($i=0;$i -lt 8;$i++){[void]$chunks.Add((Add-ShapeChunk $lane ('open-antiphon-'+$i) ('Antiphon.Tests.SeedShape.Open'+$i) 'open' 'Run' $false));$n++}
                }elseif($lane -ceq 'e2e'){
                    [void]$chunks.Add((Add-ShapeChunk $lane 'fail-e2e-0' 'Antiphon.E2E.SeedShape.Fail0' 'fail' 'Run' $false));$n++
                    $failedSplit=@(1,2,3,4,32,33)
                    for($i=0;$i -lt 36;$i++){
                        $kind=$(if($failedSplit -contains $i){'fail'}else{'sibling'})
                        [void]$chunks.Add((Add-ShapeChunk $lane ('e2-'+$i) $land $kind ('E'+$i) $true));$n++
                    }
                }elseif($lane -ceq 'agents-pty'){
                    [void]$chunks.Add((Add-ShapeChunk $lane 'fail-agents-0' 'Antiphon.Tests.SeedShape.AgentsFail' 'fail' 'Run' $false));$n++
                    [void]$chunks.Add((Add-ShapeChunk $lane 'open-agents-0' 'Antiphon.Tests.SeedShape.AgentsOpen' 'open' 'Run' $false));$n++
                }elseif($lane -ceq 'session-runner'){
                    for($i=0;$i -lt 2;$i++){[void]$chunks.Add((Add-ShapeChunk $lane ('fail-sr-'+$i) ('Antiphon.Tests.SeedShape.SrFail'+$i) 'fail' 'Run' $false));$n++}
                }elseif($lane -ceq 'pty-host'){
                    for($i=0;$i -lt 2;$i++){[void]$chunks.Add((Add-ShapeChunk $lane ('fail-pty-'+$i) ('Antiphon.Tests.SeedShape.PtyFail'+$i) 'fail' 'Run' $false));$n++}
                }
                while($n -lt $count){
                    [void]$chunks.Add((Add-ShapeChunk $lane ('pass-'+$lane+'-'+$n) ('Antiphon.Tests.SeedShape.Pass'+$lane.Replace('-','')+$n) 'pass' 'Run' $false))
                    $n++
                }
                if($n -ne $count){throw ('lane count '+$lane+' '+$n)}
                [void]$suites.Add([pscustomobject]@{id=$lane;chunks=@($chunks.ToArray());nodes=@();exclusions=@()})
            }
            if($suites.Count -ne 8 -or $failedIds.Count -ne 25){throw ('shape suites='+$suites.Count+' failed='+$failedIds.Count)}
            $total=0
            $preparedSuites=@($suites.ToArray())
            foreach($shapeSuite in $preparedSuites){
                $shapeSuite.nodes=@($suiteNodes.ToArray())
                $total+=@($shapeSuite.chunks).Count
            }
            if($total -ne 841){throw ('shape total '+$total)}
            $payload=[ordered]@{sourceObservedAtUtc=$stamp;chunks=@($seedRows.ToArray());failedTests=@($failed.ToArray())}
            $envelope=[ordered]@{format='antiphon-nightly-stopped-seed-v1';seedKind='operator-seed-from-stopped-run';sourceRunId='synthetic-shape';sourceCandidateId='rc-synthetic-old';sourceSha=('c'*40);createdAtUtc=$stamp;payload=($payload|ConvertTo-Json -Depth 20 -Compress);contentHash=''}
            $envelope.contentHash=Get-NightlyStoppedSeedHash $envelope
            $path=Join-Path $root 'shape-seed.json'
            [IO.File]::WriteAllText($path,($envelope|ConvertTo-Json -Depth 12))
            $doc=Read-NightlyStoppedRunSeed $path
            $resolved=Resolve-NightlyPriorFailures $doc $preparedSuites Full
            $schedule=New-NightlyDispatchSchedule -PreparedSuites $preparedSuites -PriorityMembers @($resolved.members) -PreviousRun $doc -CandidateSha ('b'*40) -NowUtc $now
            $again=New-NightlyDispatchSchedule -PreparedSuites $preparedSuites -PriorityMembers @($resolved.members) -PreviousRun $doc -CandidateSha ('b'*40) -NowUtc $now
            $ids=@($schedule.entries.chunk)
            $lead=@($ids[0..24])
            $leadOk=((($lead -join ',') -ceq (($failedIds.ToArray()) -join ',')))
            $openAt=[array]::IndexOf($ids,'open-antiphon-0')
            $siblingAt=[array]::IndexOf($ids,'rs-0')
            $e2Sibling=[array]::IndexOf($ids,'e2-0')
            $unique=@($ids|Select-Object -Unique).Count
            $shapeOk=($leadOk -and $openAt -eq 25 -and $siblingAt -gt $openAt -and $e2Sibling -gt $openAt -and $unique -eq 841 -and $ids.Count -eq 841 -and ($ids -join ',') -ceq ((@($again.entries.chunk) -join ',')) -and $schedule.orderingReason -like 'operator-seed-order*' -and @($resolved.unresolved).Count -eq 0)
            Check $shapeOk ('lead='+($lead -join ',')+'; openAt='+$openAt+'; rs0='+$siblingAt+'; e20='+$e2Sibling+'; unique='+$unique+'; unresolved='+@($resolved.unresolved).Count+'; reason='+$schedule.orderingReason)
        }
        'UnfinishedRunLaneResume' {
            # A trimmed seed in the exporter's own shape for an UNFINISHED run (like a074ff9a: a few torn
            # chunks, many never started), read through the real ingress with acknowledgement.
            $stamp=$now.AddHours(-5).ToString('o')
            $passStamp=$now.AddHours(-4).ToString('o')
            $oldTorn=$now.AddHours(-3).ToString('o')
            $newTorn=$now.AddHours(-2).ToString('o')
            $seedRows=New-Object System.Collections.Generic.List[object]
            $failed=New-Object System.Collections.Generic.List[object]
            $laneSuites=New-Object System.Collections.Generic.List[object]
            function Add-LaneRow([string]$Lane,[string]$Class,[string]$Kind,[string]$When){
                if($Kind -ceq 'unseen'){return}
                $row=[ordered]@{suite=$Lane;chunk=('old-'+$Class.Split('.')[-1]);classes=@($Class);membershipSource='plan';completed=($Kind -cin @('pass','fail'));started=($Kind -cne 'never');lastRunAtUtc=$(if($Kind -ceq 'never'){$null}else{$When})}
                if($Kind -ceq 'torn'){$row['unfinishedReason']='trx-open'}
                [void]$seedRows.Add($row)
            }
            # Lane plan: id, class, seed state, node methods (split chunks only), failed methods.
            $plan=[ordered]@{
                antiphon=@(
                    @('a-p0','Antiphon.Tests.Resume.P0','pass',@(),@()),
                    @('a-h','Antiphon.Tests.Resume.Hole','never',@(),@()),
                    @('a-p1','Antiphon.Tests.Resume.P1','pass',@(),@()),
                    @('a-f','Antiphon.Tests.Resume.Fail','fail',@(),@('Run')),
                    @('a-i','Antiphon.Tests.Resume.Torn','torn',@(),@()),
                    @('a-u0','Antiphon.Tests.Resume.U0','never',@(),@()),
                    @('a-new','Antiphon.Tests.Resume.Added','unseen',@(),@()),
                    @('a-u1','Antiphon.Tests.Resume.U1','never',@(),@()),
                    @('a-s0','Antiphon.Tests.Resume.Split','fail',@('M0'),@('M0')),
                    @('a-s1','Antiphon.Tests.Resume.Split','fail',@('M1'),@())
                )
                e2e=@(
                    @('e-u0','Antiphon.E2E.Resume.U0','never',@(),@()),
                    @('e-i','Antiphon.E2E.Resume.Torn','torn-old',@(),@()),
                    @('e-u1','Antiphon.E2E.Resume.U1','never',@(),@()),
                    @('e-i2','Antiphon.E2E.Resume.Torn2','torn-old',@(),@()),
                    @('e-p0','Antiphon.E2E.Resume.P0','pass',@(),@())
                )
                'session-runner'=@(
                    @('s-u0','Antiphon.Sr.Resume.U0','never',@(),@()),
                    @('s-p0','Antiphon.Sr.Resume.P0','pass',@(),@()),
                    @('s-p1','Antiphon.Sr.Resume.P1','pass',@(),@()),
                    @('s-u1','Antiphon.Sr.Resume.U1','never',@(),@()),
                    @('s-u2','Antiphon.Sr.Resume.U2','never',@(),@())
                )
            }
            $seenClass=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach($lane in @($plan.Keys)){
                $chunks=New-Object System.Collections.Generic.List[object]
                $suiteNodes=New-Object System.Collections.Generic.List[object]
                foreach($spec in @($plan[$lane])){
                    $id=[string]$spec[0];$class=[string]$spec[1];$kind=[string]$spec[2]
                    $nodes=@(foreach($m in @($spec[3])){[pscustomobject]@{Uid=($class+'/'+$m);ClassName=$class;Method=[string]$m;Type='T';Categories=@()}})
                    foreach($n in $nodes){[void]$suiteNodes.Add($n)}
                    if($nodes.Count -eq 0){[void]$suiteNodes.Add([pscustomobject]@{Uid=($class+'/Run');ClassName=$class;Method='Run';Type='T';Categories=@()})}
                    [void]$chunks.Add([pscustomobject]@{id=$id;classes=@($class);nodes=$nodes})
                    $seedKind=$(if($kind -clike 'torn*'){'torn'}else{$kind})
                    $when=$(if($kind -ceq 'torn'){$newTorn}elseif($kind -ceq 'torn-old'){$oldTorn}elseif($kind -ceq 'pass'){$passStamp}else{$stamp})
                    if($seenClass.Add($lane+'/'+$class)){Add-LaneRow $lane $class $seedKind $when}
                    foreach($m in @($spec[4])){[void]$failed.Add([ordered]@{suite=$lane;chunk=('old-'+$class.Split('.')[-1]);className=$class;method=[string]$m;outcome='Failed'})}
                }
                [void]$laneSuites.Add([pscustomobject]@{id=$lane;chunks=@($chunks.ToArray());nodes=@($suiteNodes.ToArray());exclusions=@()})
            }
            $evidence=@([ordered]@{path='timing-unfinished/groups-planned-antiphon.json';sha256=('d'*64)})
            $payload=[ordered]@{sourceObservedAtUtc=$newTorn;chunks=@($seedRows.ToArray());failedTests=@($failed.ToArray());evidence=$evidence}
            $envelope=[ordered]@{format='antiphon-nightly-stopped-seed-v1';seedKind='operator-seed-from-stopped-run';sourceRunId='a074ff9a-shape';sourceCandidateId='rc-synthetic-unfinished';sourceSha=('c'*40);createdAtUtc=$now.AddHours(-1).ToString('o');payload=($payload|ConvertTo-Json -Depth 30 -Compress);contentHash=''}
            $envelope.contentHash=Get-NightlyStoppedSeedHash $envelope
            $path=Join-Path $root 'unfinished-seed.json'
            [IO.File]::WriteAllText($path,($envelope|ConvertTo-Json -Depth 35))
            $received=Initialize-NightlyDispatchHistory -Path $path -CandidateSha ('b'*40) -AcceptCrossCandidateSeed -SeedSourceCandidateId 'rc-synthetic-unfinished'
            if($null -eq $received.History){throw ('ingress '+$received.FallbackReason)}
            $prepared=@($laneSuites.ToArray())
            $resolved=Resolve-NightlyPriorFailures $received.History $prepared Full
            $schedule=New-NightlyDispatchSchedule -PreparedSuites $prepared -PriorityMembers @($resolved.members) -PreviousRun $received.PreviousRun -CandidateSha ('b'*40) -NowUtc $now
            $ids=(@($schedule.entries.chunk) -join ',')
            # Contract: failed-method chunks; every lane's interrupted chunks (lane order, from the lane's
            # first interrupted chunk); every lane's untouched chunks from there, wrapping to earlier holes;
            # siblings of a placed failure; passed. A lane with no interrupted chunk resumes after its last pass.
            $want='a-f,a-s0,a-i,e-i,e-i2,a-u0,a-new,a-u1,a-h,e-u1,e-u0,s-u1,s-u2,s-u0,a-s1,a-p0,a-p1,e-p0,s-p0,s-p1'
            $laneText=(@($schedule.lanes|ForEach-Object {[string]$_.suite+':'+[string]$_.resumeChunk+':'+[string]$_.lastPassedChunk}) -join ',')
            $again=(@((New-NightlyDispatchSchedule -PreparedSuites $prepared -PriorityMembers @($resolved.members) -PreviousRun $received.PreviousRun -CandidateSha ('b'*40) -NowUtc $now).entries.chunk) -join ',')
            Check ($ids -ceq $want -and $again -ceq $ids -and $laneText -ceq 'antiphon:a-i:a-p1,e2e:e-i:e-p0,session-runner:s-u1:s-p1' -and $schedule.orderingReason -like 'operator-seed-order*' -and @($resolved.unresolved).Count -eq 0) ('ids='+$ids+'; lanes='+$laneText+'; reason='+$schedule.orderingReason)
        }
        'SkipRowsCountAsCompleted' {
            # MTP writes an explicit [Skip] as outcome NotExecuted. A chunk whose only not-executed rows
            # are explicit skips is completed; any other not-executed outcome, or skips under an aborted
            # run summary, leaves the chunk unfinished.
            $trx=Join-Path $run 'antiphon-all-group-1.trx'
            $original=[IO.File]::ReadAllText($trx)
            $skipDefinition='<UnitTest id="2"><TestMethod className="Synthetic.antiphon.Passed" name="Skipped" /></UnitTest></TestDefinitions>'
            function Export-Variant([string]$Name,[string]$Outcome,[string]$Summary){
                $text=$original.Replace('</TestDefinitions>',$skipDefinition).Replace('</Results>',('<UnitTestResult testId="2" outcome="'+$Outcome+'" /></Results>')).Replace('<ResultSummary outcome="Completed">',('<ResultSummary outcome="'+$Summary+'">'))
                [IO.File]::WriteAllText($trx,$text)
                $out=Join-Path $root ('skip-'+$Name+'.json')
                [void](Export-NightlyStoppedRunSeed -RunDirectory $run -OutputPath $out -PassThru)
                $doc=Read-NightlyStoppedRunSeed $out
                return @($doc.chunks|Where-Object {$_.suite -ceq 'antiphon' -and $_.chunk -ceq 'all-group-1'})[0]
            }
            try {
                $skip=Export-Variant 'notexecuted' 'NotExecuted' 'Completed'
                $named=Export-Variant 'skipped' 'Skipped' 'Completed'
                $pending=Export-Variant 'pending' 'Pending' 'Completed'
                $aborted=Export-Variant 'aborted' 'NotExecuted' 'Aborted'
            } finally { [IO.File]::WriteAllText($trx,$original) }
            Check ($skip.completed -eq $true -and $named.completed -eq $true -and $pending.completed -eq $false -and $aborted.completed -eq $false -and $skip.started -and $pending.started) ('notExecuted='+$skip.completed+'; skipped='+$named.completed+'; pending='+$pending.completed+'; abortedSummary='+$aborted.completed)
        }
        'SummaryPrefixTamper' {
            $runId='d47848cce896471db1cc8ca346707cad'
            $sha='a'*40
            $layout=New-SeedSiblingLayout (Join-Path $root 'prefix-logs') $runId '2026-10-30-0824-d47848cc'
            Write-SeedSummary $layout.Run $layout.Timing $runId $sha
            $summaryPath=Join-Path $layout.Run 'summary.json'
            $prefix=Read-NightlyStoppedSummaryPrefix $summaryPath
            $bytes=[IO.File]::ReadAllBytes($summaryPath)
            # CutBeforeBulk returns the opening quote of the depth-1 bulk key.
            # A flip there hides the key, so the prefix reader parses the tail.
            # Change a byte inside the ignored INVALID value, after that key.
            $marker=[Text.UTF8Encoding]::new($false).GetBytes(',"timingChunks":')
            $at=-1
            for($i=0;$i -le ($bytes.Length-$marker.Length);$i++){
                $match=$true
                for($j=0;$j -lt $marker.Length;$j++){ if($bytes[$i+$j] -ne $marker[$j]){ $match=$false; break } }
                if($match){ $at=$i }
            }
            # The cut is the key's opening quote, one byte after this comma.
            if(($at + 1) -ne $prefix.Length){ throw 'bulk marker is not the prefix cut' }
            $flip=$at+$marker.Length+2
            if($flip -ge $bytes.Length){ throw 'decoy offset past end' }
            $decoy=[byte[]]$bytes.Clone()
            $decoy[$flip]=[byte]([int]$decoy[$flip] -bxor 1)
            [IO.File]::WriteAllBytes($summaryPath,$decoy)
            $decoyOk=$false;$decoyDetail=''
            try {
                [void](Export-NightlyStoppedRunSeed -RunDirectory $layout.Run -OutputPath (Join-Path $root 'decoy-seed.json') -SourceCandidateId 'rc-synthetic-old' -SourceSha $sha -PassThru)
                $decoyOk=$true;$decoyDetail='accepted'
            } catch { $decoyDetail=$_.Exception.Message }
            [IO.File]::WriteAllBytes($summaryPath,$bytes)
            $state=@{done=$false}
            $script:NightlySeedReadBarrier={
                param($Path)
                if($state.done){return}
                $state.done=$true
                $live=[IO.File]::ReadAllBytes($summaryPath)
                $live[8]=[byte]([int]$live[8] -bxor 1)
                [IO.File]::WriteAllBytes($summaryPath,$live)
            }.GetNewClosure()
            $tamperDetail=''
            try { [void](Export-NightlyStoppedRunSeed -RunDirectory $layout.Run -OutputPath (Join-Path $root 'prefix-tamper-seed.json') -SourceCandidateId 'rc-synthetic-old' -SourceSha $sha -PassThru); $tamperDetail='accepted' }
            catch { $tamperDetail=$_.Exception.Message }
            finally { $script:NightlySeedReadBarrier=$null }
            Check ($decoyOk -and $tamperDetail -like '*seed-summary-tampered*') ('decoy='+$decoyDetail+'; tamper='+$tamperDetail+'; cut='+$prefix.Length)
        }
        'WindowsAncestorSwapRefused' {
            $script:NightlyHistoryForceWindowsIdentity=$true
            try {
                function New-Leaf([string]$Name){
                    Reset-NightlyHistoryLinkCache
                    $grand=Join-Path $root $Name
                    $mid=Join-Path $grand 'mid'
                    [void][IO.Directory]::CreateDirectory($mid)
                    $leaf=Join-Path $mid 'leaf.txt'
                    [IO.File]::WriteAllText($leaf,'real')
                    Assert-NightlyHistoryNoLink $leaf
                    return [pscustomobject]@{Grand=$grand;Leaf=$leaf}
                }
                $real=New-Leaf 'win-grand'
                $held=Join-Path $root 'win-grand-held'
                Move-Item -LiteralPath $real.Grand -Destination $held
                [void][IO.Directory]::CreateDirectory((Join-Path $real.Grand 'mid'))
                [IO.File]::WriteAllText($real.Leaf,'FORGED')
                $dirMsg=''
                try { Assert-NightlyHistoryFileStable $real.Leaf } catch { $dirMsg=$_.Exception.Message }
                $linked=New-Leaf 'win-link'
                $linkHeld=Join-Path $root 'win-link-held'
                $forged=Join-Path $root 'win-link-forged'
                Move-Item -LiteralPath $linked.Grand -Destination $linkHeld
                [void][IO.Directory]::CreateDirectory((Join-Path $forged 'mid'))
                [IO.File]::WriteAllText((Join-Path $forged 'mid/leaf.txt'),'FORGED')
                New-Item -ItemType SymbolicLink -Path $linked.Grand -Target $forged | Out-Null
                $linkMsg=''
                try { Assert-NightlyHistoryFileStable $linked.Leaf } catch { $linkMsg=$_.Exception.Message }
                Check ($dirMsg -like '*evidence-link*' -and $linkMsg -like '*evidence-link*') ('dir='+$dirMsg+'; link='+$linkMsg)
            } finally { $script:NightlyHistoryForceWindowsIdentity=$false; Reset-NightlyHistoryLinkCache }
        }
        'ScaleExportBound' {
            $runId='d47848cce896471db1cc8ca346707cad'
            $sha='a'*40
            $logs=Join-Path $root 'scale-logs'
            $runDir=Join-Path $logs '2026-10-30-0824-d47848cc'
            $timing=Join-Path $logs ('timing-'+$runId)
            [void][IO.Directory]::CreateDirectory($runDir)
            [void][IO.Directory]::CreateDirectory($timing)
            foreach($file in @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'fixtures/nightly/stopped-seed') -File)){
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $runDir $file.Name)
            }
            $ownerPath=Join-Path $runDir 'owner.json'
            $ownerText=[IO.File]::ReadAllText($ownerPath).Replace('"runId": "synthetic-stopped"',('"runId": "'+$runId+'"'))
            [IO.File]::WriteAllText($ownerPath,$ownerText)
            foreach($plan in @(Get-ChildItem -LiteralPath $runDir -Filter 'groups-planned*.json' -File)){
                $text=[IO.File]::ReadAllText($plan.FullName).Replace('"runId": "synthetic-stopped"',('"runId": "'+$runId+'"'))
                [IO.File]::WriteAllText((Join-Path $timing $plan.Name),$text)
                Remove-Item -LiteralPath $plan.FullName
            }
            [IO.File]::WriteAllText((Join-Path $timing 'groups-planned-index.json'),'{"version":1}')
            $header=[ordered]@{startedAt='2026-10-30T08:30:00Z';completedAt='2026-10-30T12:30:00Z';sha=$sha;runId=$runId;selection=[ordered]@{timingChunks='decoy'};timingRoot=$timing}
            $text=($header|ConvertTo-Json -Compress).TrimEnd('}')
            $text=$text+',"timingChunks":"pad"'
            $pad=New-Object string @(' ',4194304)
            [IO.File]::WriteAllText((Join-Path $runDir 'summary.json'),($text+$pad+'}'))
            $runCount=@(Get-ChildItem -LiteralPath $runDir -File).Count
            $timingCount=@(Get-ChildItem -LiteralPath $timing -File).Count
            for($i=$runCount;$i -lt 3300;$i++){[IO.File]::WriteAllText((Join-Path $runDir ('pad-'+$i.ToString('0000')+'.txt')),'x')}
            for($i=$timingCount;$i -lt 1700;$i++){[IO.File]::WriteAllText((Join-Path $timing ('pad-'+$i.ToString('0000')+'.txt')),'x')}
            $out=Join-Path $root 'scale-seed.json'
            $export=[Diagnostics.Stopwatch]::StartNew()
            [void](Export-NightlyStoppedRunSeed -RunDirectory $runDir -OutputPath $out -SourceCandidateId 'rc-synthetic-old' -SourceSha $sha -PassThru)
            $export.Stop()
            $seconds=$export.Elapsed.TotalSeconds
            $doc=Read-NightlyStoppedRunSeed $out
            $rows=@($doc.chunks).Count
            # Provisional n=1 on this host: export 13.175s. Bound is 27s, 2x that sample.
            Check ($seconds -le 27 -and $rows -gt 0 -and $doc.sourceRunId -ceq $runId) ('exportSeconds='+$seconds+'; boundSeconds=27; measuredSampleSeconds=13.175; runFiles=3300; timingFiles=1700; summaryBytes='+(Get-Item -LiteralPath (Join-Path $runDir 'summary.json')).Length+'; chunks='+$rows)
        }
        'CumulativeMergeUnion' {
            $alpha='Fixture.Alpha';$beta='Fixture.Beta';$gamma='Fixture.Gamma'
            $olderObserved='2026-11-01T00:00:00.0000000+00:00';$newerObserved='2026-11-02T00:00:00.0000000+00:00'
            $olderCreated='2026-11-01T01:00:00.0000000+00:00';$newerCreated='2026-11-02T01:00:00.0000000+00:00'
            $stamp=$olderObserved
            $older=Join-Path $root 'union-older.json';$newer=Join-Path $root 'union-newer.json'
            Write-MergeSeed $older 'run-older' 'rc-older' ('a'*40) $olderObserved $olderCreated @(
                (New-MergeChunk 'antiphon' 'g0' @($gamma) $true $true $stamp),
                (New-MergeChunk 'antiphon' 'g1' @($alpha) $true $true $stamp)
            ) @(
                (New-MergeFail 'antiphon' 'g1' $alpha 'M1' 'Failed'),
                (New-MergeFail 'antiphon' 'g0' $gamma 'M3' 'Failed')
            ) @([ordered]@{path='older.json';sha256=('ab'*32)})
            Write-MergeSeed $newer 'run-newer' 'rc-newer' ('b'*40) $newerObserved $newerCreated @(
                (New-MergeChunk 'antiphon' 'g1' @($alpha) $true $true $newerObserved),
                (New-MergeChunk 'antiphon' 'g2' @($beta) $true $true $newerObserved)
            ) @(
                (New-MergeFail 'antiphon' 'g1' $alpha 'M2(canceled)' 'Failed')
            ) @([ordered]@{path='newer.json';sha256=('cd'*32)})
            $out=Join-Path $root 'union-merged.json';$reportPath=Join-Path $root 'union-report.json'
            $merged=Invoke-MergeNightlySeeds -Seeds @($older,$newer) -OutputPath $out -ReportPath $reportPath
            $doc=$null;$report=$null;$lead='';$methods='';$chunks=''
            if($merged.Code -eq 0){
                $doc=Read-NightlyStoppedRunSeed $out
                $report=Read-MergeReport $reportPath
                $chunks=(@($doc.chunks|ForEach-Object {[string]$_.chunk}) -join ',')
                $methods=(@($doc.failedTests|ForEach-Object {[string]$_.method}) -join ',')
                $plan=@(
                    [pscustomobject]@{id='g1';classes=@($alpha)},
                    [pscustomobject]@{id='g2';classes=@($beta)}
                )
                $schedule=Invoke-MergeChunkOrder $doc $plan $now
                $lead=(@($schedule.entries|ForEach-Object {[string]$_.chunk}) -join ',')
            }
            $m1=Find-MergeIdentity $report 'M1';$m2=Find-MergeIdentity $report 'M2(canceled)';$m3=Find-MergeIdentity $report 'M3'
            $kept=($methods -ceq 'M1,M2(canceled)' -and $chunks -ceq 'g1,g2' -and [string]$doc.evidence[0].path -ceq 'newer.json' -and $lead.StartsWith('g1,') -and [string]$m2.method -ceq 'M2(canceled)' -and [string]$m1.status -ceq 'open' -and [string]$m3.status -ceq 'open' -and $m3.placed -eq $false -and $methods.Split(',') -cnotcontains 'M3')
            $bad=Join-Path $root 'union-bad.json'
            Write-MergeSeed $bad 'run-bad' 'rc-bad' ('c'*40) $olderObserved $olderCreated @(
                (New-MergeChunk 'antiphon' 'g1' @($alpha) $true $true $stamp)
            ) @(
                (New-MergeFail 'antiphon' 'g1' $beta 'M9' 'Failed')
            ) @()
            $badOut=Join-Path $root 'union-bad-out.json';$badReport=Join-Path $root 'union-bad-report.json'
            $refused=Invoke-MergeNightlySeeds -Seeds @($bad) -OutputPath $badOut -ReportPath $badReport
            $refusedOk=($refused.Code -eq 2 -and -not [IO.File]::Exists($badOut) -and -not [IO.File]::Exists($badReport))
            $strictOut=Join-Path $root 'union-strict.json';$strictReport=Join-Path $root 'union-strict-report.json'
            $strict=Invoke-MergeNightlySeeds -Seeds @($older,$newer) -OutputPath $strictOut -ReportPath $strictReport -Strict
            $strictDoc=$null
            if([IO.File]::Exists($strictOut)){$strictDoc=Read-MergeReport $strictReport}
            $strictOk=($strict.Code -eq 3 -and $null -ne $strictDoc -and [int]$strictDoc.unplacedCount -gt 0 -and (@($strictDoc.identities|Where-Object {[string]$_.method -ceq 'M3'})[0].placed) -eq $false)
            Check ($merged.Code -eq 0 -and $kept -and $refusedOk -and $strictOk) ('code='+$merged.Code+'; chunks='+$chunks+'; methods='+$methods+'; lead='+$lead+'; refused='+$refused.Code+'; strict='+$strict.Code+'; text='+$merged.Text)
        }
        'CumulativeMergeProvenDrop' {
            $repo=Join-Path $root 'proven-repo'
            New-MergeGitRepo $repo
            $rootSha=Add-MergeGitCommit $repo 'a.txt' 'root' 'root' ''
            $fixSha=Add-MergeGitCommit $repo 'a.txt' 'fixed' 'fix the arm' ''
            $tipSha=Add-MergeGitCommit $repo 'b.txt' 'later' 'later tip' ''
            $olderObserved='2026-11-01T00:00:00.0000000+00:00';$newerObserved='2026-11-02T00:00:00.0000000+00:00'
            $older=Join-Path $root 'proven-older.json';$newer=Join-Path $root 'proven-newer.json'
            Write-MergeSeed $older 'run-older' 'rc-older' $rootSha $olderObserved '2026-11-01T01:00:00.0000000+00:00' @(
                (New-MergeChunk 'antiphon' 'g1' @('Fixture.Alpha') $true $true $olderObserved),
                (New-MergeChunk 'antiphon' 'g2' @('Fixture.Beta') $true $true $olderObserved)
            ) @(
                (New-MergeFail 'antiphon' 'g1' 'Fixture.Alpha' 'M1' 'Failed'),
                (New-MergeFail 'antiphon' 'g2' 'Fixture.Beta' 'Mpending' 'Error')
            ) @()
            Write-MergeSeed $newer 'run-newer' 'rc-newer' $tipSha $newerObserved '2026-11-02T01:00:00.0000000+00:00' @(
                (New-MergeChunk 'antiphon' 'g1' @('Fixture.Alpha') $true $true $newerObserved),
                (New-MergeChunk 'antiphon' 'g2' @('Fixture.Beta') $false $true $newerObserved)
            ) @() @()
            $tab=[char]9
            $decision=Join-Path $root 'proven-decisions.txt'
            $utf8=New-Object System.Text.UTF8Encoding $false
            [IO.File]::WriteAllText($decision,("# comment`n`nantiphon|Fixture.Alpha|M1"+$tab+$fixSha+"`nantiphon|Fixture.Beta|Mpending"+$tab+$fixSha+"`n"),$utf8)
            $out=Join-Path $root 'proven-merged.json';$reportPath=Join-Path $root 'proven-report.json'
            $merged=Invoke-MergeNightlySeeds -Seeds @($older,$newer) -Decision $decision -Repo $repo -OutputPath $out -ReportPath $reportPath
            $doc=$null;$report=$null;$methods=''
            if($merged.Code -eq 0){$doc=Read-NightlyStoppedRunSeed $out;$report=Read-MergeReport $reportPath;$methods=(@($doc.failedTests|ForEach-Object {[string]$_.method}) -join ',')}
            $m1=Find-MergeIdentity $report 'M1';$pending=Find-MergeIdentity $report 'Mpending'
            $ok=($merged.Code -eq 0 -and $methods -ceq 'Mpending' -and [string]$m1.status -ceq 'proven' -and $m1.placed -eq $false -and [string]$m1.provingRun -ceq 'run-newer' -and [string]$m1.provingSha -ceq $tipSha -and [string]$m1.containingCommit -ceq $fixSha -and [string]$pending.status -ceq 'fixed-pending-proof' -and $pending.placed -eq $true -and [string]$pending.containingCommit -ceq $fixSha -and $null -eq $pending.provingRun)
            Check $ok ('code='+$merged.Code+'; methods='+$methods+'; m1='+[string]$m1.status+'/'+[string]$m1.containingCommit+'; pending='+[string]$pending.status+'; tip='+$tipSha+'; fix='+$fixSha+'; text='+$merged.Text)
        }
        'CumulativeMergeUnfixedGreen' {
            $repo=Join-Path $root 'unfixed-repo'
            New-MergeGitRepo $repo
            $rootSha=Add-MergeGitCommit $repo 'a.txt' 'root' 'root' ''
            $sourceSha=Add-MergeGitCommit $repo 'b.txt' 'unrelated' 'unrelated tip' ''
            Invoke-C487FixtureGit $repo @('checkout','-b','fixline',$rootSha)
            $fixSha=Add-MergeGitCommit $repo 'c.txt' 'fix' 'the real fix' ''
            $olderObserved='2026-11-01T00:00:00.0000000+00:00';$newerObserved='2026-11-02T00:00:00.0000000+00:00'
            $older=Join-Path $root 'unfixed-older.json';$newer=Join-Path $root 'unfixed-newer.json'
            Write-MergeSeed $older 'run-older' 'rc-older' $rootSha $olderObserved '2026-11-01T01:00:00.0000000+00:00' @(
                (New-MergeChunk 'antiphon' 'g1' @('Fixture.Alpha') $true $true $olderObserved)
            ) @((New-MergeFail 'antiphon' 'g1' 'Fixture.Alpha' 'M1' 'Failed')) @()
            Write-MergeSeed $newer 'run-newer' 'rc-newer' $sourceSha $newerObserved '2026-11-02T01:00:00.0000000+00:00' @(
                (New-MergeChunk 'antiphon' 'g1' @('Fixture.Alpha') $true $true $newerObserved)
            ) @() @()
            $decision=Join-Path $root 'unfixed-decisions.txt'
            $utf8=New-Object System.Text.UTF8Encoding $false
            [IO.File]::WriteAllText($decision,('antiphon|Fixture.Alpha|M1'+[char]9+$fixSha+"`n"),$utf8)
            $out=Join-Path $root 'unfixed-merged.json';$reportPath=Join-Path $root 'unfixed-report.json'
            $merged=Invoke-MergeNightlySeeds -Seeds @($older,$newer) -Decision $decision -Repo $repo -OutputPath $out -ReportPath $reportPath
            $methods='';$status='';$containing=''
            if($merged.Code -eq 0){
                $doc=Read-NightlyStoppedRunSeed $out
                $report=Read-MergeReport $reportPath
                $methods=(@($doc.failedTests|ForEach-Object {[string]$_.method}) -join ',')
                $row=Find-MergeIdentity $report 'M1'
                $status=[string]$row.status
                $containing=[string]$row.containingCommit
            }
            Check ($merged.Code -eq 0 -and $methods -ceq 'M1' -and $status -ceq 'open' -and [string]::IsNullOrWhiteSpace($containing)) ('code='+$merged.Code+'; methods='+$methods+'; status='+$status+'; containing='+$containing+'; text='+$merged.Text)
        }
        'CumulativeMergeCherryPick' {
            $repo=Join-Path $root 'cherry-repo'
            New-MergeGitRepo $repo
            $rootSha=Add-MergeGitCommit $repo 'a.txt' 'root' 'root' ''
            $fixSha=Add-MergeGitCommit $repo 'a.txt' 'fixed' 'fix the arm' ''
            Invoke-C487FixtureGit $repo @('checkout','-b','side',$rootSha)
            $proseSha=Add-MergeGitCommit $repo 'b.txt' 'note' ('notes mention '+$fixSha+' in the discussion') ''
            $pickSha=Add-MergeGitCommit $repo 'c.txt' 'port' 'port the fix' ('(cherry picked from commit '+$fixSha+')')
            $olderObserved='2026-11-01T00:00:00.0000000+00:00';$newerObserved='2026-11-02T00:00:00.0000000+00:00'
            function Write-CherryPair([string]$Name,[string]$NewerSha){
                $olderPath=Join-Path $root ($Name+'-older.json');$newerPath=Join-Path $root ($Name+'-newer.json')
                Write-MergeSeed $olderPath 'run-older' 'rc-older' $rootSha $olderObserved '2026-11-01T01:00:00.0000000+00:00' @(
                    (New-MergeChunk 'antiphon' 'g1' @('Fixture.Alpha') $true $true $olderObserved)
                ) @((New-MergeFail 'antiphon' 'g1' 'Fixture.Alpha' 'M1' 'Failed')) @()
                Write-MergeSeed $newerPath 'run-newer' 'rc-newer' $NewerSha $newerObserved '2026-11-02T01:00:00.0000000+00:00' @(
                    (New-MergeChunk 'antiphon' 'g1' @('Fixture.Alpha') $true $true $newerObserved)
                ) @() @()
                return @($olderPath,$newerPath)
            }
            $decision=Join-Path $root 'cherry-decisions.txt'
            $utf8=New-Object System.Text.UTF8Encoding $false
            [IO.File]::WriteAllText($decision,('antiphon|Fixture.Alpha|M1'+[char]9+$fixSha+"`n"),$utf8)
            $pickSeeds=Write-CherryPair 'cherry-pick' $pickSha
            $pickOut=Join-Path $root 'cherry-pick-merged.json';$pickReport=Join-Path $root 'cherry-pick-report.json'
            $picked=Invoke-MergeNightlySeeds -Seeds $pickSeeds -Decision $decision -Repo $repo -OutputPath $pickOut -ReportPath $pickReport
            $pickStatus='';$pickCommit='';$pickMethods=''
            if($picked.Code -eq 0){
                $pickDoc=Read-NightlyStoppedRunSeed $pickOut
                $pickMethods=(@($pickDoc.failedTests|ForEach-Object {[string]$_.method}) -join ',')
                $pickRow=Find-MergeIdentity (Read-MergeReport $pickReport) 'M1'
                $pickStatus=[string]$pickRow.status;$pickCommit=[string]$pickRow.containingCommit
            }
            $proseSeeds=Write-CherryPair 'cherry-prose' $proseSha
            $proseOut=Join-Path $root 'cherry-prose-merged.json';$proseReport=Join-Path $root 'cherry-prose-report.json'
            $prose=Invoke-MergeNightlySeeds -Seeds $proseSeeds -Decision $decision -Repo $repo -OutputPath $proseOut -ReportPath $proseReport
            $proseStatus='';$proseMethods='';$proseCommit=''
            if($prose.Code -eq 0){
                $proseDoc=Read-NightlyStoppedRunSeed $proseOut
                $proseMethods=(@($proseDoc.failedTests|ForEach-Object {[string]$_.method}) -join ',')
                $proseRow=Find-MergeIdentity (Read-MergeReport $proseReport) 'M1'
                $proseStatus=[string]$proseRow.status;$proseCommit=[string]$proseRow.containingCommit
            }
            $ok=($picked.Code -eq 0 -and $pickMethods -ceq '' -and $pickStatus -ceq 'proven' -and $pickCommit -ceq $pickSha -and $prose.Code -eq 0 -and $proseMethods -ceq 'M1' -and $proseStatus -ceq 'open' -and [string]::IsNullOrWhiteSpace($proseCommit))
            Check $ok ('pick='+$picked.Code+'/'+$pickStatus+'/'+$pickCommit+'; prose='+$prose.Code+'/'+$proseStatus+'/'+$proseCommit+'; pickSha='+$pickSha+'; text='+$picked.Text+$prose.Text)
        }
        'CumulativeMergeExplicitProven' {
            $olderObserved='2026-11-01T00:00:00.0000000+00:00';$newerObserved='2026-11-02T00:00:00.0000000+00:00'
            $olderSha='a'*40;$newerSha='b'*40
            $older=Join-Path $root 'explicit-older.json';$newer=Join-Path $root 'explicit-newer.json'
            Write-MergeSeed $older 'run-older' 'rc-older' $olderSha $olderObserved '2026-11-01T01:00:00.0000000+00:00' @(
                (New-MergeChunk 'antiphon' 'g1' @('Fixture.Alpha') $true $true $olderObserved)
            ) @((New-MergeFail 'antiphon' 'g1' 'Fixture.Alpha' 'M1' 'Failed')) @()
            Write-MergeSeed $newer 'run-newer' 'rc-newer' $newerSha $newerObserved '2026-11-02T01:00:00.0000000+00:00' @(
                (New-MergeChunk 'antiphon' 'g1' @('Fixture.Alpha') $true $true $newerObserved)
            ) @() @()
            $out=Join-Path $root 'explicit-merged.json';$reportPath=Join-Path $root 'explicit-report.json'
            $merged=Invoke-MergeNightlySeeds -Seeds @($older,$newer) -Proven @('antiphon|Fixture.Alpha|M1:run-newer') -OutputPath $out -ReportPath $reportPath
            $methods='';$status='';$provingRun='';$provingSha=''
            if($merged.Code -eq 0){
                $doc=Read-NightlyStoppedRunSeed $out
                $methods=(@($doc.failedTests|ForEach-Object {[string]$_.method}) -join ',')
                $row=Find-MergeIdentity (Read-MergeReport $reportPath) 'M1'
                $status=[string]$row.status;$provingRun=[string]$row.provingRun;$provingSha=[string]$row.provingSha
            }
            $unknownOut=Join-Path $root 'explicit-unknown.json';$unknownReport=Join-Path $root 'explicit-unknown-report.json'
            $unknown=Invoke-MergeNightlySeeds -Seeds @($older,$newer) -Proven @('antiphon|Fixture.Alpha|M1:missing-run') -OutputPath $unknownOut -ReportPath $unknownReport
            $notLaterOut=Join-Path $root 'explicit-not-later.json';$notLaterReport=Join-Path $root 'explicit-not-later-report.json'
            $notLater=Invoke-MergeNightlySeeds -Seeds @($older,$newer) -Proven @('antiphon|Fixture.Alpha|M1:run-older') -OutputPath $notLaterOut -ReportPath $notLaterReport
            $redder=Join-Path $root 'explicit-red-newer.json'
            Write-MergeSeed $redder 'run-red' 'rc-red' ('c'*40) '2026-11-03T00:00:00.0000000+00:00' '2026-11-03T01:00:00.0000000+00:00' @(
                (New-MergeChunk 'antiphon' 'g1' @('Fixture.Alpha') $false $true $newerObserved)
            ) @() @()
            $redOut=Join-Path $root 'explicit-red.json';$redReport=Join-Path $root 'explicit-red-report.json'
            $notGreen=Invoke-MergeNightlySeeds -Seeds @($older,$redder) -Proven @('antiphon|Fixture.Alpha|M1:run-red') -OutputPath $redOut -ReportPath $redReport
            $refusedOk=($unknown.Code -eq 2 -and $unknown.Text -like '*proven-run-unknown*' -and -not [IO.File]::Exists($unknownOut) -and $notLater.Code -eq 2 -and -not [IO.File]::Exists($notLaterOut) -and $notGreen.Code -eq 2 -and $notGreen.Text -like '*proven-not-green*' -and -not [IO.File]::Exists($redOut))
            Check ($merged.Code -eq 0 -and $methods -ceq '' -and $status -ceq 'proven' -and $provingRun -ceq 'run-newer' -and $provingSha -ceq $newerSha -and $refusedOk) ('code='+$merged.Code+'; status='+$status+'; unknown='+$unknown.Code+'; notLater='+$notLater.Code+'; notGreen='+$notGreen.Code+'; text='+$merged.Text+$unknown.Text+$notGreen.Text)
        }
        'CumulativeMergeDigestStable' {
            $olderObserved='2026-11-01T00:00:00.0000000+00:00';$newerObserved='2026-11-02T00:00:00.0000000+00:00'
            $newerCreated='2026-11-02T01:00:00.0000000+00:00'
            $older=Join-Path $root 'digest-older.json';$newer=Join-Path $root 'digest-newer.json'
            Write-MergeSeed $older 'run-older' 'rc-older' ('a'*40) $olderObserved '2026-11-01T01:00:00.0000000+00:00' @(
                (New-MergeChunk 'antiphon' 'g1' @('Fixture.Alpha') $true $true $olderObserved)
            ) @((New-MergeFail 'antiphon' 'g1' 'Fixture.Alpha' 'M1' 'Failed')) @()
            Write-MergeSeed $newer 'run-newer' 'rc-newer' ('b'*40) $newerObserved $newerCreated @(
                (New-MergeChunk 'antiphon' 'g1' @('Fixture.Alpha') $true $true $newerObserved)
            ) @((New-MergeFail 'antiphon' 'g1' 'Fixture.Alpha' 'M2' 'Timeout')) @()
            $out1=Join-Path $root 'digest-1.json';$report1=Join-Path $root 'digest-report-1.json'
            $out2=Join-Path $root 'digest-2.json';$report2=Join-Path $root 'digest-report-2.json'
            $first=Invoke-MergeNightlySeeds -Seeds @($older,$newer) -OutputPath $out1 -ReportPath $report1
            $second=Invoke-MergeNightlySeeds -Seeds @($older,$newer) -OutputPath $out2 -ReportPath $report2
            $doc=$null;$hash1='';$hash2='';$created='';$same=$false
            if($first.Code -eq 0 -and $second.Code -eq 0){
                $doc=Read-NightlyStoppedRunSeed $out1
                $env1=[IO.File]::ReadAllText($out1)|ConvertFrom-Json
                $env2=[IO.File]::ReadAllText($out2)|ConvertFrom-Json
                $hash1=[string]$env1.contentHash;$hash2=[string]$env2.contentHash
                $created=(ConvertTo-NightlySeedUtc $doc.createdAtUtc).ToString('o')
                $same=(Test-MergeFileBytes $out1 $out2) -and (Test-MergeFileBytes $report1 $report2)
            }
            $expectedCreated=(ConvertTo-NightlySeedUtc $newerCreated).ToString('o')
            Check ($first.Code -eq 0 -and $second.Code -eq 0 -and $same -and $hash1 -ceq $hash2 -and $hash1 -cmatch '^[a-f0-9]{64}$' -and $created -ceq $expectedCreated -and $null -ne $doc) ('first='+$first.Code+'; second='+$second.Code+'; same='+$same+'; hash='+$hash1+'; created='+$created+'; expected='+$expectedCreated+'; text='+$first.Text+$second.Text)
        }
        'CumulativeMergeLaunchDefaults' {
            $mergePath=Join-Path $PSScriptRoot 'merge-nightly-seeds.ps1'
            $nightlyPath=Join-Path $PSScriptRoot 'nightly-run.ps1'
            $raw=[IO.File]::ReadAllBytes($mergePath)
            $ascii=$true
            foreach($byte in $raw){if($byte -gt 127){$ascii=$false;break}}
            $text=[Text.Encoding]::ASCII.GetString($raw)
            $sentence='A later green proves an identity only when that seed''s source SHA contains the fix commit, or --Proven names that run.'
            $nightly=[IO.File]::ReadAllText($nightlyPath)
            $declares=($nightly.Contains('[string]$PriorFailuresPath') -and $nightly.Contains('[string]$PriorFailuresDigest') -and $nightly.Contains('[switch]$AcceptCrossCandidateSeed') -and $nightly.Contains('[string]$SeedSourceCandidateId'))
            $noCall=(-not $nightly.Contains('merge-nightly-seeds.ps1'))
            Check ($ascii -and $text.Contains($sentence) -and $declares -and $noCall) ('ascii='+$ascii+'; sentence='+$text.Contains($sentence)+'; declares='+$declares+'; noCall='+$noCall)
        }
        default {throw 'unknown case'}
    }
} catch {Check $false $_.Exception.Message}
[IO.File]::WriteAllText((Join-Path $root 'performance.json'),([ordered]@{case=$Case;wallSeconds=$watch.Elapsed.TotalSeconds;fixture='one synthetic directory per method';IO='included in body, per-call attribution unknown';database='not-used';HTTP='not-used';CLI='harness process only';buildSlot='recorded by parent';remainder='unknown'}|ConvertTo-Json))
Complete-C487Harness -ResultsDirectory $root -ExpectedRows 1
