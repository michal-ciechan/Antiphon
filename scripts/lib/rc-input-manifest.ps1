#requires -Version 7.0
# CARD-1836 S3a: object-only, conservative inventory. This is never carry authority.
. (Join-Path $PSScriptRoot 'test-selection.ps1')

function New-RcInputPattern {
    param([string]$Pattern, [switch]$IgnoreCase)
    if (-not $Pattern -or $Pattern.Length -gt 2048 -or $Pattern -match '[\\\x00-\x1f:;\[\]]' -or
        $Pattern.StartsWith('/') -or $Pattern.Split('/') -contains '..') { throw 'invalid-input-pattern' }
    $expression='^'+[regex]::Escape($Pattern).Replace('\*\*/','(?:.*/)?').Replace('\*\*','.*').Replace('\*','[^/]*').Replace('\?','[^/]')+'$'
    $options=[Text.RegularExpressions.RegexOptions]::CultureInvariant
    # A-5: **/NuGet.config is forced full in every case. Other globs stay ordinal.
    if ($IgnoreCase) { $options=$options -bor [Text.RegularExpressions.RegexOptions]::IgnoreCase }
    return [regex]::new($expression,$options,[TimeSpan]::FromMilliseconds(100))
}

function Resolve-RcInventoryPath {
    param([string]$From, [string]$Value, [string]$Project=$From)
    $directory=$Project -replace '[^/]+$',''
    $value=$Value.Replace('\','/')
    if ($value.StartsWith('$(MSBuildThisFileDirectory)',[StringComparison]::Ordinal)) {
        $directory=$From -replace '[^/]+$',''; $value=$value.Substring('$(MSBuildThisFileDirectory)'.Length)
    }
    if (-not $value -or $value -match '[$@%:\x00-\x1f]' -or $value.StartsWith('/')) { throw 'unresolved-expression' }
    $parts=[Collections.Generic.List[string]]::new()
    foreach ($part in ($directory+$value).Split('/')) {
        if ($part -ceq '..') {
            if (-not $parts.Count) { throw 'outside-repository' }; $parts.RemoveAt($parts.Count-1)
        } elseif ($part -and $part -cne '.') { $parts.Add($part) }
    }
    return $parts -join '/'
}

function Assert-RcInputJsonKeys {
    param([Text.Json.JsonElement]$Element)
    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($p in $Element.EnumerateObject()) {
            if (-not $keys.Add($p.Name)) { throw 'duplicate-input-map-key' }
            Assert-RcInputJsonKeys $p.Value
        }
    } elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-RcInputJsonKeys $item }
    }
}

function Test-RcInputNoopPath([string]$Path) {
    if (-not $script:RcInputNoopPatterns) { return $false }
    foreach ($pattern in $script:RcInputNoopPatterns) {
        if ($pattern.IsMatch($Path)) { return $true }
    }
    return $false
}

function Add-RcInventoryInput {
    param($Suite, $Tree, [string]$Path, [string]$Reason, [switch]$FromOwnerRoot)
    # A checked-in no-op still skips the project walk. An explicit owner root
    # wins, and the overlap is a noop-vs-owner conflict the census fails on.
    if (Test-RcInputNoopPath $Path) {
        if (-not $FromOwnerRoot) { return }
        $conflictKey=$Path+'|'+$Reason
        if ($script:RcNoopOwnerConflictKeys.Add($conflictKey)) {
            $script:RcNoopOwnerConflicts.Add($Path+' reason='+$Reason)
        }
    }
    if (-not $Suite.inputMap.ContainsKey($Path)) {
        $entry=$Tree[$Path]
        $Suite.inputMap[$Path]=[ordered]@{version=1;suite=$Suite.id;project=$Suite.project;path=$Path
            mode=$(if ($entry) { $entry.mode } else { $null });type=$(if ($entry) { $entry.type } else { $null })
            objectId=$(if ($entry) { $entry.objectId } else { $null });present=($null -ne $entry);reason=$Reason}
    } else {
        $record=$Suite.inputMap[$Path]
        $record.reason=(Get-TestSelectionSorted @($record.reason.Split(';')+$Reason)) -join ';'
    }
}

