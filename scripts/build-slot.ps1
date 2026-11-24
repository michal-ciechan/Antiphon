#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0589 D-6: run one build or test driver under a host build slot.

    pwsh -NoProfile -File scripts/build-slot.ps1 -Label <what> [-SlotWaitMinutes 45] [-NoSlot] -- <command...>

    Thin launcher for `antiphon-scripts build-slot` (scripts/lib/Antiphon.Nightly/BuildSlot.cs), unit 34 of
    docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md. Kept because agents, the delegate bundle, other checkouts on
    this machine and the server2 harnesses run this file by path. The command takes a lease from the session runner's
    /build-slots broker (waiting visibly in FIFO order), adds the grant's -maxcpucount:N to a dotnet build|test|publish|pack|
    msbuild command that names none, runs the command in the foreground in the caller's directory, releases the lease and
    exits with the command's exit code. `dotnet run` is leased but never given -maxcpucount (it would hand the switch to the
    program), so build first and run with --no-build to cap nodes. A non-dotnet command (a `bash loop.sh`, a script) cannot
    be capped: it gets the grant as ANTIPHON_BUILD_SLOT_MAXCPUCOUNT and a BUILD SLOT warning line (CARD-1627). Lease each
    build separately; never wrap a loop of builds in one lease.

    Exit codes: the command's own; 4 when no slot came within -SlotWaitMinutes (nothing was run); 2 for a missing command;
    1 when the command cannot be resolved or started; 130 when interrupted; 9 when the CLI cannot be built or started (one
    stderr line). An unreachable runner (connection refused, or an old runner without /build-slots) is not a failure: after
    60 s the command runs unleased at -maxcpucount:4 and a BUILD SLOT unleased line says so. -NoSlot is for an operator shell
    only.

    Everything after `--` is the command, passed on verbatim: under `-File` this script reads its own raw command line,
    because the PowerShell binder would split -m:2 into -m and 2; called from a PowerShell session it reads $args, and an
    unsplatted array stays one token per element. The lease belongs to this process (--holder-pid), as when the body was
    PowerShell. Owner: docs/testing-and-build.md "Build slots (CARD-0589)". ASCII-only: parseable under Windows PowerShell 5.1.
#>

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')

# Under -File the raw process arguments follow this script's own path; when the script is invoked from a PowerShell
# session instead, $args is what the caller passed. With -File only the file the process was started with counts, so a
# caller script that merely names this file among its own arguments still hands over $args.
$tokens = @($args)
$argv = [Environment]::GetCommandLineArgs()
$fileAt = -1
for ($i = 1; $i -lt $argv.Count; $i++) {
    if ([string]$argv[$i] -match '^-(f|fi|fil|file)$') { $fileAt = $i + 1; break }
}
for ($i = 0; $i -lt $argv.Count; $i++) {
    if ($fileAt -ge 0 -and $i -ne $fileAt) { continue }
    $candidate = [string]$argv[$i]
    if ([string]::IsNullOrWhiteSpace($candidate) -or $candidate.StartsWith('-')) { continue }
    $full = $null
    try { $full = [IO.Path]::GetFullPath($candidate) } catch { continue }
    if ([string]::Equals($full, $PSCommandPath, [StringComparison]::OrdinalIgnoreCase)) {
        $tokens = @($argv | Select-Object -Skip ($i + 1))
        break
    }
}

$arguments = New-Object System.Collections.Generic.List[string]
$command = New-Object System.Collections.Generic.List[string]
# An in-process caller can pass an unsplatted array as one argument: each element stays one token, empty ones included.
function Add-AntiphonCommandToken {
    param([Collections.Generic.List[string]]$Target, $Value)
    if ($Value -is [array]) {
        foreach ($part in $Value) { Add-AntiphonCommandToken -Target $Target -Value $part }
    } else {
        [void]$Target.Add([string]$Value)
    }
}
for ($i = 0; $i -lt $tokens.Count; $i++) {
    $token = [string]$tokens[$i]
    if ($token -eq '--') {
        for ($j = $i + 1; $j -lt $tokens.Count; $j++) { Add-AntiphonCommandToken -Target $command -Value $tokens[$j] }
        break
    }
    if ($token -ieq '-Label' -and ($i + 1) -lt $tokens.Count) { $arguments.Add('--label'); $arguments.Add([string]$tokens[$i + 1]); $i++; continue }
    if ($token -ieq '-SlotWaitMinutes' -and ($i + 1) -lt $tokens.Count) { $arguments.Add('--slot-wait-minutes'); $arguments.Add([string]$tokens[$i + 1]); $i++; continue }
    if ($token -ieq '-NoSlot') { $arguments.Add('--no-slot'); continue }
    for ($j = $i; $j -lt $tokens.Count; $j++) { Add-AntiphonCommandToken -Target $command -Value $tokens[$j] }
    break
}
$arguments.Add('--holder-pid')
$arguments.Add([string]$PID)
$tail = @('--') + @($command.ToArray())
exit (Invoke-AntiphonCli -Command 'build-slot' -Arguments $arguments.ToArray() -Verbatim $tail)
