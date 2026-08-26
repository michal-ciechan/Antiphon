#requires -Version 5.1
<#
.SYNOPSIS
    Report (default) or delete clearly attributable Codex test residue. The state_5.sqlite reader
    is read-only; the only mutation path is `codex delete --force <thread-id>`.

    Recurring cleanup is a Windmill schedule on server2
    (u/lndcobra/antiphon_codex_residue_cleanup), Monday 09:30 Europe/London. It is report-only
    until the user explicitly changes the rollout. Do not add a Windows Scheduled Task.

    ASCII-only on purpose: this must parse under both pwsh 7 and Windows PowerShell 5.1.

    Default is dry-run. -Execute is deliberately opt-in. -MaxPerRun refuses rather than truncates.
    The report is built from an allow-list of fields and never dumps the raw SQLite rows.

.PARAMETER OlderThanHours
    A thread must be strictly older than this many hours. Default 24.

.PARAMETER Execute
    Mutate only through `codex delete --force <id>`. Absent means report-only.

.PARAMETER MaxPerRun
    Refuse (exit 4), rather than truncate, when more candidates are found. Default 10.

.PARAMETER InputJson
    Classify a saved threads dump instead of reading state_5.sqlite. Forces -Execute off. This is
    the fixture seam used by scripts/test-cleanup-codex-test-residue.ps1.

.PARAMETER CodexHome
    Codex state root. Defaults to CODEX_HOME, then %USERPROFILE%\.codex.

.PARAMETER StateDbPath
    Override state_5.sqlite for a copied fixture database. Never point -Execute test runs at the
    real database.

.PARAMETER LiveCwdsJson
    Fixture seam: JSON array of live codex cwd strings. When absent, only explicit --cd values on
    live codex.exe command lines are used; unknown working directories never become a positive
    match.

.PARAMETER LockIdsJson
    Fixture seam: JSON array of ids named by thread-writer-locks files.

.PARAMETER CodexShim
    Fixture seam for the Codex-owned delete command. Signature: param($Id). Return output lines.

.PARAMETER PassThru
    Return a result object rather than exiting, for fixture tests.
