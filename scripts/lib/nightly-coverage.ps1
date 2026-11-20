#requires -Version 5.1
# CARD-0487: TRX, diagnostic-discovery and coverage reconciliation.
# ASCII-only. Discovery is never execution evidence.

if ($script:AntiphonNightlyCoverageLoaded) { return }
$script:AntiphonNightlyCoverageLoaded = $true
# The core this scope already verified stays bound to its own loader (a pinned reader root has only some sources).
if (-not $script:NightlyCoreAvailable) { . (Join-Path $PSScriptRoot 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable }

# TUnit 1.44 GetClassTypeName: nested '+', generic backtick arity, constructor
# arguments, and a middle dot where a string argument contained '.'.
# The field ends at ", MethodName = " followed by MethodArity, so a comma
# inside the arguments cannot terminate the name early.
$script:NightlyDiagnosticTypeNameClass = '[A-Za-z0-9_.+\x60()<>,\[\] \u00B7-]'

# Per-chunk existence checks without the cmdlet (Test-Path costs about four File.Exists calls). These answer
# $true only where Test-Path -LiteralPath answers $true as well: a drive-letter path (never relative, so the
# PowerShell location cannot matter) that .NET finds on disk, in PowerShell 7 on Windows. Anything else is
# $false here and the caller asks the cmdlet in the same statement, which keeps its answers and its errors
# for a missing path, a file named with a trailing separator, an unreadable directory, a provider path.
$script:NightlyDrivePathChecks = ($PSVersionTable.PSVersion.Major -ge 7 -and [System.IO.Path]::DirectorySeparatorChar -eq '\')

function Test-NightlyDriveFile([string]$Path) {
    return ($script:NightlyDrivePathChecks -and $Path -match '^[A-Za-z]:[\\/]' -and [System.IO.File]::Exists($Path))
}

function Test-NightlyDriveDirectory([string]$Path) {
    return ($script:NightlyDrivePathChecks -and $Path -match '^[A-Za-z]:[\\/]' -and [System.IO.Directory]::Exists($Path))
}

function Test-NightlyDrivePath([string]$Path) {
    return ($script:NightlyDrivePathChecks -and $Path -match '^[A-Za-z]:[\\/]' -and
        ([System.IO.File]::Exists($Path) -or [System.IO.Directory]::Exists($Path)))
}

# LastWriteTimeUtc as Get-Item -LiteralPath reports it, for a plain file only; $null sends the caller to
# Get-Item (a directory, a hidden or system file that Get-Item refuses without -Force, a stream name).
function Get-NightlyDriveFileWriteTimeUtc([string]$Path) {
    # File.Exists, not FileInfo.Exists: a FileInfo drops a trailing separator that Get-Item refuses.
    if (-not (Test-NightlyDriveFile $Path) -or $Path.IndexOf(':', 2) -ge 0) { return $null }
    $info = [System.IO.FileInfo]::new($Path)
    if (([int]$info.Attributes -band 6) -ne 0) { return $null }
    return $info.LastWriteTimeUtc
}

# The PowerShell row loop stays: publish-release.ps1, promote-release.ps1 and the release selection worker read a
# TRX in processes that may have no core (Windows PowerShell 5.1, a script copy without the project).
function Read-NightlyTrxIdentities {
    param([string]$TrxPath)
    if (-not (Test-NightlyDrivePath $TrxPath) -and -not (Test-Path -LiteralPath $TrxPath)) {
        throw ('missing TRX {0}' -f $TrxPath)
    }
    if (Get-Command Get-TestPhasePathIdentity -ErrorAction SilentlyContinue) {
        $trxIdentity = Get-TestPhasePathIdentity -Path $TrxPath
        if ($trxIdentity.Kind -ne 'regular') {
            throw ('refused TRX {0}' -f $TrxPath)
        }
    }
    try {
        [xml]$doc = Get-Content -Raw -LiteralPath $TrxPath
    } catch {
        throw ('malformed TRX {0}: {1}' -f $TrxPath, $_.Exception.Message)
    }
    if (Test-NightlyCoreAvailable) {
        # Compiled row loop (Antiphon.Nightly/NightlyTrx.cs): the same rows and the same refusals.
        $compiled = [Antiphon.Nightly.NightlyTrx]::IdentityRows($doc)
        if ($compiled.Error) { throw $compiled.Error }
        return $compiled.Rows
    }
    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $nsm = $doc.DocumentElement.NamespaceURI
    if ($nsm) { $ns.AddNamespace('t', $nsm) }
    function Select-Ns([string]$xpath) {
        if ($nsm) { return $doc.SelectNodes($xpath, $ns) }
        return $doc.SelectNodes(($xpath -replace 't:', ''))
    }
    $definitions = @{}
    foreach ($unit in (Select-Ns '//t:TestDefinitions/t:UnitTest')) {
        $id = $unit.GetAttribute('id')
        if (-not $id) { continue }
        $method = $null
        foreach ($child in $unit.ChildNodes) {
            if ($child.LocalName -eq 'TestMethod') { $method = $child; break }
        }
        $className = $null
        $methodName = $null
        if ($method) {
            $className = $method.GetAttribute('className')
            $methodName = $method.GetAttribute('name')
        }
        $definitions[$id] = @{ ClassName = $className; MethodName = $methodName; Display = $unit.GetAttribute('name') }
    }
    $rows = New-Object System.Collections.Generic.List[object]
    $results = @(Select-Ns '//t:Results/t:UnitTestResult')
    if ($results.Count -eq 0) { $results = @(Select-Ns '//t:UnitTestResult') }
    $seenIds = @{}
    foreach ($n in $results) {
        $testId = $n.GetAttribute('testId')
        $outcome = $n.GetAttribute('outcome')
        $display = $n.GetAttribute('testName')
        $def = $null
        if ($testId -and $definitions.ContainsKey($testId)) { $def = $definitions[$testId] }
        if (-not $def) {
            throw ('missing definition for testId={0} display={1}' -f $testId, $display)
        }
        if ($seenIds.ContainsKey($testId)) {
            throw ('duplicate identity testId={0}' -f $testId)
        }
        $seenIds[$testId] = $true
        $className = [string]$def.ClassName
        $methodName = [string]$def.MethodName
        if ([string]::IsNullOrWhiteSpace($className) -or [string]::IsNullOrWhiteSpace($methodName)) {
            throw ('unresolved identity testId={0} display={1}' -f $testId, $display)
        }
        $debugTrace = ''
        foreach ($child in @($n.ChildNodes)) {
            if ($child.LocalName -cne 'Output') { continue }
            foreach ($outChild in @($child.ChildNodes)) {
                if ($outChild.LocalName -ceq 'DebugTrace') {
                    $debugTrace = [string]$outChild.InnerText
                    break
                }
            }
        }
        $rows.Add([pscustomobject]@{
            TestId = $testId
            Uid = ''
            ClassName = $className
            MethodName = $methodName
            Display = $display
            Outcome = $outcome
            Identity = ('{0}.{1}' -f $className, $methodName)
            DebugTrace = $debugTrace
        })
    }
    return $rows.ToArray()
}

function Get-NightlyAssemblyHash {
    param([string]$ExePath)
    $dll = [System.IO.Path]::ChangeExtension($ExePath, '.dll')
    if (Test-Path -LiteralPath $dll) { return (Get-NightlyAssemblyFileSha256 -Path $dll) }
    if (Test-Path -LiteralPath $ExePath) { return (Get-NightlyAssemblyFileSha256 -Path $ExePath) }
    return (Get-NightlySha256Text -Text ([string]$ExePath))
}

function Invoke-NightlyProduceDiscovery {
    param(
        [string]$ExePath,
        [string]$OutputPath,
        [string]$DiagnosticDirectory,
        [string]$LogPath,
        [string]$WorkingDirectory,
        [hashtable]$Environment = $null,
        [int]$TimeoutMilliseconds = 600000,
        [string[]]$ArgumentPrefix = @(),
        [string]$AssemblyPath = ''
    )
    if ([string]::IsNullOrWhiteSpace($ExePath)) { throw 'missing assembly exe' }
    if ([string]::IsNullOrWhiteSpace($OutputPath)) { throw 'missing discovery output' }
    if ([string]::IsNullOrWhiteSpace($DiagnosticDirectory)) { throw 'missing diagnostic directory' }
    New-Item -ItemType Directory -Path $DiagnosticDirectory -Force | Out-Null
    $launchArgs = @()
    if ($ArgumentPrefix) {
        foreach ($prefixArg in @($ArgumentPrefix)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$prefixArg)) { $launchArgs += [string]$prefixArg }
        }
    }
    $launchArgs += @(
        '--list-tests',
        '--no-progress',
        '--no-ansi',
        '--diagnostic',
        '--diagnostic-output-directory',
        $DiagnosticDirectory
    )
    $run = Invoke-NightlyOwnedProcess -FilePath $ExePath -ArgumentList $launchArgs `
        -WorkingDirectory $WorkingDirectory -TimeoutMilliseconds $TimeoutMilliseconds `
        -Environment $Environment -LogPath $LogPath
    if ([bool]$run.TimedOut) { throw 'discovery list-tests timed out' }
    $diagPath = [Antiphon.Nightly.NightlyDiagnosticLog]::Latest($DiagnosticDirectory)
    if ([string]::IsNullOrWhiteSpace($diagPath)) {
        throw ('missing diagnostic discovery log in {0}' -f $DiagnosticDirectory)
    }
    $parsed = [Antiphon.Nightly.NightlyDiagnosticLog]::Read($diagPath, 'discovery')
    $nodes = @($parsed.Nodes)
    if ($nodes.Count -eq 0) { throw 'empty discovery' }
    $hashSource = $ExePath
    if (-not [string]::IsNullOrWhiteSpace($AssemblyPath)) { $hashSource = $AssemblyPath }
    $hash = Get-NightlyAssemblyHash -ExePath $hashSource
    $doc = [Antiphon.Nightly.NightlyDiagnosticLog]::NewDiscoveryDocument($nodes, $hash)
    [Antiphon.Nightly.NightlyDiagnosticLog]::WriteDiscoveryDocument($OutputPath, $doc)
    return $doc
}

