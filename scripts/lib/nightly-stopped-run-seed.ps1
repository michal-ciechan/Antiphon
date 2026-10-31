#requires -Version 5.1
# Portable, read-only evidence adapter. Seeds influence order, never execution credit.
function ConvertTo-NightlySeedUtc {
    param($Value)
    if($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)){return $null}
    if($Value -is [datetime]){return ([datetimeoffset]$Value).ToUniversalTime()}
    $stamp=[datetimeoffset]::MinValue
    if(-not [datetimeoffset]::TryParse([string]$Value,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::AssumeUniversal,[ref]$stamp)){throw 'seed-timestamp-invalid'}
    return $stamp.ToUniversalTime()
}

function Get-NightlyStoppedSeedHash {
    param($Seed)
    # Hash a versioned, unambiguous projection. Payload is an exact UTF8 JSON string;
    # date normalization survives both PS5 strings and PS7 ConvertFrom-Json dates.
    $stamp=ConvertTo-NightlySeedUtc $Seed.createdAtUtc
    $text=ConvertTo-Json -InputObject @([string]$Seed.format,[string]$Seed.seedKind,[string]$Seed.sourceRunId,[string]$Seed.sourceCandidateId,[string]$Seed.sourceSha,$stamp.ToString('o'),[string]$Seed.payload) -Compress
    $hash=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-','').ToLowerInvariant()}
    finally{$hash.Dispose()}
}

function Assert-NightlyStoppedSeedContent {
    param($Seed)
    if($Seed.seedKind -cne 'operator-seed-from-stopped-run' -or $Seed.chunks -isnot [array] -or $Seed.failedTests -isnot [array] -or $Seed.chunks.Count -eq 0){throw 'seed-schema'}
    $keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($row in $Seed.chunks){
        if([string]$row.suite -cnotmatch '^[a-z][a-z0-9-]*$' -or [string]$row.chunk -cnotmatch '^[A-Za-z0-9_-]+$' -or $row.classes -isnot [array] -or $row.completed -isnot [bool] -or $row.started -isnot [bool] -or -not $keys.Add($row.suite+'/'+$row.chunk)){throw 'seed-schema: chunk'}
        foreach($class in $row.classes){Assert-NightlyHistoryClass $class}
        [void](ConvertTo-NightlySeedUtc $row.lastRunAtUtc)
    }
    foreach($test in $Seed.failedTests){
        Assert-NightlyHistoryClass $test.className
        if(-not $keys.Contains($test.suite+'/'+$test.chunk) -or [string]::IsNullOrWhiteSpace([string]$test.method) -or $test.outcome -cnotin @('Failed','Error','Timeout','Aborted')){throw 'seed-schema: failed test'}
        $chunk=@($Seed.chunks|Where-Object {$_.suite -ceq $test.suite -and $_.chunk -ceq $test.chunk})[0]
        if(@($chunk.classes) -cnotcontains $test.className){throw 'seed-schema: failed membership'}
    }
    if($null -eq (ConvertTo-NightlySeedUtc $Seed.sourceObservedAtUtc)){throw 'seed-timestamp-missing'}
}

