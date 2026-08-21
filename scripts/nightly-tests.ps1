<#
.SYNOPSIS
    Run the Windows-only Antiphon test suites in one foreground, sequential nightly pass and
    retain an honest machine-readable record of the outcome.

    These suites cannot all live in hosted CI: they need this machine's Docker, Windows pty,
    and browser environment. The job is scheduled via Windmill on server2
    (u/lndcobra/antiphon_nightly_tests), not a local Windows Scheduled Task. Do not add one.

    Each run writes logs/nightly/<yyyy-MM-dd>/summary.json and per-suite logs. The script never
    checks out, stashes, or otherwise disturbs in-flight work: it fetches, only rebases a clean
    master checkout, and otherwise records exactly which tree it tested.

    ASCII-only on purpose - this may be invoked by Windows PowerShell 5.1 through the Windmill
    SSH path. Alert delivery is intentionally not implemented here; CARD-0131 S2 owns it.
#>
param(
    [string[]]$Suites,
    [switch]$NoAlert
)

$ErrorActionPreference = 'Continue'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$allSuites = @('antiphon', 'agents-pty', 'client', 'e2e')
$suiteLabels = @{
    'antiphon' = 'Antiphon.Tests'
    'agents-pty' = 'Antiphon.Agents.Pty.Tests'
    'client' = 'client (vitest)'
    'e2e' = 'Antiphon.E2E'
}

if ($null -eq $Suites -or $Suites.Count -eq 0) {
    $selectedSuites = $allSuites
} else {
    $selectedSuites = @()
    foreach ($suite in $Suites) {
        $normalised = $suite.ToLowerInvariant()
        if ($allSuites -notcontains $normalised) {
            Write-Error "Unknown suite '$suite'. Valid suites: $($allSuites -join ', ')."
            exit 2
        }
        if ($selectedSuites -notcontains $normalised) {
            $selectedSuites += $normalised
        }
    }
    $selectedSuites = @($allSuites | Where-Object { $selectedSuites -contains $_ })
}

$startedAt = Get-Date
$runDate = $startedAt.ToString('yyyy-MM-dd')
$nightlyRoot = Join-Path $RepoRoot 'logs/nightly'
$runDirectory = Join-Path $nightlyRoot $runDate
$summaryPath = Join-Path $runDirectory 'summary.json'
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

function Write-RunLine {
    param([string]$Message)

    $line = "[$((Get-Date).ToString('o'))] $Message"
    Write-Host $line
}

