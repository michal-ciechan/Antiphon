#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0495: restart-apphost.ps1 proves the loaded server SHA.

    Isolated git fixture plus inert seams. Never touches live 172xx ports,
    processes, scheduled tasks, or logs/ under the checkout.
    ASCII-only for pwsh 7 and Windows PowerShell 5.1.
#>
param([string]$Case = '')

$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$repo = Split-Path -Parent $here

$script:passed = 0
$script:failed = 0
$script:failures = @()

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures += "$Name : $Detail"
    Write-Host "FAIL $Name - $Detail"
}

function Assert-True {
    param([bool]$Cond, [string]$Name, [string]$Detail = '')
    if ($Cond) { Write-Pass $Name }
    else { Write-Fail $Name $Detail }
}

function New-C495Repo {
    param([string]$Path, [switch]$Commit)
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & git -C $Path init -q | Out-Null
    & git -C $Path config user.email c495@example.test
    & git -C $Path config user.name c495
    & git -C $Path config commit.gpgsign false
    'seed' | Set-Content -LiteralPath (Join-Path $Path 'README.md') -Encoding ASCII
    if ($Commit) {
        & git -C $Path add README.md | Out-Null
        & git -C $Path commit -qm 'c495 seed' | Out-Null
    }
}

function Install-C495Scripts {
    param([string]$Root)
    $scripts = Join-Path $Root 'scripts'
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $here 'restart-apphost.ps1') -Destination (Join-Path $scripts 'restart-apphost.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $here 'apphost-common.ps1') -Destination (Join-Path $scripts 'apphost-common.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $here 'deploy-local.ps1') -Destination (Join-Path $scripts 'deploy-local.ps1') -Force
    @(
        '# inert CARD-0495'
        'Write-Host "check-daemon-build inert"'
    ) | Set-Content -LiteralPath (Join-Path $scripts 'check-daemon-build.ps1') -Encoding ASCII
    @(
        '# inert CARD-0495'
        '$d = Join-Path $PSScriptRoot ''logs'''
        'New-Item -ItemType Directory -Force $d | Out-Null'
        'Set-Content -LiteralPath (Join-Path $d ''apphost-dashboard-url.txt'') ''http://127.0.0.1:9/'' -Encoding ASCII'
    ) | Set-Content -LiteralPath (Join-Path $Root 'dev-aspire.ps1') -Encoding ASCII
    @(
        '# inert CARD-0495 verify-dev-stack'
        'Set-Content -LiteralPath (Join-Path $PSScriptRoot ''verify-invoked.txt'') ''invoked'' -Encoding ASCII'
        'exit 0'
    ) | Set-Content -LiteralPath (Join-Path $Root 'verify-dev-stack.ps1') -Encoding ASCII
}

