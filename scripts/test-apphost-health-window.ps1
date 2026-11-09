#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1680: restart-apphost.ps1 waits while the launch makes progress.

    -TimeoutSec is a no-progress window that a growing log, a launcher output
    line, the dashboard URL file, the server port opening or a non-200 /health
    answer resets, up to -MaxWaitSec. A failed launcher or a vanished AppHost
    ends the wait at once. apphost.log is rotated, not emptied.
    W7-W11 (CARD-1692) witness the CARD-1680 mutation survivors: a launcher
    exit 0 or unreadable exit code is not a failure, a foreign restart stamp
    survives, a listening 17202 vetoes the vanished-AppHost exit, the port
    signal counts both ways, and rotation keeps exactly ten files.
    W12 (CARD-1806): with no dashboard URL file the restart asks 17205 itself.

    Copied script tree in a temp git repo plus inert seams; a fake launcher
    acts per poll. Never touches live ports, processes, tasks or logs/ under
    the checkout. Runs on Linux and Windows. ASCII-only for Windows PowerShell 5.1.
#>
param([ValidateSet('','W1','W2','W3','W4','W5','W6','W7','W8','W9','W10','W11','W12')][string]$Case = '')

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
    if ($Port -eq 17202 -and (Test-C1680At 'portAt') -and -not (Test-C1680At 'portUntil')) { return @(4343) }
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
        if (Test-C1680At 'launcherExitAt') {
            $this.HasExited = $true
            # An exited process whose exit code cannot be read reports ExitCode $null.
            if ($script:C1680Cfg.launcherCodeUnreadable) { $this.ExitCode = $null }
            else { $this.ExitCode = [int]$script:C1680Cfg.launcherExitCode }
        }
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
    if ($null -ne $script:C1680Cfg.foreignAt -and $n -eq [int]$script:C1680Cfg.foreignAt) {
        # Another restart's stamp replaces this run's in apphost.restart.lock. Written
        # through the held stream: FileShare.Read refuses a second writer on Windows.
        $foreign = '{0} {1}' -f 777777, [datetime]::UtcNow.ToString('o')
        $bytes = [Text.Encoding]::ASCII.GetBytes($foreign)
        $script:lock.Stream.Position = 0
        $script:lock.Stream.SetLength(0)
        $script:lock.Stream.Write($bytes, 0, $bytes.Length)
        $script:lock.Stream.Flush()
        [IO.File]::WriteAllText((Join-Path $script:C1680Root 'foreign.txt'), $foreign)
    }
}
function Invoke-AppHostHealthProbe { param($TimeoutSec)
    Write-C1680Trace "health $script:C1680Poll"
    if (Test-C1680At 'healthAt') { return [pscustomobject]@{ StatusCode = 200; Body = 'Healthy'; Error = $null } }
    if (Test-C1680At 'answerAt') { return [pscustomobject]@{ StatusCode = 503; Body = $null; Error = 'migrating' } }
    return [pscustomobject]@{ StatusCode = $null; Body = $null; Error = 'Connection refused' }
}
function Invoke-AppHostDashboardProbe { param($TimeoutSec)
    Write-C1680Trace "dashboard $script:C1680Poll"
    if (Test-C1680At 'dashUpAt') { return [pscustomobject]@{ StatusCode = 200; Url = 'http://localhost:17205'; Error = $null } }
    return [pscustomobject]@{ StatusCode = $null; Url = $null; Error = 'Connection refused' }
}
function Invoke-AppHostVersionProbe { param($TimeoutSec)
    $sha = ((& git -C $script:C1680Root rev-parse HEAD) | Select-Object -First 1).Trim()
    if ($script:C1680Cfg.otherSha) { $sha = 'b' * 40 }
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
        # A pid file left by the previous AppHost is not this launch's AppHost, whatever its mtime.
        Invoke-C1680Case 'W5b' @{ pollMs = 250; growUntil = 6; dashAt = 2; appHostDeadAt = 1; healthAt = 6 } @('-TimeoutSec', '30', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 0) 'W5b stale pid file from an earlier launch is ignored' "exit=$($run.Exit)"
        } {
            param($root)
            $old = Join-Path $root 'logs/apphost.pid'
            '515151' | Set-Content -LiteralPath $old -Encoding ASCII
            # Future-dated (clock skew, or written moments before launch): only a pid file
            # rewritten after the launch belongs to this launch.
            (Get-Item -LiteralPath $old).LastWriteTimeUtc = [datetime]::UtcNow.AddMinutes(5)
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
    # CARD-1692 (M20): dev-aspire.ps1 exits 0 once the dashboard is ready; that is not a failed launch.
    W7 = {
        Invoke-C1680Case 'W7' @{ pollMs = 250; growUntil = 8; dashAt = 2; healthAt = 10; launcherExitAt = 3; launcherExitCode = 0 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 0) 'W7 launcher exit 0 keeps waiting to exit 0' "exit=$($run.Exit) polls=$(Get-C1680Polls $run)"
            Assert-C1680 ((Get-C1680Polls $run) -ge 10) 'W7 launcher exit 0 does not end the wait' "polls=$(Get-C1680Polls $run)"
            Assert-C1680 ($run.Text -notmatch 'launcher exited') 'W7 launcher exit 0 is not reported as a failure' $run.Text
            Assert-C1680 (-not (Test-Path -LiteralPath $lock)) 'W7 healthy exit releases the restart lock'
        }
        Invoke-C1680Case 'W7t' @{ pollMs = 250; growUntil = 6; dashAt = 2; launcherExitAt = 3; launcherExitCode = 0 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 1) 'W7t launcher exit 0 then stall exits 1' "exit=$($run.Exit)"
            Assert-C1680 ((Get-C1680Polls $run) -gt 6) 'W7t launcher exit 0 waits out the window' "polls=$(Get-C1680Polls $run)"
            Assert-C1680 ($run.Text -match 'no launch progress for 2s' -and $run.Text -notmatch 'launcher exited') 'W7t ends at the no-progress window' $run.Text
            Assert-C1680 (Test-Path -LiteralPath $lock) 'W7t timeout after launcher exit 0 keeps the restart lock'
        }
        # CARD-1692 (M21): an exited launcher whose exit code is unreadable is not a failure either.
        Invoke-C1680Case 'W7n' @{ pollMs = 250; growUntil = 8; dashAt = 2; healthAt = 10; launcherExitAt = 3; launcherCodeUnreadable = $true } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 0) 'W7n unreadable launcher exit code keeps waiting to exit 0' "exit=$($run.Exit) polls=$(Get-C1680Polls $run)"
            Assert-C1680 ((Get-C1680Polls $run) -ge 10) 'W7n unreadable launcher exit code does not end the wait' "polls=$(Get-C1680Polls $run)"
            Assert-C1680 ($run.Text -notmatch 'launcher exited') 'W7n unreadable exit code is not reported as a failure' $run.Text
        }
        Invoke-C1680Case 'W7nt' @{ pollMs = 250; growUntil = 6; dashAt = 2; launcherExitAt = 3; launcherCodeUnreadable = $true } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 1) 'W7nt unreadable exit code then stall exits 1' "exit=$($run.Exit)"
            Assert-C1680 ((Get-C1680Polls $run) -gt 6) 'W7nt unreadable exit code waits out the window' "polls=$(Get-C1680Polls $run)"
            Assert-C1680 ($run.Text -match 'no launch progress for 2s' -and $run.Text -notmatch 'launcher exited') 'W7nt ends at the no-progress window' $run.Text
            Assert-C1680 (Test-Path -LiteralPath $lock) 'W7nt timeout after an unreadable exit code keeps the restart lock'
        }
    }
    # CARD-1692 (M19): a lock-releasing exit never deletes another restart's stamp.
    W8 = {
        $foreignKept = {
            param($lock, $root, [string]$Name)
            $expected = [IO.File]::ReadAllText((Join-Path $root 'foreign.txt'))
            $actual = $null
            if (Test-Path -LiteralPath $lock) { $actual = [IO.File]::ReadAllText($lock) }
            Assert-C1680 ($actual -ceq $expected) $Name "expected=[$expected] actual=[$actual]"
        }
        Invoke-C1680Case 'W8' @{ pollMs = 250; growUntil = 400; dashAt = 2; foreignAt = 2; launcherExitAt = 3; launcherExitCode = 9 } @('-TimeoutSec', '30', '-MaxWaitSec', '60') {
            param($run, $lock, $root)
            Assert-C1680 ($run.Exit -eq 1 -and $run.Text -match 'launcher exited 9') 'W8 failed launcher exits 1' "exit=$($run.Exit)"
            & $foreignKept $lock $root 'W8 failed launcher keeps a foreign restart stamp byte-for-byte'
        }
        Invoke-C1680Case 'W8b' @{ pollMs = 250; growUntil = 400; dashAt = 2; pidAt = 2; foreignAt = 2; appHostDeadAt = 4 } @('-TimeoutSec', '30', '-MaxWaitSec', '60') {
            param($run, $lock, $root)
            Assert-C1680 ($run.Exit -eq 1 -and $run.Text -match 'AppHost process 424242 exited') 'W8b vanished AppHost exits 1' "exit=$($run.Exit)"
            & $foreignKept $lock $root 'W8b vanished AppHost keeps a foreign restart stamp byte-for-byte'
        }
        Invoke-C1680Case 'W8h' @{ pollMs = 250; dashAt = 1; foreignAt = 1; healthAt = 2 } @('-TimeoutSec', '30', '-MaxWaitSec', '60') {
            param($run, $lock, $root)
            Assert-C1680 ($run.Exit -eq 0) 'W8h healthy restart exits 0' "exit=$($run.Exit)"
            & $foreignKept $lock $root 'W8h healthy exit keeps a foreign restart stamp byte-for-byte'
        }
    }
    # CARD-1692 (M25, M09a, M09b): the server port seam.
    W9 = {
        # The AppHost wrapper PID is dead but a server listens on 17202: keep waiting.
        Invoke-C1680Case 'W9' @{ pollMs = 250; growUntil = 8; dashAt = 2; pidAt = 2; appHostDeadAt = 4; portAt = 1; healthAt = 10 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 0) 'W9 dead wrapper with 17202 listening waits to exit 0' "exit=$($run.Exit) polls=$(Get-C1680Polls $run)"
            Assert-C1680 ($run.Text -notmatch 'exited before health') 'W9 no vanished-AppHost exit while 17202 listens' $run.Text
            Assert-C1680 (@($run.Trace | Where-Object { $_ -eq 'child-stop' }).Count -eq 0) 'W9 launch child left running'
        }
        Invoke-C1680Case 'W9t' @{ pollMs = 250; growUntil = 6; dashAt = 2; pidAt = 2; appHostDeadAt = 4; portAt = 1 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 1) 'W9t dead wrapper with 17202 listening then stall exits 1' "exit=$($run.Exit)"
            Assert-C1680 ((Get-C1680Polls $run) -gt 6) 'W9t waits out the window while 17202 listens' "polls=$(Get-C1680Polls $run)"
            Assert-C1680 ($run.Text -match 'no launch progress for 2s' -and $run.Text -notmatch 'exited before health') 'W9t ends at the no-progress window' $run.Text
            Assert-C1680 (Test-Path -LiteralPath $lock) 'W9t timeout with 17202 listening keeps the restart lock'
        }
        # 17202 opening is the last progress.
        Invoke-C1680Case 'W9o' @{ pollMs = 250; dashAt = 1; portAt = 6 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 1) 'W9o stall exits 1' "exit=$($run.Exit)"
            Assert-C1680 ($run.Text -match 'last progress: server port 17202 listening=True') 'W9o 17202 opening counts as progress' $run.Text
        }
        # 17202 closing is the last progress.
        Invoke-C1680Case 'W9c' @{ pollMs = 250; portAt = 1; dashAt = 2; portUntil = 4 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 1) 'W9c stall exits 1' "exit=$($run.Exit)"
            Assert-C1680 ($run.Text -match 'last progress: server port 17202 listening=False') 'W9c 17202 closing counts as progress' $run.Text
        }
    }
    # CARD-1692 (M18, M08, M12): what the window credits and how the cap is set.
    W10 = {
        # Nothing changes after the launch: the state the launch started from is not progress.
        Invoke-C1680Case 'W10' @{ pollMs = 1000 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 1) 'W10 silent launch exits 1' "exit=$($run.Exit)"
            Assert-C1680 ((Get-C1680Polls $run) -le 2) 'W10 silent launch ends at the first window' "polls=$(Get-C1680Polls $run)"
            Assert-C1680 ($run.Text -match 'last progress: launch started' -and $run.Text -notmatch 'appeared') 'W10 pre-launch state is not credited as progress' $run.Text
        }
        # /health 200 with the wrong SHA: a growing log no longer extends the window.
        Invoke-C1680Case 'W10s' @{ pollMs = 250; growUntil = 100000; dashAt = 1; healthAt = 2; otherSha = $true } @('-TimeoutSec', '2', '-MaxWaitSec', '30') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 5) 'W10s wrong SHA after health exits 5' "exit=$($run.Exit)"
            Assert-C1680 ($run.Seconds -lt 15) 'W10s exit 5 within the current window, not the hard cap' "seconds=$($run.Seconds) polls=$(Get-C1680Polls $run)"
            Assert-C1680 ($run.Text -match 'no launch progress for 2s' -and $run.Text -notmatch 'hit the hard cap') 'W10s names the no-progress window' $run.Text
            Assert-C1680 (Test-Path -LiteralPath $lock) 'W10s exit 5 keeps the restart lock'
        }
        # -MaxWaitSec below -TimeoutSec is raised to -TimeoutSec.
        Invoke-C1680Case 'W10c' @{ pollMs = 250; dashAt = 1; healthAt = 8 } @('-TimeoutSec', '4', '-MaxWaitSec', '1') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 0) 'W10c MaxWaitSec below TimeoutSec is clamped up' "exit=$($run.Exit) polls=$(Get-C1680Polls $run)"
            Assert-C1680 ((Get-C1680Polls $run) -ge 8) 'W10c waited past MaxWaitSec' "polls=$(Get-C1680Polls $run)"
        }
    }
    # CARD-1692 (M15a, M15b): twelve rotations keep apphost.log.1 .. .10 and no .11.
    W11 = {
        $root = New-C1680Fixture @{ pollMs = 50; dashAt = 1; healthAt = 1 }
        try {
            $logs = Join-Path $root 'logs'
            $exits = @()
            foreach ($k in 1..12) {
                "generation $k" | Set-Content -LiteralPath (Join-Path $logs 'apphost.log') -Encoding ASCII
                $exits += (Invoke-C1680Restart $root @('-TimeoutSec', '5')).Exit
            }
            Assert-C1680 (@($exits | Where-Object { $_ -ne 0 }).Count -eq 0) 'W11 twelve restarts exit 0' ($exits -join ',')
            foreach ($i in 1..10) {
                $path = Join-Path $logs "apphost.log.$i"
                $text = $null
                if (Test-Path -LiteralPath $path) { $text = (Get-Content -LiteralPath $path -Raw).Trim() }
                Assert-C1680 ($text -eq "generation $(13 - $i)") "W11 apphost.log.$i holds generation $(13 - $i)" "text=[$text]"
            }
            Assert-C1680 (-not (Test-Path -LiteralPath (Join-Path $logs 'apphost.log.11'))) 'W11 no apphost.log.11'
        } finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    # CARD-1806: the cold build outlasts dev-aspire.ps1's 90 s dashboard-URL wait. The
    # launcher prints "Dashboard URL not found" and exits 0 without writing the URL file;
    # the build log then stops growing, and the dashboard (17205) and /health come up
    # after it gave up (poll 16 here, past the launcher's give-up at poll 10).
    W12 = {
        Invoke-C1680Case 'W12' @{ pollMs = 250; growUntil = 12; outUntil = 10; launcherExitAt = 10; launcherExitCode = 0; dashUpAt = 16; healthAt = 16 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock, $root)
            Assert-C1680 ($run.Exit -eq 0) 'W12 no URL file, dashboard and health up after the launcher gave up: exit 0' "exit=$($run.Exit) polls=$(Get-C1680Polls $run)"
            Assert-C1680 ($run.Text -match 'dashboard http://localhost:17205, backend healthy') 'W12 names the dashboard found on 17205' $run.Text
            Assert-C1680 (-not (Test-Path -LiteralPath (Join-Path $root 'logs/apphost-dashboard-url.txt'))) 'W12 the launcher wrote no URL file'
            Assert-C1680 (-not (Test-Path -LiteralPath $lock)) 'W12 healthy exit releases the restart lock'
        }
        # The dashboard answering is launch progress; the stall after it names it.
        Invoke-C1680Case 'W12d' @{ pollMs = 250; growUntil = 3; dashUpAt = 6 } @('-TimeoutSec', '2', '-MaxWaitSec', '60') {
            param($run, $lock)
            Assert-C1680 ($run.Exit -eq 1) 'W12d dashboard without backend health exits 1' "exit=$($run.Exit)"
            Assert-C1680 ($run.Text -match 'Dashboard up \(http://localhost:17205\) but backend health not confirmed') 'W12d reports the dashboard found on 17205' $run.Text
            Assert-C1680 ($run.Text -match 'last progress: dashboard answered on http://localhost:17205') 'W12d the dashboard answering counts as progress' $run.Text
            Assert-C1680 (Test-Path -LiteralPath $lock) 'W12d timeout keeps the restart lock'
        }
    }
}

$ids = @('W1', 'W2', 'W3', 'W4', 'W5', 'W6', 'W7', 'W8', 'W9', 'W10', 'W11', 'W12')
if ($Case) { $ids = @($Case) }
foreach ($id in $ids) { & $cases[$id] }

Write-Host ''
Write-Host ('C1680 health-window: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0 -or $script:passed -eq 0) { exit 1 }
exit 0
