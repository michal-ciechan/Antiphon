#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0716: restart-apphost.ps1 asks the server to stop before the force-kill.

    Isolated git fixture plus inert seams. Never touches live 172xx ports,
    processes, scheduled tasks, or logs/ under the checkout.
    ASCII-only for pwsh 7 and Windows PowerShell 5.1.
#>
param([string]$Case = '')

$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$script:passed = 0
$script:failed = 0
$script:failures = @()
$script:Sentinel = 'C716-SENTINEL-TOKEN-9f3a'

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

function New-C716Repo {
    param([string]$Path)
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & git -C $Path init -q | Out-Null
    & git -C $Path config user.email c716@example.test
    & git -C $Path config user.name c716
    & git -C $Path config commit.gpgsign false
    'seed' | Set-Content -LiteralPath (Join-Path $Path 'README.md') -Encoding ASCII
    & git -C $Path add README.md | Out-Null
    & git -C $Path commit -qm 'c716 seed' | Out-Null
}

function Install-C716Scripts {
    param([string]$Root)
    $scripts = Join-Path $Root 'scripts'
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $here 'restart-apphost.ps1') -Destination (Join-Path $scripts 'restart-apphost.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $here 'apphost-common.ps1') -Destination (Join-Path $scripts 'apphost-common.ps1') -Force
    @(
        '# inert CARD-0716'
        'Write-Host "check-daemon-build inert"'
    ) | Set-Content -LiteralPath (Join-Path $scripts 'check-daemon-build.ps1') -Encoding ASCII
    @(
        '# inert CARD-0716'
        'exit 0'
    ) | Set-Content -LiteralPath (Join-Path $Root 'dev-aspire.ps1') -Encoding ASCII
    $logs = Join-Path $Root 'logs'
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $logs 'apphost.pid') -Value '111' -Encoding ASCII
    & git -C $Root add scripts README.md dev-aspire.ps1 | Out-Null
    & git -C $Root commit -qm 'c716 scripts' | Out-Null
}

