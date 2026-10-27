#requires -Version 5.1
# CARD-1514: opt-in phase timing sidecars. ASCII-only. Unset phase root is a no-op.
# TRX stays authoritative. This file never edits TRX, LogRoot summary.json, receipts,
# selection, the cost cache, or the release manifest.

if ($script:TestPhaseEvidenceLoaded) { return }
$script:TestPhaseEvidenceLoaded = $true

if (-not (Get-Command Write-NightlyAtomicJson -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'nightly-common.ps1')
}

$script:TestPhaseVariableNames = @(
    'ANTIPHON_TEST_PHASE_ROOT',
    'ANTIPHON_TEST_PHASE_RUN_ID',
    'ANTIPHON_TEST_PHASE_GROUP_ID',
    'ANTIPHON_TEST_PHASE_ATTEMPT',
    'ANTIPHON_TEST_PHASE_SUITE'
)

function Remove-TestPhaseVariables {
    param($Environment)
    if ($null -eq $Environment) { return }
    foreach ($name in $script:TestPhaseVariableNames) {
        if ($Environment -is [System.Collections.IDictionary]) {
            [void]$Environment.Remove($name)
        }
    }
}

function Enable-TestPhaseExecutionEnvironment {
    param(
        [hashtable]$Environment,
        [string]$LogRoot,
        [string]$RunId,
        [string]$GroupId,
        [string]$Attempt,
        [string]$Suite,
        [string]$Profile,
        [string]$TimingRoot = ''
    )
    if ($null -eq $Environment) { $Environment = @{} }
    $explicit = [Environment]::GetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT')
    $qualification = ($Profile -eq 'rc') -or ($Profile -eq 'nightly')
    $enable = $qualification -or (-not [string]::IsNullOrWhiteSpace($explicit)) -or (-not [string]::IsNullOrWhiteSpace($TimingRoot))
    if (-not $enable) {
        Remove-TestPhaseVariables -Environment $Environment
        return $Environment
    }
    $root = $TimingRoot
    if ([string]::IsNullOrWhiteSpace($root)) { $root = $explicit }
    if ([string]::IsNullOrWhiteSpace($root)) {
        $root = Join-Path $LogRoot 'timing'
    }
    $Environment['ANTIPHON_TEST_PHASE_ROOT'] = [string]$root
    $Environment['ANTIPHON_TEST_PHASE_RUN_ID'] = [string]$RunId
    $Environment['ANTIPHON_TEST_PHASE_GROUP_ID'] = [string]$GroupId
    $Environment['ANTIPHON_TEST_PHASE_ATTEMPT'] = [string]$Attempt
    $Environment['ANTIPHON_TEST_PHASE_SUITE'] = [string]$Suite
    return $Environment
}

function ConvertTo-TestPhaseCsvCell {
    param($Value)
    $text = ''
    if ($null -ne $Value) { $text = [string]$Value }
    $escaped = $text.Replace('"', '""')
    if ($escaped.Length -gt 0) {
        $lead = $escaped.Substring(0, 1)
        if ($lead -eq '=' -or $lead -eq '+' -or $lead -eq '-' -or $lead -eq '@') {
            $escaped = "'" + $escaped
        }
    }
    return '"' + $escaped + '"'
}

function ConvertTo-TestPhaseMarkdownCell {
    param($Value)
    $text = ''
    if ($null -ne $Value) { $text = [string]$Value }
    $text = $text.Replace('\', '\\')
    $text = $text.Replace('|', '\|')
    $text = $text.Replace("`r", ' ')
    $text = $text.Replace("`n", ' ')
    return $text
}

function ConvertTo-TestPhaseJsonString {
    param([string]$Text)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($ch in $Text.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '"') { [void]$sb.Append('\"') }
        elseif ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($ch -eq "`n") { [void]$sb.Append('\n') }
        elseif ($ch -eq "`r") { [void]$sb.Append('\r') }
        elseif ($code -lt 32) { [void]$sb.Append('\u' + $code.ToString('x4')) }
        else { [void]$sb.Append($ch) }
    }
    [void]$sb.Append('"')
    return $sb.ToString()
}

# Hand-built so a one-element array stays an array on Windows PowerShell 5.1.
function ConvertTo-TestPhaseJsonText {
    param($Value, [int]$Depth = 0)
    if ($Depth -gt 24) { return 'null' }
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [bool]) {
        if ($Value) { return 'true' }
        return 'false'
    }
    if ($Value -is [byte] -or $Value -is [int16] -or $Value -is [int32] -or $Value -is [int64] -or $Value -is [decimal] -or $Value -is [double] -or $Value -is [single]) {
        return [System.Convert]::ToString($Value, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [string]) { return (ConvertTo-TestPhaseJsonString -Text $Value) }
    if ($Value -is [System.Collections.IDictionary]) {
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($key in @($Value.Keys)) {
            $parts.Add((ConvertTo-TestPhaseJsonString -Text ([string]$key)) + ':' + (ConvertTo-TestPhaseJsonText -Value $Value[$key] -Depth ($Depth + 1)))
        }
        return '{' + ($parts.ToArray() -join ',') + '}'
    }
    $props = $Value.PSObject.Properties
    $hasNote = $false
    if ($null -ne $props) {
        foreach ($prop in $props) {
            if ($prop.MemberType -eq [System.Management.Automation.PSMemberTypes]::NoteProperty) { $hasNote = $true; break }
        }
    }
    if ($hasNote) {
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($prop in $props) {
            if ($prop.MemberType -ne [System.Management.Automation.PSMemberTypes]::NoteProperty) { continue }
            $parts.Add((ConvertTo-TestPhaseJsonString -Text $prop.Name) + ':' + (ConvertTo-TestPhaseJsonText -Value $prop.Value -Depth ($Depth + 1)))
        }
        return '{' + ($parts.ToArray() -join ',') + '}'
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($item in $Value) {
            $parts.Add((ConvertTo-TestPhaseJsonText -Value $item -Depth ($Depth + 1)))
        }
        return '[' + ($parts.ToArray() -join ',') + ']'
    }
    return (ConvertTo-TestPhaseJsonString -Text ([string]$Value))
}

function Write-TestPhaseTextAtomic {
    param([string]$Path, [string]$Text)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $tmp = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($tmp, $Text, $utf8)
    if (Test-Path -LiteralPath $Path) {
        $bak = $Path + '.bak'
        [System.IO.File]::Replace($tmp, $Path, $bak)
        if (Test-Path -LiteralPath $bak) { Remove-Item -LiteralPath $bak -Force -ErrorAction SilentlyContinue }
    } else {
        [System.IO.File]::Move($tmp, $Path)
    }
}

