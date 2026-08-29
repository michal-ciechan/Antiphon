<#
.SYNOPSIS
  Run a TUnit test executable under a stall watchdog that captures a dump BEFORE killing it.

.DESCRIPTION
  CARD-0165 / CARD-0222. A test process that is "still Responding but doing nothing" is only a
  stall if (a) its stdout has stopped growing AND (b) it is burning ~no CPU for a sustained
  window - and even then the only thing that names the cause is `dotnet-dump analyze <dmp>
  -c dumpasync` (thread stacks of an async wedge show nothing). This wrapper starts the test exe,
  samples log size / CPU / Postgres connection count every $SampleSec seconds into
  <OutDir>\<Tag>.progress, and when the quiet+idle condition holds for $QuietSec seconds it
  records the process tree, pg_stat_activity, a `dotnet-stack report`, and a `dotnet-dump collect`
  - and only then kills the tree.

  Both `dotnet-stack` and `dotnet-dump` must be installed as global tools (`dotnet tool install -g
  dotnet-stack` / `dotnet-dump`).

  Read the numbers before calling anything a stall. Measured 2026-08-29 (three watched runs, none
  stalled): the suite's global [NotInParallel] phase runs LAST, sequentially, for 20-26 minutes at
  0.2-0.3 CPU-s/min (about 1-2% of one core), and under the default `--output Normal` it prints
  NOTHING unless a test fails. That is exactly the "silent, alive, ~10 CPU-seconds in 9 minutes"
  shape CARD-0165 reported. Pass -Detailed so every passing test prints a line and the log-growth
  signal means something.

.EXAMPLE
  dotnet build tests/Antiphon.Tests --property:OutputPath=bin-watch/
  pwsh -File scripts/run-tests-watched.ps1 -Exe tests/Antiphon.Tests/bin-watch/Antiphon.Tests.exe -Tag full -Detailed
  pwsh -File scripts/run-tests-watched.ps1 -Exe ... -Filter "/*/Antiphon.Tests.Application/*/*" -Tag app -Detailed
  # then, if it stalled:
  dotnet-dump analyze logs\watched\full.dmp -c dumpasync -c exit
#>
param(
    [Parameter(Mandatory)] [string]$Exe,
    [string]$Filter = "/*/*/*/*",
    [string]$Tag = "run",
    [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) "logs\watched"),
    [int]$HardTimeoutSec = 3600,
    [int]$QuietSec = 150,        # stdout not grown for this long AND ...
    [double]$CpuFloor = 1.0,     # ... fewer CPU-seconds than this over one sample window => stall
    [int]$SampleSec = 20,
    [switch]$Detailed,           # --output Detailed: passing tests print too (default prints failures only)
    [string[]]$EnvVars = @()     # "NAME=VALUE;NAME2=VALUE2" exported to the child process
)

$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$Exe = (Resolve-Path $Exe).Path
$log  = Join-Path $OutDir "$Tag.log"
$err  = Join-Path $OutDir "$Tag.err"
$prog = Join-Path $OutDir "$Tag.progress"
Remove-Item $log, $err, $prog -ErrorAction SilentlyContinue

foreach ($kv in ($EnvVars -join ';').Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
    $i = $kv.IndexOf('=')
    if ($i -lt 1) { throw "EnvVars entry '$kv' is not NAME=VALUE" }
    [Environment]::SetEnvironmentVariable($kv.Substring(0, $i), $kv.Substring($i + 1), 'Process')
    "[env] $($kv.Substring(0, $i))=$($kv.Substring($i + 1))" | Tee-Object -FilePath $prog -Append
}

function Note([string]$s) { $s | Tee-Object -FilePath $prog -Append }

function PgConns {
    $c = docker ps --filter "ancestor=postgres:16-alpine" --filter "label=org.testcontainers" --format '{{.ID}}' 2>$null | Select-Object -First 1
    if (-not $c) { return "pg=none" }
    $n = docker exec $c psql -U test -d antiphon_test -Atc "select count(*) || '/' || (select setting from pg_settings where name='max_connections') from pg_stat_activity" 2>$null
    return "pg=$n"
}

