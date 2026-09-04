#requires -Version 5.1
<#
.SYNOPSIS
    Build and run the Antiphon nightly suites in an isolated checkout and write
    an honest machine-readable record of the outcome.

    CARD-0124. This script never clones, fetches, resets, or otherwise changes
    git state: scripts/nightly-run.ps1 owns sync of C:\Antiphon\nightly\checkout
    to origin/master. Do not point -RepoRoot at C:\src\Antiphon or at a path
    under C:\Antiphon\worktrees unless you pass -AllowSharedTree.

    Default suites: antiphon, agents-pty, client. E2E is opt-in via -Suites e2e.
    Headed tests stay off. Builds into the clone's own bin/; this script does
    not use an alternate output path.

    Recurring run is Windmill on server2 (u/lndcobra/antiphon_nightly_tests),
    not a local Windows Scheduled Task. Do not add one.

    ASCII-only on purpose - this may be invoked by Windows PowerShell 5.1
    through the Windmill SSH path.

.PARAMETER RepoRoot
    Checkout to build and test. Default: the directory containing this scripts
    folder. Must be the isolated nightly clone unless -AllowSharedTree.

.PARAMETER LogRoot
    Directory for this run's logs and summary.json. When omitted, a stamp
    folder is created under C:\Antiphon\nightly\logs.

.PARAMETER Suites
    Suite ids to run, comma-separated or repeated. Default antiphon,agents-pty,client.

.PARAMETER Sha
    Git sha the bootstrap resolved after sync. Recorded in summary.json.

.PARAMETER GitRef
    Ref label for the summary (default origin/master).

.PARAMETER AllowSharedTree
    Permit running against C:\src\Antiphon or a worktree. Off by default.

.PARAMETER WhatIf
    Stop after the shared-tree guard (and argument checks). Nothing is built.
#>
param(
    [string]$RepoRoot = '',
    [string]$LogRoot = '',
    [string[]]$Suites,
    [string]$Sha = '',
    [string]$GitRef = 'origin/master',
    [switch]$AllowSharedTree,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Continue'

$allSuites = @('antiphon', 'agents-pty', 'client', 'e2e')
$suiteLabels = @{
    'antiphon'    = 'Antiphon.Tests'
    'agents-pty'  = 'Antiphon.Agents.Pty.Tests'
    'client'      = 'client'
    'e2e'         = 'Antiphon.E2E'
}

$watchdogMs = @{
    'npm-ci'       = 10 * 60 * 1000
    'npm-build'    = 10 * 60 * 1000
    'dotnet-build' = 20 * 60 * 1000
    'antiphon'     = 60 * 60 * 1000
    'agents-pty'   = 20 * 60 * 1000
    'client'       = 20 * 60 * 1000
    'e2e'          = 20 * 60 * 1000
}

function ConvertTo-NightlyCanonicalPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $normalised = $Path.Replace([System.IO.Path]::AltDirectorySeparatorChar, [System.IO.Path]::DirectorySeparatorChar)
    $fullPath = [System.IO.Path]::GetFullPath($normalised)
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ($fullPath.Length -gt $root.Length) {
        $fullPath = $fullPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    }
    return $fullPath
}