function Read-RcInputInventory {
    param([string]$RepositoryRoot, [string]$Sha, $DerivedIndex, [switch]$SkipUnresolvedBroadening)
    if ($Sha -cnotmatch '^[a-f0-9]{40}$' -or (Invoke-TestSelectionGit $RepositoryRoot @('cat-file','-t',$Sha)).Stdout.Trim() -cne 'commit') { throw 'full-commit-required' }
    $script:RcInputNoopPatterns=@()
    $script:RcNoopOwnerConflicts=New-Object System.Collections.Generic.List[string]
    $script:RcNoopOwnerConflictKeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $script:RcParentProbeSha=$null
    $script:RcReferencedProjects=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $tree=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $folded=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $unresolved=[Collections.Generic.List[string]]::new()
    foreach ($line in (Invoke-TestSelectionGit $RepositoryRoot @('ls-tree','-r','-z','--full-tree',$Sha)).Stdout.Split([char]0)) {
        if (-not $line) { continue }
        if ($line -cnotmatch '^([0-7]{6}) (blob|commit) ([a-f0-9]{40})\t([^\x00-\x1f]+)$') { throw 'invalid-input-tree' }
        $path=$Matches[4]
        if ($path.StartsWith('/') -or $path.Contains('\') -or $path.Split('/') -contains '..') { throw 'invalid-input-path' }
        if (-not $folded.Add($path)) { $unresolved.Add("case-collision:$path") }
        $tree.Add($path,@{mode=$Matches[1];type=$Matches[2];objectId=$Matches[3]})
        if ($Matches[1] -cnotin @('100644','100755')) { $unresolved.Add("unresolved-link-or-submodule:$path") }
        # Filter drivers / LFS may substitute bytes outside the committed tree.
        if ($path -match '(^|/)\.gitattributes$') { $unresolved.Add("external-content-contract:$path") }
    }
    $mapPath='scripts/lib/rc-input-owners.json'
    if (-not $tree.ContainsKey($mapPath) -or $tree[$mapPath].mode -cne '100644') { throw 'missing-regular-input-map' }
    $mapText=Get-TestSelectionBlob $RepositoryRoot $Sha $mapPath
    if ([Text.Encoding]::UTF8.GetByteCount($mapText) -gt 1048576) { throw 'input-map-too-large' }
    $json=[Text.Json.JsonDocument]::Parse($mapText)
    try { Assert-RcInputJsonKeys $json.RootElement } finally { $json.Dispose() }
    $map=ConvertFrom-Json -InputObject $mapText -AsHashtable
    Assert-TestSelectionKeys $map @('schemaVersion','suites','suiteProjects','roots','testOnly')
    if (($map.schemaVersion -isnot [long] -and $map.schemaVersion -isnot [int]) -or $map.schemaVersion -ne 1) { throw 'input-map-version' }
    if ($map.roots -isnot [array] -or ($map.Contains('suites') -eq $map.Contains('suiteProjects'))) { throw 'input-map-shape' }
    $suiteRows=@()
    if ($map.Contains('suites')) {
        if ($map.suites -isnot [array]) { throw 'input-map-suites' }; $suiteRows=@($map.suites)
    } else {
        $pattern=New-RcInputPattern $map.suiteProjects
        foreach ($path in (Get-TestSelectionSorted @($tree.Keys))) {
            if ($pattern.IsMatch($path) -and $path.EndsWith('.csproj',[StringComparison]::Ordinal)) { $suiteRows+=@{id=$path;project=$path} }
        }
    }
    $suites=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $suiteFold=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $suiteRows) {
        Assert-TestSelectionKeys $row @('id','project')
        if ($row.id -isnot [string] -or [string]::IsNullOrWhiteSpace($row.id) -or -not $suiteFold.Add($row.id) -or
            $row.project -isnot [string] -or -not $tree.ContainsKey($row.project) -or -not $row.project.EndsWith('.csproj',[StringComparison]::Ordinal)) { throw 'invalid-input-suite' }
        $suites.Add($row.id,@{id=$row.id;project=$row.project
            inputMap=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
            edges=[Collections.Generic.List[object]]::new()})
    }
    if (-not $suites.Count) { throw 'input-suites-empty' }
    $noopPatterns=[Collections.Generic.List[regex]]::new()
    $noopPath='scripts/lib/rc-input-noop.json'
    if ($tree.ContainsKey($noopPath)) {
        if ($tree[$noopPath].mode -cne '100644') { throw 'missing-regular-noop-map' }
        $noopText=Get-TestSelectionBlob $RepositoryRoot $Sha $noopPath
        if ([Text.Encoding]::UTF8.GetByteCount($noopText) -gt 1048576) { throw 'noop-map-too-large' }
        $noopJson=[Text.Json.JsonDocument]::Parse($noopText)
        try { Assert-RcInputJsonKeys $noopJson.RootElement } finally { $noopJson.Dispose() }
        $noop=ConvertFrom-Json -InputObject $noopText -AsHashtable
        Assert-TestSelectionKeys $noop @('schemaVersion','rows')
        if (($noop.schemaVersion -isnot [long] -and $noop.schemaVersion -isnot [int]) -or $noop.schemaVersion -ne 1) { throw 'noop-map-version' }
        if ($noop.rows -isnot [array]) { throw 'noop-map-shape' }
        foreach ($row in @($noop.rows)) {
            Assert-TestSelectionKeys $row @('path','reason','evidence')
            if ($row.path -isnot [string] -or -not $row.path -or $row.reason -isnot [string] -or -not $row.reason -or
                $row.evidence -isnot [string] -or -not $row.evidence) { throw 'invalid-noop-row' }
            $noopPatterns.Add((New-RcInputPattern $row.path))
        }
    }
    $script:RcInputNoopPatterns=$noopPatterns
    $paths=@(Get-TestSelectionSorted @($tree.Keys))
    # ** with unmatched-path-fallback, or any ** whose consumers are *, is the
    # retired broad fallback. Ignore it. A ** row with reason repo-walk names
    # the suites that read every non-no-op path, including paths another suite owns.
    foreach ($root in $map.roots) {
        Assert-TestSelectionKeys $root @('path','consumers','reason','except','walker')
        if ($root.path -isnot [string] -or $root.reason -isnot [string] -or -not $root.reason -or
            $root.consumers -isnot [array] -or -not $root.consumers.Count) { throw 'invalid-input-root' }
        if ($root.Contains('except') -and ($root.except -isnot [array])) { throw 'invalid-input-root' }
        if ($root.Contains('walker') -and ($root.walker -isnot [bool])) { throw 'invalid-input-root' }
        foreach ($id in $root.consumers) {
            if ($id -isnot [string] -or ($id -cne '*' -and -not $suites.ContainsKey($id))) { throw 'unknown-input-consumer' }
        }
        if ($root.path -ceq '**') {
            $star=$false
            foreach ($id in $root.consumers) { if ($id -ceq '*') { $star=$true } }
            if ($root.reason -ceq 'unmatched-path-fallback' -or $star) { continue }
            if ($root.reason -cne 'repo-walk') { throw 'invalid-broad-root' }
            foreach ($id in $root.consumers) {
                foreach ($path in $paths) { Add-RcInventoryInput $suites[$id] $tree $path $root.reason }
            }
            continue
        }
        $pattern=New-RcInputPattern $root.path -IgnoreCase:($root.path -match '(?i)(^|/)nuget\.config$')
        $except=[Collections.Generic.List[regex]]::new()
        if ($root.Contains('except')) {
            foreach ($item in @($root.except)) {
                if ($item -isnot [string] -or -not $item) { throw 'invalid-input-root' }
                $except.Add((New-RcInputPattern $item))
            }
        }
        foreach ($id in $root.consumers) {
            $consumers=if ($id -ceq '*') { @($suites.Values) } else { @($suites[$id]) }
            foreach ($path in $paths) {
                if (-not $pattern.IsMatch($path)) { continue }
                $carved=$false
                foreach ($ex in $except) { if ($ex.IsMatch($path)) { $carved=$true; break } }
                if ($carved) { continue }
                foreach ($suite in $consumers) { Add-RcInventoryInput $suite $tree $path $root.reason -FromOwnerRoot }
            }
        }
    }
    $documents=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($suite in $suites.Values) {
        Add-RcInventoryInput $suite $tree $mapPath 'ownership-contract'
        foreach ($path in @('global.json','NuGet.config','Directory.Packages.props')) { Add-RcInventoryInput $suite $tree $path 'root-build-contract' }
        $projects=[Collections.Generic.Queue[string]]::new(); $projects.Enqueue($suite.project)
        $seenProjects=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        while ($projects.Count) {
            $project=$projects.Dequeue()
            if (-not $seenProjects.Add($project)) { continue }
            if (-not $tree.ContainsKey($project)) { $unresolved.Add("missing-project:$project"); continue }
            $directory=$project -replace '[^/]+$',''
            foreach ($path in $paths) {
                if ($path.StartsWith($directory,[StringComparison]::Ordinal)) { Add-RcInventoryInput $suite $tree $path "project-tree:$project" }
            }
            Add-RcInventoryInput $suite $tree ($directory+'packages.lock.json') 'package-lock-or-resolved-receipt-required'
            $queue=[Collections.Generic.Queue[string]]::new(); $queue.Enqueue($project)
            $ancestor=$directory.TrimEnd('/')
            while ($true) {
                foreach ($name in @('Directory.Build.props','Directory.Build.targets','Directory.Packages.props')) {
                    $path=if ($ancestor) { "$ancestor/$name" } else { $name }
                    Add-RcInventoryInput $suite $tree $path 'ancestor-build-input'
                    if ($tree.ContainsKey($path)) { $queue.Enqueue($path) }
                }
                if (-not $ancestor) { break }; $ancestor=$ancestor -replace '(^|/)[^/]+$',''
            }
            $seenDocuments=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            while ($queue.Count) {
                $input=$queue.Dequeue()
                if (-not $seenDocuments.Add($input)) { continue }
                Add-RcInventoryInput $suite $tree $input 'project-or-import'
                try {
                    if (-not $tree.ContainsKey($input) -or $tree[$input].type -cne 'blob' -or $tree[$input].mode -cnotin @('100644','100755')) { throw 'missing-regular-project-input' }
                    $oid=$tree[$input].objectId
                    if (-not $documents.ContainsKey($oid)) {
                        $settings=[Xml.XmlReaderSettings]::new(); $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit; $settings.XmlResolver=$null; $settings.MaxCharactersInDocument=1048576
                        $reader=[Xml.XmlReader]::Create([IO.StringReader]::new((Get-TestSelectionBlob $RepositoryRoot $Sha $input)),$settings)
                        try { $document=[Xml.XmlDocument]::new(); $document.XmlResolver=$null; $document.Load($reader); $documents[$oid]=$document } finally { $reader.Dispose() }
                    }
                    $document=$documents[$oid]
                    if ($document.DocumentElement.LocalName -cne 'Project') { throw 'invalid-project-xml' }
                    if ($document.SelectNodes("//*[local-name()='Target' or local-name()='UsingTask' or local-name()='Sdk' or local-name()='MSBuild']").Count) { $unresolved.Add("dynamic-build:$input") }
                    foreach ($node in $document.SelectNodes('//*[@Sdk]')) {
                        if ($node.GetAttribute('Sdk') -cnotin @('Microsoft.NET.Sdk','Microsoft.NET.Sdk.Web','Microsoft.NET.Sdk.Worker','Microsoft.NET.Sdk.Razor')) { $unresolved.Add("external-sdk:$input") }
                    }
                    foreach ($node in $document.SelectNodes("//*[local-name()='Import' or local-name()='ProjectReference' or local-name()='Compile' or local-name()='Content' or local-name()='None' or local-name()='EmbeddedResource' or local-name()='AdditionalFiles' or local-name()='Analyzer']")) {
                        $kind=$node.LocalName
                        $value=if ($kind -ceq 'Import') { $node.GetAttribute('Project') } else { $node.GetAttribute('Include') }
                        if (-not $value) { continue } # Update/Remove never subtract from the conservative tree.
                        foreach ($part in $value.Split(';')) {
                            try {
                                $target=if ($kind -ceq 'Import') { Resolve-RcInventoryPath $input $part } else { Resolve-RcInventoryPath $input $part $project }
                                $pattern=New-RcInputPattern $target
                                $targets=@($paths | Where-Object { $pattern.IsMatch($_) })
                                if ($target -notmatch '[*?]') { $targets=@($target) }
                                if (-not $targets.Count -and $kind -cin @('Import','ProjectReference')) { throw 'missing-glob-target' }
                                foreach ($destination in $targets) {
                                    Add-RcInventoryInput $suite $tree $destination $kind
                                    $suite.edges.Add(@{from=$input;to=$destination;project=$project;reason=$kind})
                                    if (-not $tree.ContainsKey($destination)) { $unresolved.Add("missing-input:$destination") }
                                    if ($kind -ceq 'Import') { $queue.Enqueue($destination) }
                                    if ($kind -ceq 'ProjectReference') {
                                        $projects.Enqueue($destination)
                                        if ($destination.EndsWith('.csproj',[StringComparison]::Ordinal)) { [void]$script:RcReferencedProjects.Add($destination) }
                                    }
                                }
                            } catch { $unresolved.Add("unresolved-$($kind.ToLowerInvariant()):${input}:$($_.Exception.Message)") }
                        }
                    }
                } catch { $unresolved.Add("project-parse:${input}:$($_.Exception.Message)") }
            }
        }
    }
    # Positive index edges add records in BOTH immutable revisions, even for an unchanged path.
    # Unowned is computed after these edges, so an index hit owns the path instead of flagging it.
    $indexBroadened=$false
    if ($null -ne $DerivedIndex) {
        foreach ($edge in @($DerivedIndex.edges)) {
            $path=$edge.path
            if ($path -isnot [string] -or -not $path -or $path -match '[\\:*?\x00-\x1f]' -or
                $path.StartsWith('/') -or $path.Split('/') -contains '..') { $unresolved.Add('index-invalid-input'); continue }
            foreach ($id in @($edge.suites)) {
                if ($null -eq $id -or -not $suites.ContainsKey($id)) { $unresolved.Add('index-unknown-consumer'); continue }
                if (-not $suites[$id].inputMap.ContainsKey($path)) { $indexBroadened=$true }
                Add-RcInventoryInput $suites[$id] $tree $path 'derived-index-positive'
                $suites[$id].edges.Add(@{from='derived-index';to=$path;project=$suites[$id].project;reason='derived-index-positive'})
                if (-not $tree.ContainsKey($path)) { $unresolved.Add("index-missing-input:$path") }
            }
        }
    }
    # Unresolved closure stays a flag. It does not copy every path onto every suite.
    # -SkipUnresolvedBroadening remains so the census call site stays valid.
    $null=$SkipUnresolvedBroadening
    $ownedNow=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($suite in $suites.Values) { foreach ($ownedPath in $suite.inputMap.Keys) { [void]$ownedNow.Add($ownedPath) } }
    $unowned=[Collections.Generic.List[object]]::new()
    foreach ($path in $paths) {
        if ($ownedNow.Contains($path) -or (Test-RcInputNoopPath $path)) { continue }
        $hint='unknown'
        if ($path.EndsWith('.csproj',[StringComparison]::Ordinal)) {
            $suiteProject=$false
            foreach ($suite in $suites.Values) { if ([string]$suite.project -ceq $path) { $suiteProject=$true } }
            # new-csproj means nobody references it and the parent commit does not contain it.
            if (-not $suiteProject -and -not $script:RcReferencedProjects.Contains($path) -and -not (Test-RcPathInParentCommit $RepositoryRoot $Sha $path)) { $hint='new-csproj' }
        } elseif ($path.StartsWith('docs/',[StringComparison]::Ordinal)) { $hint='new-docs-folder' }
        $unowned.Add([ordered]@{path=$path;hint=$hint;reason='unowned-path'})
    }
    $snapshotDecision=($unowned.Count -gt 0) -or ($unresolved.Count -gt 0)
    foreach ($suite in $suites.Values) {
        $suite.inputs=@(foreach ($path in (Get-TestSelectionSorted @($suite.inputMap.Keys))) { $suite.inputMap[$path] })
        $suite.Remove('inputMap')
    }
    return @{format='antiphon-rc-input-snapshot-v1';sha=$Sha;treeId=(Invoke-TestSelectionGit $RepositoryRoot @('rev-parse',"${Sha}^{tree}")).Stdout.Trim()
        mapObjectId=$tree[$mapPath].objectId;indexBroadened=$indexBroadened;suites=@(foreach ($id in (Get-TestSelectionSorted @($suites.Keys))) { $suites[$id] })
        unresolved=@(Get-TestSelectionSorted @($unresolved));unowned=@($unowned.ToArray())
        noopConflicts=@(Get-TestSelectionSorted @($script:RcNoopOwnerConflicts))
        needsOrchestratorDecision=([bool]$snapshotDecision)
        creditBlockedBy=$(if ($snapshotDecision) { 'unowned-paths' } else { $null })}
}

function Test-RcPathInParentCommit {
    param([string]$RepositoryRoot, [string]$Sha, [string]$Path)
    if ($script:RcParentProbeSha -cne $Sha) {
        $script:RcParentProbeSha=$Sha
        $script:RcParentProbe=[Collections.Generic.Dictionary[string,bool]]::new([StringComparer]::Ordinal)
        $parent=Invoke-TestSelectionGit $RepositoryRoot @('rev-parse',"${Sha}^") @(0,128)
        $parentSha=$parent.Stdout.Trim()
        if ($parent.Exit -eq 0 -and $parentSha -match '^[a-f0-9]{40}$') { $script:RcParentSha=$parentSha }
        else { $script:RcParentSha=$null }
    }
    if (-not $script:RcParentSha) { return $false }
    $present=$false
    if (-not $script:RcParentProbe.TryGetValue($Path,[ref]$present)) {
        $probe=Invoke-TestSelectionGit $RepositoryRoot @('cat-file','-e',"$($script:RcParentSha):${Path}") @(0,128)
        $present=($probe.Exit -eq 0)
        $script:RcParentProbe[$Path]=$present
    }
    return $present
}

function Format-RcUnownedCensusLines {
    param($Unowned, [string]$ListPath)
    $rows=@($Unowned | Where-Object { $null -ne $_ })
    $lines=New-Object System.Collections.Generic.List[string]
    $lines.Add('UNOWNED-COUNT '+$rows.Count)
    $limit=[Math]::Min(40, $rows.Count)
    for ($i=0; $i -lt $limit; $i++) {
        $lines.Add('UNOWNED-PATH '+[string]$rows[$i].hint+' '+[string]$rows[$i].path)
    }
    if ($rows.Count -gt 40) { $lines.Add('UNOWNED-LIST '+$ListPath) }
    return @($lines)
}

function Get-RcClassCounts([string]$Text) {
    $counts=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    if (-not $Text) { return $counts }
    foreach ($line in ($Text -split '\n')) {
        $trim=$line.Trim().TrimEnd("`r")
        if ($trim.StartsWith('//',[StringComparison]::Ordinal) -or $trim.StartsWith('*',[StringComparison]::Ordinal) -or $trim.StartsWith('/*',[StringComparison]::Ordinal)) { continue }
        foreach ($match in [regex]::Matches($trim,'(?<![A-Za-z0-9_])class\s+([A-Za-z_][A-Za-z0-9_]*)')) {
            $name=$match.Groups[1].Value
            if (-not $counts.ContainsKey($name)) { $counts[$name]=0 }
            $counts[$name]++
        }
    }
    return $counts
}

function Get-RcCommitFileText {
    param([string]$RepositoryRoot, [string]$Sha, [string]$Path)
    if ($null -eq $script:RcBlobText) { $script:RcBlobText=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal) }
    $key=$Sha+'|'+$Path
    $text=$null
    if ($script:RcBlobText.TryGetValue($key,[ref]$text)) { return $text }
    $probe=Invoke-TestSelectionGit $RepositoryRoot @('cat-file','-e',"${Sha}:${Path}") @(0,128)
    $text= if ($probe.Exit -eq 0) { Get-TestSelectionBlob $RepositoryRoot $Sha $Path } else { '' }
    $script:RcBlobText[$key]=$text
    return $text
}