$argv = @('--treenode-filter', $Filter, '--no-progress', '--no-ansi')
if ($Detailed) { $argv += @('--output', 'Detailed') }
$start = Get-Date
$p = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe) -ArgumentList $argv `
        -RedirectStandardOutput $log -RedirectStandardError $err -PassThru -NoNewWindow
Note "[$Tag] started pid=$($p.Id) at $($start.ToString('HH:mm:ss')) exe=$Exe filter=$Filter"

$lastSize = -1; $lastGrow = Get-Date; $lastCpu = 0.0; $lastSample = Get-Date; $stalled = $false
while (-not $p.HasExited) {
    Start-Sleep 5
    $size = (Get-Item $log -ErrorAction SilentlyContinue).Length
    if ($size -ne $lastSize) { $lastSize = $size; $lastGrow = Get-Date }
    $now = Get-Date
    if (($now - $lastSample).TotalSeconds -ge $SampleSec) {
        $gp = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
        $cpu = if ($gp) { $gp.CPU } else { 0 }
        $dcpu = $cpu - $lastCpu; $lastCpu = $cpu; $lastSample = $now
        $quiet = [int]($now - $lastGrow).TotalSeconds
        Note ("{0} t={1}s log={2} quiet={3}s cpu={4} dcpu={5} ws={6}MB thr={7} {8}" -f $now.ToString('HH:mm:ss'),
            [int]($now - $start).TotalSeconds, $size, $quiet, [math]::Round($cpu, 1), [math]::Round($dcpu, 2),
            [math]::Round($gp.WorkingSet64 / 1MB), $gp.Threads.Count, (PgConns))
        if ($quiet -ge $QuietSec -and $dcpu -lt $CpuFloor) { $stalled = $true; break }
    }
    if (($now - $start).TotalSeconds -ge $HardTimeoutSec) { $stalled = $true; Note "[$Tag] hard timeout"; break }
}

$elapsed = [int]((Get-Date) - $start).TotalSeconds
if ($p.HasExited -and -not $stalled) {
    Note "[$Tag] EXITED code=$($p.ExitCode) after ${elapsed}s"
} else {
    Note "[$Tag] STALLED after ${elapsed}s; stdout last grew $([int]((Get-Date) - $lastGrow).TotalSeconds)s ago"
    $tree = Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq $p.Id -or $_.ProcessId -eq $p.Id }
    foreach ($t in $tree) {
        $gp = Get-Process -Id $t.ProcessId -ErrorAction SilentlyContinue
        Note "  proc pid=$($t.ProcessId) parent=$($t.ParentProcessId) name=$($t.Name) cpu=$([math]::Round($gp.CPU,2)) threads=$($gp.Threads.Count) cmd=$($t.CommandLine.Substring(0, [Math]::Min(200, $t.CommandLine.Length)))"
        foreach ($g in (Get-CimInstance Win32_Process -Filter "ParentProcessId=$($t.ProcessId)")) {
            Note "    child pid=$($g.ProcessId) name=$($g.Name) cmd=$($g.CommandLine.Substring(0, [Math]::Min(200, $g.CommandLine.Length)))"
        }
    }
    $a = (Get-Process -Id $p.Id -EA SilentlyContinue).CPU; Start-Sleep 10; $b = (Get-Process -Id $p.Id -EA SilentlyContinue).CPU
    Note "  cpu before 10s: $a ; after: $b"
    Note ("  docker: " + ((docker ps -a --filter label=org.testcontainers --format '{{.Names}} {{.Image}} {{.Status}}') -join ' | '))
    Note ("  " + (PgConns))
    $c = docker ps --filter "ancestor=postgres:16-alpine" --filter "label=org.testcontainers" --format '{{.ID}}' 2>$null | Select-Object -First 1
    if ($c) {
        docker exec $c psql -U test -d antiphon_test -Atc "select pid, state, wait_event_type, wait_event, left(query,160) from pg_stat_activity where datname='antiphon_test' order by state" 2>$null |
            Out-File (Join-Path $OutDir "$Tag-pg-activity.txt")
        Note "  pg_stat_activity -> $Tag-pg-activity.txt"
    }
    & dotnet-stack report -p $p.Id > (Join-Path $OutDir "$Tag-stack.txt") 2>&1
    Note "  managed thread stacks -> $Tag-stack.txt"
    & dotnet-dump collect -p $p.Id -o (Join-Path $OutDir "$Tag.dmp") 2>&1 | Select-Object -Last 1 | ForEach-Object { Note "  $_" }
    Note "  dump -> $Tag.dmp  (analyze: dotnet-dump analyze $Tag.dmp -c dumpasync -c exit)"
    foreach ($t in ($tree | Sort-Object ProcessId -Descending)) { Stop-Process -Id $t.ProcessId -Force -ErrorAction SilentlyContinue }
    Note "  killed"
}
Note "--- stdout tail ($log, $((Get-Item $log -EA SilentlyContinue).Length) bytes):"
Get-Content $log -Tail 12 -ErrorAction SilentlyContinue | ForEach-Object { Note $_ }
if ((Get-Item $err -EA SilentlyContinue).Length -gt 0) { Note "--- stderr tail:"; Get-Content $err -Tail 5 | ForEach-Object { Note $_ } }
if ($stalled) { exit 124 } else { exit $p.ExitCode }
