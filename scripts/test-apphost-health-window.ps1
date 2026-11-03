#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1680: restart-apphost.ps1 waits while the launch makes progress.

    -TimeoutSec is a no-progress window that a growing log, a launcher output
    line, the dashboard URL file, the server port opening or a non-200 /health
    answer resets, up to -MaxWaitSec. A failed launcher or a vanished AppHost
    ends the wait at once. apphost.log is rotated, not emptied.

    Copied script tree in a temp git repo plus inert seams; a fake launcher
    acts per poll. Never touches live ports, processes, tasks or logs/ under
    the checkout. Runs on Linux and Windows. ASCII-only for Windows PowerShell 5.1.
#>
param([ValidateSet('','W1','W2','W3','W4','W5','W6')][string]$Case = '')

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$script:passed = 0
$script:failed = 0

function Assert-C1680([bool]$Condition, [string]$Name, [string]$Detail = '') {
    if ($Condition) { $script:passed++; Write-Host "PASS $Name"; return }
    $script:failed++
    Write-Host "FAIL $Name - $Detail"
}

function New-C1680Fixture($Config) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('c1680-' + [guid]::NewGuid().ToString('N'))
    $scripts = Join-Path $root 'scripts'
    New-Item -ItemType Directory -Path (Join-Path $scripts 'lib'), (Join-Path $root 'logs') -Force | Out-Null
    & git -C $root init -q | Out-Null
    & git -C $root config user.email c1680@example.test
    & git -C $root config user.name c1680
    & git -C $root config commit.gpgsign false
    foreach ($name in @('restart-apphost.ps1', 'apphost-common.ps1', 'lib/live-startup-timing.ps1')) {
        Copy-Item -LiteralPath (Join-Path $here $name) -Destination (Join-Path $scripts $name)
    }
    '# inert CARD-1680' | Set-Content -LiteralPath (Join-Path $scripts 'check-daemon-build.ps1') -Encoding ASCII
    '# inert CARD-1680' | Set-Content -LiteralPath (Join-Path $root 'dev-aspire.ps1') -Encoding ASCII
    & git -C $root add -A | Out-Null
    & git -C $root commit -qm 'c1680 fixture' | Out-Null
    ($Config | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath (Join-Path $root 'config.json') -Encoding ASCII
    @'
$script:C1680Root = Split-Path $PSCommandPath -Parent
$script:C1680Cfg = Get-Content -LiteralPath (Join-Path $script:C1680Root 'config.json') -Raw | ConvertFrom-Json
$script:C1680Poll = 0
$script:C1680Logs = Join-Path $script:C1680Root 'logs'
function Write-C1680Trace([string]$Line) { Add-Content -LiteralPath (Join-Path $script:C1680Root 'trace.txt') -Value $Line -Encoding ASCII }
function Test-C1680At([string]$Name) {
    $at = $script:C1680Cfg.$Name
    return ($null -ne $at -and $script:C1680Poll -ge [int]$at)
}
function Get-AppHostWorktreeClassification { param($SourceRoot); [pscustomobject]@{ Verified = $true; IsMainWorktree = $true } }
function Test-AppHostTrackedEdits { param($SourceRoot); return $false }
function Get-AppHostGitIndexLock { param($SourceRoot); return $null }
function Get-AppHostStrayProcesses { return @() }
function Stop-AppHostProcessId { param($ProcessId); Write-C1680Trace "stop-id $ProcessId" }
function Stop-AppHostProcessTree { param($ProcessId); Write-C1680Trace "stop-tree $ProcessId" }
function Invoke-AppHostGracefulStop { param($TimeoutSec, $Reason); [pscustomobject]@{ Class = 'unreachable'; Pid = $null; Detail = 'seamed' } }
function Get-AppHostPortOwners { param($Port)
    if ($Port -eq 17202 -and (Test-C1680At 'portAt')) { return @(4343) }
    return @()
}
function Test-AppHostProcessAlive { param($ProcessId)
    Write-C1680Trace "alive $ProcessId"
    return -not (Test-C1680At 'appHostDeadAt')
}
function Start-AppHostDevLaunch { param($ArgumentList, $OutputPath, $ErrorPath)
    Write-C1680Trace 'launch'
    $script:C1680Out = $OutputPath
    '' | Set-Content -LiteralPath $OutputPath -Encoding ASCII
    $p = [pscustomobject]@{ Id = 99999; HasExited = $false; ExitCode = $null }
    $p | Add-Member ScriptMethod Refresh {
        if (Test-C1680At 'launcherExitAt') { $this.HasExited = $true; $this.ExitCode = [int]$script:C1680Cfg.launcherExitCode }
    }
    return $p
}
function Stop-AppHostLaunchChild { param($Process); Write-C1680Trace 'child-stop' }
function Wait-AppHostPollInterval { param([int]$Seconds = 3)
    if ($Seconds -ne 3) { return }
    Start-Sleep -Milliseconds ([int]$script:C1680Cfg.pollMs)
    $script:C1680Poll++
    Write-C1680Trace "poll $script:C1680Poll"
    $n = $script:C1680Poll
    if ($null -ne $script:C1680Cfg.growUntil -and $n -le [int]$script:C1680Cfg.growUntil) {
        Add-Content -LiteralPath (Join-Path $script:C1680Logs 'apphost.log') -Value "build line $n" -Encoding ASCII
    }
    if ($null -ne $script:C1680Cfg.outUntil -and $n -le [int]$script:C1680Cfg.outUntil) {
        Add-Content -LiteralPath $script:C1680Out -Value "BUILD SLOT waiting label=apphost-build position=1 elapsed=${n}m" -Encoding ASCII
    }
    if ($n -eq [int]$script:C1680Cfg.dashAt) {
        Set-Content -LiteralPath (Join-Path $script:C1680Logs 'apphost-dashboard-url.txt') 'http://127.0.0.1:9/' -Encoding ASCII
    }
    if ($null -ne $script:C1680Cfg.pidAt -and $n -eq [int]$script:C1680Cfg.pidAt) {
        Set-Content -LiteralPath (Join-Path $script:C1680Logs 'apphost.pid') '424242' -Encoding ASCII
    }
}
function Invoke-AppHostHealthProbe { param($TimeoutSec)
    Write-C1680Trace "health $script:C1680Poll"
    if (Test-C1680At 'healthAt') { return [pscustomobject]@{ StatusCode = 200; Body = 'Healthy'; Error = $null } }
    if (Test-C1680At 'answerAt') { return [pscustomobject]@{ StatusCode = 503; Body = $null; Error = 'migrating' } }
    return [pscustomobject]@{ StatusCode = $null; Body = $null; Error = 'Connection refused' }
}
function Invoke-AppHostVersionProbe { param($TimeoutSec)
    $sha = ((& git -C $script:C1680Root rev-parse HEAD) | Select-Object -First 1).Trim()
    [pscustomobject]@{ StatusCode = 200; Body = ('{{"version":"{0}","informationalVersion":"{0}","capabilities":["land-v2"]}}' -f $sha); Error = $null }
}
'@ | Set-Content -LiteralPath (Join-Path $root 'seams.ps1') -Encoding ASCII
    return $root
}

