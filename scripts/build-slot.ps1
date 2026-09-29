#requires -Version 7.0
<#
.SYNOPSIS
    CARD-0589 D-6: run one build or test driver under a host build slot.

    pwsh -NoProfile -File scripts/build-slot.ps1 -Label <what> [-SlotWaitMinutes 45] [-NoSlot] -- <command...>

    Takes a lease from the session runner's /build-slots broker (waiting visibly in FIFO order),
    adds the grant's -maxcpucount:N to a dotnet build|test|publish|pack|msbuild command that names
    none, runs the command in the foreground with its output on the console, releases the lease,
    and exits with the command's exit code. `dotnet run` is leased but never given -maxcpucount
    (it would hand the switch to the program), so build first and run with --no-build to cap nodes.

    Exit codes: the command's own; 4 when no slot came within -SlotWaitMinutes (nothing was run);
    2 for a missing command. An unreachable runner (connection refused, or an old runner without
    /build-slots) is not a failure: after 60 s the command runs unleased at -maxcpucount:4 and a
    BUILD SLOT unleased line says so. -NoSlot is for an operator shell only.

    Everything after `--` is the command, passed on verbatim: under `pwsh -File` the script reads
    its own raw command line, because the PowerShell binder would split -m:2 into -m and 2.

    Native executables start with literal argv in the caller's working directory. PowerShell
    scripts and Windows command shims keep the call operator. Offline seam (tests only):
    C589_COMMAND_SHIM runs `pwsh -File <shim> <command...>` through the native launcher.
    Owner: docs/testing-and-build.md "Build slots (CARD-0589)". ASCII-only.
#>

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'build-slot.ps1'))

# Under `pwsh -File` the raw process arguments follow this script's own path; when the script is
# invoked from a PowerShell session instead, $args is what the caller passed.
$tokens = @($args)
$argv = [Environment]::GetCommandLineArgs()
for ($i = 0; $i -lt $argv.Count; $i++) {
    $candidate = [string]$argv[$i]
    if ([string]::IsNullOrWhiteSpace($candidate) -or $candidate.StartsWith('-')) { continue }
    $full = $null
    try { $full = [IO.Path]::GetFullPath($candidate) } catch { continue }
    if ([string]::Equals($full, $PSCommandPath, [StringComparison]::OrdinalIgnoreCase)) {
        $tokens = @($argv | Select-Object -Skip ($i + 1))
        break
    }
}

$label = ''
$waitMinutes = 45.0
$noSlot = $false
$command = @()
for ($i = 0; $i -lt $tokens.Count; $i++) {
    $token = [string]$tokens[$i]
    if ($token -eq '--') {
        if (($i + 1) -lt $tokens.Count) { $command = @($tokens[($i + 1)..($tokens.Count - 1)]) }
        break
    }
    if ($token -ieq '-Label' -and ($i + 1) -lt $tokens.Count) { $label = [string]$tokens[$i + 1]; $i++; continue }
    if ($token -ieq '-SlotWaitMinutes' -and ($i + 1) -lt $tokens.Count) { $waitMinutes = [double]$tokens[$i + 1]; $i++; continue }
    if ($token -ieq '-NoSlot') { $noSlot = $true; continue }
    $command = @($tokens[$i..($tokens.Count - 1)])
    break
}

if ($command.Count -eq 0) {
    Write-Host 'usage: pwsh -NoProfile -File scripts/build-slot.ps1 -Label <what> [-SlotWaitMinutes 45] [-NoSlot] -- <command...>'
    exit 2
}
if ([string]::IsNullOrWhiteSpace($label)) { $label = (@($command | Select-Object -First 2) -join ' ') }

$slot = $null
if ($noSlot) {
    Write-Host 'BUILD SLOT skipped by -NoSlot'
} else {
    $slot = Enter-AntiphonBuildSlot -Label $label -WaitMinutes $waitMinutes
    if ($slot.Outcome -eq 'timeout') { exit 4 }
    $command = @(Add-AntiphonMaxCpuCount -Command $command -MaxCpuCount ([int]$slot.MaxCpuCount))
    if (Test-AntiphonDotnetRunWithBuild -Command $command) {
        Write-Host ('BUILD SLOT note: dotnet run hands -maxcpucount to the program, so its implicit build runs at the default node count; build first and use dotnet run --no-build to apply maxcpucount={0}' -f $slot.MaxCpuCount)
    }
}

function Start-AntiphonWrappedCommand {
    param([string]$FileName, [string[]]$Arguments)
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FileName
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = (Get-Location).ProviderPath
    foreach ($token in $Arguments) { [void]$psi.ArgumentList.Add([string]$token) }
    $proc = [Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    try { [void]$proc.Start() }
    catch { $proc.Dispose(); throw }
    $script:WrappedProcess = $proc
    while (-not $script:WrappedProcess.WaitForExit(250)) { }
    return $script:WrappedProcess.ExitCode
}

$code = 1
$script:WrappedProcess = $null
try {
    if (-not [string]::IsNullOrWhiteSpace($env:C589_COMMAND_SHIM)) {
        $shimArgs = @('-NoProfile', '-NonInteractive', '-File', $env:C589_COMMAND_SHIM) + $command
        $code = Start-AntiphonWrappedCommand -FileName (Get-Command pwsh).Source -Arguments $shimArgs
    } else {
        $rest = @()
        if ($command.Count -gt 1) { $rest = @($command[1..($command.Count - 1)]) }
        $resolved = Get-Command -Name ([string]$command[0]) -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($resolved -and $resolved.CommandType -eq 'Application' -and
            (-not $IsWindows -or $resolved.Source -notmatch '\.(cmd|bat)$')) {
            $code = Start-AntiphonWrappedCommand -FileName $resolved.Source -Arguments $rest
        } else {
            & $command[0] @rest
            $code = $LASTEXITCODE
            if ($null -eq $code) { $code = $(if ($?) { 0 } else { 1 }) }
        }
    }
} finally {
    if ($null -ne $script:WrappedProcess) {
        if (-not $script:WrappedProcess.HasExited) {
            try { $script:WrappedProcess.Kill($true) } catch { }
        }
        $script:WrappedProcess.Dispose()
    }
    Exit-AntiphonBuildSlot -Lease $slot
}
exit $code