function Write-C495Seams {
    param([string]$Root, $Config)
    $trace = Join-Path $Root 'trace.jsonl'
    if (Test-Path -LiteralPath $trace) { Remove-Item -LiteralPath $trace -Force }
    $cfgPath = Join-Path $Root 'seams.json'
    ($Config | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $cfgPath -Encoding ASCII
    $seams = @(
        '$script:C495Root = Split-Path $PSCommandPath -Parent'
        '$script:C495Trace = Join-Path $script:C495Root ''trace.jsonl'''
        '$script:C495Cfg = Get-Content -LiteralPath (Join-Path $script:C495Root ''seams.json'') -Raw | ConvertFrom-Json'
        '$script:C495OriginalHead = $null'
        'try { $script:C495OriginalHead = ((& git -C $script:C495Root rev-parse HEAD 2>$null) | Select-Object -First 1).ToString().Trim() } catch { }'
        '$script:C495HeadCalls = 0'
        'function Write-C495Trace([string]$Kind, $Extra) {'
        '  $row = @{ kind = $Kind; at = [datetime]::UtcNow.ToString(''o'') }'
        '  if ($Extra) { foreach ($k in $Extra.Keys) { $row[$k] = $Extra[$k] } }'
        '  Add-Content -LiteralPath $script:C495Trace -Value ($row | ConvertTo-Json -Compress) -Encoding ASCII'
        '}'
        'function Get-C495Seq($list, [int]$index) {'
        '  $arr = @($list)'
        '  if ($arr.Count -eq 0) { return $null }'
        '  if ($index -lt $arr.Count) { return $arr[$index] }'
        '  return $arr[$arr.Count - 1]'
        '}'
        'function Get-AppHostPortOwners { param([int]$Port)'
        '  Write-C495Trace ''port'' @{ port = $Port }'
        '  $ownerPid = $null'
        '  if ($Port -eq 17204 -and $script:C495Cfg.srPid) { $ownerPid = [int]$script:C495Cfg.srPid }'
        '  elseif ($script:C495Cfg.portPid) { $ownerPid = [int]$script:C495Cfg.portPid }'
        '  if ($ownerPid) { return @($ownerPid) }'
        '  return @()'
        '}'
        'function Stop-AppHostProcessId { param([int]$ProcessId)'
        '  Write-C495Trace ''stop-id'' @{ pid = $ProcessId }'
        '}'
        'function Stop-AppHostProcessTree { param([int]$ProcessId)'
        '  Write-C495Trace ''stop-tree'' @{ pid = $ProcessId }'
        '}'
        'function Get-AppHostStrayProcesses {'
        '  Write-C495Trace ''stray'' @{}'
        '  return @()'
        '}'
        'function Start-AppHostDevLaunch { param([string[]]$ArgumentList)'
        '  Write-C495Trace ''launch'' @{ args = ($ArgumentList -join '' '') }'
        '  $logs = Join-Path $script:C495Root ''logs'''
        '  New-Item -ItemType Directory -Force $logs | Out-Null'
        '  if ($script:C495Cfg.logKind -eq ''BuildFailed'') {'
        '    Set-Content -LiteralPath (Join-Path $logs ''apphost.log'') ''The build failed.'' -Encoding ASCII'
        '  } elseif ($script:C495Cfg.logKind -eq ''Dcp'') {'
        '    Set-Content -LiteralPath (Join-Path $logs ''apphost.log'') ''dependency check returned an error'' -Encoding ASCII'
        '  }'
        '  if ($script:C495Cfg.dashboard -ne $false) {'
        '    Set-Content -LiteralPath (Join-Path $logs ''apphost-dashboard-url.txt'') ''http://127.0.0.1:9/'' -Encoding ASCII'
        '  }'
        '  return [pscustomobject]@{ Id = 99999; HasExited = $false }'
        '}'
        'function Stop-AppHostLaunchChild { param($Process)'
        '  Write-C495Trace ''child-stop'' @{}'
        '}'
        'function Invoke-AppHostHealthProbe { param([int]$TimeoutSec = 5)'
        '  Write-C495Trace ''health'' @{}'
        '  $n = @(Get-Content -LiteralPath $script:C495Trace | Where-Object { $_ -match ''"health"'' }).Count'
        '  $item = Get-C495Seq $script:C495Cfg.health ($n - 1)'
        '  $code = 200'
        '  if ($null -ne $item) { $code = [int]$item }'
        '  return [pscustomobject]@{ StatusCode = $code; Body = ''Healthy''; Error = $null }'
        '}'
        'function Invoke-AppHostVersionProbe { param([int]$TimeoutSec = 5)'
        '  Write-C495Trace ''version'' @{}'
        '  $n = @(Get-Content -LiteralPath $script:C495Trace | Where-Object { $_ -match ''"version"'' }).Count'
        '  $item = Get-C495Seq $script:C495Cfg.version ($n - 1)'
        '  if ($null -eq $item) {'
        '    $sha = $script:C495OriginalHead'
        '    $body = (''{{"version":"{0}","informationalVersion":"{0}","capabilities":["land-v2"]}}'' -f $sha)'
        '    return [pscustomobject]@{ StatusCode = 200; Body = $body; Error = $null }'
        '  }'
        '  $status = 200'
        '  if ($item.status) { $status = [int]$item.status }'
        '  if ($item.body) { return [pscustomobject]@{ StatusCode = $status; Body = [string]$item.body; Error = $null } }'
        '  $sha = $null'
        '  if ($item.sha -eq ''HEAD'') { $sha = $script:C495OriginalHead }'
        '  elseif ($item.sha -eq ''LIVE'') { try { $sha = ((& git -C $script:C495Root rev-parse HEAD) | Select-Object -First 1).ToString().Trim() } catch { $sha = $script:C495OriginalHead } }'
        '  elseif ($item.sha) { $sha = [string]$item.sha }'
        '  if ($status -ne 200) { return [pscustomobject]@{ StatusCode = $status; Body = $null; Error = ''http'' } }'
        '  $body = (''{{"version":"{0}","informationalVersion":"{0}","capabilities":["land-v2"]}}'' -f $sha)'
        '  return [pscustomobject]@{ StatusCode = 200; Body = $body; Error = $null }'
        '}'
        'function Wait-AppHostPollInterval { param([int]$Seconds = 3)'
        '  Write-C495Trace ''wait'' @{ seconds = $Seconds }'
        '  $w = 0'
        '  if ($null -ne $script:C495Cfg.waitSeconds) { $w = [double]$script:C495Cfg.waitSeconds }'
        '  if ($w -gt 0) { Start-Sleep -Milliseconds ([int]($w * 1000)) }'
        '}'
        'function Get-AppHostSourceHead { param([string]$SourceRoot)'
        '  $script:C495HeadCalls++'
        '  Write-C495Trace ''head'' @{ n = $script:C495HeadCalls }'
        '  if ($script:C495Cfg.commitOnHeadCall -and ($script:C495HeadCalls -eq [int]$script:C495Cfg.commitOnHeadCall)) {'
        '    Add-Content -LiteralPath (Join-Path $script:C495Root ''README.md'') ''moved'' -Encoding ASCII'
        '    & git -C $script:C495Root add README.md | Out-Null'
        '    & git -C $script:C495Root commit -qm ''c495 moved head'' | Out-Null'
        '  }'
        '  try {'
        '    $text = ((& git -C $SourceRoot rev-parse HEAD) | Select-Object -First 1).ToString().Trim()'
        '    if ($text -match ''^[0-9a-fA-F]{40}$'') { return $text.ToLowerInvariant() }'
        '    return $null'
        '  } catch { return $null }'
        '}'
        'function Show-DcpTimeoutVerdict { param($LogPath,$Evidence,[bool]$PodmanNoise=$false,$LaunchLock,$RestartLock,$WatchdogLog,[int]$DockerTimeoutSec=10)'
        '  Write-C495Trace ''dcp'' @{}'
        '  Write-Host ''DCP timeout (seamed)'''
        '}'
        'function Get-DockerVerdict { param([int]$TimeoutSec=10)'
        '  return [pscustomobject]@{ Healthy = $true; Summary = ''seamed''; Detail = @() }'
        '}'
    )
    $seamsPath = Join-Path $Root 'seams.ps1'
    $seams | Set-Content -LiteralPath $seamsPath -Encoding ASCII
    return $seamsPath
}

function Get-C495Trace {
    param([string]$Root)
    $path = Join-Path $Root 'trace.jsonl'
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    return @(Get-Content -LiteralPath $path -ErrorAction SilentlyContinue)
}

function Count-C495Kind {
    param($Trace, [string]$Kind)
    return @($Trace | Where-Object { $_ -match ('"kind":"{0}"' -f $Kind) }).Count
}

function Invoke-C495Restart {
    param(
        [string]$Root,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory,
        [switch]$NoSeams
    )
    $scriptPath = Join-Path $Root 'scripts\restart-apphost.ps1'
    $prevSeams = $env:ANTIPHON_APPHOST_TEST_SEAMS
    if ($NoSeams) { Remove-Item Env:ANTIPHON_APPHOST_TEST_SEAMS -ErrorAction SilentlyContinue }
    else { $env:ANTIPHON_APPHOST_TEST_SEAMS = Join-Path $Root 'seams.ps1' }
    $prev = Get-Location
    try {
        if ($WorkingDirectory) { Set-Location $WorkingDirectory }
        else { Set-Location $Root }
        $output = @(& pwsh -NoProfile -File $scriptPath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Text     = ($output -join "`n")
            Trace    = Get-C495Trace $Root
        }
    } finally {
        Set-Location $prev
        if ($null -eq $prevSeams) { Remove-Item Env:ANTIPHON_APPHOST_TEST_SEAMS -ErrorAction SilentlyContinue }
        else { $env:ANTIPHON_APPHOST_TEST_SEAMS = $prevSeams }
    }
}

function New-C495Fixture {
    param($Config, [switch]$NoCommit)
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('c495-sv-' + [guid]::NewGuid().ToString('N'))
    if ($NoCommit) { New-C495Repo -Path $root }
    else { New-C495Repo -Path $root -Commit }
    Install-C495Scripts -Root $root
    if (-not $NoCommit) {
        & git -C $root add scripts README.md dev-aspire.ps1 verify-dev-stack.ps1 | Out-Null
        & git -C $root commit -qm 'c495 scripts' | Out-Null
    }
    Write-C495Seams -Root $root -Config $Config | Out-Null
    $head = $null
    try { $head = ((& git -C $root rev-parse HEAD 2>$null) | Select-Object -First 1).ToString().Trim() } catch { }
    return [pscustomobject]@{ Root = $root; Head = $head }
}

function Remove-C495Fixture {
    param($Fixture)
    if ($Fixture -and $Fixture.Root -and (Test-Path -LiteralPath $Fixture.Root)) {
        Remove-Item -LiteralPath $Fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-C495LockPath { param($Fixture) Join-Path $Fixture.Root 'logs\apphost.restart.lock' }

$script:OtherSha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'

function Test-T1 {
    $fx = New-C495Fixture -Config @{ dashboard = $true; waitSeconds = 0; health = @(200) }
    $other = Join-Path ([System.IO.Path]::GetTempPath()) ('c495-cwd-' + [guid]::NewGuid().ToString('N'))
    try {
        New-C495Repo -Path $other -Commit
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8') -WorkingDirectory $other
        Assert-True ($r.ExitCode -eq 0) 'T1 exit 0 from foreign cwd' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True ($r.Text -match [regex]::Escape($fx.Head)) 'T1 prints script-root HEAD' $r.Text
        Assert-True (-not (Test-Path -LiteralPath (Get-C495LockPath $fx))) 'T1 lock absent after success'
    } finally {
        Remove-C495Fixture $fx
        if (Test-Path $other) { Remove-Item $other -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function Test-T2 {
    $fx = New-C495Fixture -Config @{ dashboard = $true; waitSeconds = 0; health = @(200) }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8', '-ExpectedServerSha', $fx.Head)
        Assert-True ($r.ExitCode -eq 0) 'T2 explicit HEAD exit 0' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True ($r.Text -match [regex]::Escape($fx.Head)) 'T2 prints verified SHA' $r.Text
        Assert-True (-not (Test-Path -LiteralPath (Get-C495LockPath $fx))) 'T2 lock absent'
    } finally { Remove-C495Fixture $fx }
}

function Test-T3 {
    $values = @('abc', ('a' * 39), ('a' * 41), 'deadbeefG000000000000000000000000000000', ('b' * 64))
    foreach ($v in $values) {
        $fx = New-C495Fixture -Config @{ dashboard = $true; waitSeconds = 0 }
        try {
            $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8', '-ExpectedServerSha', $v)
            $trace = $r.Trace
            Assert-True ($r.ExitCode -eq 3) ('T3 exit 3 for {0}' -f $v.Substring(0, [math]::Min(12, $v.Length))) ("exit=$($r.ExitCode); $($r.Text)")
            Assert-True ((Count-C495Kind $trace 'launch') -eq 0) ('T3 launch 0 for {0}' -f $v)
            Assert-True ((Count-C495Kind $trace 'port') -eq 0) ('T3 teardown 0 for {0}' -f $v)
            Assert-True ((Count-C495Kind $trace 'stop-id') -eq 0) ('T3 stop-id 0 for {0}' -f $v)
            Assert-True (-not (Test-Path -LiteralPath (Get-C495LockPath $fx))) ('T3 lock absent for {0}' -f $v)
            Assert-True ($r.Text -match 'REFUSED') ('T3 REFUSED for {0}' -f $v) $r.Text
            Assert-True ($r.Text -match [regex]::Escape($v)) ('T3 names supplied value {0}' -f $v) $r.Text
            Assert-True ($r.Text -match 'canonical checkout') ('T3 names canonical checkout for {0}' -f $v) $r.Text
        } finally { Remove-C495Fixture $fx }
    }
}

function Test-T4 {
    $fx = New-C495Fixture -Config @{ dashboard = $true; waitSeconds = 0 }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8', '-ExpectedServerSha', $script:OtherSha)
        Assert-True ($r.ExitCode -eq 3) 'T4 exit 3 for different SHA' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True ((Count-C495Kind $r.Trace 'port') -eq 0) 'T4 teardown 0'
        Assert-True ((Count-C495Kind $r.Trace 'launch') -eq 0) 'T4 launch 0'
        Assert-True (-not (Test-Path -LiteralPath (Get-C495LockPath $fx))) 'T4 lock absent'
        Assert-True ($r.Text -match 'REFUSED') 'T4 REFUSED' $r.Text
        Assert-True ($r.Text -match [regex]::Escape($script:OtherSha)) 'T4 names supplied SHA' $r.Text
        Assert-True ($r.Text -match [regex]::Escape($fx.Head)) 'T4 names HEAD' $r.Text
        Assert-True ($r.Text -match [regex]::Escape($fx.Root)) 'T4 names source root' $r.Text
        Assert-True ($r.Text -match 'canonical checkout') 'T4 update checkout' $r.Text
    } finally { Remove-C495Fixture $fx }
}

function Test-T5 {
    $fx = New-C495Fixture -Config @{ dashboard = $true; waitSeconds = 0 } -NoCommit
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8')
        Assert-True ($r.ExitCode -eq 3) 'T5 unreadable HEAD exit 3' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True ((Count-C495Kind $r.Trace 'port') -eq 0) 'T5 teardown 0'
        Assert-True ((Count-C495Kind $r.Trace 'launch') -eq 0) 'T5 launch 0'
        Assert-True (-not (Test-Path -LiteralPath (Get-C495LockPath $fx))) 'T5 lock absent'
        Assert-True ($r.Text -match 'REFUSED') 'T5 REFUSED' $r.Text
        Assert-True ($r.Text -match 'canonical checkout') 'T5 update checkout' $r.Text
    } finally { Remove-C495Fixture $fx }
}

function Test-T6 {
    $fx = New-C495Fixture -Config @{
        dashboard = $true; waitSeconds = 0; health = @(200)
        version = @(@{ status = 200; sha = $script:OtherSha })
    }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '2')
        Assert-True ($r.ExitCode -eq 5) 'T6 mismatch exit 5' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True ($r.Text -notmatch 'backend healthy') 'T6 no backend healthy' $r.Text
        Assert-True ($r.Text -match [regex]::Escape($fx.Head)) 'T6 names expected SHA' $r.Text
        Assert-True ($r.Text -match [regex]::Escape($script:OtherSha)) 'T6 names observed SHA' $r.Text
        Assert-True ($r.Text -match [regex]::Escape($fx.Root)) 'T6 names source root' $r.Text
        Assert-True ($r.Text -match 'apphost.restart.lock') 'T6 names lock' $r.Text
        Assert-True ($r.Text -match 'inspect') 'T6 log inspection' $r.Text
        Assert-True (Test-Path -LiteralPath (Get-C495LockPath $fx)) 'T6 lock retained'
        Assert-True ((Count-C495Kind $r.Trace 'child-stop') -eq 0) 'T6 child stop 0'
        Assert-True ((Count-C495Kind $r.Trace 'launch') -eq 1) 'T6 launch 1'
        Assert-True ((Count-C495Kind $r.Trace 'port') -ge 1) 'T6 teardown ran'
    } finally { Remove-C495Fixture $fx }
}

function Test-T7 {
    $fx = New-C495Fixture -Config @{
        dashboard = $true; waitSeconds = 0; health = @(200)
        version = @(@{ status = 404 })
    }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '2')
        Assert-True ($r.ExitCode -eq 5) 'T7 version 404 exit 5' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True ($r.Text -match 'unavailable') 'T7 reason unavailable' $r.Text
        Assert-True (Test-Path -LiteralPath (Get-C495LockPath $fx)) 'T7 lock retained'
        Assert-True ((Count-C495Kind $r.Trace 'child-stop') -eq 0) 'T7 child stop 0'
    } finally { Remove-C495Fixture $fx }
}

