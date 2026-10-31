# CARD-1545 completed-row evidence scan. ASCII. Windows PowerShell 5.1 compatible.
# Markers are required in the exact TRX test result and in the receipt and public summary.
# Console, HTML and other row files are scanned only for sentinel absence.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Scan','Publish','CompletedRows','RunPortableRows','Roster','OuterPublish')][string]$Case,
    [string]$Context,
    [string]$CheckpointToolPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Context)) { $Context = [string]$env:ANTIPHON_LIVE_EVIDENCE_CONTEXT }
$script:SentinelFragments = @(
    'SENTINEL-LINK-3a7c132f',
    'SENTINEL-SCREEN-3a7c132f',
    'SENTINEL-TOKEN-3a7c132f',
    'SENTINEL-DEVICE-3a7c132f')

function Get-PortableRoster {
    $rows = @(
        'control-success EvidenceIsSanitized CP-20',
        'control-assertion EvidenceIsSanitized CP-20',
        'control-skip EvidenceIsSanitized CP-20',
        'off-success EvidenceIsSanitized CP-20',
        'off-assertion EvidenceIsSanitized CP-20',
        'off-skip EvidenceIsSanitized CP-20',
        'slash-success EvidenceIsSanitized CP-20',
        'slash-assertion EvidenceIsSanitized CP-20',
        'slash-skip EvidenceIsSanitized CP-20',
        'submit-success EvidenceIsSanitized CP-20',
        'submit-assertion EvidenceIsSanitized CP-20',
        'submit-skip EvidenceIsSanitized CP-20',
        'c514-success EvidenceIsSanitized CP-20',
        'c514-assertion EvidenceIsSanitized CP-20',
        'c514-skip EvidenceIsSanitized CP-20',
        'enc-lf EvidenceIsSanitized CP-20',
        'enc-crlf EvidenceIsSanitized CP-20',
        'enc-ansi EvidenceIsSanitized CP-20',
        'enc-osc8 EvidenceIsSanitized CP-20',
        'enc-json EvidenceIsSanitized CP-20',
        'enc-xml EvidenceIsSanitized CP-20',
        'enc-double EvidenceIsSanitized CP-20',
        'c680-composer C680EvidenceIsSanitized CP-26',
        'c680-draft C680EvidenceIsSanitized CP-26',
        'c680-last-screen C680EvidenceIsSanitized CP-26',
        'c680-transcript C680EvidenceIsSanitized CP-26',
        'c680-observations C680EvidenceIsSanitized CP-26',
        'c680-success C680EvidenceIsSanitized CP-26',
        'c680-assertion C680EvidenceIsSanitized CP-26',
        'c680-skip C680EvidenceIsSanitized CP-26',
        'c680-exception C680EvidenceIsSanitized CP-26'
    )
    return $rows
}

function Exit-Evidence([int]$Code, [string]$Reason) {
    if ($Code -eq 0) { Write-Output 'EVIDENCE-SCAN clean' }
    else { Write-Output ('INVALID/evidence-sanitization reason=' + $Reason) }
    exit $Code
}

function Get-Items($value) {
    if ($null -eq $value) { return @() }
    $items = New-Object System.Collections.Generic.List[object]
    foreach ($item in @($value)) {
        if ($null -ne $item) { [void]$items.Add($item) }
    }
    if ($items.Count -eq 0) { return @() }
    return $items.ToArray()
}

function Get-Prop($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $prop = $obj.PSObject.Properties[$name]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}

function Read-ContextFile([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Exit-Evidence 2 'context-absent' # GUARD context-present
    }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or $item.Length -eq 0 -or $item.Length -gt 8388608) { Exit-Evidence 2 'unreadable' }
    try { $raw = [IO.File]::ReadAllText($Path) }
    catch { Exit-Evidence 2 'unreadable' }
    if ([string]::IsNullOrWhiteSpace($raw)) { Exit-Evidence 2 'empty-artifact' }
    try { return ($raw | ConvertFrom-Json) }
    catch { Exit-Evidence 2 'unreadable' }
}

