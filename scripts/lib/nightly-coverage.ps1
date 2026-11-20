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
    if ($null -eq $Entries) { $Entries = @([Antiphon.Nightly.NightlyChunkVerdict]::SkipRoster((Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'tests/expected-platform-skips.json'))) }
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
    if ($null -eq $Entries) { $Entries = @([Antiphon.Nightly.NightlyChunkVerdict]::SkipRoster((Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'tests/expected-platform-skips.json'))) }
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