function Test-RcCountMapsEqual($Left, $Right) {
    if ($Left.Count -ne $Right.Count) { return $false }
    foreach ($name in $Left.Keys) {
        if (-not $Right.ContainsKey($name) -or $Right[$name] -ne $Left[$name]) { return $false }
    }
    return $true
}

function Get-RcAddedClassNames {
    param([string]$RepositoryRoot, [string]$BaselineSha, [string]$CandidateSha, [string]$Path)
    $before=Get-RcClassCounts (Get-RcCommitFileText $RepositoryRoot $BaselineSha $Path)
    $after=Get-RcClassCounts (Get-RcCommitFileText $RepositoryRoot $CandidateSha $Path)
    $names=New-Object System.Collections.Generic.List[string]
    foreach ($name in $after.Keys) {
        $seen=0
        if ($before.ContainsKey($name)) { $seen=[int]$before[$name] }
        for ($i=$seen; $i -lt [int]$after[$name]; $i++) { $names.Add($name) }
    }
    return $names
}

function Test-RcStructuralChange {
    param([string]$RepositoryRoot, [string]$BaselineSha, [string]$CandidateSha, [string]$Path, [string]$Status)
    # File add/remove selects walkers. A class-declaration multiset change does too.
    # A method body or signature edit does not: see the test-only checkpoint note.
    if ($Status -cin @('A','D','T')) { return $true }
    $before=Get-RcClassCounts (Get-RcCommitFileText $RepositoryRoot $BaselineSha $Path)
    $after=Get-RcClassCounts (Get-RcCommitFileText $RepositoryRoot $CandidateSha $Path)
    return -not (Test-RcCountMapsEqual $before $after)
}