function Expand-EvidenceText([string]$text) {
    $script:EvidenceMalformed = $false
    if ($null -eq $text) { return '' }
    $text = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    # GUARD decode
    $decoded = $text
    $esc = [char]27
    $osc = [regex]::Escape([string]$esc) + '\]8;;(.*?)(?:' + [regex]::Escape([string]$esc) + '\\|' + [char]7 + ')'
    $decoded = [regex]::Replace($decoded, $osc, '$1')
    $decoded = [regex]::Replace($decoded, [regex]::Escape([string]$esc) + '\[[0-9;?]*[ -/]*[@-~]', '')
    for ($pass = 0; $pass -lt 3; $pass++) {
        $decoded = [System.Net.WebUtility]::HtmlDecode($decoded)
        $decoded = [regex]::Replace($decoded, '\\u([0-9A-Fa-f]{4})', {
            param($m)
            [string][char][Convert]::ToInt32($m.Groups[1].Value, 16)
        })
        $decoded = [regex]::Replace($decoded, '%([0-9A-Fa-f]{2})', {
            param($m)
            [string][char][Convert]::ToInt32($m.Groups[1].Value, 16)
        })
    }
    if ($decoded -match '%(?![0-9A-Fa-f]{2})' -and $decoded -match 'SENTINEL-') { $script:EvidenceMalformed = $true }
    return $decoded
}

function Test-Sentinel([string]$text, [string[]]$needles) {
    $forms = @($text, (Expand-EvidenceText $text))
    foreach ($form in $forms) {
        foreach ($needle in $needles) {
            if ($form.Contains($needle)) { return $true }
        }
    }
    return $false
}

function Read-RequiredText([string]$path, [string]$missingReason) {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) {
        return @{ Ok = $false; Reason = $missingReason; Text = '' }
    }
    $item = Get-Item -LiteralPath $path -Force
    if ($item.PSIsContainer -or $item.Length -gt 8388608) { return @{ Ok = $false; Reason = 'unreadable'; Text = '' } }
    if ($item.Length -eq 0) { return @{ Ok = $false; Reason = 'empty-artifact'; Text = '' } }
    try { $payload = [IO.File]::ReadAllText($path) }
    catch { return @{ Ok = $false; Reason = 'unreadable'; Text = '' } }
    return @{ Ok = $true; Reason = ''; Text = $payload }
}

function Test-ConsoleSummary([string]$text) {
    return [regex]::IsMatch($text, '(?m)^\s*succeeded:\s*[1-9]')
}

function Get-TrxMap([string]$trxText) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.LoadXml($trxText)
    $defs = @{}
    foreach ($node in $xml.SelectNodes('//*[local-name()="UnitTest"]')) {
        $id = $node.GetAttribute('id')
        $method = $node.SelectSingleNode('.//*[local-name()="TestMethod"]')
        $name = ''
        if ($null -ne $method) { $name = $method.GetAttribute('name') }
        if (-not $defs.ContainsKey($id)) { $defs[$id] = @() }
        $defs[$id] += $name
    }
    $results = @{}
    foreach ($node in $xml.SelectNodes('//*[local-name()="UnitTestResult"]')) {
        $id = $node.GetAttribute('testId')
        $stdout = $node.SelectSingleNode('.//*[local-name()="StdOut"]')
        $message = $node.SelectSingleNode('.//*[local-name()="Message"]')
        $body = ''
        if ($null -ne $stdout) { $body += $stdout.InnerText }
        if ($null -ne $message) { $body += "`n" + $message.InnerText }
        if (-not $results.ContainsKey($id)) { $results[$id] = @() }
        $results[$id] += $body
    }
    $counters = $xml.SelectSingleNode('//*[local-name()="Counters"]')
    $executed = 0
    if ($null -ne $counters) {
        $raw = $counters.GetAttribute('executed')
        if (-not [string]::IsNullOrWhiteSpace($raw)) { [void][int]::TryParse($raw, [ref]$executed) }
    }
    return @{ Defs = $defs; Results = $results; Executed = $executed }
}

function Get-Needles($ctx) {
    $custom = @()
    foreach ($item in @(Get-Prop $ctx 'sentinels')) {
        if (-not [string]::IsNullOrWhiteSpace([string]$item)) { $custom += [string]$item }
    }
    $needles = @($script:SentinelFragments)
    if ($custom.Count -gt 0) { $needles += $custom }
    return $needles
}