function Test-NightlyFreshEvidence {
    param(
        [string]$Path,
        [string]$RunDirectory,
        [datetime]$NotBeforeUtc,
        [string]$InvocationId
    )
    if (-not (Test-NightlyDrivePath $Path) -and -not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ Ok = $false; Reason = ('missing {0}' -f $Path) }
    }
    $full = ConvertTo-NightlyCanonicalPath -Path $Path
    $root = ConvertTo-NightlyCanonicalPath -Path $RunDirectory
    if (-not (Test-NightlyPathContained -Inner $full -Outer $root)) {
        return [pscustomobject]@{ Ok = $false; Reason = 'wrong-run-directory' }
    }
    $written = Get-NightlyDriveFileWriteTimeUtc $Path
    if ($null -eq $written) {
        $item = Get-Item -LiteralPath $Path
        $written = $item.LastWriteTimeUtc
    }
    if ($written -lt $NotBeforeUtc.AddSeconds(-1)) {
        return [pscustomobject]@{ Ok = $false; Reason = 'stale-timestamp' }
    }
    if (-not [string]::IsNullOrWhiteSpace($InvocationId)) {
        $raw = [System.IO.File]::ReadAllText($Path)
        if ($raw -notmatch [regex]::Escape($InvocationId)) {
            return [pscustomobject]@{ Ok = $false; Reason = 'previous-invocation' }
        }
    }
    return [pscustomobject]@{ Ok = $true; Reason = '' }
}

function Test-NightlyChunkMembership {
    param([string[]]$EligibleClasses, $Chunks)
    $assigned = @{}
    $dup = @()
    $missing = @()
    foreach ($chunk in @($Chunks)) {
        foreach ($c in @($chunk.classes)) {
            $name = [string]$c
            if ($assigned.ContainsKey($name)) { $dup += $name }
            else { $assigned[$name] = [string]$chunk.id }
        }
    }
    foreach ($c in @($EligibleClasses)) {
        if (-not $assigned.ContainsKey($c)) { $missing += $c }
    }
    return [pscustomobject]@{
        Ok = ($dup.Count -eq 0 -and $missing.Count -eq 0)
        Duplicates = $dup
        Missing = $missing
    }
}

