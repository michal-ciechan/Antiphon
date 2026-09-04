#requires -Version 5.1
<#
.SYNOPSIS
    Bootstrap for the Antiphon nightly: lock, sync the isolated clone, run
    tests, then file a card.

    CARD-0124. Windmill calls this script (from C:\src\Antiphon\scripts, which
    may be stale). After syncing C:\Antiphon\nightly\checkout to origin/master,
    if the clone's copy of this file differs, that copy is re-exec'd with
    -NoSync so the version that runs is always the one on the ref.

    Recurring run is Windmill on server2 (u/lndcobra/antiphon_nightly_tests),
    not a local Windows Scheduled Task. Do not add one.

    ASCII-only on purpose - parseable under Windows PowerShell 5.1.

.PARAMETER Suites
    Forwarded to nightly-tests.ps1. Default antiphon,agents-pty,client.

.PARAMETER NoReport
    Skip nightly-report.ps1 (still writes last-run.json).

.PARAMETER NoSync
    Skip clone/fetch/reset. Used by the self-update hop.

.PARAMETER CheckoutRoot
    Isolated clone. Default C:\Antiphon\nightly\checkout.

.PARAMETER LogRoot
    Parent of per-run stamp folders. Default C:\Antiphon\nightly\logs.

.PARAMETER Ref
    Git ref to reset the clone to. Default master (origin/master after fetch).
    The schedule never passes this; S2 positive control does.
#>
param(
    [string[]]$Suites,
    [switch]$NoReport,
    [switch]$NoSync,
    [string]$CheckoutRoot = 'C:\Antiphon\nightly\checkout',
    [string]$LogRoot = 'C:\Antiphon\nightly\logs',
    [string]$Ref = 'master',
    [string]$RemoteUrl = 'https://github.com/michal-ciechan/Antiphon'
)

$ErrorActionPreference = 'Continue'

$nightlyRoot = 'C:\Antiphon\nightly'
$lockPath = Join-Path $nightlyRoot 'run.lock'
$lastRunPath = Join-Path $nightlyRoot 'last-run.json'
$script:ownsLock = $false
$script:runDir = $null
$script:sha = ''
$script:startedAt = Get-Date
$script:testExit = 0
$script:reportExit = 0
$script:unchanged = $false
$script:hopped = $false
$script:recordRun = $false
$script:summaryPath = $null