function Read-NightlyStoppedRunSeed {
    param([string]$Path,[string]$ExpectedDigest='')
    Assert-NightlyHistoryNoLink $Path
    $digest=Get-NightlyHistoryDigest $Path
    if($ExpectedDigest -and $digest -cne $ExpectedDigest){throw 'history-digest-mismatch'}
    $envelope=[IO.File]::ReadAllText($Path)|ConvertFrom-Json
    if($envelope.format -cne 'antiphon-nightly-stopped-seed-v1' -or $envelope.seedKind -cne 'operator-seed-from-stopped-run' -or [string]$envelope.sourceSha -cnotmatch '^[a-f0-9]{40}$' -or [string]::IsNullOrWhiteSpace([string]$envelope.sourceRunId) -or [string]::IsNullOrWhiteSpace([string]$envelope.sourceCandidateId) -or $envelope.payload -isnot [string]){throw 'seed-schema'}
    if([string]$envelope.contentHash -cnotmatch '^[a-f0-9]{64}$' -or (Get-NightlyStoppedSeedHash $envelope) -cne $envelope.contentHash){throw 'seed-hash-mismatch'}
    $content=$envelope.payload|ConvertFrom-Json
    foreach($field in @('seedKind','sourceRunId','sourceCandidateId','sourceSha')){$content|Add-Member -NotePropertyName $field -NotePropertyValue ([string]$envelope.$field) -Force}
    $content|Add-Member -NotePropertyName createdAtUtc -NotePropertyValue $envelope.createdAtUtc -Force
    Assert-NightlyStoppedSeedContent $content
    # S1's resolver remains the single class -> new-plan chunk mapper.
    $members=@(foreach($group in @($content.failedTests|Group-Object suite,className)){
        $first=$group.Group[0]
        [pscustomobject]@{suite=$first.suite;className=$first.className;reasons=@('stopped-run-failure');observations=@($group.Group|ForEach-Object {[pscustomobject]@{chunk=$_.chunk}})}
    })
    $content|Add-Member -NotePropertyName members -NotePropertyValue $members
    $content|Add-Member -NotePropertyName unresolved -NotePropertyValue @()
    if((Get-NightlyHistoryDigest $Path) -cne $digest){throw 'seed-changed-during-read'}
    return $content
}

