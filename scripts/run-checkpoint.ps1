#requires -Version 7.0
<#
.SYNOPSIS
    CARD-0585 D-7: run ONE row of a plan's "### Checkpoints" table and print its report line.

    One isolated build (unless -NoBuild) plus one exact filter into a FRESH results directory,
    then the counters and the executed Class.Method roster parsed out of that run's own TRX, so
    no delegate re-derives TRX parsing or opens the file. It runs one row, never the table.

    Exit codes: 0 green; 1 one or more failed tests; 2 invalid input, a dirty tracked tree,
    a detected tracked change, a failed build, or no TRX; 3 fewer than -MinExecuted executed
    tests, or an -Expect token that matched no executed name; 4 no host build slot within
    -SlotWaitMinutes (nothing was built or run); Git failure, timeout or unavailable also refuse before proceeding.

    Build slot (CARD-0589): after input validation and the fresh results directory, the row takes a
    lease from the session runner's /build-slots broker (scripts/lib/build-slot.ps1), waits for it
    visibly in FIFO order (BUILD SLOT lines), builds with the grant's -maxcpucount:N and releases it
    after the run. An unreachable runner falls back to an unleased run at -maxcpucount:4 after 60 s
    and says so. -NoSlot is for an operator shell only; a delegate row never uses it.

    -MsBuildProperty Name=Value (CARD-0671) is forwarded as --property:Name=Value to BOTH the build
    and the `dotnet run`, so a -NoBuild row resolves the same output its build produced. Off
    Windows, UseAppHost=false is added unless the caller names UseAppHost itself: the extensionless
    fakeclaude apphost collides with the fakeclaude/ directory Antiphon.Tests stages beside it.

    A clean tracked baseline is captured before each real build. Changes are detected
    after success, failure, timeout and cancellation. The tool reports tampered paths,
    exits 2 with invalid evidence, and never changes worktree bytes or index entries.
    An entry the reader could not prove (CARD-1363: a hard link, an access refusal, a
    stalled read) is reported `refused: unreadable` apart from `tampered`, still exit 2.
    Build outputs and stamps require an exclusively created directory. Existing
    stamps and compiler-output directories are refused before the build.
    A relative -OutputPath is still
    rewritten to one absolute OutputPath under the entry project directory, and that same
    OutputPath, ArtifactsPath, and UseArtifactsOutput are used for the build, the dotnet
    run --no-build, and the build-source stamp. The rewrite and the static project-XML
    preflight only reduce noise. A shared BaseIntermediateOutputPath is not passed,
    because a global property does not expand $(MSBuildProjectName). Writes outside
    the repository, a row command string, and a -DotnetShim replacement are outside
    these repository observations.

    Owner: docs/testing-and-build.md, "Checkpoint manifest (CARD-0585)".
    ASCII-only.
#>
param(
    [Parameter(Mandatory = $true)] [string]$Name,
    [Parameter(Mandatory = $true)] [string]$Project,
    [Parameter(Mandatory = $true)] [string]$OutputPath,
    [Parameter(Mandatory = $true)] [string]$Filter,
    [string]$ResultsRoot = '.antiphon/checkpoints',
    [switch]$NoBuild,
    [int]$Repeat = 1,
    [int]$MinExecuted = 1,
    [string[]]$Expect,
    [string[]]$MsBuildProperty,
    [string]$ExpectedSourceSha,
    [string]$DotnetShim,
    [double]$SlotWaitMinutes = 45,
    [switch]$NoSlot,
    [string]$TimingRoot
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'checkpoint-source.ps1'))
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'checkpoint-repeat.ps1'))
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'checkpoint-owned-output.ps1'))
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'checkpoint-process-runner.ps1'))
$script:sourceStart = $null
$script:sourceEnd = $null
$script:buildSource = 'unknown'
$script:sourceReason = $null
$script:buildState = 'n/a'
$script:executed = 0
$script:passed = 0
$script:failed = 0
$script:skipped = 0
$script:trxPath = 'n/a'
$script:slotState = 'skipped'
$script:slotWaited = 0
$script:resultsDirectory = $null
$script:repeatEvidence = $null
$script:repeatReason = $null
$script:phaseTimings = $null
$script:buildSeconds = 0.0
$script:hostLaunch = [DateTimeOffset]::MinValue
$script:hostExit = [DateTimeOffset]::MinValue
$script:hostElapsedSeconds = -1.0

function Assert-CheckpointBoundary {
    param([string]$Boundary)
    $current = Get-CheckpointSource
    $script:sourceEnd = $current
    if ($current.captureStatus -cne 'known') { $script:sourceReason = 'source_unknown'; return $false }
    if ($script:sourceStart.captureStatus -cne 'known') { $script:sourceReason = 'source_unknown'; return $false }
    if ($current.commit -cne $script:sourceStart.commit -or $current.fingerprint -cne $script:sourceStart.fingerprint -or $current.dirtyFiles -ne $script:sourceStart.dirtyFiles) {
        $script:sourceReason = 'source_changed'
        return $false
    }
    return $true
}

function Find-CheckpointGitDir {
    param([string]$Start)
    $current = $Start
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        $candidate = Join-Path $current '.git'
        if (Test-Path -LiteralPath $candidate) { return $current }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) { break }
        $current = $parent
    }
    return $null
}

function Invoke-CheckpointGit {
    param([string]$WorkDir, [string[]]$GitArgs)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'git'
    $psi.WorkingDirectory = $WorkDir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    foreach ($arg in $GitArgs) { [void]$psi.ArgumentList.Add($arg) }
    foreach ($name in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE')) {
        if ($psi.Environment.ContainsKey($name)) { [void]$psi.Environment.Remove($name) }
    }
    # Raw bytes, then strict UTF-8 in this function. A loader that imports only
    # Invoke-CheckpointGit must still fail closed. CheckpointProcessRunner.Run follows the console code page.
    try { $result = [Antiphon.Checkpoints.CheckpointProcessRunner]::RunBytes($psi, 15000, $true, [Threading.CancellationToken]::None) }
    catch { return @{ Exit = 128; Stdout = ''; Stderr = ''; Reason = 'git-unavailable' } }
    if ($result.TimedOut) { return @{ Exit = 128; Stdout = ''; Stderr = ''; Reason = 'git-timeout' } }
    $stdout = ''
    try {
        # CARD-1608: -z paths are raw UTF-8. A replacement fallback would rename the path and
        # the content stamp would miss it. Invalid bytes fail closed. A leading U+FEFF is kept.
        $bytes = $null
        if ($null -ne $result.Stdout) { $bytes = [byte[]]$result.Stdout }
        if ($null -ne $bytes -and $bytes.Length -gt 0) {
            $stdout = [System.Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        }
    } catch {
        $inner = $_.Exception
        while ($null -ne $inner -and $inner -isnot [System.Text.DecoderFallbackException]) { $inner = $inner.InnerException }
        if ($inner -is [System.Text.DecoderFallbackException]) {
            # A real git failure keeps its own exit. Encoding is the reason only when git exited 0.
            if ([int]$result.Exit -ne 0) {
                return @{ Exit = [int]$result.Exit; Stdout = ''; Stderr = [string]$result.Stderr; Reason = $null }
            }
            return @{ Exit = 128; Stdout = ''; Stderr = [string]$result.Stderr; Reason = 'git-encoding' }
        }
        return @{ Exit = 128; Stdout = ''; Stderr = ''; Reason = 'git-unavailable' }
    }
    return @{ Exit = $result.Exit; Stdout = $stdout; Stderr = $result.Stderr; Reason = $null }
}

function Test-CheckpointComponentLink {
    param([string]$Path)
    try { $full = [System.IO.Path]::GetFullPath($Path) }
    catch { return 'path-unsafe' }
    $root = [System.IO.Path]::GetPathRoot($full)
    if ([string]::IsNullOrEmpty($root)) { return 'path-unsafe' }
    $current = $root
    $relative = $full.Substring($root.Length)
    foreach ($part in ($relative -split '[\\/]' | Where-Object { -not [string]::IsNullOrEmpty($_) })) {
        $current = [System.IO.Path]::Combine($current, $part)
        try { $attributes = [System.IO.File]::GetAttributes($current) } catch { break }
        if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return 'reparse-point' }
    }
    return $null
}

function Test-CheckpointStampRefusal {
    param([string]$StampPath)
    $linked = Test-CheckpointComponentLink $StampPath
    if ($null -ne $linked) { return $linked }
    $parent = Split-Path -Parent $StampPath
    if ([string]::IsNullOrWhiteSpace($parent)) { return 'path-unsafe' }
    $owner = Find-CheckpointGitDir $parent
    if ($null -eq $owner) { return $null }
    $top = Invoke-CheckpointGit $owner @('-C', $owner, 'rev-parse', '--show-toplevel')
    if ($top.Reason) { return [string]$top.Reason }
    if ([int]$top.Exit -ne 0) {
        if ([string]$top.Stderr -match 'not a git repository') { return 'not-a-repository' }
        return 'git-failed'
    }
    $toplevel = ([string]$top.Stdout).Trim()
    if ([string]::IsNullOrWhiteSpace($toplevel)) { return 'git-failed' }
    $listed = Invoke-CheckpointGit $toplevel @('-C', $toplevel, 'ls-files', '-z', '--', $StampPath)
    if ($listed.Reason) { return [string]$listed.Reason }
    if ([int]$listed.Exit -ne 0) {
        if ([string]$listed.Stderr -match 'not a git repository') { return 'not-a-repository' }
        return 'git-failed'
    }
    if (-not [string]::IsNullOrEmpty([string]$listed.Stdout)) { return 'tracked' }
    return $null
}

