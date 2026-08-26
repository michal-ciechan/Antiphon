#requires -Version 5.1
<#!
.SYNOPSIS
    Fixture tests for cleanup-codex-test-residue.ps1. They use a saved threads dump through
    -InputJson, never open the real state_5.sqlite, and therefore cannot call -Execute.

    ASCII-only: parses under pwsh 7 and Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$cleanup = Join-Path $here 'cleanup-codex-test-residue.ps1'
$fixture = Join-Path $here (Join-Path 'fixtures' 'codex-test-residue-threads-2026-08-26.json')
$nowPin = '2026-08-26T14:00:00Z'
$script:passed = 0
$script:failed = 0

function Pass { param([string]$Name) $script:passed++; Write-Host "PASS $Name" }
function Fail { param([string]$Name, [string]$Detail) $script:failed++; Write-Host "FAIL $Name - $Detail" }
function Assert-Eq { param($Actual, $Expected, [string]$Name) if ($Actual -eq $Expected) { Pass $Name } else { Fail $Name "expected=$Expected actual=$Actual" } }
function Assert-True { param([bool]$Value, [string]$Name, [string]$Detail = '') if ($Value) { Pass $Name } else { Fail $Name $Detail } }

if (-not (Test-Path -LiteralPath $cleanup)) { throw "missing $cleanup" }
if (-not (Test-Path -LiteralPath $fixture)) { throw "missing $fixture" }