function Get-NightlyExpectedSkipRoster {
    param([string]$Path = '')
    $useDefault = [string]::IsNullOrWhiteSpace($Path)
    if ($useDefault -and $null -ne $script:NightlyExpectedSkipRosterCache) {
        return @($script:NightlyExpectedSkipRosterCache)
    }
    if ($useDefault) {
        $repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $Path = Join-Path $repo (Join-Path 'tests' 'expected-platform-skips.json')
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw ('expected-skip roster missing {0}' -f $Path)
    }
    $doc = [System.IO.File]::ReadAllText($Path) | ConvertFrom-Json
    if ($null -eq $doc -or $null -eq $doc.entries) { throw 'expected-skip roster has no entries' }
    $entries = @($doc.entries)
    foreach ($entry in $entries) {
        $platformName = [string]$entry.platform
        $test = [string]$entry.test
        $prefix = [string]$entry.reasonPrefix
        if ($platformName -cnotin @('windows', 'linux')) { throw ('expected-skip roster platform {0}' -f $platformName) }
        if ([string]::IsNullOrWhiteSpace($test) -or $test.IndexOf('.') -lt 1) { throw ('expected-skip roster test {0}' -f $test) }
        if ([string]::IsNullOrWhiteSpace($prefix)) { throw ('expected-skip roster reason {0}' -f $test) }
        [void](Get-NightlyExpectedSkipReasonClass -Entry $entry)
    }
    if ($useDefault) { $script:NightlyExpectedSkipRosterCache = $entries }
    return $entries
}

function Get-NightlyExpectedSkipReasonClass {
    param($Entry)
    $value = ''
    if ($null -ne $Entry -and $Entry.PSObject.Properties.Name -contains 'reasonClass') {
        $value = [string]$Entry.reasonClass
    }
    if ([string]::IsNullOrWhiteSpace($value)) { return 'platform' }
    if ($value -cne 'platform' -and $value -cne 'live-context') {
        throw ('expected-skip roster reasonClass {0}' -f $value)
    }
    return $value
}

function Get-NightlyExpectedSkipRosterDigest {
    param($Entries)
    Assert-NightlyCoreAvailable 'Get-NightlyExpectedSkipRosterDigest'
    # Recomputed for every chunk verdict (NightlyCoverageParts.RosterDigest). Null: an entry's reasonClass is
    # invalid, and Get-NightlyExpectedSkipReasonClass raises the roster error for the first such entry.
    $digest = [Antiphon.Nightly.NightlyCoverageParts]::RosterDigest($Entries)
    if ($null -ne $digest) { return $digest }
    foreach ($entry in @($Entries)) { if ($null -ne $entry) { [void](Get-NightlyExpectedSkipReasonClass -Entry $entry) } }
    throw 'expected-skip roster digest refused'
}

function Get-NightlyExpectedSkipStaleWarnings {
    param([string]$Platform, $Entries, [string[]]$KnownTestIdentities = $null)
    if ($null -eq $KnownTestIdentities) { return @() }
    Assert-NightlyCoreAvailable 'Get-NightlyExpectedSkipStaleWarnings'
    # 'stale-roster:<test>' per platform entry (-ceq) whose test is not a known identity (ordinal), in roster
    # order (SuiteCompletionParts.StaleWarnings).
    return [Antiphon.Nightly.SuiteCompletionParts]::StaleWarnings($Platform, $Entries, $KnownTestIdentities)
}

function Get-NightlyDiscoveryKnownIdentities {
    param($Nodes)
    Assert-NightlyCoreAvailable 'Get-NightlyDiscoveryKnownIdentities'
    # ClassName.Method per node that has both as non-blank properties (SuiteCompletionParts.KnownIdentities).
    return [Antiphon.Nightly.SuiteCompletionParts]::KnownIdentities(@($Nodes))
}

function ConvertFrom-NightlyCsStringLiteral {
    param([string]$Raw)
    # The n, r, t, quote and backslash escapes fold to their characters; any other escaped character stands for
    # itself (NightlyCsMethodIndex.StringLiteral).
    if (-not $script:NightlyCoreAvailable) { Assert-NightlyCoreAvailable 'ConvertFrom-NightlyCsStringLiteral' }
    return [Antiphon.Nightly.NightlyCsMethodIndex]::StringLiteral($Raw)
}

function Initialize-NightlyCsMethodIndex {
    param([string]$RepoRoot)
    if ($script:NightlyCsMethodIndexRoot -eq $RepoRoot -and $null -ne $script:NightlyCsMethodIndex) {
        return $script:NightlyCsMethodIndex
    }
    $script:NightlyCsMethodIndexRoot = $RepoRoot
    # The compiled index (Antiphon.Nightly.NightlyCsMethodIndex), made by its first user.
    $script:NightlyCsMethodIndex = @{
        NativeDecided = $false
        Native = $null
    }
    return $script:NightlyCsMethodIndex
}

function Test-NightlyLiveClaudeRefusalFolds {
    param([string]$RepoRoot)
    if ($null -ne $script:NightlyLiveClaudeRefusalFolds -and $script:NightlyLiveClaudeRefusalRoot -eq $RepoRoot) {
        return [bool]$script:NightlyLiveClaudeRefusalFolds
    }
    $path = Join-Path $RepoRoot (Join-Path 'tests' (Join-Path 'Shared' (Join-Path 'LiveClaude' 'LiveClaudeLaunchContext.cs')))
    $folds = $false
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $source = [System.IO.File]::ReadAllText($path)
        if ($source.Contains('"20 " + reason')) { $folds = $true }
    }
    $script:NightlyLiveClaudeRefusalRoot = $RepoRoot
    $script:NightlyLiveClaudeRefusalFolds = $folds
    return $folds
}

function Get-NightlyReachableSkipTexts {
    param([string]$RepoRoot, [string]$Identity)
    $index = Initialize-NightlyCsMethodIndex -RepoRoot $RepoRoot
    $methodDot = $Identity.LastIndexOf('.')
    if ($methodDot -lt 1) { return @() }
    $method = $Identity.Substring($methodDot + 1)
    $head = $Identity.Substring(0, $methodDot)
    $classDot = $head.LastIndexOf('.')
    if ($classDot -lt 1) { return @() }
    $className = $head.Substring($classDot + 1)
    $namespace = $head.Substring(0, $classDot)
    # The compiled walk (NightlyCsIndex.cs): skip literals of the method and of what it calls, to depth 3. Same-class
    # helpers and gate calls (Require*, Resolve, CreateAsync) keep separate budgets (24 and 12) so a long test body
    # cannot crowd out the skip chain; other calls stay at 12. The tests path is made absolute as
    # Start-NightlyExpectedSkipScan makes it.
    if (-not $index.NativeDecided) {
        Assert-NightlyCoreAvailable 'Get-NightlyReachableSkipTexts'
        $index.NativeDecided = $true
        $index.Native = New-Object Antiphon.Nightly.NightlyCsMethodIndex -ArgumentList ([System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'tests')))
    }
    $foldNative = Test-NightlyLiveClaudeRefusalFolds -RepoRoot $RepoRoot
    return $index.Native.ReachableSkipTexts($className, $method, $namespace, $foldNative)
}