function Get-RcTestOnlyPolicy {
    param([string]$RepositoryRoot, [string]$Sha)
    $map=ConvertFrom-Json -InputObject (Get-TestSelectionBlob $RepositoryRoot $Sha 'scripts/lib/rc-input-owners.json') -AsHashtable
    $disabled=@{enabled=$false;paths=@();exceptPaths=@();exceptReasons=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);productEdges=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);roots=@()}
    if (-not $map.Contains('testOnly') -or $null -eq $map.testOnly) { return $disabled }
    $policy=$map.testOnly
    Assert-TestSelectionKeys $policy @('enabled','paths','exceptPaths','exceptReasons','productEdgeReasons')
    if ($policy.enabled -isnot [bool]) { throw 'invalid-test-only-class' }
    foreach ($name in @('paths','exceptPaths','exceptReasons','productEdgeReasons')) {
        if ($policy[$name] -isnot [array]) { throw 'invalid-test-only-class' }
    }
    $exceptReasons=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($reason in @($policy.exceptReasons)) {
        if ($reason -isnot [string] -or -not $reason) { throw 'invalid-test-only-class' }
        [void]$exceptReasons.Add($reason)
    }
    $productEdges=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($reason in @($policy.productEdgeReasons)) {
        if ($reason -isnot [string] -or -not $reason) { throw 'invalid-test-only-class' }
        [void]$productEdges.Add($reason)
    }
    $roots=New-Object System.Collections.Generic.List[object]
    foreach ($root in @($map.roots)) {
        $except=[Collections.Generic.List[regex]]::new()
        if ($root.Contains('except')) {
            foreach ($item in @($root.except)) { $except.Add((New-RcInputPattern $item)) }
        }
        $roots.Add(@{pattern=(New-RcInputPattern $root.path -IgnoreCase:($root.path -match '(?i)(^|/)nuget\.config$'));except=@($except);reason=[string]$root.reason;walker=([bool]$root.walker);consumers=@($root.consumers)})
    }
    # List[object] cannot be passed through @(): PowerShell throws "Argument types do not match".
    $pathPatterns=[Collections.Generic.List[regex]]::new()
    foreach ($item in @($policy.paths)) {
        if ($item -isnot [string] -or -not $item) { throw 'invalid-test-only-class' }
        $pathPatterns.Add((New-RcInputPattern $item))
    }
    $exceptPathPatterns=[Collections.Generic.List[regex]]::new()
    foreach ($item in @($policy.exceptPaths)) {
        if ($item -isnot [string] -or -not $item) { throw 'invalid-test-only-class' }
        $exceptPathPatterns.Add((New-RcInputPattern $item))
    }
    return @{enabled=[bool]$policy.enabled;paths=$pathPatterns.ToArray();exceptPaths=$exceptPathPatterns.ToArray();exceptReasons=$exceptReasons;productEdges=$productEdges;roots=$roots.ToArray()}
}

