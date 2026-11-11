#requires -Version 5.1
# CARD-1842 S6 witnesses. ASCII-only. No network, no git, no qualification launch.
param([string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('rd-' + [guid]::NewGuid().ToString('N'))
}
if (-not (Test-Path -LiteralPath $ResultsDirectory)) {
    New-Item -ItemType Directory -Path $ResultsDirectory | Out-Null
}
. (Join-Path $PSScriptRoot 'lib/rc-ledger.ps1')
$script:Stamp = Join-Path $PSScriptRoot 'rc-ledger-stamp-duration.ps1'

$script:Errors = New-Object System.Collections.Generic.List[string]
function Note([bool]$Ok, [string]$Detail) {
    if (-not $Ok) { $script:Errors.Add($Detail) }
}
function Assert-Contract {
    Assert-C487 ($script:Errors.Count -eq 0) ('RD ' + $Case + '.contract') ($script:Errors -join '; ')
}
function Write-Utf8([string]$Path, [string]$Text) {
    $encoding = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}
function New-Dir([string]$Prefix) {
    $path = Join-Path $ResultsDirectory ($Prefix + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}
$script:DigestA = ([string]'a') * 64
$script:DigestB = ([string]'b') * 64
$script:ChunkA = 'b-core-aaaaaaaaaaaaaaaa'
$script:ChunkB = 'b-core-bbbbbbbbbbbbbbbb'
function New-Identity([string]$Method, [string]$Digest) {
    return [ordered]@{
        suite = 'antiphon'; className = 'SampleTests'; method = $Method; boundary = 'core'
        inputDigest = $Digest; outcome = 'passed'; durationSeconds = $null; carriedFrom = $null
    }
}
function New-SampleDocument {
    $contract = [ordered]@{ policyHash = 'p'; catalogHash = 'c'; selectorHash = 's'; runtimeHash = 'r' }
    $chunks = @(
        [ordered]@{ chunkId = $script:ChunkA; boundary = 'core'; suite = 'antiphon'; identityKeys = @('SampleTests/Runs', 'SampleTests/Other'); inputDigest = $script:DigestA; outcome = 'passed'; durationSeconds = $null },
        [ordered]@{ chunkId = $script:ChunkB; boundary = 'core'; suite = 'antiphon'; identityKeys = @('SampleTests/Third'); inputDigest = $script:DigestB; outcome = 'passed'; durationSeconds = $null }
    )
    return New-RcLedger -Document ([ordered]@{
        platform = 'linux'; repository = 'antiphon'; candidateId = 'cand-1'; candidateSha = ('1' * 40)
        baselineSha = $null; baselineRef = $null; runId = 'run-1'; attempt = 1; contract = $contract
        forcedFull = @()
        identities = @((New-Identity 'Runs' $script:DigestA), (New-Identity 'Other' $script:DigestA), (New-Identity 'Third' $script:DigestB))
        chunks = $chunks
    })
}
function New-Fixture {
    $dir = New-Dir 'fixture'
    $ledger = Join-Path $dir 'ledger-in.json'
    Write-RcLedger -Path $ledger -Document (New-SampleDocument)
    $timingA = Join-Path $dir ('antiphon-' + $script:ChunkA + '.timing.json')
    Write-Utf8 $timingA ('{"suite":"antiphon","chunk":"' + $script:ChunkA + '","lastPhase":"complete","runWallSeconds":42.5}')
    # The second chunk's sidecar has no wall; its duration stays null.
    $timingB = Join-Path $dir ('antiphon-' + $script:ChunkB + '.timing.json')
    Write-Utf8 $timingB ('{"suite":"antiphon","chunk":"' + $script:ChunkB + '","lastPhase":"run"}')
    # An unrelated sidecar must not land on either chunk.
    $timingC = Join-Path $dir 'other-x.timing.json'
    Write-Utf8 $timingC ('{"suite":"other","chunk":"' + $script:ChunkB + '","lastPhase":"complete","runWallSeconds":999}')
    $trx = Join-Path $dir 'run.trx'
    $trxText = '<?xml version="1.0" encoding="utf-8"?>' +
        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">' +
        '<Results>' +
        '<UnitTestResult testId="1" testName="Runs" outcome="Passed" duration="00:00:01.2500000" />' +
        '<UnitTestResult testId="2" testName="Other(1)" outcome="Passed" duration="00:00:00.5000000" />' +
        '<UnitTestResult testId="3" testName="Other(2)" outcome="Passed" duration="00:00:00.2500000" />' +
        '<UnitTestResult testId="4" testName="Runs" outcome="Passed" duration="00:01:00.0000000" />' +
        '</Results>' +
        '<TestDefinitions>' +
        '<UnitTest id="1" name="Runs"><TestMethod className="SampleTests" name="Runs" /></UnitTest>' +
        '<UnitTest id="2" name="Other(1)"><TestMethod className="SampleTests" name="Other" /></UnitTest>' +
        '<UnitTest id="3" name="Other(2)"><TestMethod className="SampleTests" name="Other" /></UnitTest>' +
        '<UnitTest id="4" name="Runs"><TestMethod className="ElsewhereTests" name="Runs" /></UnitTest>' +
        '</TestDefinitions>' +
        '<ResultSummary outcome="Completed"><Counters total="4" executed="4" passed="4" failed="0" /></ResultSummary>' +
        '</TestRun>'
    Write-Utf8 $trx $trxText
    return [pscustomobject]@{
        Dir = $dir; Ledger = $ledger; Timing = @($timingA, $timingB, $timingC); Trx = @($trx)
        Output = (Join-Path $dir 'ledger-out.json')
    }
}
function Get-Sha([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($Path)))).Replace('-', '') }
    finally { $sha.Dispose() }
}
function Find-Item($Items, [string]$Name, [string]$Value) {
    foreach ($item in @(ConvertTo-RcList $Items)) {
        if ([string](Get-RcProp $item $Name) -ceq $Value) { return $item }
    }
    return $null
}
function Test-Seconds($Value, $Expected) {
    if ($null -eq $Expected) { return ($null -eq $Value) }
    if ($null -eq $Value) { return $false }
    return ([Math]::Abs([double]$Value - [double]$Expected) -lt 0.000001)
}
function Note-Durations($Document, [string]$Label) {
    $a = Find-Item $Document.chunks 'chunkId' $script:ChunkA
    $b = Find-Item $Document.chunks 'chunkId' $script:ChunkB
    Note (Test-Seconds (Get-RcProp $a 'durationSeconds') 42.5) ($Label + ' chunk A ' + (Get-RcProp $a 'durationSeconds'))
    Note (Test-Seconds (Get-RcProp $b 'durationSeconds') $null) ($Label + ' chunk B ' + (Get-RcProp $b 'durationSeconds'))
    Note (Test-Seconds (Get-RcProp (Find-Item $Document.identities 'method' 'Runs') 'durationSeconds') 1.25) ($Label + ' Runs')
    Note (Test-Seconds (Get-RcProp (Find-Item $Document.identities 'method' 'Other') 'durationSeconds') 0.75) ($Label + ' Other sums its cases')
    Note (Test-Seconds (Get-RcProp (Find-Item $Document.identities 'method' 'Third') 'durationSeconds') $null) ($Label + ' Third')
}
function Invoke-Stamp($Fixture, [hashtable]$Extra) {
    $stampArgs = @{ LedgerPath = $Fixture.Ledger; OutputPath = $Fixture.Output; TimingPath = $Fixture.Timing; TrxPath = $Fixture.Trx }
    if ($null -ne $Extra) { foreach ($key in @($Extra.Keys)) { $stampArgs[$key] = $Extra[$key] } }
    & $script:Stamp @stampArgs
}
function Get-Throw([scriptblock]$Block) {
    try { $null = & $Block; return '' } catch { return ('threw:' + [string]$_.Exception.Message) }
}
function Note-Rejected([string]$Label, [string]$Text, [string]$Token) {
    $path = Join-Path (New-Dir 'reject') ($Label + '.json')
    Write-Utf8 $path $Text
    $message = Get-Throw { $null = Read-RcLedger -Path $path }
    Note ($message.StartsWith('threw:ledger-invalid') -and $message.Contains($Token)) ($Label + ' => ' + $message)
}
function Note-Reads([string]$Label, [string]$Text) {
    $path = Join-Path (New-Dir 'read') ($Label + '.json')
    Write-Utf8 $path $Text
    $message = Get-Throw { $null = Read-RcLedger -Path $path }
    Note ($message -ceq '') ($Label + ' => ' + $message)
}

