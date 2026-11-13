#requires -Version 7.0
# CARD-1873: byte-identical boundary-pack witness. Captured before the index-once
# change; verify compares the live packer to those bytes. Digest and identity
# stay in scripts/lib/rc-boundary.ps1.
param(
    [ValidateSet('fixtures', 'real')][string]$Target = 'fixtures',
    [string]$DiscoveryLog,
    [string]$AssemblyPath,
    [switch]$Capture,
    [int]$TimingRuns = 0
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$golden = Join-Path $PSScriptRoot 'fixtures/rc-boundary-pack-golden'
foreach ($name in @('nightly-common','nightly-policy','nightly-coverage','nightly-tests-impl','rc-boundary')) {
    . (Join-Path $PSScriptRoot ('lib/' + $name + '.ps1'))
}
$utf8 = [Text.UTF8Encoding]::new($false)
# 2x the quiet post-change p95 (5.881s) is 12s. A later pack on this host took
# 14.5s while other script cases were running, which overlaps the old quiet
# samples (12.8s). Wall-clock is recorded, not asserted. The standing pin is
# that NightlyLaunchPlan.BoundaryIndexBuilds increases by exactly one per pack.
$script:BoundaryPackSecondsBound = 12

function Assert-BoundaryIndexOnce([int]$Before, [int]$ExpectedDelta) {
    if (-not (Test-NightlyCoreAvailable)) { return }
    $delta = [Antiphon.Nightly.NightlyLaunchPlan]::BoundaryIndexBuilds - $Before
    if ($delta -ne $ExpectedDelta) { throw ('boundary index built ' + $delta + ' times, expected ' + $ExpectedDelta) }
}

$script:PackCanonicalFields = @(
    'id','classes','exactFilter','groupFilter','methodFilter','nodes','nodeCount','seconds',
    'caseMembership','caseIdentityBasis','suite','boundary','membershipDigest','identityKeys',
    'caseKeys','alwaysDirty','collision'
)
function ConvertTo-PackCanonical([object[]]$Plan) {
    # Hashtable enumeration order of the boundary metadata is not stable across
    # processes. Copy the pack fields in a fixed order so the witness bytes are.
    $chunks = foreach ($chunk in @($Plan)) {
        $ordered = [ordered]@{}
        $present = @($chunk.PSObject.Properties.Name)
        foreach ($name in $script:PackCanonicalFields) {
            if ($present -ccontains $name) {
                if ($name -ceq 'caseMembership') {
                    $ordered[$name] = @(foreach ($case in @($chunk.caseMembership)) {
                        [ordered]@{ className=[string]$case.className; method=[string]$case.method; uid=[string]$case.uid }
                    })
                } else { $ordered[$name] = $chunk.$name }
            }
        }
        foreach ($name in @($present | Where-Object { $script:PackCanonicalFields -cnotcontains $_ } | Sort-Object)) {
            $ordered[$name] = $chunk.$name
        }
        [pscustomobject]$ordered
    }
    return @($chunks)
}
function Get-PackJson([object[]]$Plan) {
    return (ConvertTo-Json -InputObject @(ConvertTo-PackCanonical $Plan) -Depth 30 -Compress)
}
function Get-PackSha([string]$Json) {
    $bytes = $utf8.GetBytes($Json)
    return ([BitConverter]::ToString([Security.Cryptography.SHA256]::HashData($bytes)).Replace('-', '').ToLowerInvariant())
}
function Get-PackManifest([object[]]$Plan) {
    $lines = foreach ($chunk in @($Plan)) {
        $classes = (@($chunk.classes) -join ',')
        $uids = (@($chunk.caseMembership | ForEach-Object { [string]$_.uid }) -join ',')
        $seconds = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}', $chunk.seconds)
        (@(
            [string]$chunk.id, [string]$chunk.boundary, [string]$chunk.suite, $classes,
            [string]$chunk.exactFilter, [string]$chunk.groupFilter, [string]$chunk.methodFilter,
            [string]$chunk.nodeCount, $seconds, [string]$chunk.membershipDigest,
            [string]$chunk.alwaysDirty, [string]$chunk.collision, $uids
        ) -join "`t")
    }
    return (($lines -join "`n") + "`n")
}
function Write-Golden([string]$Name, [string]$Text) {
    [IO.Directory]::CreateDirectory($golden) | Out-Null
    [IO.File]::WriteAllText((Join-Path $golden $Name), $Text, $utf8)
}
function Read-Golden([string]$Name) {
    $path = Join-Path $golden $Name
    if (-not [IO.File]::Exists($path)) { throw ('missing golden ' + $Name) }
    return [IO.File]::ReadAllText($path)
}
function Assert-GoldenText([string]$Name, [string]$Actual) {
    $expected = Read-Golden $Name
    if ($expected -cne $Actual) { throw ('golden mismatch ' + $Name) }
}

