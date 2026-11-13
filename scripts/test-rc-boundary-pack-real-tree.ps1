#requires -Version 7.0
# Manual S4a acceptance: discovery/packing only, never executes the full suite.
param(
    [Parameter(Mandatory)][string]$AssemblyPath,
    [Parameter(Mandatory)][string]$DiscoveryLogPath,
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [string]$BaseRef = '2349a1938b8ff11829ca6becf3c4719383bf9e52'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$AssemblyPath = [IO.Path]::GetFullPath($AssemblyPath)
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
if (Test-Path -LiteralPath $ResultsDirectory) { throw 'fresh results directory required' }
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
foreach ($name in @('nightly-common','nightly-policy','nightly-coverage','nightly-tests-impl','rc-boundary')) {
    . (Join-Path $PSScriptRoot ('lib/' + $name + '.ps1'))
}
# The caller produces this fresh diagnostic with a gated, foreground
# dotnet run --no-build --list-tests. No Windows-only native owner is needed.
$parseWatch = [Diagnostics.Stopwatch]::StartNew()
$parsed = ConvertFrom-NightlyDiagnosticLog -Path $DiscoveryLogPath -Kind discovery
$document = ConvertTo-NightlyDiscoveryDocument -Nodes @($parsed.Nodes) -AssemblyHash (Get-NightlyAssemblyHash $AssemblyPath)
Write-NightlyDiscoveryDocument -Path (Join-Path $ResultsDirectory 'discovery.json') -Document $document
$parseWatch.Stop()
$nodes = @((ConvertFrom-NightlyDiscoveryDocument $document 'antiphon-tunit-discovery-v1').Nodes)
$byClass = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$byNamespace = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($node in $nodes) {
    $bare = Get-NightlyBareClassIdentity $node.ClassName
    if (-not $expected.Add([string]$node.Uid)) { throw 'duplicate discovered UID' }
    if (-not $byClass.ContainsKey($bare)) { $byClass.Add($bare, [Collections.Generic.List[object]]::new()) }
    $byClass[$bare].Add($node)
    if (-not $byNamespace.ContainsKey($node.Namespace)) { $byNamespace.Add($node.Namespace, [Collections.Generic.List[object]]::new()) }
    $byNamespace[$node.Namespace].Add($node)
}
# Resolve source ownership once, including partial-class conflicts as unassigned.
$map = Read-RcBoundaryMap
$sourceBoundaries = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
foreach ($path in @(git -C $repo ls-files 'tests/Antiphon.Tests/*.cs' 'tests/Antiphon.Tests/**/*.cs')) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $path))
    $ns = [regex]::Match($source, '\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)')
    if (-not $ns.Success) { continue }
    foreach ($match in [regex]::Matches($source, '\bclass\s+([A-Za-z_][A-Za-z0-9_]*)')) {
        $bare = $ns.Groups[1].Value + '.' + $match.Groups[1].Value
        if (-not $byClass.ContainsKey($bare)) { continue }
        $boundary = Get-RcClassBoundary -Map $map -Suite antiphon -TestPath $path -Source $source
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
# Execute the base function's actual body, using the same discovery and unchanged
# helper functions. Restore the candidate body before opt-out and opt-in calls.
$current = (Get-Command ConvertTo-NightlyProfileLaunchGroups).ScriptBlock
$baseText = (git -C $repo show ($BaseRef + ':scripts/lib/nightly-tests-impl.ps1')) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'base source unavailable' }
$parseErrors = $null; $tokens = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($baseText, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'base parse failed' }
$definition = $ast.Find({ param($a) $a -is [Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -ceq 'ConvertTo-NightlyProfileLaunchGroups' }, $true)
if ($null -eq $definition) { throw 'base packer absent' }
. ([scriptblock]::Create($definition.Extent.Text))
$baseWatch = [Diagnostics.Stopwatch]::StartNew()
$baseJson = ConvertTo-Json -InputObject @(ConvertTo-NightlyProfileLaunchGroups -Leaves $leaves -DiscoveryNodes $nodes) -Depth 30 -Compress
$baseWatch.Stop()
Set-Item Function:ConvertTo-NightlyProfileLaunchGroups $current
$offWatch = [Diagnostics.Stopwatch]::StartNew()
$offJson = ConvertTo-Json -InputObject @(ConvertTo-NightlyProfileLaunchGroups -Leaves $leaves -DiscoveryNodes $nodes) -Depth 30 -Compress
$offWatch.Stop()
if ($baseJson -cne $offJson) { throw 'opt-out differs from base bytes' }
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $ResultsDirectory 'base-pack.json'), $baseJson, $utf8)
[IO.File]::WriteAllText((Join-Path $ResultsDirectory 'off-pack.json'), $offJson, $utf8)
$packWatch = [Diagnostics.Stopwatch]::StartNew()
$packed = @(ConvertTo-NightlyProfileLaunchGroups -Leaves $leaves -DiscoveryNodes $nodes -BoundaryAware -SuiteId antiphon -ClassBoundaries $boundaries)
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
            $bare = Get-NightlyBareTypeParts $node.ClassName
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
    sourceSha=(git -C $repo rev-parse HEAD); baseSha=$BaseRef; assemblyPath=$AssemblyPath
    assemblySha256=(Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    cases=$expected.Count; classes=$byClass.Count; chunks=$packed.Count; selectedExactlyOnce=$seen.Count
    optOutByteIdentical=$true; optOutBytes=$utf8.GetByteCount($offJson)
    discoveryLogPath=[IO.Path]::GetFullPath($DiscoveryLogPath); discoverySeconds=$null
    discoveryParseSeconds=$parseWatch.Elapsed.TotalSeconds; basePackSeconds=$baseWatch.Elapsed.TotalSeconds
    offPackSeconds=$offWatch.Elapsed.TotalSeconds; boundaryPackSeconds=$packWatch.Elapsed.TotalSeconds
    verificationSeconds=$verificationWatch.Elapsed.TotalSeconds
    boundaryCounts=@($packed | Group-Object boundary | ForEach-Object { @{ boundary=$_.Name; chunks=$_.Count } })
}
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'acceptance.json')
$receipt | ConvertTo-Json -Depth 8