function Test-RcRootCarves($Root, [string]$Path) {
    foreach ($pattern in @($Root.except)) { if ($pattern.IsMatch($Path)) { return $true } }
    return $false
}

function Test-RcTestOnlyPath {
    param([string]$Path, $Policy)
    if (-not $Policy.enabled) { return $false }
    $hit=$false
    foreach ($pattern in @($Policy.paths)) { if ($pattern.IsMatch($Path)) { $hit=$true } }
    if (-not $hit) { return $false }
    foreach ($pattern in @($Policy.exceptPaths)) { if ($pattern.IsMatch($Path)) { return $false } }
    foreach ($root in @($Policy.roots)) {
        if (-not $root.pattern.IsMatch($Path) -or (Test-RcRootCarves $root $Path)) { continue }
        if ($Policy.exceptReasons.Contains([string]$root.reason)) { return $false }
    }
    return $true
}

function Test-RcProductEdgeReason {
    param([string]$Reason, $Policy)
    if (-not $Reason) { return $false }
    foreach ($part in $Reason.Split(';')) { if ($Policy.productEdges.Contains($part)) { return $true } }
    return $false
}

function Test-RcWalkerSuite {
    param([string]$SuiteId, [string]$Path, $Policy)
    foreach ($root in @($Policy.roots)) {
        if (-not $root.walker -or -not $root.pattern.IsMatch($Path) -or (Test-RcRootCarves $root $Path)) { continue }
        if (@($root.consumers) -ccontains $SuiteId -or @($root.consumers) -ccontains '*') { return $true }
    }
    return $false
}