function Export-NightlyStoppedRunSeed {
    [CmdletBinding()]
    param([string]$RunDirectory,[string]$OutputPath,[string]$SourceRunId='',
        [string]$SourceCandidateId='', [string]$SourceSha='', [switch]$PassThru)
    if([string]::IsNullOrWhiteSpace($RunDirectory) -or [string]::IsNullOrWhiteSpace($OutputPath)){throw 'seed-path-required'}
    $root=[IO.Path]::GetFullPath($RunDirectory).TrimEnd('\','/')
    if(-not $root -or $root -ceq [IO.Path]::GetPathRoot($root)){throw 'seed-source-root-refused'}
    $output=[IO.Path]::GetFullPath($OutputPath)
    if(Test-NightlyHistoryUnderRoot $output $root){throw 'seed-output-inside-source'}
    Assert-NightlyHistoryNoLink $root
    Assert-NightlyHistoryNoLink $output
    if(-not [IO.Directory]::Exists($root)){throw 'seed-source-missing'}
    if([IO.File]::Exists($output)){throw 'seed-output-exists'}
    $inputs=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    function Read-SeedFile([string]$Path){
        if(-not (Test-NightlyHistoryUnderRoot ([IO.Path]::GetFullPath($Path)) $root)){throw 'seed-input-outside-source'}
        Assert-NightlyHistoryNoLink $Path
        $digest=Get-NightlyHistoryDigest $Path
        $text=[IO.File]::ReadAllText($Path)
        if((Get-NightlyHistoryDigest $Path) -cne $digest){throw 'seed-source-changed'}
        if($inputs.ContainsKey($Path) -and $inputs[$Path] -cne $digest){throw 'seed-source-changed'}
        $inputs[$Path]=$digest
        return $text
    }
    # Walk only the supplied directory, refusing links before entering a child.
    $files=[Collections.Generic.List[object]]::new();$pending=[Collections.Generic.Queue[string]]::new();$pending.Enqueue($root)
    while($pending.Count -gt 0){
        foreach($item in @(Get-ChildItem -LiteralPath $pending.Dequeue() -Force)){
            Assert-NightlyHistoryNoLink $item.FullName
            if($item.PSIsContainer){$pending.Enqueue($item.FullName)}else{$files.Add($item)}
        }
    }
    $plans=@($files|Where-Object {$_.Name -clike 'groups-planned-*.json' -and $_.Name -cne 'groups-planned-index.json'}|Sort-Object FullName)
    if($plans.Count -eq 0){$plans=@($files|Where-Object Name -ceq 'groups-planned.json')}
    if($plans.Count -eq 0){throw 'seed-plan-missing'}
    $observed=[datetimeoffset]::MinValue
    foreach($ownerFile in @($files|Where-Object {$_.Name -like '*owner*.json'})){
        $owner=(Read-SeedFile $ownerFile.FullName)|ConvertFrom-Json
        foreach($pair in @(@('runId','SourceRunId'),@('candidateId','SourceCandidateId'),@('sha','SourceSha'))){
            $value=[string]$owner.($pair[0]);$current=Get-Variable -Name $pair[1] -ValueOnly
            if($value){if($current -and $current -cne $value){throw 'seed-owner-identity-mismatch'};Set-Variable -Name $pair[1] -Value $value}
        }
        $time=ConvertTo-NightlySeedUtc $owner.startedAt
        if($time -and $time -gt $observed){$observed=$time}
    }
    $chunks=[Collections.Generic.List[object]]::new();$failed=[Collections.Generic.List[object]]::new()
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $failedKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $documents=[Collections.Generic.List[object]]::new()
    $plannedKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($file in $plans){
        $document=(Read-SeedFile $file.FullName)|ConvertFrom-Json
        $documents.Add($document)
        foreach($group in @($document.groups)){[void]$plannedKeys.Add([string]$document.suite+'/'+[string]$group.chunk)}
    }
    if(-not $SourceRunId){$SourceRunId=[string]$documents[0].runId}
    if(-not $SourceSha){$SourceSha=[string]$documents[0].sourceDigest}
    # A stopped run can retain a TRX whose historical group row is missing. Keep
    # its identities independently of plan lookup; missing membership elsewhere
    # must never erase a resolvable failure. Only supervisor per-chunk names at
    # the supplied root are admitted, never nested discovery/diagnostic TRX files.
    foreach($trxFile in @($files|Where-Object {$_.DirectoryName -eq $root -and $_.Extension -ceq '.trx'}|Sort-Object Name)){
        if($trxFile.Name -cnotmatch '^(antiphon|session-runner|pty-host|agents-pty|messaging|e2e|markdown-pdf|observability)-([A-Za-z0-9_-]+)\.trx$'){continue}
        $orphanSuite=$Matches[1];$orphanChunk=$Matches[2]
        if($plannedKeys.Contains($orphanSuite+'/'+$orphanChunk)){continue}
        $orphanRows=@()
        try{
            [void](Read-SeedFile $trxFile.FullName)
            $orphanRows=@(Read-NightlyTrxIdentities $trxFile.FullName)
        }catch{if($_.Exception.Message -clike 'seed-*'){throw}}
        $orphanClasses=@(Get-NightlyHistoryOrdinalStrings @($orphanRows|ForEach-Object {Get-NightlyBareClassIdentity $_.ClassName}))
        $documents.Add([pscustomobject]@{version=1;runId=$SourceRunId;sourceDigest=$SourceSha;suite=$orphanSuite;groups=@([pscustomobject]@{chunk=$orphanChunk;classes=$orphanClasses;plannedTestIds=@();fromTrxOnly=$true})})
    }
    foreach($plan in $documents){
        if($plan.version -ne 1 -or -not $plan.suite -or $plan.groups -isnot [array]){throw 'seed-plan-schema'}
        if($SourceRunId -and $plan.runId -cne $SourceRunId){throw 'seed-plan-run-mismatch'}
        if($SourceSha -and $plan.sourceDigest -cne $SourceSha){throw 'seed-plan-source-mismatch'}
        $SourceRunId=[string]$plan.runId;$SourceSha=[string]$plan.sourceDigest
        foreach($group in $plan.groups){
            $suite=[string]$plan.suite;$chunk=[string]$group.chunk;$key=$suite+'/'+$chunk
            if($suite -cnotmatch '^[a-z][a-z0-9-]*$' -or $chunk -cnotmatch '^[A-Za-z0-9_-]+$'){throw 'seed-plan-identity'}
            if(-not $seen.Add($key)){throw 'seed-plan-duplicate'}
            $classes=@(Get-NightlyHistoryOrdinalStrings @($group.classes|ForEach-Object {Get-NightlyBareClassIdentity $_}))
            $stem=$suite+'-'+$chunk;$timing=$null;$last=$null;$completed=$false;$started=$false
            $timingPath=Join-Path $root ($stem+'.timing.json')
            if([IO.File]::Exists($timingPath)){
                $timing=(Read-SeedFile $timingPath)|ConvertFrom-Json
                if($timing.suite -cne $suite -or $timing.chunk -cne $chunk){throw 'seed-timing-identity-mismatch'}
                foreach($field in @('startedAt','completedAt','lastPhaseUtc')){
                    $time=ConvertTo-NightlySeedUtc $timing.$field
                    if($time -and $time -gt $observed){$observed=$time}
                }
                $last=ConvertTo-NightlySeedUtc $timing.startedAt
                if($last){$started=$true}
            }
            $trxPath=Join-Path $root ($stem+'.trx')
            if([IO.File]::Exists($trxPath)){
                $started=$true
                try{
                    [xml]$xml=Read-SeedFile $trxPath
                    $rows=@(Read-NightlyTrxIdentities $trxPath)
                    $terminal=$xml.SelectSingleNode("//*[local-name()='ResultSummary']")
                    $times=$xml.SelectSingleNode("//*[local-name()='Times']")
                    $finish=$null
                    if($times){$finish=ConvertTo-NightlySeedUtc $times.GetAttribute('finish')}
                    if(-not $finish -and $timing){$finish=ConvertTo-NightlySeedUtc $timing.completedAt}
                    if($finish){$last=$finish;if($finish -gt $observed){$observed=$finish}}
                    $closed=($terminal -and $terminal.GetAttribute('outcome') -cin @('Completed','Passed','Failed','Error','Timeout','Aborted'))
                    $completed=($closed -and $null -ne $finish -and $rows.Count -gt 0 -and $rows.Count -ge @($group.plannedTestIds).Count -and @($rows|Where-Object {$_.Outcome -cnotin @('Passed','Failed','Error','Timeout','Aborted')}).Count -eq 0)
                    foreach($row in $rows){
                        $class=Get-NightlyBareClassIdentity $row.ClassName
                        if($classes -cnotcontains $class){throw 'seed-trx-class-unplanned'}
                        if($row.Outcome -cin @('Failed','Error','Timeout','Aborted')){
                            $failureKey=$key+[char]0+$class+[char]0+[string]$row.MethodName
                            if($failedKeys.Add($failureKey)){$failed.Add([ordered]@{suite=$suite;chunk=$chunk;className=$class;method=[string]$row.MethodName;outcome=[string]$row.Outcome})}
                        }
                    }
                }catch{
                    if($_.Exception.Message -clike 'seed-*'){throw}
                    # Torn/malformed TRX is unfinished, never a passing completion.
                    $completed=$false
                }
            }
            $chunks.Add([ordered]@{suite=$suite;chunk=$chunk;classes=$classes;membershipSource=$(if($group.fromTrxOnly){'trx-without-plan-row'}else{'plan'});completed=[bool]$completed;started=[bool]$started;lastRunAtUtc=$(if($last){$last.ToString('o')}else{$null})})
        }
    }
    if(-not $SourceRunId -or -not $SourceCandidateId -or $SourceSha -cnotmatch '^[a-f0-9]{40}$' -or $observed -eq [datetimeoffset]::MinValue){throw 'seed-source-identity-or-time-missing: supply source candidate/run/sha where owner metadata is absent'}
    $payload=[ordered]@{sourceObservedAtUtc=$observed.ToString('o');chunks=$chunks.ToArray();failedTests=$failed.ToArray();evidence=@(foreach($path in @($inputs.Keys|Sort-Object)){[ordered]@{path=$path.Substring($root.Length).TrimStart('\','/');sha256=$inputs[$path]}})}
    $envelope=[ordered]@{format='antiphon-nightly-stopped-seed-v1';seedKind='operator-seed-from-stopped-run';sourceRunId=$SourceRunId;sourceCandidateId=$SourceCandidateId;sourceSha=$SourceSha;createdAtUtc=[datetime]::UtcNow.ToString('o');payload=($payload|ConvertTo-Json -Depth 30 -Compress);contentHash=''}
    $envelope.contentHash=Get-NightlyStoppedSeedHash $envelope
    # Recheck all inputs before the first write. Never update source files/metadata.
    foreach($path in $inputs.Keys){if((Get-NightlyHistoryDigest $path) -cne $inputs[$path]){throw 'seed-source-changed'}}
    $parent=[IO.Path]::GetDirectoryName($output)
    [void][IO.Directory]::CreateDirectory($parent);Assert-NightlyHistoryNoLink $parent
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($envelope|ConvertTo-Json -Depth 35))
    $stream=[IO.File]::Open($output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
    if($PassThru){return [pscustomobject]@{Path=$output;Digest=(Get-NightlyHistoryDigest $output)}}
    Write-Host ('SEED '+$output+' SHA256 '+(Get-NightlyHistoryDigest $output))
}

function Resolve-NightlyStoppedSeedOrder {
    param([object[]]$Entries,$Seed)
    try{
        Assert-NightlyStoppedSeedContent $Seed
        $mapped=0;$ranked=[Collections.Generic.List[object]]::new()
        foreach($entry in $Entries){
            $classes=@($entry.ChunkRef.classes)
            if($classes.Count -eq 0){
                $nodes=@($entry.ChunkRef.nodes)
                if($nodes.Count -eq 0){$nodes=@($entry.SuiteState.nodes)}
                $classes=@(Get-NightlyHistoryOrdinalStrings @($nodes|ForEach-Object {Get-NightlyBareClassIdentity $_.ClassName}))
            }
            $passed=($classes.Count -gt 0);$last=$null
            foreach($class in $classes){
                $history=@($Seed.chunks|Where-Object {$_.suite -ceq $entry.suite -and @($_.classes) -ccontains $class})
                if($history.Count -gt 0){$mapped++}else{$passed=$false}
                foreach($old in $history){
                    if(-not $old.completed -or @($Seed.failedTests|Where-Object {$_.suite -ceq $old.suite -and $_.chunk -ceq $old.chunk -and $_.className -ceq $class}).Count -gt 0){$passed=$false}
                    $time=ConvertTo-NightlySeedUtc $old.lastRunAtUtc
                    if($time -and ($null -eq $last -or $time -gt $last)){$last=$time}
                }
            }
            $priority=1
            if($entry.priorFailure){$priority=0}elseif($passed){$priority=2}
            $ticks=[long]::MinValue;if($last){$ticks=$last.UtcDateTime.Ticks}
            $ranked.Add([pscustomobject]@{entry=$entry;priority=$priority;ticks=$ticks;ordinal=$entry.canonicalOrdinal})
        }
        if($mapped -eq 0){throw 'seed-plan-unmapped'}
        $ordered=@($ranked.ToArray()|Sort-Object priority,ticks,ordinal|ForEach-Object {$_.entry})
        return [pscustomobject]@{entries=$ordered;reason='operator-seed-order: unknown timestamps retain original order; known timestamps oldest first';lanes=@()}
    }catch{return [pscustomobject]@{entries=@($Entries);reason=$_.Exception.Message;lanes=@()}}
}
