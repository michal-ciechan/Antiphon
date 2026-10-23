#requires -Version 5.1
param([ValidateSet('Recovery','Launcher','Alert','Guards')][string]$Case)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:passed = 0
function Assert-C1527([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL $Name" }
    $script:passed++; Write-Host "PASS $Name"
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('c1527-' + [guid]::NewGuid().ToString('N'))
$scripts = Join-Path $fixture 'scripts'
$logs = Join-Path $fixture 'logs'
New-Item -ItemType Directory $scripts,$logs | Out-Null
try {
    foreach ($name in @('apphost-common.ps1','watchdog-apphost.ps1','restart-apphost.ps1')) {
        Copy-Item (Join-Path $PSScriptRoot $name) (Join-Path $scripts $name)
    }
    '# inert fixture' | Set-Content (Join-Path $scripts 'check-daemon-build.ps1')
    @'
function Get-AppHostWorktreeClassification { param($SourceRoot); [pscustomobject]@{ Verified=$true; IsMainWorktree=$true } }
function Get-AppHostSourceHead { param($SourceRoot); return ('a' * 40) }
function Test-AppHostTrackedEdits { param($SourceRoot); return $false }
function Get-AppHostGitIndexLock { param($SourceRoot); return $null }
function Get-AppHostPortOwners { param($Port); return @() }
function Get-AppHostStrayProcesses { return @() }
function Stop-AppHostProcessId { throw 'fixture must never kill' }
function Stop-AppHostProcessTree { throw 'fixture must never kill' }
function Wait-AppHostPollInterval { param($Seconds) }
function Get-ScheduledTask { param($TaskName); return @() }
function docker { $global:LASTEXITCODE=0 }
function Get-AppHostLaunchActivity { param($SourceRoot,$Lock); return 'Absent' }
function Get-AppHostWatchdogNow {
    $p=Join-Path (Split-Path $PSScriptRoot -Parent) 'now.txt'
    if (Test-Path $p) { return [datetime]::Parse((Get-Content $p -Raw)).ToUniversalTime() }
    return [datetime]::UtcNow
}
function Invoke-WebRequest {
    param($Uri,$UseBasicParsing,$TimeoutSec)
    if (Test-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'up')) { return [pscustomobject]@{StatusCode=200;Content='ok'} }
    throw 'Connection refused'
}
function Invoke-AppHostHealthProbe { param($TimeoutSec); [pscustomobject]@{StatusCode=$null;Error='Connection refused'} }
function Start-AppHostDevLaunch {
    param($ArgumentList,$OutputPath,$ErrorPath)
    'admitted' | Add-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'admitted')
    $p=[pscustomobject]@{HasExited=$true;ExitCode=17;Id=999999}
    $p | Add-Member ScriptMethod Refresh {}
    return $p
}
function Send-AppHostOutageEvent {
    param($Event)
    $root=Split-Path $PSScriptRoot -Parent
    if (Test-Path (Join-Path $root 'send-fails')) { return $false }
    $Event | ConvertTo-Json -Compress | Add-Content (Join-Path $root 'sent.jsonl')
    return $true
}
'@ | Add-Content (Join-Path $scripts 'apphost-common.ps1')
    @'
& (Join-Path $PSScriptRoot 'restart-apphost.ps1') -TimeoutSec 0 -SkipGracefulStop
exit $LASTEXITCODE
'@ | Set-Content (Join-Path $scripts 'restart-fixture.ps1')
    function Run-Fixture([string]$Entry, [string[]]$Extra = @()) {
        $psi = New-Object Diagnostics.ProcessStartInfo
        $psi.FileName = (Get-Process -Id $PID).Path
        $psi.UseShellExecute=$false; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
        $argsList=@('-NoProfile','-File',(Join-Path $scripts $Entry)) + $Extra
        # Fixtures have generated paths without spaces on Linux; quote each argument for Windows.
        $psi.Arguments=($argsList | ForEach-Object { '"' + $_.Replace('"','\"') + '"' }) -join ' '
        $p=[Diagnostics.Process]::Start($psi)
        $stdout=$p.StandardOutput.ReadToEndAsync(); $stderr=$p.StandardError.ReadToEndAsync()
        if (-not $p.WaitForExit(30000)) { $p.Kill(); throw 'fixture exceeded 30s' }
        $text=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()
        Write-Host $text
        return [pscustomobject]@{Exit=$p.ExitCode;Text=$text}
    }
    function Fire { Run-Fixture 'watchdog-apphost.ps1' @('-IntervalSeconds','0','-RestartScript',(Join-Path $scripts 'restart-fixture.ps1')) }
    $restartLock=Join-Path $logs 'apphost.restart.lock'
    $statePath=Join-Path $logs 'apphost-watchdog.state'
    if ($Case -eq 'Recovery') {
        ('999999 ' + [datetime]::UtcNow.ToString('o')) | Set-Content $restartLock
        $first=Fire
        Assert-C1527 (-not (Test-Path (Join-Path $fixture 'admitted'))) 'V-1 first fire preserves fresh lock'
        $second=Fire
        Assert-C1527 (Test-Path (Join-Path $fixture 'admitted')) 'V-1 second fire reaches shared restart admission'
    } elseif ($Case -eq 'Launcher') {
        $run=Run-Fixture 'restart-apphost.ps1' @('-TimeoutSec','0','-SkipGracefulStop')
        Assert-C1527 ($run.Exit -eq 1) 'V-2 failed launch remains failure'
        Assert-C1527 (-not (Test-Path $restartLock)) 'V-2 nonzero launcher timeout clears owned lock'
        Assert-C1527 ($run.Text -match 'launcher exited 17' -and $run.Text -match 'launcher.*\.out') 'V-2 prints launcher failure and captured output path'
    } elseif ($Case -eq 'Alert') {
        ($PID.ToString() + ' ' + [datetime]::UtcNow.ToString('o')) | Set-Content $restartLock
        @{lastOkUtc=$null;restartsUtc=@();downSinceUtc=[datetime]::UtcNow.AddMinutes(-4).ToString('o')} | ConvertTo-Json | Set-Content $statePath
        $null=Fire
        $sent=Join-Path $fixture 'sent.jsonl'
        Assert-C1527 (Test-Path $sent) 'V-3 outage alerts despite live restart lock'
        $null=Fire
        Assert-C1527 (@(Get-Content $sent).Count -eq 1) 'V-3 outage alert deduplicated across fires'
        '' | Set-Content (Join-Path $fixture 'up')
        $null=Fire
        Assert-C1527 (@(Get-Content $sent).Count -eq 2) 'V-3 one recovery notice'
    } else {
        throw 'guard matrix is added with implementation'
    }
    Write-Host "C1527 $Case assertions=$script:passed failed=0"
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
