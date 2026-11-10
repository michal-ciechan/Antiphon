#requires -Version 5.1
# CARD-1836 S1 witnesses. ASCII-only. No network, no git, no qualification launch.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('rl-' + [guid]::NewGuid().ToString('N'))
}
if (-not (Test-Path -LiteralPath $ResultsDirectory)) {
    New-Item -ItemType Directory -Path $ResultsDirectory | Out-Null
}
$lib = Join-Path $PSScriptRoot 'lib/rc-ledger.ps1'
if (-not (Test-Path -LiteralPath $lib)) { throw 'rc-ledger.ps1 is absent' }
. $lib
foreach ($name in @('Read-RcLedger', 'Write-RcLedger', 'Test-RcLedgerDocument', 'New-RcLedger', 'Compare-RcInputDigest', 'Get-RcIdentityCarryDecision', 'Get-RcChunkCarryDecision')) {
    if (-not (Get-Command -Name $name -CommandType Function -ErrorAction SilentlyContinue)) { throw ($name + ' is absent') }
}

$script:RlErrors = New-Object System.Collections.Generic.List[string]
function Note([bool]$Ok, [string]$Detail) {
    if (-not $Ok) { $script:RlErrors.Add($Detail) }
}
function Assert-Contract {
    Assert-C487 ($script:RlErrors.Count -eq 0) ('RL ' + $Case + '.contract') ($script:RlErrors -join '; ')
}
function New-Hex([char]$Ch) { return ([string]$Ch) * 64 }
function New-Id([string]$Outcome, [string]$Digest, [string]$Boundary, [string]$Method) {
    return [ordered]@{
        suite = 'antiphon'
        className = 'SampleTests'
        method = $Method
        boundary = $Boundary
        inputDigest = $Digest
        outcome = $Outcome
        durationSeconds = 1.25
        carriedFrom = $null
    }
}
function New-Ledger([string]$Platform, $Identity, $Chunk) {
    return [ordered]@{
        format = 'antiphon-rc-ledger-v1'
        platform = $Platform
        repository = 'antiphon'
        candidateId = 'cand-1'
        candidateSha = ('1' * 40)
        baselineSha = $null
        baselineRef = $null
        runId = 'run-1'
        attempt = 1
        identities = @($Identity)
        chunks = @($Chunk)
    }
}
function New-Chunk([string]$Digest, [string]$Boundary, [string]$Outcome, [string]$Method) {
    return [ordered]@{
        chunkId = 'b-core-aaaaaaaaaaaaaaaa'
        boundary = $Boundary
        suite = 'antiphon'
        identityKeys = @('SampleTests/' + $Method)
        inputDigest = $Digest
        outcome = $Outcome
        durationSeconds = 2.5
    }
}
function Decide($Platform, $Boundary, $NewDigest, $Ledger, $Identity, $Chunk, $History, [bool]$FixProven, $Roster) {
    return Get-RcIdentityCarryDecision -Platform $Platform -Boundary $Boundary -NewDigest $NewDigest -SourceLedger $Ledger -SourceIdentity $Identity -SourceChunk $Chunk -History $History -FixProven:$FixProven -ExpectedSkipRoster $Roster -ForcedFull @()
}

$same = New-Hex 'a'
$other = New-Hex 'b'