function Test-CheckpointPreflight {
    param([string]$Repository, [string[]]$Roots)
    if ($null -eq $Roots -or @($Roots).Count -eq 0) { return 'git-failed' }
    foreach ($one in @($Roots)) {
        $linked = Test-CheckpointComponentLink $one
        if ($null -ne $linked) { return $linked }
    }
    $start = $Repository
    if (-not [System.IO.Directory]::Exists($start)) {
        $parent = Split-Path -Parent $start
        if ([string]::IsNullOrWhiteSpace($parent)) { return 'path-unsafe' }
        $start = $parent
    }
    $owner = Find-CheckpointGitDir $start
    if ($null -eq $owner) { return $null }
    $fullRoots = @($Roots | ForEach-Object { [System.IO.Path]::GetFullPath($_).TrimEnd('\', '/') })
    $gitArgs = @('-C', $owner, 'ls-files', '-z', '--') + $fullRoots
    $listed = Invoke-CheckpointGit $owner $gitArgs
    if ($listed.Reason) { return [string]$listed.Reason }
    if ([int]$listed.Exit -ne 0) { return 'git-failed' }
    if (-not [string]::IsNullOrEmpty([string]$listed.Stdout)) { return 'tracked' }
    return $null
}

function Test-CheckpointWriteRefusal {
    param([string]$Path, [string]$Root)
    $linked = Test-CheckpointComponentLink $Path
    if ($null -ne $linked) { return $linked }
    try {
        $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
        $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    } catch { return 'path-unsafe' }
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $inside = [string]::Equals($full, $rootFull, $comparison) -or $full.StartsWith($rootFull + [System.IO.Path]::DirectorySeparatorChar, $comparison)
    if (-not $inside) { return 'outside-root' }
    return Test-CheckpointPreflight $rootFull @($full)
}

function Write-CheckpointSafeText {
    param([string]$Path, [string]$Content, [string]$Root)
    $reason = Test-CheckpointWriteRefusal $Path $Root
    if ($null -ne $reason) { return $reason }
    try { $full = [System.IO.Path]::GetFullPath($Path) } catch { return 'path-unsafe' }
    if ([System.IO.File]::Exists($full) -or [System.IO.Directory]::Exists($full)) { return 'unrecorded-existing' }
    $directory = [System.IO.Path]::GetDirectoryName($full)
    if (-not [string]::IsNullOrEmpty($directory) -and -not [System.IO.Directory]::Exists($directory)) {
        $dirReason = Test-CheckpointWriteRefusal $directory $Root
        if ($null -ne $dirReason) { return $dirReason }
        [void][System.IO.Directory]::CreateDirectory($directory)
    }
    $temporary = $full + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [System.IO.File]::WriteAllText($temporary, $Content)
        $again = Test-CheckpointWriteRefusal $full $Root
        if ($null -ne $again) { return $again }
        [System.IO.File]::Move($temporary, $full, $true)
        return $null
    } finally {
        if ([System.IO.File]::Exists($temporary)) {
            try { [System.IO.File]::Delete($temporary) } catch { }
        }
    }
}

function New-CheckpointDirectory {
    param([string]$Path, [string]$Root)
    $reason = Test-CheckpointWriteRefusal $Path $Root
    if ($null -ne $reason) { return $reason }
    if ([System.IO.Directory]::Exists([System.IO.Path]::GetFullPath($Path))) { return $null }
    [void][System.IO.Directory]::CreateDirectory($Path)
    return $null
}

function Open-CheckpointSafeCreate {
    param([string]$Path, [string]$Root)
    $reason = Test-CheckpointWriteRefusal $Path $Root
    if ($null -ne $reason) { throw "refused: $reason" }
    $full = [System.IO.Path]::GetFullPath($Path)
    if ([System.IO.File]::Exists($full) -or [System.IO.Directory]::Exists($full)) { throw 'refused: unrecorded-existing' }
    $directory = [System.IO.Path]::GetDirectoryName($full)
    if (-not [string]::IsNullOrEmpty($directory) -and -not [System.IO.Directory]::Exists($directory)) {
        $dirReason = Test-CheckpointWriteRefusal $directory $Root
        if ($null -ne $dirReason) { throw "refused: $dirReason" }
        [void][System.IO.Directory]::CreateDirectory($directory)
    }
    return [System.IO.FileStream]::new($full, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
}

function Test-CheckpointProjectExtension {
    param([string]$Extension)
    return [string]::Equals($Extension, '.csproj', [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($Extension, '.fsproj', [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($Extension, '.vbproj', [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-CheckpointProjectDirectory {
    param([string]$ProjectFull)
    if (Test-CheckpointProjectExtension ([System.IO.Path]::GetExtension($ProjectFull))) {
        $parent = [System.IO.Path]::GetDirectoryName($ProjectFull)
        if ([string]::IsNullOrEmpty($parent)) { return $ProjectFull }
        return $parent
    }
    return $ProjectFull
}

function Get-CheckpointOutputLayout {
    param([string]$Repo, [string]$ProjectPath, [string]$Output)
    $projectFull = if ([System.IO.Path]::IsPathRooted($ProjectPath)) { [System.IO.Path]::GetFullPath($ProjectPath) } else { [System.IO.Path]::GetFullPath((Join-Path $Repo $ProjectPath)) }
    $projectDir = Resolve-CheckpointProjectDirectory $projectFull
    if ([string]::IsNullOrEmpty($projectDir)) { $projectDir = $Repo }
    if ([System.IO.Path]::IsPathRooted($Output)) { $ownedFull = [System.IO.Path]::GetFullPath($Output) }
    else { $ownedFull = [System.IO.Path]::GetFullPath((Join-Path $projectDir $Output)) }
    $owned = ($ownedFull -replace '\\', '/')
    if (-not $owned.EndsWith('/')) { $owned += '/' }
    return @{ Owned = $owned; Artifacts = $owned.TrimEnd('/') + '/artifacts' }
}

function Find-CheckpointProjectFile {
    param([string]$ProjectFull)
    if ([System.IO.File]::Exists($ProjectFull)) {
        if (Test-CheckpointProjectExtension ([System.IO.Path]::GetExtension($ProjectFull))) { return $ProjectFull }
        return ''
    }
    if (-not [System.IO.Directory]::Exists($ProjectFull)) { return $null }
    $matched = New-Object System.Collections.Generic.List[string]
    try {
        foreach ($candidate in [System.IO.Directory]::GetFiles($ProjectFull)) {
            if (Test-CheckpointProjectExtension ([System.IO.Path]::GetExtension($candidate))) {
                [void]$matched.Add($candidate)
            }
        }
    } catch {
        return ''
    }
    if ($matched.Count -eq 0) { return $null }
    if ($matched.Count -ne 1) { return '' }
    return $matched[0]
}

# Defence in depth only: ProjectReference elements in this file. Imports and
# Directory.Build.props are outside this scan. Invoke-ConfinedCheckpointBuild
# evaluates that graph before a real dotnet build.
function Read-CheckpointProjectReferences {
    param([string]$ProjectFile)
    $document = New-Object System.Xml.XmlDocument
    $document.XmlResolver = $null
    $document.LoadXml((Read-CheckpointText $ProjectFile))
    $includes = New-Object System.Collections.Generic.List[string]
    foreach ($node in $document.GetElementsByTagName('ProjectReference')) {
        $include = ([string]$node.GetAttribute('Include')).Trim()
        if ([string]::IsNullOrWhiteSpace($include)) { return $null }
        if ($include.IndexOfAny(@([char]36, [char]42, [char]63)) -ge 0) { return $null }
        [void]$includes.Add($include)
    }
    return ,$includes.ToArray()
}

function Add-CheckpointWritePair {
    param($Directories, [string]$ProjectDirectory, [string]$Output)
    if ([System.IO.Path]::IsPathRooted($Output)) {
        $outputFull = [System.IO.Path]::GetFullPath($Output)
    } else {
        $outputFull = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($ProjectDirectory, $Output))
    }
    [void]$Directories.Add($outputFull)
    [void]$Directories.Add([System.IO.Path]::GetFullPath([System.IO.Path]::Combine($ProjectDirectory, 'obj')))
}

function Get-CheckpointWriteDirectories {
    param([string]$Repository, [string]$ProjectPath, [string]$Output)
    $projectFull = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Repository, $ProjectPath))
    $directories = New-Object System.Collections.Generic.List[string]
    $entry = Find-CheckpointProjectFile $projectFull
    if ($null -eq $entry) {
        Add-CheckpointWritePair $directories (Resolve-CheckpointProjectDirectory $projectFull) $Output
        return [pscustomobject]@{ Refusal = $null; Directories = $directories.ToArray() }
    }
    if ($entry -eq '') {
        return [pscustomobject]@{ Refusal = 'project-closure'; Directories = @() }
    }
    $comparer = if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
    $seen = [System.Collections.Generic.HashSet[string]]::new($comparer)
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($entry)
    while ($queue.Count -gt 0) {
        $current = [System.IO.Path]::GetFullPath($queue.Dequeue())
        if (-not $seen.Add($current)) { continue }
        $projectDir = Resolve-CheckpointProjectDirectory $current
        Add-CheckpointWritePair $directories $projectDir $Output
        try {
            $includes = Read-CheckpointProjectReferences $current
        } catch {
            return [pscustomobject]@{ Refusal = 'project-closure'; Directories = @() }
        }
        if ($null -eq $includes) {
            return [pscustomobject]@{ Refusal = 'project-closure'; Directories = @() }
        }
        foreach ($include in @($includes)) {
            $normalized = ([string]$include).Replace('\', [System.IO.Path]::DirectorySeparatorChar)
            $resolved = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($projectDir, $normalized))
            if (-not [System.IO.File]::Exists($resolved)) {
                return [pscustomobject]@{ Refusal = 'project-closure'; Directories = @() }
            }
            $queue.Enqueue($resolved)
        }
    }
    return [pscustomobject]@{ Refusal = $null; Directories = $directories.ToArray() }
}

function Remove-CheckpointCheckedFile {
    param([string]$Path)
    if ([System.IO.File]::Exists($Path)) { return 'pre-existing-build-source' }
    $directory = [System.IO.Path]::GetDirectoryName($Path)
    $reason = Test-CheckpointPreflight $root @($directory)
    if ($null -ne $reason) { return $reason }
    if ([System.IO.Directory]::Exists($directory) -or [System.IO.File]::Exists($directory)) { return 'pre-existing-output' }
    return $null
}

function Get-CheckpointBuildStampPath {
    param([string]$Root, [string]$ProjectPath, [string]$Output)
    $projectFull = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Root, $ProjectPath))
    $projectDir = Resolve-CheckpointProjectDirectory $projectFull
    return [System.IO.Path]::Combine($projectDir, $Output, 'checkpoint-build-source.json')
}

function Get-CheckpointBuildBinding {
    param([string]$Root, [string]$ProjectPath, [string]$Output, [string[]]$Properties, $Source)
    $projectFull = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Root, $ProjectPath))
    $projectDir = Resolve-CheckpointProjectDirectory $projectFull
    $outputFull = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($projectDir, $Output))
    return [pscustomobject]@{
        version = 1
        repositoryRoot = [System.IO.Path]::GetFullPath($Root)
        project = $projectFull
        outputPath = $outputFull
        properties = @($Properties | Sort-Object -CaseSensitive)
        commit = $Source.commit
        fingerprint = $Source.fingerprint
        sourceState = if ($Source.dirtyFiles -eq 0) { 'clean' } else { 'dirty' }
    }
}

function Test-CheckpointBuildBinding {
    param($Actual, $Expected)
    if ($null -eq $Actual -or $Actual.version -ne 1) { return $false }
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    foreach ($field in @('repositoryRoot', 'project', 'outputPath')) {
        if (-not [string]::Equals([string]$Actual.$field, [string]$Expected.$field, $comparison)) { return $false }
    }
    foreach ($field in @('commit', 'fingerprint', 'sourceState')) {
        if ($Actual.$field -cne $Expected.$field) { return $false }
    }
    if (@($Actual.properties).Count -ne @($Expected.properties).Count) { return $false }
    for ($i = 0; $i -lt @($Actual.properties).Count; $i++) {
        if ([string]$Actual.properties[$i] -cne [string]$Expected.properties[$i]) { return $false }
    }
    return $true
}

function Write-WrapperTiming {
    param([string]$Kind, [int]$Code = 0)
    $root = $TimingRoot
    if (-not $root) { $root = $env:ANTIPHON_TEST_TIMING_ROOT }
    if (-not $root) { return }
    try {
        . (Join-Path $PSScriptRoot (Join-Path 'lib' 'test-timings.ps1'))
        $stamp = [DateTimeOffset]::UtcNow.ToString('o')
        $outcome = if ($Kind -eq 'run-started') { 'started' } elseif ($Code -eq 0) { 'passed' } else { 'failed' }
        Write-TestTimingEvent -Root $root -Event ([ordered]@{
            schemaVersion = 'antiphon-test-timing-v1'
            eventId = ([guid]::NewGuid().ToString('N'))
            eventKind = $(if ($Kind -eq 'run-started') { 'run-started' } else { 'set-completed' })
            runId = 'wrapper'
            attempt = 1
            producer = 'wrapper'
            setKind = 'checkpoint-row'
            setId = $Name
            outcome = $outcome
            exitCode = $Code
            startedAt = $stamp
            endedAt = $stamp
            category = 'unavailable'
            timingPersistence = 'complete'
            durabilityClass = 'configured-root'
        })
    } catch { }
}

function Write-Trailer {
    param([int]$Code)
    if ($Code -eq 0 -and $null -ne $script:expectedBinding) {
        try {
            $presentStamp = Read-CheckpointText $script:stampPath | ConvertFrom-Json
            if (-not (Test-CheckpointBuildBinding $presentStamp $script:expectedBinding)) { throw 'stamp-mismatch' }
        } catch { Write-Host ('refused: checkpoint-build-source.json receipt-missing-or-mismatching'); $Code = 2; $script:buildSource = 'mismatch' }
    }
    $start = $script:sourceStart
    if ($null -eq $start) { $start = [pscustomobject]@{ commit = $null; dirtyFiles = $null; fingerprint = $null; observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); captureStatus = 'unknown'; errorCode = 'not_observed' } }
    $state = Get-CheckpointSourceState $start $script:sourceEnd
    if ($null -ne $script:sourceReason -and $state -eq 'clean') { $state = 'unknown' }
    $dirty = if ($null -eq $start.dirtyFiles) { 'unknown' } else { [string]$start.dirtyFiles }
    $commit = if ($start.captureStatus -eq 'known') { [string]$start.commit } else { 'unknown' }
    $line = ('CHECKPOINT {0} commit={1} build={2} filter={3} executed={4} passed={5} failed={6} skipped={7} trx={8} slot={9} waited={10}s dirty={11} source={12} sourceState={13} buildSource={14}' -f `
        $Name, $commit, $script:buildState, $Filter, $script:executed, $script:passed, $script:failed, $script:skipped, $script:trxPath, $script:slotState, $script:slotWaited, $dirty, (Get-CheckpointSourceToken $start), $state, $script:buildSource)
    if ($Repeat -gt 1) {
        $completed = if ($null -eq $script:repeatEvidence) { 0 } else { [int]$script:repeatEvidence.passed }
        $line += (' repeat={0} repetitions={1}/{0} hostInvocations=1' -f $Repeat, $completed)
    }
    if ($null -ne $script:sourceReason) { $line += ' reason=' + $script:sourceReason }
    Write-Host $line
    if ($null -ne $script:resultsDirectory) {
        $evidence = [pscustomobject]@{
            version = 1; name = $Name; start = $start; end = $script:sourceEnd
            state = $state; buildSource = $script:buildSource; reason = $script:sourceReason
            receipt = $line; exitCode = $Code; executed = $script:executed
            passed = $script:passed; failed = $script:failed; skipped = $script:skipped
            timings = $script:phaseTimings
        }
        if ($Repeat -gt 1) {
            $source = [pscustomobject]@{ version = 1; start = $start; end = $script:sourceEnd
                state = $state; buildSource = $script:buildSource }
            $evidence = [pscustomobject]@{
                version = 2; source = $source; name = $Name; receipt = $line; exitCode = $Code
                executed = $script:executed; passed = $script:passed; failed = $script:failed
                skipped = $script:skipped; repeat = $script:repeatEvidence
                repeatReason = $script:repeatReason; timings = $script:phaseTimings
            }
        }
        try { $sourceWrite = Write-CheckpointSafeText (Join-Path $script:resultsDirectory 'source.json') (($evidence | ConvertTo-Json -Depth 12) + "`n") $script:resultsDirectory }
        catch { Write-Host ('refused: source.json receipt-write-failed: ' + $_.Exception.GetType().Name); exit 2 }
        if ($null -ne $sourceWrite) { Write-Host ('refused: source.json receipt-write-failed: ' + $sourceWrite); exit 2 }
        try { $readBack = Read-CheckpointText (Join-Path $script:resultsDirectory 'source.json') | ConvertFrom-Json; if ($readBack.receipt -cne $line) { throw 'receipt-mismatch' } }
        catch { Write-Host ('refused: source.json receipt-unreadable-or-mismatching'); exit 2 }
        $gitWrite = Write-CheckpointSafeText (Join-Path $script:resultsDirectory 'git.txt') (('commit={0} source={1} sourceState={2} buildSource={3}' -f $commit, (Get-CheckpointSourceToken $start), $state, $script:buildSource) + "`n") $script:resultsDirectory
        if ($null -ne $gitWrite) { Write-Host ('refused: ' + $gitWrite) }
    }
    Write-WrapperTiming -Kind 'set-completed' -Code $Code
    Write-Host ('CHECKPOINT {0} EXIT CODE: {1}' -f $Name, $Code)
    if ($Code -eq 2) { exit 2 }
}

function Stop-Invalid {
    param([string]$Message)
    Write-Host ('CHECKPOINT {0} invalid input: {1}' -f $Name, $Message)
    Write-Trailer -Code 2
    exit 2
}

# (1) OutputPath guard. A trailing BACKSLASH loses itself to Windows argv quoting and creates a
# directory whose name ends in a space, breaking the whole build (CARD-0448 argv hazard).
if ($OutputPath -cnotmatch '^bin-[A-Za-z0-9._-]+/$') {
    Stop-Invalid ("OutputPath '$OutputPath' must be bin-<name>/ with a forward slash and no trailing space (CARD-0448 argv hazard: a trailing backslash creates a directory whose name ends in a space)")
}
if ($Repeat -lt 1 -or ([long]$Repeat * [long]$MinExecuted) -gt [int]::MaxValue) {
    Stop-Invalid 'Repeat must be a positive integer and Repeat x MinExecuted must fit an integer'
}

# (1b) MSBuild properties (CARD-0671). Split on commas as -Expect is: `pwsh -File` binds every
# argument as a string, so -MsBuildProperty A=1,B=2 arrives there as one element. A value that
# itself needs a comma is written with MSBuild's %2C escape. The output path stays owned by (1).
$propertyTokens = @(@($MsBuildProperty) | ForEach-Object { ([string]$_).Split(',') } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($token in $propertyTokens) {
    if ($token -cnotmatch '^[A-Za-z_][A-Za-z0-9_.-]*=') {
        Stop-Invalid ("MsBuildProperty '$token' must be Name=Value")
    }
    if ($token -match '(^|;)\s*(OutputPath|OutDir|BaseOutputPath)\s*=') {
        Stop-Invalid ("MsBuildProperty '$token' may not set the output path; use -OutputPath")
    }
    if ($token -match '(^|;)\s*AntiphonCheckpointRepeat(Project)?\s*=') {
        Stop-Invalid ("MsBuildProperty '$token' may not set reserved repeat properties")
    }
}
if ($Repeat -gt 1) {
    $repeatProject = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Get-Location).Path, $Project))
    if ([System.IO.Directory]::Exists($repeatProject)) {
        $matches = @([System.IO.Directory]::GetFiles($repeatProject, '*.csproj', [System.IO.SearchOption]::TopDirectoryOnly))
        if ($matches.Count -ne 1) { Stop-Invalid 'Repeat project directory must contain exactly one csproj' }
        $repeatProject = $matches[0]
    }
    if (-not [System.IO.File]::Exists($repeatProject) -or -not $repeatProject.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Invalid 'Repeat project must resolve to exactly one csproj'
    }
    [xml]$repeatProjectXml = (Read-CheckpointText $repeatProject)
    $tunit = @($repeatProjectXml.SelectNodes('//*[local-name()="PackageReference"]') | Where-Object { $_.GetAttribute('Include') -eq 'TUnit' })
    if ($tunit.Count -ne 1) { Stop-Invalid 'Repeat project must reference TUnit directly' }
    $propertyTokens = @($propertyTokens) + @("AntiphonCheckpointRepeat=$Repeat", "AntiphonCheckpointRepeatProject=$repeatProject")
}
# Off Windows the extensionless fakeclaude apphost and the fakeclaude/ directory Antiphon.Tests
# stages beside it are the same path, so the build fails; with no apphost `dotnet run` falls back
# to `dotnet exec <dll>`. C671_PLATFORM is the offline harness's platform override.
$onWindows = $IsWindows
if ($env:C671_PLATFORM -eq 'windows') { $onWindows = $true }
elseif ($env:C671_PLATFORM -eq 'linux') { $onWindows = $false }
$namesAppHost = @($propertyTokens | Where-Object { $_ -match '(^|;)\s*UseAppHost\s*=' }).Count -gt 0
if (-not $onWindows -and -not $namesAppHost) {
    $propertyTokens = @($propertyTokens) + @('UseAppHost=false')
}
$propertyArguments = @($propertyTokens | ForEach-Object { '--property:' + $_ })
if ($ExpectedSourceSha -and $ExpectedSourceSha -cnotmatch '^([0-9a-f]{40}|[0-9a-f]{64})$') {
    Stop-Invalid 'ExpectedSourceSha must be a normalized full Git object ID'
}

