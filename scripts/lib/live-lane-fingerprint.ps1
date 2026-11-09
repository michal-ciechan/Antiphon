#requires -Version 7.0
# CARD-1545 S2a-1: live-lane input fingerprint. Diagnostic and shadow-only: it never earns
# gate credit or stands for execution. It reads Git objects at one commit only (no working
# tree, no mtime) and makes the lane DUE whenever an input is not proven covered.
. "$PSScriptRoot/rc-project-selection.ps1"
. "$PSScriptRoot/live-lane-attestation.ps1"

$script:LiveLaneDeclarationPath='tests/live-lane-inputs.json'
$script:LiveLaneDeclarationFields=@('manifest','contractFiles','minimumRoots','runtimeRoots','namespaceRoots','instructionPatterns')
$script:LiveLaneComponents=@('laneContract','repositoryClosure','liveRoots','testSources','runtimeInputs','instructions')
# The files the CLI loads by name, independent of what a declaration says it hashes.
$script:LiveLaneInstructionLeaves=@('CLAUDE.md','CLAUDE.local.md','AGENTS.md','.mcp.json')
$script:LiveLaneImportingLeaves=@('CLAUDE.md','CLAUDE.local.md','AGENTS.md')
$script:LiveLaneSharedBuild=[regex]::new('(^|/)(Directory\.Build\.[^/]*|[^/]*\.props|[^/]*\.targets|global\.json|NuGet\.Config|nuget\.config|Directory\.Packages\.props|packages\.lock\.json|[^/]*\.sln[^/]*)$','CultureInvariant')

function ConvertTo-LiveLaneGlobRegex {
    param([string]$Pattern)
    $builder=[Text.StringBuilder]::new('^')
    for ($i=0; $i -lt $Pattern.Length; $i++) {
        $ch=$Pattern[$i]
        if ($ch -ceq '*' -and $i+1 -lt $Pattern.Length -and $Pattern[$i+1] -ceq '*') { [void]$builder.Append('.*'); $i++ }
        elseif ($ch -ceq '*') { [void]$builder.Append('[^/]*') }
        elseif ($ch -ceq '?') { [void]$builder.Append('[^/]') }
        else { [void]$builder.Append([regex]::Escape([string]$ch)) }
    }
    return [regex]::new($builder.Append('$').ToString(),'CultureInvariant')
}