# The three end-of-run expected-skip scans (source gaps, reason gaps, roster drift) read only tests/ and the
# roster, never a chunk result. A nightly run starts them in the core (SkipScanTask.cs) as soon as the roster is
# loaded and passes the scan to each function at the end of the run (-Scan); the first one waits for it (the
# join). A stage that failed in the task rethrows its exception at its own function, in its own place. A caller
# that passes no scan (scripts/test-expected-platform-skips.ps1) runs the in-line body. Nothing is kept across
# runs; the scan object lives in the run's own variable.
function Start-NightlyExpectedSkipScan {
    param([string]$RepoRoot, [string]$Platform, $Entries)
    Assert-NightlyCoreAvailable 'Start-NightlyExpectedSkipScan'
    Wait-NightlyExpectedSkipScan -Task $script:NightlyExpectedSkipScanPending
    $testsRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'tests'))
    # The run's compiled method index (Get-NightlyReachableSkipTexts uses the same one), used by the task alone
    # until the join.
    $index = Initialize-NightlyCsMethodIndex -RepoRoot $RepoRoot
    if ($null -eq $index.Native) {
        $index.NativeDecided = $true
        $index.Native = New-Object Antiphon.Nightly.NightlyCsMethodIndex -ArgumentList $testsRoot
    }
    $platforms = [System.Collections.Generic.List[string]]::new()
    $tests = [System.Collections.Generic.List[string]]::new()
    $prefixes = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        $platforms.Add([string]$entry.platform)
        $tests.Add([string]$entry.test)
        $prefixes.Add([string]$entry.reasonPrefix)
    }
    $foldKnown = -1
    if ($null -ne $script:NightlyLiveClaudeRefusalFolds -and $script:NightlyLiveClaudeRefusalRoot -eq $RepoRoot) {
        $foldKnown = $(if ([bool]$script:NightlyLiveClaudeRefusalFolds) { 1 } else { 0 })
    }
    $foldPath = Join-Path $testsRoot (Join-Path 'Shared' (Join-Path 'LiveClaude' 'LiveClaudeLaunchContext.cs'))
    $driftPath = Join-Path $testsRoot (Join-Path 'Antiphon.Tests' (Join-Path 'Scripts' 'WindowsUnitSkipRoster.cs'))
    $task = [Antiphon.Nightly.NightlySkipScanTask]::Start($testsRoot, $driftPath, $foldPath, $foldKnown, $index.Native, $Platform, $platforms.ToArray(), $tests.ToArray(), $prefixes.ToArray())
    $script:NightlyExpectedSkipScanPending = $task
    return [pscustomobject]@{ Task = $task; RepoRoot = $RepoRoot; Platform = $Platform; Entries = $Entries }
}

# Waits for a started scan (it never throws: a stage's exception stays on the task until its function rethrows it).
function Wait-NightlyExpectedSkipScan {
    param($Task)
    if ($null -eq $Task) { return }
    $Task.Join()
    if ([object]::ReferenceEquals($script:NightlyExpectedSkipScanPending, $Task)) { $script:NightlyExpectedSkipScanPending = $null }
}

# The finished scan when it was started for these arguments, else $null (the caller runs its in-line body). Also
# waits for a scan an earlier run never joined, so no scan still uses the compiled method index when an in-line
# body does.
function Receive-NightlyExpectedSkipScan {
    param($Scan, [string]$RepoRoot, $Platform, $Entries, [switch]$AnyPlatform)
    Wait-NightlyExpectedSkipScan -Task $script:NightlyExpectedSkipScanPending
    if ($null -eq $Scan) { return $null }
    Wait-NightlyExpectedSkipScan -Task $Scan.Task
    if ([string]$Scan.RepoRoot -cne $RepoRoot) { return $null }
    if (-not $AnyPlatform -and [string]$Scan.Platform -cne [string]$Platform) { return $null }
    if (-not [object]::ReferenceEquals($Scan.Entries, $Entries)) { return $null }
    return $Scan.Task
}

function Get-NightlyExpectedSkipReasonGaps {
    # The named method's reachable skip text must equal reasonPrefix. Existence stays on Get-NightlyExpectedSkipSourceGaps.
    param([string]$RepoRoot, [string]$Platform, $Entries = $null, $Scan = $null)
    $scanned = Receive-NightlyExpectedSkipScan -Scan $Scan -RepoRoot $RepoRoot -Platform $Platform -Entries $Entries
    if ($null -ne $scanned) {
        if (-not $scanned.ReasonGapsDone) { throw $scanned.ReasonGapsError }
        return $scanned.ReasonGaps
    }
    if ($null -eq $Entries) { $Entries = @(Get-NightlyExpectedSkipRoster) }
    $gaps = New-Object System.Collections.Generic.List[string]
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        if ([string]$entry.platform -cne $Platform) { continue }
        $test = [string]$entry.test
        $prefix = [string]$entry.reasonPrefix
        $texts = @(Get-NightlyReachableSkipTexts -RepoRoot $RepoRoot -Identity $test)
        $found = $false
        foreach ($candidate in @($texts)) {
            if ([string]$candidate -ceq $prefix) { $found = $true; break }
        }
        if (-not $found) { [void]$gaps.Add($test) }
    }
    return $gaps.ToArray()
}

