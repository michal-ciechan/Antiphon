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
    [int]$MinExecuted = 1,
    [string[]]$Expect,
    [string[]]$MsBuildProperty,
    [string]$DotnetShim,
    [double]$SlotWaitMinutes = 45,
    [switch]$NoSlot
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Write-Trailer {
    param([int]$Code)
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
$cpuArguments = @()
if ($null -ne $slot -and [int]$slot.MaxCpuCount -gt 0) { $cpuArguments = @('-maxcpucount:' + [int]$slot.MaxCpuCount) }

function Invoke-Dotnet {
    param([string[]]$Arguments)
    # Out-Host keeps the child's own output on the console instead of returning it from here.
    if (-not [string]::IsNullOrWhiteSpace($DotnetShim)) {
        & pwsh -NoProfile -NonInteractive -File $DotnetShim @Arguments | Out-Host
    } else {
        & dotnet @Arguments | Out-Host
    }
    return $LASTEXITCODE
}

# (3)-(4) run under the slot; it is released however they end (the runner also reaps it if this
# process dies).
$trxPath = Join-Path $resultsDirectory 'run.trx'
try {
    # (3) Build, unless this row reuses an earlier row's output. The grant's -maxcpucount applies to
    # the build only: the --no-build run below starts no MSBuild nodes.
    $buildState = 'reused'
    $buildExit = 0
    if (-not $NoBuild) {
        $buildExit = Invoke-Dotnet (@('build', $Project, ('--property:OutputPath=' + $OutputPath)) + $propertyArguments + $cpuArguments + @('--nologo'))
        if ($buildExit -eq 0) { $buildState = 'ok' }
    }

    # (4) One filter, one fresh TRX.
    if ($buildExit -eq 0) {
        $runExit = Invoke-Dotnet (@('run', '--project', $Project, '--no-build', ('--property:OutputPath=' + $OutputPath)) + $propertyArguments + @(
            '--', '--treenode-filter', $Filter, '--report-trx', '--report-trx-filename', 'run.trx',
            '--results-directory', $resultsDirectory))
    }
} finally {
    Exit-AntiphonBuildSlot -Lease $slot
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

$commit = 'unknown'
try {
    $rev = (& git rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and $rev) { $commit = ([string]$rev).Trim() }
} catch { }

# (6) The report line the bundle asks Code to produce, then the roster.
Write-Host ('CHECKPOINT {0} commit={1} build={2} filter={3} executed={4} passed={5} failed={6} skipped={7} trx={8} slot={9} waited={10}s' -f `
    $Name, $commit, $buildState, $Filter, $executed, $passed, $failed, $skipped, $trxPath, $slotState, $slotWaited)

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
