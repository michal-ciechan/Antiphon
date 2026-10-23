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
    param($Uri,[switch]$UseBasicParsing,$TimeoutSec)
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
        Assert-C1527 ($first.Exit -eq 0) 'V-1 first fire completed'
        $second=Fire
        Assert-C1527 ($second.Exit -eq 0) 'V-1 second fire completed'
        Assert-C1527 (Test-Path (Join-Path $fixture 'admitted')) 'V-1 second fire reaches shared restart admission'
    } elseif ($Case -eq 'Launcher') {
        $run=Run-Fixture 'restart-apphost.ps1' @('-TimeoutSec','0','-SkipGracefulStop')
        Assert-C1527 ($run.Exit -eq 1) 'V-2 failed launch remains failure'
        Assert-C1527 (-not (Test-Path $restartLock)) 'V-2 nonzero launcher timeout clears owned lock'
        Assert-C1527 ($run.Text -match 'launcher exited 17' -and $run.Text -match 'launcher.*\.out') 'V-2 prints launcher failure and captured output path'
    } elseif ($Case -eq 'Alert') {
        ($PID.ToString() + ' ' + [datetime]::UtcNow.ToString('o')) | Set-Content $restartLock
        @{lastOkUtc=$null;restartsUtc=@();downSinceUtc=[datetime]::UtcNow.AddMinutes(-4).ToString('o')} | ConvertTo-Json | Set-Content $statePath
        $alertFire=Fire
        Assert-C1527 ($alertFire.Exit -eq 0) 'V-3 skipped-recovery fire completed'
        $sent=Join-Path $fixture 'sent.jsonl'
        Assert-C1527 (Test-Path $sent) 'V-3 outage alerts despite live restart lock'
        $null=Fire
        Assert-C1527 (@(Get-Content $sent).Count -eq 1) 'V-3 outage alert deduplicated across fires'
        '' | Set-Content (Join-Path $fixture 'up')
        $null=Fire
        Assert-C1527 (@(Get-Content $sent).Count -eq 2) 'V-3 one recovery notice'
        Remove-Item (Join-Path $fixture 'up')
        '' | Set-Content (Join-Path $fixture 'send-fails')
        $clock=[datetime]::UtcNow
        $clock.ToString('o') | Set-Content (Join-Path $fixture 'now.txt')
        $null=Fire
        $clock.AddSeconds(180).ToString('o') | Set-Content (Join-Path $fixture 'now.txt')
        $null=Fire
        $pending=Get-Content $statePath -Raw | ConvertFrom-Json
        Assert-C1527 ($pending.pendingAlerts.Count -eq 1) 'V-3 failed delivery persists intent'
        $pendingId=$pending.pendingAlerts[0].Id
        Remove-Item (Join-Path $fixture 'send-fails')
        $null=Fire
        $delivered=Get-Content $sent | Select-Object -Last 1 | ConvertFrom-Json
        Assert-C1527 ($delivered.Id -eq $pendingId -and @(Get-Content $sent).Count -eq 3) 'V-3 retry delivers same event once'
        '' | Set-Content (Join-Path $logs 'apphost.down-on-purpose')
        $clock.AddSeconds(600).ToString('o') | Set-Content (Join-Path $fixture 'now.txt')
        $null=Fire
        Assert-C1527 (@(Get-Content $sent).Count -eq 3) 'V-3 maintenance sends no alert or recovery'
    } else {
        . (Join-Path $PSScriptRoot 'apphost-common.ps1')
        $now=[datetime]::UtcNow
        $lock=[pscustomobject]@{Exists=$true;Readable=$true;ProcessId=999999;StampRaw=$now.ToString('o');AgeMinutes=0;HolderState='Dead'}
        function Decide($previous, $fire='one', $activity='Absent', $dead=$true, $launch=$false) {
            Resolve-AppHostDeadLaunch -Lock $lock -Activity $activity -HealthDead $dead -LaunchBlocked $launch -Previous $previous -FireId $fire -NowUtc $now
        }
        $one=Decide $null
        Assert-C1527 (-not $one.Reclaim -and $one.Proof.Count -eq 1) 'R-1 one observation cannot reclaim'
        $repeat=Decide $one.Proof
        Assert-C1527 (-not $repeat.Reclaim) 'R-1 same fire cannot count twice'
        $two=Decide $one.Proof 'two'
        Assert-C1527 $two.Reclaim 'R-1 two distinct fires reclaim'
        foreach ($activity in @('Present','Unknown','')) {
            $veto=Decide $two.Proof 'three' $activity
            Assert-C1527 (-not $veto.Reclaim -and $veto.Proof.Count -eq 0) "R-1 activity $activity resets evidence"
        }
        foreach ($holder in @('Alive','Unknown')) {
            $lock.HolderState=$holder
            Assert-C1527 (-not (Decide $two.Proof 'three').Reclaim) "R-1 holder $holder veto"
        }
        $lock.HolderState='Dead'
        Assert-C1527 (-not (Decide $two.Proof 'three' 'Absent' $false).Reclaim) 'R-1 healthy or timeout resets proof'
        Assert-C1527 (-not (Decide $two.Proof 'three' 'Absent' $true $true).Reclaim) 'R-1 launch lock veto'
        $lock.StampRaw=$now.AddSeconds(1).ToString('o')
        Assert-C1527 (-not (Decide $two.Proof 'three').Reclaim) 'R-1 replacement restarts at one observation'
        $lock.StampRaw=$now.ToString('o');$lock.AgeMinutes=-1
        Assert-C1527 (-not (Decide $two.Proof 'three').Reclaim) 'R-1 future stamp veto'
        $lock.AgeMinutes=0;$lock.Readable=$false
        Assert-C1527 (-not (Decide $two.Proof 'three').Reclaim) 'R-1 unreadable lock veto'
        $lock.Readable=$true
        $old=$two.Proof.PSObject.Copy();$old.ObservedUtc=$now.AddMinutes(-6).ToString('o')
        Assert-C1527 (-not (Decide $old 'three').Reclaim) 'R-1 stale evidence resets'
        foreach ($entry in @(@($false,0),@($true,0),@($true,23))) {
            $process=[pscustomobject]@{HasExited=$entry[0];ExitCode=$entry[1]}
            $process | Add-Member ScriptMethod Refresh {}
            $failure=Get-AppHostLauncherFailure $process
            Assert-C1527 (($null -ne $failure) -eq ($entry[0] -and $entry[1] -ne 0)) "R-1 launcher running or successful exit retains lock ($entry)"
        }
        Assert-C1527 ($null -eq (Get-AppHostLauncherFailure $null)) 'R-1 missing launcher evidence retains lock'
        ('999999 ' + $now.AddMinutes(-15).ToString('o')) | Set-Content $restartLock
        Assert-C1527 ($null -eq (Test-AppHostLockActive -Path $restartLock -NowUtc $now)) 'R-1 exact fifteen minute fallback'
        ('999999 ' + $now.ToString('o')) | Set-Content $restartLock
        @{deadLaunch=$two.Proof} | ConvertTo-Json -Depth 5 | Set-Content $statePath
        function Get-AppHostDeadLaunchObservation { param($SourceRoot,$Lock); [pscustomobject]@{Activity='Absent';HealthDead=$true;LaunchBlocked=$false} }
        $admitted=New-AppHostRestartLock -Path $restartLock -SourceRoot $fixture
        try {
            Assert-C1527 $admitted.Acquired 'R-1 shared admission acquires proven-dead generation'
            $competitor=New-AppHostRestartLock -Path $restartLock -SourceRoot $fixture
            Assert-C1527 (-not $competitor.Acquired) 'R-1 concurrent holder cannot steal admitted lock'
        } finally { Remove-AppHostLock $admitted }
        ('999999 ' + $now.AddSeconds(1).ToString('o')) | Set-Content $restartLock
        $refused=New-AppHostRestartLock -Path $restartLock -SourceRoot $fixture
        Assert-C1527 (-not $refused.Acquired) 'R-1 replacement before admission refused'
        Assert-C1527 ((Get-Content $restartLock -Raw).Trim() -eq ('999999 ' + $now.AddSeconds(1).ToString('o'))) 'R-1 replacement bytes preserved'
        $outage=[pscustomobject]@{}
        $outage=Update-AppHostOutage $outage $false $false $now
        $outage=Update-AppHostOutage $outage $false $false ($now.AddSeconds(179))
        Assert-C1527 ($outage.pendingAlerts.Count -eq 0) 'R-1 no alert before 180 seconds'
        $outage=Update-AppHostOutage $outage $false $false ($now.AddSeconds(180))
        Assert-C1527 ($outage.pendingAlerts.Count -eq 1) 'R-1 alert at exact 180 seconds'
        $id=$outage.pendingAlerts[0].Id
        $outage=$outage | ConvertTo-Json -Depth 6 | ConvertFrom-Json
        $outage=Update-AppHostOutage $outage $false $false ($now.AddSeconds(240))
        Assert-C1527 ($outage.pendingAlerts.Count -eq 1 -and $outage.pendingAlerts[0].Id -eq $id) 'R-1 pending retry identity survives persistence'
        $outage=Update-AppHostOutage $outage $true $false ($now.AddSeconds(250))
        Assert-C1527 ($outage.pendingAlerts.Count -eq 2 -and $outage.pendingAlerts[1].Kind -eq 'recovery') 'R-1 recovery queued behind undelivered outage'
        $outage=Update-AppHostOutage $outage $true $false ($now.AddSeconds(260))
        Assert-C1527 ($outage.pendingAlerts.Count -eq 2) 'R-1 recovery deduplicates'
        $outage=Update-AppHostOutage $outage $false $true ($now.AddSeconds(300))
        Assert-C1527 ($outage.pendingAlerts.Count -eq 0 -and $null -eq $outage.downSinceUtc) 'R-1 maintenance suppresses outage'
        $script:inventory=@();$script:listeners=@();$script:censusDenied=$false
        function Get-CimInstance { param($ClassName); if ($script:censusDenied) { throw 'denied' }; return $script:inventory }
        function Get-NetTCPConnection { param($State); return $script:listeners }
        function Get-ScheduledTask { return @() }
        $nativeLock=[pscustomobject]@{StampUtc=$now}
        Assert-C1527 ((Get-AppHostLaunchActivity $fixture $nativeLock) -eq 'Absent') 'R-1 complete empty census is absence'
        $script:censusDenied=$true
        Assert-C1527 ((Get-AppHostLaunchActivity $fixture $nativeLock) -eq 'Unknown') 'R-1 denied census is unknown'
        $script:censusDenied=$false
        $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes("Set-Location '$fixture/Antiphon.AppHost'; dotnet run"))
        $script:inventory=@([pscustomobject]@{ProcessId=999997;Name='pwsh.exe';CommandLine="pwsh -EncodedCommand $encoded";CreationDate=$now})
        Assert-C1527 ((Get-AppHostLaunchActivity $fixture $nativeLock) -eq 'Present') 'R-1 encoded detached wrapper veto'
        $script:inventory[0].CommandLine="pwsh -File $fixture/dev-aspire.ps1"
        Assert-C1527 ((Get-AppHostLaunchActivity $fixture $nativeLock) -eq 'Present') 'R-1 restore or build-slot launcher veto'
        $script:inventory[0].CommandLine='dotnet build /unrelated/checkout/project.csproj'
        Assert-C1527 ((Get-AppHostLaunchActivity $fixture $nativeLock) -eq 'Absent') 'R-1 unrelated build does not block'
        999997 | Set-Content (Join-Path $logs 'apphost.pid')
        $script:inventory[0].CreationDate=$now.AddMinutes(1)
        Assert-C1527 ((Get-AppHostLaunchActivity $fixture $nativeLock) -eq 'Absent') 'R-1 reused pid after lock ignored when unrelated'
        $script:inventory[0].CreationDate=$null
        Assert-C1527 ((Get-AppHostLaunchActivity $fixture $nativeLock) -eq 'Unknown') 'R-1 wrapper identity unknown veto'
        $script:inventory[0].CommandLine=$null
        Assert-C1527 ((Get-AppHostLaunchActivity $fixture $nativeLock) -eq 'Unknown') 'R-1 unreadable command line veto'
        Remove-Item (Join-Path $logs 'apphost.pid')
        $script:inventory=@();$script:listeners=@([pscustomobject]@{LocalPort=17202})
        Assert-C1527 ((Get-AppHostLaunchActivity $fixture $nativeLock) -eq 'Present') 'R-1 listener veto without health success'
        $script:listeners=@([pscustomobject]@{LocalPort=17204})
        Assert-C1527 ((Get-AppHostLaunchActivity $fixture $nativeLock) -eq 'Absent') 'R-1 runner port excluded'
        $script:listeners=@()
        $ownPath=Join-Path $logs 'owned.lock'
        $owned=New-AppHostLock -Path $ownPath
        $owned.Stream.Dispose();$owned.Stream=$null
        '999999 replacement' | Set-Content $ownPath
        Remove-AppHostLock $owned
        Assert-C1527 ((Get-Content $ownPath -Raw).Trim() -eq '999999 replacement') 'R-1 cleanup preserves replacement lock'
        foreach ($name in @('apphost-common.ps1','watchdog-apphost.ps1','restart-apphost.ps1')) {
            $path=Join-Path $PSScriptRoot $name
            $bytes=[IO.File]::ReadAllBytes($path)
            Assert-C1527 (@($bytes | Where-Object { $_ -gt 127 }).Count -eq 0) "R-1 ASCII $name"
            $tokens=$null;$errors=$null
            $null=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
            Assert-C1527 ($errors.Count -eq 0) "R-1 syntax $name"
        }
    }
    Write-Host "C1527 $Case assertions=$script:passed failed=0"
} finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