function Get-RcContainingSuiteId {
    param([string]$Path, $Suites)
    $best=$null
    $length=-1
    foreach ($suite in @($Suites)) {
        $directory=[string]$suite.project -replace '[^/]+$',''
        if ($directory -and $Path.StartsWith($directory,[StringComparison]::Ordinal) -and $directory.Length -gt $length) {
            $best=[string]$suite.id
            $length=$directory.Length
        }
    }
    return $best
}

function Get-RcChangedInputPaths($Left, $Right) {
    $before=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $after=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($input in @($Left.inputs)) { if ($null -ne $input) { $before[[string]$input.path]=$input } }
    foreach ($input in @($Right.inputs)) { if ($null -ne $input) { $after[[string]$input.path]=$input } }
    $changed=New-Object System.Collections.Generic.List[string]
    foreach ($path in (Get-TestSelectionSorted @(@($before.Keys)+@($after.Keys)))) {
        $left=$null; $right=$null
        $hasLeft=$before.TryGetValue($path,[ref]$left)
        $hasRight=$after.TryGetValue($path,[ref]$right)
        if (-not $hasLeft -or -not $hasRight) { $changed.Add($path); continue }
        if ([string]$left.objectId -cne [string]$right.objectId -or [bool]$left.present -ne [bool]$right.present) { $changed.Add($path) }
    }
    return $changed
}