try {
    if (-not (Test-Path -LiteralPath $script:Stamp)) { throw 'rc-ledger-stamp-duration.ps1 is absent' }
    foreach ($name in @('Set-RcLedgerDurations', 'Set-RcLedgerHost')) {
        if (-not (Get-Command -Name $name -CommandType Function -ErrorAction SilentlyContinue)) { throw ($name + ' is absent') }
    }
    switch ($Case) {
        'V1DurationFromSidecarAndTrx' {
            $fixture = New-Fixture
            $inputSha = Get-Sha $fixture.Ledger
            Invoke-Stamp $fixture $null
            $text = [System.IO.File]::ReadAllText($fixture.Output)
            $read = Read-RcLedger -Path $fixture.Output
            Note ([string]$read.format -ceq 'antiphon-rc-ledger-v1') ('format ' + $read.format)
            Note-Durations $read 'stamped file'
            Note ((Get-Sha $fixture.Ledger) -ceq $inputSha) 'input ledger changed'
            # In memory: the filler sets durations and leaves the format and the host alone.
            $doc = Read-RcLedger -Path $fixture.Ledger
            $filled = Set-RcLedgerDurations -Document $doc -TimingPath $fixture.Timing -TrxPath $fixture.Trx
            Note ([string]$filled.format -ceq 'antiphon-rc-ledger-v1') ('filled format ' + $filled.format)
            Note-Durations $filled 'in memory'
            foreach ($chunk in @(ConvertTo-RcList $filled.chunks)) {
                Note (-not (Test-RcHasProp $chunk 'hostId') -and -not (Test-RcHasProp $chunk 'os')) 'filler set a host'
            }
            Note (Test-RcLedgerDocument -Document $filled).valid 'filled document invalid'
            Assert-Contract
        }
        'V1RejectsHostBeforeWrite' {
            # A v1 chunk that carries a host is refused before any byte is written.
            $doc = New-SampleDocument
            $doc.chunks[0] | Add-Member -NotePropertyName hostId -NotePropertyValue 'server2'
            $path = Join-Path (New-Dir 'write') 'v1-host.json'
            $message = Get-Throw { Write-RcLedger -Path $path -Document $doc }
            Note ($message.StartsWith('threw:ledger-invalid')) ('v1 host write => ' + $message)
            Note (-not [System.IO.File]::Exists($path)) 'v1 host file was written'
            $osDoc = New-SampleDocument
            $osDoc.chunks[1] | Add-Member -NotePropertyName os -NotePropertyValue 'linux'
            $osPath = Join-Path (New-Dir 'write') 'v1-os.json'
            $message = Get-Throw { Write-RcLedger -Path $osPath -Document $osDoc }
            Note ($message.StartsWith('threw:ledger-invalid')) ('v1 os write => ' + $message)
            Note (-not [System.IO.File]::Exists($osPath)) 'v1 os file was written'
            # The host setter refuses a v1 document and leaves it untouched.
            $plain = New-SampleDocument
            $message = Get-Throw { Set-RcLedgerHost -Document $plain -HostId 'server2' -Os 'linux' }
            Note ($message -ceq 'threw:ledger-schema') ('v1 host setter => ' + $message)
            Note (-not (Test-RcHasProp $plain.chunks[0] 'hostId')) 'v1 host setter changed the document'
            # A host argument without the switch is refused before the output exists.
            $fixture = New-Fixture
            $message = Get-Throw { Invoke-Stamp $fixture @{ HostId = 'server2'; Os = 'linux' } }
            Note ($message.StartsWith('threw:')) ('host without -IncludeHost => ' + $message)
            Note (-not [System.IO.File]::Exists($fixture.Output)) 'host without -IncludeHost wrote the output'
            # A v1 file with a chunk host stays invalid on read.
            $json = ConvertTo-RcLedgerJson -Document (New-SampleDocument)
            Note-Rejected 'v1-read-host' ($json.Replace('{"chunkId":', '{"hostId":"server2","chunkId":')) 'hostId'
            Note-Rejected 'v1-read-os' ($json.Replace('{"chunkId":', '{"os":"linux","chunkId":')) 'os'
            Assert-Contract
        }
        'V2AcceptsExplicitHost' {
            $fixture = New-Fixture
            Invoke-Stamp $fixture @{ IncludeHost = $true; HostId = 'server2'; Os = 'linux' }
            $read = Read-RcLedger -Path $fixture.Output
            Note ([string]$read.format -ceq 'antiphon-rc-ledger-v2') ('format ' + $read.format)
            Note-Durations $read 'v2 file'
            foreach ($chunk in @(ConvertTo-RcList $read.chunks)) {
                Note ([string](Get-RcProp $chunk 'hostId') -ceq 'server2') ('chunk host ' + (Get-RcProp $chunk 'hostId'))
                Note ([string](Get-RcProp $chunk 'os') -ceq 'linux') ('chunk os ' + (Get-RcProp $chunk 'os'))
            }
            Note (Test-RcLedgerDocument -Document $read).valid 'v2 document invalid'
            # The host is never invented: the switch without -HostId or with a supervisor label is refused.
            $missing = New-Fixture
            $message = Get-Throw { Invoke-Stamp $missing @{ IncludeHost = $true; Os = 'linux' } }
            Note ($message.StartsWith('threw:')) ('missing host => ' + $message)
            Note (-not [System.IO.File]::Exists($missing.Output)) 'missing host wrote the output'
            $unix = New-Fixture
            $message = Get-Throw { Invoke-Stamp $unix @{ IncludeHost = $true; HostId = 'server2'; Os = 'Unix' } }
            Note ($message.StartsWith('threw:')) ('Unix os => ' + $message)
            Note (-not [System.IO.File]::Exists($unix.Output)) 'Unix os wrote the output'
            # Set-RcLedgerHost on a v2 document sets exactly the passed values.
            $doc = New-SampleDocument
            $doc.format = 'antiphon-rc-ledger-v2'
            $hosted = Set-RcLedgerHost -Document $doc -HostId 'desk-1' -Os 'windows'
            Note ([string](Get-RcProp $hosted.chunks[0] 'hostId') -ceq 'desk-1' -and [string](Get-RcProp $hosted.chunks[1] 'os') -ceq 'windows') 'v2 setter values'
            # v2 host fields are optional and nullable; a wrong type is invalid.
            $v2 = (ConvertTo-RcLedgerJson -Document (New-SampleDocument)).Replace('antiphon-rc-ledger-v1', 'antiphon-rc-ledger-v2')
            Note-Reads 'v2-no-host' $v2
            Note-Reads 'v2-null-host' ($v2.Replace('{"chunkId":', '{"hostId":null,"os":null,"chunkId":'))
            Note-Rejected 'v2-number-host' ($v2.Replace('{"chunkId":', '{"hostId":5,"chunkId":')) 'type'
            Note-Rejected 'v2-identity-host' ($v2.Replace('{"suite":"antiphon","className"', '{"hostId":"x","suite":"antiphon","className"')) 'hostId'
            Assert-Contract
        }
        'V2OmitsHostWhenSwitchOff' {
            $fixture = New-Fixture
            Invoke-Stamp $fixture $null
            $text = [System.IO.File]::ReadAllText($fixture.Output)
            Note (-not $text.Contains('hostId')) 'switch-off output names hostId'
            Note (-not $text.Contains('"os"')) 'switch-off output names os'
            Note (-not $text.Contains('antiphon-rc-ledger-v2')) 'switch-off output is v2'
            Note ($text.Contains('"durationSeconds":42.5')) 'switch-off output lost the chunk wall'
            # The stamp refuses an existing output path, the same way Write-RcLedger does.
            $again = Get-Throw { Invoke-Stamp $fixture @{ IncludeHost = $true; HostId = 'server2'; Os = 'linux' } }
            Note ($again -ceq 'threw:ledger-exists') ('existing output => ' + $again)
            Note ([System.IO.File]::ReadAllText($fixture.Output) -ceq $text) 'existing output changed'
            Assert-Contract
        }
        default { throw ('unknown case ' + $Case) }
    }
} catch {
    Assert-C487 $false ('RD ' + $Case + '.contract') $_.Exception.Message
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 1