# (2) Fresh results directory. A reused directory is how a stale TRX gets reported as this run.
$stamp = $env:C585_STAMP
if ([string]::IsNullOrWhiteSpace($stamp)) {
    $suffix = ([guid]::NewGuid().ToString('N')).Substring(0, 4)
    $stamp = ((Get-Date).ToString('yyyyMMdd-HHmmss') + '-' + $suffix)
}
$resultsDirectory = Join-Path $ResultsRoot ('{0}-{1}' -f $Name, $stamp)
if (Test-Path -LiteralPath $resultsDirectory) {
    Stop-Invalid ("results directory exists: $resultsDirectory")
}
$resultsParent = Split-Path -Parent $resultsDirectory
$resultsRefusal = New-CheckpointDirectory $resultsParent $resultsParent
if ($null -eq $resultsRefusal) { $resultsRefusal = New-CheckpointFreshOutput $resultsDirectory }
if ($null -ne $resultsRefusal) { Stop-Invalid ("refused: $resultsRefusal") }
$resultsDirectory = (Resolve-Path -LiteralPath $resultsDirectory).Path
$script:resultsDirectory = $resultsDirectory
$script:sourceStart = Get-CheckpointSource
if ($script:sourceStart.captureStatus -cne 'known') {
    $script:sourceReason = 'source_unknown'
    Write-Trailer -Code 2
    exit 2
}
if ($ExpectedSourceSha -and ($script:sourceStart.dirtyFiles -ne 0 -or $script:sourceStart.commit -cne $ExpectedSourceSha)) {
    $script:sourceReason = if ($script:sourceStart.dirtyFiles -ne 0) { 'source_dirty' } else { 'source_mismatch' }
    Write-Trailer -Code 2
    exit 2
}
$root = ([System.Text.Encoding]::UTF8.GetString((Get-CheckpointGitBytes (Get-Location).Path @('rev-parse', '--show-toplevel')))).Trim()
$layout = Get-CheckpointOutputLayout -Repo $root -ProjectPath $Project -Output $OutputPath
if (@($propertyTokens | Where-Object { $_ -match '(^|;)\s*ArtifactsPath\s*=' }).Count -eq 0) {
    $propertyTokens = @($propertyTokens) + @('ArtifactsPath=' + $layout.Artifacts)
}
if (@($propertyTokens | Where-Object { $_ -match '(^|;)\s*UseArtifactsOutput\s*=' }).Count -eq 0) {
    $propertyTokens = @($propertyTokens) + @('UseArtifactsOutput=true')
}
$propertyArguments = @($propertyTokens | ForEach-Object { '--property:' + $_ })
$stampPath = Get-CheckpointBuildStampPath $root $Project $OutputPath
$expectedBinding = Get-CheckpointBuildBinding $root $Project $OutputPath $propertyTokens $script:sourceStart
if ($NoBuild) {
    $stampValue = $null
    try { $stampValue = Read-CheckpointText $stampPath | ConvertFrom-Json }
    catch { }
    $script:buildSource = if (Test-CheckpointBuildBinding $stampValue $expectedBinding) { 'verified' } elseif ($null -eq $stampValue) { 'unknown' } else { 'mismatch' }
    if (($ExpectedSourceSha -or $Repeat -gt 1) -and $script:buildSource -ne 'verified') {
        $script:sourceReason = 'build_source_mismatch'
        Write-Trailer -Code 2
        exit 2
    }
}