function Get-RcInputManifest {
    param([string]$RepositoryRoot, [string]$BaselineSha, [string]$CandidateSha, $DerivedIndex)
    $old=Read-RcInputInventory $RepositoryRoot $BaselineSha $DerivedIndex
    $new=Read-RcInputInventory $RepositoryRoot $CandidateSha $DerivedIndex
    $unresolved=[Collections.Generic.List[string]]::new()
    foreach ($reason in @($old.unresolved)+@($new.unresolved)) { $unresolved.Add($reason) }
    if ((Invoke-TestSelectionGit $RepositoryRoot @('merge-base','--is-ancestor',$BaselineSha,$CandidateSha) @(0,1)).Exit -ne 0) { $unresolved.Add('baseline-not-ancestor') }
    if ($old.mapObjectId -cne $new.mapObjectId) { $unresolved.Add('ownership-map-changed') }
    $dirty=[Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    $changes=[Collections.Generic.List[object]]::new()
    $diff=(Invoke-TestSelectionGit $RepositoryRoot @('diff','--no-ext-diff','--no-renames','--name-status','-z',$BaselineSha,$CandidateSha,'--')).Stdout.Split([char]0)
    for ($i=0;$i -lt $diff.Count-1;$i+=2) {
        if ($diff[$i] -cnotmatch '^[ADMTUXB]$' -or -not $diff[$i+1]) { throw 'invalid-input-diff' }
        $changes.Add(@{status=$diff[$i];path=$diff[$i+1]})
    }
    # The union records absence explicitly and includes old edges, added inputs and retired inputs.
    $ids=@(Get-TestSelectionSorted @(@($old.suites.id)+@($new.suites.id)))
    foreach ($id in $ids) {
        $before=@($old.suites | Where-Object id -CEQ $id)
        $after=@($new.suites | Where-Object id -CEQ $id)
        if (-not $before.Count -or -not $after.Count) { [void]$dirty.Add($id); continue }
        $left=$before[0]; $right=$after[0]
        $union=@(Get-TestSelectionSorted @(@($left.inputs.path)+@($right.inputs.path)))
        foreach ($side in @($left,$right)) {
            $present=[Collections.Generic.HashSet[string]]::new([string[]]@($side.inputs.path),[StringComparer]::Ordinal)
            foreach ($path in $union) {
                if (-not $present.Contains($path)) { $side.inputs+=@{version=1;suite=$id;project=$side.project;path=$path;mode=$null;type=$null;objectId=$null;present=$false;reason='absent-from-revision-closure'} }
            }
            $side.inputs=@($side.inputs | Sort-Object path -CaseSensitive)
        }
        if ((ConvertTo-TestSelectionCanonicalJson $left.inputs) -cne (ConvertTo-TestSelectionCanonicalJson $right.inputs)) { [void]$dirty.Add($id) }
    }
    $broadened=$old.indexBroadened -or $new.indexBroadened
    if ($null -ne $DerivedIndex) {
        foreach ($edge in @($DerivedIndex.edges)) {
            if (@($changes.path) -cnotcontains $edge.path) { continue }
            foreach ($id in @($edge.suites)) {
                if ($ids -cnotcontains $id) { $unresolved.Add('index-unknown-consumer'); continue }
                if ($dirty.Add($id)) { $broadened=$true }
            }
        }
    }
    $unownedByPath=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($snap in @($old,$new)) {
        foreach ($row in @($snap.unowned)) {
            if ($null -eq $row) { continue }
            $unownedPath=[string]$row.path
            if ($unownedPath) { $unownedByPath[$unownedPath]=$row }
        }
    }
    $script:RcBlobText=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $policy=Get-RcTestOnlyPolicy $RepositoryRoot $CandidateSha
    $deferred=[Collections.Generic.List[string]]::new()
    $notRun=[Collections.Generic.List[string]]::new()
    if ($policy.enabled) {
        $status=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
        foreach ($change in @($changes)) { $status[[string]$change.path]=[string]$change.status }
        $remove=New-Object System.Collections.Generic.List[string]
        foreach ($id in @($dirty)) {
            $before=@($old.suites | Where-Object id -CEQ $id)
            $after=@($new.suites | Where-Object id -CEQ $id)
            if (-not $before.Count -or -not $after.Count) { continue }
            $changedPaths=@(Get-RcChangedInputPaths $before[0] $after[0])
            if (-not $changedPaths.Count) { continue }
            $keep=$false
            $deferredPaths=New-Object System.Collections.Generic.List[string]
            foreach ($path in $changedPaths) {
                if (-not (Test-RcTestOnlyPath $path $policy)) { $keep=$true; continue }
                $deferredPaths.Add($path)
                $record=$null
                foreach ($input in @($after[0].inputs)+@($before[0].inputs)) {
                    if ($null -ne $input -and [string]$input.path -ceq $path -and $input.present) { $record=$input; break }
                }
                if ($null -ne $record -and (Test-RcProductEdgeReason ([string]$record.reason) $policy)) { $keep=$true; continue }
                $changeStatus= if ($status.ContainsKey($path)) { $status[$path] } else { 'M' }
                if ((Test-RcStructuralChange $RepositoryRoot $BaselineSha $CandidateSha $path $changeStatus) -and (Test-RcWalkerSuite $id $path $policy)) { $keep=$true }
            }
            if (-not $keep) {
                $remove.Add($id)
                foreach ($path in $deferredPaths) { $deferred.Add($id+':'+$path) }
            }
        }
        foreach ($id in $remove) { [void]$dirty.Remove($id) }
        foreach ($change in @($changes)) {
            $path=[string]$change.path
            if (-not (Test-RcTestOnlyPath $path $policy)) { continue }
            $names=@(Get-RcAddedClassNames $RepositoryRoot $BaselineSha $CandidateSha $path)
            if (-not $names.Count) { continue }
            $suiteId=Get-RcContainingSuiteId $path @($new.suites)
            if (-not $suiteId) { continue }
            foreach ($name in $names) { $notRun.Add($suiteId+':'+$path+':'+$name) }
        }
    }
    $manifestUnowned=@(foreach ($unownedPath in (Get-TestSelectionSorted @($unownedByPath.Keys))) { $unownedByPath[$unownedPath] })
    $conflictRows=New-Object System.Collections.Generic.List[string]
    foreach ($snap in @($old,$new)) { foreach ($row in @($snap.noopConflicts)) { if ($row) { $conflictRows.Add([string]$row) } } }
    $sortedUnresolved=@(Get-TestSelectionSorted @($unresolved))
    $cutDecision=($manifestUnowned.Count -gt 0) -or ($sortedUnresolved.Count -gt 0)
    return @{format='antiphon-rc-input-inventory-v1'; baselineSha=$BaselineSha; candidateSha=$CandidateSha
        snapshots=@($old,$new);changes=$changes.ToArray();dirtySuites=@($dirty);unresolved=$sortedUnresolved
        unowned=$manifestUnowned;needsOrchestratorDecision=([bool]$cutDecision)
        testOnlyDeferred=@(Get-TestSelectionSorted @($deferred));changedNotRun=@(Get-TestSelectionSorted @($notRun))
        noopConflicts=@(Get-TestSelectionSorted @($conflictRows))
        creditBlockedBy=$(if ($cutDecision) { 'unowned-paths' } else { $null })
        index=@{version=$(if ($null -ne $DerivedIndex) { $DerivedIndex.version } else { $null });broadened=$broadened;authority='positive-only'}
        requiredActualReceipts=@('sdk-runtime-os-architecture','powershell-node-browser-native','configuration-environment',
            'resolved-package-graph-and-content','generated-fixture-recipes-and-outputs','immutable-images','discovery-case-manifest')
        activation='shadow-only'; executionEvidence=$false; gateCredit=$false}
}
