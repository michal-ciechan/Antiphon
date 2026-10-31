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
    $members=@(foreach($group in @($content.failedTests|Group-Object suite,className -CaseSensitive)){
        $first=$group.Group[0]
        [pscustomobject]@{suite=$first.suite;className=$first.className;reasons=@('stopped-run-failure');observations=@($group.Group|ForEach-Object {[pscustomobject]@{chunk=$_.chunk}})}
    })
    $content|Add-Member -NotePropertyName members -NotePropertyValue $members
    $content|Add-Member -NotePropertyName unresolved -NotePropertyValue @()
    if((Get-NightlyHistoryDigest $Path) -cne $digest){throw 'seed-changed-during-read'}
    return $content
}

function Read-NightlyStoppedSummaryPrefix {
    param([string]$Path)
    Initialize-NightlyHistoryIdentity
    $fs=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try {
        $buf=New-Object byte[] 65536
        $ms=New-Object IO.MemoryStream
        $cut=-1
        while($ms.Length -lt 33554432){
            $n=$fs.Read($buf,0,$buf.Length)
            if($n -le 0){break}
            $ms.Write($buf,0,$n)
            $cut=[Antiphon.NightlyHistory.SummaryPrefix]::CutBeforeBulk($ms.ToArray())
            if($cut -ge 0){break}
        }
        $all=$ms.ToArray()
        if($cut -lt 0){
            if($all.Length -ge 33554432){throw 'seed-summary-header'}
            $extra=$fs.Read($buf,0,1)
            if($extra -gt 0){throw 'seed-summary-header'}
            $cut=$all.Length
        }
        $prefix=New-Object byte[] $cut
        [Array]::Copy($all,$prefix,$cut)
        $text=[Text.UTF8Encoding]::new($false).GetString($prefix).Trim()
        if($text.Length -gt 0 -and [int][char]$text[0] -eq 0xFEFF){$text=$text.Substring(1)}
        $text=$text.TrimEnd().TrimEnd(',')
        if(-not $text.EndsWith('}')){$text=$text+'}'}
        $doc=$text|ConvertFrom-Json
        $sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=[BitConverter]::ToString($sha.ComputeHash($prefix)).Replace('-','').ToLowerInvariant()}
        finally{$sha.Dispose()}
        return [pscustomobject]@{Document=$doc;Sha=$hash;Length=$cut}
    } finally { $fs.Dispose() }
}

function Get-NightlyStoppedSummaryPrefixSha {
    param([string]$Path,[int]$Length)
    $fs=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try {
        $prefix=New-Object byte[] $Length
        $got=0
        while($got -lt $Length){
            $n=$fs.Read($prefix,$got,($Length-$got))
            if($n -le 0){throw 'seed-summary-tampered'}
            $got+=$n
        }
        $sha=[Security.Cryptography.SHA256]::Create()
        try{return [BitConverter]::ToString($sha.ComputeHash($prefix)).Replace('-','').ToLowerInvariant()}
        finally{$sha.Dispose()}
    } finally { $fs.Dispose() }
}