# (2b) Host build slot (CARD-0589), after every input check so an invalid row never waits.
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'build-slot.ps1'))
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'build-driver-environment.ps1'))
$slot = $null
$slotState = 'skipped'
$slotWaited = 0
if ($NoSlot) {
    Write-Host 'BUILD SLOT skipped by -NoSlot'
} else {
    $slot = Enter-AntiphonBuildSlot -Label ('{0}@{1}' -f $Name, $Project) -WaitMinutes $SlotWaitMinutes
    $slotState = [string]$slot.Outcome
    $slotWaited = [int]$slot.WaitedSeconds
    if ($slotState -eq 'timeout') {
        Write-Host ('CHECKPOINT {0} slot=timeout waited={1}s: no build slot within {2} minutes; nothing was built or run' -f $Name, $slotWaited, $SlotWaitMinutes)
        Write-Trailer -Code 4
        exit 4
    }
}
$script:slotState = $slotState
$script:slotWaited = $slotWaited
if (-not (Assert-CheckpointBoundary 'post_slot')) {
    Exit-AntiphonBuildSlot -Lease $slot
    Write-Trailer -Code 2
    exit 2
}
$cpuArguments = @()
if ($null -ne $slot -and [int]$slot.MaxCpuCount -gt 0) { $cpuArguments = @('-maxcpucount:' + [int]$slot.MaxCpuCount) }

