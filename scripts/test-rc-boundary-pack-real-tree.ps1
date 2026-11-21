#requires -Version 7.0
# Manual S4a acceptance: discovery/packing only, never executes the full suite.
param(
    [Parameter(Mandatory)][string]$AssemblyPath,
    [Parameter(Mandatory)][string]$DiscoveryLogPath,
    [Parameter(Mandatory)][string]$ResultsDirectory
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$AssemblyPath = [IO.Path]::GetFullPath($AssemblyPath)
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
if (Test-Path -LiteralPath $ResultsDirectory) { throw 'fresh results directory required' }
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
foreach ($name in @('nightly-common','nightly-coverage','nightly-tests-impl')) {
    . (Join-Path $PSScriptRoot ('lib/' + $name + '.ps1'))
}
# The caller produces this fresh diagnostic with a gated, foreground
# dotnet run --no-build --list-tests. No Windows-only native owner is needed.
$parseWatch = [Diagnostics.Stopwatch]::StartNew()
$parsed = [Antiphon.Nightly.NightlyDiagnosticLog]::Read($DiscoveryLogPath, 'discovery')
$document = [Antiphon.Nightly.NightlyDiagnosticLog]::NewDiscoveryDocument(@($parsed.Nodes), ([Antiphon.Nightly.NightlyDiagnosticLog]::AssemblyHash($AssemblyPath)))
[Antiphon.Nightly.NightlyDiagnosticLog]::WriteDiscoveryDocument((Join-Path $ResultsDirectory 'discovery.json'), $document)
$parseWatch.Stop()
$nodes = @(([Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryDocument($document, 'antiphon-tunit-discovery-v1')).Nodes)
$byClass = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$byNamespace = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($node in $nodes) {
    $bare = [Antiphon.Nightly.NightlyCensus]::BareClassIdentity($node.ClassName)
    if (-not $expected.Add([string]$node.Uid)) { throw 'duplicate discovered UID' }
    if (-not $byClass.ContainsKey($bare)) { $byClass.Add($bare, [Collections.Generic.List[object]]::new()) }
    $byClass[$bare].Add($node)
    if (-not $byNamespace.ContainsKey($node.Namespace)) { $byNamespace.Add($node.Namespace, [Collections.Generic.List[object]]::new()) }
    $byNamespace[$node.Namespace].Add($node)
}
# Resolve source ownership once, including partial-class conflicts as unassigned.
$map = [Antiphon.Nightly.RcBoundary]::ReadMap((Join-Path $PSScriptRoot '..'))
$sourceBoundaries = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
foreach ($path in @(git -C $repo ls-files 'tests/Antiphon.Tests/*.cs' 'tests/Antiphon.Tests/**/*.cs')) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $path))
    $ns = [regex]::Match($source, '\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)')
    if (-not $ns.Success) { continue }
    foreach ($match in [regex]::Matches($source, '\bclass\s+([A-Za-z_][A-Za-z0-9_]*)')) {
        $bare = $ns.Groups[1].Value + '.' + $match.Groups[1].Value
        if (-not $byClass.ContainsKey($bare)) { continue }
        $boundary = [Antiphon.Nightly.RcBoundary]::ClassBoundary($map, 'antiphon', $path, $source)
        if (-not $sourceBoundaries.ContainsKey($bare)) { $sourceBoundaries.Add($bare, [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)) }
        [void]$sourceBoundaries[$bare].Add($boundary)
    }
}
$boundaries = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$leaves = @(foreach ($bare in $byClass.Keys) {
    $boundary = 'unassigned'
    if ($sourceBoundaries.ContainsKey($bare) -and $sourceBoundaries[$bare].Count -eq 1) { $boundary = [string]@($sourceBoundaries[$bare])[0] }
    $boundaries.Add($bare, $boundary)
    [pscustomobject]@{ id='all-profile-1'; classes=@($bare); exactFilter=''; nodes=@() }
})
# W2-nightly S11: both packs are compiled (NightlyTestsPlan). The base-ref body comparison is retired: that body called
# PowerShell helpers (Get-NightlyBareTypeParts, Split-NightlyItemsBySeconds) the compiled planner replaced.
$costs = [Antiphon.Nightly.NightlyClassCosts]::new([string]$script:NightlyClassCostPath, $script:NightlyClassCostTable)
$offWatch = [Diagnostics.Stopwatch]::StartNew()
$offJson = ConvertTo-Json -InputObject @([Antiphon.Nightly.NightlyTestsPlan]::ProfileLaunchGroups($leaves, $nodes, $null, $costs, $null)) -Depth 30 -Compress
$offWatch.Stop()
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $ResultsDirectory 'off-pack.json'), $offJson, $utf8)
$packWatch = [Diagnostics.Stopwatch]::StartNew()
$packed = @([Antiphon.Nightly.NightlyTestsPlan]::BoundaryLaunchGroups($leaves, $nodes, 'antiphon', $boundaries, $costs, $null))
$packWatch.Stop()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$verificationWatch = [Diagnostics.Stopwatch]::StartNew()
foreach ($chunk in $packed) {
    if (-not $ids.Add($chunk.id)) { throw 'duplicate chunk ID' }
    $selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $filter = [string]$chunk.exactFilter
    if (-not $filter) { $filter = [string]$chunk.methodFilter }
    if (-not $filter) { $filter = [string]$chunk.groupFilter }
    if ($filter) {
        $parts = $filter.Split('/')
        $candidates = $byNamespace[$parts[2]]
        foreach ($node in $candidates) {
            $bare = [Antiphon.Nightly.NightlyLaunchPlan]::BareTypeParts($node.ClassName)
            if ($chunk.exactFilter) { $hit = ('/*/{0}/{1}/{2}' -f $bare.Namespace,$bare.Type,$node.Method) -ceq $filter }
            else {
                $typeRx = '^(?:' + $parts[3].Replace('*','.*') + ')$'
                $methodRx = '^(?:' + $parts[4].Replace('*','.*') + ')$'
                $hit = $bare.Type -cmatch $typeRx -and $node.Method -cmatch $methodRx
            }
            if ($hit) { [void]$selected.Add([string]$node.Uid) }
        }
    } else {
        foreach ($bare in $chunk.classes) { foreach ($node in $byClass[$bare]) { [void]$selected.Add([string]$node.Uid) } }
    }
    if ($selected.Count -eq 0 -or -not $selected.SetEquals([string[]]@($chunk.caseMembership.uid))) { throw ('filter/manifest mismatch: ' + $chunk.id) }
    if ($chunk.caseMembership.Count -ne $selected.Count -or $chunk.caseKeys.Count -ne $selected.Count) { throw 'duplicated case manifest' }
    foreach ($uid in $selected) { if (-not $seen.Add($uid)) { throw ('duplicated case selection: ' + $uid) } }
    foreach ($bare in $chunk.classes) {
        if (-not $chunk.collision -and $boundaries[$bare] -cne $chunk.boundary) { throw 'mixed boundaries' }
    }
}
if (-not $seen.SetEquals($expected)) { throw 'incomplete case union' }
$verificationWatch.Stop()
$receipt = [ordered]@{
    sourceSha=(git -C $repo rev-parse HEAD); assemblyPath=$AssemblyPath
    assemblySha256=(Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    cases=$expected.Count; classes=$byClass.Count; chunks=$packed.Count; selectedExactlyOnce=$seen.Count
    optOutBytes=$utf8.GetByteCount($offJson)
    discoveryLogPath=[IO.Path]::GetFullPath($DiscoveryLogPath); discoverySeconds=$null
    discoveryParseSeconds=$parseWatch.Elapsed.TotalSeconds
    offPackSeconds=$offWatch.Elapsed.TotalSeconds; boundaryPackSeconds=$packWatch.Elapsed.TotalSeconds
    verificationSeconds=$verificationWatch.Elapsed.TotalSeconds
    boundaryCounts=@($packed | Group-Object boundary | ForEach-Object { @{ boundary=$_.Name; chunks=$_.Count } })
}
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'acceptance.json')
$receipt | ConvertTo-Json -Depth 8