function Test-PublicationFault($ctx) {
    $fault = [string](Get-Prop $ctx 'fault')
    if ($fault -eq 'context-write') { return 'write-fault' } # GUARD context-write
    if ($fault -eq 'artifact-read') { return 'unreadable' } # GUARD artifact-read
    if ($fault -eq 'receipt-write') { return 'write-fault' } # GUARD receipt-write
    if ($fault -eq 'summary-write') { return 'write-fault' } # GUARD summary-write
    if ($fault -eq 'publish-write') { return 'write-fault' } # GUARD publish-write
    return ''
}

function Test-PublicationGate($ctx) {
    $phase = [string](Get-Prop $ctx 'phase')
    if ($phase -eq 'wait75') { return 'wait75' } # GUARD wait75
    $rawExit = 0
    $rawText = [string](Get-Prop $ctx 'rawExit')
    if (-not [string]::IsNullOrWhiteSpace($rawText)) { [void][int]::TryParse($rawText, [ref]$rawExit) }
    if ($rawExit -eq 1 -or $rawExit -eq 2 -or $rawExit -eq 3 -or $rawExit -eq 8) { return 'nonzero-exit' } # GUARD nonzero-exit
    return (Test-PublicationFault $ctx)
}

function Invoke-EvidenceScan($ctx) {
    $cases = Get-Items (Get-Prop $ctx 'cases')
    if ($cases.Count -eq 0) { return 'zero-case' } # GUARD zero-case
    $seen = @{}
    foreach ($case in $cases) {
        $key = [string](Get-Prop $case 'case') + '|' + [string](Get-Prop $case 'test')
        if ($seen.ContainsKey($key)) { return 'duplicate-uid' } # GUARD duplicate-uid
        $seen[$key] = $true
    }
    $needles = Get-Needles $ctx
    $expectedSource = [string](Get-Prop $ctx 'source')
    $expectedRun = [string](Get-Prop $ctx 'checkpointRun')
    $producers = Get-Items (Get-Prop $ctx 'producers')
    if ($producers.Count -eq 0) { return 'missing-trx' }
    $maps = @{}
    foreach ($producer in $producers) {
        $state = [string](Get-Prop $producer 'state')
        if ($state -ne 'green' -and $state -ne 'red') { return 'producer-running' } # GUARD producer-running
        $exitCode = 0
        $exitText = [string](Get-Prop $producer 'exit')
        if (-not [string]::IsNullOrWhiteSpace($exitText)) { [void][int]::TryParse($exitText, [ref]$exitCode) }
        if ($state -eq 'red' -or $exitCode -ne 0) { return 'child-red' } # GUARD child-red
        $source = [string](Get-Prop $producer 'source')
        $build = [string](Get-Prop $producer 'build')
        $runId = [string](Get-Prop $producer 'runId')
        if ($source -ne $expectedSource) { return 'stale-source' } # GUARD stale-source
        if ($build -ne 'verified' -or [string](Get-Prop $producer 'buildCommit') -ne $expectedSource) { return 'stale-build' } # GUARD stale-build
        if ($runId -ne $expectedRun -or [string]::IsNullOrWhiteSpace($expectedRun)) { return 'cross-run' } # GUARD cross-run
        $id = [string](Get-Prop $producer 'id')
        $trxRead = Read-RequiredText ([string](Get-Prop $producer 'trx')) 'missing-trx'
        if (-not $trxRead.Ok) { return $trxRead.Reason } # GUARD missing-trx
        $consoleRead = Read-RequiredText ([string](Get-Prop $producer 'console')) 'missing-trx'
        if (-not $consoleRead.Ok) { return $consoleRead.Reason }
        if (-not (Test-ConsoleSummary $consoleRead.Text)) { return 'zero-summary' } # GUARD zero-summary
        # GUARD console-absence-only
        $map = Get-TrxMap $trxRead.Text
        if ($map.Executed -lt 1) { return 'zero-results' } # GUARD zero-results
        $maps[$id] = $map
        $rowDir = [string](Get-Prop $producer 'rowDir')
        if ([string]::IsNullOrWhiteSpace($rowDir) -or -not (Test-Path -LiteralPath $rowDir -PathType Container)) { return 'missing-trx' }
        $files = New-Object System.Collections.Generic.List[string]
        try {
            foreach ($file in [IO.Directory]::EnumerateFileSystemEntries($rowDir, '*', [IO.SearchOption]::AllDirectories)) { $files.Add($file) }
        } catch { return 'unreadable' }
        foreach ($file in $files) {
            $item = Get-Item -LiteralPath $file -Force
            if (Test-Sentinel ([string]$item.Name) $needles) { return 'sentinel-present' }
            if ($item.PSIsContainer) { continue }
            if ($item.Length -gt 8388608) { return 'unreadable' }
            if ($item.Length -eq 0) { continue }
            $payload = ''
            try { $payload = [IO.File]::ReadAllText($file) }
            catch { return 'unreadable' }
            $script:EvidenceMalformed = $false
            if (Test-Sentinel $payload $needles) { return 'sentinel-present' } # GUARD row-sentinel
            if ($script:EvidenceMalformed) { return 'encoded-sentinel' }
        }
    }
    foreach ($case in $cases) {
        $caseId = [string](Get-Prop $case 'case')
        $test = [string](Get-Prop $case 'test')
        $cp = [string](Get-Prop $case 'cp')
        $marker = 'C1545-EVIDENCE case=' + $caseId + ' test=' + $test + ' kind='
        if (-not $maps.ContainsKey($cp)) { return 'marker-missing' }
        $map = $maps[$cp]
        $hits = @()
        foreach ($id in @($map.Defs.Keys)) {
            $names = @($map.Defs[$id])
            $matched = $false
            foreach ($name in $names) { if ($name -eq $test) { $matched = $true } }
            if (-not $matched) { continue }
            if ($names.Count -gt 1) { return 'duplicate-uid' }
            $bodies = @()
            if ($map.Results.ContainsKey($id)) { $bodies = @($map.Results[$id]) }
            if ($bodies.Count -gt 1) { return 'duplicate-uid' }
            foreach ($body in $bodies) { if ($body.Contains($marker)) { $hits += $id } }
        }
        if ($hits.Count -ne 1) { return 'marker-missing' } # GUARD marker-missing
    }
    $receipt = Read-RequiredText ([string](Get-Prop $ctx 'receipt')) 'missing-trx'
    if (-not $receipt.Ok -and $receipt.Reason -eq 'unreadable') { return 'unreadable' } # GUARD unreadable
    if (-not $receipt.Ok) { return 'empty-artifact' } # GUARD empty-artifact
    $summary = Read-RequiredText ([string](Get-Prop $ctx 'summary')) 'missing-trx'
    if (-not $summary.Ok -and $summary.Reason -eq 'unreadable') { return 'unreadable' }
    if (-not $summary.Ok) { return 'empty-artifact' }
    if (Test-Sentinel $receipt.Text $needles) { return 'sentinel-present' }
    if ($script:EvidenceMalformed) { return 'encoded-sentinel' }
    if (Test-Sentinel $summary.Text $needles) { return 'sentinel-present' }
    if ($script:EvidenceMalformed) { return 'encoded-sentinel' }
    foreach ($case in $cases) {
        $caseId = [string](Get-Prop $case 'case')
        $test = [string](Get-Prop $case 'test')
        $marker = 'C1545-EVIDENCE case=' + $caseId + ' test=' + $test + ' '
        if (-not $receipt.Text.Contains($marker)) { return 'receipt-marker-missing' } # GUARD receipt-marker
        if (-not $summary.Text.Contains($marker)) { return 'summary-marker-missing' } # GUARD summary-marker
    }
    $requirePublic = Get-Prop $ctx 'requirePublic'
    if ($requirePublic -eq $true) {
        $publicRead = Read-RequiredText ([string](Get-Prop $ctx 'publicSummary')) 'missing-trx'
        if (-not $publicRead.Ok) { return 'empty-artifact' }
        if (Test-Sentinel $publicRead.Text $needles) { return 'sentinel-present' }
        if ($script:EvidenceMalformed) { return 'encoded-sentinel' }
        foreach ($case in $cases) {
            $caseId = [string](Get-Prop $case 'case')
            $test = [string](Get-Prop $case 'test')
            $marker = 'C1545-EVIDENCE case=' + $caseId + ' test=' + $test + ' '
            if (-not $publicRead.Text.Contains($marker)) { return 'summary-marker-missing' }
        }
    }
    $requireReport = Get-Prop $ctx 'requireReport'
    if ($requireReport -eq $true) {
        $report = [string](Get-Prop $ctx 'report')
        if ([string]::IsNullOrWhiteSpace($report) -or -not (Test-Path -LiteralPath $report -PathType Leaf)) { return 'report-missing' }
        $reportText = Read-RequiredText $report 'report-missing'
        if (-not $reportText.Ok) { return 'report-missing' }
        if ($reportText.Text -notmatch [regex]::Escape($expectedSource)) { return 'stale-source' }
    }
    $tree = Test-BoundEvidenceTree $ctx $needles
    if ($tree) { return $tree }
    return ''
}