function Invoke-Dotnet {
    param([string[]]$Arguments, [string]$Phase, [string]$Nonce)
    # ProcessStartInfo.ArgumentList passes each token literally. The call operator
    # wildcard-scans a treenode filter (/*/*/Class/*) across /proc and /tmp before
    # the child starts: tens of seconds on a busy host, long enough for the script
    # harness's 300s budget to cancel a case that launches several children.
    # A phase log is created before the child and flushed on each line. An exit
    # receipt is written only after the child and both redirected streams finish.
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    # Bind both children to the repository certified by the source/build receipt.
    $psi.WorkingDirectory = $root
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    if (-not [string]::IsNullOrWhiteSpace($DotnetShim)) {
        $psi.FileName = 'pwsh'
        $tokens = @('-NoProfile', '-NonInteractive', '-File', $DotnetShim) + @($Arguments)
    } else {
        $psi.FileName = 'dotnet'
        $tokens = @($Arguments)
    }
    foreach ($token in $tokens) { [void]$psi.ArgumentList.Add([string]$token) }
    if ($Nonce) { $psi.Environment['ANTIPHON_CHECKPOINT_NONCE'] = $Nonce }
    Set-AntiphonBuildDriverEnvironment -StartInfo $psi
    $logPath = Join-Path $resultsDirectory ($Phase + '.log')
    $stream = Open-CheckpointSafeCreate $logPath $resultsDirectory
    $writer = [System.IO.StreamWriter]::new($stream)
    $writer.AutoFlush = $true
    try {
        $launched = [DateTimeOffset]::UtcNow
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        if ($Phase -eq 'run') { Write-WrapperTiming -Kind 'run-started' }
        $proc = [System.Diagnostics.Process]::Start($psi)
        try {
            $stdoutTask = $proc.StandardOutput.ReadLineAsync()
            $stderrTask = $proc.StandardError.ReadLineAsync()
            while ($null -ne $stdoutTask -or $null -ne $stderrTask) {
                $pending = [System.Collections.Generic.List[System.Threading.Tasks.Task]]::new()
                if ($null -ne $stdoutTask) { $pending.Add($stdoutTask) }
                if ($null -ne $stderrTask) { $pending.Add($stderrTask) }
                $completed = [System.Threading.Tasks.Task]::WhenAny($pending.ToArray()).GetAwaiter().GetResult()
                if ([object]::ReferenceEquals($completed, $stdoutTask)) {
                    $line = $stdoutTask.GetAwaiter().GetResult()
                    $stdoutTask = if ($null -eq $line) { $null } else { $proc.StandardOutput.ReadLineAsync() }
                } else {
                    $line = $stderrTask.GetAwaiter().GetResult()
                    $stderrTask = if ($null -eq $line) { $null } else { $proc.StandardError.ReadLineAsync() }
                }
                if ($null -ne $line) {
                    $writer.WriteLine($line)
                    Write-Host $line
                }
            }
            $proc.WaitForExit()
            $watch.Stop()
            $exited = [DateTimeOffset]::UtcNow
            if ($Phase -eq 'build') { $script:buildSeconds = $watch.Elapsed.TotalSeconds }
            elseif ($Phase -eq 'run') {
                $script:hostLaunch = $launched; $script:hostExit = $exited
                $script:hostElapsedSeconds = $watch.Elapsed.TotalSeconds
            }
            $exitCode = $proc.ExitCode
            $exitLine = ('DOTNET {0} EXIT CODE: {1}' -f $Phase, $exitCode)
            $writer.WriteLine($exitLine)
            Write-Host $exitLine
            return $exitCode
        } finally { $proc.Dispose() }
    } finally { $writer.Dispose() }
}

function Get-CheckpointPropertyPair {
    param([string]$Token)
    $body = $null
    if ($Token.StartsWith('--property:', [StringComparison]::OrdinalIgnoreCase)) { $body = $Token.Substring(11) }
    elseif ($Token.StartsWith('-p:', [StringComparison]::OrdinalIgnoreCase)) { $body = $Token.Substring(3) }
    elseif ($Token.StartsWith('/p:', [StringComparison]::OrdinalIgnoreCase)) { $body = $Token.Substring(3) }
    if ($null -eq $body) { return $null }
    $eq = $body.IndexOf('=')
    if ($eq -lt 1) { return $null }
    return @{ Key = $body.Substring(0, $eq); Value = $body.Substring($eq + 1) }
}

function Test-CheckpointIsolationKey {
    param([string]$Key)
    foreach ($name in @('OutputPath','OutDir','BaseOutputPath','BaseIntermediateOutputPath','MSBuildProjectExtensionsPath','ArtifactsPath','UseArtifactsOutput')) {
        if ($Key -eq $name) { return $true }
    }
    return $false
}

function Invoke-CheckpointGraphEval {
    param([string]$Project, [string]$Owned, [string]$Artifacts, [string[]]$Forwarded)
    $evalArgs = @(
        'msbuild', $Project, '-getItem:ProjectReference', '-nologo', '-nodeReuse:false',
        ('--property:OutputPath=' + $Owned),
        ('--property:ArtifactsPath=' + $Artifacts),
        '--property:UseArtifactsOutput=true'
    )
    $sawHost = $false
    foreach ($token in @($Forwarded)) {
        $evalArgs += $token
        if ($token.StartsWith('--property:UseAppHost=', [StringComparison]::OrdinalIgnoreCase)) { $sawHost = $true }
    }
    if (-not $sawHost -and -not $onWindows) { $evalArgs += '--property:UseAppHost=false' }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'dotnet'
    $psi.WorkingDirectory = $root
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($token in $evalArgs) { [void]$psi.ArgumentList.Add([string]$token) }
    $psi.Environment['MSBUILDDISABLENODEREUSE'] = '1'
    $psi.Environment['DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER'] = '1'
    Set-AntiphonBuildDriverEnvironment -StartInfo $psi
    foreach ($name in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE')) {
        if ($psi.Environment.ContainsKey($name)) { [void]$psi.Environment.Remove($name) }
    }
    try {
        try { $result = [Antiphon.Checkpoints.CheckpointProcessRunner]::Run($psi, 120000, $false, [Threading.CancellationToken]::None) }
        catch { return @{ Error = 'msbuild did not start'; References = @() } }
        if ($result.TimedOut) { return @{ Error = 'msbuild evaluation timed out'; References = @() } }
        $stdout = $result.Stdout
        if ($result.Exit -ne 0) { return @{ Error = ('msbuild exit ' + $result.Exit); References = @() } }
        $start = $stdout.IndexOf('{')
        if ($start -lt 0) { return @{ Error = 'msbuild json missing'; References = @() } }
        try { $doc = $stdout.Substring($start) | ConvertFrom-Json } catch { return @{ Error = 'msbuild json unusable'; References = @() } }
        if ($null -eq $doc.Items) { return @{ Error = 'msbuild items missing'; References = @() } }
        $item = $doc.Items.ProjectReference
        if ($null -eq $item) { return @{ Error = $null; References = @() } }
        $refs = New-Object System.Collections.Generic.List[string]
        foreach ($one in @($item)) {
            if ($null -eq $one) { continue }
            $full = [string]$one.FullPath
            if ([string]::IsNullOrWhiteSpace($full)) { return @{ Error = 'project reference FullPath missing'; References = @() } }
            [void]$refs.Add($full)
        }
        return @{ Error = $null; References = $refs.ToArray() }
    } finally { }
}