function Get-NightlyExpectedSkipRosterDrift {
    param([string]$RepoRoot, $Entries, $Scan = $null)
    $scanned = Receive-NightlyExpectedSkipScan -Scan $Scan -RepoRoot $RepoRoot -Entries $Entries -AnyPlatform
    if ($null -ne $scanned) {
        if (-not $scanned.DriftDone) { throw $scanned.DriftError }
        return [pscustomobject]@{
            Overlap = $scanned.DriftOverlap
            Mismatches = $scanned.DriftMismatches
        }
    }
    $path = Join-Path $RepoRoot (Join-Path 'tests' (Join-Path 'Antiphon.Tests' (Join-Path 'Scripts' 'WindowsUnitSkipRoster.cs')))
    $reasonsById = @{}
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $text = [System.IO.File]::ReadAllText($path)
        $constants = @{}
        $constRx = [regex]'const string ([A-Za-z_][A-Za-z0-9_]*) = "((?:\\.|[^"\\])*)"'
        foreach ($match in $constRx.Matches($text)) {
            $constants[$match.Groups[1].Value] = ConvertFrom-NightlyCsStringLiteral -Raw $match.Groups[2].Value
        }
        $newRx = [regex]'new\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*-?\d+\s*,\s*(?:([A-Za-z_][A-Za-z0-9_]*)|"((?:\\.|[^"\\])*)")'
        foreach ($match in $newRx.Matches($text)) {
            $id = $match.Groups[1].Value + '.' + $match.Groups[2].Value + '.' + $match.Groups[3].Value
            $reason = ''
            if ($match.Groups[4].Success -and -not [string]::IsNullOrWhiteSpace($match.Groups[4].Value)) {
                $token = $match.Groups[4].Value
                if ($constants.ContainsKey($token)) { $reason = [string]$constants[$token] }
            } elseif ($match.Groups[5].Success) {
                $reason = ConvertFrom-NightlyCsStringLiteral -Raw $match.Groups[5].Value
            }
            if (-not $reasonsById.ContainsKey($id)) {
                $reasonsById[$id] = [System.Collections.Generic.List[string]]::new()
            }
            [void]$reasonsById[$id].Add($reason)
        }
    }
    $overlap = New-Object System.Collections.Generic.List[string]
    $mismatches = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        $id = [string]$entry.test
        if ([string]::IsNullOrWhiteSpace($id) -or $seen.ContainsKey($id)) { continue }
        if (-not $reasonsById.ContainsKey($id)) { continue }
        $seen[$id] = $true
        [void]$overlap.Add($id)
        $prefix = [string]$entry.reasonPrefix
        $hit = $false
        foreach ($reason in $reasonsById[$id].ToArray()) {
            if ([string]$reason -ceq $prefix) { $hit = $true; break }
        }
        if (-not $hit) { [void]$mismatches.Add($id) }
    }
    return [pscustomobject]@{
        Overlap = $overlap.ToArray()
        Mismatches = $mismatches.ToArray()
    }
}

function Get-NightlyExpectedSkipSourceGaps {
    # A roster test whose class or method is absent from tests/. Not a coverage failure.
    param([string]$RepoRoot, [string]$Platform, $Entries = $null, $Scan = $null)
    $scanned = Receive-NightlyExpectedSkipScan -Scan $Scan -RepoRoot $RepoRoot -Platform $Platform -Entries $Entries
    if ($null -ne $scanned) {
        if (-not $scanned.SourceGapsDone) { throw $scanned.SourceGapsError }
        return @($scanned.SourceGaps)
    }
    if ($null -eq $Entries) { $Entries = @(Get-NightlyExpectedSkipRoster) }
    $wanted = @()
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        if ([string]$entry.platform -cne $Platform) { continue }
        $wanted += $entry
    }
    if ($wanted.Count -eq 0) { return @() }
    Assert-NightlyCoreAvailable 'Get-NightlyExpectedSkipSourceGaps'
    # The compiled scan (NightlyTestSources.cs) shares one racy-safe read of each source file with the reason-gap
    # walk. The tests path is made absolute as Start-NightlyExpectedSkipScan makes it.
    $testsRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'tests'))
    $tests = [string[]]@(foreach ($entry in $wanted) { [string]$entry.test })
    return @([Antiphon.Nightly.NightlyTestSources]::ExpectedSkipSourceGaps($testsRoot, $tests))
}

function Find-NightlyExpectedSkipEntry {
    param($Row, [string]$Platform, [object[]]$Entries)
    if ($null -eq $Row -or [string]::IsNullOrWhiteSpace($Platform)) { return $null }
    if (([Antiphon.Nightly.NightlyClassNames]::NormalizedOutcome(([string]$Row.Outcome))) -cne 'NotExecuted') { return $null }
    $trace = ''
    if ($Row.PSObject.Properties.Name -contains 'DebugTrace') { $trace = [string]$Row.DebugTrace }
    $identity = [string]$Row.Identity
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        if ([string]$entry.test -cne $identity) { continue }
        $reasonClass = Get-NightlyExpectedSkipReasonClass -Entry $entry
        $entryPlatform = [string]$entry.platform
        if ($entryPlatform -cne 'windows' -and $entryPlatform -cne 'linux') { continue }
        if ($reasonClass -ceq 'platform' -and $entryPlatform -cne $Platform) { continue }
        $expected = 'Skipped: ' + [string]$entry.reasonPrefix
        if ($trace -ceq $expected) { return $entry }
    }
    return $null
}

function Test-NightlyRowIsExpectedSkip {
    param($Row, [string]$Platform, [object[]]$Entries)
    return ($null -ne (Find-NightlyExpectedSkipEntry -Row $Row -Platform $Platform -Entries $Entries))
}

