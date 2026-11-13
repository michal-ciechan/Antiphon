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
        'DuplicateMembershipsAndUnassignedCollisions' {
            $one = @{ suite='antiphon'; boundary='core'; identities=@(@{ identityKey='ATests/M'; caseKey='' }) }
            $overlap = @{ suite='antiphon'; boundary='core'; identities=@(@{ identityKey='ATests/M'; caseKey='' }, @{ identityKey='BTests/M' }) }
            $otherBoundary = @{ suite='antiphon'; boundary='git'; identities=@(@{ identityKey='ATests/M'; caseKey='' }) }
            Note (Refuses { Resolve-RcBoundaryChunks -Memberships @($one, $one) }) 'identical memberships accepted'
            Note (Refuses { Resolve-RcBoundaryChunks -Memberships @($one, $overlap) }) 'overlapping memberships accepted'
            Note (Refuses { Resolve-RcBoundaryChunks -Memberships @($one, $otherBoundary) }) 'one identity assigned to two boundaries'
            Note (Refuses { New-RcBoundaryChunk -Identities @((Row core 'ATests/M'), (Row core 'ATests/M')) }) 'duplicate within chunk accepted'
            $distinct = @(Resolve-RcBoundaryChunks -Memberships @($one,
                @{ suite='scripts'; boundary='core'; identities=@(@{ identityKey='ATests/M'; caseKey='' }) },
                @{ suite='antiphon'; boundary='core'; identities=@(@{ identityKey='ATests/M'; caseKey='case' }) }
            ))
            Note ($distinct.Count -eq 3) 'distinct suite/case identities refused'
            foreach ($chunk in $distinct) { Note (-not $chunk.collision -and -not $chunk.alwaysDirty) 'distinct identity marked dirty' }
            # Force only the digest prefix; retain the real SHA suffix and all real
            # construction/rebuild paths. A real unassigned 64-bit collision is costly.
            $script:realDigest = ${function:Get-RcBoundaryCanonicalDigest}
            try {
                function Get-RcBoundaryCanonicalDigest {
                    param($Suite, $Boundary, $Identities)
                    $hex = & $script:realDigest -Suite $Suite -Boundary $Boundary -Identities $Identities
                    if ($Boundary -ceq 'unassigned') { return ('0000000000000000' + $hex.Substring(16)) }
                    return $hex
                }
                $chunks = @(Resolve-RcBoundaryChunks -Memberships @(
                    @{ suite='antiphon'; boundary='unassigned'; identities=@(@{ identityKey='ATests/M'; caseKey='left' }) },
                    @{ suite='antiphon'; boundary='unassigned'; identities=@(@{ identityKey='BTests/M'; caseKey='right' }) }
                ))
                Note ($chunks.Count -eq 2) 'unassigned collision lost membership'
                $byId = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
                foreach ($chunk in $chunks) {
                    Note ($chunk.collision -and $chunk.alwaysDirty -and $chunk.boundary -ceq 'unassigned') 'unassigned collision became clean'
                    Note ($chunk.fullDigest -cmatch '^[0-9a-f]{64}$') 'unassigned full digest lost'
                    Note ($chunk.chunkId -ceq ('b-unassigned-' + $chunk.fullDigest)) 'collision key is not full digest'
                    $byId[$chunk.chunkId] = $chunk
                }
                Note ($byId.Count -eq 2) 'dictionary overwrite erased colliding membership'
                Note ((@($chunks | ForEach-Object { $_.caseKeys[0] }) -join ',') -ceq 'left,right') 'colliding cases lost'
            } finally { Set-Item Function:Get-RcBoundaryCanonicalDigest $script:realDigest }
        }
        'LoneSurrogatesFailClosed' {
            foreach ($code in @(0xd800, 0xdc00)) {
                $bad = [string][char]$code
                foreach ($key in @(($bad + 'Tests/M'), ('ATests/' + $bad))) {
                    Note (Refuses { Get-RcBoundaryChunkId antiphon core @($key) }) 'lone surrogate identity accepted'
                    Note (Refuses { New-RcBoundaryChunk -Identities @((Row core $key)) }) 'lone surrogate chunk accepted'
                }
                Note (Refuses { Get-RcBoundaryChunkId antiphon core @('ATests/M') -CaseKeys @($bad) }) 'lone surrogate case key accepted'
            }
            # A well-formed pair and the replacement character remain legal and distinct.
            $pair = [char]::ConvertFromUtf32(0x1f600)
            $replacement = [string][char]0xfffd
            $valid = Get-RcBoundaryChunkId antiphon core @('ATests/M') -CaseKeys @($pair)
            Note ($valid -cne (Get-RcBoundaryChunkId antiphon core @('ATests/M') -CaseKeys @($replacement))) 'valid Unicode collapsed'
        }
        'StableIdIgnoresOrder' {
            $id = Get-RcBoundaryChunkId -Suite antiphon -Boundary core -IdentityKeys @('ZTests/B', 'ATests/A')
            # Independently pinned SHA-256 of length-delimited UTF-8 membership bytes.
            Note ($id -ceq 'b-core-eca0b76b8b1ed195') ('canonical id: ' + $id)
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
            $multi = 'namespace Antiphon.Tests.Domain; string a="server/Application/Services/Foo.cs"; string b="server/Domain/Foo.cs";'
            Note ((Get-RcClassBoundary -Map $map -Suite antiphon -TestPath 'other' -Source $multi) -ceq 'unassigned') 'conflicting rules assigned'
            $same = 'string a="server/Domain/Foo.cs"; string b="server/Domain/Bar.cs";'
            Note ((Get-RcClassBoundary -Map $map -Suite antiphon -TestPath 'other' -Source $same) -ceq 'core') 'same boundary treated as conflict'
            Note ((Get-RcClassBoundary -Map $map -Suite antiphon -TestPath 'other' -Source 'namespace Antiphon.Tests.Domain.Sub;') -ceq 'core') 'namespace fallback missing'
            Note ((Get-RcClassBoundary -Map $map -Suite antiphon -TestPath 'other' -Source 'namespace Antiphon.Tests.DomainExtra;') -ceq 'unassigned') 'namespace prefix boundary lost'
            Note (@($map.edges.host).Count -eq 6 -and @($map.edges.git)[0] -ceq 'core') 'layer edges missing'
            Note ((Get-RcClassBoundary -Map $map -Suite antiphon -TestPath 'other' -Source 'string p="server/Infrastructure/Agents/nested/Foo.cs";') -ceq 'unassigned') 'single star crossed directory'
        }
        'CaseKeyChangesIdentity' {
            $script:held = $null
            $threw = Refuses {
                $script:held = New-RcBoundaryChunk -Identities @(
                    @{ boundary = 'core'; suite = 'antiphon'; identityKey = 'ATests/A'; caseKey = 'one<' },
                    @{ boundary = 'core'; suite = 'antiphon'; identityKey = 'ATests/A'; caseKey = "two&'" }
                )
            }
            Note (-not $threw) 'distinct case keys refused'
            if (-not $threw) {
                Note (@($script:held.caseKeys).Count -eq 2) 'caseKey collapsed'
                $methodOnly = Get-RcBoundaryChunkId antiphon core @('ATests/A')
                Note ($script:held.chunkId -cne $methodOnly) 'caseKey omitted from digest'
                Note ((@($script:held.caseKeys) -join '|') -ceq "one<|two&'") 'case key order lost'
            }
        }
        'CanonicalEncodingKeepsSpecialCharacters' {
            # Key contains < > & '. Independently computed length-delimited SHA-256,
            # not ConvertTo-Json (pwsh 7 and Windows PowerShell 5.1 escape those differently).
            $id = Get-RcBoundaryChunkId -Suite antiphon -Boundary core -IdentityKeys @('A<T>/M>&''')
            Note ($id -ceq 'b-core-f2e0eae2ff0cf3c2') ('json encoding survived: ' + $id)
        }
        'ShortIdCollisionIsUnassigned' {
            # Forced 64-bit prefix collision of CTests/M under suite antiphon, boundary core.
            # The two case keys were found by a distinguished-point search over the canonical digest.
            $left = '10788987499786589518'
            $right = '17490688019260441962'
            $idLeft = Get-RcBoundaryChunkId antiphon core @('CTests/M') -CaseKeys @($left)
            $idRight = Get-RcBoundaryChunkId antiphon core @('CTests/M') -CaseKeys @($right)
            Note ($idLeft -ceq $idRight) 'fixture is not a short-id collision'
            Note ($idLeft -ceq 'b-core-360a4ef28b10a96c') ('pinned collision prefix moved: ' + $idLeft)
            $one = New-RcBoundaryChunk -Identities @(@{ boundary = 'core'; suite = 'antiphon'; identityKey = 'ATests/A' })
            Note ($one.fullDigest -match '^[0-9a-f]{64}$' -and $one.chunkId -ceq ('b-core-' + $one.fullDigest.Substring(0, 16))) 'full digest dropped'
            if (-not (Get-Command Resolve-RcBoundaryChunks -ErrorAction SilentlyContinue)) {
                Note $false 'collision resolver absent'
            } else {
                $resolved = @(Resolve-RcBoundaryChunks -Memberships @(
                    @{ suite = 'antiphon'; boundary = 'core'; identities = @(@{ identityKey = 'CTests/M'; caseKey = $left }) },
                    @{ suite = 'antiphon'; boundary = 'core'; identities = @(@{ identityKey = 'CTests/M'; caseKey = $right }) }
                ))
                Note (@($resolved).Count -eq 2) 'collision dropped a chunk'
                foreach ($row in @($resolved)) {
                    Note ($row.boundary -ceq 'unassigned' -and $row.alwaysDirty -eq $true -and $row.collision -eq $true) 'short-id collision accepted'
                    Note ($row.chunkId -like 'b-unassigned-*') 'colliding short id kept'
                }
                $caseJoin = (@($resolved) | ForEach-Object { [string]@($_.caseKeys)[0] }) -join '|'
                Note ($caseJoin -ceq ($left + '|' + $right)) 'collision case keys dropped'
                Note ($resolved[0].chunkId -cne $resolved[1].chunkId) 'collision rebuilt as one id'
                Note ($resolved[0].fullDigest -cne $resolved[1].fullDigest) 'collision full digests collapsed'
                $clean = @(Resolve-RcBoundaryChunks -Memberships @(
                    @{ suite = 'antiphon'; boundary = 'core'; identities = @(@{ identityKey = 'ATests/A'; caseKey = '' }) },
                    @{ suite = 'antiphon'; boundary = 'core'; identities = @(@{ identityKey = 'ZTests/B'; caseKey = '' }) }
                ))
                Note (@($clean).Count -eq 2) 'distinct chunks dropped'
                foreach ($row in @($clean)) {
                    Note ($row.boundary -ceq 'core' -and $row.collision -eq $false -and $row.alwaysDirty -eq $false) 'distinct short ids refused'
                }
                Note (Refuses { Resolve-RcBoundaryChunks -Memberships @(
                    @{ suite = 'antiphon'; boundary = 'core'; identities = @(@{ identityKey = 'ATests/A'; caseKey = '' }) },
                    @{ suite = 'antiphon'; boundary = 'core'; identities = @(@{ identityKey = 'ATests/A'; caseKey = '' }) }
                ) }) 'identical membership accepted twice'
            }
        }
        default { throw ('unknown case ' + $Case) }
    }
}
Assert-C487 ($script:errors.Count -eq 0) ('RB ' + $Case + '.contract') ($script:errors -join '; ')
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