function Invoke-C1680Restart([string]$Root, [string[]]$Extra, [int]$LimitMs = 90000) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = (Get-Process -Id $PID).Path
    $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $Root
    $psi.EnvironmentVariables['ANTIPHON_APPHOST_TEST_SEAMS'] = (Join-Path $Root 'seams.ps1')
    $argsList = @('-NoProfile', '-File', (Join-Path $Root 'scripts/restart-apphost.ps1'), '-SkipGracefulStop') + $Extra
    # Fixture paths have no spaces on Linux; quote each argument for Windows.
    $psi.Arguments = ($argsList | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEndAsync(); $stderr = $p.StandardError.ReadToEndAsync()
    $exited = $p.WaitForExit($LimitMs)
    if (-not $exited) { $p.Kill(); $p.WaitForExit() }
    $clock.Stop()
    $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    Write-Host $text
    $trace = @()
    $tracePath = Join-Path $Root 'trace.txt'
    if (Test-Path -LiteralPath $tracePath) { $trace = @(Get-Content -LiteralPath $tracePath) }
    $exit = -1
    if ($exited) { $exit = $p.ExitCode }
    return [pscustomobject]@{ Exit = $exit; Text = $text; Seconds = $clock.Elapsed.TotalSeconds; Trace = $trace; TimedOut = -not $exited }
}