function New-FixtureNode([string]$Type, [string]$Method = 'Run', [int]$Row = 0) {
    [pscustomobject]@{ ClassName="Antiphon.Tests.Fixture.$Type"; Namespace='Antiphon.Tests.Fixture'; Type=$Type
        Method=$Method; Uid="$Type/$Method/$Row"; Categories=@(); Excluded=$false }
}
function Get-FixtureLeaves([object[]]$Nodes) {
    foreach ($class in @($Nodes | Group-Object { Get-NightlyBareClassIdentity -Name $_.ClassName })) {
        [pscustomobject]@{ id='all-profile-1'; classes=@($class.Name); exactFilter=''; nodes=@() }
    }
}
function Get-FixturePack([object[]]$Nodes, [hashtable]$Boundaries, [object[]]$InputLeaves = @(), [string]$Suite = 'antiphon') {
    if ($InputLeaves.Count -eq 0) { $InputLeaves = @(Get-FixtureLeaves $Nodes) }
    $before = 0
    if (Test-NightlyCoreAvailable) { $before = [Antiphon.Nightly.NightlyLaunchPlan]::BoundaryIndexBuilds }
    $plan = @(ConvertTo-NightlyProfileLaunchGroups -Leaves $InputLeaves -DiscoveryNodes $Nodes -BoundaryAware -SuiteId $Suite -ClassBoundaries $Boundaries)
    Assert-BoundaryIndexOnce $before 1
    return $plan
}