function Test-T8 {
    $bodies = @(
        @{ name = 'unknown'; body = '{"version":"unknown","informationalVersion":"unknown"}' },
        @{ name = 'non-json'; body = 'not-json' },
        @{ name = 'short'; body = '{"version":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","informationalVersion":"x"}' }
    )
    foreach ($row in $bodies) {
        $fx = New-C495Fixture -Config @{
            dashboard = $true; waitSeconds = 0; health = @(200)
            version = @(@{ status = 200; body = $row.body })
        }
        try {
            $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '2')
            Assert-True ($r.ExitCode -eq 5) ('T8 {0} exit 5' -f $row.name) ("exit=$($r.ExitCode); $($r.Text)")
            Assert-True ($r.Text -match 'malformed') ('T8 {0} malformed' -f $row.name) $r.Text
        } finally { Remove-C495Fixture $fx }
    }
}

function Test-T9 {
    $fx = New-C495Fixture -Config @{
        dashboard = $true; waitSeconds = 0; health = @(200)
        version = @(
            @{ status = 404 },
            @{ status = 404 },
            @{ status = 200; sha = $script:OtherSha },
            @{ status = 200; sha = 'HEAD' }
        )
    }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8')
        Assert-True ($r.ExitCode -eq 0) 'T9 delayed match exit 0' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True (-not (Test-Path -LiteralPath (Get-C495LockPath $fx))) 'T9 lock absent'
        Assert-True ((Count-C495Kind $r.Trace 'version') -ge 4) 'T9 version probes >= 4'
        Assert-True ($r.Text -match [regex]::Escape($fx.Head)) 'T9 prints verified SHA' $r.Text
    } finally { Remove-C495Fixture $fx }
}