function Test-BoundEvidenceTree($ctx, [string[]]$needles) {
    $roots = New-Object System.Collections.Generic.List[string]
    $evidenceRoot = [string](Get-Prop $ctx 'evidenceRoot')
    if (-not [string]::IsNullOrWhiteSpace($evidenceRoot)) { $roots.Add($evidenceRoot) }
    $resultsRoot = [string](Get-Prop $ctx 'resultsRoot')
    $checkpointRun = [string](Get-Prop $ctx 'checkpointRun')
    if (-not [string]::IsNullOrWhiteSpace($resultsRoot) -and -not [string]::IsNullOrWhiteSpace($checkpointRun)) {
        $roots.Add((Join-Path $resultsRoot $checkpointRun))
    }
    foreach ($directory in $roots) {
        $reason = Test-EvidenceTree $directory $needles
        if ($reason) { return $reason }
    }
    return ''
}

function Read-BoundedText([string]$path, [long]$length) {
    if ($length -le 8388608) { return [IO.File]::ReadAllText($path) }
    $stream = New-Object IO.FileStream($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $count = 8388608
        $buffer = New-Object byte[] $count
        $read = $stream.Read($buffer, 0, $count)
        return [Text.Encoding]::UTF8.GetString($buffer, 0, $read)
    } finally { $stream.Dispose() }
}

