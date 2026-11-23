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

# W2-nightly S20: LP_NoSyncRefuses runs NightlyRun.Run in-process (LongPathsCheckoutScriptTests); since unit 7 of
# docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md LP_LaunchRefuses and LP_PreflightRefuses do too (qualification-launch,
# qualification-preflight), and since unit 13 LP_CloneFlag (ReleaseCandidate.Run: release-candidate.ps1 is deleted).

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -CommandType Function -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'LP_')) { & $fn }
}
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { 6 })