function Resolve-NightlyStoppedTimingRoot {
    param([string]$RunRoot,[string]$TimingPath,[string]$RunId)
    if([string]::IsNullOrWhiteSpace($TimingPath)){throw 'seed-timing-missing'}
    $full=ConvertTo-NightlyHistoryFull $TimingPath
    $id=Get-NightlyHistoryNodeIdentity $full
    if($id.Missing){throw 'seed-timing-missing'}
    if($id.Unreadable){throw 'evidence-unreadable'}
    if($id.Linked -or -not $id.Directory){throw 'evidence-link'}
    $parent=ConvertTo-NightlyHistoryFull ([IO.Path]::GetDirectoryName((ConvertTo-NightlyHistoryFull $RunRoot)))
    $timingParent=ConvertTo-NightlyHistoryFull ([IO.Path]::GetDirectoryName($full))
    $comparison=[StringComparison]::Ordinal
    if([IO.Path]::DirectorySeparatorChar -eq '\'){$comparison=[StringComparison]::OrdinalIgnoreCase}
    if(-not $timingParent.Equals($parent,$comparison)){throw 'seed-timing-outside'}
    if(-not (Test-NightlyHistoryUnderRoot $full $parent)){throw 'seed-timing-outside'}
    if($full.Equals((ConvertTo-NightlyHistoryFull $RunRoot),$comparison)){throw 'seed-timing-outside'}
    $name=[IO.Path]::GetFileName($full)
    if($name -cnotmatch '^timing-.+$'){throw 'seed-timing-run-mismatch'}
    $namedId=$name.Substring(7)
    if($RunId -and $namedId -cne $RunId){throw 'seed-timing-run-mismatch'}
    return $full
}

function Find-NightlyStoppedTimingSibling {
    param([string]$RunRoot,[string]$RunId)
    $parent=[IO.Path]::GetDirectoryName((ConvertTo-NightlyHistoryFull $RunRoot))
    if($RunId){
        $named=Join-Path $parent ('timing-'+$RunId)
        $id=Get-NightlyHistoryNodeIdentity $named
        if(-not $id.Missing -and $id.Directory -and -not $id.Linked){return (ConvertTo-NightlyHistoryFull $named)}
    }
    $base=[IO.Path]::GetFileName((ConvertTo-NightlyHistoryFull $RunRoot))
    $short=''
    if($base -match '-([A-Za-z0-9]{8})$'){$short=$Matches[1]}
    if(-not $short){return $null}
    $hits=[Collections.Generic.List[string]]::new()
    foreach($dir in [IO.Directory]::EnumerateDirectories($parent)){
        $name=[IO.Path]::GetFileName($dir)
        if($name.StartsWith(('timing-'+$short),[StringComparison]::Ordinal) -and $name -cmatch ('^timing-'+[regex]::Escape($short)+'[A-Za-z0-9]*$')){
            $hits.Add((ConvertTo-NightlyHistoryFull $dir))
        }
    }
    if($hits.Count -gt 1){throw 'seed-timing-ambiguous'}
    if($hits.Count -eq 1){return $hits[0]}
    return $null
}

function Export-NightlyStoppedRunSeed {
    [CmdletBinding()]
    param([string]$RunDirectory,[string]$OutputPath,[string]$SourceRunId='',
        [string]$SourceCandidateId='', [string]$SourceSha='', [string]$TimingRoot='', [switch]$PassThru)
    if([string]::IsNullOrWhiteSpace($RunDirectory) -or [string]::IsNullOrWhiteSpace($OutputPath)){throw 'seed-path-required'}
    Reset-NightlyHistoryLinkCache
    $root=ConvertTo-NightlyHistoryFull $RunDirectory
    if(-not $root -or $root -ceq [IO.Path]::GetPathRoot($root)){throw 'seed-source-root-refused'}
    $output=[IO.Path]::GetFullPath($OutputPath)
    if(Test-NightlyHistoryUnderRoot $output $root){throw 'seed-output-inside-source'}
    Assert-NightlyHistoryNoLink $root
    Assert-NightlyHistoryNoLink $output
    if(-not [IO.Directory]::Exists($root)){throw 'seed-source-missing'}
    if([IO.File]::Exists($output)){throw 'seed-output-exists'}
    $summaryPrefix=$null
    $summaryPath=Join-Path $root 'summary.json'
    $declaredTiming=''
    if([IO.File]::Exists($summaryPath)){
        Assert-NightlyHistoryNoLink $summaryPath
        $summaryPrefix=Read-NightlyStoppedSummaryPrefix $summaryPath
        $summary=$summaryPrefix.Document
        if($summary.runId){ if($SourceRunId -and $SourceRunId -cne [string]$summary.runId){throw 'seed-summary-run-mismatch'}; if(-not $SourceRunId){$SourceRunId=[string]$summary.runId} }
        if($summary.sha){ if($SourceSha -and $SourceSha -cne [string]$summary.sha){throw 'seed-summary-sha-mismatch'}; if(-not $SourceSha){$SourceSha=[string]$summary.sha} }
        if($summary.candidateId){ if($SourceCandidateId -and $SourceCandidateId -cne [string]$summary.candidateId){throw 'seed-summary-candidate-mismatch'}; if(-not $SourceCandidateId){$SourceCandidateId=[string]$summary.candidateId} }
        $declaredTiming=[string]$summary.timingRoot
    }
    $inRunPlans=@(foreach($name in [IO.Directory]::EnumerateFiles($root)){ $file=[IO.Path]::GetFileName($name); if(($file -clike 'groups-planned-*.json' -and $file -cne 'groups-planned-index.json') -or $file -ceq 'groups-planned.json'){ $name } })
    $timingFull=$null
    if(-not [string]::IsNullOrWhiteSpace($declaredTiming)){
        if($TimingRoot -and (ConvertTo-NightlyHistoryFull $TimingRoot) -cne (ConvertTo-NightlyHistoryFull $declaredTiming)){throw 'seed-timing-conflict'}
        $timingFull=Resolve-NightlyStoppedTimingRoot $root $declaredTiming $SourceRunId
    } elseif($TimingRoot){
        $timingFull=Resolve-NightlyStoppedTimingRoot $root $TimingRoot $SourceRunId
    } elseif($inRunPlans.Count -eq 0){
        $found=Find-NightlyStoppedTimingSibling $root $SourceRunId
        if($found){$timingFull=Resolve-NightlyStoppedTimingRoot $root $found $SourceRunId}
    }
    if($timingFull -and -not $SourceRunId){
        $SourceRunId=[IO.Path]::GetFileName($timingFull).Substring(7)
        $timingFull=Resolve-NightlyStoppedTimingRoot $root $timingFull $SourceRunId
    }
    if($timingFull -and (Test-NightlyHistoryUnderRoot $output $timingFull)){throw 'seed-output-inside-source'}
    $inputs=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $chunkInputs=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    function Test-SeedInputAllowed([string]$Path){
        $full=[IO.Path]::GetFullPath($Path)
        if(Test-NightlyHistoryUnderRoot $full $root){return $true}
        if($timingFull -and (Test-NightlyHistoryUnderRoot $full $timingFull)){return $true}
        return $false
    }
    function Read-SeedFile([string]$Path,[switch]$Tolerate){
        $full=ConvertTo-NightlyHistoryFull $Path
        if(-not (Test-SeedInputAllowed $full)){throw 'seed-input-outside-source'}
        Assert-NightlyHistoryNoLink $full
        try {
            $digest=Get-NightlyHistoryDigest $full
            $text=[IO.File]::ReadAllText($full)
            if($script:NightlySeedReadBarrier){ & $script:NightlySeedReadBarrier $full }
            if((Get-NightlyHistoryDigest $full) -cne $digest){throw 'seed-source-changed'}
        } catch {
            $msg=$_.Exception.Message
            if($msg -like 'seed-*' -or $msg -eq 'evidence-link'){throw}
            if($Tolerate){throw 'seed-chunk-unreadable'}
            throw
        }
        if($inputs.ContainsKey($full) -and $inputs[$full] -cne $digest){throw 'seed-source-changed'}
        $inputs[$full]=$digest
        return $text
    }
    # Walk the run directory and, when plans live beside it, that timing sibling.
    # One lstat covers each cached ancestor; each entry rechecks only itself and its parent.
    $files=[Collections.Generic.List[object]]::new()
    $pending=[Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($root)
    if($timingFull){$pending.Enqueue($timingFull)}
    while($pending.Count -gt 0){
        $dir=$pending.Dequeue()
        foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($dir)){
            Assert-NightlyHistoryNoLink $entry
            $full=ConvertTo-NightlyHistoryFull $entry
            $info=Get-NightlyHistoryNodeIdentity $full
            if($info.Directory -and -not $info.Linked){$pending.Enqueue($full)}
            else {$files.Add([pscustomobject]@{FullName=$full;Name=[IO.Path]::GetFileName($full);DirectoryName=[IO.Path]::GetDirectoryName($full);Extension=[IO.Path]::GetExtension($full)})}
        }
    }
    $planRoot=$(if($timingFull){$timingFull}else{$root})
    $plans=@($files|Where-Object {$_.DirectoryName -ceq $planRoot -and $_.Name -clike 'groups-planned-*.json' -and $_.Name -cne 'groups-planned-index.json'}|Sort-Object FullName)
    if($plans.Count -eq 0){$plans=@($files|Where-Object {$_.DirectoryName -ceq $planRoot -and $_.Name -ceq 'groups-planned.json'})}
    if($plans.Count -eq 0){throw 'seed-plan-missing'}
    $observed=[datetimeoffset]::MinValue
    if($summaryPrefix){
        foreach($field in @('startedAt','completedAt')){
            $time=ConvertTo-NightlySeedUtc $summaryPrefix.Document.$field
            if($time -and $time -gt $observed){$observed=$time}
        }
    }
    foreach($ownerFile in @($files|Where-Object {$_.DirectoryName -ceq $root -and $_.Name -like '*owner*.json'})){
        $owner=(Read-SeedFile $ownerFile.FullName)|ConvertFrom-Json
        foreach($pair in @(@('runId','SourceRunId'),@('candidateId','SourceCandidateId'),@('sha','SourceSha'))){
            $value=[string]$owner.($pair[0]);$current=Get-Variable -Name $pair[1] -ValueOnly
            if($value){if($current -and $current -cne $value){throw 'seed-owner-identity-mismatch'};Set-Variable -Name $pair[1] -Value $value}
        }
        $time=ConvertTo-NightlySeedUtc $owner.startedAt
        if($time -and $time -gt $observed){$observed=$time}
    }
    function Test-SeedChunkPresent([string]$Path){
        $id=Get-NightlyHistoryNodeIdentity $Path
        if($id.Missing){return $false}
        if($id.Linked){throw 'evidence-link'}
        return $true
    }
    function Add-SeedChunkReason([System.Collections.Generic.List[string]]$Reasons,[string]$Reason){
        if($Reason -and -not $Reasons.Contains($Reason)){[void]$Reasons.Add($Reason)}
    }
    function Resolve-SeedChunkRead([string]$Kind,[string]$Message,[System.Collections.Generic.List[string]]$Reasons){
        if($Message -eq 'seed-source-changed'){Add-SeedChunkReason $Reasons ($Kind+'-changed');return}
        if($Message -eq 'seed-chunk-unreadable' -or $Message -eq 'evidence-unreadable'){Add-SeedChunkReason $Reasons ($Kind+'-unreadable');return}
        if($Message -like 'seed-*' -or $Message -eq 'evidence-link'){throw $Message}
        Add-SeedChunkReason $Reasons ($Kind+'-unreadable')
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
            $reasons=[Collections.Generic.List[string]]::new()
            $chunkFailed=[Collections.Generic.List[object]]::new()
            $timingPath=ConvertTo-NightlyHistoryFull (Join-Path $root ($stem+'.timing.json'))
            if(Test-SeedChunkPresent $timingPath){
                $started=$true
                try{
                    $timing=(Read-SeedFile $timingPath -Tolerate)|ConvertFrom-Json
                    $chunkInputs[$timingPath]=$key
                    if($timing.suite -cne $suite -or $timing.chunk -cne $chunk){Add-SeedChunkReason $reasons 'timing-identity'}
                    else{
                        foreach($field in @('startedAt','completedAt','lastPhaseUtc')){
                            $time=ConvertTo-NightlySeedUtc $timing.$field
                            if($time -and $time -gt $observed){$observed=$time}
                        }
                        $last=ConvertTo-NightlySeedUtc $timing.startedAt
                    }
                }catch{Resolve-SeedChunkRead 'timing' $_.Exception.Message $reasons}
            }
            $trxPath=ConvertTo-NightlyHistoryFull (Join-Path $root ($stem+'.trx'))
            if(Test-SeedChunkPresent $trxPath){
                $started=$true
                try{
                    $trxText=Read-SeedFile $trxPath -Tolerate
                    $chunkInputs[$trxPath]=$key
                    # A file still being appended has no closing tag. Do not apply the ResultSummary closed guard to it.
                    if($trxText -cnotmatch '</TestRun>'){Add-SeedChunkReason $reasons 'trx-open'}
                    else{
                        [xml]$xml=$trxText
                        $rows=@(Read-NightlyTrxIdentities $trxPath)
                        if($classes.Count -eq 0){$classes=@(Get-NightlyHistoryOrdinalStrings @($rows|ForEach-Object {Get-NightlyBareClassIdentity $_.ClassName}))}
                        $terminal=$xml.SelectSingleNode("//*[local-name()='ResultSummary']")
                        $times=$xml.SelectSingleNode("//*[local-name()='Times']")
                        $finish=$null
                        if($times){$finish=ConvertTo-NightlySeedUtc $times.GetAttribute('finish')}
                        if(-not $finish -and $timing){$finish=ConvertTo-NightlySeedUtc $timing.completedAt}
                        if($finish){$last=$finish;if($finish -gt $observed){$observed=$finish}}
                        $closed=($terminal -and $terminal.GetAttribute('outcome') -cin @('Completed','Passed','Failed','Error','Timeout','Aborted'))
                        # $closed stays in this conjunction so a missing ResultSummary cannot count as completed.
                        $completed=($closed -and $null -ne $finish -and $rows.Count -gt 0 -and $rows.Count -ge @($group.plannedTestIds).Count -and @($rows|Where-Object {$_.Outcome -cnotin @('Passed','Failed','Error','Timeout','Aborted')}).Count -eq 0)
                        if(-not $closed){Add-SeedChunkReason $reasons 'trx-result-open'}
                        foreach($row in $rows){
                            $class=Get-NightlyBareClassIdentity $row.ClassName
                            if($classes -cnotcontains $class){throw 'seed-trx-class-unplanned'}
                            if($row.Outcome -cin @('Failed','Error','Timeout','Aborted')){
                                $chunkFailed.Add([ordered]@{suite=$suite;chunk=$chunk;className=$class;method=[string]$row.MethodName;outcome=[string]$row.Outcome})
                            }
                        }
                    }
                }catch{Resolve-SeedChunkRead 'trx' $_.Exception.Message $reasons}
            }
            $force=$false;$dropFailures=$false
            foreach($reason in $reasons){
                if($reason -cne 'trx-result-open'){$force=$true}
                if($reason -cin @('trx-unreadable','trx-changed','trx-open')){$dropFailures=$true}
            }
            if($force){$completed=$false}
            if(-not $dropFailures){
                foreach($item in $chunkFailed){
                    $failureKey=$key+[char]0+$item.className+[char]0+[string]$item.method
                    if($failedKeys.Add($failureKey)){$failed.Add($item)}
                }
            }
            $chunkRow=[ordered]@{suite=$suite;chunk=$chunk;classes=$classes;membershipSource=$(if($group.fromTrxOnly){'trx-without-plan-row'}else{'plan'});completed=[bool]$completed;started=[bool]$started;lastRunAtUtc=$(if($last){$last.ToString('o')}else{$null})}
            if($reasons.Count -gt 0){$chunkRow['unfinishedReason']=($reasons -join ',')}
            $chunks.Add($chunkRow)
        }
    }
    if(-not $SourceRunId -or -not $SourceCandidateId -or $SourceSha -cnotmatch '^[a-f0-9]{40}$' -or $observed -eq [datetimeoffset]::MinValue){throw 'seed-source-identity-or-time-missing: supply source candidate/run/sha where owner metadata is absent'}
    # Chunk-file drift marks that chunk unfinished. Plan, owner and summary-prefix drift still refuse the export.
    foreach($path in @($inputs.Keys)){
        if((Get-NightlyHistoryDigest $path) -ceq $inputs[$path]){continue}
        if(-not $chunkInputs.ContainsKey($path)){throw 'seed-source-changed'}
        $changedKey=$chunkInputs[$path]
        $inputs.Remove($path)|Out-Null
        $kind=$(if($path.EndsWith('.trx',[StringComparison]::Ordinal)){'trx-changed'}else{'timing-changed'})
        for($i=$chunks.Count-1;$i -ge 0;$i--){
            $row=$chunks[$i]
            if(($row.suite+'/'+$row.chunk) -cne $changedKey){continue}
            $row['completed']=$false
            $prior=[string]$row['unfinishedReason']
            $row['unfinishedReason']=$(if($prior){$prior+','+$kind}else{$kind})
        }
        if($kind -ceq 'trx-changed'){
            for($i=$failed.Count-1;$i -ge 0;$i--){
                $item=$failed[$i]
                if(($item.suite+'/'+$item.chunk) -ceq $changedKey){$failed.RemoveAt($i)}
            }
        }
    }
    if($summaryPrefix -and (Get-NightlyStoppedSummaryPrefixSha $summaryPath $summaryPrefix.Length) -cne $summaryPrefix.Sha){throw 'seed-summary-tampered'}
    function ConvertTo-SeedEvidencePath([string]$Full){
        if(Test-NightlyHistoryUnderRoot $Full $root){return $Full.Substring($root.Length).TrimStart('\','/')}
        if($timingFull -and (Test-NightlyHistoryUnderRoot $Full $timingFull)){
            $rel=$Full.Substring($timingFull.Length).TrimStart('\','/')
            $name=[IO.Path]::GetFileName($timingFull)
            if($rel){return ($name+'/'+$rel)}
            return $name
        }
        throw 'seed-input-outside-source'
    }
    $payload=[ordered]@{sourceObservedAtUtc=$observed.ToString('o');chunks=$chunks.ToArray();failedTests=$failed.ToArray();evidence=@(foreach($path in @($inputs.Keys|Sort-Object)){[ordered]@{path=(ConvertTo-SeedEvidencePath $path);sha256=$inputs[$path]}})}
    $envelope=[ordered]@{format='antiphon-nightly-stopped-seed-v1';seedKind='operator-seed-from-stopped-run';sourceRunId=$SourceRunId;sourceCandidateId=$SourceCandidateId;sourceSha=$SourceSha;createdAtUtc=[datetime]::UtcNow.ToString('o');payload=($payload|ConvertTo-Json -Depth 30 -Compress);contentHash=''}
    $envelope.contentHash=Get-NightlyStoppedSeedHash $envelope
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