function Invoke-LoggedNative {
    param(
        [string]$FilePath,
        [string[]]$CommandArguments,
        [string]$LogPath,
        [string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath $CommandArguments 2>&1 | Tee-Object -FilePath $LogPath | Out-Host
        $exitCode = $LASTEXITCODE
        return [int]$exitCode
    } catch {
        $_ | Out-String | Tee-Object -FilePath $LogPath -Append | Write-Host
        return 1
    } finally {
        Pop-Location
    }
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

function New-SuiteResult {
    param(
        [string]$Name,
        [string]$LogPath,
        [datetime]$SuiteStartedAt,
        [int]$ExitCode,
        [string]$AdditionalDetail
    )

    $completedAt = Get-Date
    $counts = Get-TestCounts -LogPath $LogPath -ExitCode $ExitCode
    $detail = if ([string]::IsNullOrEmpty($AdditionalDetail)) { $counts.detail } else { $AdditionalDetail }
    return [ordered]@{
        name = $Name
        log = $LogPath.Substring($RepoRoot.Length + 1).Replace('\', '/')
        startedAt = $SuiteStartedAt.ToString('o')
        completedAt = $completedAt.ToString('o')
        durationSeconds = [math]::Round(($completedAt - $SuiteStartedAt).TotalSeconds, 3)
        exitCode = $ExitCode
        countsParsed = $counts.parsed
        passed = $counts.passed
        failed = $counts.failed
        skipped = $counts.skipped
        detail = $detail
    }
}

function Remove-NightlyOutputs {
    $removed = 0
    $directories = Get-ChildItem -Path $RepoRoot -Directory -Recurse -Depth 3 -Filter 'bin-nightly' -ErrorAction SilentlyContinue
    foreach ($directory in $directories) {
        try {
            if (-not (Test-Path -LiteralPath $directory.FullName)) {
                continue
            }
            Remove-Item -LiteralPath $directory.FullName -Recurse -Force -Confirm:$false -ErrorAction Stop
            if (-not (Test-Path -LiteralPath $directory.FullName)) {
                $removed++
            }
        } catch {
            Write-RunLine "cleanup skipped $($directory.FullName): $($_.Exception.Message)"
        }
    }
    Write-RunLine "bin-nightly cleanup removed $removed directory(s)."
}

function Prune-OldRuns {
    $cutoff = (Get-Date).Date.AddDays(-14)
    Get-ChildItem -Path $nightlyRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $cutoff } |
        ForEach-Object {
            try {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force -Confirm:$false -ErrorAction Stop
                Write-RunLine "pruned old nightly run $($_.Name)"
            } catch {
                Write-RunLine "could not prune $($_.FullName): $($_.Exception.Message)"
            }
        }
}

$preflight = [ordered]@{}
$suiteResults = @()
$overallFailed = $false
$gitContext = [ordered]@{
    sha = $null
    branch = $null
    dirty = $null
    stamp = $null
    fetchExitCode = $null
    pullExitCode = $null
}

try {
    Prune-OldRuns

    Write-RunLine 'preflight: checking Docker responsiveness.'
    $dockerJob = Start-Job -ScriptBlock {
        $dockerOutput = & docker info 2>&1
        $dockerExitCode = $LASTEXITCODE
        [pscustomobject]@{
            exitCode = $dockerExitCode
            output = ($dockerOutput | Out-String)
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
        $preflight.docker = [ordered]@{ exitCode = $dockerExitCode; detail = $dockerOutput.Trim() }
        if ($dockerExitCode -ne 0) {
            $overallFailed = $true
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
        exitCode = $diskExitCode
        freeBytes = $freeBytes
        minimumFreeBytes = $minimumFreeBytes
        detail = "free $freeBytes bytes; minimum $minimumFreeBytes bytes"
    }
    if ($diskExitCode -ne 0) {
        $overallFailed = $true
    }

    Push-Location $RepoRoot
    try {
        $dirtyEntries = @(& git status --porcelain 2>&1)
        $statusExitCode = $LASTEXITCODE
        $branch = (& git branch --show-current 2>&1 | Select-Object -First 1).ToString().Trim()
        $branchExitCode = $LASTEXITCODE

        & git fetch origin 2>&1 | Tee-Object -FilePath (Join-Path $runDirectory 'git-fetch.log')
        $gitContext.fetchExitCode = $LASTEXITCODE
        if ($gitContext.fetchExitCode -ne 0) {
            $overallFailed = $true
        }

        $isDirty = $statusExitCode -ne 0 -or $dirtyEntries.Count -gt 0
        if (-not $isDirty -and $branchExitCode -eq 0 -and $branch -eq 'master') {
            & git pull --rebase 2>&1 | Tee-Object -FilePath (Join-Path $runDirectory 'git-pull.log')
            $gitContext.pullExitCode = $LASTEXITCODE
            if ($gitContext.pullExitCode -ne 0) {
                $overallFailed = $true
            }
        }

        $gitContext.sha = (& git rev-parse HEAD 2>&1 | Select-Object -First 1).ToString().Trim()
        $gitContext.branch = (& git branch --show-current 2>&1 | Select-Object -First 1).ToString().Trim()
        $gitContext.dirty = @(& git status --porcelain 2>&1).Count -gt 0
        if ($gitContext.branch -ne 'master') {
            $gitContext.stamp = "ON BRANCH $($gitContext.branch)"
        } elseif ($gitContext.dirty) {
            $gitContext.stamp = "DIRTY TREE - ran at $($gitContext.sha) without pull"
        } elseif ($null -ne $gitContext.pullExitCode -and $gitContext.pullExitCode -eq 0) {
            $gitContext.stamp = 'CLEAN MASTER - pulled with rebase'
        } else {
            $gitContext.stamp = "CLEAN MASTER - ran at $($gitContext.sha)"
        }
        Write-RunLine $gitContext.stamp
    } finally {
        Pop-Location
    }

    if ($preflight.docker.exitCode -ne 0 -or $preflight.disk.exitCode -ne 0) {
        Write-RunLine 'hard preflight failed; suite execution skipped.'
    } else {
        if ($selectedSuites -contains 'client' -or $selectedSuites -contains 'e2e') {
            # Do not use npm ci here: it deletes node_modules out from under the always-on Vite
            # client in the shared live tree. npm install updates dependencies without that wipe.
            $npmInstallLogPath = Join-Path $runDirectory 'client-install.log'
            Write-RunLine 'preparing client dependencies with npm install.'
            $npmInstallExitCode = Invoke-LoggedNative -FilePath 'npm.cmd' -CommandArguments @('install') -LogPath $npmInstallLogPath -WorkingDirectory (Join-Path $RepoRoot 'client')
            $preflight.npmInstall = [ordered]@{
                exitCode = $npmInstallExitCode
                log = $npmInstallLogPath.Substring($RepoRoot.Length + 1).Replace('\', '/')
            }
            if ($npmInstallExitCode -ne 0) {
                $overallFailed = $true
            }
        }

        if ($selectedSuites -contains 'antiphon') {
            $logPath = Join-Path $runDirectory 'antiphon-tests.log'
            $suiteStartedAt = Get-Date
            Write-RunLine 'running Antiphon.Tests.'
            $exitCode = Invoke-LoggedNative -FilePath 'dotnet' -CommandArguments @('run', '--project', 'tests/Antiphon.Tests', '--property:OutputPath=bin-nightly/') -LogPath $logPath -WorkingDirectory $RepoRoot
            $suiteResults += New-SuiteResult -Name $suiteLabels['antiphon'] -LogPath $logPath -SuiteStartedAt $suiteStartedAt -ExitCode $exitCode
            if ($exitCode -ne 0) { $overallFailed = $true }
        }

        if ($selectedSuites -contains 'agents-pty') {
            $logPath = Join-Path $runDirectory 'agents-pty-tests.log'
            $suiteStartedAt = Get-Date
            Write-RunLine 'running Antiphon.Agents.Pty.Tests after Antiphon.Tests has completed.'
            $exitCode = Invoke-LoggedNative -FilePath 'dotnet' -CommandArguments @('run', '--project', 'tests/Antiphon.Agents.Pty.Tests', '--property:OutputPath=bin-nightly/') -LogPath $logPath -WorkingDirectory $RepoRoot
            $suiteResults += New-SuiteResult -Name $suiteLabels['agents-pty'] -LogPath $logPath -SuiteStartedAt $suiteStartedAt -ExitCode $exitCode
            if ($exitCode -ne 0) { $overallFailed = $true }
        }

        if ($selectedSuites -contains 'client') {
            $logPath = Join-Path $runDirectory 'client-tests.log'
            $sourceLogPath = Join-Path $RepoRoot 'logs/client-tests.log'
            $suiteStartedAt = Get-Date
            Write-RunLine 'running client vitest through scripts/test-client.ps1.'
            Push-Location $RepoRoot
            try {
                & pwsh -NoLogo -File (Join-Path $RepoRoot 'scripts/test-client.ps1')
                $exitCode = $LASTEXITCODE
            } catch {
                Write-RunLine "client wrapper failed to start: $($_.Exception.Message)"
                $exitCode = 1
            } finally {
                Pop-Location
            }
            if (Test-Path -LiteralPath $sourceLogPath) {
                Copy-Item -LiteralPath $sourceLogPath -Destination $logPath -Force
            } else {
                "Client wrapper exited $exitCode, but logs/client-tests.log was not created." | Set-Content -LiteralPath $logPath
            }
            $suiteResults += New-SuiteResult -Name $suiteLabels['client'] -LogPath $logPath -SuiteStartedAt $suiteStartedAt -ExitCode $exitCode
            if ($exitCode -ne 0) { $overallFailed = $true }
        }

        if ($selectedSuites -contains 'e2e') {
            $logPath = Join-Path $runDirectory 'e2e-tests.log'
            $suiteStartedAt = Get-Date
            Write-RunLine 'building client bundle for E2E.'
            $buildExitCode = Invoke-LoggedNative -FilePath 'npm.cmd' -CommandArguments @('run', 'build') -LogPath $logPath -WorkingDirectory (Join-Path $RepoRoot 'client')
            if ($buildExitCode -ne 0) {
                $suiteResults += New-SuiteResult -Name $suiteLabels['e2e'] -LogPath $logPath -SuiteStartedAt $suiteStartedAt -ExitCode $buildExitCode -AdditionalDetail "client build failed with exit $buildExitCode; Antiphon.E2E was not run - see log"
                $overallFailed = $true
            } else {
                Write-RunLine 'running Antiphon.E2E.'
                $exitCode = Invoke-LoggedNative -FilePath 'dotnet' -CommandArguments @('run', '--project', 'tests/Antiphon.E2E', '--property:OutputPath=bin-nightly/') -LogPath $logPath -WorkingDirectory $RepoRoot
                $suiteResults += New-SuiteResult -Name $suiteLabels['e2e'] -LogPath $logPath -SuiteStartedAt $suiteStartedAt -ExitCode $exitCode
                if ($exitCode -ne 0) { $overallFailed = $true }
            }
        }
    }
} catch {
    $overallFailed = $true
    $preflight.unhandledError = $_.Exception.Message
    Write-RunLine "unhandled orchestrator error: $($_.Exception.Message)"
} finally {
    Remove-NightlyOutputs
    $completedAt = Get-Date
    $summary = [ordered]@{
        startedAt = $startedAt.ToString('o')
        completedAt = $completedAt.ToString('o')
        durationSeconds = [math]::Round(($completedAt - $startedAt).TotalSeconds, 3)
        sha = $gitContext.sha
        branch = $gitContext.branch
        dirty = $gitContext.dirty
        treeStamp = $gitContext.stamp
        selectedSuites = $selectedSuites
        noAlert = [bool]$NoAlert
        preflight = $preflight
        suites = $suiteResults
        succeeded = -not $overallFailed
    }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-RunLine "wrote $summaryPath"
}

if ($overallFailed) {
    exit 1
}

exit 0
