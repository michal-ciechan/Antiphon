# CARD-0204: find - and with -Execute, kill - Antiphon.PtyHost processes that a test host leaked
# onto the always-on production session-runner. DRY RUN BY DEFAULT: without -Execute it only
# prints the census and the verdict per host.
#
# A host is a positive orphan only when EVERY rule below holds. Age alone is never evidence
# (CARD-0203): a host outlives its child by design, and an idle session's sequence does not move.
#
#   R1  no AgentSessions row in the production database for the manifest's session id
#       (the DB must ANSWER - an unreachable DB aborts the run; "could not look" is not "no row")
#   R2  the manifest's hostPid is alive AND its process start time matches the manifest's
#       hostStartTimeUtc within -PidReuseToleranceSec (pid-reuse guard)
#   R3  the manifest's childPid is alive and is the process the shape rule expects
#   R4  the manifest records no exit (a host lingering after child exit leaves on its own)
#   R5  the launch has one of the two shapes a test host produces:
#         test-raw-check-interpreter : exe is cmd.exe AND cwd is -CheckInterpreterDir
#                                      (AntiphonWebAppFactory's test-raw definition, launched
#                                      by CheckInterpreterProvisioner at host startup)
#         kind-test-temp-dir         : cwd is under %TEMP%\antiphon-kind-test*
#                                      (AgentTaskAgentKindTests, dispatched by a factory host
#                                      before the shared-schema isolation of 2026-08-20)
#   R6  for the cmd.exe shape, the ansi log holds only cmd's banner (nothing was ever typed)
#   R7  the manifest is older than -MinAgeMinutes (a test still running is left to finish)
#   R8  the runner lists the session as Running, so the kill goes THROUGH the runner
#       (POST /sessions/{id}/kill -> host kills child -> exit -> Shutdown ack -> host exits,
#       manifest deleted) - never a bare Stop-Process on a host the runner still serves
#
# Anything with a database row is printed under "protected" and never touched, whatever else
# is true of it. Anything failing a rule is printed with the rule that failed.
#
# ASCII-only: must parse under Windows PowerShell 5.1.
#
# Usage:
#   pwsh -File scripts/reap-orphaned-pty-hosts.ps1              # census + verdicts, kills nothing
#   pwsh -File scripts/reap-orphaned-pty-hosts.ps1 -Execute     # kills every positive orphan
#   pwsh -File scripts/reap-orphaned-pty-hosts.ps1 -Execute -Limit 20   # the 20 oldest only
#
# Exit codes:
#   0  dry run completed, or every kill was verified (host pid gone)
#   1  a kill was requested but the host is still alive afterwards
#   2  a prerequisite could not be read (runner unreachable, database did not answer)
[CmdletBinding()]
param(
    [switch]$Execute,

    [string]$RunnerUrl = 'http://localhost:17204',

    [string]$SessionLogPath = 'C:\logs\antiphon\session-runner',

    [string]$CheckInterpreterDir = 'C:\logs\antiphon\check-interpreter',

    [string]$PgContainer = 'antiphon-postgres',
    [string]$PgUser = 'antiphon',
    [string]$PgDatabase = 'antiphon',

    [int]$MinAgeMinutes = 30,

    [int]$PidReuseToleranceSec = 5,

    # cmd.exe's banner ("Microsoft Windows [Version ...]" + copyright + prompt) is ~164 bytes.
    [int]$MaxBannerOnlyAnsiBytes = 512,

    [int]$KillVerifySeconds = 20,

    # With -Execute, kill at most this many (oldest first); 0 = all. Lets an operator reap a
    # handful, look, then reap the rest.
    [int]$Limit = 0
)

$ErrorActionPreference = 'Stop'
$RunnerUrl = $RunnerUrl.TrimEnd('/')
$manifestDir = Join-Path $SessionLogPath 'pty-hosts\manifests'