function Resolve-LiveLanePath {
    # Joins a relative reference onto a repository directory; $null when it leaves the tree.
    param([string]$Directory, [string]$Reference)
    $parts=[Collections.Generic.List[string]]::new()
    foreach ($part in ($Directory+$Reference.Replace('\','/')).Split('/')) {
        if ($part -ceq '..') {
            if (-not $parts.Count) { return $null }
            $parts.RemoveAt($parts.Count-1)
        } elseif ($part -and $part -cne '.') { $parts.Add($part) }
    }
    return $parts -join '/'
}

function Test-LiveLaneStringList {
    param($Value, [switch]$Root)
    if ($Value -isnot [Collections.IList]) { return $false }
    foreach ($item in $Value) {
        if ($item -isnot [string] -or -not $item -or $item.StartsWith('/') -or $item.Contains('\') -or $item.Contains('..')) { return $false }
        if ($Root -and -not $item.EndsWith('/')) { return $false }
    }
    return $true
}

function Read-LiveLaneTree {
    param([string]$RepositoryRoot, [string]$Sha, [ref]$Listing)
    $tree=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $text=(Invoke-TestSelectionGit $RepositoryRoot @('ls-tree','-r','-z',$Sha,'--')).Stdout
    if ($Listing) { $Listing.Value=$text }
    foreach ($entry in $text.Split([char]0)) {
        if (-not $entry) { continue }
        if ($entry -cnotmatch '^([0-7]{6}) ([a-z]+) ([0-9a-f]{40}|[0-9a-f]{64})\t(.+)$') { throw 'malformed-lane-tree' }
        $tree[$Matches[4]]=[ordered]@{path=$Matches[4];mode=$Matches[1];blob=$Matches[3]}
    }
    return $tree
}

function Read-LiveLaneBlob {
    # The bytes of the tree entry at Path, read by its blob id: the same bytes as <sha>:<path>.
    param([string]$RepositoryRoot, [string]$Sha, $Tree, [string]$Path, [hashtable]$Cache)
    if ($null -eq $Cache) { return Get-TestSelectionBlob $RepositoryRoot $Sha $Path }
    return Read-RcProjectBlob $RepositoryRoot $Tree[$Path].blob $Cache
}

function Read-LiveLaneDeclaration {
    param([string]$RepositoryRoot, [string]$Sha, $Tree, [string]$Lane, [hashtable]$Cache)
    if (-not $Tree.ContainsKey($script:LiveLaneDeclarationPath) -or $Tree[$script:LiveLaneDeclarationPath].mode -cnotmatch '^1006[0-7]{2}$') { throw 'declaration-invalid' }
    try { $document=Read-LiveLaneBlob $RepositoryRoot $Sha $Tree $script:LiveLaneDeclarationPath $Cache | ConvertFrom-Json -AsHashtable } catch { throw 'declaration-invalid' }
    if ($document -isnot [Collections.IDictionary]) { throw 'declaration-invalid' }
    $keys=@(Get-TestSelectionSorted @($document.Keys))
    if (($keys -join ',') -cne 'format,lanes' -or $document['format'] -cne 'antiphon-live-lane-inputs-v1' -or $document['lanes'] -isnot [Collections.IDictionary]) { throw 'declaration-invalid' }
    $name=@($document['lanes'].Keys | Where-Object { $_ -ceq $Lane })
    if ($name.Count -ne 1) { throw 'declaration-invalid' }
    $entry=$document['lanes'][$name[0]]
    if ($entry -isnot [Collections.IDictionary]) { throw 'declaration-invalid' }
    $fields=@(Get-TestSelectionSorted @($entry.Keys))
    if (($fields -join ',') -cne ((Get-TestSelectionSorted $script:LiveLaneDeclarationFields) -join ',')) { throw 'declaration-invalid' }
    if ($entry['manifest'] -isnot [string] -or -not (Test-LiveLaneStringList @($entry['manifest']))) { throw 'declaration-invalid' }
    foreach ($field in @('contractFiles','instructionPatterns')) { if (-not (Test-LiveLaneStringList $entry[$field])) { throw 'declaration-invalid' } }
    foreach ($field in @('minimumRoots','runtimeRoots')) { if (-not (Test-LiveLaneStringList $entry[$field] -Root)) { throw 'declaration-invalid' } }
    if ($entry['namespaceRoots'] -isnot [Collections.IList] -or @($entry['namespaceRoots'] | Where-Object { $_ -isnot [string] -or $_ -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$' }).Count) { throw 'declaration-invalid' }
    return $entry
}

function Read-LiveLaneManifestProjects {
    param([string]$RepositoryRoot, [string]$Sha, $Tree, [string]$Path, [hashtable]$Cache)
    if (-not $Tree.ContainsKey($Path)) { throw 'manifest-missing' }
    try { $manifest=Read-LiveLaneBlob $RepositoryRoot $Sha $Tree $Path $Cache | ConvertFrom-Json -AsHashtable } catch { throw 'manifest-invalid' }
    if ($manifest -isnot [Collections.IDictionary] -or $manifest['format'] -cne 'antiphon-live-lanes-v1' -or $manifest['core'] -isnot [Collections.IList] -or -not $manifest['core'].Count) { throw 'manifest-invalid' }
    $projects=foreach ($row in $manifest['core']) {
        if ($row -isnot [Collections.IDictionary] -or $row['project'] -isnot [string] -or $row['project'] -cnotmatch '^[A-Za-z0-9_][A-Za-z0-9_.-]*$') { throw 'manifest-invalid' }
        $row['project']
    }
    return @(Get-TestSelectionSorted @($projects))
}

function Get-LiveLaneManifestRoster {
    # The lane's expanded core uids at one commit (attestation rows and receipts bind to them).
    param([string]$RepositoryRoot, [string]$Sha, $Tree, [string]$Path, [hashtable]$Cache)
    if (-not $Tree.ContainsKey($Path)) { throw 'manifest-missing' }
    try { $manifest=Read-LiveLaneBlob $RepositoryRoot $Sha $Tree $Path $Cache | ConvertFrom-Json -AsHashtable } catch { throw 'manifest-invalid' }
    return ConvertTo-LiveLaneRoster $manifest
}

function Get-LiveLaneFingerprintDigest {
    # Domain-separated by lane and schema version; components are the whole material.
    param([string]$Lane, $Components)
    $material="antiphon-live-lane-fingerprint`n"+(ConvertTo-TestSelectionCanonicalJson ([ordered]@{schemaVersion=1;laneId=$Lane;components=$Components}))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($material))).ToLowerInvariant()
}

function Get-LiveLaneImportSet {
    # The files MSBuild visits for the closure projects, mirroring the reader's literal walk:
    # ancestor Directory.Build.* and literal <Import> targets, missing targets included.
    param($Graph, [string[]]$Projects, [hashtable]$Documents)
    $visited=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($project in $Projects) {
        $queue=[Collections.Generic.Queue[string]]::new(); $queue.Enqueue($project)
        $ancestor=$project -replace '/[^/]+$',''
        if ($ancestor -ceq $project) { $ancestor='' }
        while ($true) {
            foreach ($name in @('Directory.Build.props','Directory.Build.targets')) {
                $input=if ($ancestor) { "$ancestor/$name" } else { $name }
                if ($Graph.paths.Contains($input)) { $queue.Enqueue($input) }
            }
            if (-not $ancestor) { break }
            $ancestor=if ($ancestor.Contains('/')) { $ancestor -replace '/[^/]+$','' } else { '' }
        }
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        while ($queue.Count) {
            $input=$queue.Dequeue()
            if (-not $seen.Add($input)) { continue }
            [void]$visited.Add($input)
            if (-not $Graph.blobs.ContainsKey($input)) { continue }
            $document=$Documents[$Graph.blobs[$input]]
            if ($document -isnot [Xml.XmlDocument]) { continue }
            foreach ($node in $document.SelectNodes("//*[local-name()='Import']")) {
                # An unresolvable import is already a graph reason naming this input.
                try { $queue.Enqueue((Resolve-RcProjectInput $input $node.GetAttribute('Project'))) } catch { }
            }
        }
    }
    return $visited
}

function Get-LiveLaneInstructionImports {
    # Follows CLI `@path` lines relative to the importing file, bounded and cycle-safe.
    param([string]$RepositoryRoot, [string]$Sha, $Tree, [string[]]$Files, $Unknown, [hashtable]$Cache)
    $found=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $queue=[Collections.Generic.Queue[object]]::new()
    foreach ($file in $Files) { $queue.Enqueue(@($file,0)) }
    $read=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    while ($queue.Count) {
        $file,$depth=$queue.Dequeue()
        if (-not $read.Add($file)) { continue }
        if ($Tree[$file].mode -cnotmatch '^1006[0-7]{2}$') { continue }
        $text=ConvertTo-TestSelectionLfText (Read-LiveLaneBlob $RepositoryRoot $Sha $Tree $file $Cache)
        foreach ($line in $text.Split("`n")) {
            if ($line -cnotmatch '^\s*@(\S+)\s*$') { continue }
            $reference=$Matches[1]
            if ($reference.StartsWith('~')) { $Unknown.Add("instruction-import-user:$file"); continue }
            if ($reference.StartsWith('/') -or $reference.StartsWith('\') -or $reference -match '^[A-Za-z]:') { $Unknown.Add("instruction-import-outside:$file"); continue }
            $target=Resolve-LiveLanePath ($file -replace '[^/]+$','') $reference
            if ($null -eq $target -or -not $target) { $Unknown.Add("instruction-import-outside:$file"); continue }
            if (-not $Tree.ContainsKey($target)) { $Unknown.Add("instruction-import-missing:$target"); continue }
            [void]$found.Add($target)
            if ($depth+1 -ge 8) { $Unknown.Add("instruction-import-depth:$target"); continue }
            $queue.Enqueue(@($target,($depth+1)))
        }
    }
    return $found
}

function Get-LiveLaneGrep {
    # One git process per scan; records are rev:path NUL line NUL text.
    param([string]$RepositoryRoot, [string]$Sha, [string]$Pattern, [string[]]$PathSpecs)
    $rows=[Collections.Generic.List[object]]::new()
    $text=(Invoke-TestSelectionGit $RepositoryRoot (@('grep','-z','-n','-I','-E',$Pattern,$Sha,'--')+$PathSpecs) @(0,1)).Stdout
    foreach ($record in $text.Split("`n")) {
        if (-not $record) { continue }
        $fields=$record.Split([char]0,3)
        if ($fields.Count -ne 3 -or -not $fields[0].StartsWith("${Sha}:",[StringComparison]::Ordinal)) { throw 'malformed-lane-grep' }
        $rows.Add(@($fields[0].Substring($Sha.Length+1),$fields[2]))
    }
    return $rows
}

function Get-LiveLaneEntryDigest {
    # Get-TestSelectionDigest over the tree entries of Paths, byte for byte, without one recursive
    # canonical call per field: the entry keys sort as blob, mode, path; blob and mode are hex and
    # octal; a printable-ASCII path escapes only at '"' and '\' (so is copied when it has neither).
    # Any other path takes the canonical serializer; a path the tree does not hold, the original form.
    param([string[]]$Paths, $Tree)
    foreach ($path in $Paths) { if (-not $Tree.ContainsKey($path)) { return Get-TestSelectionDigest @($Paths | ForEach-Object { $Tree[$_] }) } }
    $builder=[Text.StringBuilder]::new('[')
    foreach ($path in $Paths) {
        if ($builder.Length -gt 1) { [void]$builder.Append(',') }
        $entry=$Tree[$path]
        if ($path -cmatch '^[ !#-\[\]-~]*$') {
            [void]$builder.Append('{"blob":"').Append($entry.blob).Append('","mode":"').Append($entry.mode).Append('","path":"').Append($path).Append('"}')
        } else { [void]$builder.Append((ConvertTo-TestSelectionCanonicalJson $entry)) }
    }
    $bytes=[Text.Encoding]::UTF8.GetBytes($builder.Append(']').ToString())
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-LiveLaneFingerprint {
    # Every git call below reads objects of one commit; none can change the filter/textconv driver settings the
    # runner asks git for before each command. That answer is reused for this computation (60 s per directory)
    # and the caller's setting is restored after it.
    param([string]$RepositoryRoot, [string]$Sha, [string]$Lane, [hashtable]$DocumentCache = @{}, [string]$Attestation)
    . (Join-Path $PSScriptRoot 'checkpoint-process-runner.ps1')
    $savedDriverCache=[Antiphon.Checkpoints.CheckpointProcessRunner]::DriverSettingsCacheMilliseconds
    [Antiphon.Checkpoints.CheckpointProcessRunner]::DriverSettingsCacheMilliseconds=60000
    try { return Get-LiveLaneFingerprintCore -RepositoryRoot $RepositoryRoot -Sha $Sha -Lane $Lane -DocumentCache $DocumentCache -Attestation $Attestation }
    finally { [Antiphon.Checkpoints.CheckpointProcessRunner]::DriverSettingsCacheMilliseconds=$savedDriverCache }
}

function Get-LiveLaneFingerprintCore {
    param([string]$RepositoryRoot, [string]$Sha, [string]$Lane, [hashtable]$DocumentCache = @{}, [string]$Attestation)
    if ($Sha -cnotmatch '^[0-9a-f]{40}$' -or (Invoke-TestSelectionGit $RepositoryRoot @('cat-file','-t',$Sha)).Stdout.Trim() -cne 'commit') { throw 'full-commit-required' }
    if ($Lane -cnotmatch '^[a-z0-9][a-z0-9.-]*$') { throw 'declaration-invalid' }
    $listing=$null
    $tree=Read-LiveLaneTree $RepositoryRoot $Sha ([ref]$listing)
    $declaration=Read-LiveLaneDeclaration $RepositoryRoot $Sha $tree $Lane $DocumentCache
    $projectNames=Read-LiveLaneManifestProjects $RepositoryRoot $Sha $tree $declaration['manifest'] $DocumentCache
    $unknown=[Collections.Generic.List[string]]::new()
    $observations=[Collections.Generic.List[string]]::new()

    # Core rows name projects; each must resolve to exactly one csproj, never a fallback.
    $csprojs=@(foreach ($path in $tree.Keys) { if ($path.EndsWith('.csproj',[StringComparison]::Ordinal)) { $path } })
    $selected=@()
    foreach ($name in $projectNames) {
        $hits=@($csprojs | Where-Object { ($_ -replace '^.*/','') -ceq "$name.csproj" })
        if ($hits.Count -eq 0) { $unknown.Add("core-project-missing:$name") }
        elseif ($hits.Count -gt 1) { $unknown.Add("core-project-ambiguous:$name") }
        else { $selected+=$hits[0] }
    }

    # Closure from the candidate graph: selected test projects and every transitive reference.
    $graph=Read-RcProjectGraph $RepositoryRoot $Sha $DocumentCache $listing
    $closure=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $queue=[Collections.Generic.Queue[string]]::new()
    foreach ($project in $selected) { if ($closure.Add($project)) { $queue.Enqueue($project) } }
    while ($queue.Count) {
        $current=$queue.Dequeue()
        if (-not $graph.projects.ContainsKey($current)) { continue }
        foreach ($reference in $graph.projects[$current].references) { if ($closure.Add($reference)) { $queue.Enqueue($reference) } }
    }
    $closureProjects=@(Get-TestSelectionSorted @($closure))
    $imports=Get-LiveLaneImportSet $graph $closureProjects $DocumentCache

    # Input faults in the closure or its imports leave the closure unknown; catalog
    # faults and faults in other projects are observations (Q-8).
    $relevant=@($closureProjects)+@($imports)
    foreach ($reason in $graph.fallbackReasons) {
        $closureFault=$false
        if ($reason.StartsWith('graph:',[StringComparison]::Ordinal)) {
            $closureFault=@($relevant | Where-Object { $reason.StartsWith("graph:${_}:",[StringComparison]::Ordinal) }).Count -gt 0
        } elseif ($reason.StartsWith('project-cycle:',[StringComparison]::Ordinal)) {
            $closureFault=$closure.Contains($reason.Substring('project-cycle:'.Length))
        }
        if ($closureFault) { $unknown.Add($reason) } else { $observations.Add($reason) }
    }

    $members=[ordered]@{}
    foreach ($name in $script:LiveLaneComponents) { $members[$name]=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }
    $closureDirectories=@($closureProjects | ForEach-Object { $_ -replace '[^/]+$','' })
    $testDirectories=@($selected | ForEach-Object { $_ -replace '[^/]+$','' })
    $contract=@($declaration['contractFiles'] | ForEach-Object { ConvertTo-LiveLaneGlobRegex $_ })
    $instructionRules=foreach ($pattern in $declaration['instructionPatterns']) {
        if ($pattern.EndsWith('/**',[StringComparison]::Ordinal) -and -not $pattern.Substring(0,$pattern.Length-3).Contains('*')) {
            [regex]::new('(^|/)'+[regex]::Escape($pattern.Substring(0,$pattern.Length-3))+'/','CultureInvariant')
        } elseif (-not $pattern.Contains('/')) {
            [regex]::new('(^|/)'+(ConvertTo-LiveLaneGlobRegex $pattern).ToString().Substring(1),'CultureInvariant')
        } else { ConvertTo-LiveLaneGlobRegex $pattern }
    }
    $rootCounts=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    foreach ($root in @($declaration['minimumRoots'])+@($declaration['runtimeRoots'])) { $rootCounts[$root]=0 }
    $contractCounts=[int[]]::new($contract.Count)
    $instructionFiles=@()
    foreach ($path in $tree.Keys) {
        foreach ($directory in $closureDirectories) { if ($path.StartsWith($directory,[StringComparison]::Ordinal)) { [void]$members.repositoryClosure.Add($path); break } }
        foreach ($directory in $testDirectories) { if ($path.StartsWith($directory,[StringComparison]::Ordinal)) { [void]$members.testSources.Add($path); break } }
        if ($script:LiveLaneSharedBuild.IsMatch($path)) { [void]$members.repositoryClosure.Add($path) }
        foreach ($root in $declaration['minimumRoots']) { if ($path.StartsWith($root,[StringComparison]::Ordinal)) { [void]$members.liveRoots.Add($path); $rootCounts[$root]++ } }
        foreach ($root in $declaration['runtimeRoots']) { if ($path.StartsWith($root,[StringComparison]::Ordinal)) { [void]$members.runtimeInputs.Add($path); $rootCounts[$root]++ } }
        for ($i=0; $i -lt $contract.Count; $i++) { if ($contract[$i].IsMatch($path)) { [void]$members.laneContract.Add($path); $contractCounts[$i]++ } }
        $leaf=$path -replace '^.*/',''
        if ($script:LiveLaneInstructionLeaves -ccontains $leaf -or $path -cmatch '(^|/)\.claude/') {
            [void]$members.instructions.Add($path); $instructionFiles+=$path
            if (-not @($instructionRules | Where-Object { $_.IsMatch($path) }).Count) { $unknown.Add("instruction-outside-inputs:$path") }
        }
    }
    foreach ($project in $closureProjects) {
        if (-not $graph.projects.ContainsKey($project)) { continue }
        foreach ($input in $graph.projects[$project].inputs) {
            if (-not $tree.ContainsKey($input)) { continue }
            [void]$members.repositoryClosure.Add($input)
            if ($selected -ccontains $project) { [void]$members.testSources.Add($input) }
        }
    }
    foreach ($input in $imports) { if ($tree.ContainsKey($input)) { [void]$members.repositoryClosure.Add($input) } }
    foreach ($path in @($script:LiveLaneDeclarationPath,$declaration['manifest'])) { [void]$members.laneContract.Add($path) }
    foreach ($root in $rootCounts.Keys) { if ($rootCounts[$root] -eq 0) { $unknown.Add("declared-root-empty:$root") } }
    for ($i=0; $i -lt $contract.Count; $i++) { if ($contractCounts[$i] -eq 0) { $unknown.Add("declared-root-empty:$($declaration['contractFiles'][$i])") } }
    $importers=@($instructionFiles | Where-Object { $script:LiveLaneImportingLeaves -ccontains ($_ -replace '^.*/','') })
    foreach ($path in (Get-LiveLaneInstructionImports $RepositoryRoot $Sha $tree $importers $unknown $DocumentCache)) { [void]$members.instructions.Add($path) }

    $inventory=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($set in $members.Values) { $inventory.UnionWith($set) }
    foreach ($path in $inventory) {
        if ($tree[$path].mode -ceq '120000') { $unknown.Add("symlink:$path") }
        elseif ($tree[$path].mode -ceq '160000') { $unknown.Add("gitlink:$path") }
    }

    # A relevant namespace declared outside the inventory is code the lane may compile
    # or load without hashing it.
    $namespaceRoots=@($declaration['namespaceRoots'])
    foreach ($row in (Get-LiveLaneGrep $RepositoryRoot $Sha '^[[:space:]]*namespace[[:space:]]+[A-Za-z_][A-Za-z0-9_.]*' @('*.cs'))) {
        if ($row[1] -cnotmatch '^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)') { continue }
        $namespace=$Matches[1]
        if ($inventory.Contains($row[0])) { continue }
        foreach ($root in $namespaceRoots) {
            if ($namespace -ceq $root -or $namespace.StartsWith("$root.",[StringComparison]::Ordinal)) { $unknown.Add("namespace-outside-inputs:$($row[0])"); break }
        }
    }

    # Script literals in selected test sources must name hashed inputs. A bare name is a
    # harness under scripts/; a root that is hashed also covers a file it does not yet hold.
    if ($testDirectories.Count) {
        $covering=@($declaration['minimumRoots'])+@($declaration['runtimeRoots'])+$closureDirectories
        foreach ($row in (Get-LiveLaneGrep $RepositoryRoot $Sha '"(scripts/)?[A-Za-z0-9_./-]+\.(ps1|psm1|mjs|js|sh)"' $testDirectories)) {
            foreach ($match in [regex]::Matches($row[1],'"((?:scripts/)?[A-Za-z0-9_./-]+\.(?:ps1|psm1|mjs|js|sh))"')) {
                $literal=$match.Groups[1].Value
                $target=if ($literal.StartsWith('scripts/',[StringComparison]::Ordinal)) { Resolve-LiveLanePath '' $literal }
                    elseif (-not $literal.Contains('/')) { "scripts/$literal" }
                    elseif ($tree.ContainsKey($literal)) { $literal }
                    else { Resolve-LiveLanePath 'scripts/' $literal }
                if (-not $target) { $unknown.Add("undeclared-runtime:$literal"); continue }
                if ($inventory.Contains($target) -or @($covering | Where-Object { $target.StartsWith($_,[StringComparison]::Ordinal) }).Count) { continue }
                $unknown.Add("undeclared-runtime:$target")
            }
        }
    }

    $components=[ordered]@{}
    foreach ($name in $script:LiveLaneComponents) {
        $components[$name]=Get-LiveLaneEntryDigest (Get-TestSelectionSorted @($members[$name])) $tree
    }
    # S2a-2: an attested environment is one more component; unknown environment leaves the lane unknown.
    $environment=$null
    if ($Attestation) {
        $roster=Get-LiveLaneManifestRoster $RepositoryRoot $Sha $tree $declaration['manifest'] $DocumentCache
        $environment=Get-LiveLaneEnvironmentAttestation -Path $Attestation -Lane $Lane -Roster $roster
        if ($environment.status -ceq 'complete') { $components['environment']=$environment.digest }
        else { $unknown.Add('environment-unknown'); $components['environment']=Get-TestSelectionDigest @($environment.reasons) }
    }
    $digest=Get-LiveLaneFingerprintDigest $Lane $components
    $unknownReasons=@(Get-TestSelectionSorted @($unknown))
    $result=[ordered]@{format='antiphon-live-lane-fingerprint-v1';schemaVersion=1;laneId=$Lane
        status=$(if ($unknownReasons.Count) { 'unknown' } else { 'complete' })
        unknownReasons=$unknownReasons;observations=@(Get-TestSelectionSorted @($observations))
        digest=$digest;components=$components
        selectedProjects=@(Get-TestSelectionSorted $selected);closureProjects=$closureProjects
        inventory=@(Get-TestSelectionSorted @($inventory) | ForEach-Object { $tree[$_] })}
    if ($environment) { $result['environment']=[ordered]@{status=$environment.status;reasons=$environment.reasons} }
    return $result
}

function Get-LiveLaneStatus {
    # Without receipt history (S2b -History) every lane is DUE. The document is shadow-only. An
    # attestation is validated before any Git read, so a schema fault refuses on its own.
    param([string]$RepositoryRoot, [string]$Lane, [string]$CandidateSha, [string]$Attestation, [hashtable]$DocumentCache = @{})
    if ($Attestation) { [void](Get-LiveLaneEnvironmentAttestation -Path $Attestation -Lane $Lane) }
    $fingerprint=Get-LiveLaneFingerprint -RepositoryRoot $RepositoryRoot -Sha $CandidateSha -Lane $Lane -Attestation $Attestation -DocumentCache $DocumentCache
    return [ordered]@{format='antiphon-live-lane-status-v1';laneId=$Lane;candidateSha=$CandidateSha;state='DUE'
        reasons=@($(if ($fingerprint.status -ceq 'complete') { 'no-history' } else { 'unknown-input' }))
        effectiveMode='Full';activation='shadow-only';gateCredit=$false;executionEvidence=$false
        fingerprint=[ordered]@{status=$fingerprint.status;digest=$fingerprint.digest;components=$fingerprint.components
            unknownReasons=$fingerprint.unknownReasons;observations=$fingerprint.observations
            environment=$fingerprint['environment']}}
}
