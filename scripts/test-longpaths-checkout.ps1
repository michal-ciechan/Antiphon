#requires -Version 5.1
# Witnesses for an rc checkout cloned without core.longpaths=true.
# ASCII-only for Windows PowerShell 5.1.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$repoRoot = Split-Path -Parent $here
if ([string]::IsNullOrWhiteSpace($env:TEMP)) {
    if (-not [string]::IsNullOrWhiteSpace($env:TMPDIR)) { $env:TEMP = $env:TMPDIR }
    else { $env:TEMP = '/tmp' }
}
. (Join-Path $here 'lib/c487-harness.ps1')
. (Join-Path $here 'lib/nightly-common.ps1')
. (Join-Path $here 'lib/nightly-coverage.ps1')
. (Join-Path $here 'lib/release-gate.ps1')
. (Join-Path $here 'lib/nightly-run-impl.ps1')
$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory

function Remove-LpTree {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue | ForEach-Object { $_.IsReadOnly = $false }
    Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
}

function New-LpDirectory {
    param([string]$Name)
    $path = Join-Path $ResultsDirectory $Name
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

function New-LpGitRepo {
    param([string]$Root, [string]$Origin, [switch]$LongPaths)
    $clone = Join-Path $Root 'checkout'
    New-Item -ItemType Directory -Path $clone -Force | Out-Null
    & git -C $clone init -q -b master
    if ($LASTEXITCODE -ne 0) { & git -C $clone init -q }
    & git -C $clone config user.email 'lp@example.invalid'
    & git -C $clone config user.name 'lp'
    & git -C $clone -c user.name=lp -c user.email=lp@example.invalid -c commit.gpgsign=false commit --allow-empty -q -m lp
    if ($LASTEXITCODE -ne 0) { throw 'lp fixture commit failed' }
    if (-not [string]::IsNullOrWhiteSpace($Origin)) { & git -C $clone remote add origin $Origin }
    if ($LongPaths) { & git -C $clone config --local core.longpaths true }
    $head = (@(& git -C $clone rev-parse HEAD) | Select-Object -First 1).ToString().Trim()
    if ($head -notmatch '^[0-9a-f]{40}$') { throw ('lp fixture head unreadable: ' + $head) }
    return [pscustomobject]@{ Path = $clone; Head = $head }
}

function Write-LpNightlyMarker {
    param([string]$Clone, [string]$Origin)
    $json = '{"origin":"' + $Origin + '","kind":"nightly-clone"}'
    [IO.File]::WriteAllText((Join-Path $Clone '.antiphon-nightly-owned'), $json)
}

function Invoke-LpChild {
    param([string]$File, [string[]]$Arguments, [hashtable]$Environment)
    $pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $pwsh
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $repoRoot
    $argText = '-NoProfile -NonInteractive -File "' + $File + '"'
    if ($Arguments) {
        foreach ($argument in @($Arguments)) {
            if ([string]::IsNullOrEmpty($argument)) { continue }
            if ($argument.Contains(' ')) { $argText += ' "' + $argument + '"' }
            else { $argText += ' ' + $argument }
        }
    }
    $psi.Arguments = $argText
    if ($Environment) {
        foreach ($key in @($Environment.Keys)) { $psi.EnvironmentVariables[$key] = [string]$Environment[$key] }
    }
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    [void]$proc.WaitForExit()
    return [pscustomobject]@{ Code = $proc.ExitCode; Text = ($stdoutTask.Result + "`n" + $stderrTask.Result) }
}

function Get-LpCaptured {
    param($Captured)
    $lines = New-Object System.Collections.Generic.List[string]
    $result = $null
    foreach ($item in @($Captured)) {
        if ($item -is [System.Management.Automation.InformationRecord]) { $lines.Add([string]$item.MessageData) }
        elseif ($null -ne $item -and $null -ne $item.PSObject.Properties['Refusal']) { $result = $item }
        elseif ($null -ne $item) { $lines.Add([string]$item) }
    }
    return [pscustomobject]@{ Result = $result; Text = ($lines -join "`n") }
}

function Invoke-LpNoSync {
    param([string]$Clone, [string]$Root, [string]$Origin)
    $runId = 'lp' + [guid]::NewGuid().ToString('N')
    $captured = @(Invoke-AntiphonNightlyRunCore -NoSync -WhatIf -CheckoutRoot $Clone -StateRoot (Join-Path $Root 'state') -LogRoot (Join-Path $Root 'logs') -CoordinationRoot (Join-Path $Root 'coord') -RemoteUrl $Origin -Ref 'master' -Trigger 'scheduled' -Profile 'nightly' -SelectionMode 'Full' -RunId $runId 6>&1)
    return (Get-LpCaptured $captured)
}

function Write-LpGitShim {
    param([string]$Directory, [string]$Log, [string]$RealGit)
    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    $windows = [System.IO.Path]::DirectorySeparatorChar -eq [char]92
    if ($windows) {
        $cmd = Join-Path $Directory 'git.cmd'
        # The cmd shim records every invocation, fails worktree add and remove, and
        # delegates every other git command. It is unobserved on Windows.
        $text = @"
@echo off
echo %*>>"%LONGPATHS_GIT_LOG%"
set "ALL=%*"
set "prev="
:lp_loop
if "%~1"=="" goto lp_run
if /I "%prev%"=="worktree" if /I "%~1"=="add" goto lp_add
if /I "%prev%"=="worktree" if /I "%~1"=="remove" goto lp_remove
set "prev=%~1"
shift
goto lp_loop
:lp_add
>&2 echo LONGPATHS-WITNESS worktree-add stderr
exit /b 128
:lp_remove
>&2 echo LONGPATHS-WITNESS worktree-remove stderr
exit /b 128
:lp_run
"%LONGPATHS_REAL_GIT%" %ALL%
exit /b %ERRORLEVEL%
"@
        [IO.File]::WriteAllText($cmd, $text.Replace("`n", "`r`n"))
        return $cmd
    }
    $shim = Join-Path $Directory 'git'
    $text = @'
#!/bin/sh
printf '%s\n' "$*" >> "$LONGPATHS_GIT_LOG"
prev=
for arg in "$@"; do
    if [ "$prev" = "worktree" ]; then
        if [ "$arg" = "add" ]; then
            echo "LONGPATHS-WITNESS worktree-add stderr" >&2
            exit 128
        fi
        if [ "$arg" = "remove" ]; then
            echo "LONGPATHS-WITNESS worktree-remove stderr" >&2
            exit 128
        fi
    fi
    prev=$arg
done
exec "$LONGPATHS_REAL_GIT" "$@"
'@
    [IO.File]::WriteAllText($shim, $text.Replace("`r`n", "`n"))
    & chmod a+x $shim
    if ($LASTEXITCODE -ne 0) { throw 'lp git shim was not marked executable' }
    return $shim
}

function Test-LP_GuardStderr {
    $root = New-LpDirectory ('lp-guard-' + [guid]::NewGuid().ToString('N'))
    $log = Join-Path $root 'git.log'
    $code = -1
    $text = ''
    $logged = ''
    $detail = ''
    try {
        $real = (Get-Command git -CommandType Application | Select-Object -First 1).Source
        $bin = Join-Path $root 'bin'
        [void](Write-LpGitShim -Directory $bin -Log $log -RealGit $real)
        $child = Invoke-LpChild -File (Join-Path $here 'test-apphost-main-worktree-guard.ps1') -Environment @{
            PATH = ($bin + [IO.Path]::PathSeparator + $env:PATH)
            LONGPATHS_GIT_LOG = $log
            LONGPATHS_REAL_GIT = $real
        }
        $code = $child.Code
        $text = $child.Text
        if (Test-Path -LiteralPath $log) { $logged = [IO.File]::ReadAllText($log) }
    } catch {
        $detail = $_.Exception.Message
    } finally {
        Remove-LpTree $root
    }
    Assert-C487 ($code -eq 1) 'LP guard exits 1 when worktree add fails' ($detail + ' exit=' + $code)
    Assert-C487 ($text.Contains('LONGPATHS-WITNESS worktree-add stderr')) 'LP guard failure contains git stderr' ($detail + ' ' + $text)
    Assert-C487 ($logged.Contains('core.longpaths=true') -and $logged.Contains('worktree add')) 'LP guard worktree add passes core.longpaths' ($detail + ' ' + $logged)
}

function Test-LP_PathArithmetic {
    $prefix = 'C:\Users\lndco\AppData\Local\Temp\antiphon-apphost-worktree-guard-' + ('a' * 32) + '\linked'
    $files = @(& git -C $repoRoot -c core.quotepath=false ls-files)
    $over = @($files | Where-Object { ($prefix.Length + 1 + ([string]$_).Length) -gt 260 })
    $guard = [IO.File]::ReadAllText((Join-Path $here 'test-apphost-main-worktree-guard.ps1'))
    $documented = $guard.Contains('git -c core.longpaths=true -C $sourceRoot') -and $guard.Contains('269') -and $guard.Contains('260')
    Assert-C487 ($prefix.Length -eq 105) 'LP guard temp prefix is 105 characters' ([string]$prefix.Length)
    Assert-C487 ($over.Count -eq 1) 'LP exactly one tracked path exceeds MAX_PATH under the guard prefix' (([string]$over.Count) + ' ' + ($over -join ','))
    Assert-C487 $documented 'LP guard documents the 269 character sum and passes core.longpaths' $(if ($documented) { '' } else { 'missing override or arithmetic note' })
}

function Test-LP_LaunchRefuses {
    $root = New-LpDirectory ('lp-launch-' + [guid]::NewGuid().ToString('N'))
    $badText = ''
    $badCode = -1
    $goodText = ''
    $detail = ''
    $fix = ''
    try {
        $repo = New-LpGitRepo -Root $root
        $state = Join-Path $root 'state'
        New-Item -ItemType Directory -Path $state -Force | Out-Null
        $config = Join-Path $root 'launch-record.json'
        $record = [ordered]@{
            checkoutRoot = $repo.Path
            expectedSha = $repo.Head
            nightlyArguments = @('-NoSync', '-RunId', 'lp-launch', '-StateRoot', $state)
        }
        [IO.File]::WriteAllText($config, ($record | ConvertTo-Json -Depth 4))
        $fix = 'git -C ' + $repo.Path + ' config --local core.longpaths true'
        $bad = Invoke-LpChild -File (Join-Path $here 'qualification/launch-rc.ps1') -Arguments @('-Config', $config) -Environment @{ ANTIPHON_QUALIFICATION_RUN_ID = 'lp-launch' }
        $badCode = $bad.Code
        $badText = $bad.Text
        & git -C $repo.Path config --local core.longpaths true
        $good = Invoke-LpChild -File (Join-Path $here 'qualification/launch-rc.ps1') -Arguments @('-Config', $config) -Environment @{ ANTIPHON_QUALIFICATION_RUN_ID = 'lp-launch' }
        $goodText = $good.Text
    } catch {
        $detail = $_.Exception.Message
    } finally {
        Remove-LpTree $root
    }
    Assert-C487 ($badCode -eq 2 -and $badText.Contains('longpaths-not-enabled')) 'LP launch refuses longpaths-not-enabled' ($detail + ' exit=' + $badCode + ' ' + $badText)
    Assert-C487 ($badText.Contains($fix) -and $fix.Contains('config --local core.longpaths true')) 'LP launch names the fix command' ($detail + ' ' + $badText)
    Assert-C487 ($goodText.Contains('build-slot')) 'LP launch with longpaths continues to build-slot' ($detail + ' ' + $goodText)
    Assert-C487 (-not $goodText.Contains('longpaths-not-enabled')) 'LP launch with longpaths does not refuse longpaths' ($detail + ' ' + $goodText)
}

function Test-LP_NoSyncRefuses {
    $origin = 'https://example.invalid/antiphon.git'
    $root = New-LpDirectory ('lp-nosync-' + [guid]::NewGuid().ToString('N'))
    $bad = $null
    $good = $null
    $detail = ''
    try {
        $repo = New-LpGitRepo -Root $root -Origin $origin
        Write-LpNightlyMarker -Clone $repo.Path -Origin $origin
        foreach ($name in @('state', 'logs', 'coord')) { New-Item -ItemType Directory -Path (Join-Path $root $name) -Force | Out-Null }
        $bad = Invoke-LpNoSync -Clone $repo.Path -Root $root -Origin $origin
        & git -C $repo.Path config --local core.longpaths true
        $good = Invoke-LpNoSync -Clone $repo.Path -Root $root -Origin $origin
    } catch {
        $detail = $_.ToString()
    } finally {
        Remove-LpTree $root
    }
    $badText = ''
    $badCode = -1
    $badRefusal = ''
    if ($null -ne $bad -and $null -ne $bad.Result) {
        $badText = [string]$bad.Text
        $badCode = [int]$bad.Result.ExitCode
        $badRefusal = [string]$bad.Result.Refusal
    } elseif ($null -ne $bad) { $badText = [string]$bad.Text }
    $goodText = ''
    $goodCode = -1
    $goodPhase = ''
    $goodRefusal = 'unset'
    if ($null -ne $good -and $null -ne $good.Result) {
        $goodText = [string]$good.Text
        $goodCode = [int]$good.Result.ExitCode
        $goodPhase = [string]$good.Result.Phase
        $goodRefusal = [string]$good.Result.Refusal
    }
    $fixNamed = $badText.Contains('config --local core.longpaths true') -and $badText.Contains('git -C ')
    Assert-C487 ($badCode -eq 3 -and $badRefusal -ceq 'longpaths-not-enabled' -and $badText.Contains('longpaths-not-enabled')) 'LP nosync refuses longpaths-not-enabled' ($detail + ' exit=' + $badCode + ' refusal=' + $badRefusal + ' ' + $badText)
    Assert-C487 $fixNamed 'LP nosync names the fix command' ($detail + ' ' + $badText)
    Assert-C487 ($goodCode -eq 0 -and $goodPhase -eq 'whatif') 'LP nosync with longpaths reaches whatif' ($detail + ' exit=' + $goodCode + ' phase=' + $goodPhase + ' ' + $goodText)
    Assert-C487 ($goodRefusal -eq '') 'LP nosync with longpaths has an empty refusal' ($detail + ' refusal=' + $goodRefusal)
}

function Test-LP_PreflightRefuses {
    $root = New-LpDirectory ('lp-preflight-' + [guid]::NewGuid().ToString('N'))
    $script = Join-Path $here 'qualification/preflight.ps1'
    $badText = ''
    $badCode = -1
    $goodText = ''
    $goodCode = -1
    $detail = ''
    $fix = ''
    try {
        if (-not (Test-Path -LiteralPath $script)) { throw 'preflight.ps1 is absent' }
        $repo = New-LpGitRepo -Root $root
        $fix = 'git -C ' + $repo.Path + ' config --local core.longpaths true'
        $bad = Invoke-LpChild -File $script -Arguments @('-CheckoutRoot', $repo.Path)
        $badCode = $bad.Code
        $badText = $bad.Text
        & git -C $repo.Path config --local core.longpaths true
        $good = Invoke-LpChild -File $script -Arguments @('-CheckoutRoot', $repo.Path)
        $goodCode = $good.Code
        $goodText = $good.Text
    } catch {
        $detail = $_.Exception.Message
    } finally {
        Remove-LpTree $root
    }
    Assert-C487 ($badCode -eq 2 -and $badText.Contains('longpaths-not-enabled')) 'LP preflight refuses longpaths-not-enabled' ($detail + ' exit=' + $badCode + ' ' + $badText)
    Assert-C487 ($badText.Contains($fix) -and $fix.Contains('config --local core.longpaths true')) 'LP preflight names the fix command' ($detail + ' ' + $badText)
    Assert-C487 ($goodCode -eq 0 -and -not $goodText.Contains('longpaths-not-enabled')) 'LP preflight accepts core.longpaths true' ($detail + ' exit=' + $goodCode + ' ' + $goodText)
}

function Write-LpReleaseSeams {
    param([string]$Path, [string]$Log, [string]$Sha)
    $text = @"
`$ReleaseGateSeams = @{
    Git = {
        param(`$WorkingDirectory, `$Arguments)
        Add-Content -LiteralPath '$Log' -Value (`$Arguments -join '|') -Encoding ASCII
        if ([string]`$Arguments[0] -eq 'ls-remote') {
            return @{ ExitCode = 0; Output = '$Sha refs/heads/rc/2026-11-19' }
        }
        return @{ ExitCode = 0; Output = '' }
    }
    GitHub = {
        param(`$Verb, `$Arguments, `$WorkingDirectory)
        return @{ ExitCode = 0; Output = '' }
    }
}
"@
    [IO.File]::WriteAllText($Path, $text)
}