function Read-Manifest([System.IO.FileInfo]$file) {
    $raw = Get-Content $file.FullName -Raw
    function Field([string]$name) {
        $m = [regex]::Match($raw, ('"' + $name + '":\s*(?:"([^"]*)"|([^,\s}]+))'))
        if (-not $m.Success) { return $null }
        if ($m.Groups[1].Success) { return $m.Groups[1].Value }
        return $m.Groups[2].Value
    }
    $exe = Field 'exe'
    $cwd = Field 'cwd'
    if ($exe) { $exe = $exe -replace '\\\\', '\' }
    if ($cwd) { $cwd = $cwd -replace '\\\\', '\' }
    $exit = Field 'exitCode'
    [pscustomobject]@{
        File         = $file.Name
        SessionId    = Field 'sessionId'
        HostPid      = [int](Field 'hostPid')
        HostStartUtc = [datetime]::Parse((Field 'hostStartTimeUtc'), $null, 'AdjustToUniversal')
        ChildPid     = [int](Field 'childPid')
        Exe          = $exe
        ExeLeaf      = if ($exe) { [System.IO.Path]::GetFileName($exe) } else { '' }
        Cwd          = $cwd
        CreatedUtc   = [datetime]::Parse((Field 'createdAtUtc'), $null, 'AdjustToUniversal')
        Exited       = ($null -ne $exit -and $exit -ne 'null')
        AnsiLog      = (Field 'ansiLogPath') -replace '\\\\', '\'
    }
}

# ---- prerequisites: the runner and the database must both ANSWER ----------------------------

try {
    $runnerSessions = Invoke-RestMethod -Method GET -Uri "$RunnerUrl/sessions" -TimeoutSec 15
} catch {
    Write-Host "ABORT: session-runner at $RunnerUrl did not answer GET /sessions: $($_.Exception.Message)"
    exit 2
}
$runnerStatus = @{}
foreach ($s in $runnerSessions) { $runnerStatus[[string]$s.sessionId] = [string]$s.status }

$manifests = @(Get-ChildItem $manifestDir -File -Filter *.json | ForEach-Object { Read-Manifest $_ })
if ($manifests.Count -eq 0) {
    Write-Host "No manifests under $manifestDir - nothing to do."
    exit 0
}

$idList = ($manifests | ForEach-Object { "'" + $_.SessionId + "'" }) -join ','
$sql = 'select "Id" from "AgentSessions" where "Id" in (' + $idList + ');'
$dbRows = & docker exec $PgContainer psql -U $PgUser -d $PgDatabase -At -c $sql 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "ABORT: the database did not answer (docker exec $PgContainer psql ... exit $LASTEXITCODE):"
    Write-Host ($dbRows | Out-String)
    exit 2
}
$known = @{}
foreach ($line in @($dbRows)) {
    $t = ([string]$line).Trim()
    if ($t -match '^[0-9a-f-]{36}$') { $known[$t] = $true }
}

$processes = @{}
foreach ($p in Get-CimInstance Win32_Process) {
    $processes[[int]$p.ProcessId] = $p
}

function Get-ProcessStartUtc($cim) {
    if ($null -eq $cim -or $null -eq $cim.CreationDate) { return $null }
    return ([datetime]$cim.CreationDate).ToUniversalTime()
}

# ---- verdicts ------------------------------------------------------------------------------

$now = [datetime]::UtcNow
$verdicts = foreach ($m in $manifests) {
    $reasons = New-Object System.Collections.Generic.List[string]
    $rule = ''

    if ($known.ContainsKey($m.SessionId)) {
        $reasons.Add('R1 has AgentSessions row (protected)')
    }

    $hostProc = $processes[$m.HostPid]
    if ($null -eq $hostProc) {
        $reasons.Add('R2 host pid not alive')
    } elseif ($hostProc.Name -ne 'Antiphon.PtyHost.exe') {
        $reasons.Add("R2 host pid is $($hostProc.Name), not a pty-host (pid reused)")
    } else {
        $start = Get-ProcessStartUtc $hostProc
        if ($null -eq $start -or [math]::Abs(($start - $m.HostStartUtc).TotalSeconds) -gt $PidReuseToleranceSec) {
            $reasons.Add("R2 host start $start does not match manifest $($m.HostStartUtc) (pid reused)")
        }
    }

    $child = $processes[$m.ChildPid]
    if ($null -eq $child) { $reasons.Add('R3 child pid not alive') }

    if ($m.Exited) { $reasons.Add('R4 manifest records an exit (host is lingering, will leave on its own)') }

    $isCheckInterp = ($m.ExeLeaf -ieq 'cmd.exe') -and ($m.Cwd -ieq $CheckInterpreterDir)
    $isKindTest = $m.Cwd -match '\\antiphon-kind-test[^\\]*$'
    if ($isCheckInterp) {
        $rule = 'test-raw-check-interpreter'
        if ($null -ne $child -and $child.Name -ne 'cmd.exe') { $reasons.Add("R3 child is $($child.Name), expected cmd.exe") }
        $ansi = Get-Item $m.AnsiLog -ErrorAction SilentlyContinue
        if ($null -eq $ansi) { $reasons.Add('R6 ansi log missing') }
        elseif ($ansi.Length -gt $MaxBannerOnlyAnsiBytes) { $reasons.Add("R6 ansi log is $($ansi.Length) bytes - something was typed or printed") }
    } elseif ($isKindTest) {
        $rule = 'kind-test-temp-dir'
        if ($null -ne $child -and $child.Name -notlike 'claude*') { $reasons.Add("R3 child is $($child.Name), expected claude") }
    } else {
        $reasons.Add("R5 shape not recognised ($($m.ExeLeaf) in $($m.Cwd))")
    }

    $ageMin = [int](($now - $m.CreatedUtc).TotalMinutes)
    if ($ageMin -lt $MinAgeMinutes) { $reasons.Add("R7 only $ageMin min old (< $MinAgeMinutes)") }

    $status = $runnerStatus[$m.SessionId]
    if ($status -ne 'Running') { $reasons.Add("R8 runner status is '$status', not Running") }

    [pscustomobject]@{
        SessionId = $m.SessionId
        Rule      = $rule
        AgeMin    = $ageMin
        HostPid   = $m.HostPid
        ChildPid  = $m.ChildPid
        Exe       = $m.ExeLeaf
        Cwd       = $m.Cwd
        Protected = $known.ContainsKey($m.SessionId)
        Orphan    = ($reasons.Count -eq 0)
        Reasons   = ($reasons -join '; ')
    }
}

$protected = @($verdicts | Where-Object Protected)
$orphans = @($verdicts | Where-Object Orphan)
$undecided = @($verdicts | Where-Object { -not $_.Protected -and -not $_.Orphan })

Write-Host ("Manifests: {0}   protected (DB row): {1}   positive orphans: {2}   not touched: {3}" -f `
    $manifests.Count, $protected.Count, $orphans.Count, $undecided.Count)
Write-Host ''
Write-Host '--- protected (have an AgentSessions row; never touched) ---'
$protected | Sort-Object Cwd | Format-Table SessionId, Exe, Cwd, AgeMin -AutoSize | Out-String -Width 220 | Write-Host
Write-Host '--- not touched (no DB row, but a rule failed) ---'
$undecided | Format-Table SessionId, Exe, Cwd, AgeMin, Reasons -AutoSize | Out-String -Width 220 | Write-Host
Write-Host '--- positive orphans ---'
$orphans | Group-Object Rule | ForEach-Object { Write-Host ("  {0,-30} {1}" -f $_.Name, $_.Count) }
$orphans | Sort-Object AgeMin -Descending | Format-Table SessionId, Rule, AgeMin, HostPid, ChildPid -AutoSize | Out-String -Width 220 | Write-Host

if (-not $Execute) {
    Write-Host 'Dry run: nothing was killed. Re-run with -Execute to kill the positive orphans through the runner.'
    exit 0
}

# ---- execute ------------------------------------------------------------------------------

$failed = 0
$killed = 0
$toKill = @($orphans | Sort-Object AgeMin -Descending)
if ($Limit -gt 0 -and $toKill.Count -gt $Limit) {
    Write-Host ("-Limit {0}: killing the {0} oldest of {1} positive orphans this run." -f $Limit, $toKill.Count)
    $toKill = @($toKill | Select-Object -First $Limit)
}
foreach ($o in $toKill) {
    try {
        $null = Invoke-RestMethod -Method POST -Uri "$RunnerUrl/sessions/$($o.SessionId)/kill" -TimeoutSec 30
    } catch {
        Write-Host ("KILL FAILED  {0}  POST /kill: {1}" -f $o.SessionId, $_.Exception.Message)
        $failed++
        continue
    }
    $deadline = [datetime]::UtcNow.AddSeconds($KillVerifySeconds)
    $gone = $false
    while ([datetime]::UtcNow -lt $deadline) {
        if ($null -eq (Get-Process -Id $o.HostPid -ErrorAction SilentlyContinue)) { $gone = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if ($gone) {
        $killed++
        Write-Host ("killed       {0}  {1}  host {2} gone" -f $o.SessionId, $o.Rule, $o.HostPid)
    } else {
        $failed++
        Write-Host ("STILL ALIVE  {0}  host {1} after {2}s" -f $o.SessionId, $o.HostPid, $KillVerifySeconds)
    }
}

Write-Host ''
Write-Host ("Killed and verified: {0}   failed: {1}   protected: {2}   not touched: {3}" -f `
    $killed, $failed, $protected.Count, $undecided.Count)
Write-Host ("Live Antiphon.PtyHost processes now: {0}" -f @(Get-Process -Name Antiphon.PtyHost -ErrorAction SilentlyContinue).Count)
if ($failed -gt 0) { exit 1 }
exit 0