function ConvertTo-NightlyCoverageVerdict {
    param(
        [object[]]$TerminalRows,
        [int]$ProcessExit,
        [string]$Sha,
        [string]$ExpectedSha,
        [string]$GitRef,
        [string]$ExpectedRef,
        [string]$PolicyHash,
        [string]$ExpectedPolicyHash,
        [string[]]$RequiredSkips = @(),
        [object[]]$DiscoveryNodes = @(),
        [bool]$UsedDiscoveryAsExecution = $false,
        [string]$Platform = '',
        $ExpectedSkipRoster = $null,
        [string[]]$KnownTestIdentities = $null
    )
    $coverageComplete = $true
    $testsPassed = $true
    $reasons = @()
    if ($UsedDiscoveryAsExecution) {
        $coverageComplete = $false
        $reasons += 'discovery-as-execution'
    }
    $inProgress = @($DiscoveryNodes | Where-Object { $_.State -eq 'InProgress' })
    if ($inProgress.Count -gt 0 -and ($null -eq $TerminalRows -or $TerminalRows.Count -eq 0)) {
        $coverageComplete = $false
        $reasons += 'in-progress-only'
    }
    if ($null -eq $TerminalRows -or $TerminalRows.Count -eq 0) {
        $coverageComplete = $false
        $testsPassed = $false
        $reasons += 'zero-terminal-rows'
    }
    Assert-NightlyCoreAvailable 'ConvertTo-NightlyCoverageVerdict'
    # One compiled pass classifies every row once.
    $classified = [Antiphon.Nightly.NightlyVerdicts]::ClassifyOutcomes($TerminalRows)
    $failed = @($classified.Failed); $skipped = @($classified.Skipped)
    $unexecuted = @($classified.NotExecuted); $unknown = @($classified.Unknown)
    $passedCount = [int]$classified.Passed
    # Expected platform skips stay out of the unexecuted bucket. Outcome=Skipped is unchanged.
    $rosterEntries = @()
    if ($null -ne $ExpectedSkipRoster) { $rosterEntries = @($ExpectedSkipRoster) }
    elseif (-not [string]::IsNullOrWhiteSpace($Platform)) { $rosterEntries = @(Get-NightlyExpectedSkipRoster) }
    $expectedSkipRows = New-Object System.Collections.Generic.List[object]
    $stillUnexecuted = New-Object System.Collections.Generic.List[object]
    $toleratedSkips = New-Object System.Collections.Generic.List[object]
    foreach ($row in @($unexecuted)) {
        $matchedEntry = Find-NightlyExpectedSkipEntry -Row $row -Platform $Platform -Entries $rosterEntries
        if ($null -ne $matchedEntry) {
            [void]$expectedSkipRows.Add($row)
            [void]$toleratedSkips.Add([pscustomobject]@{
                identity = [string]$row.Identity
                reasonClass = (Get-NightlyExpectedSkipReasonClass -Entry $matchedEntry)
            })
        } else {
            [void]$stillUnexecuted.Add($row)
        }
    }
    $unexecuted = $stillUnexecuted.ToArray()
    $presentCount = [Antiphon.Nightly.NightlyCoverageParts]::PresentCount($TerminalRows)
    $allExpectedSkips = ($presentCount -gt 0 -and $expectedSkipRows.Count -eq $presentCount)
    $processExitTolerated = $false
    if ($failed.Count -gt 0) {
        $testsPassed = $false
        $reasons += 'failed-row'
    }
    if ($unexecuted.Count -gt 0) {
        $coverageComplete = $false
        $reasons += ('unexecuted-outcome:{0}' -f (($unexecuted | ForEach-Object { $_.Identity }) -join ','))
    }
    if ($unknown.Count -gt 0) {
        $coverageComplete = $false
        $testsPassed = $false
        $reasons += ('unknown-outcome:{0}' -f (($unknown | ForEach-Object { $_.Identity }) -join ','))
    }
    if ($ProcessExit -ne 0) {
        if ($ProcessExit -eq 8 -and $allExpectedSkips) { $processExitTolerated = $true }
        else {
            $testsPassed = $false
            $reasons += ('process-exit-{0}' -f $ProcessExit)
        }
    }
    if ($skipped.Count -gt 0) {
        $requiredHit = @($skipped | Where-Object {
            $id = [string]$_.Identity
            $RequiredSkips -contains $id -or $id -match 'Slow' -or $id -match 'broker'
        })
        if ($requiredHit.Count -gt 0 -or $skipped.Count -gt 0) {
            $coverageComplete = $false
            $reasons += ('skipped:{0}' -f (($skipped | ForEach-Object { $_.Identity }) -join ','))
        }
    }
    if (-not [string]::Equals([string]$Sha, [string]$ExpectedSha, [StringComparison]::OrdinalIgnoreCase)) {
        $coverageComplete = $false
        $reasons += ('sha-mismatch {0} vs {1}' -f $Sha, $ExpectedSha)
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedRef) -and
        -not [string]::Equals([string]$GitRef, [string]$ExpectedRef, [StringComparison]::OrdinalIgnoreCase)) {
        $coverageComplete = $false
        $reasons += ('ref-mismatch {0} vs {1}' -f $GitRef, $ExpectedRef)
    }
    if (-not [string]::Equals([string]$PolicyHash, [string]$ExpectedPolicyHash, [StringComparison]::OrdinalIgnoreCase)) {
        $coverageComplete = $false
        $reasons += ('policy-mismatch {0} vs {1}' -f $PolicyHash, $ExpectedPolicyHash)
    }
    $skipWarnings = @(Get-NightlyExpectedSkipStaleWarnings -Platform $Platform -Entries $rosterEntries -KnownTestIdentities $KnownTestIdentities)
    $rosterDigest = Get-NightlyExpectedSkipRosterDigest -Entries $rosterEntries
    return [pscustomobject]@{
        coverageComplete = [bool]$coverageComplete
        testsPassed = [bool]$testsPassed
        reasons = $reasons
        passed = $passedCount
        failed = $failed.Count
        skipped = $skipped.Count
        total = @($TerminalRows).Count
        skippedIdentities = @($skipped | ForEach-Object { $_.Identity })
        failedIdentities = @($failed | ForEach-Object { $_.Identity })
        expectedSkips = $expectedSkipRows.Count
        warnings = @($skipWarnings)
        processExitTolerated = [bool]$processExitTolerated
        toleratedSkips = @($toleratedSkips.ToArray())
        rosterDigest = [string]$rosterDigest
    }
}

function Test-NightlyCounterAgreement {
    param($Verdict, [int]$ReportedTotal, [int]$ReportedFailed)
    if ($ReportedTotal -ne $Verdict.total) { return $false }
    if ($ReportedFailed -ne $Verdict.failed) { return $false }
    return $true
}

function Test-NightlyExpandedRowsPresent {
    param([object[]]$DiscoveryNodes, [object[]]$TerminalRows, [string[]]$RequiredUids)
    # Required UIDs with no executed (Passed, Failed, Skipped) terminal row.
    Assert-NightlyCoreAvailable 'Test-NightlyExpandedRowsPresent'
    return [Antiphon.Nightly.NightlyVerdicts]::ExpandedRowsPresent($TerminalRows, $RequiredUids)
}

function Test-NightlyTrxDiagnosticCrossCheck {
    param([object[]]$TrxRows, [object[]]$TerminalNodes, [string[]]$RequiredClasses = @())
    # The TRX rows and the diagnostic terminal nodes of the required classes as identity bags: every count must agree.
    Assert-NightlyCoreAvailable 'Test-NightlyTrxDiagnosticCrossCheck'
    return [Antiphon.Nightly.NightlyVerdicts]::CrossCheck($TrxRows, $TerminalNodes, $RequiredClasses)
}

function Test-NightlySuiteUidUnion {
    param([object[]]$DiscoveryNodes, [object[]]$ChunkTerminalNodes)
    $required = @([Antiphon.Nightly.NightlyCensus]::RequiredUids($DiscoveryNodes, @(), $null, $false))
    return (Test-NightlyExpandedRowsPresent -DiscoveryNodes $DiscoveryNodes -TerminalRows $ChunkTerminalNodes -RequiredUids $required)
}

function Test-NightlyRequiredClassesPresent {
    param([object[]]$TerminalRows, [string[]]$RequiredClasses)
    if ($null -eq $RequiredClasses -or @($RequiredClasses).Count -eq 0) {
        return [pscustomobject]@{ Ok = $true; Missing = @() }
    }
    Assert-NightlyCoreAvailable 'Test-NightlyRequiredClassesPresent'
    return [Antiphon.Nightly.NightlyVerdicts]::RequiredClassesPresent($TerminalRows, $RequiredClasses)
}