#>
[CmdletBinding()]
param(
    [int]$OlderThanHours = 24,
    [switch]$Execute,
    [int]$MaxPerRun = 10,
    [string]$ReportPath = '',
    [string]$InputJson = '',
    [string]$CodexHome = '',
    [string]$StateDbPath = '',
    [string]$CodexExe = 'codex',
    [string]$LiveCwdsJson = '',
    [string]$LockIdsJson = '',
    [string]$Now = '',
    [scriptblock]$CodexShim,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$repoRoot = Split-Path $PSScriptRoot -Parent

if (-not $CodexHome) {
    $CodexHome = [System.Environment]::GetEnvironmentVariable('CODEX_HOME')
}
if (-not $CodexHome) {
    $CodexHome = Join-Path $env:USERPROFILE '.codex'
}
$CodexHome = [IO.Path]::GetFullPath($CodexHome)
if (-not $StateDbPath) {
    $StateDbPath = Join-Path $CodexHome 'state_5.sqlite'
}
if (-not $ReportPath) {
    $ReportPath = Join-Path (Join-Path $repoRoot 'logs') 'codex-test-residue'
}

if ($OlderThanHours -lt 0) { throw 'OlderThanHours must be non-negative.' }
if ($MaxPerRun -lt 1) { throw 'MaxPerRun must be at least 1.' }
if ($InputJson -and $Execute) {
    Write-Host 'InputJson is set; -Execute is ignored (dry run only).'
    $Execute = $false
}

$candidates = @()
$skipped = @()
$unattributed = @()
$mutations = @()
$threadCount = 0
$liveCwdCount = 0
$lockIdCount = 0
$reportFile = $null
$nowUtc = [datetime]::UtcNow

function ConvertTo-UtcDate {
    param([object]$Value)
    if ($null -eq $Value -or $Value -eq '') { return $null }
    if ($Value -is [datetimeoffset]) { return ([datetimeoffset]$Value).UtcDateTime }
    if ($Value -is [datetime]) {
        $date = [datetime]$Value
        if ($date.Kind -eq [DateTimeKind]::Utc) { return $date }
        if ($date.Kind -eq [DateTimeKind]::Local) { return $date.ToUniversalTime() }
        return [datetime]::SpecifyKind($date, [DateTimeKind]::Utc)
    }
    $number = 0L
    if ([int64]::TryParse([string]$Value, [ref]$number)) {
        $epoch = [datetime]::SpecifyKind([datetime]'1970-01-01', [DateTimeKind]::Utc)
        if ($number -gt 100000000000) { return $epoch.AddMilliseconds($number) }
        if ($number -gt 1000000000) { return $epoch.AddSeconds($number) }
    }
    try {
        return [datetimeoffset]::Parse([string]$Value, $invariant,
            [System.Globalization.DateTimeStyles]::RoundtripKind).UtcDateTime
    } catch {
        return $null
    }
}

function Format-UtcStamp {
    param([object]$Value)
    $date = ConvertTo-UtcDate $Value
    if ($null -eq $date) { return [string]$Value }
    return $date.ToString('yyyy-MM-ddTHH:mm:ssZ', $invariant)
}

function ConvertTo-Bool {
    param([object]$Value)
    if ($null -eq $Value) { return $false }
    if ($Value -is [bool]) { return [bool]$Value }
    $text = ([string]$Value).Trim()
    return $text -eq '1' -or $text -ieq 'true'
}

function Normalize-PathText {
    param([string]$PathText)
    if ([string]::IsNullOrWhiteSpace($PathText)) { return '' }
    $value = $PathText.Trim()
    if ($value.StartsWith('\\?\')) { $value = $value.Substring(4) }
    return $value.TrimEnd('\', '/')
}

function Test-PathUnder {
    param([string]$PathText, [string]$Root)
    $path = Normalize-PathText $PathText
    $rootText = (Normalize-PathText $Root) + '\'
    return $path.StartsWith($rootText, [StringComparison]::OrdinalIgnoreCase) -or
        $path.Equals($rootText.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
}

function Get-ThreadRows {
    if ($InputJson) {
        if (-not (Test-Path -LiteralPath $InputJson)) { throw "InputJson not found: $InputJson" }
        $raw = Get-Content -LiteralPath $InputJson -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($null -eq $raw) { return @() }
        if ($raw -is [System.Array]) { return @($raw) }
        if ($raw.PSObject.Properties.Name -contains 'threads') { return @($raw.threads) }
        if ($raw.PSObject.Properties.Name -contains 'data') { return @($raw.data) }
        return @($raw)
    }

    if (-not (Test-Path -LiteralPath $StateDbPath)) { throw "state_5.sqlite not found: $StateDbPath" }
    # mode=ro prevents writes while still observing current WAL-committed changes.
    $python = @'
import json, pathlib, sqlite3, sys
path = pathlib.Path(sys.argv[1]).resolve()
uri = path.as_uri() + '?mode=ro'
con = sqlite3.connect(uri, uri=True)
try:
    columns = {r[1] for r in con.execute('PRAGMA table_info(threads)')}
    wanted = ['id', 'cwd', 'source', 'model_provider', 'created_at', 'rollout_path', 'archived', 'is_pinned', 'first_user_message']
    select = [c if c in columns else 'NULL AS ' + c for c in wanted]
    rows = [dict(zip(wanted, row)) for row in con.execute('SELECT ' + ', '.join(select) + ' FROM threads')]
    print(json.dumps(rows))
finally:
    con.close()
'@
    $rawJson = & python -c $python $StateDbPath
    if ($LASTEXITCODE -ne 0) { throw 'Python could not read state_5.sqlite read-only.' }
    if (-not $rawJson) { return @() }
    $rawJsonText = $rawJson -join "`n"
    if ([string]::IsNullOrWhiteSpace($rawJsonText) -or $rawJsonText.Trim() -eq '[]') { return @() }
    return @($rawJsonText | ConvertFrom-Json)
}

function Get-LiveCodexCwds {
    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    if ($LiveCwdsJson) {
        if (-not (Test-Path -LiteralPath $LiveCwdsJson)) { throw "LiveCwdsJson not found: $LiveCwdsJson" }
        foreach ($cwd in @(Get-Content -LiteralPath $LiveCwdsJson -Raw -Encoding UTF8 | ConvertFrom-Json)) {
            $normal = Normalize-PathText ([string]$cwd)
            if ($normal) { [void]$set.Add($normal) }
        }
        return ,$set
    }

    # Win32_Process exposes command lines, not a general current-directory field. Only an explicit
    # --cd is positive evidence of a matching cwd; no guessed cwd is allowed to make a row eligible.
    foreach ($proc in @(Get-CimInstance Win32_Process -Filter "Name = 'codex.exe'" -ErrorAction SilentlyContinue)) {
        $command = [string]$proc.CommandLine
        if ($command -match '(?i)(?:^|\s)--cd\s+"([^"]+)"') {
            [void]$set.Add((Normalize-PathText $Matches[1]))
        } elseif ($command -match '(?i)(?:^|\s)--cd\s+([^\s]+)') {
            [void]$set.Add((Normalize-PathText $Matches[1]))
        }
    }
    return ,$set
}

function Get-WriterLockIds {
    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    if ($LockIdsJson) {
        if (-not (Test-Path -LiteralPath $LockIdsJson)) { throw "LockIdsJson not found: $LockIdsJson" }
        foreach ($id in @(Get-Content -LiteralPath $LockIdsJson -Raw -Encoding UTF8 | ConvertFrom-Json)) {
            if ($id) { [void]$set.Add([string]$id) }
        }
        return ,$set
    }
    $lockRoot = Join-Path $CodexHome 'thread-writer-locks'
    if (Test-Path -LiteralPath $lockRoot) {
        foreach ($file in @(Get-ChildItem -LiteralPath $lockRoot -File -Recurse -ErrorAction SilentlyContinue)) {
            if ($file.Name -match '(?i)[0-9a-f]{8}-[0-9a-f-]{27,}') { [void]$set.Add($Matches[0]) }
        }
    }
    return ,$set
}

function Test-UnattributedProbe {
    param([string]$Cwd)
    $tempRoot = Normalize-PathText ([IO.Path]::GetTempPath())
    $normal = Normalize-PathText $Cwd
    if (-not (Test-PathUnder $normal $tempRoot)) { return $false }
    $relative = $normal.Substring($tempRoot.Length).TrimStart('\')
    return $relative -match '(?i)^claude\\.+\\scratchpad(?:\\|$)' -or
        $relative -match '(?i)^codex-tui-probe-[^\\]*(?:\\|$)' -or
        $relative -match '(?i)^codexprobe[^\\]*(?:\\|$)' -or
        $relative -match '(?i)^cx0108-probe[^\\]*(?:\\|$)'
}

function Test-FixedTestCwd {
    param([string]$Cwd)
    $tempRoot = Normalize-PathText ([IO.Path]::GetTempPath())
    $normal = Normalize-PathText $Cwd
    if (-not (Test-PathUnder $normal $tempRoot)) { return $false }
    $relative = $normal.Substring($tempRoot.Length).TrimStart('\\')
    return $relative -match '(?i)^antiphon-codex-canary[^\\]*(?:\\|$)' -or
        $relative -match '(?i)^antiphon-codex-roundtrip[^\\]*(?:\\|$)' -or
        $relative -match '(?i)^codex-brunner-[^\\]+\\cwd$'
}

function Get-AllowlistedRow {
    param($Row, [double]$AgeHours, [string]$Reason, [bool]$Candidate, [bool]$IncludeMessage)
    $entry = [ordered]@{
        id = [string]$Row.id
        cwd = Normalize-PathText ([string]$Row.cwd)
        source = [string]$Row.source
        modelProvider = [string]$Row.model_provider
        createdAt = Format-UtcStamp $Row.created_at
        ageHours = [math]::Round($AgeHours, 3)
        rolloutPath = Normalize-PathText ([string]$Row.rollout_path)
        rolloutExists = if ($Row.rollout_path) { Test-Path -LiteralPath ([string]$Row.rollout_path) } else { $false }
        archived = ConvertTo-Bool $Row.archived
        pinned = ConvertTo-Bool $Row.is_pinned
        reason = $Reason
        candidate = $Candidate
    }
    if ($IncludeMessage) { $entry.firstUserMessage = [string]$Row.first_user_message }
    return [pscustomobject]$entry
}

function Write-ReportFile {
    param([string]$Path, [int]$Code)
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $payload = [pscustomobject]@{
        generatedAt = [datetime]::UtcNow.ToString('o')
        now = $nowUtc.ToString('o')
        olderThanHours = $OlderThanHours
        execute = [bool]$Execute
        maxPerRun = $MaxPerRun
        inputJson = [bool]$InputJson
        threadCount = $threadCount
        liveCwdCount = $liveCwdCount
        writerLockCount = $lockIdCount
        candidateCount = @($candidates).Count
        skipCount = @($skipped).Count
        unattributedCount = @($unattributed).Count
        candidates = @($candidates)
        skipped = @($skipped)
        unattributed = @($unattributed)
        mutations = @($mutations)
        exitCode = $Code
    }
    $payload | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function New-PassThruResult {
    param([int]$Code, [string]$Path)
    return [pscustomobject]@{
        ExitCode = $Code
        ReportPath = $Path
        CandidateCount = @($candidates).Count
        SkipCount = @($skipped).Count
        UnattributedCount = @($unattributed).Count
        CandidateIds = @($candidates | ForEach-Object { $_.id })
        UnattributedIds = @($unattributed | ForEach-Object { $_.id })
        Mutations = @($mutations)
    }
}

function Complete-Run {
    param([int]$Code)
    if ($reportFile) {
        try { Write-ReportFile -Path $reportFile -Code $Code } catch { Write-Host ('warning: failed to write report: ' + $_.Exception.Message) }
    }
    $result = New-PassThruResult -Code $Code -Path $reportFile
    if ($PassThru) { return $result }
    exit $Code
}

if ($Now) {
    $parsed = ConvertTo-UtcDate $Now
    if ($null -eq $parsed) { throw "Could not parse -Now '$Now' as a timestamp." }
    $nowUtc = $parsed
}
if (-not (Test-Path -LiteralPath $ReportPath)) { New-Item -ItemType Directory -Path $ReportPath -Force | Out-Null }
$reportFile = Join-Path $ReportPath ([datetime]::UtcNow.ToString('yyyy-MM-dd-HHmmss') + '.json')

try {
    $rows = @(Get-ThreadRows)
} catch {
    Write-Host $_.Exception.Message
    return (Complete-Run 1)
}
$threadCount = $rows.Count
$liveCwds = Get-LiveCodexCwds
$liveCwdCount = $liveCwds.Count
$lockIds = Get-WriterLockIds
$lockIdCount = $lockIds.Count

$candidateList = New-Object System.Collections.ArrayList
$skipList = New-Object System.Collections.ArrayList
$unattributedList = New-Object System.Collections.ArrayList
$tempRoot = Normalize-PathText ([IO.Path]::GetTempPath())

foreach ($row in $rows) {
    if ($null -eq $row) { continue }
    $id = [string]$row.id
    $cwd = Normalize-PathText ([string]$row.cwd)
    $source = ([string]$row.source).ToLowerInvariant()
    $provider = ([string]$row.model_provider).ToLowerInvariant()
    $rollout = Normalize-PathText ([string]$row.rollout_path)
    $created = ConvertTo-UtcDate $row.created_at
    $ageHours = if ($null -eq $created) { -1.0 } else { ($nowUtc - $created).TotalHours }

    # These are ad-hoc probes typed by agent sessions. They are reported for a human decision and
    # never flow through the candidate list, even if every other condition would pass.
    if (Test-UnattributedProbe $cwd) {
        [void]$unattributedList.Add((Get-AllowlistedRow $row $ageHours 'UNATTRIBUTED-PROBE' $false $true))
        continue
    }

    $reasons = New-Object System.Collections.ArrayList
    # C1: only the fixed test cwd prefixes, or the synthetic-only stub provider.
    if (-not ((Test-FixedTestCwd $cwd) -or $provider -eq 'stub')) { [void]$reasons.Add('not-test-cwd-or-stub') }
    # C2: desktop is always the user, never a test candidate.
    if ($source -notin @('cli', 'exec')) { [void]$reasons.Add("source=$source") }
    # C3: strictly older than the age gate.
    if ($null -eq $created) { [void]$reasons.Add('no-created-at') }
    elseif ($ageHours -le $OlderThanHours) { [void]$reasons.Add('fresh') }
    # C4: an exact known live cwd or writer lock is an exclusion.
    if ($cwd -and $liveCwds.Contains($cwd)) { [void]$reasons.Add('LIVE-CODEX-CWD') }
    if ($id -and $lockIds.Contains($id)) { [void]$reasons.Add('THREAD-WRITER-LOCK') }
    # C5: archive and pin are human decisions.
    if (ConvertTo-Bool $row.archived) { [void]$reasons.Add('archived') }
    if (ConvertTo-Bool $row.is_pinned) { [void]$reasons.Add('pinned') }

    # Never-candidate construction. These guards intentionally duplicate C1/C2 where that makes a
    # future broadening of the allow-list fail closed rather than reaching a user's data.
    if ($source -eq 'vscode') { [void]$reasons.Add('VSCODE-NEVER') }
    if (-not (Test-PathUnder $cwd $tempRoot) -and $provider -ne 'stub') { [void]$reasons.Add('outside-temp-NEVER') }
    if ($rollout -match '(?i)(^|\\)(remote_control_enrollments|thread_sections|archived_sessions)(\\|$)') {
        [void]$reasons.Add('protected-rollout-NEVER')
    }

    $isCandidate = $reasons.Count -eq 0
    $entry = Get-AllowlistedRow $row $ageHours ($reasons -join ', ') $isCandidate $false
    if ($isCandidate) { [void]$candidateList.Add($entry) }
    else { [void]$skipList.Add($entry) }
}

$candidates = @($candidateList | Sort-Object createdAt)
$skipped = @($skipList | Sort-Object createdAt)
$unattributed = @($unattributedList | Sort-Object createdAt)

Write-Host ('=== CANDIDATES ({0}) ===' -f $candidates.Count)
foreach ($row in $candidates) { Write-Host ('{0}  {1}  {2}  age={3}h' -f $row.id, $row.source, $row.cwd, $row.ageHours) }
Write-Host ('=== UNATTRIBUTED ({0}) ===' -f $unattributed.Count)
foreach ($row in $unattributed) { Write-Host ('{0}  {1}  {2}' -f $row.id, $row.cwd, $row.firstUserMessage) }
Write-Host ('=== SKIPPED ({0}) ===' -f $skipped.Count)
Write-Host ('candidates={0} unattributed={1} skipped={2} execute={3}' -f $candidates.Count, $unattributed.Count, $skipped.Count, [bool]$Execute)

if ($Execute) {
    if ($candidates.Count -gt $MaxPerRun) {
        Write-Host ("Refusing to delete $($candidates.Count) candidates; -MaxPerRun is $MaxPerRun. The cap refuses, it does not truncate.")
        return (Complete-Run 4)
    }
    $failures = 0
    foreach ($row in $candidates) {
        $output = @()
        try {
            if ($CodexShim) { $output = @(& $CodexShim $row.id) }
            else { $output = @(& $CodexExe delete --force $row.id 2>&1) }
            $text = ($output | ForEach-Object { [string]$_ }) -join [System.Environment]::NewLine
            $deletedLine = @($text -split "`r?`n" | Where-Object { $_ -match '^Deleted session\b' }) | Select-Object -First 1
            $ok = $null -ne $deletedLine
            if ($ok -and -not $InputJson) {
                $remaining = @(Get-ThreadRows | Where-Object { [string]$_.id -eq $row.id })
                if ($remaining.Count -ne 0) { $ok = $false; $text = 'Deleted session line was present but threads row remained.' }
            }
            $mutations += [pscustomobject]@{ id = $row.id; action = 'delete'; ok = $ok; deletedLine = [string]$deletedLine }
            if ($ok) { Write-Host ("delete $($row.id) ok") }
            else {
                $failures++
                Write-Host ("delete $($row.id) FAILED " + $text.Substring(0, [Math]::Min(180, $text.Length)))
            }
        } catch {
            $failures++
            $mutations += [pscustomobject]@{ id = $row.id; action = 'delete'; ok = $false; deletedLine = '' }
            Write-Host ("delete $($row.id) FAILED " + $_.Exception.Message)
        }
    }
    if ($failures -gt 0) { return (Complete-Run 5) }
}

return (Complete-Run 0)
