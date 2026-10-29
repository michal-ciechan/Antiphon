#requires -Version 7.0
# CARD-1545: advisory project batches; never execution or qualification authority.
. "$PSScriptRoot/test-selection.ps1"

function Resolve-RcProjectInput {
    param([string]$From, [string]$Include, [string]$Project = $From, [switch]$AllowWildcard)
    $directory=($Project -replace '[^/]+$','')
    $value=$Include.Replace('\','/')
    if ($value.StartsWith('$(MSBuildThisFileDirectory)',[StringComparison]::Ordinal)) {
        $directory=$From -replace '[^/]+$',''
        $value=$value.Substring('$(MSBuildThisFileDirectory)'.Length)
    }
    if (-not $value -or $value -match '[$@%;:]' -or (-not $AllowWildcard -and $value -match '[*?\[]') -or $value.StartsWith('/')) { throw "unresolved-input:$From" }
    $parts=[Collections.Generic.List[string]]::new()
    foreach ($part in ($directory+$value).Split('/')) {
        if ($part -ceq '..') {
            if (-not $parts.Count) { throw "outside-input:$From" }
            $parts.RemoveAt($parts.Count-1)
        } elseif ($part -and $part -cne '.') { $parts.Add($part) }
    }
    return $parts -join '/'
}

function Get-RcDownstream {
    param($Projects, [string[]]$Seeds)
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $queue=[Collections.Generic.Queue[string]]::new()
    foreach ($seed in $Seeds) { if ($seen.Add($seed)) { $queue.Enqueue($seed) } }
    while ($queue.Count) {
        $current=$queue.Dequeue()
        foreach ($project in $Projects.Values) {
            if ($project.references -ccontains $current -and $seen.Add($project.path)) { $queue.Enqueue($project.path) }
        }
    }
    return @(Get-TestSelectionSorted @($seen))
}

function Read-RcProjectGraph {
    param([string]$Root, [string]$Sha, [hashtable]$DocumentCache = @{})
    $reasons=[Collections.Generic.List[string]]::new()
    $paths=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $blobs=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $text=(Invoke-TestSelectionGit $Root @('ls-tree','-r','-z',$Sha,'--')).Stdout
    foreach ($entry in $text.Split([char]0)) {
        if (-not $entry) { continue }
        $tab=$entry.IndexOf([char]9)
        if ($tab -lt 0) { throw 'malformed-project-tree' }
        $header=$entry.Substring(0,$tab); $path=$entry.Substring($tab+1)
        [void]$paths.Add($path)
        if ($header -cmatch '^100(?:644|755) blob ([0-9a-f]{40})$') { $blobs[$path]=$Matches[1] }
    }
    $catalogPath='tests/rc-project-categories.json'
    $catalog=@{projects=@{};batches=@()}
    try {
        if (-not $blobs.ContainsKey($catalogPath)) { throw 'category-inventory-absent' }
        $catalog=Get-TestSelectionBlob $Root $Sha $catalogPath | ConvertFrom-Json -AsHashtable
        if ($catalog.schemaVersion -ne 1 -or $catalog.projects -isnot [Collections.IDictionary] -or $catalog.batches -isnot [array]) { throw 'category-inventory-invalid' }
    } catch { $reasons.Add('category-inventory-invalid'); $catalog=@{projects=@{};batches=@()} }
    $projects=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($path in (Get-TestSelectionSorted @($paths | Where-Object { $_ -cmatch '\.csproj$' }))) {
        $categories=@($catalog.projects[$path])
        if (-not $categories.Count -or @($categories | Where-Object { $_ -isnot [string] -or $_ -cnotmatch '^[a-z][a-z0-9-]*$' }).Count) {
            $reasons.Add("unknown-project:$path"); $categories=@('unknown')
        }
        $projects[$path]=@{path=$path;categories=@(Get-TestSelectionSorted $categories);references=@();inputs=@();downstream=@();hasTests=$false;batchDisposition='unclassified'}
    }
    foreach ($path in $catalog.projects.Keys) { if (-not $projects.ContainsKey($path)) { $reasons.Add("stale-project:$path") } }
    foreach ($batch in $catalog.batches) { if (-not $projects.ContainsKey([string]$batch)) { $reasons.Add("unknown-batch:$batch") } }
    # Read literal imports and ancestor Directory.Build inputs conservatively, including
    # all conditional references. Never execute MSBuild or evaluate project expressions.
    # Git blob identity binds the immutable XML bytes. Share only parsed documents;
    # imports, relative paths, edges and inventories are still resolved for each graph.
    $documents=$DocumentCache
    foreach ($project in $projects.Values) {
        $queue=[Collections.Generic.Queue[string]]::new(); $queue.Enqueue($project.path)
        $ancestor=$project.path -replace '/[^/]+$',''
        while ($true) {
            foreach ($name in @('Directory.Build.props','Directory.Build.targets')) {
                $input=if ($ancestor) { "$ancestor/$name" } else { $name }
                if ($paths.Contains($input)) { $queue.Enqueue($input) }
            }
            if (-not $ancestor) { break }
            $ancestor=if ($ancestor.Contains('/')) { $ancestor -replace '/[^/]+$','' } else { '' }
        }
        $visited=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $references=@(); $inputs=@()
        while ($queue.Count) {
            $input=$queue.Dequeue()
            if (-not $visited.Add($input)) { continue }
            try {
                if (-not $blobs.ContainsKey($input)) { throw "missing-input:$input" }
                $blob=$blobs[$input]
                if (-not $documents.ContainsKey($blob)) {
                    $settings=[Xml.XmlReaderSettings]::new(); $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit; $settings.XmlResolver=$null
                    $reader=[Xml.XmlReader]::Create([IO.StringReader]::new((Get-TestSelectionBlob $Root $Sha $input)), $settings)
                    try { $document=[Xml.XmlDocument]::new(); $document.XmlResolver=$null; $document.Load($reader); $documents[$blob]=$document } finally { $reader.Dispose() }
                }
                $document=$documents[$blob]
                foreach ($node in $document.SelectNodes("//*[local-name()='ProjectReference']")) {
                    $target=Resolve-RcProjectInput $input $node.GetAttribute('Include') $project.path
                    if (-not $projects.ContainsKey($target)) { throw "missing-reference:$input" }
                    $references+=$target
                }
                foreach ($node in $document.SelectNodes("//*[local-name()='Import']")) {
                    $target=Resolve-RcProjectInput $input $node.GetAttribute('Project')
                    $queue.Enqueue($target)
                }
                if (@($document.SelectNodes("//*[local-name()='PackageReference' and @Include]") | Where-Object { $_.GetAttribute('Include') -ieq 'TUnit' }).Count) { $project.hasTests=$true }
                foreach ($node in $document.SelectNodes("//*[@Include and (local-name()='Compile' or local-name()='Content' or local-name()='None' or local-name()='EmbeddedResource' or local-name()='AdditionalFiles') and not(ancestor::*[local-name()='Target'])]")) {
                    $include=$node.GetAttribute('Include')
                    # NuGet-generated Pkg properties name restored package binaries,
                    # not repository files. Package/version inputs are owned separately.
                    if ($include -cmatch '^\$\(Pkg[A-Za-z0-9_]+\)[\\/]') { continue }
                    $target=Resolve-RcProjectInput $input $include $project.path -AllowWildcard
                    if ($target -match '[*?]') {
                        # MSBuild treats square brackets literally. Match its simple
                        # globs conservatively across directories, with bounded work.
                        $expression='^'+[regex]::Escape($target).Replace('\*','.*').Replace('\?','.')+'$'
                        $pattern=[regex]::new($expression,([Text.RegularExpressions.RegexOptions]::CultureInvariant -bor [Text.RegularExpressions.RegexOptions]::Singleline),[TimeSpan]::FromMilliseconds(100))
                        $inputs+=@($paths | Where-Object { $pattern.IsMatch($_) })
                    } else {
                        if ($node.LocalName -ceq 'Compile' -and -not $paths.Contains($target)) { throw "missing-linked-input:$input" }
                        $inputs+=$target
                    }
                }
            } catch { $reasons.Add("graph:$input"+':'+$_.Exception.Message) }
        }
        $project.references=@(Get-TestSelectionSorted $references)
        $project.inputs=@(Get-TestSelectionSorted $inputs)
    }
    $exemptions=$catalog.nonQualificationTests
    if ($null -eq $exemptions) { $exemptions=@{} }
    if ($exemptions -isnot [Collections.IDictionary]) { $reasons.Add('invalid-test-dispositions'); $exemptions=@{} }
    foreach ($path in $exemptions.Keys) {
        if (-not $projects.ContainsKey($path) -or $exemptions[$path] -isnot [string] -or [string]::IsNullOrWhiteSpace($exemptions[$path])) { $reasons.Add("invalid-test-disposition:$path") }
    }
    $batches=@($catalog.batches)
    foreach ($project in $projects.Values) {
        if ($batches -ccontains $project.path) { $project.batchDisposition='whole-project' }
        elseif ($exemptions.Contains($project.path)) { $project.batchDisposition='harness-only' }
        elseif ($project.hasTests) {
            $reasons.Add("unbatched-test-project:$($project.path)")
            $batches+=$project.path
        }
        $project.downstream=@(Get-RcDownstream $projects @($project.path) | Where-Object { $_ -cne $project.path })
        # An edge back to the root through its own downstream is a cycle.
        foreach ($dependency in $project.references) {
            if ($dependency -ceq $project.path -or $project.downstream -ccontains $dependency) { $reasons.Add("project-cycle:$($project.path)") }
        }
    }
    return @{sha=$Sha;projects=$projects;paths=$paths;blobs=$blobs;batches=@(Get-TestSelectionSorted $batches)
        inventoryBlob=$(if ($blobs.ContainsKey($catalogPath)) { $blobs[$catalogPath] } else { '' })
        fallbackReasons=@(Get-TestSelectionSorted @($reasons))}
}

function Get-RcProjectSelection {
    param([string]$RepositoryRoot, [string]$BaselineSha, [string]$CandidateSha)
    foreach ($sha in @($BaselineSha,$CandidateSha)) {
        if ($sha -cnotmatch '^[0-9a-f]{40}$' -or (Invoke-TestSelectionGit $RepositoryRoot @('cat-file','-t',$sha)).Stdout.Trim() -cne 'commit') { throw 'full-commit-required' }
    }
    $documents=@{}
    $old=Read-RcProjectGraph $RepositoryRoot $BaselineSha $documents
    $new=Read-RcProjectGraph $RepositoryRoot $CandidateSha $documents
    $reasons=@($old.fallbackReasons)+@($new.fallbackReasons)
    if ((Invoke-TestSelectionGit $RepositoryRoot @('merge-base','--is-ancestor',$BaselineSha,$CandidateSha) @(0,1)).Exit -ne 0) { $reasons+='baseline-not-ancestor' }
    # No rename inference: both removed and added ownership must participate.
    $diff=(Invoke-TestSelectionGit $RepositoryRoot @('diff','--no-ext-diff','--no-renames','--name-status','-z',$BaselineSha,$CandidateSha,'--')).Stdout.Split([char]0)
    $changes=@(); $changed=@(); $affected=@()
    for ($i=0;$i -lt $diff.Count-1;$i+=2) {
        $status=$diff[$i]; $path=$diff[$i+1]
        if ($status -cnotmatch '^[ADMTUXB]$' -or -not $path) { throw 'malformed-project-diff' }
        $changes+=@{path=$path;status=$status}
        if ($path -match '(^scripts/|\.(props|targets|sln|slnx)$|(^|/)(global\.json|NuGet\.Config|nuget\.config)$|^tests/[^/]*policy[^/]*\.json$|^tests/rc-project-categories\.json$)') { $reasons+="shared-input:$path" }
        if ($status -cnotin @('A','D','M')) { $reasons+="uncertain-change:$path" }
        $owners=@()
        $graphs=if ($status -ceq 'A') { @($new) } elseif ($status -ceq 'D') { @($old) } else { @($old,$new) }
        foreach ($graph in $graphs) {
            $local=@()
            foreach ($project in $graph.projects.Values) {
                $directory=$project.path -replace '[^/]+$',''
                if ($path.StartsWith($directory,[StringComparison]::Ordinal) -or $project.inputs -ccontains $path) { $local+=$project.path }
            }
            if (-not $local.Count) { $reasons+="unknown-path:$path" }
            $owners+=$local
            $affected+=@(Get-RcDownstream $graph.projects $local)
        }
        $changed+=$owners
    }
    $reasons=@(Get-TestSelectionSorted $reasons)
    if ($reasons.Count) { $affected=@($new.projects.Keys) }
    $batchRows=@()
    foreach ($path in (Get-TestSelectionSorted @($new.batches))) {
        if ($new.projects.ContainsKey($path) -and $affected -ccontains $path) {
            $batchRows+=@{id=$path;project=$path;categories=$new.projects[$path].categories;membership='whole-project'}
        }
    }
    $graphs=@()
    foreach ($graph in @($old,$new)) {
        $rows=@($graph.projects.Values | Sort-Object path -CaseSensitive)
        $graphs+=@{sha=$graph.sha;inventoryBlob=$graph.inventoryBlob;projects=$rows}
    }
    $result=[ordered]@{format='antiphon-rc-project-selection-v1';baselineSha=$BaselineSha;candidateSha=$CandidateSha
        plannerVersion=1;plannerHash=(Get-TestSelectionDigest ([IO.File]::ReadAllText($PSCommandPath)))
        mode=$(if ($reasons.Count) { 'Full' } else { 'Scoped' });effectiveMode='Full';coverageScope='Full'
        activation='shadow-only';executionEvidence=$false;gateCredit=$false
        changedPaths=$changes;changedProjects=@(Get-TestSelectionSorted $changed);affectedProjects=@(Get-TestSelectionSorted $affected)
        categories=@(Get-TestSelectionSorted @($new.projects.Values | Where-Object { $affected -ccontains $_.path } | ForEach-Object { $_.categories }))
        batches=$batchRows;graphs=$graphs;fallbackReasons=$reasons}
    $result.digest=Get-TestSelectionDigest $result
    return $result
}