function Write-C716Seams {
    param([string]$Root, $Config)
    $trace = Join-Path $Root 'trace.jsonl'
    if (Test-Path -LiteralPath $trace) { Remove-Item -LiteralPath $trace -Force }
    $cfgPath = Join-Path $Root 'seams.json'
    ($Config | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $cfgPath -Encoding ASCII
    $seams = @(
        '$script:C716Root = Split-Path $PSCommandPath -Parent'
        '$script:C716Trace = Join-Path $script:C716Root ''trace.jsonl'''
        '$script:C716Cfg = Get-Content -LiteralPath (Join-Path $script:C716Root ''seams.json'') -Raw | ConvertFrom-Json'
        '$script:C716OriginalHead = $null'
        'try { $script:C716OriginalHead = ((& git -C $script:C716Root rev-parse HEAD 2>$null) | Select-Object -First 1).ToString().Trim() } catch { }'
        'function Write-C716Trace([string]$Kind, $Extra) {'
        '  $row = @{ kind = $Kind; at = [datetime]::UtcNow.ToString(''o'') }'
        '  if ($Extra) { foreach ($k in $Extra.Keys) { $row[$k] = $Extra[$k] } }'
        '  Add-Content -LiteralPath $script:C716Trace -Value ($row | ConvertTo-Json -Compress) -Encoding ASCII'
        '}'
        'function Get-AppHostPortOwners { param([int]$Port)'
        '  Write-C716Trace ''port'' @{ port = $Port }'
        '  return @()'
        '}'
        'function Stop-AppHostProcessId { param([int]$ProcessId)'
        '  Write-C716Trace ''stop-id'' @{ pid = $ProcessId }'
        '}'
        'function Stop-AppHostProcessTree { param([int]$ProcessId)'
        '  Write-C716Trace ''stop-tree'' @{ pid = $ProcessId }'
        '}'
        'function Get-AppHostStrayProcesses {'
        '  Write-C716Trace ''stray'' @{}'
        '  return @()'
        '}'
        'function Start-AppHostDevLaunch { param([string[]]$ArgumentList)'
        '  Write-C716Trace ''launch'' @{ args = ($ArgumentList -join '' '') }'
        '  $logs = Join-Path $script:C716Root ''logs'''
        '  New-Item -ItemType Directory -Force $logs | Out-Null'
        '  Set-Content -LiteralPath (Join-Path $logs ''apphost-dashboard-url.txt'') ''http://127.0.0.1:9/'' -Encoding ASCII'
        '  return [pscustomobject]@{ Id = 99999; HasExited = $false }'
        '}'
        'function Stop-AppHostLaunchChild { param($Process)'
        '  Write-C716Trace ''child-stop'' @{}'
        '}'
        'function Invoke-AppHostHealthProbe { param([int]$TimeoutSec = 5)'
        '  Write-C716Trace ''health'' @{}'
        '  return [pscustomobject]@{ StatusCode = 200; Body = ''Healthy''; Error = $null }'
        '}'
        'function Invoke-AppHostVersionProbe { param([int]$TimeoutSec = 5)'
        '  Write-C716Trace ''version'' @{}'
        '  $sha = $script:C716OriginalHead'
        '  $body = (''{{"version":"{0}","informationalVersion":"{0}","capabilities":["land-v2"]}}'' -f $sha)'
        '  return [pscustomobject]@{ StatusCode = 200; Body = $body; Error = $null }'
        '}'
        'function Wait-AppHostPollInterval { param([int]$Seconds = 3)'
        '  Write-C716Trace ''wait'' @{ seconds = $Seconds }'
        '  $w = 0'
        '  if ($null -ne $script:C716Cfg.waitSeconds) { $w = [double]$script:C716Cfg.waitSeconds }'
        '  if ($w -gt 0) { Start-Sleep -Milliseconds ([int]($w * 1000)) }'
        '}'
        'function Get-AppHostSourceHead { param([string]$SourceRoot)'
        '  Write-C716Trace ''head'' @{}'
        '  try {'
        '    $text = ((& git -C $SourceRoot rev-parse HEAD) | Select-Object -First 1).ToString().Trim()'
        '    if ($text -match ''^[0-9a-fA-F]{40}$'') { return $text.ToLowerInvariant() }'
        '    return $null'
        '  } catch { return $null }'
        '}'
        'function Show-DcpTimeoutVerdict { param($LogPath,$Evidence,[bool]$PodmanNoise=$false,$LaunchLock,$RestartLock,$WatchdogLog,[int]$DockerTimeoutSec=10)'
        '  Write-C716Trace ''dcp'' @{}'
        '  Write-Host ''DCP timeout (seamed)'''
        '}'
        'function Get-DockerVerdict { param([int]$TimeoutSec=10)'
        '  return [pscustomobject]@{ Healthy = $true; Summary = ''seamed''; Detail = @() }'
        '}'
        'function Invoke-AppHostGracefulStop { param([int]$TimeoutSec = 5, [string]$Reason)'
        '  $class = ''unreachable'''
        '  if ($script:C716Cfg.gracefulClass) { $class = [string]$script:C716Cfg.gracefulClass }'
        '  $detail = ''seamed'''
        '  if ($script:C716Cfg.gracefulDetail) { $detail = [string]$script:C716Cfg.gracefulDetail }'
        '  $pidValue = $null'
        '  if ($class -eq ''accepted'') {'
        '    $pidValue = 4242'
        '    if ($script:C716Cfg.gracefulPid) { $pidValue = [int]$script:C716Cfg.gracefulPid }'
        '  }'
        '  Write-C716Trace ''graceful'' @{ class = $class; timeout = $TimeoutSec; reason = $Reason }'
        '  return [pscustomobject]@{ Class = $class; Pid = $pidValue; Detail = $detail }'
        '}'
        'function Wait-AppHostProcessExit { param([int]$ProcessId, [int]$TimeoutSec)'
        '  Write-C716Trace ''wait-exit'' @{ pid = $ProcessId; timeout = $TimeoutSec }'
        '  $never = $false'
        '  if ($null -ne $script:C716Cfg.gracefulNeverExit) { $never = [bool]$script:C716Cfg.gracefulNeverExit }'
        '  if ($never) {'
        '    Wait-AppHostPollInterval -Seconds 1'
        '    return $null'
        '  }'
        '  $polls = 2'
        '  if ($script:C716Cfg.gracefulAlivePolls) { $polls = [int]$script:C716Cfg.gracefulAlivePolls }'
        '  for ($i = 0; $i -lt $polls; $i++) { Wait-AppHostPollInterval -Seconds 1 }'
        '  return $polls'
        '}'
    )
    $seamsPath = Join-Path $Root 'seams.ps1'
    $seams | Set-Content -LiteralPath $seamsPath -Encoding ASCII
    return $seamsPath
}

function Get-C716Trace {
    param([string]$Root)
    $path = Join-Path $Root 'trace.jsonl'
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    return @(Get-Content -LiteralPath $path -ErrorAction SilentlyContinue)
}

function Count-C716Kind {
    param($Trace, [string]$Kind)
    return @($Trace | Where-Object { $_ -match ('"kind":"{0}"' -f $Kind) }).Count
}

function Index-C716Kind {
    param($Trace, [string]$Kind)
    $pattern = '"kind":"{0}"' -f $Kind
    for ($i = 0; $i -lt @($Trace).Count; $i++) {
        if (@($Trace)[$i] -match $pattern) { return $i }
    }
    return -1
}

function Invoke-C716Restart {
    param(
        [string]$Root,
        [string[]]$Arguments = @()
    )
    $scriptPath = Join-Path (Join-Path $Root 'scripts') 'restart-apphost.ps1'
    $prevSeams = $env:ANTIPHON_APPHOST_TEST_SEAMS
    $env:ANTIPHON_APPHOST_TEST_SEAMS = Join-Path $Root 'seams.ps1'
    $prev = Get-Location
    try {
        Set-Location $Root
        $output = @(& pwsh -NoProfile -File $scriptPath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Text     = ($output -join "`n")
            Trace    = Get-C716Trace $Root
        }
    } finally {
        Set-Location $prev
        if ($null -eq $prevSeams) { Remove-Item Env:ANTIPHON_APPHOST_TEST_SEAMS -ErrorAction SilentlyContinue }
        else { $env:ANTIPHON_APPHOST_TEST_SEAMS = $prevSeams }
    }
}

function New-C716Fixture {
    param($Config)
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('c716-gs-' + [guid]::NewGuid().ToString('N'))
    New-C716Repo -Path $root
    Install-C716Scripts -Root $root
    if ($null -eq $Config.token) { $Config.token = $script:Sentinel }
    if ($null -eq $Config.waitSeconds) { $Config.waitSeconds = 0 }
    if ($null -eq $Config.dashboard) { $Config.dashboard = $true }
    Write-C716Seams -Root $root -Config $Config | Out-Null
    return [pscustomobject]@{ Root = $root }
}

function Remove-C716Fixture {
    param($Fixture)
    if ($Fixture -and $Fixture.Root -and (Test-Path -LiteralPath $Fixture.Root)) {
        Remove-Item -LiteralPath $Fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-T1 {
    $fx = New-C716Fixture -Config @{
        gracefulClass = 'accepted'; gracefulPid = 4242; gracefulAlivePolls = 2; gracefulDetail = 'accepted'
    }
    try {
        $r = Invoke-C716Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8')
        $gracefulAt = Index-C716Kind $r.Trace 'graceful'
        $stopAt = Index-C716Kind $r.Trace 'stop-tree'
        $ok = ($gracefulAt -ge 0) -and ($stopAt -gt $gracefulAt) -and ($r.Text -match 'graceful stop accepted; server PID 4242 exited after')
        if ($ok) { Write-Pass 'T-1' }
        else { Write-Fail 'T-1' ("gracefulAt=$gracefulAt stopAt=$stopAt exit=$($r.ExitCode); $($r.Text)") }
    } catch {
        Write-Fail 'T-1' $_.Exception.Message
    } finally {
        Remove-C716Fixture $fx
    }
}

function Test-T2 {
    $baseline = $null
    $subject = $null
    $baseFx = New-C716Fixture -Config @{ gracefulClass = 'unreachable'; gracefulDetail = 'connection refused' }
    $subFx = New-C716Fixture -Config @{
        gracefulClass = 'accepted'; gracefulPid = 4242; gracefulNeverExit = $true; gracefulDetail = 'accepted'
    }
    try {
        $baseline = Invoke-C716Restart -Root $baseFx.Root -Arguments @('-TimeoutSec', '8', '-GracefulStopTimeoutSec', '3')
        $subject = Invoke-C716Restart -Root $subFx.Root -Arguments @('-TimeoutSec', '8', '-GracefulStopTimeoutSec', '3')
        $ok = ($subject.ExitCode -eq $baseline.ExitCode) -and
            ($subject.Text -match 'still alive after 3s; forcing') -and
            ((Count-C716Kind $subject.Trace 'stop-tree') -ge 1)
        if ($ok) { Write-Pass 'T-2' }
        else {
            Write-Fail 'T-2' ("subjectExit=$($subject.ExitCode) baselineExit=$($baseline.ExitCode) stopTree=$(Count-C716Kind $subject.Trace 'stop-tree'); $($subject.Text)")
        }
    } catch {
        Write-Fail 'T-2' $_.Exception.Message
    } finally {
        Remove-C716Fixture $baseFx
        Remove-C716Fixture $subFx
    }
}

function Test-T3 {
    $fx = New-C716Fixture -Config @{ gracefulClass = 'unreachable'; gracefulDetail = 'connection refused' }
    try {
        $r = Invoke-C716Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8')
        $ok = ($r.Text -match 'graceful stop unreachable: .*; forcing') -and
            ((Count-C716Kind $r.Trace 'wait-exit') -eq 0) -and
            ((Count-C716Kind $r.Trace 'stop-tree') -ge 1)
        if ($ok) { Write-Pass 'T-3' }
        else {
            Write-Fail 'T-3' ("waitExit=$(Count-C716Kind $r.Trace 'wait-exit') stopTree=$(Count-C716Kind $r.Trace 'stop-tree') exit=$($r.ExitCode); $($r.Text)")
        }
    } catch {
        Write-Fail 'T-3' $_.Exception.Message
    } finally {
        Remove-C716Fixture $fx
    }
}

function Test-T4 {
    $unsupported = New-C716Fixture -Config @{ gracefulClass = 'unsupported'; gracefulDetail = 'http 404' }
    $refused = New-C716Fixture -Config @{ gracefulClass = 'refused'; gracefulDetail = 'http 403' }
    try {
        $u = Invoke-C716Restart -Root $unsupported.Root -Arguments @('-TimeoutSec', '8')
        $f = Invoke-C716Restart -Root $refused.Root -Arguments @('-TimeoutSec', '8')
        $combined = $u.Text + "`n" + $f.Text
        $ok = ($u.Text -match 'graceful stop unsupported:') -and
            ($f.Text -match 'graceful stop refused:') -and
            ($combined -notmatch [regex]::Escape($script:Sentinel)) -and
            ((Count-C716Kind $u.Trace 'stop-tree') -ge 1) -and
            ((Count-C716Kind $f.Trace 'stop-tree') -ge 1)
        if ($ok) { Write-Pass 'T-4' }
        else { Write-Fail 'T-4' $combined }
    } catch {
        Write-Fail 'T-4' $_.Exception.Message
    } finally {
        Remove-C716Fixture $unsupported
        Remove-C716Fixture $refused
    }
}

function Test-T5 {
    # Unbound named arguments are ignored without CmdletBinding, so a missing
    # switch still kills and leaves no graceful trace. The parameter has to exist.
    $declared = Select-String -LiteralPath (Join-Path $here 'restart-apphost.ps1') -Pattern 'SkipGracefulStop' -SimpleMatch
    $fx = New-C716Fixture -Config @{ gracefulClass = 'accepted'; gracefulPid = 4242; gracefulAlivePolls = 2 }
    try {
        $r = Invoke-C716Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8', '-SkipGracefulStop')
        $ok = ($null -ne $declared) -and
            ((Count-C716Kind $r.Trace 'graceful') -eq 0) -and
            ((Count-C716Kind $r.Trace 'stop-tree') -ge 1)
        if ($ok) { Write-Pass 'T-5' }
        else {
            Write-Fail 'T-5' ("declared=$([bool]($null -ne $declared)) graceful=$(Count-C716Kind $r.Trace 'graceful') stopTree=$(Count-C716Kind $r.Trace 'stop-tree') exit=$($r.ExitCode); $($r.Text)")
        }
    } catch {
        Write-Fail 'T-5' $_.Exception.Message
    } finally {
        Remove-C716Fixture $fx
    }
}

function Test-T6 {
    $fx = New-C716Fixture -Config @{ gracefulClass = 'accepted'; gracefulPid = 4242 }
    try {
        $lock = Join-Path (Join-Path $fx.Root 'logs') 'apphost.restart.lock'
        $stamp = '{0} {1}' -f 12345, [datetime]::UtcNow.ToString('o')
        Set-Content -LiteralPath $lock -Value $stamp -Encoding ASCII
        $r = Invoke-C716Restart -Root $fx.Root -Arguments @('-TimeoutSec', '8')
        $ok = ($r.ExitCode -eq 3) -and
            ((Count-C716Kind $r.Trace 'graceful') -eq 0) -and
            ((Count-C716Kind $r.Trace 'stop-tree') -eq 0)
        if ($ok) { Write-Pass 'T-6' }
        else { Write-Fail 'T-6' ("exit=$($r.ExitCode) graceful=$(Count-C716Kind $r.Trace 'graceful') stopTree=$(Count-C716Kind $r.Trace 'stop-tree'); $($r.Text)") }
    } catch {
        Write-Fail 'T-6' $_.Exception.Message
    } finally {
        Remove-C716Fixture $fx
    }
}

function Test-T7 {
    $files = @(
        (Join-Path $here 'restart-apphost.ps1'),
        (Join-Path $here 'apphost-common.ps1'),
        (Join-Path $here 'test-apphost-graceful-stop.ps1')
    )
    $reasons = @()
    foreach ($f in $files) {
        $bytes = [System.IO.File]::ReadAllBytes($f)
        $high = @($bytes | Where-Object { $_ -gt 0x7F }).Count
        if ($high -ne 0) { $reasons += ('ASCII {0} high={1}' -f (Split-Path $f -Leaf), $high) }
        $errs = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$errs)
        $n = 0
        if ($null -ne $errs) { $n = @($errs).Count }
        if ($n -ne 0) { $reasons += ('pwsh parse {0} errors={1}' -f (Split-Path $f -Leaf), $n) }
    }
    $ps51 = $null
    if (-not [string]::IsNullOrWhiteSpace($env:SystemRoot)) {
        $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    }
    if ($ps51 -and (Test-Path -LiteralPath $ps51)) {
        foreach ($f in $files) {
            $cmd = '& { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile(''' + $f.Replace('''', '''''') + ''', [ref]$null, [ref]$e); if ($null -eq $e) { 0 } else { @($e).Count } }'
            $out = & $ps51 -NoProfile -NonInteractive -Command $cmd
            if ([int]$out -ne 0) { $reasons += ('5.1 parse {0} errors={1}' -f (Split-Path $f -Leaf), $out) }
        }
    } else {
        Write-Host 'T-7 5.1 parse skipped (powershell.exe absent)'
    }
    if ($reasons.Count -eq 0) { Write-Pass 'T-7' }
    else { Write-Fail 'T-7' ($reasons -join '; ') }
}

$all = @('T1', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7')
if ($Case) {
    $name = $Case
    if ($name -match '^T-([1-7])$') { $name = 'T' + $Matches[1] }
    $fn = Get-Command -Name ('Test-{0}' -f $name) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($id in $all) { & ('Test-{0}' -f $id) }
}

Write-Host ''
Write-Host ('CARD-0716 graceful stop: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ('  ' + $line) }
    Write-Host 'APPHOST GRACEFUL STOP TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'APPHOST GRACEFUL STOP TESTS EXIT CODE: 0  (PASS)'
exit 0
