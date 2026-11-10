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

function New-SampleJson {
    param($Identity, $Chunk)
    if ($null -eq $Identity) { $Identity = New-Id 'passed' $same 'core' 'Runs' }
    if ($null -eq $Chunk) { $Chunk = New-Chunk $same 'core' 'passed' 'Runs' }
    $sampleContract = [ordered]@{ policyHash = 'p'; catalogHash = 'c'; selectorHash = 's'; runtimeHash = 'r' }
    $sample = New-RcLedger -Document ([ordered]@{
        platform = 'windows'; repository = 'antiphon'; candidateId = 'cand-1'; candidateSha = ('1' * 40)
        baselineSha = $null; baselineRef = $null; runId = 'run-1'; attempt = 1; contract = $sampleContract
        forcedFull = @(); identities = @($Identity); chunks = @($Chunk)
    })
    return (ConvertTo-RcLedgerJson -Document $sample)
}
function Update-Once {
    param([string]$Text, [string]$Old, [string]$New)
    $at = $Text.IndexOf($Old)
    if ($at -lt 0) { throw ('fixture missing ' + $Old) }
    return $Text.Remove($at, $Old.Length).Insert($at, $New)
}
function Write-LedgerFile {
    param([string]$Name, [string]$Text, [bool]$Bom = $false, [bool]$Crlf = $false)
    if ($Crlf) { $Text = $Text.Replace(',"', ",`r`n`"") }
    $path = Join-Path $ResultsDirectory ($Name + '.json')
    $encoding = New-Object System.Text.UTF8Encoding $Bom
    [System.IO.File]::WriteAllText($path, $Text, $encoding)
    return $path
}
function Assert-Rejected {
    param([string]$Label, [string]$Text, [bool]$Bom = $false, [bool]$Crlf = $false, [string]$Token = 'ledger-invalid')
    $path = Write-LedgerFile $Label $Text $Bom $Crlf
    $threw = $false
    $message = ''
    try {
        $doc = Read-RcLedger -Path $path
        $message = 'accepted'
        if ($null -ne $doc) { $message = 'accepted format=' + [string](Get-RcProp $doc 'format') }
    } catch {
        $threw = $true
        $message = [string]$_.Exception.Message
    }
    Note ($threw -and $message.StartsWith('ledger-invalid') -and $message.Contains($Token)) ($Label + ' => ' + $message)
}
function Assert-CarriesFile {
    param([string]$Label, [string]$Path)
    $doc = Read-RcLedger -Path $Path
    $id = $doc.identities
    if ($id -is [System.Array]) { $id = $id[0] }
    $chunk = $doc.chunks
    if ($chunk -is [System.Array]) { $chunk = $chunk[0] }
    $decision = Get-RcIdentityCarryDecision -Platform 'windows' -Boundary 'core' -NewDigest ([string](Get-RcProp $id 'inputDigest')) -SourceLedger $doc -SourceIdentity $id -SourceChunk $chunk -History $null -FixProven:$false -ExpectedSkipRoster @() -ForcedFull @()
    Note ($decision.decision -ceq 'carry' -and $decision.reason -ceq 'carry' -and [string](Get-RcProp $id 'outcome') -ceq 'passed') ($Label + ' ' + $decision.decision + '/' + $decision.reason + '/' + (Get-RcProp $id 'outcome'))
}
function New-WideJson {
    param([int]$Count)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('{"format":"antiphon-rc-ledger-v1","platform":"windows","repository":"antiphon","candidateId":"cand-1","candidateSha":"')
    [void]$sb.Append(('1' * 40))
    [void]$sb.Append('","baselineSha":null,"baselineRef":null,"runId":"run-1","attempt":1,"contract":{"policyHash":"p","catalogHash":"c","selectorHash":"s","runtimeHash":"r"},"forcedFull":[],"identities":[')
    $keys = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $Count; $i++) {
        if ($i -gt 0) { [void]$sb.Append(','); [void]$keys.Append(',') }
        $method = 'M' + $i.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        [void]$keys.Append('"SampleTests/')
        [void]$keys.Append($method)
        [void]$keys.Append('"')
        [void]$sb.Append('{"suite":"antiphon","className":"SampleTests","method":"')
        [void]$sb.Append($method)
        [void]$sb.Append('","boundary":"core","inputDigest":"')
        [void]$sb.Append($same)
        [void]$sb.Append('","outcome":"passed","durationSeconds":1,"carriedFrom":null}')
    }
    [void]$sb.Append('],"chunks":[{"chunkId":"b-core-aaaaaaaaaaaaaaaa","boundary":"core","suite":"antiphon","identityKeys":[')
    [void]$sb.Append($keys.ToString())
    [void]$sb.Append('],"inputDigest":"')
    [void]$sb.Append($same)
    [void]$sb.Append('","outcome":"passed","durationSeconds":1}]}')
    return $sb.ToString()
}

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
    'DuplicateOutcomeNeverCarries' {
        $json = New-SampleJson
        $dup = Update-Once $json '"outcome":"passed"' '"outcome":"failed","outcome":"passed"'
        Assert-Rejected 'duplicate-outcome' $dup
        Assert-Contract
    }
    'NestedDuplicateIsInvalid' {
        $json = New-SampleJson
        $contractDup = Update-Once $json '"policyHash":"p"' '"policyHash":"p","policyHash":"q"'
        Assert-Rejected 'duplicate-contract' $contractDup
        $chunkDup = Update-Once $json '"chunkId":"b-core-aaaaaaaaaaaaaaaa"' '"chunkId":"b-core-aaaaaaaaaaaaaaaa","chunkId":"b-core-bbbbbbbbbbbbbbbb"'
        Assert-Rejected 'duplicate-chunk' $chunkDup
        $carried = New-Id 'passed' $same 'core' 'Runs'
        $carried.carriedFrom = [ordered]@{
            candidateId = 'cand-0'; runId = 'run-0'; candidateSha = ('2' * 40)
            chunkId = 'b-core-aaaaaaaaaaaaaaaa'; inputDigest = $same
        }
        $carriedJson = New-SampleJson $carried
        $carriedDup = Update-Once $carriedJson '"candidateId":"cand-0"' '"candidateId":"cand-0","candidateId":"cand-9"'
        Assert-Rejected 'duplicate-carried' $carriedDup
        Assert-Contract
    }
    'CaseDuplicateIsInvalid' {
        $json = New-SampleJson
        $dup = Update-Once $json '"outcome":"passed"' '"outcome":"failed","Outcome":"passed"'
        Assert-Rejected 'case-outcome' $dup $false $false 'duplicate-property'
        $contractDup = Update-Once $json '"policyHash":"p"' '"policyHash":"p","PolicyHash":"q"'
        Assert-Rejected 'case-contract' $contractDup $false $false 'duplicate-property'
        Assert-Contract
    }
    'UnknownPropertyIsInvalid' {
        $json = New-SampleJson
        Assert-Rejected 'unknown-top' (Update-Once $json '{"format":' '{"doNotCarry":true,"format":')
        Assert-Rejected 'unknown-contract' (Update-Once $json '"policyHash":"p"' '"policyHash":"p","extra":true')
        Assert-Rejected 'unknown-identity' (Update-Once $json '{"suite":"antiphon"' '{"extra":1,"suite":"antiphon"')
        Assert-Rejected 'unknown-chunk' (Update-Once $json '{"chunkId":' '{"extra":true,"chunkId":')
        $carried = New-Id 'passed' $same 'core' 'Runs'
        $carried.carriedFrom = [ordered]@{
            candidateId = 'cand-0'; runId = 'run-0'; candidateSha = ('2' * 40)
            chunkId = 'b-core-aaaaaaaaaaaaaaaa'; inputDigest = $same
        }
        $carriedJson = New-SampleJson $carried
        Assert-Rejected 'unknown-carried' (Update-Once $carriedJson '"carriedFrom":{"candidateId":' '"carriedFrom":{"extra":true,"candidateId":')
        Assert-Contract
    }
    'WrongTypeIsInvalid' {
        $json = New-SampleJson
        Assert-Rejected 'number-repository' ($json.Replace('"repository":"antiphon"', '"repository":1'))
        Assert-Rejected 'string-attempt' ($json.Replace('"attempt":1', '"attempt":"1"'))
        $contractJson = '"contract":{"policyHash":"p","catalogHash":"c","selectorHash":"s","runtimeHash":"r"}'
        Assert-Rejected 'bool-contract' ($json.Replace($contractJson, '"contract":true'))
        Assert-Rejected 'array-contract' ($json.Replace($contractJson, '"contract":[]'))
        $identityStart = $json.IndexOf('"identities":[')
        $identityEnd = $json.IndexOf('],"chunks":')
        if ($identityStart -lt 0 -or $identityEnd -lt $identityStart) { throw 'fixture identities missing' }
        $boolIdentity = $json.Remove($identityStart, ($identityEnd - $identityStart) + 1).Insert($identityStart, '"identities":[true]')
        Assert-Rejected 'bool-identity' $boolIdentity
        Assert-Contract
    }
    'MissingFieldIsInvalid' {
        $json = New-SampleJson
        Assert-Rejected 'missing-format' ($json.Replace('"format":"antiphon-rc-ledger-v1",', ''))
        Assert-Rejected 'missing-forced' ($json.Replace('"forcedFull":[],', ''))
        Assert-Rejected 'missing-duration' (Update-Once $json '"durationSeconds":1.25,' '')
        Assert-Rejected 'missing-outcome' (Update-Once $json '"outcome":"passed",' '')
        Assert-Rejected 'truncated' ($json.Substring(0, 40))
        Assert-Rejected 'newer-format' ($json.Replace('antiphon-rc-ledger-v1', 'antiphon-rc-ledger-v2'))
        Assert-Contract
    }
    'BomAndCrlfStillRead' {
        $json = New-SampleJson
        $bomPath = Write-LedgerFile 'bom' $json $true $false
        $bomBytes = [System.IO.File]::ReadAllBytes($bomPath)
        Note ($bomBytes.Length -ge 3 -and $bomBytes[0] -eq 0xEF -and $bomBytes[1] -eq 0xBB -and $bomBytes[2] -eq 0xBF) 'bom marker'
        Assert-CarriesFile 'bom-carry' $bomPath
        $crlfPath = Write-LedgerFile 'crlf' $json $false $true
        $crlfBytes = [System.IO.File]::ReadAllBytes($crlfPath)
        $crlfText = [System.Text.Encoding]::UTF8.GetString($crlfBytes)
        Note ($crlfText.Contains("`r`n")) 'crlf marker'
        Assert-CarriesFile 'crlf-carry' $crlfPath
        $bothPath = Write-LedgerFile 'both' $json $true $true
        Assert-CarriesFile 'bom-crlf-carry' $bothPath
        $dup = Update-Once $json '"outcome":"passed"' '"outcome":"failed","outcome":"passed"'
        Assert-Rejected 'crlf-duplicate' $dup $false $true
        Assert-Rejected 'bom-duplicate' $dup $true $false
        Assert-Contract
    }
    'LargeLedgerStaysLinear' {
        $count = 2500
        $wide = New-WideJson $count
        $validPath = Write-LedgerFile 'wide-valid' $wide
        $validWatch = [System.Diagnostics.Stopwatch]::StartNew()
        Assert-CarriesFile 'wide-carry' $validPath
        $validWatch.Stop()
        Write-Host ('wide-valid-ms ' + $validWatch.ElapsedMilliseconds)
        # 14198 ms on 2026-11-10 for 2500 identities, including validation. Bound is about 2x.
        Note ($validWatch.ElapsedMilliseconds -lt 30000) ('wide-valid-ms ' + $validWatch.ElapsedMilliseconds)
        $marker = '"outcome":"passed","durationSeconds":1,"carriedFrom":null'
        $at = $wide.LastIndexOf($marker)
        if ($at -lt 0) { throw 'wide marker missing' }
        $bad = $wide.Remove($at, $marker.Length).Insert($at, '"outcome":"failed","outcome":"passed","durationSeconds":1,"carriedFrom":null')
        $badPath = Write-LedgerFile 'wide-dup' $bad
        $badWatch = [System.Diagnostics.Stopwatch]::StartNew()
        Assert-Rejected 'wide-duplicate' $bad
        $badWatch.Stop()
        Write-Host ('wide-dup-ms ' + $badWatch.ElapsedMilliseconds)
        Note ($badWatch.ElapsedMilliseconds -lt 8000) ('wide-dup-ms ' + $badWatch.ElapsedMilliseconds)
        Note ($wide.Length -gt 400000) ('wide-bytes ' + $wide.Length)
        Assert-Contract
    }
    'WellFormedRowStillCarries' {
        $path = Write-LedgerFile 'valid' (New-SampleJson)
        Assert-CarriesFile 'valid-carry' $path
        $read = Read-RcLedger -Path $path
        $check = Test-RcLedgerDocument -Document $read
        Note ($check.valid) ('valid document ' + ($check.errors -join ','))
        Assert-Contract
    }
    default { throw ('unknown case ' + $Case) }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 0