function ConvertTo-NightlyNativeSuiteVerdict {
    param(
        [string]$TrxPath,
        [string]$DiscoveryPath = '',
        [string]$ExecutionDiagnosticPath = '',
        [string]$RunDirectory,
        [datetime]$NotBeforeUtc,
        [int]$ProcessExit,
        [string]$Sha,
        [string]$ExpectedSha,
        [string]$GitRef,
        [string]$ExpectedRef,
        [string]$PolicyHash,
        [string]$ExpectedPolicyHash,
        [string[]]$RequiredClasses = @(),
        $ProfileExclusions = $null,
        [switch]$ProfileAware,
        $DiscoveryIndex = $null,
        [string]$Platform = '',
        $ExpectedSkipRoster = $null
    )
    $reasons = @()
    if ([string]::IsNullOrWhiteSpace($TrxPath) -or (-not (Test-NightlyDrivePath $TrxPath) -and -not (Test-Path -LiteralPath $TrxPath))) {
        return [pscustomobject]@{
            coverageComplete = $false
            testsPassed = $false
            reasons = @('missing-trx')
            rows = @()
        }
    }
    if (Get-Command Get-TestPhasePathIdentity -ErrorAction SilentlyContinue) {
        $trxIdentity = Get-TestPhasePathIdentity -Path $TrxPath
        if ($trxIdentity.Kind -ne 'regular') {
            return [pscustomobject]@{
                coverageComplete = $false
                testsPassed = $false
                reasons = @('refused-trx')
                rows = @()
            }
        }
    }
    $fresh = Test-NightlyFreshEvidence -Path $TrxPath -RunDirectory $RunDirectory -NotBeforeUtc $NotBeforeUtc -InvocationId ''
    if (-not $fresh.Ok) {
        return [pscustomobject]@{
            coverageComplete = $false
            testsPassed = $false
            reasons = @($fresh.Reason)
            rows = @()
        }
    }
    $rows = @()
    try {
        $rows = @(Read-NightlyTrxIdentities -TrxPath $TrxPath)
    } catch {
        return [pscustomobject]@{
            coverageComplete = $false
            testsPassed = $false
            reasons = @($_.Exception.Message)
            rows = @()
        }
    }
    $discoveryNodes = @()
    $discoveryEntry = $null
    $discoveryView = $null
    $discoveryPresent = $false
    if ([string]::IsNullOrWhiteSpace($DiscoveryPath) -or (-not (Test-NightlyDrivePath $DiscoveryPath) -and -not (Test-Path -LiteralPath $DiscoveryPath))) {
        $reasons += 'missing-discovery'
    } else {
        try {
            if ($null -eq $DiscoveryIndex) { $DiscoveryIndex = [Antiphon.Nightly.NightlyDiscoveryIndex]::new() }
            $discoveryEntry = [Antiphon.Nightly.NightlyDiscoveryIndex]::EntryOf($DiscoveryIndex, $DiscoveryPath)
            $discoveryNodes = @($discoveryEntry.Document.Nodes)
            if ($discoveryNodes.Count -eq 0) {
                $reasons += 'empty-discovery'
            } else {
                $discoveryPresent = $true
            }
        } catch {
            return [pscustomobject]@{
                coverageComplete = $false
                testsPassed = $false
                reasons = @($_.Exception.Message)
                rows = $rows
                terminalNodes = @()
                discoveryNodes = @()
            }
        }
    }
    $terminalNodes = @()
    $execPresent = $false
    $execPath = $ExecutionDiagnosticPath
    # A directory .NET finds is a container and a file it finds is not; otherwise both cmdlet answers as before.
    if (-not [string]::IsNullOrWhiteSpace($execPath) -and ((Test-NightlyDriveDirectory $execPath) -or
            (-not (Test-NightlyDriveFile $execPath) -and (Test-Path -LiteralPath $execPath) -and (Test-Path -LiteralPath $execPath -PathType Container)))) {
        $execPath = [Antiphon.Nightly.NightlyDiagnosticLog]::Latest($execPath)
    }
    if ([string]::IsNullOrWhiteSpace($execPath) -or (-not (Test-NightlyDrivePath $execPath) -and -not (Test-Path -LiteralPath $execPath))) {
        if ($discoveryPresent) { $reasons += 'missing-execution-diagnostic' }
    } else {
        try {
            $exec = [Antiphon.Nightly.NightlyDiagnosticLog]::Read($execPath, 'execution')
            $terminalNodes = @($exec.Nodes)
            $execPresent = $true
        } catch {
            $reasons += $_.Exception.Message
        }
    }
    # The coverage verdict reads discovery only for its InProgress nodes.
    $inProgressNodes = @()
    if ($discoveryEntry) { $inProgressNodes = @($discoveryEntry.InProgress) }
    $verdict = ConvertTo-NightlyCoverageVerdict -TerminalRows $rows -ProcessExit $ProcessExit `
        -Sha $Sha -ExpectedSha $ExpectedSha -GitRef $GitRef -ExpectedRef $ExpectedRef `
        -PolicyHash $PolicyHash -ExpectedPolicyHash $ExpectedPolicyHash `
        -DiscoveryNodes $inProgressNodes -UsedDiscoveryAsExecution $false `
        -Platform $Platform -ExpectedSkipRoster $ExpectedSkipRoster
    $coverageComplete = [bool]$verdict.coverageComplete
    $testsPassed = [bool]$verdict.testsPassed
    foreach ($r in @($verdict.reasons)) { $reasons += $r }
    if (-not $discoveryPresent) {
        $coverageComplete = $false
    } else {
        $discoveryView = [Antiphon.Nightly.NightlyDiscoveryIndex]::View($discoveryEntry, $ProfileExclusions, [bool]($ProfileAware))
        $requiredUids = @([Antiphon.Nightly.NightlyDiscoveryIndex]::RequiredUids($discoveryView, $RequiredClasses))
        $censusCheck = $discoveryView.Census
        if ($censusCheck.Duplicates.Count -gt 0) {
            $coverageComplete = $false
            $reasons += ('duplicate-discovery-uid {0}' -f ($censusCheck.Duplicates -join ','))
        }
        if ($ProfileAware -and $censusCheck.RequiredCount -eq 0) {
            # D-5: a profile lane never reports green on an empty required set.
            $coverageComplete = $false
            $testsPassed = $false
            $reasons += 'zero-required-uid'
        }
        if (-not $execPresent) {
            $coverageComplete = $false
        } else {
            $expanded = Test-NightlyExpandedRowsPresent -DiscoveryNodes $discoveryNodes -TerminalRows $terminalNodes -RequiredUids $requiredUids
            if (-not $expanded.Ok) {
                $coverageComplete = $false
                $reasons += ('missing-expanded-row {0}' -f ($expanded.Missing -join ','))
            }
            $cross = Test-NightlyTrxDiagnosticCrossCheck -TrxRows $rows -TerminalNodes $terminalNodes -RequiredClasses $RequiredClasses
            if (-not $cross.Ok) {
                $coverageComplete = $false
                if ($cross.Mismatch.Count -gt 0) {
                    $reasons += ('trx-diagnostic-mismatch {0}' -f ($cross.Mismatch -join ','))
                }
                if ($cross.ExtraClasses.Count -gt 0) {
                    $reasons += ('extra-class-in-chunk {0}' -f ($cross.ExtraClasses -join ','))
                }
            }
        }
        $classReq = @()
        if ($RequiredClasses -and @($RequiredClasses).Count -gt 0) {
            foreach ($c in @($RequiredClasses)) { $classReq += [string]$c }
        } else {
            $classReq = @($discoveryView.Classes)
        }
        if ($classReq.Count -gt 0) {
            $member = Test-NightlyRequiredClassesPresent -TerminalRows $rows -RequiredClasses $classReq
            if (-not $member.Ok) {
                $coverageComplete = $false
                $testsPassed = $false
                $reasons += ('missing-required-class {0}' -f ($member.Missing -join ','))
            }
        }
    }
    $censusOut = $null
    if ($discoveryPresent) {
        $censusOut = $discoveryView.Census
    }
    return [pscustomobject]@{
        coverageComplete = $coverageComplete
        testsPassed = $testsPassed
        reasons = $reasons
        rows = $rows
        terminalNodes = $terminalNodes
        discoveryNodes = $discoveryNodes
        census = $censusOut
        processExitTolerated = [bool]$verdict.processExitTolerated
        warnings = @($verdict.warnings)
        toleratedSkips = @($verdict.toleratedSkips)
        rosterDigest = [string]$verdict.rosterDigest
    }
}