function Get-FixtureGoldens {
    $savedTarget = $script:NightlyLaunchTargetSeconds
    $savedBudget = $script:NightlyLaunchLoneClassBudgetSeconds
    try {
        $sepNodes = @((New-FixtureNode A), (New-FixtureNode AExtra), (New-FixtureNode B), (New-FixtureNode C), (New-FixtureNode Unknown))
        $sepMap = @{ 'Antiphon.Tests.Fixture.A'='core'; 'Antiphon.Tests.Fixture.B'='core'; 'Antiphon.Tests.Fixture.AExtra'='git'; 'Antiphon.Tests.Fixture.C'='git' }
        $out = [ordered]@{}
        $out['separation.json'] = Get-PackJson (Get-FixturePack $sepNodes $sepMap)
        $out['separation-scripts.json'] = Get-PackJson (Get-FixturePack $sepNodes $sepMap @() 'scripts')

        $repackNodes = @((New-FixtureNode Matrix Run 0), (New-FixtureNode Matrix Run 1), (New-FixtureNode Matrix RunExtra 0), (New-FixtureNode Matrix Other 0), (New-FixtureNode Matrix Other 1), (New-FixtureNode Matrix Last 0), (New-FixtureNode Z))
        $repackMap = @{ 'Antiphon.Tests.Fixture.Matrix'='core'; 'Antiphon.Tests.Fixture.Z'='core' }
        $script:NightlyLaunchTargetSeconds = 1
        $script:NightlyLaunchLoneClassBudgetSeconds = 2
        $out['repack-split.json'] = Get-PackJson (Get-FixturePack $repackNodes $repackMap)
        $script:NightlyLaunchTargetSeconds = 900
        $script:NightlyLaunchLoneClassBudgetSeconds = 2400
        $out['repack-restored.json'] = Get-PackJson (Get-FixturePack $repackNodes $repackMap)
        $exact = [pscustomobject]@{ id='all-profile-1'; classes=@('Antiphon.Tests.Fixture.Matrix'); exactFilter='/*/Antiphon.Tests.Fixture/Matrix/Run'; nodes=@($repackNodes | Where-Object Method -CEQ Run | Where-Object Type -CEQ Matrix) }
        $out['repack-exact.json'] = Get-PackJson (Get-FixturePack $repackNodes $repackMap @($exact))
        $constructors = @((New-FixtureNode 'Matrix("a")' Run 0), (New-FixtureNode 'Matrix("b")' Run 0))
        $ctorLeaf = [pscustomobject]@{ id='all-profile-1'; classes=@('Antiphon.Tests.Fixture.Matrix'); exactFilter='/*/Antiphon.Tests.Fixture/Matrix/Run'; nodes=$constructors }
        $out['repack-ctor.json'] = Get-PackJson (Get-FixturePack $constructors $repackMap @($ctorLeaf))

        $defaultNodes = @((New-FixtureNode B), (New-FixtureNode C))
        $defaultLeaves = @(Get-FixtureLeaves $defaultNodes)
        $out['default-off.json'] = Get-PackJson @(ConvertTo-NightlyProfileLaunchGroups -Leaves $defaultLeaves -DiscoveryNodes $defaultNodes)
        $out['default-on.json'] = Get-PackJson (Get-FixturePack $defaultNodes @{ 'Antiphon.Tests.Fixture.B'='core'; 'Antiphon.Tests.Fixture.C'='git' })

        $canonNodes = @((New-FixtureNode Matrix Run 0), (New-FixtureNode Matrix Run 1))
        $canonMap = @{ 'Antiphon.Tests.Fixture.Matrix'='core' }
        $out['canonical.json'] = Get-PackJson (Get-FixturePack $canonNodes $canonMap)
        $changed = @((New-FixtureNode Matrix Run 0), (New-FixtureNode Matrix Run 2))
        $out['canonical-changed.json'] = Get-PackJson (Get-FixturePack $changed $canonMap)

        $keys = @('10788987499786589518','17490688019260441962')
        $chunks = @(foreach ($key in $keys) { [pscustomobject]@{ id='pending'; nodes=@([pscustomobject]@{ Uid=$key }); marker=$key } })
        $memberships = @(foreach ($key in $keys) { @{ suite='antiphon'; boundary='core'; identities=@(@{ identityKey='CTests/M'; caseKey=$key }) } })
        $out['collision.json'] = Get-PackJson @(Complete-NightlyBoundaryLaunchGroups -Chunks $chunks -Memberships $memberships)
        return $out
    } finally {
        $script:NightlyLaunchTargetSeconds = $savedTarget
        $script:NightlyLaunchLoneClassBudgetSeconds = $savedBudget
    }
}

function Get-RealTreeInputs([object[]]$Nodes) {
    $byClass = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($node in $Nodes) {
        $bare = Get-NightlyBareClassIdentity $node.ClassName
        if (-not $byClass.ContainsKey($bare)) { $byClass.Add($bare, [Collections.Generic.List[object]]::new()) }
        $byClass[$bare].Add($node)
    }
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
    return [pscustomobject]@{ Leaves = $leaves; Boundaries = $boundaries; Classes = $byClass.Count }
}

function Get-RealTreeNodes {
    if ([string]::IsNullOrWhiteSpace($DiscoveryLog) -or [string]::IsNullOrWhiteSpace($AssemblyPath)) {
        throw 'real target requires -DiscoveryLog and -AssemblyPath'
    }
    $parsed = ConvertFrom-NightlyDiagnosticLog -Path $DiscoveryLog -Kind discovery
    $document = ConvertTo-NightlyDiscoveryDocument -Nodes @($parsed.Nodes) -AssemblyHash (Get-NightlyAssemblyHash $AssemblyPath)
    return @((ConvertFrom-NightlyDiscoveryDocument $document 'antiphon-tunit-discovery-v1').Nodes)
}