function Get-CheckpointRepoRelative {
    param([string]$Repo, [string]$Directory)
    $fullRepo = [System.IO.Path]::GetFullPath($Repo).TrimEnd('\', '/')
    $fullDir = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\', '/')
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if ([string]::Equals($fullDir, $fullRepo, $comparison)) { return '' }
    $sep = [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullDir.StartsWith($fullRepo + $sep, $comparison)) { return $null }
    return $fullDir.Substring($fullRepo.Length + 1).Replace('\', '/')
}

function Get-CheckpointTrackedSnapshot {
    param([string[]]$Projects, [switch]$IndexOnly)
    # Retained for the membership probe. The baseline is the whole tracked set.
    $null = $Projects
    $top = Invoke-CheckpointGit $root @('rev-parse', '--show-toplevel')
    if ($top.Reason -or [int]$top.Exit -ne 0) { return @{ Error = 'git-toplevel'; Index = ''; Hashes = $null; Head = $null; Repository = $null; Untracked = $null } }
    $repo = ([string]$top.Stdout).Trim()
    $headCall = Invoke-CheckpointGit $repo @('rev-parse', 'HEAD')
    if ($headCall.Reason -or [int]$headCall.Exit -ne 0) { return @{ Error = 'git-head'; Index = ''; Hashes = $null; Head = $null; Repository = $repo; Untracked = $null } }
    $head = ([string]$headCall.Stdout).Trim()
    $listed = Invoke-CheckpointGit $repo @('ls-files', '-z', '-s')
    if ($listed.Reason -or [int]$listed.Exit -ne 0) {
        $listingError = 'git-ls-files'
        if ([string]$listed.Reason -ceq 'git-encoding') { $listingError = 'git-ls-files git output is not valid UTF-8 (a non-UTF-8 file name?)' }
        return @{ Error = $listingError; Index = ''; Hashes = $null; Head = $head; Repository = $repo; Untracked = $null }
    }
    $raw = [string]$listed.Stdout
    $hashes = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($record in $raw.Split([char]0, [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $tab = $record.IndexOf("`t")
        if ($tab -lt 1 -or $tab -ge ($record.Length - 1)) { return @{ Error = 'index-record'; Index = ''; Hashes = $null; Head = $head; Repository = $repo; Untracked = $null } }
        $hashes[$record.Substring($tab + 1)] = $record.Substring(0, $tab)
    }
    if ($IndexOnly) { return @{ Error = $null; Index = $raw; Hashes = $hashes; Head = $head; Repository = $repo; Untracked = $null } }
    $manifest = Get-CheckpointUntrackedManifest $repo
    if ($null -ne $manifest.Error) { return @{ Error = [string]$manifest.Error; Index = ''; Hashes = $null; Head = $head; Repository = $repo; Untracked = $null } }
    $content = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($key in $hashes.Keys) { $content[$key] = Get-CheckpointContentStamp (Join-CheckpointRepoPath $repo $key) }
    return @{ Error = $null; Index = $raw; Hashes = $hashes; Head = $head; Repository = $repo; Untracked = $manifest.Files; Content = $content }
}

function Get-CheckpointTrackedStatus {
    param([string]$Repo)
    $raw = Invoke-CheckpointGit $Repo @('status', '--porcelain=v2', '-z', '--untracked-files=no')
    if ($raw.Reason -or [int]$raw.Exit -ne 0) { return @{ Error = 'git-status'; Paths = @() } }
    $parts = ([string]$raw.Stdout).Split([char]0, [System.StringSplitOptions]::RemoveEmptyEntries)
    $paths = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $parts.Length; $i++) {
        $entry = [string]$parts[$i]
        $count = switch ($entry[0]) { '1' { 9 } '2' { 10 } 'u' { 11 } default { 0 } }
        if ($count -eq 0) { return @{ Error = 'status-record'; Paths = @() } }
        $fields = $entry.Split([char]' ', [int]$count)
        if ($fields.Length -ne $count -or [string]::IsNullOrEmpty($fields[-1])) { return @{ Error = 'status-record'; Paths = @() } }
        [void]$paths.Add([string]$fields[-1])
        if ($entry[0] -eq '2') {
            $i++
            if ($i -ge $parts.Length) { return @{ Error = 'status-rename'; Paths = @() } }
            [void]$paths.Add([string]$parts[$i])
        }
    }
    $distinct = New-Object System.Collections.Generic.List[string]
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $paths) {
        if ($seen.Add($path)) { [void]$distinct.Add($path) }
    }
    return @{ Error = $null; Paths = $distinct.ToArray() }
}

function Join-CheckpointRepoPath {
    param([string]$Repository, [string]$Relative)
    $full = $Repository
    foreach ($part in ($Relative -split '/', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $full = [System.IO.Path]::Combine($full, $part)
    }
    return $full
}

function Get-CheckpointSha256 {
    param([byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.Convert]::ToHexString($sha.ComputeHash($Bytes))).ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-CheckpointStampLink {
    param([string]$Target)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Target)
    return 'symlink|' + $bytes.Length + '|' + (Get-CheckpointSha256 $bytes)
}

function Get-CheckpointContentStamp {
    param([string]$Full)
    try {
        $file = [System.IO.FileInfo]::new($Full)
        if ($null -ne $file.LinkTarget) { return (Get-CheckpointStampLink ([string]$file.LinkTarget)) }
        $directory = [System.IO.DirectoryInfo]::new($Full)
        if ($null -ne $directory.LinkTarget) { return (Get-CheckpointStampLink ([string]$directory.LinkTarget)) }
        if ($directory.Exists) { return 'directory|0|' }
        if (-not $file.Exists) { return 'absent|0|' }
        $digest = Get-CheckpointFileDigest $Full
        if ($null -eq $digest -or $null -eq $digest.PSObject.Properties['Length'] -or $null -eq $digest.PSObject.Properties['Sha256']) {
            throw [IO.InvalidDataException]::new('content digest missing')
        }
        if ($digest.Length -isnot [long] -or $digest.Length -lt 0 -or $digest.Length -gt 1073741824L -or
            $digest.Sha256 -isnot [string] -or $digest.Sha256 -cnotmatch '^[0-9A-Fa-f]{64}$') {
            throw [IO.InvalidDataException]::new('content digest invalid')
        }
        return 'file|' + $digest.Length + '|' + $digest.Sha256.ToLowerInvariant()
    } catch {
        # CARD-1363 (the CARD-1358 D-4 design): a refused read stamps 'unreadable|<reason>', the innermost
        # message on one line, at most 200 characters, so settlement can say why an entry is unprovable.
        $inner = $_.Exception
        while ($null -ne $inner.InnerException) { $inner = $inner.InnerException }
        $reason = ([string]$inner.Message).Replace("`r", ' ').Replace("`n", ' ').Trim()
        if ($reason.Length -eq 0) { $reason = $inner.GetType().Name }
        if ($reason.Length -gt 200) { $reason = $reason.Substring(0, 200) }
        return 'unreadable|' + $reason
    }
}

function Test-CheckpointUnreadableStamp {
    param([string]$Stamp)
    return $Stamp.StartsWith('unreadable', [System.StringComparison]::Ordinal)
}

function Get-CheckpointUnreadableReason {
    param([string]$Stamp)
    if ($Stamp.Length -gt 11 -and $Stamp.StartsWith('unreadable|', [System.StringComparison]::Ordinal)) { return $Stamp.Substring(11) }
    return 'read refused'
}

function Get-CheckpointUntrackedManifest {
    param([string]$Repo)
    $ordinary = Invoke-CheckpointGit $Repo @('ls-files', '-z', '--others', '--exclude-standard')
    if ($ordinary.Reason -or [int]$ordinary.Exit -ne 0) {
        $listingError = 'git-ls-files'
        if ([string]$ordinary.Reason -ceq 'git-encoding') { $listingError = 'git-ls-files git output is not valid UTF-8 (a non-UTF-8 file name?)' }
        return @{ Error = $listingError; Files = $null }
    }
    $listed = Invoke-CheckpointGit $Repo @('ls-files', '-z', '--others', '--ignored', '--exclude-standard')
    if ($listed.Reason -or [int]$listed.Exit -ne 0) {
        $listingError = 'git-ls-files'
        if ([string]$listed.Reason -ceq 'git-encoding') { $listingError = 'git-ls-files git output is not valid UTF-8 (a non-UTF-8 file name?)' }
        return @{ Error = $listingError; Files = $null }
    }
    $files = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($relative in ([string]($ordinary.Stdout + $listed.Stdout)).Split([char]0, [System.StringSplitOptions]::RemoveEmptyEntries)) {
        if ($relative.StartsWith('.antiphon/', [StringComparison]::Ordinal)) { continue }
        $files[$relative] = Get-CheckpointContentStamp (Join-CheckpointRepoPath $Repo $relative)
    }
    return @{ Error = $null; Files = $files }
}

function Format-CheckpointDirty {
    param([string[]]$Paths)
    return 'refused: dirty-tracked' + "`n" + ((@($Paths) | ForEach-Object { 'DIRTY ' + $_ }) -join "`n")
}

function Complete-CheckpointBaseline {
    param($Baseline)
    $status = Get-CheckpointTrackedStatus ([string]$Baseline.Repository)
    if ($null -ne $status.Error) { return @{ Kind = 'tracked-snapshot'; Detail = 'refused: tracked-snapshot ' + $status.Error } }
    $paths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in @($status.Paths)) { [void]$paths.Add([string]$path) }
    # CARD-1363 (the CARD-1358 D-3 design): an entry whose recorded or current stamp the reader refused is
    # unprovable, not a change; a readable stamp that differs is a change.
    $unreadable = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($key in @($Baseline.Content.Keys) + @($Baseline.Untracked.Keys)) {
        $expected = if ($Baseline.Content.ContainsKey($key)) { $Baseline.Content[$key] } else { $Baseline.Untracked[$key] }
        if (Test-CheckpointUnreadableStamp $expected) { $unreadable[$key] = Get-CheckpointUnreadableReason $expected; continue }
        $current = Get-CheckpointContentStamp (Join-CheckpointRepoPath $Baseline.Repository $key)
        if (Test-CheckpointUnreadableStamp $current) { $unreadable[$key] = Get-CheckpointUnreadableReason $current }
        elseif ($current -cne $expected) { [void]$paths.Add([string]$key) }
    }
    $again = Get-CheckpointTrackedSnapshot -Projects @() -IndexOnly
    if ($null -ne $again.Error) { return @{ Kind = 'tracked-snapshot'; Detail = 'refused: tracked-snapshot ' + $again.Error } }
    foreach ($key in @($Baseline.Hashes.Keys) + @($again.Hashes.Keys)) {
        if (-not $Baseline.Hashes.ContainsKey($key) -or -not $again.Hashes.ContainsKey($key) -or $Baseline.Hashes[$key] -cne $again.Hashes[$key]) { [void]$paths.Add([string]$key) }
    }
    $ordinary = Invoke-CheckpointGit $Baseline.Repository @('ls-files', '-z', '--others', '--exclude-standard')
    if ($ordinary.Reason -or [int]$ordinary.Exit -ne 0) {
        $listingDetail = 'refused: git-ls-files'
        if ([string]$ordinary.Reason -ceq 'git-encoding') { $listingDetail = 'refused: git-ls-files git output is not valid UTF-8 (a non-UTF-8 file name?)' }
        return @{ Kind = 'tracked-snapshot'; Detail = $listingDetail }
    }
    foreach ($path in ([string]$ordinary.Stdout).Split([char]0, [System.StringSplitOptions]::RemoveEmptyEntries)) {
        if (-not $path.StartsWith('.antiphon/', [StringComparison]::Ordinal) -and -not $Baseline.Untracked.ContainsKey($path)) { [void]$paths.Add($path) }
    }
    return Format-CheckpointSettlement $Baseline $again $paths $unreadable
}

# CARD-1363 (the CARD-1358 D-3 design): any changed path makes the kind tampered, with the unprovable entries listed
# beside it before the restore line; only unprovable entries make the kind unreadable, which names no restore because
# no content difference was observed. Exit stays 2 either way.
function Format-CheckpointSettlement {
    param($Baseline, $Again, $Changed, $Unreadable)
    $changed = @($Changed | Sort-Object -CaseSensitive)
    $unprovable = @($Unreadable.Keys | Where-Object { -not $Changed.Contains($_) } | Sort-Object -CaseSensitive)
    if ($changed.Count -eq 0 -and $unprovable.Count -eq 0) { return @{ Kind = $null; Detail = '' } }
    $lines = [System.Collections.Generic.List[string]]::new()
    if ($changed.Count -gt 0) {
        [void]$lines.Add('refused: tampered')
        foreach ($path in $changed) {
            [void]$lines.Add('TAMPERED ' + $path)
            $quoted = "'" + $path.Replace("'", "'\''") + "'"
            if ($Baseline.Hashes.ContainsKey($path)) { [void]$lines.Add('manual command: git restore --source=' + $Baseline.Head + ' --staged --worktree -- ' + $quoted) }
            elseif ($Again.Hashes.ContainsKey($path)) { [void]$lines.Add('manual command: git restore --staged -- ' + $quoted) }
            else { [void]$lines.Add('manual command: git status --short -- ' + $quoted + '; inspect and recover user bytes from your backup') }
        }
        foreach ($path in $unprovable) { [void]$lines.Add('UNREADABLE ' + $path + ': ' + $Unreadable[$path]) }
        [void]$lines.Add('manual restore required; Build-result bytes and index entries were left in place; no source recovery was attempted.')
        return @{ Kind = 'tampered'; Detail = ($lines -join "`n") }
    }
    [void]$lines.Add('refused: unreadable')
    foreach ($path in $unprovable) { [void]$lines.Add('UNREADABLE ' + $path + ': ' + $Unreadable[$path]) }
    [void]$lines.Add('no content difference was observed; nothing in the worktree or index was changed; no restore is needed; repeat the run.')
    return @{ Kind = 'unreadable'; Detail = ($lines -join "`n") }
}

function Publish-CheckpointSettlement {
    param($Baseline, [string]$FallbackReason, [string]$FallbackDetail)
    $settled = Complete-CheckpointBaseline $Baseline
    if ($null -ne $settled.Kind) { Write-Host ([string]$settled.Detail); return 2 }
    if (-not [string]::IsNullOrEmpty($FallbackReason)) {
        Write-Host ('refused: ' + $FallbackReason)
        if (-not [string]::IsNullOrEmpty($FallbackDetail)) { Write-Host $FallbackDetail }
        return 2
    }
    return $null
}

function Invoke-ConfinedCheckpointBuild {
    param([string[]]$Arguments, [string]$Phase)
    $outputValue = $null
    $project = $null
    foreach ($token in @($Arguments)) {
        if ($token -eq 'build') { continue }
        $pair = Get-CheckpointPropertyPair $token
        if ($null -ne $pair -and $pair.Key -eq 'OutputPath') { $outputValue = [string]$pair.Value; continue }
        if ($null -eq $project -and -not $token.StartsWith('-') -and -not $token.StartsWith('/p:', [StringComparison]::OrdinalIgnoreCase) -and -not $token.StartsWith('/property:', [StringComparison]::OrdinalIgnoreCase)) { $project = $token }
    }
    if ([string]::IsNullOrWhiteSpace($outputValue) -or [string]::IsNullOrWhiteSpace($project)) {
        return (Invoke-Dotnet -Phase $Phase -Arguments $Arguments)
    }
    $projectFull = if ([System.IO.Path]::IsPathRooted($project)) { [System.IO.Path]::GetFullPath($project) } else { [System.IO.Path]::GetFullPath((Join-Path $root $project)) }
    $projectDir = Resolve-CheckpointProjectDirectory $projectFull
    if ([string]::IsNullOrEmpty($projectDir)) {
        Write-Host 'refused: graph-unevaluated'
        Write-Host 'project directory missing'
        return 2
    }
    $layout = Get-CheckpointOutputLayout -Repo $root -ProjectPath $project -Output $outputValue
    $status = Get-CheckpointTrackedStatus $root
    if ($null -ne $status.Error) {
        Write-Host 'refused: tracked-snapshot'
        Write-Host ([string]$status.Error)
        return 2
    }
    if (@($status.Paths).Count -gt 0) {
        Write-Host (Format-CheckpointDirty @($status.Paths))
        return 2
    }
    $before = Get-CheckpointTrackedSnapshot -Projects @($projectFull)
    if ($null -ne $before.Error) {
        Write-Host 'refused: tracked-snapshot'
        Write-Host ([string]$before.Error)
        return 2
    }
    $rewritten = New-Object System.Collections.Generic.List[string]
    $sawArtifacts = $false
    $sawUse = $false
    $sawHost = $false
    foreach ($token in @($Arguments)) {
        $pair = Get-CheckpointPropertyPair $token
        if ($null -ne $pair -and $pair.Key -eq 'OutputPath') {
            [void]$rewritten.Add('--property:OutputPath=' + $layout.Owned)
            continue
        }
        if ($null -ne $pair -and $pair.Key -eq 'ArtifactsPath') { $sawArtifacts = $true }
        if ($null -ne $pair -and $pair.Key -eq 'UseArtifactsOutput') { $sawUse = $true }
        if ($null -ne $pair -and $pair.Key -eq 'UseAppHost') { $sawHost = $true }
        [void]$rewritten.Add([string]$token)
    }
    if (-not $sawArtifacts) { [void]$rewritten.Add('--property:ArtifactsPath=' + $layout.Artifacts) }
    if (-not $sawUse) { [void]$rewritten.Add('--property:UseArtifactsOutput=true') }
    if (-not $sawHost -and -not $onWindows) { [void]$rewritten.Add('--property:UseAppHost=false') }
    $forwarded = New-Object System.Collections.Generic.List[string]
    foreach ($token in @($Arguments)) {
        $pair = Get-CheckpointPropertyPair $token
        if ($null -eq $pair) { continue }
        if (Test-CheckpointIsolationKey ([string]$pair.Key)) { continue }
        [void]$forwarded.Add('--property:' + $pair.Key + '=' + $pair.Value)
    }
    $freshReason = New-CheckpointFreshOutput $layout.Owned
    if ($null -ne $freshReason) { Write-Host ('refused: ' + $freshReason); return 2 }
    $projects = New-Object System.Collections.Generic.List[string]
    $comparer = if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
    $seen = [System.Collections.Generic.HashSet[string]]::new($comparer)
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $walkError = $null
    $queue.Enqueue($projectFull)
    while ($queue.Count -gt 0 -and $null -eq $walkError) {
        $current = [System.IO.Path]::GetFullPath($queue.Dequeue())
        if (-not $seen.Add($current)) { continue }
        if ($projects.Count -ge 500) {
            $walkError = 'walk cap exceeded'
            break
        }
        [void]$projects.Add($current)
        $eval = Invoke-CheckpointGraphEval -Project $current -Owned $layout.Owned -Artifacts $layout.Artifacts -Forwarded $forwarded.ToArray()
        if ($null -ne $eval.Error) {
            $walkError = [string]$eval.Error
            break
        }
        foreach ($reference in @($eval.References)) {
            if ([string]::IsNullOrWhiteSpace([string]$reference)) { continue }
            if (-not [System.IO.File]::Exists([string]$reference)) {
                $walkError = 'referenced project missing: ' + $reference
                break
            }
            $queue.Enqueue([string]$reference)
        }
    }
    if ($null -ne $walkError) {
        $walkCode = Publish-CheckpointSettlement $before 'graph-unevaluated' $walkError
        return $walkCode
    }
    $exit = 0
    $thrown = $null
    try {
        $exit = Invoke-Dotnet -Phase $Phase -Arguments $rewritten.ToArray()
    } catch {
        $thrown = $_
    }
    $settledCode = Publish-CheckpointSettlement $before '' ''
    if ($null -ne $settledCode) { return $settledCode }
    if ($null -ne $thrown) { throw $thrown }
    if ($exit -eq 8) { return 2 }
    return $exit
}

# (3)-(4) run under the slot; it is released however they end (the runner also reaps it if this
# process dies).
$trxPath = Join-Path $resultsDirectory 'run.trx'
$script:trxPath = $trxPath
$phaseError = $null
try {
    # (3) Build, unless this row reuses an earlier row's output. The grant's -maxcpucount applies to
    # the build only: the --no-build run below starts no MSBuild nodes.
    $buildState = 'reused'
    $script:buildState = $buildState
    $buildExit = 0
    if (-not $NoBuild) {
        $graph = Get-CheckpointWriteDirectories $root $Project $OutputPath
        if ($null -ne $graph.Refusal) {
            $outputRefusal = $graph.Refusal
        } else {
            $outputRefusal = Test-CheckpointPreflight $root @($graph.Directories)
        }
        if ($null -ne $outputRefusal) {
            Write-Host ('refused: ' + $outputRefusal)
            $buildExit = 2
            $buildState = 'failed'
            $script:buildState = 'failed'
        } elseif ($null -ne ($stampRefusal = Test-CheckpointStampRefusal $stampPath)) {
            Write-Host ('refused: ' + $stampRefusal)
            $buildExit = 2
            $buildState = 'failed'
            $script:buildState = 'failed'
        } else {
            $deleteRefusal = Remove-CheckpointCheckedFile $stampPath
            if ($null -ne $deleteRefusal) {
                Write-Host ('refused: ' + $deleteRefusal)
                $buildExit = 2
                $buildState = 'failed'
                $script:buildState = 'failed'
            } else {
            if ([string]::IsNullOrWhiteSpace($DotnetShim)) {
                $buildExit = Invoke-ConfinedCheckpointBuild -Phase 'build' -Arguments (@('build', $Project, ('--property:OutputPath=' + $OutputPath)) + $propertyArguments + $cpuArguments + @('--nologo'))
            } else {
                $buildExit = Invoke-Dotnet -Phase 'build' -Arguments (@('build', $Project, ('--property:OutputPath=' + $OutputPath)) + $propertyArguments + $cpuArguments + @('--nologo'))
            }
            if ($buildExit -eq 0) { $buildState = 'ok'; $script:buildState = 'ok' }
            else { $buildState = 'failed'; $script:buildState = 'failed' }
            if ($buildExit -ne 8 -and -not (Assert-CheckpointBoundary 'post_build')) { $buildExit = 2 }
            if ($buildExit -eq 0) {
                $replaceRefusal = Test-CheckpointStampRefusal $stampPath
                if ($null -ne $replaceRefusal) {
                    Write-Host ('refused: ' + $replaceRefusal)
                    $buildExit = 2
                    $buildState = 'failed'
                    $script:buildState = 'failed'
                    $script:buildSource = 'unknown'
                } else {
                    $stampDirectory = Split-Path -Parent $stampPath
                    $made = New-CheckpointDirectory $stampDirectory $stampDirectory
                    if ($null -ne $made) {
                        Write-Host ('refused: ' + $made)
                        $buildExit = 2
                        $buildState = 'failed'
                        $script:buildState = 'failed'
                        $script:buildSource = 'unknown'
                    } else {
                    $stampWrite = Write-CheckpointSafeText $stampPath (($expectedBinding | ConvertTo-Json -Depth 8) + "`n") $stampDirectory
                    if ($null -ne $stampWrite) {
                        Write-Host ('refused: ' + $stampWrite)
                        $buildExit = 2
                        $buildState = 'failed'
                        $script:buildState = 'failed'
                        $script:buildSource = 'unknown'
                    } else {
                        $ledgerRefusal = Seal-CheckpointFreshOutput $stampDirectory
                        if ($null -ne $ledgerRefusal) {
                            Write-Host ('refused: ' + $ledgerRefusal)
                            $buildExit = 2; $script:buildState = 'failed'; $buildState = 'failed'
                        } else { $script:buildSource = 'verified' }
                    }
                }
            }
        }
        }
        }
    }

    # (4) One filter, one fresh TRX.
    if ($buildExit -eq 0) {
        if (-not (Assert-CheckpointBoundary 'pre_run')) { $buildExit = 2 }
    }
    if ($buildExit -eq 0) {
        $repeatNonce = if ($Repeat -gt 1) { [guid]::NewGuid().ToString('N') } else { $null }
        $runExit = Invoke-Dotnet -Phase 'run' -Nonce $repeatNonce -Arguments (@('run', '--project', $Project, '--no-build', ('--property:OutputPath=' + $layout.Owned)) + $propertyArguments + @(
            '--', '--treenode-filter', $Filter, '--report-trx', '--report-trx-filename', 'run.trx',
            '--results-directory', $resultsDirectory))
        $null = Assert-CheckpointBoundary 'post_run'
    }
} catch {
    $phaseError = $_.Exception.GetType().Name
    $script:sourceEnd = $null
    $script:sourceReason = 'source_unknown'
} finally {
    Exit-AntiphonBuildSlot -Lease $slot
}
if ($null -ne $phaseError) {
    Write-Host ('CHECKPOINT {0} driver interruption: {1}' -f $Name, $phaseError)
    Write-Trailer -Code 2
    exit 2
}
if ($buildExit -ne 0) {
    Write-Host ('CHECKPOINT {0} build=failed project={1} outputPath={2} exit={3} slot={4} waited={5}s' -f $Name, $Project, $OutputPath, $buildExit, $slotState, $slotWaited)
    foreach ($token in $propertyTokens) { Write-Host ('MSBUILD PROPERTY {0}' -f $token) }
    if ($buildExit -eq 8) {
        Write-Trailer -Code 8
        exit 8
    }
    Write-Trailer -Code 2
    exit 2
}

# (5) No TRX is not a result.
if (-not (Test-Path -LiteralPath $trxPath)) {
    Write-Host ('CHECKPOINT {0} no TRX written to {1} (runner exit {2})' -f $Name, $trxPath, $runExit)
    Write-Trailer -Code 2
    exit 2
}

try {
    [xml]$doc = Read-CheckpointText $trxPath
} catch {
    Write-Host ('CHECKPOINT {0} malformed TRX {1}: {2}' -f $Name, $trxPath, $_.Exception.Message)
    Write-Trailer -Code 2
    exit 2
}

$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$nsUri = $doc.DocumentElement.NamespaceURI
if ($nsUri) { $ns.AddNamespace('t', $nsUri) }
function Select-Ns {
    param([string]$XPath)
    if ($nsUri) { return $doc.SelectNodes($XPath, $ns) }
    return $doc.SelectNodes(($XPath -replace 't:', ''))
}

# Join UnitTestResult to TestDefinitions/UnitTest/TestMethod@className (never the display name).
$definitions = @{}
foreach ($unit in (Select-Ns '//t:TestDefinitions/t:UnitTest')) {
    $id = $unit.GetAttribute('id')
    if (-not $id) { continue }
    $method = $null
    foreach ($child in $unit.ChildNodes) {
        if ($child.LocalName -eq 'TestMethod') { $method = $child; break }
    }
    if ($null -eq $method) { continue }
    $definitions[$id] = ('{0}.{1}' -f $method.GetAttribute('className'), $method.GetAttribute('name'))
}

$results = @(Select-Ns '//t:Results/t:UnitTestResult')
if ($results.Count -eq 0) { $results = @(Select-Ns '//t:UnitTestResult') }

$executedNames = New-Object 'System.Collections.Generic.List[string]'
$failedNames = New-Object 'System.Collections.Generic.List[string]'
foreach ($result in $results) {
    $id = $result.GetAttribute('testId')
    $testName = $null
    if ($id -and $definitions.ContainsKey($id)) { $testName = [string]$definitions[$id] }
    if ([string]::IsNullOrWhiteSpace($testName)) { $testName = [string]$result.GetAttribute('testName') }
    $outcome = [string]$result.GetAttribute('outcome')
    if ($outcome -eq 'NotExecuted') { continue }
    $executedNames.Add($testName)
    if ($outcome -eq 'Failed' -or $outcome -eq 'Error' -or $outcome -eq 'Timeout' -or $outcome -eq 'Aborted') {
        $failedNames.Add($testName)
    }
}

$counters = $null
foreach ($node in (Select-Ns '//t:ResultSummary/t:Counters')) { $counters = $node; break }
if ($null -eq $counters) { foreach ($node in (Select-Ns '//t:Counters')) { $counters = $node; break } }

function Get-Counter {
    param([string]$Attribute, [int]$Fallback)
    if ($null -eq $counters) { return $Fallback }
    $raw = $counters.GetAttribute($Attribute)
    $value = 0
    if ([string]::IsNullOrWhiteSpace($raw) -or -not [int]::TryParse($raw, [ref]$value)) { return $Fallback }
    return $value
}

$executed = Get-Counter -Attribute 'executed' -Fallback $executedNames.Count
$failed = Get-Counter -Attribute 'failed' -Fallback $failedNames.Count
$total = Get-Counter -Attribute 'total' -Fallback $executed
$passed = Get-Counter -Attribute 'passed' -Fallback ($executed - $failed)
$skipped = $total - $executed
if ($skipped -lt 0) { $skipped = 0 }
$script:executed = $executed
$script:passed = $passed
$script:failed = $failed
$script:skipped = $skipped
$script:phaseTimings = Get-CheckpointPhaseTimings -Document $doc -Launched $script:hostLaunch -Exited $script:hostExit `
    -HostElapsedSeconds $script:hostElapsedSeconds -BuildSeconds $script:buildSeconds -SlotWaitSeconds $script:slotWaited
if ($Repeat -gt 1) {
    try {
        $script:repeatEvidence = Get-CheckpointRepeatEvidence -Document $doc -Requested $Repeat -Nonce $repeatNonce `
            -MinExecuted $MinExecuted -Expect $Expect
        if (-not (Test-CheckpointRepeatSummary $script:repeatEvidence $executed $passed $failed $skipped $Repeat)) {
            $script:repeatReason = 'repeat_counter_or_outcome_disagreement'
        }
    } catch { $script:repeatReason = $_.Exception.Message }
}

