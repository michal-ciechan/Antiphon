#requires -Version 7.0
# CARD-1313. Selection intent is data, never execution or release authority.
function ConvertTo-TestSelectionCanonicalJson {
    param($Value)
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string]) { return ConvertTo-Json -InputObject $Value -Compress }
    if ($Value -is [bool]) { return $Value.ToString().ToLowerInvariant() }
    if ($Value -is [System.Collections.IDictionary]) {
        $keys = [string[]]@($Value.Keys); [Array]::Sort($keys, [StringComparer]::Ordinal)
        $parts = foreach ($key in $keys) { (ConvertTo-TestSelectionCanonicalJson $key) + ':' + (ConvertTo-TestSelectionCanonicalJson $Value[$key]) }
        return '{' + ($parts -join ',') + '}'
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $parts = foreach ($item in $Value) { ConvertTo-TestSelectionCanonicalJson $item }
        return '[' + ($parts -join ',') + ']'
    }
    if ($Value -is [ValueType]) { return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture) }
    $map = @{}; foreach ($p in $Value.PSObject.Properties) { $map[$p.Name] = $p.Value }
    return ConvertTo-TestSelectionCanonicalJson $map
}

function Get-TestSelectionDigest {
    param($Value)
    # Estimates are attached after the digest. Hashing an augmented object stays stable.
    if ($Value -is [System.Collections.IDictionary] -and ($Value.Contains('estimates') -or $Value.Contains('budgetObservations'))) {
        $copy = [ordered]@{}
        foreach ($key in @($Value.Keys)) {
            if ($key -cne 'estimates' -and $key -cne 'budgetObservations') { $copy[$key] = $Value[$key] }
        }
        $Value = $copy
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-TestSelectionCanonicalJson $Value))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function ConvertTo-TestSelectionLfText {
    param([string]$Text)
    # CR and CRLF become LF. Spaces, tabs and case stay, so an edit cannot pass.
    if ($null -eq $Text) { return '' }
    return $Text.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Get-TestSelectionCatalogHash {
    param($Catalog)
    $copy = @{}; foreach ($key in $Catalog.Keys) { if ($key -cne 'catalogHash') { $copy[$key] = $Catalog[$key] } }
    return Get-TestSelectionDigest $copy
}

function Assert-TestSelectionKeys {
    param($Object, [string[]]$Allowed)
    if ($Object -isnot [Collections.IDictionary]) { throw 'invalid-catalog-object' }
    foreach ($key in $Object.Keys) { if ($Allowed -cnotcontains $key) { throw "invalid-catalog-field:$key" } }
}

function Assert-TestSelectionCatalog {
    param($Catalog)
    Assert-TestSelectionKeys $Catalog @('schemaVersion','catalogHash','sets','rules','prerequisites','edges','activation')
    if ($Catalog.schemaVersion -isnot [long] -and $Catalog.schemaVersion -isnot [int]) { throw 'catalog-schema-unsupported' }
    if ($Catalog.schemaVersion -ne 1) { throw 'catalog-schema-unsupported' }
    if ($Catalog.catalogHash -cne (Get-TestSelectionCatalogHash $Catalog)) { throw 'catalog-hash-stale' }
    if ($Catalog.activation -cne 'unqualified') { throw 'selection-cannot-qualify-itself' }
    $sets = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($set in $Catalog.sets) {
        Assert-TestSelectionKeys $set @('id','members','remainderProjects','sourceInputs','dependsOn','resources','platforms','categories','eligibility','owner','reason','prerequisites')
        if ([string]$set.id -notmatch '^[a-z0-9.-]+$' -or $sets.ContainsKey($set.id)) { throw 'invalid-set-id' }
        $sets.Add($set.id,$set)
        if ($set.eligibility -cnotin @('unattended','manual','unclassified')) { throw 'invalid-eligibility' }
        if (-not $set.owner -or -not $set.reason -or @($set.platforms).Count -eq 0) { throw 'missing-disposition' }
        Assert-TestSelectionKeys $set.resources @('vendorExecutable','interaction','api','browser','desktopUi','nativePty','postgres','docker','broker')
        foreach ($key in @('vendorExecutable','interaction','api','browser','desktopUi','nativePty','postgres','docker','broker')) {
            if (-not $set.resources.Contains($key)) { throw 'unknown-resource' }
            if ($set.resources[$key] -is [string] -and $set.resources[$key] -ceq 'unknown' -and $set.eligibility -ceq 'unattended') { throw 'unknown-resource' }
        }
        if ($set.resources.vendorExecutable -cnotin @('none','claude','codex','grok','unknown') -or $set.resources.interaction -cnotin @('none','native-probe','tui','unknown') -or $set.resources.api -cnotin @('none','stub','live','unknown')) { throw 'invalid-resource' }
        foreach ($key in @('browser','desktopUi','nativePty','postgres','docker','broker')) {
            if ($set.resources[$key] -isnot [bool] -and $set.resources[$key] -cne 'unknown') { throw 'invalid-resource' }
        }
        foreach ($member in $set.members) {
            Assert-TestSelectionKeys $member @('project','class','method','script','source')
            if (-not $member.project -and -not $member.script) { throw 'invalid-member' }
            foreach ($key in @('class','method')) { if ($member[$key] -and $member[$key] -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$') { throw 'invalid-member' } }
        }
    }
    if (-not $sets.ContainsKey('core')) { throw 'missing-core' }
    $prereqs = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($p in $Catalog.prerequisites) {
        Assert-TestSelectionKeys $p @('id','dependsOn','reason')
        if (-not $p.id -or $prereqs.ContainsKey($p.id)) { throw 'invalid-prerequisite' }
        $prereqs.Add($p.id,$p)
    }
    function Visit-SelectionNode($id,$map,$visiting,$done) {
        if (-not $map.ContainsKey($id)) { throw "missing-dependency:$id" }
        if ($done.Contains($id)) { return }
        if (-not $visiting.Add($id)) { throw "dependency-cycle:$id" }
        foreach ($target in $map[$id].dependsOn) { Visit-SelectionNode $target $map $visiting $done }
        [void]$visiting.Remove($id); [void]$done.Add($id)
    }
    foreach ($map in @($sets,$prereqs)) {
        $visiting = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $done = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($id in $map.Keys) { Visit-SelectionNode $id $map $visiting $done }
    }
    foreach ($set in $sets.Values) { foreach ($p in $set.prerequisites) { if (-not $prereqs.ContainsKey($p)) { throw "missing-prerequisite:$p" } } }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($rule in $Catalog.rules) {
        Assert-TestSelectionKeys $rule @('id','paths','pattern','sets','full','partition')
        if (-not $ids.Add([string]$rule.id)) { throw 'duplicate-rule' }
        if (@($rule.paths | Where-Object { $_ }).Count -eq 0 -and -not $rule.pattern) { throw 'invalid-rule' }
        if ($rule.full -isnot [bool]) { throw 'invalid-rule' }
        if ($rule.pattern) {
            if (-not $rule.pattern.StartsWith('^') -or -not $rule.pattern.EndsWith('$') -or $rule.pattern.Length -gt 2048) { throw 'invalid-rule' }
            try { [void][regex]::new($rule.pattern,[Text.RegularExpressions.RegexOptions]::CultureInvariant,[TimeSpan]::FromMilliseconds(100)) } catch { throw 'invalid-rule' }
        }
        foreach ($s in $rule.sets) { if (-not $sets.ContainsKey($s)) { throw "missing-dependency:$s" } }
    }
    foreach ($edge in $Catalog.edges) {
        Assert-TestSelectionKeys $edge @('id','path','sets','kind')
        if (-not $edge.path -or -not $edge.id -or $edge.kind -cnotin @('linked-compile','staged-child','imported-helper','runtime-bundle')) { throw 'invalid-edge' }
        foreach ($s in $edge.sets) { if (-not $sets.ContainsKey($s)) { throw "missing-dependency:$s" } }
    }
    return $Catalog
}

function Read-TestSelectionCatalog {
    param([string]$Path, [string]$ExpectedHash)
    $catalog = [IO.File]::ReadAllText($Path) | ConvertFrom-Json -AsHashtable
    [void](Assert-TestSelectionCatalog $catalog)
    if ($ExpectedHash -cne $catalog.catalogHash) { throw 'catalog-pin-mismatch' }
    return $catalog
}

function Invoke-TestSelectionGit {
    param([string]$RepositoryRoot, [string[]]$Arguments, [int[]]$AllowedExit = @(0))
    . (Join-Path $PSScriptRoot 'checkpoint-process-runner.ps1')
    $start=[Diagnostics.ProcessStartInfo]::new('git')
    $start.WorkingDirectory=[IO.Path]::GetFullPath($RepositoryRoot)
    $start.StandardOutputEncoding=[Text.UTF8Encoding]::new($false,$true)
    foreach($arg in $Arguments){$start.ArgumentList.Add($arg)}
    $result=[Antiphon.Checkpoints.CheckpointProcessRunner]::Run($start,30000,$true)
    if($result.TimedOut -or $AllowedExit -notcontains $result.Exit){throw "git-read-failed:$($Arguments[0]):$($result.Exit)"}
    if($result.Stdout.Length -gt 16777216){throw 'git-output-too-large'}
    return $result
}

function Get-TestSelectionBlob {
    param([string]$Root,[string]$Sha,[string]$Path)
    return (Invoke-TestSelectionGit $Root @('cat-file','blob',"${Sha}:$Path")).Stdout
}

function Get-TestSelectionSorted {
    param([object[]]$Values)
    $set=[Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach($v in $Values){if($null -ne $v){[void]$set.Add([string]$v)}}
    return @($set)
}

function Get-TestSelection {
    param([string]$RepositoryRoot,[string]$BaselineSha,[string]$CandidateSha,[string]$Profile='rc')
    foreach($sha in @($BaselineSha,$CandidateSha)){
        if($sha -cnotmatch '^[0-9a-fA-F]{40}$'){throw 'full-commit-sha-required'}
        if((Invoke-TestSelectionGit $RepositoryRoot @('cat-file','-t',$sha)).Stdout.Trim() -cne 'commit'){throw 'commit-object-required'}
    }
    $BaselineSha=$BaselineSha.ToLowerInvariant();$CandidateSha=$CandidateSha.ToLowerInvariant()
    $policyPath='tests/test-execution-policy.json'
    $policyText=Get-TestSelectionBlob $RepositoryRoot $CandidateSha $policyPath
    $policy=$policyText|ConvertFrom-Json
    if($policy.schemaVersion -notin @(3,4)){throw 'policy-schema-unsupported'}
    if($policy.policyHash -cne (Get-NightlyPolicyHash $policy)){throw 'policy-hash-stale'}
    [void](Get-NightlyPolicyProfile $policy $Profile)
    Test-NightlyProjectDispositions $policy
    if($policy.selectionCatalog.path -cne 'tests/test-selection-policy.json'){throw 'catalog-path-unsupported'}
    $catalogPath=[string]$policy.selectionCatalog.path
    $catalog=Get-TestSelectionBlob $RepositoryRoot $CandidateSha $catalogPath | ConvertFrom-Json -AsHashtable
    [void](Assert-TestSelectionCatalog $catalog)
    if($policy.selectionCatalog.hash -cne $catalog.catalogHash){throw 'catalog-pin-mismatch'}
    $selectorFiles=@('scripts/select-test-rosters.ps1','scripts/lib/test-selection.ps1')
    $selectorBytes=@{}
    foreach($path in $selectorFiles){
        $pinned=ConvertTo-TestSelectionLfText (Get-TestSelectionBlob $RepositoryRoot $CandidateSha $path)
        $actual=ConvertTo-TestSelectionLfText ([IO.File]::ReadAllText((Join-Path (Split-Path (Split-Path $PSScriptRoot)) $path)))
        if($pinned -cne $actual){throw 'selector-source-mismatch'}
        $selectorBytes[$path]=Get-TestSelectionDigest $pinned
    }
    $tree=(Invoke-TestSelectionGit $RepositoryRoot @('ls-tree','-r','--name-only','-z',$CandidateSha,'--')).Stdout
    $candidatePaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($path in $tree.Split([char]0)){if($path){[void]$candidatePaths.Add($path)}}
    $baselineCatalog=$null
    $oldTree=(Invoke-TestSelectionGit $RepositoryRoot @('ls-tree','--name-only','-z',$BaselineSha,'--',$catalogPath)).Stdout
    if($oldTree){
        $baselineCatalog=Get-TestSelectionBlob $RepositoryRoot $BaselineSha $catalogPath | ConvertFrom-Json -AsHashtable
        [void](Assert-TestSelectionCatalog $baselineCatalog)
        $baselinePolicy=Get-TestSelectionBlob $RepositoryRoot $BaselineSha $policyPath | ConvertFrom-Json
        if($baselinePolicy.policyHash -cne (Get-NightlyPolicyHash $baselinePolicy) -or $baselinePolicy.selectionCatalog.hash -cne $baselineCatalog.catalogHash){throw 'baseline-policy-hash-stale'}
    }
    $fallback=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if((Invoke-TestSelectionGit $RepositoryRoot @('merge-base','--is-ancestor',$BaselineSha,$CandidateSha) @(0,1)).Exit -eq 1){[void]$fallback.Add('baseline-not-ancestor')}
    $diff=(Invoke-TestSelectionGit $RepositoryRoot @('diff','--no-ext-diff','--name-status','-z','--no-renames',$BaselineSha,$CandidateSha,'--')).Stdout
    $tokens=$diff.Split([char]0)
    if($diff -and ($tokens[-1] -cne '' -or ($tokens.Count % 2) -ne 1)){throw 'malformed-diff'}
    $changes=[Collections.Generic.List[object]]::new()
    for($i=0;$i -lt $tokens.Count-1;$i+=2){
        $status=$tokens[$i];$path=$tokens[$i+1]
        if($status -cnotmatch '^[ADMTUXB]$' -or -not $path -or $path.StartsWith('/') -or $path.Contains([char]0) -or $path.Split('/') -contains '..'){throw 'malformed-diff'}
        $changes.Add(@{path=$path;status=$status})
    }
    $selected=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);[void]$selected.Add('core')
    $setMap=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach($s in $catalog.sets){$setMap.Add($s.id,$s)}
    $records=@();$edges=@()
    foreach($path in (Get-TestSelectionSorted @($changes.path))){
        $change=@($changes|Where-Object {$_.path -ceq $path})[0]
        $roles=if($change.status -ceq 'A'){@('new')}elseif($change.status -ceq 'D'){@('old')}else{@('old','new')}
        foreach($role in $roles){
            $source=if($role -ceq 'old'){$baselineCatalog}else{$catalog}
            $ruleIds=@();$impacts=@();$edgeIds=@()
            if($null -eq $source){[void]$fallback.Add('baseline-catalog-unavailable')}
            else{
                $providerMatch=$false
                foreach($r in $source.rules){if($r.partition -ceq 'provider' -and @($r.paths|Where-Object {$_ -ceq $path}).Count){$providerMatch=$true}}
                foreach($rule in $source.rules){
                    if($providerMatch -and $rule.partition -ceq 'shared'){continue}
                    $match=@($rule.paths|Where-Object {$_ -ceq $path}).Count -gt 0
                    if($rule.pattern){
                        try{$match=$match -or [regex]::new($rule.pattern,[Text.RegularExpressions.RegexOptions]::CultureInvariant,[TimeSpan]::FromMilliseconds(100)).IsMatch($path)}
                        catch [Text.RegularExpressions.RegexMatchTimeoutException]{throw 'invalid-rule-timeout'}
                    }
                    if(-not $match){continue}
                    $ruleIds += $rule.id;$impacts += @($rule.sets)
                    if($rule.full){
                        if($rule.id -cin @('P-AUTHORITY','P-BUILD')){[void]$fallback.Add('selection-authority-changed')}
                        else{[void]$fallback.Add("rule-full:$($rule.id)")}
                    }
                }
                foreach($edge in $source.edges){if($edge.path -ceq $path){$edgeIds+=$edge.id;$impacts+=@($edge.sets);$edges+=@{id=$edge.id;path=$path;role=$role;kind=$edge.kind;sets=@(Get-TestSelectionSorted $edge.sets)}}}
                if($ruleIds.Count -eq 0 -and $edgeIds.Count -eq 0){$ruleIds+= 'P-UNKNOWN';[void]$fallback.Add("unknown-path:$path")}
            }
            foreach($id in $impacts){if($setMap.ContainsKey($id)){[void]$selected.Add($id)}else{[void]$fallback.Add("removed-owner:$id")}}
            $records+=@{path=$path;status=$change.status;role=$role;ruleIds=@(Get-TestSelectionSorted $ruleIds);sets=@(Get-TestSelectionSorted $impacts);edges=@(Get-TestSelectionSorted $edgeIds)}
        }
    }
    if($fallback.Count){foreach($id in $setMap.Keys){[void]$selected.Add($id)}}
    $queue=[Collections.Generic.Queue[string]]::new();foreach($id in $selected){$queue.Enqueue($id)}
    while($queue.Count){$id=$queue.Dequeue();foreach($target in $setMap[$id].dependsOn){$edges+=@{id="set:$id->$target";kind='dependsOn';from=$id;to=$target};if($selected.Add($target)){$queue.Enqueue($target)}}}
    $prereqs=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $prereqMap=@{};foreach($p in $catalog.prerequisites){$prereqMap[$p.id]=$p}
    foreach($id in $selected){foreach($p in $setMap[$id].prerequisites){if($prereqs.Add($p)){$queue.Enqueue($p)}}}
    while($queue.Count){foreach($p in $prereqMap[$queue.Dequeue()].dependsOn){if($prereqs.Add($p)){$queue.Enqueue($p)}}}
    $removed=@();$sets=@()
    foreach($id in (Get-TestSelectionSorted @($selected))){
        $set=$setMap[$id];$surviving=@()
        foreach($m in $set.members){if($m.source -and -not $candidatePaths.Contains($m.source)){$removed+=@{set=$id;member=$m;reason='source-removed'}}else{$surviving+=$m}}
        $copy=@{};foreach($key in $set.Keys){$copy[$key]=$set[$key]};$copy.members=@($surviving|Sort-Object {ConvertTo-TestSelectionCanonicalJson $_} -CaseSensitive)
        $sets+=$copy
    }
    $roots=(Invoke-TestSelectionGit $RepositoryRoot @('rev-list','--max-parents=0',$CandidateSha,'--')).Stdout.Trim().Split("`n")
    $result=[ordered]@{
        format='antiphon-test-selection-v1';repositoryIdentity=(Get-TestSelectionDigest @(Get-TestSelectionSorted $roots));baselineSha=$BaselineSha;candidateSha=$CandidateSha
        policyBlob=(Invoke-TestSelectionGit $RepositoryRoot @('rev-parse',"${CandidateSha}:$policyPath")).Stdout.Trim();policyHash=$policy.policyHash
        catalogBlob=(Invoke-TestSelectionGit $RepositoryRoot @('rev-parse',"${CandidateSha}:$catalogPath")).Stdout.Trim();catalogHash=$catalog.catalogHash
        selectorVersion=1;selectorHash=(Get-TestSelectionDigest $selectorBytes);profile=$Profile;mode=$(if($fallback.Count){'full'}else{'narrow'})
        fallbackReasons=@(Get-TestSelectionSorted @($fallback));changedPaths=@($records);selectedSets=@($sets)
        unselectedSets=@(Get-TestSelectionSorted @($setMap.Keys|Where-Object {-not $selected.Contains($_)}))
        prerequisites=@(Get-TestSelectionSorted @($prereqs));explanationEdges=@($edges|Sort-Object {ConvertTo-TestSelectionCanonicalJson $_} -CaseSensitive)
        unresolvedObligations=@($sets|Where-Object {$_.eligibility -cne 'unattended' -or $_.platforms -cnotcontains 'any'}|ForEach-Object {@{set=$_.id;eligibility=$_.eligibility;platforms=$_.platforms;reason=$_.reason}})
        removedMembers=@($removed);executionEvidence=$false;activation='unqualified'
    }
    $result.digest=Get-TestSelectionDigest $result
    return $result
}
