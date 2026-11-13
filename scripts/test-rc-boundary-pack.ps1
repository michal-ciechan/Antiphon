#requires -Version 7.0
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
foreach ($name in @('nightly-common','nightly-policy','nightly-coverage','nightly-tests-impl','rc-boundary','c487-harness')) {
    . (Join-Path $PSScriptRoot ('lib/' + $name + '.ps1'))
}
$root = New-C487Root $ResultsDirectory
$script:errors = [Collections.Generic.List[string]]::new()
function Note([bool]$Ok, [string]$Why) { if (-not $Ok) { $script:errors.Add($Why) } }
function Refuses([scriptblock]$Action) { try { & $Action | Out-Null; return $false } catch { return $true } }
function Node([string]$Type, [string]$Method = 'Run', [int]$Row = 0) {
    [pscustomobject]@{ ClassName="Antiphon.Tests.Fixture.$Type"; Namespace='Antiphon.Tests.Fixture'; Type=$Type
        Method=$Method; Uid="$Type/$Method/$Row"; Categories=@(); Excluded=$false }
}
function Leaves([object[]]$Nodes) {
    foreach ($class in @($Nodes | Group-Object { Get-NightlyBareClassIdentity -Name $_.ClassName })) {
        [pscustomobject]@{ id='all-profile-1'; classes=@($class.Name); exactFilter=''; nodes=@() }
    }
}
function Pack([object[]]$Nodes, [hashtable]$Boundaries, [object[]]$InputLeaves = @(), [string]$Suite = 'antiphon') {
    if ($InputLeaves.Count -eq 0) { $InputLeaves = @(Leaves $Nodes) }
    # Before S4a, the default-deny adapter exercises the real old packer. The red
    # is a wrong packing result, never a missing function/parameter exception.
    $options = @{}
    if ((Get-Command ConvertTo-NightlyProfileLaunchGroups).Parameters.ContainsKey('BoundaryAware')) {
        $options = @{ BoundaryAware=$true; SuiteId=$Suite; ClassBoundaries=$Boundaries }
    }
    @(ConvertTo-NightlyProfileLaunchGroups -Leaves $InputLeaves -DiscoveryNodes $Nodes @options)
}
function Selected([object[]]$Plan, [object[]]$Nodes) {
    foreach ($chunk in $Plan) {
        foreach ($node in $Nodes) {
            $type = ($node.Type -split '\(', 2)[0]
            $path = '/*/{0}/{1}/{2}' -f $node.Namespace, $type, $node.Method
            if ($chunk.exactFilter) { $hit = $path -ceq $chunk.exactFilter }
            elseif ($chunk.methodFilter) {
                $parts = $chunk.methodFilter.Split('/')
                $hit = $node.Namespace -ceq $parts[2] -and $type -ceq $parts[3] -and $node.Method -cmatch ('^(?:' + $parts[4].Replace('*','.*') + ')$')
            } elseif ($chunk.groupFilter) {
                $parts = $chunk.groupFilter.Split('/')
                $hit = $node.Namespace -ceq $parts[2] -and $type -cmatch ('^(?:' + $parts[3].Replace('*','.*') + ')$')
            } else { $hit = @($chunk.classes) -ccontains ($node.Namespace + '.' + $type) }
            if ($hit) { $node.Uid }
        }
    }
}
function SameBag([object[]]$Left, [object[]]$Right) {
    return (($Left | Sort-Object -CaseSensitive | ConvertTo-Json -Compress) -ceq ($Right | Sort-Object -CaseSensitive | ConvertTo-Json -Compress))
}
function Test-Separation {
    $nodes = @((Node A), (Node AExtra), (Node B), (Node C), (Node Unknown))
    $map = @{ 'Antiphon.Tests.Fixture.A'='core'; 'Antiphon.Tests.Fixture.B'='core'; 'Antiphon.Tests.Fixture.AExtra'='git'; 'Antiphon.Tests.Fixture.C'='git' }
    $plan = @(Pack $nodes $map)
    Note ($plan.Count -eq 4) 'boundaries/prefix-unsafe classes were not separated'
    foreach ($chunk in $plan) {
        $expected = @($chunk.classes | ForEach-Object { if ($map.ContainsKey($_)) { $map[$_] } else { 'unassigned' } } | Select-Object -Unique)
        Note ($expected.Count -eq 1 -and $chunk.boundary -ceq $expected[0]) 'mixed or missing boundary'
        Note ($chunk.suite -ceq 'antiphon') 'suite lost'
        Note ($chunk.id -cmatch '^b-[a-z-]+-[0-9a-f]{16}$' -and $chunk.membershipDigest -cmatch '^[0-9a-f]{64}$') 'membership hash absent'
        Note ([bool]$chunk.alwaysDirty -eq ($expected -ccontains 'unassigned')) 'unassigned is carryable'
    }
    Note (SameBag @(Selected $plan $nodes) @($nodes.Uid)) 'filters duplicate or omit a prefix sibling'
    $other = @(Pack $nodes $map @() scripts)
    Note (@($plan.id | Where-Object { $other.id -ccontains $_ }).Count -eq 0) 'suite aliases chunk IDs'
}
function Test-Repack {
    $nodes = @((Node Matrix Run 0), (Node Matrix Run 1), (Node Matrix RunExtra 0), (Node Matrix Other 0), (Node Matrix Other 1), (Node Matrix Last 0), (Node Z))
    $map = @{ 'Antiphon.Tests.Fixture.Matrix'='core'; 'Antiphon.Tests.Fixture.Z'='core' }
    $script:NightlyLaunchTargetSeconds = 1
    $script:NightlyLaunchLoneClassBudgetSeconds = 2
    $plan = @(Pack $nodes $map)
    Note ($plan.Count -ge 4) 'oversized class did not split'
    Note (SameBag @(Selected $plan $nodes) @($nodes.Uid)) 'split filter case universe changed'
    Note (SameBag @($plan | ForEach-Object { $_.caseMembership | ForEach-Object { $_.uid } }) @($nodes.Uid)) 'case manifest incomplete'
    Note (SameBag @($plan | ForEach-Object { $_.caseKeys }) @($nodes.Uid)) 'canonical case keys incomplete or duplicated'
    Note (SameBag @($plan | ForEach-Object { $_.identityKeys } | Select-Object -Unique) @('Antiphon.Tests.Fixture.Matrix/Run','Antiphon.Tests.Fixture.Matrix/RunExtra','Antiphon.Tests.Fixture.Matrix/Other','Antiphon.Tests.Fixture.Matrix/Last','Antiphon.Tests.Fixture.Z/Run')) 'method manifest incomplete'
    $reversed = @($nodes); [array]::Reverse($reversed)
    Note (SameBag @($plan.id) @((Pack $reversed $map).id)) 'discovery order changed IDs'
    $script:NightlyLaunchTargetSeconds = 900; $script:NightlyLaunchLoneClassBudgetSeconds = 2400
    $repacked = @(Pack $nodes $map)
    Note (SameBag @(Selected $repacked $nodes) @($nodes.Uid)) 'repack changed case universe'
    Note (SameBag @($repacked | ForEach-Object { $_.caseMembership.uid }) @($nodes.Uid)) 'repack lost case evidence'
    Note (Refuses { Pack @($nodes + $nodes[0]) $map }) 'duplicate discovery UID accepted'
    $leaves = @(Leaves $nodes)
    Note (Refuses { Pack $nodes $map @($leaves + $leaves[0]) }) 'overlapping leaves accepted'
    $exact = [pscustomobject]@{ id='all-profile-1'; classes=@('Antiphon.Tests.Fixture.Matrix'); exactFilter='/*/Antiphon.Tests.Fixture/Matrix/Run'; nodes=@($nodes | Where-Object Method -CEQ Run | Where-Object Type -CEQ Matrix) }
    $partial = @(Pack $nodes $map @($exact))
    Note (SameBag @(Selected $partial $nodes) @('Matrix/Run/0','Matrix/Run/1')) 'exact leaf launches excluded sibling'
    Note (SameBag @($partial.caseMembership.uid) @('Matrix/Run/0','Matrix/Run/1')) 'exact leaf manifest includes excluded sibling'
    $constructors = @((Node 'Matrix("a")' Run 0), (Node 'Matrix("b")' Run 0))
    $ctorLeaf = [pscustomobject]@{ id='all-profile-1'; classes=@('Antiphon.Tests.Fixture.Matrix'); exactFilter='/*/Antiphon.Tests.Fixture/Matrix/Run'; nodes=$constructors }
    try {
        $ctorPlan = @(Pack $constructors $map @($ctorLeaf))
        Note (SameBag @(Selected $ctorPlan $constructors) @($constructors.Uid)) 'constructor filter lost cases'
        Note (SameBag @($ctorPlan.caseMembership.className) @($constructors.ClassName)) 'constructor identities collapsed'
        Note (SameBag @($ctorPlan.identityKeys) @('Antiphon.Tests.Fixture.Matrix/Run','Antiphon.Tests.Fixture.Matrix/Run')) 'constructor canonical identity pairs changed'
        Note (SameBag @($ctorPlan.caseKeys) @($constructors.Uid)) 'constructor canonical case keys collapsed'
    } catch { Note $false ('constructor exact leaf refused: ' + $_.Exception.Message) }
    Note (Refuses { Pack @((Node Upper Run), (Node upper Other)) @{} }) 'case-colliding classes accepted'
}
function Test-Default {
    $nodes = @((Node B), (Node C))
    $leaves = @(Leaves $nodes)
    $plan = @(ConvertTo-NightlyProfileLaunchGroups -Leaves $leaves -DiscoveryNodes $nodes)
    Note ($plan.Count -eq 1 -and $plan[0].id -ceq 'all-group-1') 'Full ID changed'
    Note ($plan[0].groupFilter -ceq '/*/Antiphon.Tests.Fixture/(B*)|(C*)/*') 'Full filter changed'
    Note ($plan[0].PSObject.Properties.Name -cnotcontains 'boundary') 'Full opted into boundary metadata'
    Note ($plan[0].nodeCount -eq 2 -and $plan[0].seconds -eq 1) 'Full estimate changed'
    # The new path must be opt-in and produce an actual different result.
    $opt = @(Pack $nodes @{ 'Antiphon.Tests.Fixture.B'='core'; 'Antiphon.Tests.Fixture.C'='git' })
    Note ($opt.Count -eq 2 -and @($opt.id | Where-Object { $_ -cmatch '^b-' }).Count -eq 2) 'opt-in ignored'
}
function Test-CanonicalCases {
    $nodes = @((Node Matrix Run 0), (Node Matrix Run 1))
    $map = @{ 'Antiphon.Tests.Fixture.Matrix'='core' }
    try {
        $plan = @(Pack $nodes $map)
        # Pinned length-delimited vector, including both expanded case UIDs.
        Note ($plan.Count -eq 1 -and $plan[0].id -ceq 'b-core-c3d9377ff31dfd63') 'packer did not use canonical case membership'
        Note (SameBag @($plan[0].caseKeys) @($nodes.Uid)) 'S2 did not receive case keys'
        $changed = @((Node Matrix Run 0), (Node Matrix Run 2))
        Note ($plan[0].id -cne (Pack $changed $map)[0].id) 'changed argument case reused the ID'
        Note (SameBag @(Selected $plan $nodes) @($nodes.Uid)) 'canonical packing changed selection'
    } catch { Note $false ('canonical pack refused: ' + $_.Exception.Message) }
}
function Test-SerializerOrder {
    # pwsh 7 ConvertTo-Json ordinal order. U+0085 must sort as "\u0085", before "xa".
    . (Join-Path $PSScriptRoot 'fixtures/rc-boundary-pack-golden/serializer-order.ps1')
    $oracle = @(Get-RcSerializerOracleFailures)
    Note ($oracle.Count -eq 0) ('ConvertTo-Json oracle moved: ' + ($oracle -join ','))
    $keys = @(Get-RcSerializerKeyFailures)
    Note ($keys.Count -eq 0) ('sort key is not ConvertTo-Json: ' + ($keys -join ','))
    $codes = @(Get-RcSerializerCodeUnitFailures)
    Note ($codes.Count -eq 0) ('code unit sort key diverges: ' + ($codes -join ','))
    $nodes = @(New-RcSerializerOrderNodes)
    $map = Get-RcSerializerOrderMap
    $before = [Antiphon.Nightly.NightlyLaunchPlan]::BoundaryIndexBuilds
    try {
        $plan = @(Pack $nodes $map @((New-RcSerializerOrderLeaf $nodes)))
        Note (($plan.Count -eq 1) -and ($plan[0].boundary -ceq 'core')) 'serializer-order constructors did not stay one core chunk'
        $built = [Antiphon.Nightly.NightlyLaunchPlan]::BoundaryIndexBuilds - $before
        Note ($built -eq 1) ('boundary index built ' + $built + ' times')
        $actual = @($plan[0].caseMembership | ForEach-Object { [string]$_.uid })
        $expected = @(Get-RcSerializerUidOrder $nodes)
        Note (($actual -join ',') -ceq ($expected -join ',')) ('membership order ' + ($actual -join ',') + ' != ' + ($expected -join ','))
        $nel = [array]::IndexOf($actual, '1')
        $xa = [array]::IndexOf($actual, '2')
        Note ($nel -ge 0 -and $xa -ge 0 -and $nel -lt $xa) ('U+0085 uid 1 sorted after xa uid 2 (' + $nel + ',' + $xa + ')')
        Note (@($plan[0].identityKeys | Select-Object -Unique) -join '|' -ceq 'Antiphon.Tests.Fixture.Matrix/Run') 'constructor identity was not the bare class'
    } catch { Note $false ('serializer-order pack refused: ' + $_.Exception.Message) }
    $numeric = @(New-RcSerializerNumericUidNodes)
    $beforeNumeric = [Antiphon.Nightly.NightlyLaunchPlan]::BoundaryIndexBuilds
    try {
        $numericPlan = @(Pack $numeric $map @((New-RcSerializerOrderLeaf $numeric)))
        $builtNumeric = [Antiphon.Nightly.NightlyLaunchPlan]::BoundaryIndexBuilds - $beforeNumeric
        Note ($builtNumeric -eq 1) ('numeric uid index built ' + $builtNumeric + ' times')
        $numericActual = @($numericPlan[0].caseMembership | ForEach-Object { [string]$_.uid })
        Note (($numericActual -join ',') -ceq '10,2') ('uid string order lost: ' + ($numericActual -join ','))
    } catch { Note $false ('numeric uid pack refused: ' + $_.Exception.Message) }
}
function Test-Collision {
    # Real S2 64-bit collision through the finalizer used by the packer.
    # Already partitioned launch descriptors witness metadata and case custody;
    # they do not claim that a method filter selects individual argument cases.
    $keys = @('10788987499786589518','17490688019260441962')
    $chunks = @(foreach ($key in $keys) {
        [pscustomobject]@{ id='pending'; nodes=@([pscustomobject]@{ Uid=$key }); marker=$key }
    })
    $memberships = @(foreach ($key in $keys) {
        @{ suite='antiphon'; boundary='core'; identities=@(@{ identityKey='CTests/M'; caseKey=$key }) }
    })
    Note ((Get-RcBoundaryChunkId antiphon core @('CTests/M') -CaseKeys @($keys[0])) -ceq 'b-core-360a4ef28b10a96c') 'collision fixture moved'
    try {
        $plan = @(Complete-NightlyBoundaryLaunchGroups -Chunks $chunks -Memberships $memberships)
        Note ($plan.Count -eq 2) 'collision dropped a launch'
        foreach ($chunk in $plan) {
            Note ($chunk.boundary -ceq 'unassigned' -and $chunk.alwaysDirty -eq $true -and $chunk.collision -eq $true) 'collision was not dirty/unassigned'
            Note ($chunk.id -ceq ('b-unassigned-' + $chunk.membershipDigest) -and $chunk.membershipDigest.Length -eq 64) 'collision lost full digest ID'
            Note ($chunk.caseKeys[0] -ceq $chunk.marker) 'resolution reordered case ownership'
        }
        Note ($plan[0].id -cne $plan[1].id) 'collision IDs collapsed'
        Note (SameBag @($plan | ForEach-Object { $_.nodes.Uid }) $keys) 'collision omitted or duplicated cases'
    } catch { Note $false ('collision threw instead of resolving: ' + $_.Exception.Message) }
}
switch ($Case) {
    TwoBoundariesAndPrefixCollisionsStaySeparate { Test-Separation }
    RepackPreservesCaseUniverse { Test-Repack }
    DefaultFullPackKeepsIds { Test-Default }
    CanonicalDigestIncludesCaseKeys { Test-CanonicalCases }
    ShortIdCollisionUsesS2Policy { Test-Collision }
    SerializerEquivalentConstructorOrder { Test-SerializerOrder }
    default { throw "unknown case $Case" }
}
Assert-C487 ($script:errors.Count -eq 0) ('RBP ' + $Case + '.contract') ($script:errors -join '; ')
Complete-C487Harness -ResultsDirectory $root -ExpectedRows 1