function Test-EvidenceTree([string]$directory, [string[]]$needles) {
    if ([string]::IsNullOrWhiteSpace($directory) -or -not (Test-Path -LiteralPath $directory -PathType Container)) { return '' }
    $entries = New-Object System.Collections.Generic.List[string]
    try {
        foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($directory, '*', [IO.SearchOption]::AllDirectories)) {
            $entries.Add($entry)
        }
    } catch { return 'unreadable' }
    foreach ($entry in $entries) {
        $item = Get-Item -LiteralPath $entry -Force
        if (Test-Sentinel ([string]$item.Name) $needles) { return 'sentinel-present' }
        if ($item.PSIsContainer) { continue }
        if ($item.Length -eq 0) { continue }
        $payload = ''
        try { $payload = Read-BoundedText $entry $item.Length }
        catch { return 'unreadable' }
        $script:EvidenceMalformed = $false
        if (Test-Sentinel $payload $needles) { return 'sentinel-present' }
        if ($script:EvidenceMalformed) { return 'encoded-sentinel' }
    }
    return ''
}

function Write-PublicSummary($ctx) {
    $public = [string](Get-Prop $ctx 'publicSummary')
    if ([string]::IsNullOrWhiteSpace($public)) { return 'write-fault' }
    $parent = Split-Path -Parent $public
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    if (Test-Path -LiteralPath $public -PathType Container) { return 'write-fault' }
    $summary = Read-RequiredText ([string](Get-Prop $ctx 'summary')) 'empty-artifact'
    if (-not $summary.Ok) { return 'write-fault' }
    try { [IO.File]::WriteAllText($public, $summary.Text) }
    catch { return 'write-fault' }
    if (-not (Test-Path -LiteralPath $public -PathType Leaf)) { return 'write-fault' }
    return ''
}