function Test-T9b {
    $fx = New-C495Fixture -Config @{
        dashboard = $true; waitSeconds = 0.7; health = @(200)
        version = @(
            @{ status = 404 }, @{ status = 404 }, @{ status = 404 }, @{ status = 200; sha = 'HEAD' }
        )
    }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '2')
        Assert-True ($r.ExitCode -eq 5) 'T9b match after deadline exit 5' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True (Test-Path -LiteralPath (Get-C495LockPath $fx)) 'T9b lock retained'
    } finally { Remove-C495Fixture $fx }
}

function Test-T10a {
    $fx = New-C495Fixture -Config @{
        dashboard = $true; waitSeconds = 0; health = @(200)
        commitOnHeadCall = 2
        version = @(@{ status = 404 }, @{ status = 200; sha = 'HEAD' })
    }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '2')
        Assert-True ($r.ExitCode -eq 5) 'T10a moved HEAD A still served exit 5' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True ($r.Text -match 'checkout moved') 'T10a checkout moved' $r.Text
        Assert-True (Test-Path -LiteralPath (Get-C495LockPath $fx)) 'T10a lock retained'
    } finally { Remove-C495Fixture $fx }
}

function Test-T10b {
    $fx = New-C495Fixture -Config @{
        dashboard = $true; waitSeconds = 0; health = @(200)
        commitOnHeadCall = 2
        version = @(@{ status = 404 }, @{ status = 200; sha = 'LIVE' })
    }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '2')
        Assert-True ($r.ExitCode -eq 5) 'T10b moved HEAD B served exit 5' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True ($r.Text -match 'checkout moved') 'T10b checkout moved' $r.Text
        Assert-True (Test-Path -LiteralPath (Get-C495LockPath $fx)) 'T10b lock retained'
    } finally { Remove-C495Fixture $fx }
}