function Write-RunLine {
    param([string]$Message)
    Write-Host ('[{0}] {1}' -f (Get-Date).ToString('o'), $Message)
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

function Get-ParentProcessId {
    try {
        return (Get-CimInstance Win32_Process -Filter ("ProcessId={0}" -f $PID) -ErrorAction Stop).ParentProcessId
    } catch {
        return 0
    }
}

function ConvertTo-UtcStamp {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    try {
        $invariant = [cultureinfo]::InvariantCulture
        if ($Text -match '(Z|[+-]\d{2}:\d{2})$') {
            return [datetimeoffset]::Parse($Text, $invariant).UtcDateTime
        }
        $styles = [System.Globalization.DateTimeStyles]::AssumeUniversal -bor `
                  [System.Globalization.DateTimeStyles]::AdjustToUniversal
        return [datetime]::Parse($Text, $invariant, $styles)
    } catch {
        return $null
    }
}

function Enter-NightlyLock {
    New-Item -ItemType Directory -Path $nightlyRoot -Force | Out-Null
    if (Test-Path -LiteralPath $lockPath) {
        $line = (Get-Content -LiteralPath $lockPath -TotalCount 1 -ErrorAction SilentlyContinue)
        $parts = @([string]$line -split '\s+', 2)
        $lockPid = 0
        [void][int]::TryParse($parts[0], [ref]$lockPid)
        $alive = $false
        if ($lockPid -gt 0) {
            try { $null = Get-Process -Id $lockPid -ErrorAction Stop; $alive = $true } catch { $alive = $false }
        }
        $stamp = $null
        if ($parts.Count -gt 1) { $stamp = ConvertTo-UtcStamp $parts[1] }
        $ageMin = 9999
        if ($null -ne $stamp) {
            $ageMin = ([datetime]::UtcNow - $stamp.ToUniversalTime()).TotalMinutes
        }
        $parent = Get-ParentProcessId
        if ($alive -and $lockPid -eq $parent) {
            $script:ownsLock = $false
            Write-RunLine ('lock held by parent pid {0}; hop continues.' -f $lockPid)
            return $true
        }
        if ($alive -and $ageMin -lt 240) {
            Write-Host ('REFUSED: nightly lock held by pid {0} ({1:N1} min old).' -f $lockPid, $ageMin)
            return $false
        }
        Write-RunLine ('replacing stale lock pid={0} age={1:N1}m alive={2}' -f $lockPid, $ageMin, $alive)
    }
    ('{0} {1}' -f $PID, [datetime]::UtcNow.ToString('o')) | Set-Content -LiteralPath $lockPath -Encoding ASCII
    $script:ownsLock = $true
    return $true
}

function Exit-NightlyLock {
    if (-not $script:ownsLock) { return }
    if (Test-Path -LiteralPath $lockPath) {
        Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
    }
    $script:ownsLock = $false
}

function Write-LastRun {
    param([int]$Code)
    $completed = Get-Date
    $obj = [ordered]@{
        sha             = $script:sha
        ref             = $Ref
        succeeded       = ($Code -eq 0)
        startedAt       = $script:startedAt.ToString('o')
        completedAt     = $completed.ToString('o')
        durationSeconds = [math]::Round(($completed - $script:startedAt).TotalSeconds, 3)
        logDir          = $script:runDir
        summaryPath     = $script:summaryPath
        testExit        = $script:testExit
        reportExit      = $script:reportExit
        exitCode        = $Code
        unchanged       = [bool]$script:unchanged
    }
    New-Item -ItemType Directory -Path $nightlyRoot -Force | Out-Null
    $obj | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $lastRunPath -Encoding UTF8
    Write-RunLine ('wrote {0}' -f $lastRunPath)
}

function Prune-OldLogs {
    param([string]$Root)
    if (-not (Test-Path -LiteralPath $Root)) { return }
    $cutoff = (Get-Date).AddDays(-14)
    Get-ChildItem -Path $Root -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $cutoff } |
        ForEach-Object {
            try {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force -Confirm:$false -ErrorAction Stop
                Write-RunLine ('pruned old nightly run {0}' -f $_.Name)
            } catch {
                Write-RunLine ('could not prune {0}: {1}' -f $_.FullName, $_.Exception.Message)
            }
        }
}

function Sync-NightlyClone {
    New-Item -ItemType Directory -Path (Split-Path -Parent $CheckoutRoot) -Force | Out-Null
    $gitDir = Join-Path $CheckoutRoot '.git'
    if (-not (Test-Path -LiteralPath $gitDir)) {
        Write-RunLine ('cloning {0} to {1}' -f $RemoteUrl, $CheckoutRoot)
        & git clone $RemoteUrl $CheckoutRoot
        if ($LASTEXITCODE -ne 0) {
            throw ('git clone failed with exit {0}' -f $LASTEXITCODE)
        }
    }
    Write-RunLine ('fetch origin {0}' -f $Ref)
    & git -C $CheckoutRoot fetch origin $Ref
    if ($LASTEXITCODE -ne 0) {
        throw ('git fetch origin {0} failed with exit {1}' -f $Ref, $LASTEXITCODE)
    }
    Write-RunLine 'reset --hard FETCH_HEAD'
    & git -C $CheckoutRoot reset --hard FETCH_HEAD
    if ($LASTEXITCODE -ne 0) {
        throw ('git reset --hard FETCH_HEAD failed with exit {0}' -f $LASTEXITCODE)
    }
    Write-RunLine 'clean -fdx'
    & git -C $CheckoutRoot clean -fdx
    if ($LASTEXITCODE -ne 0) {
        throw ('git clean -fdx failed with exit {0}' -f $LASTEXITCODE)
    }
    $script:sha = (& git -C $CheckoutRoot rev-parse HEAD 2>&1 | Select-Object -First 1).ToString().Trim()
    Write-RunLine ('clone at {0}' -f $script:sha)
}

function Invoke-SelfUpdateHop {
    $cloneScript = Join-Path $CheckoutRoot 'scripts\nightly-run.ps1'
    if (-not (Test-Path -LiteralPath $cloneScript)) {
        Write-RunLine 'clone has no nightly-run.ps1; continuing with this copy.'
        return $false
    }
    $running = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($running)) { $running = $MyInvocation.MyCommand.Path }
    $left = (Get-FileHash -LiteralPath $running -Algorithm SHA256).Hash
    $right = (Get-FileHash -LiteralPath $cloneScript -Algorithm SHA256).Hash
    if ($left -eq $right) { return $false }

    Write-RunLine ('self-update: re-exec {0}' -f $cloneScript)
    $pwshArgs = @(
        '-NoProfile', '-NonInteractive', '-File', $cloneScript,
        '-NoSync',
        '-CheckoutRoot', $CheckoutRoot,
        '-LogRoot', $LogRoot,
        '-Ref', $Ref
    )
    if ($NoReport) { $pwshArgs += '-NoReport' }
    if ($Suites -and $Suites.Count -gt 0) {
        $pwshArgs += '-Suites'
        $pwshArgs += ($Suites -join ',')
    }
    & pwsh @pwshArgs
    $code = $LASTEXITCODE
    if ($null -eq $code) { $code = 1 }
    $script:hopped = $true
    return $code
}

$exitCode = 0
$runTests = $true
try {
    if (Test-SharedNightlyTree -Path $CheckoutRoot) {
        Write-Host 'REFUSED: -CheckoutRoot is the shared tree or a worktree.'
        Write-Host ('  CheckoutRoot: {0}' -f $CheckoutRoot)
        Write-Host '  Isolated clone: C:\Antiphon\nightly\checkout'
        $exitCode = 3
        $runTests = $false
    } elseif (-not (Enter-NightlyLock)) {
        $exitCode = 3
        $runTests = $false
    } elseif (-not $NoSync) {
        $script:recordRun = $true
        try {
            Sync-NightlyClone
        } catch {
            Write-RunLine ('PREFLIGHT: {0}' -f $_.Exception.Message)
            $exitCode = 1
            $runTests = $false
        }

        if ($runTests -and (Test-Path -LiteralPath $lastRunPath)) {
            try {
                $last = Get-Content -LiteralPath $lastRunPath -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($last.succeeded -and [string]$last.sha -eq $script:sha -and -not [string]::IsNullOrWhiteSpace($script:sha)) {
                    Write-Host ('unchanged since green run at {0}' -f $script:sha)
                    $script:unchanged = $true
                    $script:recordRun = $false
                    $exitCode = 0
                    $runTests = $false
                }
            } catch {
                Write-RunLine ('could not read last-run.json: {0}' -f $_.Exception.Message)
            }
        }

        if ($runTests) {
            $hopCode = Invoke-SelfUpdateHop
            if ($script:hopped) {
                $exitCode = $hopCode
                $script:recordRun = $false
                $runTests = $false
            }
        }
    } else {
        $script:recordRun = $true
        $script:sha = (& git -C $CheckoutRoot rev-parse HEAD 2>&1 | Select-Object -First 1).ToString().Trim()
    }

    if (-not $runTests) {
        # hop, lock refusal, unchanged, or preflight already set $exitCode
    } else {

    Prune-OldLogs -Root $LogRoot
    $stamp = (Get-Date).ToString('yyyy-MM-dd-HHmm')
    $script:runDir = Join-Path $LogRoot $stamp
    New-Item -ItemType Directory -Path $script:runDir -Force | Out-Null
    $script:summaryPath = Join-Path $script:runDir 'summary.json'

    $testsScript = Join-Path $CheckoutRoot 'scripts\nightly-tests.ps1'
    $reportScript = Join-Path $CheckoutRoot 'scripts\nightly-report.ps1'
    if (-not (Test-Path -LiteralPath $testsScript)) {
        $testsScript = Join-Path $PSScriptRoot 'nightly-tests.ps1'
    }
    if (-not (Test-Path -LiteralPath $reportScript)) {
        $reportScript = Join-Path $PSScriptRoot 'nightly-report.ps1'
    }

    $gitRefLabel = ('origin/{0}' -f $Ref)
    $testArgs = @(
        '-NoProfile', '-NonInteractive', '-File', $testsScript,
        '-RepoRoot', $CheckoutRoot,
        '-LogRoot', $script:runDir,
        '-Sha', $script:sha,
        '-GitRef', $gitRefLabel
    )
    if ($Suites -and $Suites.Count -gt 0) {
        $testArgs += '-Suites'
        $testArgs += ($Suites -join ',')
    }

    Write-RunLine ('running {0}' -f $testsScript)
    & pwsh @testArgs
    $script:testExit = $LASTEXITCODE
    if ($null -eq $script:testExit) { $script:testExit = 1 }

    $prevSummary = ''
    if (Test-Path -LiteralPath $lastRunPath) {
        try {
            $last = Get-Content -LiteralPath $lastRunPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($last.summaryPath -and (Test-Path -LiteralPath ([string]$last.summaryPath))) {
                $prevSummary = [string]$last.summaryPath
            }
        } catch { }
    }

    if (-not $NoReport) {
        try {
            $reportArgs = @(
                '-NoProfile', '-NonInteractive', '-File', $reportScript,
                '-Summary', $script:summaryPath
            )
            if ($prevSummary) {
                $reportArgs += '-PreviousSummary'
                $reportArgs += $prevSummary
            }
            Write-RunLine ('running {0}' -f $reportScript)
            & pwsh @reportArgs
            $script:reportExit = $LASTEXITCODE
            if ($null -eq $script:reportExit) { $script:reportExit = 3 }
        } catch {
            $script:reportExit = 3
            Write-RunLine ('REPORTING: {0}' -f $_.Exception.Message)
        }
    }

    if ($script:reportExit -eq 3) {
        $exitCode = 3
    } elseif ($script:testExit -eq 3) {
        $exitCode = 3
    } elseif ($script:testExit -eq 2) {
        $exitCode = 2
    } elseif ($script:testExit -ne 0) {
        $exitCode = 1
    } else {
        $exitCode = 0
    }

    } # end $runTests
} catch {
    if ($exitCode -eq 0) { $exitCode = 1 }
    Write-RunLine $_.Exception.Message
} finally {
    if ($script:recordRun) {
        try { Write-LastRun -Code $exitCode } catch {
            Write-RunLine ('could not write last-run.json: {0}' -f $_.Exception.Message)
        }
    }
    Exit-NightlyLock
}

exit $exitCode