function Invoke-EvidencePublish($ctx) {
    $gate = Test-PublicationGate $ctx
    if ($gate) { return $gate }
    $reason = Invoke-EvidenceScan $ctx
    if ($reason) { return $reason } # GUARD publish-after-scan
    $written = Write-PublicSummary $ctx
    if ($written) { return $written }
    $script:PublicationCount = 1
    return ''
}

function Convert-ProducerFromRun($ctx, [string]$rowId, [string]$outputRelative) {
    $run = [string](Get-Prop $ctx 'checkpointRun')
    $root = [string](Get-Prop $ctx 'resultsRoot')
    if ([string]::IsNullOrWhiteSpace($run) -or [string]::IsNullOrWhiteSpace($root)) { Exit-Evidence 2 'cross-run' }
    $runDir = Join-Path $root $run
    $statePath = Join-Path $runDir 'state.json'
    $stateRead = Read-RequiredText $statePath 'missing-trx'
    if (-not $stateRead.Ok) { Exit-Evidence 2 $stateRead.Reason }
    $state = $stateRead.Text | ConvertFrom-Json
    if ([string]$state.runId -ne $run) { Exit-Evidence 2 'cross-run' }
    $row = $null
    foreach ($candidate in @($state.rows)) { if ([string]$candidate.id -eq $rowId) { $row = $candidate } }
    if ($null -eq $row) { Exit-Evidence 2 'producer-running' }
    $line = [string]$row.line
    $commit = ''
    $buildSource = ''
    $commitMatch = [regex]::Match($line, 'commit=([0-9a-f]{40})')
    if ($commitMatch.Success) { $commit = $commitMatch.Groups[1].Value }
    $buildMatch = [regex]::Match($line, 'buildSource=([A-Za-z0-9_-]+)')
    if ($buildMatch.Success) { $buildSource = $buildMatch.Groups[1].Value }
    $repo = [string](Get-Prop $ctx 'repo')
    $bindingPath = Join-Path (Join-Path $repo $outputRelative) 'checkpoint-build-source.json'
    $binding = Read-RequiredText $bindingPath 'stale-build'
    $buildCommit = ''
    if ($binding.Ok) {
        try {
            $doc = $binding.Text | ConvertFrom-Json
            $buildCommit = [string]$doc.Commit
        } catch { Exit-Evidence 2 'stale-build' }
    }
    $producer = [pscustomobject]@{
        id = $rowId
        state = [string]$row.state
        exit = [string]$row.exitCode
        source = $commit
        build = $buildSource
        buildCommit = $buildCommit
        runId = $run
        trx = (Join-Path (Join-Path (Join-Path $runDir 'rows') $rowId) 'run.trx')
        console = (Join-Path (Join-Path (Join-Path $runDir 'rows') $rowId) 'console.log')
        rowDir = (Join-Path (Join-Path $runDir 'rows') $rowId)
    }
    return $producer
}

if ($Case -eq 'Roster') {
    Get-PortableRoster | ForEach-Object { Write-Output $_ }
    exit 0
}

if ($Case -eq 'Scan' -or $Case -eq 'Publish') {
    $ctx = Read-ContextFile $Context
    if ($Case -eq 'Scan') {
        $reason = Invoke-EvidenceScan $ctx
        if ($reason) { Exit-Evidence 2 $reason }
        Exit-Evidence 0 'clean'
    }
    $script:PublicationCount = 0
    $reason = Invoke-EvidencePublish $ctx
    if ($reason) { Exit-Evidence 2 $reason }
    if ($script:PublicationCount -ne 1) { Exit-Evidence 2 'write-fault' }
    Write-Output 'publicationCount=1'
    Exit-Evidence 0 'clean'
}

