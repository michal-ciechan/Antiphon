#requires -Version 7.0
<#
.SYNOPSIS
    CARD-0585 D-7: run ONE row of a plan's "### Checkpoints" table and print its report line.

    One isolated build (unless -NoBuild) plus one exact filter into a FRESH results directory,
    then the counters and the executed Class.Method roster parsed out of that run's own TRX, so
    no delegate re-derives TRX parsing or opens the file. It runs one row, never the table.

    Exit codes: 0 green; 1 one or more failed tests; 2 invalid input, failed build or no TRX;
    3 fewer than -MinExecuted executed tests, or an -Expect token that matched no executed name;
    4 no host build slot within -SlotWaitMinutes (nothing was built or run).

    Build slot (CARD-0589): after input validation and the fresh results directory, the row takes a
    lease from the session runner's /build-slots broker (scripts/lib/build-slot.ps1), waits for it
    visibly in FIFO order (BUILD SLOT lines), builds with the grant's -maxcpucount:N and releases it
    after the run. An unreachable runner falls back to an unleased run at -maxcpucount:4 after 60 s
    and says so. -NoSlot is for an operator shell only; a delegate row never uses it.

    -MsBuildProperty Name=Value (CARD-0671) is forwarded as --property:Name=Value to BOTH the build
    and the `dotnet run`, so a -NoBuild row resolves the same output its build produced. Off
    Windows, UseAppHost=false is added unless the caller names UseAppHost itself: the extensionless
    fakeclaude apphost collides with the fakeclaude/ directory Antiphon.Tests stages beside it.

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
    [switch]$NoSlot
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'checkpoint-source.ps1'))
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'checkpoint-repeat.ps1'))
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

function Get-CheckpointBuildStampPath {
    param([string]$Root, [string]$ProjectPath, [string]$Output)
    return [System.IO.Path]::Combine($Root, $ProjectPath, $Output, 'checkpoint-build-source.json')
}

function Get-CheckpointBuildBinding {
    param([string]$Root, [string]$ProjectPath, [string]$Output, [string[]]$Properties, $Source)
    $projectFull = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Root, $ProjectPath))
    $outputFull = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($projectFull, $Output))
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

function Write-Trailer {
    param([int]$Code)
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
        $evidence | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $script:resultsDirectory 'source.json') -Encoding utf8
        ('commit={0} source={1} sourceState={2} buildSource={3}' -f $commit,
            (Get-CheckpointSourceToken $start), $state, $script:buildSource) |
            Set-Content -LiteralPath (Join-Path $script:resultsDirectory 'git.txt') -Encoding utf8
    }
    Write-Host ('CHECKPOINT {0} EXIT CODE: {1}' -f $Name, $Code)
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
    [xml]$repeatProjectXml = [System.IO.File]::ReadAllText($repeatProject)
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
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
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
$stampPath = Get-CheckpointBuildStampPath $root $Project $OutputPath
$expectedBinding = Get-CheckpointBuildBinding $root $Project $OutputPath $propertyTokens $script:sourceStart
if ($NoBuild) {
    $stampValue = $null
    try { $stampValue = Get-Content -Raw -LiteralPath $stampPath | ConvertFrom-Json }
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
    $logPath = Join-Path $resultsDirectory ($Phase + '.log')
    $stream = [System.IO.FileStream]::new($logPath, [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
    $writer = [System.IO.StreamWriter]::new($stream)
    $writer.AutoFlush = $true
    try {
        $launched = [DateTimeOffset]::UtcNow
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
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
            $writer.WriteLine(('DOTNET {0} EXIT CODE: {1}' -f $Phase, $exitCode))
            return $exitCode
        } finally { $proc.Dispose() }
    } finally { $writer.Dispose() }
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
        if (Test-Path -LiteralPath $stampPath) { Remove-Item -LiteralPath $stampPath -Force }
        $buildExit = Invoke-Dotnet -Phase 'build' -Arguments (@('build', $Project, ('--property:OutputPath=' + $OutputPath)) + $propertyArguments + $cpuArguments + @('--nologo'))
        if ($buildExit -eq 0) { $buildState = 'ok'; $script:buildState = 'ok' }
        else { $buildState = 'failed'; $script:buildState = 'failed' }
        if (-not (Assert-CheckpointBoundary 'post_build')) { $buildExit = 2 }
        if ($buildExit -eq 0) {
            $stampDirectory = Split-Path -Parent $stampPath
            New-Item -ItemType Directory -Path $stampDirectory -Force | Out-Null
            $temporaryStamp = $stampPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
            $expectedBinding | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporaryStamp -Encoding utf8
            Move-Item -LiteralPath $temporaryStamp -Destination $stampPath -Force
            $script:buildSource = 'verified'
        }
    }

    # (4) One filter, one fresh TRX.
    if ($buildExit -eq 0) {
        if (-not (Assert-CheckpointBoundary 'pre_run')) { $buildExit = 2 }
    }
    if ($buildExit -eq 0) {
        $repeatNonce = if ($Repeat -gt 1) { [guid]::NewGuid().ToString('N') } else { $null }
        $runExit = Invoke-Dotnet -Phase 'run' -Nonce $repeatNonce -Arguments (@('run', '--project', $Project, '--no-build', ('--property:OutputPath=' + $OutputPath)) + $propertyArguments + @(
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
    [xml]$doc = Get-Content -Raw -LiteralPath $trxPath
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