function Get-C1680Polls($Run) { @($Run.Trace | Where-Object { $_ -match '^poll ' }).Count }

function Invoke-C1680Case([string]$Id, $Config, [string[]]$Extra, [scriptblock]$Check, [scriptblock]$Before = $null) {
    $root = New-C1680Fixture $Config
    try {
        if ($Before) { & $Before $root }
        $run = Invoke-C1680Restart $root $Extra
        $lock = Join-Path $root 'logs/apphost.restart.lock'
        & $Check $run $lock $root
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$cases = @{
    # Still starting past the window: the log grows every poll, health answers after 6+ windows.
    W1 = {
        Invoke-C1680Case 'W1' @{ pollMs = 250; growUntil = 40; dashAt = 2; healthAt = 40 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 0) 'W1 a launch still progressing past TimeoutSec reaches exit 0' "exit=$($run.Exit) polls=$(Get-C1680Polls $run)"
            Assert-C1680 ((Get-C1680Polls $run) -ge 40) 'W1 waited through the progressing launch' "polls=$(Get-C1680Polls $run)"
            Assert-C1680 (-not (Test-Path -LiteralPath $lock)) 'W1 healthy exit releases the restart lock'
        }
    }
    # A BUILD SLOT wait in the launcher output and a 503 /health are progress too.
    W2 = {
        Invoke-C1680Case 'W2' @{ pollMs = 250; outUntil = 16; dashAt = 18; answerAt = 18; healthAt = 36 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 0) 'W2 launcher output and non-200 health extend the window' "exit=$($run.Exit) polls=$(Get-C1680Polls $run)"
        }
    }
    # Progress stops: fail one window after the last progress, long before the hard cap.
    W3 = {
        Invoke-C1680Case 'W3' @{ pollMs = 250; growUntil = 12; dashAt = 2 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            $polls = Get-C1680Polls $run
            Assert-C1680 ($run.Exit -eq 1) 'W3 stalled launch exits 1' "exit=$($run.Exit)"
            Assert-C1680 ($polls -gt 12) 'W3 progress extended the window past the first TimeoutSec' "polls=$polls"
            Assert-C1680 ($run.Seconds -lt 40) 'W3 stall fails at the no-progress window, not the hard cap' "seconds=$($run.Seconds)"
            Assert-C1680 ($run.Text -match 'no launch progress for 2s') 'W3 names the no-progress window' $run.Text
            Assert-C1680 ($run.Text -match 'last progress: apphost\.log grew') 'W3 names the last progress signal' $run.Text
            Assert-C1680 (Test-Path -LiteralPath $lock) 'W3 timeout keeps the restart lock (child may still launch)'
        }
    }
    # Launcher gone: fail at once although the log keeps growing.
    W4 = {
        Invoke-C1680Case 'W4' @{ pollMs = 250; growUntil = 400; dashAt = 2; launcherExitAt = 3; launcherExitCode = 9 } @('-TimeoutSec', '30', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 1) 'W4 failed launcher exits 1' "exit=$($run.Exit)"
            Assert-C1680 ((Get-C1680Polls $run) -le 4) 'W4 failed launcher ends the wait at once' "polls=$(Get-C1680Polls $run) seconds=$($run.Seconds)"
            Assert-C1680 ($run.Text -match 'launcher exited 9') 'W4 names the launcher exit code' $run.Text
            Assert-C1680 (-not (Test-Path -LiteralPath $lock)) 'W4 failed launcher releases the restart lock'
        }
    }
    # AppHost gone (fresh pid file, process dead, no server port): fail at once.
    W5 = {
        Invoke-C1680Case 'W5' @{ pollMs = 250; growUntil = 400; dashAt = 2; pidAt = 2; appHostDeadAt = 4 } @('-TimeoutSec', '30', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 1) 'W5 vanished AppHost exits 1' "exit=$($run.Exit)"
            Assert-C1680 ((Get-C1680Polls $run) -le 5) 'W5 vanished AppHost ends the wait at once' "polls=$(Get-C1680Polls $run) seconds=$($run.Seconds)"
            Assert-C1680 ($run.Text -match 'AppHost process 424242 exited') 'W5 names the vanished AppHost PID' $run.Text
            Assert-C1680 (@($run.Trace | Where-Object { $_ -eq 'child-stop' }).Count -eq 1) 'W5 stops the launch child'
            Assert-C1680 (-not (Test-Path -LiteralPath $lock)) 'W5 vanished AppHost releases the restart lock'
        }
        # A stale pid file from the previous AppHost is not this launch's AppHost.
        Invoke-C1680Case 'W5b' @{ pollMs = 250; growUntil = 6; dashAt = 2; appHostDeadAt = 1; healthAt = 6 } @('-TimeoutSec', '30', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 0) 'W5b stale pid file from an earlier launch is ignored' "exit=$($run.Exit)"
        } {
            param($root)
            $old = Join-Path $root 'logs/apphost.pid'
            '515151' | Set-Content -LiteralPath $old -Encoding ASCII
            (Get-Item -LiteralPath $old).LastWriteTimeUtc = [datetime]::UtcNow.AddMinutes(-30)
        }
    }
    # Hard cap: health keeps answering 503 and the log keeps growing.
    W6 = {
        Invoke-C1680Case 'W6' @{ pollMs = 250; growUntil = 100000; dashAt = 2; answerAt = 2 } @('-TimeoutSec', '2', '-MaxWaitSec', '5') {
            param($run, $lock)
            Assert-C1680 (-not $run.TimedOut) 'W6 the hard cap ends a progressing wait' "seconds=$($run.Seconds)"
            Assert-C1680 ($run.Exit -eq 1) 'W6 hard cap exits 1' "exit=$($run.Exit)"
            Assert-C1680 ((Get-C1680Polls $run) -gt 8) 'W6 waited past TimeoutSec toward the cap' "polls=$(Get-C1680Polls $run)"
            Assert-C1680 ($run.Text -match 'hard cap of 5s') 'W6 names the hard cap' $run.Text
            Assert-C1680 (Test-Path -LiteralPath $lock) 'W6 hard cap keeps the restart lock'
        }
        # Rotation: the previous runs' apphost.log survives as apphost.log.1 / .2.
        Invoke-C1680Case 'W6r' @{ pollMs = 50; dashAt = 1; healthAt = 1 } @('-TimeoutSec', '5') {
            param($run, $lock, $root)
            Assert-C1680 ($run.Exit -eq 0) 'W6r rotation run exit 0' "exit=$($run.Exit)"
            $one = Join-Path $root 'logs/apphost.log.1'
            $two = Join-Path $root 'logs/apphost.log.2'
            Assert-C1680 ((Test-Path -LiteralPath $one) -and ((Get-Content -LiteralPath $one -Raw) -match 'previous-run LIVE_TIMING')) 'W6r previous apphost.log kept as apphost.log.1'
            Assert-C1680 ((Test-Path -LiteralPath $two) -and ((Get-Content -LiteralPath $two -Raw) -match 'older-run')) 'W6r older history shifts to apphost.log.2'
            $current = Join-Path $root 'logs/apphost.log'
            Assert-C1680 ((Test-Path -LiteralPath $current) -and ((Get-Content -LiteralPath $current -Raw) -notmatch 'previous-run')) 'W6r current apphost.log starts empty'
        } {
            param($root)
            'previous-run LIVE_TIMING build' | Set-Content -LiteralPath (Join-Path $root 'logs/apphost.log') -Encoding ASCII
            'older-run' | Set-Content -LiteralPath (Join-Path $root 'logs/apphost.log.1') -Encoding ASCII
        }
        $bytes = [IO.File]::ReadAllBytes($PSCommandPath)
        Assert-C1680 (@($bytes | Where-Object { $_ -gt 127 }).Count -eq 0) 'W6 ASCII test-apphost-health-window.ps1'
    }
}

$ids = @('W1', 'W2', 'W3', 'W4', 'W5', 'W6')
if ($Case) { $ids = @($Case) }
foreach ($id in $ids) { & $cases[$id] }

Write-Host ''
Write-Host ('C1680 health-window: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0 -or $script:passed -eq 0) { exit 1 }
exit 0
