# Offline witness for the -PreserveSessions patch. Each case runs in its own pwsh process
# via run-case.ps1 (all process/CLI/clock/launch effects mocked). Exit 1 on any failed expectation.
#
# Hardening (2026-10-22, task 09570633): the first version of this harness hit the caller's
# `timeout` (exit 124) because pwsh -File passed the case arguments as one literal string, so
# update-claude.ps1 ran with its defaults, took the elevated branch and called an UNMOCKED
# Start-Process -Verb RunAs: a real UAC consent prompt opened and blocked the case forever.
# Now: run-case.ps1 mocks Start-Process and refuses any case without -NoElevate, and every
# case has its own hard timeout here, so a blocked case fails fast instead of hanging.
param([int]$CaseTimeoutSec = 60)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$orig = Join-Path $root 'orig\scripts\update-claude.ps1'
$patched = Join-Path $root 'patched\scripts\update-claude.ps1'
$out = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$workDir = $out   # exists; the script only Test-Paths it
$pwshExe = (Get-Process -Id $PID).Path

$before = @(Get-Process -Name claude -ErrorAction SilentlyContinue).Count

$cases = @(
    @{ Name = 'A-current-daily-default';      Script = $orig;    Args = @('-NoElevate', '-NoLaunch');                                   Remote = $true;  Fails = $false; ExpectStops = @(4000001, 4000005); ExpectRc = 0;    ExpectLaunch = 0 }
    @{ Name = 'B-preserve-default';           Script = $patched; Args = @('-NoElevate', '-PreserveSessions', '-NoLaunch');              Remote = $true;  Fails = $false; ExpectStops = @();                 ExpectRc = 0;    ExpectLaunch = 0 }
    @{ Name = 'C-preserve-locked-defers';     Script = $patched; Args = @('-NoElevate', '-PreserveSessions', '-NoLaunch');              Remote = $true;  Fails = $true;  ExpectStops = @();                 ExpectRc = 2;    ExpectLaunch = 0 }
    @{ Name = 'D-preserve-no-double-launch';  Script = $patched; Args = @('-NoElevate', '-PreserveSessions', '-WorkDir', $workDir);     Remote = $true;  Fails = $false; ExpectStops = @();                 ExpectRc = 0;    ExpectLaunch = 0 }
    @{ Name = 'E-preserve-launch-if-absent';  Script = $patched; Args = @('-NoElevate', '-PreserveSessions', '-WorkDir', $workDir);     Remote = $false; Fails = $false; ExpectStops = @();                 ExpectRc = 0;    ExpectLaunch = 1 }
    @{ Name = 'F-manual-mode-unchanged';      Script = $patched; Args = @('-NoElevate', '-NoLaunch');                                   Remote = $true;  Fails = $false; ExpectStops = @(4000001, 4000005); ExpectRc = 0;    ExpectLaunch = 0 }
    @{ Name = 'G-current-locked-kills';       Script = $orig;    Args = @('-NoElevate', '-NoLaunch');                                   Remote = $true;  Fails = $true;  ExpectStops = @(4000001, 4000005); ExpectRc = 1;    ExpectLaunch = 0; ExpectError = $true }
)

function Quote([string]$a) { if ($a -match '[\s"]') { '"' + ($a -replace '"', '\"') + '"' } else { $a } }

$failed = 0
$rows = foreach ($c in $cases) {
    $rec = Join-Path $out ($c.Name + '.json')
    $launchRec = Join-Path $out ($c.Name + '.launch.txt')
    $log = Join-Path $out ($c.Name + '.log')
    foreach ($f in $rec, $launchRec, $log, "$log.err") { if (Test-Path -LiteralPath $f) { [System.IO.File]::Delete($f) } }
    $argv = @('-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'run-case.ps1'), '-Script', $c.Script, '-Record', $rec, '-LaunchRecord', $launchRec)
    if ($c.Remote) { $argv += '-WithRemoteSession' }
    if ($c.Fails) { $argv += '-UpdateFails' }
    $argv += '-ScriptArgsSpec'; $argv += ('args=' + ($c.Args -join '|'))
    Write-Host "== $($c.Name): update-claude.ps1 $($c.Args -join ' ')"
    $p = Start-Process -FilePath $pwshExe -ArgumentList (($argv | ForEach-Object { Quote $_ }) -join ' ') `
        -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    if (-not $p.WaitForExit($CaseTimeoutSec * 1000)) {
        $p.Kill($true)   # only this harness's own child pwsh tree
        $failed++
        [pscustomobject]@{ Case = $c.Name; Result = "FAIL (timeout ${CaseTimeoutSec}s)" }
        continue
    }
    Get-Content -LiteralPath $log, "$log.err" -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "   $_" }
    if (-not (Test-Path -LiteralPath $rec)) { $failed++; [pscustomobject]@{ Case = $c.Name; Result = 'FAIL (no record)' }; continue }
    $r = Get-Content $rec -Raw | ConvertFrom-Json
    $stops = @($r.stop | ForEach-Object { [int]$_ })
    $okStops = (($stops | Sort-Object) -join ',') -eq (($c.ExpectStops | Sort-Object) -join ',')
    $okRc = ([int]$r.exitCode -eq $c.ExpectRc)
    $launches = @($r.launch | Where-Object { $_ })   # an empty JSON array comes back as $null
    $okLaunch = ($launches.Count -eq $c.ExpectLaunch)
    $ok = $okStops -and $okRc -and $okLaunch -and ($c.ExpectError -or -not $r.error)
    if (-not $ok) { $failed++ }
    [pscustomobject]@{
        Case = $c.Name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' })
        StopCalls = ($stops -join ','); ExpectedStops = ($c.ExpectStops -join ',')
        UpdateCalls = (@($r.update | Where-Object { $_ -like 'update*' }).Count)
        Rc = $r.exitCode; ExpRc = $c.ExpectRc; Launches = $launches.Count; ExpLaunch = $c.ExpectLaunch; Error = $r.error
    }
}

''
$rows | Format-Table -AutoSize | Out-String -Width 220
$after = @(Get-Process -Name claude -ErrorAction SilentlyContinue).Count
# Live agent sessions start and end on their own, so this count is reported, not asserted; the
# guarantee is run-case.ps1's refusal to start unless every dangerous command is a mock.
"real claude.exe processes before/after harness (informational): $before / $after"
"cases: $($cases.Count), failed: $failed"
if ($failed -gt 0) { exit 1 }