# (6) The report line the bundle asks Code to produce, then the roster.
foreach ($token in $propertyTokens) { Write-Host ('MSBUILD PROPERTY {0}' -f $token) }
foreach ($testName in $failedNames) { Write-Host ('FAILED {0}' -f $testName) }

$shown = 0
foreach ($testName in $executedNames) {
    if ($shown -ge 300) { break }
    Write-Host ('EXECUTED {0}' -f $testName)
    $shown++
}
if ($executedNames.Count -gt $shown) {
    Write-Host ('EXECUTED ... +{0} more' -f ($executedNames.Count - $shown))
}

# (7) Verdict.
if ($null -ne $script:sourceReason) {
    Write-Trailer -Code 2
    exit 2
}
$rosterMisses = New-Object 'System.Collections.Generic.List[string]'
# Split on commas too: `pwsh -File` binds every argument as a string, so -Expect A,B arrives as
# one element there and as two when the script is dot-sourced or called in-process.
# Then strip the edge quote characters a quoted roster carries (CARD-0615): written -Expect 'A','B'
# those quotes survive that same string binding as literal data, and they are wrapper syntax, never
# part of the name to match. Only leading/trailing quotes go; an interior quote stays significant.
$expectTokens = @(@($Expect) | ForEach-Object { ([string]$_).Split(',') } | ForEach-Object { $_.Trim().Trim([char[]]@([char]39, [char]34)).Trim() })
foreach ($token in $expectTokens) {
    if ([string]::IsNullOrWhiteSpace($token)) { continue }
    $hit = $false
    foreach ($testName in $executedNames) {
        if ($testName -and $testName.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $hit = $true; break }
    }
    if (-not $hit) { $rosterMisses.Add($token) }
}
foreach ($token in $rosterMisses) { Write-Host ('ROSTER MISS {0}' -f $token) }

if ($failed -gt 0) {
    Write-Trailer -Code 1
    exit 1
}
if ($Repeat -gt 1 -and $null -ne $script:repeatReason) {
    Write-Host ('REPEAT INVALID {0}' -f $script:repeatReason)
    Write-Trailer -Code 2
    exit 2
}
if ($executed -lt $MinExecuted) {
    Write-Host ('MIN EXECUTED expected at least={0} actual={1}' -f $MinExecuted, $executed)
    Write-Trailer -Code 3
    exit 3
}
if ($rosterMisses.Count -gt 0) {
    Write-Trailer -Code 3
    exit 3
}
Write-Trailer -Code 0
exit 0