function Test-T11 {
    $fx = New-C495Fixture -Config @{ dashboard = $false; waitSeconds = 0 }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '1')
        Assert-True ($r.ExitCode -eq 1) 'T11 no dashboard exit 1' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True (Test-Path -LiteralPath (Get-C495LockPath $fx)) 'T11 lock kept'
        Assert-True ((Count-C495Kind $r.Trace 'version') -eq 0) 'T11 version probe 0'
    } finally { Remove-C495Fixture $fx }
}

function Test-T12 {
    $fx = New-C495Fixture -Config @{
        dashboard = $true; waitSeconds = 0; health = @(200)
        version = @(@{ status = 200; sha = $script:OtherSha })
    }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '2', '-NoBuild')
        Assert-True ($r.ExitCode -eq 5) 'T12 NoBuild mismatch exit 5' ("exit=$($r.ExitCode); $($r.Text)")
        $launch = @($r.Trace | Where-Object { $_ -match '"launch"' } | Select-Object -First 1)
        Assert-True ($launch.Count -eq 1 -and $launch[0] -match '-NoBuild') 'T12 launch args contain -NoBuild' ($launch -join '`n')
        Assert-True ((Count-C495Kind $r.Trace 'child-stop') -eq 0) 'T12 child stop 0'
    } finally { Remove-C495Fixture $fx }
}