function ConvertTo-TestPhaseRelativePath {
    param([string]$LogRoot, [string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($LogRoot)) { return '' }
    $root = [System.IO.Path]::GetFullPath($LogRoot).TrimEnd('\', '/')
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { return '' }
    $rel = $full.Substring($root.Length).TrimStart('\', '/')
    return ($rel.Replace('\', '/'))
}

function Get-TestPhaseClassBasisMap {
    $map = @{}
    $path = [string]$script:NightlyClassCostPath
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = Join-Path $PSScriptRoot 'nightly-class-costs.json'
    }
    if (-not (Test-Path -LiteralPath $path)) { return $map }
    $doc = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
    if ($null -eq $doc.classes) { return $map }
    foreach ($row in @($doc.classes.PSObject.Properties)) {
        $map[[string]$row.Name] = [string]$row.Value.basis
    }
    return $map
}

function Get-TestPhaseTimingDirectory {
    param([string]$RunDirectory)
    if (-not [string]::IsNullOrWhiteSpace([string]$script:QualificationTimingRoot)) {
        return [string]$script:QualificationTimingRoot
    }
    return (Join-Path $RunDirectory 'timing')
}

function Read-TestPhaseTrxRows {
    param([string]$Path)
    $rows = @()
    if (-not (Test-Path -LiteralPath $Path)) { return $rows }
    $doc = New-Object System.Xml.XmlDocument
    $doc.Load($Path)
    foreach ($node in @($doc.GetElementsByTagName('UnitTestResult'))) {
        $stdout = ''
        foreach ($child in @($node.GetElementsByTagName('StdOut'))) {
            $stdout = [string]$child.InnerText
        }
        $rows += [pscustomobject]@{
            TestId = [string]$node.GetAttribute('testId')
            Outcome = [string]$node.GetAttribute('outcome')
            Name = [string]$node.GetAttribute('testName')
            Duration = [string]$node.GetAttribute('duration')
            StdOut = $stdout
            Path = $Path
        }
    }
    return $rows
}

function Get-TestPhaseNativeIds {
    param([string]$Text)
    $ids = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrEmpty($Text)) { return @() }
    $marker = 'C1514_NATIVE '
    foreach ($line in ($Text -split "`n")) {
        $trim = $line.Trim().TrimEnd("`r")
        if ($trim.StartsWith('ANTIPHON_TEST_PHASE_ID ', [StringComparison]::Ordinal)) {
            $id = $trim.Substring('ANTIPHON_TEST_PHASE_ID '.Length).Trim()
        } elseif ($trim.StartsWith($marker, [StringComparison]::Ordinal)) {
            # Preserve the original probe's explicit identity witness.
            $rest = $trim.Substring($marker.Length)
            $space = $rest.IndexOf(' ')
            if ($space -lt 1) { continue }
            $id = $rest.Substring($space + 1).Trim()
        } else { continue }
        if (-not [string]::IsNullOrWhiteSpace($id) -and -not $ids.Contains($id)) {
            $ids.Add($id)
        }
    }
    return @($ids.ToArray())
}

function Read-TestPhaseJsonlFile {
    param([string]$Path)
    $records = New-Object System.Collections.Generic.List[object]
    $torn = 0
    $malformed = 0
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
    $reader = [System.IO.StreamReader]::new($stream)
    try { $raw = $reader.ReadToEnd() } finally { $reader.Dispose(); $stream.Dispose() }
    $parts = $raw.Split([string[]]@("`n"), [System.StringSplitOptions]::None)
    for ($i = 0; $i -lt $parts.Length; $i++) {
        $line = $parts[$i].TrimEnd("`r")
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parsed = $null
        $ok = $true
        try { $parsed = $line | ConvertFrom-Json } catch { $ok = $false }
        if ($ok -and $null -ne $parsed -and -not [string]::IsNullOrWhiteSpace([string]$parsed.recordKind)) {
            $records.Add($parsed)
            continue
        }
        $lastContent = $true
        for ($j = $i + 1; $j -lt $parts.Length; $j++) {
            if (-not [string]::IsNullOrWhiteSpace($parts[$j].TrimEnd("`r"))) { $lastContent = $false }
        }
        if ($lastContent) { $torn++ } else { $malformed++ }
    }
    return [pscustomobject]@{ Records = @($records.ToArray()); Torn = $torn; Malformed = $malformed; Path = $Path }
}

function Read-TestPhaseJsonlBounded {
    param(
        [string]$Path,
        [int]$MaxBytes = 262144,
        [int]$MaxLines = 4000,
        [int]$DeadlineMilliseconds = 200
    )
    $records = New-Object System.Collections.Generic.List[object]
    $complete = $true
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
    try {
        $reader = [System.IO.StreamReader]::new($stream)
        $bytes = 0
        $lines = 0
        while ($lines -lt $MaxLines -and $bytes -lt $MaxBytes) {
            if ($watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) {
                $complete = $false
                break
            }
            $line = $reader.ReadLine()
            if ($null -eq $line) { break }
            $lines++
            $bytes += [Text.Encoding]::UTF8.GetByteCount($line) + 1
            if ($bytes -gt $MaxBytes) {
                $complete = $false
                break
            }
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $parsed = $null
            $ok = $true
            try { $parsed = $line | ConvertFrom-Json } catch { $ok = $false }
            if ($ok -and $null -ne $parsed -and -not [string]::IsNullOrWhiteSpace([string]$parsed.recordKind)) {
                $records.Add($parsed)
            }
        }
        if ($lines -ge $MaxLines -or $bytes -ge $MaxBytes) { $complete = $false }
    } finally {
        $stream.Dispose()
    }
    return [pscustomobject]@{ Records = @($records.ToArray()); Complete = $complete; Path = $Path }
}

function Write-TestPhasePlannedManifest {
    param(
        [string]$LogRoot,
        [string]$RunId,
        [string]$Attempt,
        [string]$Suite,
        [string]$SourceDigest,
        [string]$AssemblyDigest,
        [string]$PolicyDigest,
        [string]$CostFileDigest,
        [string]$AssemblyId,
        $Groups,
        $DiscoveryNodes,
        $ProfileExclusions
    )
    $entry = [pscustomobject]@{
        Document = [pscustomobject]@{ Nodes = @($DiscoveryNodes) }
        InProgress = @()
        Views = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    }
    $view = Get-NightlyDiscoveryIndexView -Entry $entry -ProfileExclusions $ProfileExclusions -ProfileAware
    $basisMap = Get-TestPhaseClassBasisMap
    $groupRows = @()
    foreach ($group in @($Groups)) {
        $ids = New-Object System.Collections.Generic.List[string]
        $carried = @()
        if ($group.PSObject.Properties.Name -contains 'nodes' -and $null -ne $group.nodes) {
            $carried = @($group.nodes)
        }
        if ($carried.Count -gt 0) {
            foreach ($node in $carried) {
                $uid = [string]$node.Uid
                if (-not [string]::IsNullOrWhiteSpace($uid)) { $ids.Add($uid) }
            }
        } else {
            $classes = @()
            foreach ($c in @($group.classes)) { $classes += [string]$c }
            foreach ($uid in @(Get-NightlyDiscoveryIndexRequiredUids -View $view -RequiredClasses $classes)) {
                $ids.Add([string]$uid)
            }
        }
        $bases = @{}
        foreach ($className in @($group.classes)) {
            $key = [string]$className
            $basis = 'model'
            if ($basisMap.ContainsKey($key)) { $basis = [string]$basisMap[$key] }
            elseif ($null -ne $script:NightlyClassCostTable -and $script:NightlyClassCostTable.ContainsKey($key)) { $basis = 'measured' }
            $bases[$basis] = $true
        }
        $pricing = 'model'
        $basisNames = @($bases.Keys)
        if ($basisNames.Count -eq 1) { $pricing = [string]$basisNames[0] }
        elseif ($basisNames.Count -gt 1) { $pricing = 'mixed' }
        $filter = ''
        if ($group.PSObject.Properties.Name -contains 'exactFilter' -and -not [string]::IsNullOrWhiteSpace([string]$group.exactFilter)) {
            $filter = [string]$group.exactFilter
        } elseif ($group.PSObject.Properties.Name -contains 'groupFilter') {
            $filter = [string]$group.groupFilter
        }
        $seconds = 0
        if ($group.PSObject.Properties.Name -contains 'seconds' -and $null -ne $group.seconds) {
            $seconds = [double]$group.seconds
        }
        $groupRows += [ordered]@{
            chunk = [string]$group.id
            classes = @($group.classes | ForEach-Object { [string]$_ })
            filter = $filter
            plannedTestIds = @($ids.ToArray())
            plannedSeconds = $seconds
            pricingBasis = $pricing
            sharedManifestOrder = $true
        }
    }
    $union = @{}
    $duplicate = $false
    foreach ($row in $groupRows) {
        foreach ($id in @($row.plannedTestIds)) {
            if ($union.ContainsKey([string]$id)) { $duplicate = $true }
            $union[[string]$id] = $true
        }
    }
    $missing = @()
    foreach ($id in @($view.RequiredUids)) {
        if (-not $union.ContainsKey([string]$id)) { $missing += [string]$id }
    }
    $extra = @()
    foreach ($id in @($union.Keys)) {
        $found = $false
        foreach ($req in @($view.RequiredUids)) {
            if ([string]$req -eq [string]$id) { $found = $true }
        }
        if (-not $found) { $extra += [string]$id }
    }
    $manifest = [ordered]@{
        version = 1
        runId = [string]$RunId
        attempt = [string]$Attempt
        suite = [string]$Suite
        sourceDigest = [string]$SourceDigest
        assemblyDigest = [string]$AssemblyDigest
        policyDigest = [string]$PolicyDigest
        costFileDigest = [string]$CostFileDigest
        assembly = [string]$AssemblyId
        requiredPartitionExact = ((-not $duplicate) -and $missing.Count -eq 0 -and $extra.Count -eq 0)
        missingIds = @($missing)
        extraIds = @($extra)
        groups = @($groupRows)
    }
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
    $path = Join-Path $dir 'groups-planned.json'
    Write-TestPhaseTextAtomic -Path $path -Text (ConvertTo-TestPhaseJsonText -Value $manifest)
    return $path
}

function Write-TestPhaseFailureMarker {
    param([string]$RunDirectory, [string]$Code)
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    try {
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        $path = Join-Path $dir 'phase-summary-failed'
        [System.IO.File]::WriteAllText($path, $Code)
    } catch {
        # An unwritable timing directory must not change the caller's verdict.
    }
}

function ConvertTo-TestPhaseExecutedChunk {
    param($Item, [string]$Chunk, [string]$RunId, [string]$Attempt, [string]$Suite, [double]$HostWallSeconds, [bool]$TrxMissing, [int]$PassedCount, $Outcomes, [string]$Trx, [string]$TrxDigest, $ExitCode, $CoverageComplete, $TestsPassed)
    $priorOutcomes = New-Object System.Collections.Generic.List[object]
    if ($null -ne $Outcomes) {
        foreach ($row in @($Outcomes)) {
            if ($null -eq $row) { continue }
            $priorOutcomes.Add([ordered]@{ testId = [string]$row.testId; outcome = [string]$row.outcome })
        }
    }
    $chunkId = $Chunk
    $run = $RunId
    $attempt = $Attempt
    $suite = $Suite
    $wall = $HostWallSeconds
    $missing = $TrxMissing
    $passed = $PassedCount
    $trx = $Trx
    $digest = $TrxDigest
    $exitCode = $ExitCode
    $coverage = $CoverageComplete
    $tests = $TestsPassed
    if ($null -ne $Item) {
        if ($Item.PSObject.Properties.Name -contains 'chunk') { $chunkId = [string]$Item.chunk }
        if ($Item.PSObject.Properties.Name -contains 'runId') { $run = [string]$Item.runId }
        if ($Item.PSObject.Properties.Name -contains 'attempt') { $attempt = [string]$Item.attempt }
        if ($Item.PSObject.Properties.Name -contains 'suite') { $suite = [string]$Item.suite }
        if ($Item.PSObject.Properties.Name -contains 'hostWallSeconds' -and $null -ne $Item.hostWallSeconds) { $wall = [double]$Item.hostWallSeconds }
        if ($Item.PSObject.Properties.Name -contains 'trxMissing') { $missing = [bool]$Item.trxMissing }
        if ($Item.PSObject.Properties.Name -contains 'passedCount' -and $null -ne $Item.passedCount) { $passed = [int]$Item.passedCount }
        if ($Item.PSObject.Properties.Name -contains 'trx') { $trx = [string]$Item.trx }
        if ($Item.PSObject.Properties.Name -contains 'trxDigest') { $digest = [string]$Item.trxDigest }
        if ($Item.PSObject.Properties.Name -contains 'exitCode') { $exitCode = $Item.exitCode }
        if ($Item.PSObject.Properties.Name -contains 'coverageComplete') { $coverage = $Item.coverageComplete }
        if ($Item.PSObject.Properties.Name -contains 'testsPassed') { $tests = $Item.testsPassed }
        $priorOutcomes = New-Object System.Collections.Generic.List[object]
        if ($Item.PSObject.Properties.Name -contains 'outcomes' -and $null -ne $Item.outcomes) {
            foreach ($row in @($Item.outcomes)) {
                if ($null -eq $row) { continue }
                $priorOutcomes.Add([ordered]@{ testId = [string]$row.testId; outcome = [string]$row.outcome })
            }
        }
    }
    return [ordered]@{
        chunk = [string]$chunkId
        runId = [string]$run
        attempt = [string]$attempt
        suite = [string]$suite
        hostWallSeconds = $wall
        trxMissing = [bool]$missing
        manufacturedPassed = $false
        passedCount = $passed
        outcomes = @($priorOutcomes.ToArray())
        trx = [string]$trx
        trxDigest = [string]$digest
        exitCode = $exitCode
        coverageComplete = $coverage
        testsPassed = $tests
    }
}

function Write-TestPhaseExecutedManifest {
    param(
        $Verdict,
        [string]$LogRoot,
        [string]$TrxPath,
        [string]$Suite,
        [string]$Chunk,
        [string]$RunId,
        [string]$Attempt,
        [double]$HostWallSeconds = 0
    )
    $rows = @()
    $trxMissing = -not (Test-Path -LiteralPath $TrxPath)
    if (-not $trxMissing) { $rows = @(Read-TestPhaseTrxRows -Path $TrxPath) }
    $outcomes = New-Object System.Collections.Generic.List[object]
    $passed = 0
    foreach ($row in $rows) {
        $outcomes.Add([ordered]@{ testId = [string]$row.TestId; outcome = [string]$row.Outcome })
        if ([string]$row.Outcome -eq 'Passed') { $passed++ }
    }
    $digest = ''
    if (-not $trxMissing) { $digest = Get-NightlyFileSha256 -Path $TrxPath }
    $exitCode = $null
    $coverageComplete = $null
    $testsPassed = $null
    if ($null -ne $Verdict) {
        if ($Verdict.PSObject.Properties.Name -contains 'ExitCode') { $exitCode = $Verdict.ExitCode }
        if ($Verdict.PSObject.Properties.Name -contains 'coverageComplete') { $coverageComplete = [bool]$Verdict.coverageComplete }
        if ($Verdict.PSObject.Properties.Name -contains 'testsPassed') { $testsPassed = [bool]$Verdict.testsPassed }
    }
    $fresh = ConvertTo-TestPhaseExecutedChunk -Chunk $Chunk -RunId $RunId -Attempt $Attempt -Suite $Suite -HostWallSeconds $HostWallSeconds -TrxMissing $trxMissing -PassedCount $passed -Outcomes @($outcomes.ToArray()) -Trx (ConvertTo-TestPhaseRelativePath -LogRoot $LogRoot -Path $TrxPath) -TrxDigest $digest -ExitCode $exitCode -CoverageComplete $coverageComplete -TestsPassed $testsPassed
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
    $path = Join-Path $dir 'groups-executed.json'
    $kept = New-Object System.Collections.Generic.List[object]
    $topRun = [string]$RunId
    $topAttempt = [string]$Attempt
    $topSuite = [string]$Suite
    if (Test-Path -LiteralPath $path) {
        $existing = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        if ($existing.PSObject.Properties.Name -contains 'runId' -and -not [string]::IsNullOrWhiteSpace([string]$existing.runId)) { $topRun = [string]$existing.runId }
        if ($existing.PSObject.Properties.Name -contains 'attempt') { $topAttempt = [string]$existing.attempt }
        if ($existing.PSObject.Properties.Name -contains 'suite' -and -not [string]::IsNullOrWhiteSpace([string]$existing.suite)) { $topSuite = [string]$existing.suite }
        $prior = @()
        if ($existing.PSObject.Properties.Name -contains 'chunks' -and $null -ne $existing.chunks) { $prior = @($existing.chunks) }
        elseif ($existing.PSObject.Properties.Name -contains 'chunk') { $prior = @($existing) }
        foreach ($item in $prior) {
            if ($null -eq $item) { continue }
            $same = ([string]$item.chunk -eq [string]$Chunk) -and ([string]$item.suite -eq [string]$Suite)
            if ($same) { continue }
            $kept.Add((ConvertTo-TestPhaseExecutedChunk -Item $item))
        }
    }
    $kept.Add($fresh)
    $manifest = [ordered]@{
        version = 1
        runId = $topRun
        attempt = $topAttempt
        suite = $topSuite
        manufacturedPassed = $false
        chunks = @($kept.ToArray())
    }
    Write-TestPhaseTextAtomic -Path $path -Text (ConvertTo-TestPhaseJsonText -Value $manifest)
}

function Complete-TestPhaseLaunch {
    param(
        $Verdict,
        [string]$LogRoot,
        [string]$TrxPath,
        [string]$Suite,
        [string]$Chunk,
        [string]$RunId,
        [string]$Attempt,
        [double]$HostWallSeconds = 0
    )
    try {
        if ($script:TestPhaseSeams -and $script:TestPhaseSeams.Summary) {
            & $script:TestPhaseSeams.Summary
        }
        Write-TestPhaseExecutedManifest -Verdict $Verdict -LogRoot $LogRoot -TrxPath $TrxPath -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt -HostWallSeconds $HostWallSeconds
        $planned = Join-Path (Get-TestPhaseTimingDirectory -RunDirectory $LogRoot) 'groups-planned.json'
        if (Test-Path -LiteralPath $planned) {
            Invoke-TestPhaseRunSummary -RunDirectory $LogRoot -Top 50
        }
    } catch {
        Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
    }
    return $Verdict
}

function ConvertTo-TestPhaseSummaryRows {
    param($Planned, [string]$RunDirectory)
    $quality = New-Object System.Collections.Generic.List[string]
    $torn = 0
    $malformed = 0
    $shards = @()
    $timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    if (Test-Path -LiteralPath $timing) {
        $shards = @(Get-ChildItem -LiteralPath $timing -Filter 'cases-*.jsonl' -File -ErrorAction SilentlyContinue)
    }
    $completes = @{}
    $starts = @{}
    $fixtures = @()
    $seenComplete = @{}
    foreach ($shard in $shards) {
        $parsed = Read-TestPhaseJsonlFile -Path $shard.FullName
        $torn += [int]$parsed.Torn
        $malformed += [int]$parsed.Malformed
        $header = $null
        foreach ($rec in @($parsed.Records)) {
            if ([string]$rec.recordKind -eq 'header') { $header = $rec; break }
        }
        $stale = $false
        if ($null -ne $header) {
            if (-not [string]::IsNullOrWhiteSpace([string]$Planned.runId) -and [string]$header.runId -ne [string]$Planned.runId) { $stale = $true }
            if (-not [string]::IsNullOrWhiteSpace([string]$Planned.assembly) -and [string]$header.assembly -ne [string]$Planned.assembly) { $stale = $true }
        }
        if ($stale) {
            if (-not $quality.Contains('stale-input-not-joined')) { $quality.Add('stale-input-not-joined') }
            continue
        }
        foreach ($rec in @($parsed.Records)) {
            $kind = [string]$rec.recordKind
            if ($kind -eq 'fixture') { $fixtures += $rec; continue }
            $id = [string]$rec.testId
            if ([string]::IsNullOrWhiteSpace($id)) { continue }
            if ($kind -eq 'start') { $starts[$id] = $rec; continue }
            if ($kind -ne 'complete') { continue }
            if ($seenComplete.ContainsKey($id)) {
                $prior = $completes[$id]
                if ([string]$prior.Record.outcome -ne [string]$rec.outcome) {
                    $prior.Conflict = $true
                    if (-not $quality.Contains('conflicting-duplicate')) { $quality.Add('conflicting-duplicate') }
                }
            } else {
                $seenComplete[$id] = $true
                $completes[$id] = [pscustomobject]@{ Record = $rec; Header = $header; Conflict = $false }
            }
        }
    }
    if ($torn -gt 0 -and -not $quality.Contains('torn-tail')) { $quality.Add('torn-tail') }
    if ($malformed -gt 0 -and -not $quality.Contains('malformed-line')) { $quality.Add('malformed-line') }

    $trxGroups = @{}
    $executedPath = Join-Path $timing 'groups-executed.json'
    if (Test-Path -LiteralPath $executedPath) {
        $executed = [System.IO.File]::ReadAllText($executedPath) | ConvertFrom-Json
        foreach ($chunk in @($executed.chunks)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$chunk.trx)) {
                $trxGroups[[System.IO.Path]::GetFullPath((Join-Path $RunDirectory ([string]$chunk.trx)))] = [string]$chunk.chunk
            }
        }
    }
    $trxRows = @()
    $trxFiles = @(Get-ChildItem -LiteralPath $RunDirectory -Filter '*.trx' -File -ErrorAction SilentlyContinue)
    foreach ($trx in $trxFiles) {
        foreach ($row in @(Read-TestPhaseTrxRows -Path $trx.FullName)) {
            $group = ''
            if ($trxGroups.ContainsKey($trx.FullName)) { $group = [string]$trxGroups[$trx.FullName] }
            $row | Add-Member -NotePropertyName GroupId -NotePropertyValue $group
            $trxRows += $row
        }
    }
    $nativeMap = @{}
    foreach ($row in $trxRows) {
        $keys = @([string]$row.TestId)
        foreach ($native in @(Get-TestPhaseNativeIds -Text ([string]$row.StdOut))) { $keys += [string]$native }
        foreach ($key in $keys) {
            if ([string]::IsNullOrWhiteSpace($key)) { continue }
            if (-not $nativeMap.ContainsKey($key)) { $nativeMap[$key] = New-Object System.Collections.Generic.List[object] }
            $list = $nativeMap[$key]
            $already = $false
            foreach ($existing in $list) {
                if ([string]$existing.TestId -eq [string]$row.TestId -and [string]$existing.Path -eq [string]$row.Path) { $already = $true }
            }
            if (-not $already) { $list.Add($row) }
        }
    }

    $tests = @()
    $joinedTrx = @{}
    foreach ($id in @($completes.Keys)) {
        $bag = $completes[$id]
        $matches = @()
        if ($nativeMap.ContainsKey([string]$id)) { $matches = @($nativeMap[[string]$id].ToArray()) }
        $distinct = @{}
        foreach ($match in $matches) { $distinct[[string]$match.TestId + '|' + [string]$match.Path] = $match }
        $matchRows = @($distinct.Values)
        $joined = $matchRows.Count -eq 1 -and -not $bag.Conflict
        $trxOutcome = ''
        $phaseOutcome = ''
        $duration = $null
        if ($bag.Conflict) {
            $phaseOutcome = 'conflict'
            if (-not $quality.Contains('conflicting-duplicate')) { $quality.Add('conflicting-duplicate') }
        } else {
            $phaseOutcome = [string]$bag.Record.outcome
        }
        if ($matchRows.Count -gt 1) {
            $joined = $false
            if (-not $quality.Contains('ambiguous-id')) { $quality.Add('ambiguous-id') }
        } elseif ($matchRows.Count -eq 1) {
            $trxOutcome = [string]$matchRows[0].Outcome
            $duration = [string]$matchRows[0].Duration
            $joinedTrx[[string]$matchRows[0].TestId + '|' + [string]$matchRows[0].Path] = $true
        }
        $outcome = 'incomplete'
        $incomplete = $true
        if ($joined) {
            if ($trxOutcome -eq $phaseOutcome -and -not [string]::IsNullOrWhiteSpace($trxOutcome)) {
                $outcome = $trxOutcome
                $incomplete = $false
            } else {
                $outcome = 'incomplete'
                if (-not $quality.Contains('outcome-disagreement')) { $quality.Add('outcome-disagreement') }
            }
        } else {
            if (-not $quality.Contains('missing-terminal-remains-incomplete')) { $quality.Add('missing-terminal-remains-incomplete') }
        }
        $src = $bag.Record
        $ex = $src.exclusive
        $tests += [ordered]@{
            testId = [string]$id
            className = [string]$src.className
            method = [string]$src.method
            duration = $duration
            groupId = [string]$bag.Header.groupId
            outcome = $outcome
            trxOutcome = $trxOutcome
            phaseOutcome = $phaseOutcome
            joined = [bool]$joined
            incomplete = [bool]$incomplete
            db = $(if ($ex) { [long]$ex.db } else { 0 })
            host = $(if ($ex) { [long]$ex.host } else { 0 })
            process = $(if ($ex) { [long]$ex.process } else { 0 })
            fs = $(if ($ex) { [long]$ex.fs } else { 0 })
            network = $(if ($ex) { [long]$ex.network } else { 0 })
            fixture = $(if ($ex) { [long]$ex.fixture } else { 0 })
            overlapMs = $(if ($src.overlapMs) { [long]$src.overlapMs } else { 0 })
            remainderMs = $(if ($src.remainderMs) { [long]$src.remainderMs } else { 0 })
            droppedPhaseCount = $(if ($src.droppedPhaseCount) { [int]$src.droppedPhaseCount } else { 0 })
        }
    }
    foreach ($id in @($starts.Keys)) {
        if ($completes.ContainsKey([string]$id)) { continue }
        if (-not $quality.Contains('missing-terminal-remains-incomplete')) { $quality.Add('missing-terminal-remains-incomplete') }
        $tests += [ordered]@{
            testId = [string]$id
            className = ''
            method = ''
            duration = $null
            groupId = $null
            outcome = 'incomplete'
            trxOutcome = ''
            phaseOutcome = ''
            joined = $false
            incomplete = $true
            db = 0; host = 0; process = 0; fs = 0; network = 0; fixture = 0
            overlapMs = 0; remainderMs = 0; droppedPhaseCount = 0
        }
    }
    foreach ($row in $trxRows) {
        $key = [string]$row.TestId + '|' + [string]$row.Path
        if ($joinedTrx.ContainsKey($key)) { continue }
        $nativeHit = $false
        foreach ($native in @(Get-TestPhaseNativeIds -Text ([string]$row.StdOut))) {
            if ($completes.ContainsKey([string]$native)) { $nativeHit = $true }
        }
        if ($nativeHit) { continue }
        if ($completes.ContainsKey([string]$row.TestId)) { continue }
        $tests += [ordered]@{
            testId = [string]$row.TestId
            className = ''
            method = [string]$row.Name
            duration = [string]$row.Duration
            groupId = [string]$row.GroupId
            outcome = [string]$row.Outcome
            trxOutcome = [string]$row.Outcome
            phaseOutcome = ''
            joined = $false
            incomplete = $false
            db = 0; host = 0; process = 0; fs = 0; network = 0; fixture = 0
            overlapMs = 0; remainderMs = 0; droppedPhaseCount = 0
        }
    }
    $shared = @()
    foreach ($fix in $fixtures) {
        $ex = $fix.exclusive
        $shared += [ordered]@{
            fixtureId = [string]$fix.fixtureId
            db = $(if ($ex) { [long]$ex.db } else { 0 })
            host = $(if ($ex) { [long]$ex.host } else { 0 })
            process = $(if ($ex) { [long]$ex.process } else { 0 })
            fs = $(if ($ex) { [long]$ex.fs } else { 0 })
            network = $(if ($ex) { [long]$ex.network } else { 0 })
            fixture = $(if ($ex) { [long]$ex.fixture } else { 0 })
        }
    }
    $complete = $quality.Count -eq 0
    foreach ($test in $tests) {
        if ([bool]$test.incomplete) { $complete = $false }
    }
    return [pscustomobject]@{
        Quality = @($quality.ToArray())
        Torn = $torn
        Malformed = $malformed
        Tests = @($tests)
        Shared = @($shared)
        Complete = [bool]$complete
    }
}

function Write-TestPhaseSummaryFiles {
    param($Planned, $Report, [string]$RunDirectory, [int]$Top)
    $timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    if (-not (Test-Path -LiteralPath $timing)) {
        New-Item -ItemType Directory -Path $timing -Force | Out-Null
    }
    $orderedTests = @($Report.Tests | Sort-Object { [string]$_.testId })
    $summary = [ordered]@{
        version = 1
        complete = [bool]$Report.Complete
        quality = @($Report.Quality)
        tornLines = [int]$Report.Torn
        malformedLines = [int]$Report.Malformed
        tests = @($orderedTests)
        sharedFixtures = @($Report.Shared)
    }
    Write-TestPhaseTextAtomic -Path (Join-Path $timing 'summary.json') -Text (ConvertTo-TestPhaseJsonText -Value $summary)

    $headers = @('testId','duration','groupId','outcome','trxOutcome','phaseOutcome','joined','incomplete','db','host','process','fs','network','fixture','overlapMs','remainderMs','droppedPhaseCount')
    $lines = New-Object System.Collections.Generic.List[string]
    $headCells = @()
    foreach ($h in $headers) { $headCells += (ConvertTo-TestPhaseCsvCell -Value $h) }
    $lines.Add(($headCells -join ','))
    foreach ($test in $orderedTests) {
        $cells = @(
            (ConvertTo-TestPhaseCsvCell -Value $test.testId),
            (ConvertTo-TestPhaseCsvCell -Value $test.duration),
            (ConvertTo-TestPhaseCsvCell -Value $test.groupId),
            (ConvertTo-TestPhaseCsvCell -Value $test.outcome),
            (ConvertTo-TestPhaseCsvCell -Value $test.trxOutcome),
            (ConvertTo-TestPhaseCsvCell -Value $test.phaseOutcome),
            (ConvertTo-TestPhaseCsvCell -Value $test.joined),
            (ConvertTo-TestPhaseCsvCell -Value $test.incomplete),
            (ConvertTo-TestPhaseCsvCell -Value $test.db),
            (ConvertTo-TestPhaseCsvCell -Value $test.host),
            (ConvertTo-TestPhaseCsvCell -Value $test.process),
            (ConvertTo-TestPhaseCsvCell -Value $test.fs),
            (ConvertTo-TestPhaseCsvCell -Value $test.network),
            (ConvertTo-TestPhaseCsvCell -Value $test.fixture),
            (ConvertTo-TestPhaseCsvCell -Value $test.overlapMs),
            (ConvertTo-TestPhaseCsvCell -Value $test.remainderMs),
            (ConvertTo-TestPhaseCsvCell -Value $test.droppedPhaseCount)
        )
        $lines.Add(($cells -join ','))
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllLines((Join-Path $timing 'tests.csv'), $lines.ToArray(), $utf8)

    $limit = $Top
    if ($limit -lt 1) { $limit = 1 }
    $ranked = @($orderedTests | Sort-Object { -1 * ([long]$_.db + [long]$_.host + [long]$_.process + [long]$_.fs + [long]$_.network + [long]$_.fixture + [long]$_.overlapMs + [long]$_.remainderMs) })
    $md = New-Object System.Collections.Generic.List[string]
    $md.Add('# Phase timing')
    $md.Add('')
    $md.Add('TRX remains authoritative. Sidecars are private and are not gate inputs.')
    $md.Add('')
    $md.Add('| test | outcome | exclusive+overlap+remainder |')
    $md.Add('| --- | --- | --- |')
    $shown = 0
    foreach ($test in $ranked) {
        if ($shown -ge $limit) { break }
        $shown++
        $total = [long]$test.db + [long]$test.host + [long]$test.process + [long]$test.fs + [long]$test.network + [long]$test.fixture + [long]$test.overlapMs + [long]$test.remainderMs
        $md.Add('| ' + (ConvertTo-TestPhaseMarkdownCell -Value $test.testId) + ' | ' + (ConvertTo-TestPhaseMarkdownCell -Value $test.outcome) + ' | ' + (ConvertTo-TestPhaseMarkdownCell -Value $total) + ' |')
    }
    $md.Add('')
    $md.Add('Shared fixture rows are separate from per-test totals.')
    [System.IO.File]::WriteAllLines((Join-Path $timing 'summary.md'), $md.ToArray(), $utf8)
}

function Invoke-TestPhaseRunSummary {
    param([string]$RunDirectory, [int]$Top = 50)
    $plannedPath = Join-Path (Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory) 'groups-planned.json'
    if (-not (Test-Path -LiteralPath $plannedPath)) { return }
    try {
        $planned = [System.IO.File]::ReadAllText($plannedPath) | ConvertFrom-Json
        $report = ConvertTo-TestPhaseSummaryRows -Planned $planned -RunDirectory $RunDirectory
        Write-TestPhaseSummaryFiles -Planned $planned -Report $report -RunDirectory $RunDirectory -Top $Top
    } catch {
        Write-TestPhaseFailureMarker -RunDirectory $RunDirectory -Code 'phase-summary-failed'
    }
}

function Resolve-QualificationTimingRoot {
    param([string]$TimingRoot, [string]$LogRoot, [string]$RunId)
    if (-not [string]::IsNullOrWhiteSpace($TimingRoot)) { return [string]$TimingRoot }
    if ([string]::IsNullOrWhiteSpace($LogRoot)) { return '' }
    if (-not [string]::IsNullOrWhiteSpace($RunId)) { return (Join-Path $LogRoot ('timing-' + $RunId)) }
    return (Join-Path $LogRoot 'timing')
}

function Get-QualificationNearestRank {
    param($Sorted, [double]$Fraction)
    $values = @($Sorted)
    $n = $values.Count
    if ($n -lt 1) { return $null }
    $rank = [int][math]::Ceiling($Fraction * $n)
    if ($rank -lt 1) { $rank = 1 }
    if ($rank -gt $n) { $rank = $n }
    return [math]::Round([double]$values[$rank - 1], 3)
}

function Get-QualificationTrxStats {
    param([string]$TrxPath)
    if ([string]::IsNullOrWhiteSpace($TrxPath) -or -not (Test-Path -LiteralPath $TrxPath)) { return $null }
    $named = @()
    foreach ($row in @(Read-TestPhaseTrxRows -Path $TrxPath)) {
        $seconds = $null
        try { $seconds = ([TimeSpan][string]$row.Duration).TotalSeconds } catch { $seconds = $null }
        if ($null -eq $seconds) { continue }
        $named += [pscustomobject]@{ name = [string]$row.Name; seconds = [math]::Round([double]$seconds, 3) }
    }
    if ($named.Count -lt 1) { return $null }
    $sorted = @($named | ForEach-Object { [double]$_.seconds } | Sort-Object)
    $slow = @($named | Sort-Object { -1 * [double]$_.seconds }, { [string]$_.name } | Select-Object -First 20)
    $slowRows = @()
    foreach ($row in $slow) {
        $slowRows += [ordered]@{ name = [string]$row.name; seconds = [math]::Round([double]$row.seconds, 3) }
    }
    return [pscustomobject]@{
        P50 = (Get-QualificationNearestRank -Sorted $sorted -Fraction 0.50)
        P95 = (Get-QualificationNearestRank -Sorted $sorted -Fraction 0.95)
        Max = [math]::Round([double]$sorted[$sorted.Count - 1], 3)
        Slowest = @($slowRows)
        Count = $sorted.Count
    }
}

function Get-QualificationSpanSeconds {
    param($Phase)
    if ($null -eq $Phase) { return $null }
    $start = 0.0
    $end = 0.0
    try { $start = [double]$Phase.startOffsetMs } catch { return $null }
    try { $end = [double]$Phase.endOffsetMs } catch { $end = $start }
    if ($end -lt $start) { $end = $start }
    return (($end - $start) / 1000.0)
}

function ConvertTo-QualificationShardContribution {
    param(
        $Records,
        [string]$FileName,
        [string]$Chunk,
        [string]$RunId,
        [string]$Suite,
        [string]$Attempt
    )
    $empty = [pscustomobject]@{ fixtures = @{}; processSpawn = $null; processWait = $null }
    $header = $null
    foreach ($rec in @($Records)) {
        if ([string]$rec.recordKind -eq 'header') { $header = $rec; break }
    }
    if ($null -eq $header) { return $empty }
    if ([string]$header.groupId -ne [string]$Chunk) { return $empty }
    if ([string]$header.runId -ne [string]$RunId) { return $empty }
    if ([string]$header.suite -ne [string]$Suite) { return $empty }
    if ([string]$header.attempt -ne [string]$Attempt) { return $empty }
    $filePid = ''
    $fileNonce = ''
    if ($FileName -match '^cases-(\d+)-([^.]+)\.jsonl$') {
        $filePid = [string]$Matches[1]
        $fileNonce = [string]$Matches[2]
    } else {
        return $empty
    }
    $headerPid = ''
    if ($header.PSObject.Properties.Name -contains 'pid' -and $null -ne $header.pid -and -not [string]::IsNullOrWhiteSpace([string]$header.pid)) {
        $headerPid = [string]$header.pid
    }
    $headerNonce = ''
    if ($header.PSObject.Properties.Name -contains 'nonce' -and $null -ne $header.nonce -and -not [string]::IsNullOrWhiteSpace([string]$header.nonce)) {
        $headerNonce = [string]$header.nonce
    }
    if ($headerPid -ne '' -and $headerPid -ne $filePid) { return $empty }
    if ($headerNonce -ne '' -and $headerNonce -ne $fileNonce) { return $empty }
    $fixtures = @{}
    $processSpawn = $null
    $processWait = $null
    foreach ($rec in @($Records)) {
        $kind = [string]$rec.recordKind
        if ($kind -eq 'fixture') {
            $fid = ''
            if ($rec.PSObject.Properties.Name -contains 'fixtureId') { $fid = [string]$rec.fixtureId }
            if ([string]::IsNullOrWhiteSpace($fid)) { $fid = [guid]::NewGuid().ToString('N') }
            if ($fixtures.ContainsKey($fid)) { continue }
            $bucket = @{ setup = $null; teardown = $null }
            foreach ($phase in @($rec.phases)) {
                $seconds = Get-QualificationSpanSeconds -Phase $phase
                if ($null -eq $seconds) { continue }
                $name = [string]$phase.phase
                if ($name -eq 'fixture-setup') {
                    if ($null -eq $bucket.setup) { $bucket.setup = 0.0 }
                    $bucket.setup = [double]$bucket.setup + [double]$seconds
                } elseif ($name -eq 'fixture-teardown') {
                    if ($null -eq $bucket.teardown) { $bucket.teardown = 0.0 }
                    $bucket.teardown = [double]$bucket.teardown + [double]$seconds
                }
            }
            $fixtures[$fid] = $bucket
        } elseif ($kind -eq 'complete') {
            foreach ($phase in @($rec.phases)) {
                $seconds = Get-QualificationSpanSeconds -Phase $phase
                if ($null -eq $seconds) { continue }
                $name = [string]$phase.phase
                if ($name -eq 'process-spawn') {
                    if ($null -eq $processSpawn) { $processSpawn = 0.0 }
                    $processSpawn = [double]$processSpawn + [double]$seconds
                } elseif ($name -eq 'process-wait') {
                    if ($null -eq $processWait) { $processWait = 0.0 }
                    $processWait = [double]$processWait + [double]$seconds
                }
            }
        }
    }
    return [pscustomobject]@{ fixtures = $fixtures; processSpawn = $processSpawn; processWait = $processWait }
}

function Get-QualificationPhaseSums {
    param(
        [string]$TimingRoot,
        [string]$Chunk,
        [string]$RunId = '',
        [string]$Suite = '',
        [string]$Attempt = ''
    )
    $empty = [pscustomobject]@{
        fixtureSetupSeconds = $null
        fixtureTeardownSeconds = $null
        processSpawnSeconds = $null
        processWaitSeconds = $null
    }
    if ([string]::IsNullOrWhiteSpace($TimingRoot) -or -not (Test-Path -LiteralPath $TimingRoot)) { return $empty }
    if (-not $script:QualificationPhaseShardCache) { $script:QualificationPhaseShardCache = @{} }
    $budget = [Diagnostics.Stopwatch]::StartNew()
    $seenFixtures = @{}
    $fixtureSetup = $null
    $fixtureTeardown = $null
    $processSpawn = $null
    $processWait = $null
    foreach ($shard in @(Get-ChildItem -LiteralPath $TimingRoot -Filter 'cases-*.jsonl' -File -ErrorAction SilentlyContinue)) {
        if ($budget.ElapsedMilliseconds -ge 500) { break }
        $identity = ([string]$RunId + '|' + [string]$Suite + '|' + [string]$Attempt + '|' + [string]$Chunk)
        $key = ([string]$shard.FullName + '|' + [string]$shard.Length + '|' + [string]$shard.LastWriteTimeUtc.Ticks + '|' + $identity)
        $contribution = $null
        if ($script:QualificationPhaseShardCache.ContainsKey($key)) {
            $contribution = $script:QualificationPhaseShardCache[$key]
        } else {
            $left = 500 - [int]$budget.ElapsedMilliseconds
            if ($left -lt 1) { break }
            $slice = $left
            if ($slice -gt 200) { $slice = 200 }
            $parsed = Read-TestPhaseJsonlBounded -Path $shard.FullName -MaxBytes 262144 -MaxLines 4000 -DeadlineMilliseconds $slice
            $contribution = ConvertTo-QualificationShardContribution -Records $parsed.Records -FileName $shard.Name -Chunk $Chunk -RunId $RunId -Suite $Suite -Attempt $Attempt
            if ($parsed.Complete) { $script:QualificationPhaseShardCache[$key] = $contribution }
        }
        if ($null -eq $contribution) { continue }
        foreach ($fid in @($contribution.fixtures.Keys)) {
            if ($seenFixtures.ContainsKey([string]$fid)) { continue }
            $seenFixtures[[string]$fid] = $true
            $bucket = $contribution.fixtures[$fid]
            if ($null -ne $bucket.setup) {
                if ($null -eq $fixtureSetup) { $fixtureSetup = 0.0 }
                $fixtureSetup = [double]$fixtureSetup + [double]$bucket.setup
            }
            if ($null -ne $bucket.teardown) {
                if ($null -eq $fixtureTeardown) { $fixtureTeardown = 0.0 }
                $fixtureTeardown = [double]$fixtureTeardown + [double]$bucket.teardown
            }
        }
        if ($null -ne $contribution.processSpawn) {
            if ($null -eq $processSpawn) { $processSpawn = 0.0 }
            $processSpawn = [double]$processSpawn + [double]$contribution.processSpawn
        }
        if ($null -ne $contribution.processWait) {
            if ($null -eq $processWait) { $processWait = 0.0 }
            $processWait = [double]$processWait + [double]$contribution.processWait
        }
    }
    if ($null -ne $fixtureSetup) { $empty.fixtureSetupSeconds = [math]::Round([double]$fixtureSetup, 3) }
    if ($null -ne $fixtureTeardown) { $empty.fixtureTeardownSeconds = [math]::Round([double]$fixtureTeardown, 3) }
    if ($null -ne $processSpawn) { $empty.processSpawnSeconds = [math]::Round([double]$processSpawn, 3) }
    if ($null -ne $processWait) { $empty.processWaitSeconds = [math]::Round([double]$processWait, 3) }
    return $empty
}

function ConvertTo-QualificationStampText {
    param($Value)
    if ($null -eq $Value) { return '' }
    if ($Value -is [datetime]) { return ([datetime]$Value).ToUniversalTime().ToString('o') }
    return [string]$Value
}

function ConvertFrom-QualificationJToken {
    param($Token)
    if ($null -eq $Token) { return $null }
    if ($Token -is [Newtonsoft.Json.Linq.JObject]) {
        $map = [ordered]@{}
        foreach ($prop in $Token.Properties()) {
            $map[[string]$prop.Name] = ConvertFrom-QualificationJToken -Token $prop.Value
        }
        return [pscustomobject]$map
    }
    if ($Token -is [Newtonsoft.Json.Linq.JArray]) {
        $items = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Token) { $items.Add((ConvertFrom-QualificationJToken -Token $item)) }
        return ,@($items.ToArray())
    }
    if ($Token -is [Newtonsoft.Json.Linq.JValue]) { return $Token.Value }
    return $null
}

function Read-QualificationSidecar {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return $null }
    $info = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $info -or [int64]$info.Length -gt 1048576) { return $null }
    $text = [IO.File]::ReadAllText($Path)
    if (-not ('Newtonsoft.Json.JsonConvert' -as [type])) {
        $dll = Join-Path $PSHome 'Newtonsoft.Json.dll'
        if (Test-Path -LiteralPath $dll) { Add-Type -Path $dll }
    }
    if ('Newtonsoft.Json.JsonConvert' -as [type]) {
        $settings = New-Object Newtonsoft.Json.JsonSerializerSettings
        $settings.DateParseHandling = [Newtonsoft.Json.DateParseHandling]::None
        $token = [Newtonsoft.Json.JsonConvert]::DeserializeObject($text, $settings)
        return (ConvertFrom-QualificationJToken -Token $token)
    }
    try { return ($text | ConvertFrom-Json) } catch { return $null }
}