if ($Target -eq 'fixtures') {
    $plans = Get-FixtureGoldens
    foreach ($name in $plans.Keys) {
        if ($Capture) { Write-Golden $name $plans[$name] }
        else { Assert-GoldenText $name $plans[$name] }
    }
    Write-Output ('fixtures ' + $(if ($Capture) { 'captured' } else { 'matched' }) + ' count=' + $plans.Count)
    return
}

$nodes = Get-RealTreeNodes
$inputs = Get-RealTreeInputs $nodes
$beforeOff = 0
if (Test-NightlyCoreAvailable) { $beforeOff = [Antiphon.Nightly.NightlyLaunchPlan]::BoundaryIndexBuilds }
$offWatch = [Diagnostics.Stopwatch]::StartNew()
$offJson = Get-PackJson @(ConvertTo-NightlyProfileLaunchGroups -Leaves $inputs.Leaves -DiscoveryNodes $nodes)
$offWatch.Stop()
Assert-BoundaryIndexOnce $beforeOff 0
$offBytes = $utf8.GetByteCount($offJson)
$times = New-Object System.Collections.Generic.List[double]
$boundaryJson = $null
$boundaryPlan = $null
$runs = [Math]::Max(1, $TimingRuns)
for ($i = 0; $i -lt $runs; $i++) {
    $beforePack = 0
    if (Test-NightlyCoreAvailable) { $beforePack = [Antiphon.Nightly.NightlyLaunchPlan]::BoundaryIndexBuilds }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $boundaryPlan = @(ConvertTo-NightlyProfileLaunchGroups -Leaves $inputs.Leaves -DiscoveryNodes $nodes -BoundaryAware -SuiteId antiphon -ClassBoundaries $inputs.Boundaries)
    $watch.Stop()
    Assert-BoundaryIndexOnce $beforePack 1
    $times.Add($watch.Elapsed.TotalSeconds)
    if ($i -eq 0) { $boundaryJson = Get-PackJson $boundaryPlan }
}
$sorted = @($times | Sort-Object)
$p50 = $sorted[[int][Math]::Floor(($sorted.Count - 1) / 2)]
$p95 = $sorted[[int][Math]::Ceiling(($sorted.Count - 1) * 0.95)]
$manifest = Get-PackManifest $boundaryPlan
$record = [ordered]@{
    cases = $nodes.Count
    classes = $inputs.Classes
    chunks = @($boundaryPlan).Count
    optOutBytes = $offBytes
    optOutSha256 = (Get-PackSha $offJson)
    boundarySha256 = (Get-PackSha $boundaryJson)
    manifestSha256 = (Get-PackSha $manifest)
    offPackSeconds = $offWatch.Elapsed.TotalSeconds
    boundarySeconds = @($times)
    boundaryP50 = $p50
    boundaryP95 = $p95
    boundSeconds = $script:BoundaryPackSecondsBound
    boundEnforced = $false
}
if ($Capture) {
    Write-Golden 'real-tree-off.sha256' ($record.optOutSha256 + "`n")
    Write-Golden 'real-tree-off.bytes' ([string]$offBytes + "`n")
    Write-Golden 'real-tree-boundary.sha256' ($record.boundarySha256 + "`n")
    Write-Golden 'real-tree-manifest.sha256' ($record.manifestSha256 + "`n")
    Write-Golden 'real-tree-manifest.txt' $manifest
    Write-Golden 'real-tree-baseline.txt' (($record | ConvertTo-Json -Compress) + "`n")
} else {
    Assert-GoldenText 'real-tree-off.sha256' ($record.optOutSha256 + "`n")
    Assert-GoldenText 'real-tree-off.bytes' ([string]$offBytes + "`n")
    Assert-GoldenText 'real-tree-boundary.sha256' ($record.boundarySha256 + "`n")
    Assert-GoldenText 'real-tree-manifest.sha256' ($record.manifestSha256 + "`n")
    Assert-GoldenText 'real-tree-manifest.txt' $manifest
}
$record | ConvertTo-Json -Compress
