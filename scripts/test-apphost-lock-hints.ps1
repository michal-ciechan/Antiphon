#requires -Version 5.1
<#
.SYNOPSIS
    CARD-1533. Fresh restart-lock refusal and the no-dashboard timeout must
    not tell the operator to delete the lock. Isolated temp root, inert seams.
    Never touches live ports, processes, tasks, or logs/ under the checkout.
    ASCII-only for Windows PowerShell 5.1.
#>
param([ValidateSet('', 'Refusal', 'Timeout')][string]$Case = '')

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$script:passed = 0
$script:failed = 0

function Assert-C1533([bool]$Condition, [string]$Name, [string]$Detail = '') {
    if ($Condition) { $script:passed++; Write-Host "PASS $Name"; return }
    $script:failed++
    Write-Host "FAIL $Name - $Detail"
}

function New-C1533Fixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('c1533-' + [guid]::NewGuid().ToString('N'))
    $scripts = Join-Path $root 'scripts'
    New-Item -ItemType Directory -Path (Join-Path $scripts 'lib'), (Join-Path $root 'logs') -Force | Out-Null
    foreach ($name in @('restart-apphost.ps1', 'apphost-common.ps1', 'lib/live-startup-timing.ps1')) {
        Copy-Item -LiteralPath (Join-Path $here $name) -Destination (Join-Path $scripts $name)
    }
    '# inert CARD-1533' | Set-Content -LiteralPath (Join-Path $scripts 'check-daemon-build.ps1') -Encoding ASCII
    '# inert CARD-1533' | Set-Content -LiteralPath (Join-Path $root 'dev-aspire.ps1') -Encoding ASCII
    @'
$script:C1533Root = Split-Path $PSCommandPath -Parent
function Write-C1533Trace([string]$Line) { Add-Content -LiteralPath (Join-Path $script:C1533Root 'trace.txt') -Value $Line -Encoding ASCII }
function Get-AppHostWorktreeClassification { param($SourceRoot); [pscustomobject]@{ Verified = $true; IsMainWorktree = $true } }
function Get-AppHostSourceHead { param($SourceRoot); return ('a' * 40) }
function Test-AppHostTrackedEdits { param($SourceRoot); return $false }
function Get-AppHostGitIndexLock { param($SourceRoot); return $null }
function Get-AppHostPortOwners { param($Port); return @() }
function Stop-AppHostProcessId { param($ProcessId); Write-C1533Trace "stop-id $ProcessId" }
function Stop-AppHostProcessTree { param($ProcessId); Write-C1533Trace "stop-tree $ProcessId" }
function Get-AppHostStrayProcesses { return @() }
function Invoke-AppHostGracefulStop { param($TimeoutSec, $Reason); [pscustomobject]@{ Class = 'unreachable'; Pid = $null; Detail = 'seamed' } }
function Start-AppHostDevLaunch { param($ArgumentList, $OutputPath, $ErrorPath)
    Write-C1533Trace 'launch'
    if ($OutputPath) { '' | Set-Content -LiteralPath $OutputPath -Encoding ASCII }
    if ($ErrorPath) { '' | Set-Content -LiteralPath $ErrorPath -Encoding ASCII }
    $p = [pscustomobject]@{ Id = 99999; HasExited = $false; ExitCode = $null }
    $p | Add-Member ScriptMethod Refresh { }
    return $p
}
function Stop-AppHostLaunchChild { param($Process); Write-C1533Trace 'child-stop' }
function Test-AppHostProcessAlive { param($ProcessId); return $true }
function Wait-AppHostPollInterval { param([int]$Seconds = 3); if ($Seconds -eq 3) { Start-Sleep -Milliseconds 30 } }
function Invoke-AppHostHealthProbe { param($TimeoutSec); [pscustomobject]@{ StatusCode = $null; Body = $null; Error = 'Connection refused' } }
function Invoke-AppHostDashboardProbe { param($TimeoutSec); [pscustomobject]@{ StatusCode = $null; Url = $null; Error = 'Connection refused' } }
function Invoke-AppHostVersionProbe { param($TimeoutSec); [pscustomobject]@{ StatusCode = $null; Body = $null; Error = 'Connection refused' } }
'@ | Set-Content -LiteralPath (Join-Path $root 'seams.ps1') -Encoding ASCII
    return $root
}