function Update-QualificationChunkTiming {
    param(
        [string]$LogRoot,
        [string]$TimingRoot,
        [string]$Suite,
        [string]$Chunk,
        [string]$TrxPath,
        [string]$LastPhase,
        [string]$Why,
        $QueueWaitSeconds,
        $BuildSeconds,
        $DiscoverySeconds,
        $RunWallSeconds,
        [string]$StartedAt,
        [string]$CompletedAt,
        [string]$RunId = '',
        [string]$Attempt = '',
        [switch]$Terminal
    )
    try {
        if ([string]::IsNullOrWhiteSpace($LogRoot) -or [string]::IsNullOrWhiteSpace($Suite) -or [string]::IsNullOrWhiteSpace($Chunk)) { return }
        $path = Join-Path $LogRoot ('{0}-{1}.timing.json' -f $Suite, $Chunk)
        $prior = Read-QualificationSidecar -Path $path
        $bound = $PSBoundParameters
        $trx = ''
        if ($bound.ContainsKey('TrxPath')) { $trx = [string]$bound['TrxPath'] }
        elseif ($null -ne $prior -and $prior.PSObject.Properties.Name -contains 'trx') { $trx = [string]$prior.trx }
        $whyText = ''
        if ($bound.ContainsKey('Why')) { $whyText = [string]$bound['Why'] }
        elseif ($null -ne $prior -and $prior.PSObject.Properties.Name -contains 'why') { $whyText = [string]$prior.why }
        $phaseText = ''
        if ($bound.ContainsKey('LastPhase')) { $phaseText = [string]$bound['LastPhase'] }
        elseif ($null -ne $prior -and $prior.PSObject.Properties.Name -contains 'lastPhase') { $phaseText = [string]$prior.lastPhase }
        $startedText = ''
        if ($bound.ContainsKey('StartedAt')) { $startedText = ConvertTo-QualificationStampText $bound['StartedAt'] }
        elseif ($null -ne $prior -and $prior.PSObject.Properties.Name -contains 'startedAt') { $startedText = ConvertTo-QualificationStampText $prior.startedAt }
        $completedText = ''
        if ($bound.ContainsKey('CompletedAt')) { $completedText = ConvertTo-QualificationStampText $bound['CompletedAt'] }
        elseif ($null -ne $prior -and $prior.PSObject.Properties.Name -contains 'completedAt') { $completedText = ConvertTo-QualificationStampText $prior.completedAt }
        $queue = $null
        if ($bound.ContainsKey('QueueWaitSeconds')) { $queue = $QueueWaitSeconds }
        elseif ($null -ne $prior -and $prior.PSObject.Properties.Name -contains 'queueWaitSeconds') { $queue = $prior.queueWaitSeconds }
        $build = $null
        if ($bound.ContainsKey('BuildSeconds')) { $build = $BuildSeconds }
        elseif ($null -ne $prior -and $prior.PSObject.Properties.Name -contains 'buildSeconds') { $build = $prior.buildSeconds }
        $discovery = $null
        if ($bound.ContainsKey('DiscoverySeconds')) { $discovery = $DiscoverySeconds }
        elseif ($null -ne $prior -and $prior.PSObject.Properties.Name -contains 'discoverySeconds') { $discovery = $prior.discoverySeconds }
        $runWall = $null
        if ($bound.ContainsKey('RunWallSeconds')) { $runWall = $RunWallSeconds }
        elseif ($null -ne $prior -and $prior.PSObject.Properties.Name -contains 'runWallSeconds') { $runWall = $prior.runWallSeconds }
        $trxMissing = $true
        $stats = $null
        if (-not [string]::IsNullOrWhiteSpace($trx) -and (Test-Path -LiteralPath $trx)) {
            $trxMissing = $false
            $stats = Get-QualificationTrxStats -TrxPath $trx
        }
        $heartbeat = ($phaseText -eq 'run' -and [string]::IsNullOrWhiteSpace($whyText) -and [string]::IsNullOrWhiteSpace($completedText))
        if ($heartbeat) {
            $phases = [ordered]@{
                fixtureSetupSeconds = $null
                fixtureTeardownSeconds = $null
                processSpawnSeconds = $null
                processWaitSeconds = $null
            }
        } else {
            $phases = Get-QualificationPhaseSums -TimingRoot $TimingRoot -Chunk $Chunk -RunId $RunId -Suite $Suite -Attempt $Attempt
        }
        $p50 = $null; $p95 = $null; $max = $null; $slow = @(); $count = 0
        if ($null -ne $prior) {
            if ($prior.PSObject.Properties.Name -contains 'p50Seconds') { $p50 = $prior.p50Seconds }
            if ($prior.PSObject.Properties.Name -contains 'p95Seconds') { $p95 = $prior.p95Seconds }
            if ($prior.PSObject.Properties.Name -contains 'maxSeconds') { $max = $prior.maxSeconds }
            if ($prior.PSObject.Properties.Name -contains 'testCount' -and $null -ne $prior.testCount) { $count = [int]$prior.testCount }
            if ($prior.PSObject.Properties.Name -contains 'slowest' -and $null -ne $prior.slowest) { $slow = @($prior.slowest) }
        }
        if ($null -ne $stats) {
            $p50 = $stats.P50; $p95 = $stats.P95; $max = $stats.Max; $slow = @($stats.Slowest); $count = [int]$stats.Count
        }
        $record = [ordered]@{
            suite = [string]$Suite
            chunk = [string]$Chunk
            timingRoot = [string]$TimingRoot
            trx = $trx
            trxMissing = [bool]$trxMissing
            why = $whyText
            lastPhase = $phaseText
            lastPhaseUtc = (Get-NightlyUtcNow).ToString('o')
            startedAt = $startedText
            completedAt = $completedText
            queueWaitSeconds = $queue
            buildSeconds = $build
            buildSlotWaitSeconds = $null
            buildSlot = 'not-used'
            discoverySeconds = $discovery
            runWallSeconds = $runWall
            distillationSeconds = $null
            fixtureSetupSeconds = $phases.fixtureSetupSeconds
            fixtureTeardownSeconds = $phases.fixtureTeardownSeconds
            processSpawnSeconds = $phases.processSpawnSeconds
            processWaitSeconds = $phases.processWaitSeconds
            testCount = $count
            p50Seconds = $p50
            p95Seconds = $p95
            maxSeconds = $max
            slowest = @($slow)
        }
        Write-TestPhaseTextAtomic -Path $path -Text (ConvertTo-TestPhaseJsonText -Value $record)
        if (-not [string]::IsNullOrWhiteSpace($TimingRoot)) {
            $copy = Join-Path $TimingRoot ('{0}-{1}.timing.json' -f $Suite, $Chunk)
            if (([IO.Path]::GetFullPath($copy)) -ne ([IO.Path]::GetFullPath($path))) {
                Write-TestPhaseTextAtomic -Path $copy -Text (ConvertTo-TestPhaseJsonText -Value $record)
            }
        }
    } catch {
        # A timing sidecar must not change the suite verdict.
    }
}