function Test-LP_CloneFlag {
    $root = New-LpDirectory ('lp-clone-' + [guid]::NewGuid().ToString('N'))
    $log = Join-Path $root 'git.log'
    $logged = ''
    $whatIfText = ''
    $detail = ''
    try {
        $seams = Join-Path $root 'seams.ps1'
        Write-LpReleaseSeams -Path $seams -Log $log -Sha ('a' * 40)
        $release = Join-Path $root 'releases'
        $checkout = Join-Path $root 'checkout'
        $script = Join-Path $here 'release-candidate.ps1'
        $whatIf = Get-LpCaptured @(& $script -PassThru -WhatIf -ReleaseRoot $release -CheckoutRoot $checkout -RemoteUrl 'https://example.invalid/antiphon.git' -Ref 'rc/2026-11-19' -SeamsPath $seams 6>&1)
        $whatIfText = [string]$whatIf.Text
        $liveCheckout = Join-Path $root 'live-checkout'
        [void](Get-LpCaptured @(& $script -PassThru -ReleaseRoot $release -CheckoutRoot $liveCheckout -RemoteUrl 'https://example.invalid/antiphon.git' -Ref 'rc/2026-11-19' -SeamsPath $seams 6>&1))
        if (Test-Path -LiteralPath $log) { $logged = [IO.File]::ReadAllText($log) }
    } catch {
        $detail = $_.ToString()
    } finally {
        Remove-LpTree $root
    }
    Assert-C487 ($logged.Contains('clone|-c|core.longpaths=true|')) 'LP release clone argv passes core.longpaths' ($detail + ' ' + $logged)
    Assert-C487 ($whatIfText.Contains('WhatIf: would clone -c core.longpaths=true')) 'LP release whatif names core.longpaths' ($detail + ' ' + $whatIfText)
}

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'LP_')) { & $fn }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 19 })