function Test-T13 {
    $fx = New-C495Fixture -Config @{ dashboard = $true; waitSeconds = 0; logKind = 'BuildFailed' }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8')
        Assert-True ($r.ExitCode -eq 1) 'T13 BuildFailed exit 1' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True (-not (Test-Path -LiteralPath (Get-C495LockPath $fx))) 'T13 lock removed'
        Assert-True ((Count-C495Kind $r.Trace 'child-stop') -eq 1) 'T13 child stop 1'
        Assert-True ((Count-C495Kind $r.Trace 'version') -eq 0) 'T13 version probe 0'
    } finally { Remove-C495Fixture $fx }
}

function Test-T14 {
    $fx = New-C495Fixture -Config @{ dashboard = $true; waitSeconds = 0; logKind = 'Dcp' }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8')
        Assert-True ($r.ExitCode -eq 4) 'T14 DCP exit 4' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True (Test-Path -LiteralPath (Get-C495LockPath $fx)) 'T14 lock retained'
    } finally { Remove-C495Fixture $fx }
}

function Test-T15 {
    . (Join-Path $here 'apphost-common.ps1')
    Assert-True ((Format-AppHostRestartExitName 5) -eq '5=server build unverified') 'T15 exit 5 named'
}

function Test-T16 {
    $fx = New-C495Fixture -Config @{
        dashboard = $true; waitSeconds = 0; health = @(200)
        version = @(@{ status = 200; sha = $script:OtherSha })
    }
    try {
        $env:ANTIPHON_APPHOST_TEST_SEAMS = Join-Path $fx.Root 'seams.ps1'
        $deploy = Join-Path $fx.Root 'scripts\deploy-local.ps1'
        $output = @(& pwsh -NoProfile -File $deploy -TimeoutSec 2 2>&1 | ForEach-Object { $_.ToString() })
        $code = $LASTEXITCODE
        $text = $output -join "`n"
        $last = @($output | Where-Object { $_ -match 'DEPLOY VERDICT:' } | Select-Object -Last 1)
        Assert-True ($code -eq 1) 'T16 deploy exit 1' ("exit=$code; $text")
        Assert-True ($last -eq 'DEPLOY VERDICT: failed restart-apphost.ps1 exited 5') 'T16 last line failed exit 5' ($last -join ';')
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $fx.Root 'verify-invoked.txt'))) 'T16 verify-dev-stack not invoked'
    } finally {
        Remove-Item Env:ANTIPHON_APPHOST_TEST_SEAMS -ErrorAction SilentlyContinue
        Remove-C495Fixture $fx
    }
}

