#requires -Version 5.1
# CARD-1836 S2. Pure fixtures only; never imports the qualification launcher.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('rb-' + [guid]::NewGuid().ToString('N')) }
if (-not (Test-Path -LiteralPath $ResultsDirectory)) { New-Item -ItemType Directory -Path $ResultsDirectory | Out-Null }
$lib = Join-Path $PSScriptRoot 'lib/rc-boundary.ps1'
$script:errors = New-Object 'System.Collections.Generic.List[string]'
function Note([bool]$Ok, [string]$Why) { if (-not $Ok) { $script:errors.Add($Why) } }
function Refuses([scriptblock]$Action) { try { & $Action | Out-Null; return $false } catch { return $true } }
function Row([string]$Boundary, [string]$Key, [string]$Suite = 'antiphon') { return @{ boundary=$Boundary; suite=$Suite; identityKey=$Key } }
if (-not (Test-Path -LiteralPath $lib)) {
    Note $false 'boundary library absent'
} else {
    . $lib
    $map = Read-RcBoundaryMap
    switch ($Case) {
        'StableIdIgnoresOrder' {
            $id = Get-RcBoundaryChunkId -Suite antiphon -Boundary core -IdentityKeys @('ZTests/B', 'ATests/A')
            # Independently pinned SHA-256 of compact JSON [suite,boundary,sorted keys].
            Note ($id -ceq 'b-core-25837f34776fc625') ('canonical id: ' + $id)
            Note ($id -ceq (Get-RcBoundaryChunkId antiphon core @('ATests/A','ZTests/B'))) 'packing order changed id'
            Note ($id -cne (Get-RcBoundaryChunkId scripts core @('ATests/A','ZTests/B'))) 'suite omitted'
            Note ($id -cne (Get-RcBoundaryChunkId antiphon git @('ATests/A','ZTests/B'))) 'boundary omitted'
            Note ($id -cne (Get-RcBoundaryChunkId antiphon core @('ATests/a','ZTests/B'))) 'identity case folded'
            Note (Refuses { Get-RcBoundaryChunkId antiphon core @() }) 'empty membership accepted'
            Note (Refuses { Get-RcBoundaryChunkId antiphon core @('ATests/A','ATests/A') }) 'duplicate membership accepted'
        }
        'MovedClassChangesId' {
            $beforeA = Get-RcBoundaryChunkId antiphon core @('ATests/A','BTests/B')
            $beforeB = Get-RcBoundaryChunkId antiphon core @('CTests/C')
            Note ($beforeA -cne (Get-RcBoundaryChunkId antiphon core @('ATests/A'))) 'source bin id survived move'
            Note ($beforeB -cne (Get-RcBoundaryChunkId antiphon core @('BTests/B','CTests/C'))) 'destination bin id survived move'
            Note ($beforeA -cne (Get-RcBoundaryChunkId antiphon core @('ATests/A','BTests/Other'))) 'method omitted'
        }
        'UnassignedStaysAlone' {
            Note ((Get-RcClassBoundary -Map $map -Suite antiphon -TestPath 'tests/Antiphon.Tests/Infrastructure/Unknown.cs' -Source 'namespace Antiphon.Tests.Infrastructure;') -ceq 'unassigned') 'generic Infrastructure guessed'
            Note ((Get-RcClassBoundary -Map $map -Suite unknown -TestPath 'tests/Elsewhere.cs' -Source '') -ceq 'unassigned') 'unknown suite guessed'
            Note (Refuses { New-RcBoundaryChunk -Identities @((Row core 'ATests/A'), (Row unassigned 'BTests/B')) }) 'unassigned joined core'
            $alone = New-RcBoundaryChunk -Identities @((Row unassigned 'BTests/B'))
            Note ($alone.boundary -ceq 'unassigned' -and $alone.alwaysDirty -eq $true) 'unknown is not dirty'
            Note ($alone.chunkId -match '^b-unassigned-[0-9a-f]{16}$') 'unassigned id missing'
        }
        'TwoBoundariesNeverShare' {
            Note (Refuses { New-RcBoundaryChunk -Identities @((Row core 'ATests/A'), (Row git 'BTests/B')) }) 'mixed boundary accepted'
            Note (Refuses { New-RcBoundaryChunk -Identities @((Row core 'ATests/A'), (Row core 'BTests/B' scripts)) }) 'mixed suite accepted'
            $chunk = New-RcBoundaryChunk -Identities @((Row core 'ZTests/B'), (Row core 'ATests/A'))
            Note ($chunk.boundary -ceq 'core' -and $chunk.suite -ceq 'antiphon' -and -not $chunk.alwaysDirty) 'single boundary refused'
            Note (($chunk.identityKeys -join ',') -ceq 'ATests/A,ZTests/B') 'membership not canonical'
            # Lane path wins over both production references and namespace fallback.
            foreach ($lane in @(@('Scripts','scripts'),@('Checkpoints','checkpoints'),@('AgentTui','tui'))) {
                $b = Get-RcClassBoundary -Map $map -Suite antiphon -TestPath ('tests/Antiphon.Tests/' + $lane[0] + '/Example.cs') -Source 'namespace Antiphon.Tests.Domain; string p = "server/Domain/Example.cs";'
                Note ($b -ceq $lane[1]) ('lane precedence: ' + $lane[0])
            }
            foreach ($suite in @('e2e','client','messaging','session-runner','pty-host','agents-pty','observability','server-git','markdown-pdf')) {
                Note ((Get-RcClassBoundary -Map $map -Suite $suite -TestPath 'arbitrary' -Source '') -ceq $suite) ('suite lane: ' + $suite)
            }
            $examples = @(
                @('server/Api/Foo.cs','host'), @('server/Domain/Foo.cs','core'),
                @('server/Infrastructure/Data/Foo.cs','persistence'), @('server/Application/Services/GitIndexLock.cs','git'),
                @('server/Infrastructure/Agents/Pty/Foo.cs','runners'), @('server/Infrastructure/Agents/Foo.cs','integrations'),
                @('server/Application/Services/Foo.cs','application'), @('server/Infrastructure/Resilience/NpgsqlTransientFailureClassifier.cs','persistence')
            )
            foreach ($example in $examples) {
                Note ((Get-RcClassBoundary -Map $map -Suite antiphon -TestPath 'tests/Antiphon.Tests/Other/Foo.cs' -Source ('namespace Antiphon.Tests.Domain; string p = "' + $example[0] + '";')) -ceq $example[1]) ('production glob: ' + $example[0])
            }
            $multi = 'string a="server/Application/Services/Foo.cs"; string b="server/Domain/Foo.cs";'
            Note ((Get-RcClassBoundary -Map $map -Suite antiphon -TestPath 'other' -Source $multi) -ceq 'core') 'rule order lost'
            Note ((Get-RcClassBoundary -Map $map -Suite antiphon -TestPath 'other' -Source 'namespace Antiphon.Tests.Domain.Sub;') -ceq 'core') 'namespace fallback missing'
            Note ((Get-RcClassBoundary -Map $map -Suite antiphon -TestPath 'other' -Source 'namespace Antiphon.Tests.DomainExtra;') -ceq 'unassigned') 'namespace prefix boundary lost'
            Note (@($map.edges.host).Count -eq 6 -and @($map.edges.git)[0] -ceq 'core') 'layer edges missing'
            Note ((Get-RcClassBoundary -Map $map -Suite antiphon -TestPath 'other' -Source 'string p="server/Infrastructure/Agents/nested/Foo.cs";') -ceq 'unassigned') 'single star crossed directory'
        }
        default { throw ('unknown case ' + $Case) }
    }
}
Assert-C487 ($script:errors.Count -eq 0) ('RB ' + $Case + '.contract') ($script:errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