# CARD-1313: exact membership is a multiset contract over expanded UIDs.
function Test-NightlyExactCoverage {
    param([string[]]$RequiredUids, [object[]]$TerminalNodes)
    $required = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $reasons = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($uid in $RequiredUids) {
        if (-not $uid -or -not $required.Add($uid)) { [void]$reasons.Add('invalid-required') }
    }
    if ($required.Count -eq 0 -or @($TerminalNodes).Count -eq 0) { [void]$reasons.Add('zero-required') }
    foreach ($node in $TerminalNodes) {
        $uid = [string]$node.Uid
        if (-not $seen.Add($uid)) { [void]$reasons.Add('duplicate-uid') }
        if (-not $required.Contains($uid)) { [void]$reasons.Add('extra-uid') }
        $outcome = [Antiphon.Nightly.NightlyClassNames]::NormalizedOutcome(([string]$node.Outcome))
        if ($outcome -eq 'Skipped') { [void]$reasons.Add('required-skip') }
        elseif ($outcome -ne 'Passed') { [void]$reasons.Add('failed-outcome') }
    }
    foreach ($uid in $required) { if (-not $seen.Contains($uid)) { [void]$reasons.Add('missing-uid') } }
    return [pscustomobject]@{ Ok = ($reasons.Count -eq 0); Reasons = @($reasons); Required = $required.Count; Executed = @($TerminalNodes).Count }
}

function New-NightlyExactRoster {
    param($Discovery, [object[]]$Members, [string[]]$ClassifiedUids = $null)
    $parsed = [Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryDocument($Discovery, [Antiphon.Nightly.NightlyDiagnosticLog]::DiscoveryFormat)
    if (@($parsed.Nodes).Count -eq 0) { throw 'empty-discovery' }
    $selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($member in $Members) {
        $class = [Antiphon.Nightly.NightlyCensus]::BareClassIdentity(([string]$member.class))
        $method = [string]$member.method
        if ($class -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$' -or ($method -and $method -notmatch '^[A-Za-z_][A-Za-z0-9_]*$')) { throw 'unresolvable-exact-member' }
        $matches = @($parsed.Nodes | Where-Object { ([Antiphon.Nightly.NightlyCensus]::BareClassIdentity(([string]$_.ClassName))) -ceq $class -and (-not $method -or $_.Method -ceq $method) })
        if ($matches.Count -eq 0) { throw ('missing-exact-member:' + $class + '.' + $method) }
        foreach ($node in $matches) { [void]$selected.Add($node.Uid) }
    }
    $unresolved = @()
    if ($null -ne $ClassifiedUids) {
        $known = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($uid in $ClassifiedUids) { [void]$known.Add($uid) }
        $unresolved = @($parsed.Nodes | Where-Object { -not $known.Contains($_.Uid) } | ForEach-Object { $_.Uid })
        if ($unresolved.Count -gt 0) {
            # Widen the owning assembly before launch. Unknown resources cannot be run unattended.
            foreach ($node in $parsed.Nodes) { [void]$selected.Add($node.Uid) }
        }
    }
    if ($selected.Count -eq 0) { throw 'zero-required' }
    $groups = [Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($node in $parsed.Nodes) {
        if (-not $selected.Contains($node.Uid)) { continue }
        $bareType = [Antiphon.Nightly.NightlyCensus]::BareClassIdentity(([string]$node.Type))
        foreach ($part in @($node.Namespace, $bareType, $node.Method)) {
            if ($part -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$') { throw 'unresolvable-filter-identity' }
        }
        $filter = '/*/{0}/{1}/{2}' -f $node.Namespace, $bareType, $node.Method
        if (-not $groups.ContainsKey($filter)) { $groups.Add($filter, [Collections.Generic.List[string]]::new()) }
        $groups[$filter].Add($node.Uid)
    }
    $chunks = @()
    foreach ($filter in $groups.Keys) {
        $uids = [string[]]$groups[$filter].ToArray(); [Array]::Sort($uids, [StringComparer]::Ordinal)
        $chunks += [pscustomobject]@{ Filter = $filter; RequiredUids = $uids }
    }
    return [pscustomobject]@{ AssemblyHash = $parsed.AssemblyHash; Chunks = $chunks; RequiredUids = @($selected); Widened = ($unresolved.Count -gt 0); UnresolvedUids = $unresolved }
}