function Test-T18 {
    . (Join-Path $here 'apphost-common.ps1')
    $def = (Get-Command Invoke-AppHostVersionProbe).Definition
    Assert-True ($def -match '/api/version') 'T18 default version probe names /api/version' $def
    $fx = New-C495Fixture -Config @{ dashboard = $true; waitSeconds = 0 } 
    $linked = Join-Path ([System.IO.Path]::GetTempPath()) ('c495-linked-' + [guid]::NewGuid().ToString('N'))
    $added = $false
    try {
        & git -C $fx.Root worktree add --detach $linked $fx.Head 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { $added = $true }
        $prev = $env:ANTIPHON_APPHOST_TEST_SEAMS
        Remove-Item Env:ANTIPHON_APPHOST_TEST_SEAMS -ErrorAction SilentlyContinue
        $output = @(& pwsh -NoProfile -File (Join-Path $linked 'scripts\restart-apphost.ps1') -NoBuild -TimeoutSec 1 2>&1 | ForEach-Object { $_.ToString() })
        $text = $output -join "`n"
        Assert-True ($LASTEXITCODE -eq 3) 'T18 linked worktree exit 3' $text
        Assert-True ($text -notmatch 'TEST SEAMS ACTIVE') 'T18 no TEST SEAMS ACTIVE' $text
        if ($null -ne $prev) { $env:ANTIPHON_APPHOST_TEST_SEAMS = $prev }
    } finally {
        if ($added) { & git -C $fx.Root worktree remove --force $linked 2>$null | Out-Null }
        Remove-C495Fixture $fx
    }
}