switch ($Case) {
    'MissingDigestIsDirty' {
        $id = New-Id 'passed' $same 'core' 'Runs'
        $chunk = New-Chunk $same 'core' 'passed' 'Runs'
        $ledger = New-Ledger 'windows' $id $chunk
        $unknownId = New-Id 'passed' 'unknown' 'core' 'Runs'
        $missing = Decide 'windows' 'core' $same $ledger $unknownId $chunk $null $false @()
        Note ($missing.decision -ceq 'dirty' -and $missing.reason -ceq 'missing-digest' -and $null -eq $missing.carriedFrom) ('unknown digest carried: ' + $missing.decision + '/' + $missing.reason)
        $emptyId = New-Id 'passed' '' 'core' 'Runs'
        $empty = Decide 'windows' 'core' $same $ledger $emptyId $chunk $null $false @()
        Note ($empty.decision -ceq 'dirty' -and $empty.reason -ceq 'missing-digest') ('empty digest ' + $empty.reason)
        $shortId = New-Id 'passed' ('ab') 'core' 'Runs'
        $short = Decide 'windows' 'core' $same $ledger $shortId $chunk $null $false @()
        Note ($short.decision -ceq 'dirty' -and $short.reason -ceq 'missing-digest') ('short digest ' + $short.reason)
        $upper = New-Hex 'A'
        $upperId = New-Id 'passed' $upper 'core' 'Runs'
        $upperDecision = Decide 'windows' 'core' $same $ledger $upperId $chunk $null $false @()
        Note ($upperDecision.decision -ceq 'dirty' -and $upperDecision.reason -ceq 'missing-digest') ('upper digest ' + $upperDecision.reason)
        $newUnknown = Decide 'windows' 'core' 'unknown' $ledger $id $chunk $null $false @()
        Note ($newUnknown.decision -ceq 'dirty' -and $newUnknown.reason -ceq 'missing-digest') ('new unknown ' + $newUnknown.reason)
        $changed = Decide 'windows' 'core' $other $ledger $id $chunk $null $false @()
        Note ($changed.decision -ceq 'dirty' -and $changed.reason -ceq 'digest-mismatch' -and $null -eq $changed.carriedFrom) ('changed digest ' + $changed.decision + '/' + $changed.reason)
        $bad = Compare-RcInputDigest -Left 'unknown' -Right $same
        $eq = Compare-RcInputDigest -Left $same -Right $same
        $diff = Compare-RcInputDigest -Left $same -Right $other
        Note ($bad.relation -ceq 'invalid' -and -not $bad.equal) 'unknown compare'
        Note ($eq.relation -ceq 'same' -and $eq.equal) 'same compare'
        Note ($diff.relation -ceq 'different' -and -not $diff.equal) 'different compare'
        $absent = Decide 'windows' 'core' $same $null $null $null $null $false @()
        Note ($absent.decision -ceq 'dirty' -and $absent.reason -ceq 'missing-row') ('missing row ' + $absent.reason)
        Assert-Contract
    }
    'UnexpectedSkipNeverCarries' {
        $id = New-Id 'passed' $same 'core' 'Runs'
        $chunk = New-Chunk $same 'core' 'passed' 'Runs'
        $ledger = New-Ledger 'windows' $id $chunk
        $skipId = New-Id 'unexpected-skip' $same 'core' 'Runs'
        $skip = Decide 'windows' 'core' $same $ledger $skipId $chunk $null $false @('SampleTests/Runs')
        Note ($skip.decision -ceq 'dirty' -and $skip.reason -ceq 'unexpected-skip' -and $null -eq $skip.carriedFrom) ('unexpected-skip ' + $skip.decision + '/' + $skip.reason)
        $rawId = New-Id 'NotExecuted' $same 'core' 'Runs'
        $raw = Decide 'windows' 'core' $same $ledger $rawId $chunk $null $false @()
        Note ($raw.decision -ceq 'dirty' -and $raw.reason -ceq 'unexpected-skip') ('NotExecuted off roster ' + $raw.reason)
        $named = Decide 'windows' 'core' $same $ledger $rawId $chunk $null $false @('SampleTests/Runs')
        Note ($named.decision -ceq 'carry' -and $named.reason -ceq 'carry' -and $named.outcome -ceq 'expected-skip') ('NotExecuted on roster ' + $named.decision + '/' + $named.outcome)
        $interruptedId = New-Id 'interrupted' $same 'core' 'Runs'
        $interrupted = Decide 'windows' 'core' $same $ledger $interruptedId $chunk $null $false @()
        Note ($interrupted.decision -ceq 'dirty' -and $interrupted.reason -ceq 'interrupted') ('interrupted ' + $interrupted.reason)
        $notRunId = New-Id 'not-run' $same 'core' 'Runs'
        $notRun = Decide 'windows' 'core' $same $ledger $notRunId $chunk $null $false @()
        Note ($notRun.decision -ceq 'dirty' -and $notRun.reason -ceq 'not-run') ('not-run ' + $notRun.reason)
        $failedId = New-Id 'failed' $same 'core' 'Runs'
        $failed = Decide 'windows' 'core' $same $ledger $failedId $chunk $null $false @()
        Note ($failed.decision -ceq 'dirty' -and $failed.reason -ceq 'failed') ('failed ' + $failed.reason)
        $openChunk = New-Chunk $same 'core' 'interrupted' 'Runs'
        $open = Decide 'windows' 'core' $same $ledger $id $openChunk $null $false @()
        Note ($open.decision -ceq 'dirty' -and $open.reason -ceq 'chunk-interrupted') ('passed inside interrupted chunk ' + $open.reason)
        $openChunkDecision = Get-RcChunkCarryDecision -Platform 'windows' -NewDigest $same -SourceLedger $ledger -SourceChunk $openChunk -IdentityDecisions @($named) -ForcedFull @()
        Note ($openChunkDecision.decision -ceq 'dirty' -and $openChunkDecision.reason -ceq 'chunk-interrupted') ('chunk interrupted ' + $openChunkDecision.reason)
        Assert-Contract
    }
    'CrossPlatformNeverCarries' {
        $id = New-Id 'passed' $same 'core' 'Runs'
        $chunk = New-Chunk $same 'core' 'passed' 'Runs'
        $linux = New-Ledger 'linux' $id $chunk
        $toWindows = Decide 'windows' 'core' $same $linux $id $chunk $null $false @()
        Note ($toWindows.decision -ceq 'dirty' -and $toWindows.reason -ceq 'cross-platform' -and $null -eq $toWindows.carriedFrom) ('linux to windows ' + $toWindows.decision + '/' + $toWindows.reason)
        $windows = New-Ledger 'windows' $id $chunk
        $toLinux = Decide 'linux' 'core' $same $windows $id $chunk $null $false @()
        Note ($toLinux.decision -ceq 'dirty' -and $toLinux.reason -ceq 'cross-platform') ('windows to linux ' + $toLinux.reason)
        $samePlatform = Decide 'linux' 'core' $same $linux $id $chunk $null $false @()
        Note ($samePlatform.decision -ceq 'carry' -and $samePlatform.carriedFrom.runId -ceq 'run-1' -and $samePlatform.carriedFrom.candidateId -ceq 'cand-1') ('same platform ' + $samePlatform.decision)
        $chunkCross = Get-RcChunkCarryDecision -Platform 'windows' -NewDigest $same -SourceLedger $linux -SourceChunk $chunk -IdentityDecisions @($samePlatform) -ForcedFull @()
        Note ($chunkCross.decision -ceq 'dirty' -and $chunkCross.reason -ceq 'cross-platform') ('chunk cross-platform ' + $chunkCross.reason)
        Assert-Contract
    }
    'SameDigestCarriesProvenance' {
        $id = New-Id 'passed' $same 'core' 'Runs'
        $chunk = New-Chunk $same 'core' 'passed' 'Runs'
        $ledger = New-Ledger 'windows' $id $chunk
        $carry = Decide 'windows' 'core' $same $ledger $id $chunk $null $false @()
        Note ($carry.decision -ceq 'carry' -and $carry.reason -ceq 'carry') ('pass ' + $carry.decision + '/' + $carry.reason)
        Note ($null -ne $carry.carriedFrom -and $carry.carriedFrom.runId -ceq 'run-1' -and $carry.carriedFrom.candidateId -ceq 'cand-1' -and $carry.carriedFrom.candidateSha -ceq ('1' * 40) -and $carry.carriedFrom.chunkId -ceq 'b-core-aaaaaaaaaaaaaaaa' -and $carry.carriedFrom.inputDigest -ceq $same) 'carriedFrom fields'
        $fail = New-Id 'failed' $same 'core' 'Runs'
        $pass = New-Id 'passed' $same 'core' 'Runs'
        $historyForward = @($fail, $pass)
        $historyReverse = @($pass, $fail)
        $flake = Decide 'windows' 'core' $same $ledger $pass $chunk $historyForward $false @()
        $flakeReverse = Decide 'windows' 'core' $same $ledger $pass $chunk $historyReverse $false @()
        Note ($flake.decision -ceq 'dirty' -and $flake.reason -ceq 'flake' -and $null -eq $flake.carriedFrom) ('flake ' + $flake.decision + '/' + $flake.reason)
        Note ($flakeReverse.decision -ceq $flake.decision -and $flakeReverse.reason -ceq $flake.reason) 'flake order'
        $proven = Decide 'windows' 'core' $same $ledger $pass $chunk $historyForward $true @()
        Note ($proven.decision -ceq 'carry' -and $proven.carriedFrom.runId -ceq 'run-1') ('proven fix ' + $proven.decision)
        $unassigned = Decide 'windows' 'unassigned' $same $ledger $id $chunk $null $false @()
        Note ($unassigned.decision -ceq 'dirty' -and $unassigned.reason -ceq 'unknown-boundary') ('unassigned ' + $unassigned.reason)
        $unknownBoundary = Decide 'windows' 'unknown' $same $ledger $id $chunk $null $false @()
        Note ($unknownBoundary.decision -ceq 'dirty' -and $unknownBoundary.reason -ceq 'unknown-boundary') ('unknown boundary ' + $unknownBoundary.reason)
        $chunkCarry = Get-RcChunkCarryDecision -Platform 'windows' -NewDigest $same -SourceLedger $ledger -SourceChunk $chunk -IdentityDecisions @($carry) -ForcedFull @()
        Note ($chunkCarry.decision -ceq 'carry' -and $chunkCarry.reason -ceq 'carry') ('chunk carry ' + $chunkCarry.decision)
        $otherDecision = [pscustomobject]@{ decision = 'dirty'; reason = 'flake'; identityKey = 'SampleTests/Other'; carriedFrom = $null }
        $forward = Get-RcChunkCarryDecision -Platform 'windows' -NewDigest $same -SourceLedger $ledger -SourceChunk $chunk -IdentityDecisions @($carry, $otherDecision) -ForcedFull @()
        $reverse = Get-RcChunkCarryDecision -Platform 'windows' -NewDigest $same -SourceLedger $ledger -SourceChunk $chunk -IdentityDecisions @($otherDecision, $carry) -ForcedFull @()
        Note ($forward.decision -ceq 'dirty' -and $forward.reason -ceq $reverse.reason -and $forward.reason -ceq 'identity:SampleTests/Other:flake') ('chunk order ' + $forward.reason)
        $second = New-Id 'passed' $same 'core' 'Other'
        $both = New-Chunk $same 'core' 'passed' 'Runs'
        $both.identityKeys = @('SampleTests/Runs', 'SampleTests/Other')
        $contract = [ordered]@{ policyHash = 'p'; catalogHash = 'c'; selectorHash = 's'; runtimeHash = 'r' }
        $docA = New-RcLedger -Document ([ordered]@{
            platform = 'windows'; repository = 'antiphon'; candidateId = 'cand-1'; candidateSha = ('1' * 40)
            baselineSha = $null; baselineRef = $null; runId = 'run-1'; attempt = 1; contract = $contract
            forcedFull = @(); identities = @($second, $id); chunks = @($both)
        })
        $docB = New-RcLedger -Document ([ordered]@{
            platform = 'windows'; repository = 'antiphon'; candidateId = 'cand-1'; candidateSha = ('1' * 40)
            baselineSha = $null; baselineRef = $null; runId = 'run-1'; attempt = 1; contract = $contract
            forcedFull = @(); identities = @($id, $second); chunks = @($both)
        })
        $jsonA = ConvertTo-RcLedgerJson -Document $docA
        $forced = New-RcLedger -Document ([ordered]@{
            platform = 'windows'; repository = 'antiphon'; candidateId = 'cand-1'; candidateSha = ('1' * 40)
            baselineSha = $null; baselineRef = $null; runId = 'run-1'; attempt = 1; contract = $contract
            forcedFull = @('later', 'earlier'); identities = @($id); chunks = @((New-Chunk $same 'core' 'passed' 'Runs'))
        })
        $jsonForced = ConvertTo-RcLedgerJson -Document $forced
        Note ($jsonA -ceq (ConvertTo-RcLedgerJson -Document $docB)) 'writer order'
        Note ($jsonForced.Contains('"forcedFull":["earlier","later"]')) 'forcedFull order'
        Note ($jsonA.Contains('"format":"antiphon-rc-ledger-v1"')) 'format token'
        $pathA = Join-Path $ResultsDirectory 'a.json'
        $pathB = Join-Path $ResultsDirectory 'b.json'
        Write-RcLedger -Path $pathA -Document $docA
        Write-RcLedger -Path $pathB -Document $docB
        $read = Read-RcLedger -Path $pathA
        Note ([string]$read.format -ceq 'antiphon-rc-ledger-v1') 'read format'
        $check = Test-RcLedgerDocument -Document $read
        Note ($check.valid) ('read validate ' + ($check.errors -join ','))
        $exists = $false
        try { Write-RcLedger -Path $pathA -Document $docA } catch { if ($_.Exception.Message -ceq 'ledger-exists') { $exists = $true } else { throw } }
        Note $exists 'second write refused'
        Assert-Contract
    }
    default { throw ('unknown case ' + $Case) }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
