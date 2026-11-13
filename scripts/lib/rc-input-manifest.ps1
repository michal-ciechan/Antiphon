#requires -Version 7.0
# CARD-1836 S3a: object-only, conservative inventory. This is never carry authority.
. (Join-Path $PSScriptRoot 'test-selection.ps1')

function New-RcInputPattern {
    param([string]$Pattern)
    if (-not $Pattern -or $Pattern.Length -gt 2048 -or $Pattern -match '[\\\x00-\x1f:;\[\]]' -or
        $Pattern.StartsWith('/') -or $Pattern.Split('/') -contains '..') { throw 'invalid-input-pattern' }
    $expression='^'+[regex]::Escape($Pattern).Replace('\*\*/','(?:.*/)?').Replace('\*\*','.*').Replace('\*','[^/]*').Replace('\?','[^/]')+'$'
    return [regex]::new($expression,[Text.RegularExpressions.RegexOptions]::CultureInvariant,[TimeSpan]::FromMilliseconds(100))
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

function Add-RcInventoryInput {
    param($Suite, $Tree, [string]$Path, [string]$Reason)
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
    param([string]$RepositoryRoot, [string]$Sha, $DerivedIndex)
    if ($Sha -cnotmatch '^[a-f0-9]{40}$' -or (Invoke-TestSelectionGit $RepositoryRoot @('cat-file','-t',$Sha)).Stdout.Trim() -cne 'commit') { throw 'full-commit-required' }
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
    Assert-TestSelectionKeys $map @('schemaVersion','suites','suiteProjects','roots')
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
    $paths=@(Get-TestSelectionSorted @($tree.Keys))
    foreach ($root in $map.roots) {
        Assert-TestSelectionKeys $root @('path','consumers','reason')
        if ($root.path -isnot [string] -or $root.reason -isnot [string] -or -not $root.reason -or
            $root.consumers -isnot [array] -or -not $root.consumers.Count) { throw 'invalid-input-root' }
        $pattern=New-RcInputPattern $root.path
        foreach ($id in $root.consumers) {
            if ($id -isnot [string] -or ($id -cne '*' -and -not $suites.ContainsKey($id))) { throw 'unknown-input-consumer' }
            $consumers=if ($id -ceq '*') { @($suites.Values) } else { @($suites[$id]) }
            foreach ($path in $paths) {
                if ($pattern.IsMatch($path)) { foreach ($suite in $consumers) { Add-RcInventoryInput $suite $tree $path $root.reason } }
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
                                    if ($kind -ceq 'ProjectReference') { $projects.Enqueue($destination) }
                                }
                            } catch { $unresolved.Add("unresolved-$($kind.ToLowerInvariant()):${input}:$($_.Exception.Message)") }
                        }
                    }
                } catch { $unresolved.Add("project-parse:${input}:$($_.Exception.Message)") }
            }
        }
    }
    $owned=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($suite in $suites.Values) { foreach ($path in $suite.inputMap.Keys) { [void]$owned.Add($path) } }
    foreach ($path in $paths) { if (-not $owned.Contains($path)) { $unresolved.Add("unknown-path:$path") } }
    # Positive index edges add records in BOTH immutable revisions, even for an unchanged path.
    # Check unknown ownership first: an index hit must never rescue an unowned path from dirty-all.
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
    # Any unresolved closure broadens all suites. It can never become a negative dependency proof.
    if ($unresolved.Count) { foreach ($suite in $suites.Values) { foreach ($path in $paths) { Add-RcInventoryInput $suite $tree $path 'unresolved-conservative-all-inputs' } } }
    foreach ($suite in $suites.Values) {
        $suite.inputs=@(foreach ($path in (Get-TestSelectionSorted @($suite.inputMap.Keys))) { $suite.inputMap[$path] })
        $suite.Remove('inputMap')
    }
    return @{format='antiphon-rc-input-snapshot-v1';sha=$Sha;treeId=(Invoke-TestSelectionGit $RepositoryRoot @('rev-parse',"${Sha}^{tree}")).Stdout.Trim()
        mapObjectId=$tree[$mapPath].objectId;indexBroadened=$indexBroadened;suites=@(foreach ($id in (Get-TestSelectionSorted @($suites.Keys))) { $suites[$id] })
        unresolved=@(Get-TestSelectionSorted @($unresolved))}
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
    if ($unresolved.Count) { foreach ($id in $ids) { [void]$dirty.Add($id) } }
    return @{format='antiphon-rc-input-inventory-v1'; baselineSha=$BaselineSha; candidateSha=$CandidateSha
        snapshots=@($old,$new);changes=$changes.ToArray();dirtySuites=@($dirty);unresolved=@(Get-TestSelectionSorted @($unresolved))
        index=@{version=$(if ($null -ne $DerivedIndex) { $DerivedIndex.version } else { $null });broadened=$broadened;authority='positive-only'}
        requiredActualReceipts=@('sdk-runtime-os-architecture','powershell-node-browser-native','configuration-environment',
            'resolved-package-graph-and-content','generated-fixture-recipes-and-outputs','immutable-images','discovery-case-manifest')
        activation='shadow-only'; executionEvidence=$false; gateCredit=$false}
}