function Invoke-C1533Restart([string]$Root, [string[]]$Extra, [int]$LimitMs) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = (Get-Process -Id $PID).Path
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $Root
    $psi.EnvironmentVariables['ANTIPHON_APPHOST_TEST_SEAMS'] = (Join-Path $Root 'seams.ps1')
    $psi.EnvironmentVariables['TEMP'] = [IO.Path]::GetTempPath()
    $psi.EnvironmentVariables['TMP'] = [IO.Path]::GetTempPath()
    $argsList = @('-NoProfile', '-File', (Join-Path $Root 'scripts/restart-apphost.ps1'), '-SkipGracefulStop') + $Extra
    $psi.Arguments = ($argsList | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $p = [Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEndAsync()
    $stderr = $p.StandardError.ReadToEndAsync()
    $exited = $p.WaitForExit($LimitMs)
    if (-not $exited) { try { $p.Kill() } catch { }; $p.WaitForExit() }
    $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    Write-Host $text
    $trace = @()
    $tracePath = Join-Path $Root 'trace.txt'
    if (Test-Path -LiteralPath $tracePath) { $trace = @(Get-Content -LiteralPath $tracePath) }
    $exit = -1
    if ($exited) { $exit = $p.ExitCode }
    return [pscustomobject]@{ Exit = $exit; Text = $text; Trace = $trace; TimedOut = (-not $exited) }
}

function Test-C1533Refusal {
    $root = New-C1533Fixture
    try {
        $lock = Join-Path $root 'logs/apphost.restart.lock'
        $stamp = '{0} {1}' -f 424242, [datetime]::UtcNow.ToString('o')
        [IO.File]::WriteAllText($lock, $stamp)
        $before = [IO.File]::ReadAllText($lock)
        $run = Invoke-C1533Restart $root @('-TimeoutSec', '30', '-MaxWaitSec', '30') 20000
        $after = [IO.File]::ReadAllText($lock)
        $stops = @($run.Trace | Where-Object { $_ -match '^(stop-|launch|child-stop)' }).Count
        Assert-C1533 (-not $run.TimedOut) 'Refusal returns' "timedOut exit=$($run.Exit)"
        Assert-C1533 ($run.Exit -eq 3) 'Refusal exits 3' "exit=$($run.Exit)"
        Assert-C1533 ($stops -eq 0) 'Refusal kills and launches nothing' ($run.Trace -join ',')
        Assert-C1533 ($after -eq $before) 'Refusal leaves the fresh lock bytes unchanged'
        Assert-C1533 ($run.Text -match 'REFUSED: another restart is in flight') 'Refusal names the in-flight restart' $run.Text
        Assert-C1533 ($run.Text -match 'Nothing was killed') 'Refusal says nothing was killed' $run.Text
        Assert-C1533 ($run.Text -match 'A dead holder is not a reason to delete') 'Refusal rejects holder death as delete permission' $run.Text
        Assert-C1533 ($run.Text -match 'two watchdog fires within five minutes') 'Refusal names the two-fire proof' $run.Text
        Assert-C1533 ($run.Text -match 'holder state Dead, launcher activity Absent, connection-refused health, and no launch lock') 'Refusal names the proof conditions' $run.Text
        Assert-C1533 ($run.Text -match 'wait for LockMaxAgeMinutes') 'Refusal prefers waiting out the fresh lock' $run.Text
        Assert-C1533 ($run.Text -notmatch 'genuinely gone') 'Refusal does not offer holder-death deletion' $run.Text
        Assert-C1533 ($run.Text -notmatch 'force a retry') 'Refusal does not offer a forced retry' $run.Text
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-C1533Timeout {
    $root = New-C1533Fixture
    try {
        $lock = Join-Path $root 'logs/apphost.restart.lock'
        $run = Invoke-C1533Restart $root @('-TimeoutSec', '1', '-MaxWaitSec', '4') 20000
        Assert-C1533 (-not $run.TimedOut) 'Timeout returns' "timedOut exit=$($run.Exit)"
        Assert-C1533 ($run.Exit -eq 1) 'Timeout exits 1' "exit=$($run.Exit)"
        Assert-C1533 ($run.Text -match 'AppHost did not come up') 'Timeout names the missed dashboard' $run.Text
        Assert-C1533 ($run.Text -match 'Do not re-run blind') 'Timeout still warns against a blind re-run' $run.Text
        Assert-C1533 ($run.Text -match 'Do not delete it to force a retry') 'Timeout refuses a delete-to-retry' $run.Text
        Assert-C1533 ($run.Text -match 'guarded dead-launch admission, or ordinary expiry') 'Timeout prefers admission or expiry' $run.Text
        Assert-C1533 ($run.Text -notmatch '; delete it to force a retry') 'Timeout does not tell the operator to delete the lock' $run.Text
        Assert-C1533 (Test-Path -LiteralPath $lock) 'Timeout keeps the restart lock'
        Assert-C1533 (@($run.Trace | Where-Object { $_ -eq 'launch' }).Count -eq 1) 'Timeout still launches once' ($run.Trace -join ',')
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-C1533Ascii {
    foreach ($relative in @('restart-apphost.ps1', 'test-apphost-lock-hints.ps1')) {
        $path = Join-Path $here $relative
        $bytes = [IO.File]::ReadAllBytes($path)
        $high = @($bytes | Where-Object { $_ -gt 127 }).Count
        Assert-C1533 ($high -eq 0) "ASCII $relative" "high=$high"
        $errs = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errs)
        $n = 0
        if ($null -ne $errs) { $n = @($errs).Count }
        Assert-C1533 ($n -eq 0) "parse $relative" "errors=$n"
    }
}

if ($Case -eq '' -or $Case -eq 'Refusal') { Test-C1533Refusal }
if ($Case -eq '' -or $Case -eq 'Timeout') { Test-C1533Timeout }
if ($Case -eq '') { Test-C1533Ascii }

Write-Host ''
Write-Host ('CARD-1533 lock hints: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0 -or $script:passed -eq 0) { exit 1 }
exit 0