function Test-SharedNightlyTree {
    param([string]$Path)
    $canonical = ConvertTo-NightlyCanonicalPath -Path $Path
    $sharedMain = ConvertTo-NightlyCanonicalPath -Path 'C:\src\Antiphon'
    $worktrees = ConvertTo-NightlyCanonicalPath -Path 'C:\Antiphon\worktrees'
    $sep = [System.IO.Path]::DirectorySeparatorChar
    if ([string]::Equals($canonical, $sharedMain, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($canonical.StartsWith($sharedMain + $sep, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ([string]::Equals($canonical, $worktrees, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($canonical.StartsWith($worktrees + $sep, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $false
}

function Write-RunLine {
    param([string]$Message)
    $line = "[$((Get-Date).ToString('o'))] $Message"
    Write-Host $line
}

function Invoke-WatchedProcess {
    param(
        [string]$FilePath,
        [string[]]$CommandArguments,
        [string]$LogPath,
        [string]$WorkingDirectory,
        [int]$TimeoutMilliseconds
    )

    $stdoutPath = $LogPath + '.stdout.tmp'
    $stderrPath = $LogPath + '.stderr.tmp'
    foreach ($p in @($stdoutPath, $stderrPath, $LogPath)) {
        if (Test-Path -LiteralPath $p) {
            Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
        }
    }

    $startParams = @{
        FilePath               = $FilePath
        WorkingDirectory       = $WorkingDirectory
        PassThru               = $true
        NoNewWindow            = $true
        RedirectStandardOutput = $stdoutPath
        RedirectStandardError  = $stderrPath
    }
    if ($CommandArguments -and $CommandArguments.Count -gt 0) {
        $startParams.ArgumentList = $CommandArguments
    }

    try {
        $proc = Start-Process @startParams
    } catch {
        $_ | Out-String | Set-Content -LiteralPath $LogPath -Encoding UTF8
        return [pscustomobject]@{ ExitCode = 1; TimedOut = $false }
    }
    if ($null -eq $proc) {
        ('failed to start {0}' -f $FilePath) | Set-Content -LiteralPath $LogPath -Encoding UTF8
        return [pscustomobject]@{ ExitCode = 1; TimedOut = $false }
    }

    $finished = $false
    try {
        $finished = $proc.WaitForExit($TimeoutMilliseconds)
    } catch {
        $finished = $false
    }
    $timedOut = -not $finished
    $exitCode = 1
    if ($timedOut) {
        & taskkill.exe /PID $proc.Id /T /F 2>$null | Out-Null
        try { $null = $proc.WaitForExit(15000) } catch { }
    } else {
        $exitCode = [int]$proc.ExitCode
    }

    $chunks = @()
    foreach ($p in @($stdoutPath, $stderrPath)) {
        if (Test-Path -LiteralPath $p) {
            $chunks += Get-Content -LiteralPath $p -ErrorAction SilentlyContinue
        }
    }
    if ($timedOut) {
        $chunks += ('TIMEOUT after {0} ms; process tree killed.' -f $TimeoutMilliseconds)
        $exitCode = 1
    }
    $chunks | Set-Content -LiteralPath $LogPath -Encoding UTF8
    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    return [pscustomobject]@{ ExitCode = $exitCode; TimedOut = $timedOut }
}

function Get-TestCounts {
    param([string]$LogPath, [int]$ExitCode)

    $result = [ordered]@{
        parsed = $false
        passed = $null
        failed = $null
        skipped = $null
        detail = "exit $ExitCode, counts unparsed - see log"
    }

    if (-not (Test-Path -LiteralPath $LogPath)) {
        return [pscustomobject]$result
    }

    $tail = (Get-Content -LiteralPath $LogPath -Tail 120 -ErrorAction SilentlyContinue) -join "`n"
    $tail = [regex]::Replace($tail, '[\x1B]\[[0-?]*[ -/]*[@-~]', '')
    $summaryLines = [regex]::Matches($tail, '(?im)^\s*(?:Tests?|Test Files)\s+.+$')
    foreach ($summaryLine in $summaryLines) {
        $counts = @{}
        foreach ($count in [regex]::Matches($summaryLine.Value, '(?i)(\d+)\s+(passed|failed|skipped)')) {
            $counts[$count.Groups[2].Value.ToLowerInvariant()] = [int]$count.Groups[1].Value
        }
        if ($counts.ContainsKey('passed') -or $counts.ContainsKey('failed') -or $counts.ContainsKey('skipped')) {
            $result.parsed = $true
            $result.passed = if ($counts.ContainsKey('passed')) { $counts['passed'] } else { 0 }
            $result.failed = if ($counts.ContainsKey('failed')) { $counts['failed'] } else { 0 }
            $result.skipped = if ($counts.ContainsKey('skipped')) { $counts['skipped'] } else { 0 }
            $result.detail = 'counts parsed from summary line'
        }
    }

    if (-not $result.parsed) {
        $tunitMatch = [regex]::Match(
            $tail,
            '(?is)Total:\s*\d+.*?Passed:\s*(?<passed>\d+).*?Failed:\s*(?<failed>\d+).*?Skipped:\s*(?<skipped>\d+)')
        if ($tunitMatch.Success) {
            $result.parsed = $true
            $result.passed = [int]$tunitMatch.Groups['passed'].Value
            $result.failed = [int]$tunitMatch.Groups['failed'].Value
            $result.skipped = [int]$tunitMatch.Groups['skipped'].Value
            $result.detail = 'counts parsed from TUnit summary'
        }
    }

    if (-not $result.parsed) {
        $tunitMatch = [regex]::Match(
            $tail,
            '(?is)Test run summary:.*?total:\s*\d+.*?failed:\s*(?<failed>\d+).*?succeeded:\s*(?<passed>\d+).*?skipped:\s*(?<skipped>\d+)')
        if ($tunitMatch.Success) {
            $result.parsed = $true
            $result.passed = [int]$tunitMatch.Groups['passed'].Value
            $result.failed = [int]$tunitMatch.Groups['failed'].Value
            $result.skipped = [int]$tunitMatch.Groups['skipped'].Value
            $result.detail = 'counts parsed from TUnit summary'
        }
    }

    return [pscustomobject]$result
}

function Get-FailedTests {
    param([string]$LogPath, [int]$Cap = 25)

    $items = @()
    if (-not (Test-Path -LiteralPath $LogPath)) {
        return @{ names = @(); details = @(); extra = 0 }
    }
    $text = Get-Content -LiteralPath $LogPath -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($text)) {
        return @{ names = @(); details = @(); extra = 0 }
    }
    $text = [regex]::Replace($text, '[\x1B]\[[0-?]*[ -/]*[@-~]', '')
    $lines = $text -split '\r?\n'
    $times = [char]0xD7

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $name = $null
        $m = [regex]::Match($line, '^failed\s+(.+?)\s+\(')
        if ($m.Success) {
            $name = $m.Groups[1].Value.Trim()
        }
        if (-not $name) {
            $m = [regex]::Match($line, '^\s*FAIL\s+(\S+)\s+>\s+(.+?)\s*$')
            if ($m.Success) {
                $name = ($m.Groups[1].Value.Trim() + ' > ' + $m.Groups[2].Value.Trim())
            }
        }
        if (-not $name) {
            $m = [regex]::Match($line, ('^\s*[' + [regex]::Escape([string]$times) + 'x]\s+(.+?)\s*$'))
            if ($m.Success) {
                $name = $m.Groups[1].Value.Trim()
                $name = [regex]::Replace($name, '\s+\d+m?s$', '')
            }
        }
        if (-not $name) { continue }

        $detail = ''
        for ($j = $i + 1; $j -lt $lines.Count; $j++) {
            $next = $lines[$j].Trim()
            if ([string]::IsNullOrEmpty($next)) { continue }
            if ($next -match '^failed\s+' ) { break }
            if ($next -match '^\s*FAIL\s+') { break }
            $detail = $next
            if ($detail.Length -gt 300) { $detail = $detail.Substring(0, 300) }
            break
        }
        $items += [pscustomobject]@{ name = $name; detail = $detail }
    }

    $seen = @{}
    $unique = @()
    foreach ($it in $items) {
        if ($seen.ContainsKey($it.name)) { continue }
        $seen[$it.name] = $true
        $unique += $it
    }
    $extra = 0
    if ($unique.Count -gt $Cap) {
        $extra = $unique.Count - $Cap
        $unique = $unique[0..($Cap - 1)]
    }
    return @{
        names   = @($unique | ForEach-Object { $_.name })
        details = @($unique)
        extra   = $extra
    }
}

function Get-BuildErrorLines {
    param([string]$LogPath, [int]$Cap = 30)
    $lines = @()
    if (-not (Test-Path -LiteralPath $LogPath)) { return $lines }
    $text = Get-Content -LiteralPath $LogPath -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($text)) { return $lines }
    $text = [regex]::Replace($text, '[\x1B]\[[0-?]*[ -/]*[@-~]', '')
    foreach ($m in [regex]::Matches($text, '(?m)^.*\berror (?:CS|TS)\d+:.*$')) {
        $line = $m.Value.Trim()
        if ($line -and ($lines -notcontains $line)) { $lines += $line }
        if ($lines.Count -ge $Cap) { return $lines }
    }
    foreach ($m in [regex]::Matches($text, '(?m)^npm ERR!.*$')) {
        $line = $m.Value.Trim()
        if ($line -and ($lines -notcontains $line)) { $lines += $line }
        if ($lines.Count -ge $Cap) { return $lines }
    }
    return $lines
}

function New-SuiteResult {
    param(
        [string]$Id,
        [string]$Name,
        [string]$LogPath,
        [datetime]$SuiteStartedAt,
        [int]$ExitCode,
        [string]$AdditionalDetail,
        [bool]$TimedOut,
        [bool]$Skipped
    )

    $completedAt = Get-Date
    $failed = Get-FailedTests -LogPath $LogPath
    $counts = Get-TestCounts -LogPath $LogPath -ExitCode $ExitCode
    $detail = if ([string]::IsNullOrEmpty($AdditionalDetail)) { $counts.detail } else { $AdditionalDetail }
    if ($TimedOut) {
        $detail = 'TIMEOUT - killed by watchdog; see log'
    }
    $result = 'pass'
    if ($Skipped) { $result = 'skipped' }
    elseif ($TimedOut) { $result = 'TIMEOUT' }
    elseif ($ExitCode -ne 0) { $result = 'FAIL' }

    return [ordered]@{
        id              = $Id
        name            = $Name
        log             = $LogPath
        startedAt       = $SuiteStartedAt.ToString('o')
        completedAt     = $completedAt.ToString('o')
        durationSeconds = [math]::Round(($completedAt - $SuiteStartedAt).TotalSeconds, 3)
        exitCode        = $ExitCode
        timedOut        = [bool]$TimedOut
        skipped         = [bool]$Skipped
        result          = $result
        countsParsed    = $counts.parsed
        passed          = $counts.passed
        failed          = $counts.failed
        skippedCount    = $counts.skipped
        detail          = $detail
        failedTests     = @($failed.names)
        failedDetails   = @($failed.details)
        failedExtra     = [int]$failed.extra
    }
}

function New-BuildResult {
    param(
        [string]$Name,
        [string]$LogPath,
        [datetime]$StartedAt,
        [int]$ExitCode,
        [bool]$TimedOut,
        [bool]$Skipped,
        [string]$Detail
    )
    $completedAt = Get-Date
    $result = 'pass'
    if ($Skipped) { $result = 'skipped' }
    elseif ($TimedOut) { $result = 'TIMEOUT' }
    elseif ($ExitCode -ne 0) { $result = 'FAIL' }
    $errors = @()
    if ($ExitCode -ne 0 -and -not $Skipped) {
        $errors = @(Get-BuildErrorLines -LogPath $LogPath)
    }
    return [ordered]@{
        name            = $Name
        log             = $LogPath
        startedAt       = $StartedAt.ToString('o')
        completedAt     = $completedAt.ToString('o')
        durationSeconds = [math]::Round(($completedAt - $StartedAt).TotalSeconds, 3)
        exitCode        = $ExitCode
        timedOut        = [bool]$TimedOut
        skipped         = [bool]$Skipped
        result          = $result
        detail          = $Detail
        errors          = $errors
    }
}

function Get-ConcurrentCensus {
    $antiphon = @(Get-Process -Name 'Antiphon.Tests' -ErrorAction SilentlyContinue)
    $pty = @(Get-Process -Name 'Antiphon.Agents.Pty.Tests' -ErrorAction SilentlyContinue)
    $e2e = @(Get-Process -Name 'Antiphon.E2E' -ErrorAction SilentlyContinue)
    $vitest = 0
    try {
        $vitest = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '(?i)node' -and $_.CommandLine -match 'vitest' }).Count
    } catch {
        $vitest = 0
    }
    $total = $antiphon.Count + $pty.Count + $e2e.Count + $vitest
    return [ordered]@{
        antiphonTests  = $antiphon.Count
        agentsPtyTests = $pty.Count
        e2eTests       = $e2e.Count
        vitest         = $vitest
        total          = $total
    }
}

function Get-TestExePath {
    param([string]$ProjectName)
    return (Join-Path $RepoRoot ("tests\{0}\bin\Debug\net9.0\{0}.exe" -f $ProjectName))
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
try {
    $RepoRoot = ConvertTo-NightlyCanonicalPath -Path $RepoRoot
} catch {
    Write-Error ("-RepoRoot '{0}' could not be canonicalised: {1}" -f $RepoRoot, $_.Exception.Message)
    exit 2
}

$selectedSuites = @()
if ($null -eq $Suites -or $Suites.Count -eq 0) {
    $selectedSuites = @('antiphon', 'agents-pty', 'client')
} else {
    $seen = @{}
    foreach ($suite in $Suites) {
        foreach ($part in @($suite -split ',')) {
            $normalised = $part.Trim().ToLowerInvariant()
            if ([string]::IsNullOrWhiteSpace($normalised)) { continue }
            if ($allSuites -notcontains $normalised) {
                Write-Error ("Unknown suite '{0}'. Valid suites: {1}." -f $suite, ($allSuites -join ', '))
                exit 2
            }
            if (-not $seen.ContainsKey($normalised)) {
                $seen[$normalised] = $true
                $selectedSuites += $normalised
            }
        }
    }
    $selectedSuites = @($allSuites | Where-Object { $seen.ContainsKey($_) })
}

if (Test-SharedNightlyTree -Path $RepoRoot) {
    if (-not $AllowSharedTree) {
        Write-Host 'REFUSED: nightly-tests.ps1 will not run in the shared tree.'
        Write-Host ('  RepoRoot: {0}' -f $RepoRoot)
        Write-Host '  Isolated clone: C:\Antiphon\nightly\checkout'
        Write-Host '  The shared checkout is what the AppHost builds from; a scheduled run must not touch it.'
        Write-Host '  Re-run with -AllowSharedTree only for a deliberate shared-tree run.'
        exit 3
    }
    Write-RunLine ('WARNING: -AllowSharedTree is set; running in shared tree {0}' -f $RepoRoot)
}

if ($WhatIf) {
    Write-Host ('WhatIf: would run suites {0} in {1}' -f ($selectedSuites -join ','), $RepoRoot)
    exit 0
}

$startedAt = Get-Date
if ([string]::IsNullOrWhiteSpace($LogRoot)) {
    $stamp = $startedAt.ToString('yyyy-MM-dd-HHmm')
    $LogRoot = Join-Path 'C:\Antiphon\nightly\logs' $stamp
}
try {
    $LogRoot = ConvertTo-NightlyCanonicalPath -Path $LogRoot
} catch { }
New-Item -ItemType Directory -Path $LogRoot -Force | Out-Null
$summaryPath = Join-Path $LogRoot 'summary.json'
$buildLogPath = Join-Path $LogRoot 'build.log'

$preflight = [ordered]@{}
$suiteResults = @()
$buildResults = @()
$overallFailed = $false
$outcome = 'green'
$hadTimeout = $false
$hadBuildFailure = $false
$npmOk = $true
$dotnetOk = $true

function Set-Outcome {
    param([string]$Class)
    $order = @('PREFLIGHT', 'BUILD', 'TESTS', 'TIMEOUT')
    $current = $script:outcome
    if ($current -eq 'green') {
        $script:outcome = $Class
        return
    }
    $curIdx = [array]::IndexOf($order, $current)
    $newIdx = [array]::IndexOf($order, $Class)
    if ($newIdx -ge 0 -and ($curIdx -lt 0 -or $newIdx -lt $curIdx)) {
        $script:outcome = $Class
    }
}

try {
    $preflight.concurrent = Get-ConcurrentCensus
    Write-RunLine ('preflight: concurrent test processes at start: {0}' -f $preflight.concurrent.total)

    Write-RunLine 'preflight: checking Docker responsiveness.'
    $dockerJob = Start-Job -ScriptBlock {
        $dockerOutput = & docker info 2>&1
        $dockerExitCode = $LASTEXITCODE
        [pscustomobject]@{
            exitCode = $dockerExitCode
            output   = ($dockerOutput | Out-String)
        }
    }
    try {
        $dockerCompleted = Wait-Job -Job $dockerJob -Timeout 30
        $dockerResult = Receive-Job -Job $dockerJob -ErrorAction SilentlyContinue | Select-Object -Last 1
        $dockerOutput = if ($null -ne $dockerResult) { $dockerResult.output } else { '' }
        $dockerExitCode = if ($null -ne $dockerResult -and $null -ne $dockerResult.exitCode) { [int]$dockerResult.exitCode } else { 1 }
        if ($null -eq $dockerCompleted -or $dockerJob.State -ne 'Completed') {
            $dockerExitCode = 1
            Stop-Job -Job $dockerJob -ErrorAction SilentlyContinue
            $dockerOutput += 'docker info timed out after 30 seconds.'
        }
        $dockerVersion = $null
        $verMatch = [regex]::Match([string]$dockerOutput, 'Server Version:\s*(\S+)')
        if ($verMatch.Success) { $dockerVersion = $verMatch.Groups[1].Value }
        $preflight.docker = [ordered]@{
            exitCode = $dockerExitCode
            version  = $dockerVersion
            detail   = ([string]$dockerOutput).Trim()
        }
        if ($dockerExitCode -ne 0) {
            $overallFailed = $true
            Set-Outcome PREFLIGHT
        }
    } finally {
        Remove-Job -Job $dockerJob -Force -ErrorAction SilentlyContinue
    }

    $driveName = (Split-Path -Path $RepoRoot -Qualifier).TrimEnd(':')
    $drive = Get-PSDrive -Name $driveName -ErrorAction SilentlyContinue
    $freeBytes = if ($null -ne $drive) { [int64]$drive.Free } else { 0 }
    $minimumFreeBytes = 10GB
    $diskExitCode = if ($freeBytes -ge $minimumFreeBytes) { 0 } else { 1 }
    $preflight.disk = [ordered]@{
        exitCode          = $diskExitCode
        freeBytes         = $freeBytes
        minimumFreeBytes  = $minimumFreeBytes
        detail            = "free $freeBytes bytes; minimum $minimumFreeBytes bytes"
    }
    if ($diskExitCode -ne 0) {
        $overallFailed = $true
        Set-Outcome PREFLIGHT
    }

    if ($preflight.docker.exitCode -ne 0 -or $preflight.disk.exitCode -ne 0) {
        Write-RunLine 'hard preflight failed; build and suite execution skipped.'
    } else {
        $clientDir = Join-Path $RepoRoot 'client'

        $npmCiStarted = Get-Date
        Write-RunLine 'build: npm ci in client/.'
        $npmCi = Invoke-WatchedProcess -FilePath "$env:ComSpec" -CommandArguments @('/c', 'npm.cmd', 'ci') `
            -LogPath (Join-Path $LogRoot 'npm-ci.log') -WorkingDirectory $clientDir -TimeoutMilliseconds $watchdogMs['npm-ci']
        $npmCiDetail = "exit $($npmCi.ExitCode)"
        if ($npmCi.TimedOut) { $npmCiDetail = 'TIMEOUT'; $hadTimeout = $true; Set-Outcome TIMEOUT }
        if ($npmCi.ExitCode -ne 0) {
            $npmOk = $false
            $hadBuildFailure = $true
            $overallFailed = $true
            if (-not $npmCi.TimedOut) { Set-Outcome BUILD }
            $errs = @(Get-BuildErrorLines -LogPath (Join-Path $LogRoot 'npm-ci.log'))
            if ($errs.Count -gt 0) { $npmCiDetail = $errs[0] }
        }
        $buildResults += New-BuildResult -Name 'npm ci' -LogPath (Join-Path $LogRoot 'npm-ci.log') `
            -StartedAt $npmCiStarted -ExitCode $npmCi.ExitCode -TimedOut $npmCi.TimedOut -Skipped $false -Detail $npmCiDetail

        $npmBuildStarted = Get-Date
        if (-not $npmOk) {
            Write-RunLine 'build: npm run build skipped because npm ci failed.'
            $buildResults += New-BuildResult -Name 'npm run build' -LogPath (Join-Path $LogRoot 'npm-build.log') `
                -StartedAt $npmBuildStarted -ExitCode 1 -TimedOut $false -Skipped $true -Detail 'skipped: npm ci failed'
        } else {
            Write-RunLine 'build: npm run build in client/.'
            $npmBuild = Invoke-WatchedProcess -FilePath "$env:ComSpec" -CommandArguments @('/c', 'npm.cmd', 'run', 'build') `
                -LogPath (Join-Path $LogRoot 'npm-build.log') -WorkingDirectory $clientDir -TimeoutMilliseconds $watchdogMs['npm-build']
            $npmBuildDetail = "exit $($npmBuild.ExitCode)"
            if ($npmBuild.TimedOut) { $npmBuildDetail = 'TIMEOUT'; $hadTimeout = $true; Set-Outcome TIMEOUT }
            if ($npmBuild.ExitCode -ne 0) {
                $npmOk = $false
                $hadBuildFailure = $true
                $overallFailed = $true
                if (-not $npmBuild.TimedOut) { Set-Outcome BUILD }
                $errs = @(Get-BuildErrorLines -LogPath (Join-Path $LogRoot 'npm-build.log'))
                if ($errs.Count -gt 0) { $npmBuildDetail = ($errs | Select-Object -First 5) -join ' | ' }
            }
            $buildResults += New-BuildResult -Name 'npm run build' -LogPath (Join-Path $LogRoot 'npm-build.log') `
                -StartedAt $npmBuildStarted -ExitCode $npmBuild.ExitCode -TimedOut $npmBuild.TimedOut -Skipped $false -Detail $npmBuildDetail
        }

        $dotnetStarted = Get-Date
        Write-RunLine 'build: dotnet build Antiphon.sln -c Debug.'
        $dotnetBuild = Invoke-WatchedProcess -FilePath 'dotnet' -CommandArguments @('build', 'Antiphon.sln', '-c', 'Debug', '--nologo') `
            -LogPath (Join-Path $LogRoot 'dotnet-build.log') -WorkingDirectory $RepoRoot -TimeoutMilliseconds $watchdogMs['dotnet-build']
        $dotnetDetail = "exit $($dotnetBuild.ExitCode)"
        if ($dotnetBuild.TimedOut) { $dotnetDetail = 'TIMEOUT'; $hadTimeout = $true; Set-Outcome TIMEOUT }
        if ($dotnetBuild.ExitCode -ne 0) {
            $dotnetOk = $false
            $hadBuildFailure = $true
            $overallFailed = $true
            if (-not $dotnetBuild.TimedOut) { Set-Outcome BUILD }
            $errs = @(Get-BuildErrorLines -LogPath (Join-Path $LogRoot 'dotnet-build.log'))
            if ($errs.Count -gt 0) { $dotnetDetail = ($errs | Select-Object -First 5) -join ' | ' }
        }
        $buildResults += New-BuildResult -Name 'dotnet build Antiphon.sln' -LogPath (Join-Path $LogRoot 'dotnet-build.log') `
            -StartedAt $dotnetStarted -ExitCode $dotnetBuild.ExitCode -TimedOut $dotnetBuild.TimedOut -Skipped $false -Detail $dotnetDetail

        $buildChunks = @()
        foreach ($stepName in @('npm-ci.log', 'npm-build.log', 'dotnet-build.log')) {
            $p = Join-Path $LogRoot $stepName
            if (Test-Path -LiteralPath $p) {
                $buildChunks += "----- $stepName -----"
                $buildChunks += Get-Content -LiteralPath $p -ErrorAction SilentlyContinue
            }
        }
        $buildChunks | Set-Content -LiteralPath $buildLogPath -Encoding UTF8

        $skipClient = -not $npmOk
        $skipDotnetSuites = -not $dotnetOk

        foreach ($suiteId in $selectedSuites) {
            $label = $suiteLabels[$suiteId]
            $logPath = Join-Path $LogRoot ($suiteId + '-tests.log')
            $suiteStartedAt = Get-Date
            $needsDotnet = ($suiteId -eq 'antiphon' -or $suiteId -eq 'agents-pty' -or $suiteId -eq 'e2e')
            $needsNpm = ($suiteId -eq 'client' -or $suiteId -eq 'e2e')
            if ($needsNpm -and $skipClient) {
                Write-RunLine ("skipping {0}: build failed." -f $label)
                "skipped: build failed" | Set-Content -LiteralPath $logPath -Encoding UTF8
                $suiteResults += New-SuiteResult -Id $suiteId -Name $label -LogPath $logPath -SuiteStartedAt $suiteStartedAt `
                    -ExitCode 1 -AdditionalDetail 'skipped: build failed' -TimedOut $false -Skipped $true
                continue
            }
            if ($needsDotnet -and $skipDotnetSuites) {
                Write-RunLine ("skipping {0}: build failed." -f $label)
                "skipped: build failed" | Set-Content -LiteralPath $logPath -Encoding UTF8
                $suiteResults += New-SuiteResult -Id $suiteId -Name $label -LogPath $logPath -SuiteStartedAt $suiteStartedAt `
                    -ExitCode 1 -AdditionalDetail 'skipped: build failed' -TimedOut $false -Skipped $true
                continue
            }

            if ($suiteId -eq 'antiphon') {
                Write-RunLine 'running Antiphon.Tests from the built exe.'
                $exe = Get-TestExePath -ProjectName 'Antiphon.Tests'
                if (-not (Test-Path -LiteralPath $exe)) {
                    "missing $exe" | Set-Content -LiteralPath $logPath -Encoding UTF8
                    $suiteResults += New-SuiteResult -Id $suiteId -Name $label -LogPath $logPath -SuiteStartedAt $suiteStartedAt `
                        -ExitCode 1 -AdditionalDetail 'built exe missing' -TimedOut $false -Skipped $false
                    $overallFailed = $true
                    Set-Outcome TESTS
                    continue
                }
                $run = Invoke-WatchedProcess -FilePath $exe -CommandArguments @('--no-progress', '--no-ansi') `
                    -LogPath $logPath -WorkingDirectory $RepoRoot -TimeoutMilliseconds $watchdogMs['antiphon']
            } elseif ($suiteId -eq 'agents-pty') {
                Write-RunLine 'running Antiphon.Agents.Pty.Tests after Antiphon.Tests has completed.'
                $exe = Get-TestExePath -ProjectName 'Antiphon.Agents.Pty.Tests'
                if (-not (Test-Path -LiteralPath $exe)) {
                    "missing $exe" | Set-Content -LiteralPath $logPath -Encoding UTF8
                    $suiteResults += New-SuiteResult -Id $suiteId -Name $label -LogPath $logPath -SuiteStartedAt $suiteStartedAt `
                        -ExitCode 1 -AdditionalDetail 'built exe missing' -TimedOut $false -Skipped $false
                    $overallFailed = $true
                    Set-Outcome TESTS
                    continue
                }
                $run = Invoke-WatchedProcess -FilePath $exe -CommandArguments @('--no-progress', '--no-ansi') `
                    -LogPath $logPath -WorkingDirectory $RepoRoot -TimeoutMilliseconds $watchdogMs['agents-pty']
            } elseif ($suiteId -eq 'client') {
                Write-RunLine 'running client vitest through scripts/test-client.ps1.'
                $wrapper = Join-Path $RepoRoot 'scripts\test-client.ps1'
                $run = Invoke-WatchedProcess -FilePath 'pwsh' -CommandArguments @('-NoProfile', '-NoLogo', '-File', $wrapper) `
                    -LogPath $logPath -WorkingDirectory $RepoRoot -TimeoutMilliseconds $watchdogMs['client']
                $sourceLogPath = Join-Path $RepoRoot 'logs\client-tests.log'
                if (Test-Path -LiteralPath $sourceLogPath) {
                    $wrapperText = Get-Content -LiteralPath $sourceLogPath -ErrorAction SilentlyContinue
                    if ($wrapperText) {
                        Add-Content -LiteralPath $logPath -Value $wrapperText -Encoding UTF8
                    }
                }
            } elseif ($suiteId -eq 'e2e') {
                Write-RunLine 'running Antiphon.E2E from the built exe.'
                $exe = Get-TestExePath -ProjectName 'Antiphon.E2E'
                if (-not (Test-Path -LiteralPath $exe)) {
                    "missing $exe" | Set-Content -LiteralPath $logPath -Encoding UTF8
                    $suiteResults += New-SuiteResult -Id $suiteId -Name $label -LogPath $logPath -SuiteStartedAt $suiteStartedAt `
                        -ExitCode 1 -AdditionalDetail 'built exe missing' -TimedOut $false -Skipped $false
                    $overallFailed = $true
                    Set-Outcome TESTS
                    continue
                }
                $run = Invoke-WatchedProcess -FilePath $exe -CommandArguments @('--no-progress', '--no-ansi') `
                    -LogPath $logPath -WorkingDirectory $RepoRoot -TimeoutMilliseconds $watchdogMs['e2e']
            }

            $suiteResults += New-SuiteResult -Id $suiteId -Name $label -LogPath $logPath -SuiteStartedAt $suiteStartedAt `
                -ExitCode $run.ExitCode -AdditionalDetail '' -TimedOut $run.TimedOut -Skipped $false
            if ($run.TimedOut) {
                $overallFailed = $true
                $hadTimeout = $true
                Set-Outcome TIMEOUT
            } elseif ($run.ExitCode -ne 0) {
                $overallFailed = $true
                Set-Outcome TESTS
            }
        }
    }
} catch {
    $overallFailed = $true
    $preflight.unhandledError = $_.Exception.Message
    Write-RunLine "unhandled orchestrator error: $($_.Exception.Message)"
    if ($outcome -eq 'green') { $outcome = 'TESTS' }
} finally {
    $completedAt = Get-Date
    if ($overallFailed -and $outcome -eq 'green') {
        if ($hadTimeout) { $outcome = 'TIMEOUT' }
        elseif ($hadBuildFailure) { $outcome = 'BUILD' }
        else { $outcome = 'TESTS' }
    }
    $summary = [ordered]@{
        startedAt         = $startedAt.ToString('o')
        completedAt       = $completedAt.ToString('o')
        durationSeconds   = [math]::Round(($completedAt - $startedAt).TotalSeconds, 3)
        sha               = $Sha
        gitRef            = $GitRef
        clone             = $RepoRoot
        logDir            = $LogRoot
        selectedSuites    = $selectedSuites
        outcome           = $outcome
        succeeded         = -not $overallFailed
        preflight         = $preflight
        builds            = $buildResults
        suites            = $suiteResults
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-RunLine "wrote $summaryPath"
}

if ($overallFailed) {
    exit 1
}

exit 0