if ($Case -eq 'CompletedRows') {
    $ctx = Read-ContextFile $Context
    $repo = [string](Get-Prop $ctx 'repo')
    $outputs = Get-Items (Get-Prop $ctx 'outputs')
    if ($outputs.Count -eq 0) { Exit-Evidence 2 'missing-trx' }
    $producers = @()
    foreach ($output in $outputs) {
        $producers += Convert-ProducerFromRun $ctx ([string](Get-Prop $output 'id')) ([string](Get-Prop $output 'path'))
    }
    $scan = [pscustomobject]@{
        source = [string](Get-Prop $ctx 'source')
        checkpointRun = [string](Get-Prop $ctx 'checkpointRun')
        cases = @(Get-Prop $ctx 'cases')
        sentinels = @(Get-Prop $ctx 'sentinels')
        producers = $producers
        resultsRoot = [string](Get-Prop $ctx 'resultsRoot')
        receipt = [string](Get-Prop $ctx 'receipt')
        summary = [string](Get-Prop $ctx 'summary')
        publicSummary = [string](Get-Prop $ctx 'publicSummary')
        requirePublic = (Get-Prop $ctx 'requirePublic')
        requireReport = $false # GUARD report-deferred
        report = [string](Get-Prop $ctx 'report')
        fault = ''
    }
    $reason = Invoke-EvidenceScan $scan
    if ($reason) { Exit-Evidence 2 $reason }
    Exit-Evidence 0 'clean'
}

if ($Case -eq 'OuterPublish') {
    $ctx = Read-ContextFile $Context
    $gate = Test-PublicationGate $ctx
    if ($gate) { Exit-Evidence 2 $gate }
    $producers = Get-Items (Get-Prop $ctx 'producers')
    $run = [string](Get-Prop $ctx 'checkpointRun')
    $root = [string](Get-Prop $ctx 'resultsRoot')
    $stateReady = $false
    if (-not [string]::IsNullOrWhiteSpace($run) -and -not [string]::IsNullOrWhiteSpace($root)) {
        $statePath = Join-Path (Join-Path $root $run) 'state.json'
        if (Test-Path -LiteralPath $statePath -PathType Leaf) { $stateReady = $true }
    }
    $scan = $ctx
    if ($stateReady -or $producers.Count -eq 0) {
        $outputs = Get-Items (Get-Prop $ctx 'outputs')
        if ($outputs.Count -eq 0) { Exit-Evidence 2 'missing-trx' }
        $built = @()
        foreach ($output in $outputs) {
            $built += Convert-ProducerFromRun $ctx ([string](Get-Prop $output 'id')) ([string](Get-Prop $output 'path'))
        }
        $scan = [pscustomobject]@{
            source = [string](Get-Prop $ctx 'source')
            checkpointRun = [string](Get-Prop $ctx 'checkpointRun')
            resultsRoot = [string](Get-Prop $ctx 'resultsRoot')
            cases = @(Get-Prop $ctx 'cases')
            sentinels = @(Get-Prop $ctx 'sentinels')
            producers = $built
            receipt = [string](Get-Prop $ctx 'receipt')
            summary = [string](Get-Prop $ctx 'summary')
            publicSummary = [string](Get-Prop $ctx 'publicSummary')
            requirePublic = (Get-Prop $ctx 'requirePublic')
            requireReport = $false
            report = ''
            fault = ''
        }
    }
    $reason = Invoke-EvidenceScan $scan
    if ($reason) { Exit-Evidence 2 $reason } # GUARD scanner-before-publish
    $report = [string](Get-Prop $ctx 'report')
    if ([string]::IsNullOrWhiteSpace($report)) {
        $report = Join-Path (Join-Path ([string](Get-Prop $ctx 'resultsRoot')) ([string](Get-Prop $ctx 'checkpointRun'))) 'report.json'
    }
    if ([string]::IsNullOrWhiteSpace($report) -or -not (Test-Path -LiteralPath $report -PathType Leaf)) {
        Exit-Evidence 2 'report-missing' # GUARD final-report
    }
    $reportText = [IO.File]::ReadAllText($report)
    if ($reportText -notmatch [regex]::Escape([string](Get-Prop $ctx 'source'))) {
        Exit-Evidence 2 'stale-source' # GUARD final-report-source
    }
    $script:PublicationCount = 0
    $written = Write-PublicSummary $ctx
    if ($written) { Exit-Evidence 2 $written }
    $script:PublicationCount = 1
    Write-Output 'publicationCount=1'
    Exit-Evidence 0 'clean'
}

if ($Case -eq 'RunPortableRows') {
    Exit-Evidence 2 'sanitizer-absent'
}