function Test-T19 {
    $files = @(
        (Join-Path $here 'restart-apphost.ps1'),
        (Join-Path $here 'apphost-common.ps1'),
        (Join-Path $here 'deploy-local.ps1'),
        (Join-Path $here 'delegate.ps1'),
        (Join-Path $here 'test-apphost-server-version.ps1')
    )
    foreach ($f in $files) {
        $bytes = [System.IO.File]::ReadAllBytes($f)
        $high = @($bytes | Where-Object { $_ -gt 0x7F }).Count
        Assert-True ($high -eq 0) ('T19 ASCII {0}' -f (Split-Path $f -Leaf)) ("high=$high")
        $errs = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$errs)
        $n = 0
        if ($null -ne $errs) { $n = @($errs).Count }
        Assert-True ($n -eq 0) ('T19 pwsh parse {0}' -f (Split-Path $f -Leaf)) ("errors=$n")
    }
    $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (Test-Path -LiteralPath $ps51) {
        foreach ($f in $files) {
            $cmd = '& { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile(''' + $f.Replace('''', '''''') + ''', [ref]$null, [ref]$e); if ($null -eq $e) { 0 } else { @($e).Count } }'
            $out = & $ps51 -NoProfile -NonInteractive -Command $cmd
            Assert-True ([int]$out -eq 0) ('T19 5.1 parse {0}' -f (Split-Path $f -Leaf)) ("errors=$out")
        }
    } else {
        Write-Pass 'T19 5.1 parse skipped (powershell.exe absent)'
    }
}

function Test-T20 {
    $fx = New-C495Fixture -Config @{ dashboard = $true; waitSeconds = 0; health = @(200) }
    try {
        Add-Content -LiteralPath (Join-Path $fx.Root 'README.md') 'dirty' -Encoding ASCII
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8')
        Assert-True ($r.ExitCode -eq 0) 'T20 dirty tracked exit 0' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True ($r.Text -match 'tracked edits') 'T20 prints dirty-tree limitation' $r.Text
    } finally { Remove-C495Fixture $fx }
}

function Test-T21 {
    $fx = New-C495Fixture -Config @{
        dashboard = $true; waitSeconds = 0; health = @(200)
        srPid = 4242; portPid = 4242
    }
    try {
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8')
        Assert-True ($r.ExitCode -eq 0) 'T21 exit 0' ("exit=$($r.ExitCode); $($r.Text)")
        $stops = @($r.Trace | Where-Object { $_ -match '"stop-id"' })
        $hit = @($stops | Where-Object { $_ -match '"pid":\s*4242' -or $_ -match '"pid":4242' }).Count
        Assert-True ($hit -eq 0) 'T21 session-runner PID never stopped' ($stops -join ';')
    } finally { Remove-C495Fixture $fx }
}

function Test-T22 {
    $fx = New-C495Fixture -Config @{ dashboard = $true; waitSeconds = 0 }
    try {
        $lock = Get-C495LockPath $fx
        New-Item -ItemType Directory -Force (Split-Path $lock) | Out-Null
        $stamp = '{0} {1}' -f 12345, [datetime]::UtcNow.ToString('o')
        Set-Content -LiteralPath $lock -Value $stamp -Encoding ASCII
        $before = Get-Content -LiteralPath $lock -Raw
        $r = Invoke-C495Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8')
        $after = Get-Content -LiteralPath $lock -Raw
        Assert-True ($r.ExitCode -eq 3) 'T22 fresh lock exit 3' ("exit=$($r.ExitCode); $($r.Text)")
        Assert-True ((Count-C495Kind $r.Trace 'port') -eq 0) 'T22 teardown 0'
        Assert-True ($after -eq $before) 'T22 lock file untouched'
    } finally { Remove-C495Fixture $fx }
}

$all = @(
    'T1','T2','T3','T4','T5','T6','T7','T8','T9','T9b','T10a','T10b',
    'T11','T12','T13','T14','T15','T16','T18','T19','T20','T21','T22'
)
if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($id in $all) { & ('Test-{0}' -f $id) }
}

Write-Host ''
Write-Host ('CARD-0495 server-version: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ('  ' + $line) }
    Write-Host 'APPHOST SERVER VERSION TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'APPHOST SERVER VERSION TESTS EXIT CODE: 0  (PASS)'
exit 0