$work = Join-Path $env:TEMP ('codex-residue-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    $input = Join-Path $work 'threads.json'
    $raw = Get-Content -LiteralPath $fixture -Raw -Encoding UTF8
    $temp = [IO.Path]::GetTempPath().TrimEnd('\', '/')
    $raw.Replace('__TEMP__', $temp.Replace('\', '\\')) | Set-Content -LiteralPath $input -Encoding UTF8

    $result = & $cleanup -InputJson $input -Now $nowPin -ReportPath $work -PassThru
    Assert-Eq $result.ExitCode 0 'T1 dry run exit 0'
    Assert-Eq $result.CandidateCount 5 'T1 exact candidate count'
    Assert-Eq $result.UnattributedCount 4 'T1 exact unattributed count'
    Assert-Eq $result.SkipCount 4 'T1 exact skipped count'
    $expected = @(
        '11111111-1111-1111-1111-111111111111',
        '22222222-2222-2222-2222-222222222222',
        '33333333-3333-3333-3333-333333333333',
        '44444444-4444-4444-4444-444444444444')
    Assert-True (@($expected | Where-Object { $result.CandidateIds -notcontains $_ }).Count -eq 0) 'T1 candidate ids exact'
    Assert-True ($result.CandidateIds -notcontains '99999999-9999-9999-9999-999999999999') 'T2 vscode never candidate'
    Assert-True ($result.CandidateIds -notcontains 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa') 'T3 normal delegate cwd never candidate'
    Assert-True ($result.CandidateIds -notcontains 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb') 'T4 fresh row never candidate'
    Assert-True ($result.CandidateIds -notcontains 'cccccccc-cccc-cccc-cccc-cccccccccccc') 'T5 archived path never candidate'
    Assert-True ($result.CandidateIds -contains 'dddddddd-dddd-dddd-dddd-dddddddddddd') 'T6 row is a candidate before a writer lock is supplied'

    $lockIds = Join-Path $work 'locks.json'
    '["dddddddd-dddd-dddd-dddd-dddddddddddd"]' | Set-Content -LiteralPath $lockIds -Encoding UTF8
    $withLock = & $cleanup -InputJson $input -LockIdsJson $lockIds -Now $nowPin -ReportPath $work -PassThru
    Assert-Eq $withLock.CandidateCount 4 'T7 writer lock excludes exact id'
    Assert-True ($withLock.CandidateIds -notcontains 'dddddddd-dddd-dddd-dddd-dddddddddddd') 'T7 locked row never candidate'

    $liveCwds = Join-Path $work 'live.json'
    @($temp + '\antiphon-codex-canaryabc') | ConvertTo-Json | Set-Content -LiteralPath $liveCwds -Encoding UTF8
    $withLive = & $cleanup -InputJson $input -LiveCwdsJson $liveCwds -Now $nowPin -ReportPath $work -PassThru
    Assert-Eq $withLive.CandidateCount 4 'T8 live codex cwd excludes exact id'
    Assert-True ($withLive.CandidateIds -notcontains '11111111-1111-1111-1111-111111111111') 'T8 live cwd never candidate'

    $forced = & $cleanup -InputJson $input -Execute -Now $nowPin -ReportPath $work -PassThru
    Assert-Eq $forced.ExitCode 0 'T9 InputJson forces execute off'
    Assert-Eq $forced.Mutations.Count 0 'T9 fixture run makes no mutation'

    $report = Get-Item -LiteralPath $forced.ReportPath
    $json = Get-Content -LiteralPath $report.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Eq $json.unattributed.Count 4 'T10 report has a separate unattributed section'
    Assert-Eq $json.candidates.Count 5 'T10 report has allow-listed candidates'

    # Keep the writer connected after deleting the row so the deletion remains in the WAL. A
    # read-only SQLite connection must observe the WAL rather than return the stale base database.
    $db = Join-Path $work 'state_5.sqlite'
    $ready = Join-Path $work 'sqlite-writer-ready'
    $release = Join-Path $work 'sqlite-writer-release'
    $writer = Join-Path $work 'hold-wal-writer.py'
    $walCwd = $temp + '\antiphon-codex-canary-wal-regression'
    @'
import pathlib, sqlite3, sys, time
db, ready, release, cwd = sys.argv[1:]
con = sqlite3.connect(db)
try:
    con.execute("PRAGMA journal_mode=WAL")
    con.execute("CREATE TABLE threads (id TEXT, cwd TEXT, source TEXT, model_provider TEXT, created_at TEXT, rollout_path TEXT, archived INTEGER, is_pinned INTEGER, first_user_message TEXT)")
    con.execute("INSERT INTO threads VALUES (?, ?, 'cli', 'openai', '2026-08-01T00:00:00Z', NULL, 0, 0, NULL)", ('wal-deleted-thread', cwd))
    con.commit()
    con.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    con.execute("DELETE FROM threads WHERE id = 'wal-deleted-thread'")
    con.commit()
    pathlib.Path(ready).touch()
    while not pathlib.Path(release).exists():
        time.sleep(0.05)
finally:
    con.close()
'@ | Set-Content -LiteralPath $writer -Encoding ASCII
    $writerProcess = Start-Process -FilePath python -ArgumentList @($writer, $db, $ready, $release, $walCwd) -PassThru
    try {
        $deadline = [datetime]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath $ready) -and [datetime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
        Assert-True (Test-Path -LiteralPath $ready) 'T11 WAL fixture writer started' 'writer did not signal readiness'
        if (Test-Path -LiteralPath $ready) {
            $walRead = & $cleanup -StateDbPath $db -Now $nowPin -ReportPath $work -PassThru
            Assert-Eq $walRead.ExitCode 0 'T12 WAL database read exits 0'
            Assert-Eq $walRead.CandidateCount 0 'T12 WAL deletion is visible to read-only connection'
            Assert-Eq $walRead.SkipCount 0 'T12 no stale base-database row is classified'
        }
    }
    finally {
        New-Item -ItemType File -Path $release -Force | Out-Null
        if (-not $writerProcess.WaitForExit(10000)) { Stop-Process -Id $writerProcess.Id -Force }
    }

    $nonAscii = [IO.File]::ReadAllBytes($cleanup) | Where-Object { $_ -gt 127 }
    Assert-Eq @($nonAscii).Count 0 'T13 cleanup script is ASCII-only'
}
finally {
    try { Remove-Item -LiteralPath $work -Recurse -Force } catch { }
}

Write-Host "RESULT passed=$script:passed failed=$script:failed"
if ($script:failed -gt 0) { exit 1 }