function Get-QualificationTimingReport {
    param([string]$LogRoot, [string]$TimingRoot)
    $chunks = @()
    if (-not [string]::IsNullOrWhiteSpace($LogRoot) -and (Test-Path -LiteralPath $LogRoot)) {
        foreach ($file in @(Get-ChildItem -LiteralPath $LogRoot -Filter '*.timing.json' -File -ErrorAction SilentlyContinue)) {
            try {
                $row = Read-QualificationSidecar -Path $file.FullName
                if ($null -ne $row) { $chunks += $row }
            } catch { }
        }
    }
    $bySuite = @{}
    foreach ($row in $chunks) {
        $suite = [string]$row.suite
        if ([string]::IsNullOrWhiteSpace($suite)) { continue }
        if (-not $bySuite.ContainsKey($suite)) {
            $bySuite[$suite] = [ordered]@{ suite = $suite; startedAt = ''; completedAt = ''; lastPhase = ''; lastPhaseUtc = '' }
        }
        $bag = $bySuite[$suite]
        $start = [string]$row.startedAt
        if (-not [string]::IsNullOrWhiteSpace($start) -and ([string]::IsNullOrWhiteSpace([string]$bag.startedAt) -or $start -lt [string]$bag.startedAt)) {
            $bag.startedAt = $start
        }
        $end = [string]$row.completedAt
        if (-not [string]::IsNullOrWhiteSpace($end) -and $end -gt [string]$bag.completedAt) { $bag.completedAt = $end }
        $utc = [string]$row.lastPhaseUtc
        if (-not [string]::IsNullOrWhiteSpace($utc) -and $utc -gt [string]$bag.lastPhaseUtc) {
            $bag.lastPhaseUtc = $utc
            $bag.lastPhase = [string]$row.lastPhase
        }
    }
    $timeline = @()
    foreach ($key in @($bySuite.Keys | Sort-Object)) { $timeline += $bySuite[$key] }
    return [pscustomobject]@{
        TimingRoot = [string]$TimingRoot
        Chunks = @($chunks)
        Timeline = @($timeline)
    }
}
