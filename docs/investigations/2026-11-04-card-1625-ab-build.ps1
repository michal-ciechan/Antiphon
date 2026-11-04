# CARD-1625 desktop A/B: the land verifier's build under the CARD-1506 policy versus a candidate.
# PowerShell 7, ASCII only. READY TO RUN LATER on the Windows desktop, never beside a live
# qualification or any other time-sensitive run. Not run by the Investigate task that wrote it.
#
# What it does, per repeat and per variant (variants interleaved A,B then B,A to cancel drift):
#   1. a detached scratch worktree per variant at -BaseSha, warmed once with a full build into
#      that variant's own --artifacts-path (untimed for the comparison, recorded as "warm");
#   2. git checkout -HeadSha in that worktree (only the changed files get new mtimes, like the
#      land worktree's per-land reset), then the TIMED build: exactly the land verifier's command
#      `dotnet build --artifacts-path <out> --configuration Debug` with the grant's -maxcpucount,
#      through scripts/build-slot.ps1 (one lease per build);
#   3. optional timed test step (-TestFilter): `dotnet run --project tests/Antiphon.Tests
#      --no-build --artifacts-path <out> --configuration Debug -- --treenode-filter <f>`;
#   4. a 1-second sampler for the whole build: available physical memory (GlobalMemoryStatusEx
#      ullAvailPhys, the value the build-slot broker compares with its floor), the summed and peak
#      working set of the build's process tree, and VBCSCompiler/MSBuild/csc working sets;
#   5. after the build, -ResidencySeconds of further sampling of any VBCSCompiler (the resident
#      memory CARD-1506 exists to prevent).
# Before each warm build of a step the script checks HeadSha back out to BaseSha, so every timed
# build compiles the same delta.
#
# Variants (environment applied to the build-slot.ps1 process, which its child inherits):
#   Policy        today: no opt-out, build-slot.ps1 applies scripts/lib/BuildDriverEnvironment.cs.
#   Exempt        option (a): ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT=1 and the six variables removed.
#   SharedCompiler option (b): the policy minus UseSharedCompilation (gc/MSBuild-server/node-reuse kept).
#   SharedCompilerServerGc diagnostic: option (b) without DOTNET_gcServer/DOTNET_gcConcurrent.
#   PolicyWithoutGc option (e): the policy minus DOTNET_gcServer/DOTNET_gcConcurrent (csc.exe per
#                 project keeps its runtimeconfig System.GC.Server=true; no resident compiler).
#
# Output: one JSON file (-OutFile) with every sample summary, wall times, slot lines and exit codes.
# Exit 0 when every build exited 0, 1 when any build failed, 2 for a refused precondition.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaseSha,
    [Parameter(Mandatory = $true)][string]$HeadSha,
    [ValidateSet('Policy', 'Exempt', 'SharedCompiler', 'SharedCompilerServerGc', 'PolicyWithoutGc')]
    [string[]]$Variants = @('Policy', 'PolicyWithoutGc', 'SharedCompiler'),
    [ValidateRange(1, 5)][int]$Repeat = 2,
    [string]$Repository = 'C:\src\Antiphon',
    [string]$WorkRoot = 'C:\Antiphon\ab-card-1625',
    [string]$OutFile = '',
    [string]$TestFilter = '',
    [ValidateRange(0, 1800)][int]$ResidencySeconds = 120,
    [string]$SlotsUrl = 'http://localhost:17204/build-slots',
    [switch]$AllowBusyHost,
    [switch]$KeepOutputs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) { Write-Host 'refused: this A/B measures the Windows build-driver policy; run it on the desktop'; exit 2 }
if ($BaseSha -notmatch '^[0-9a-f]{40}$' -or $HeadSha -notmatch '^[0-9a-f]{40}$') { Write-Host 'refused: -BaseSha and -HeadSha must be full 40-hex SHAs'; exit 2 }
if ($Variants.Count -lt 2) { Write-Host 'refused: name at least two variants'; exit 2 }
$wrapper = Join-Path $Repository 'scripts\build-slot.ps1'
if (-not (Test-Path -LiteralPath $wrapper)) { Write-Host "refused: $wrapper not found"; exit 2 }
$sharedVariants = @($Variants | Where-Object { $_ -in @('Exempt', 'SharedCompiler', 'SharedCompilerServerGc') })
# One VBCSCompiler serves every shared-compiler build of this user and SDK, and it keeps the GC mode of
# the build that started it, so two shared-compiler variants in one run would measure the same process.
if ($sharedVariants.Count -gt 1) { Write-Host "refused: run at most one of Exempt, SharedCompiler, SharedCompilerServerGc per invocation (they share one VBCSCompiler)"; exit 2 }
if (-not $OutFile) { $OutFile = Join-Path $WorkRoot ('ab-' + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + '.json') }

$policyNames = @('UseSharedCompilation', 'DOTNET_gcServer', 'DOTNET_gcConcurrent', 'DOTNET_CLI_USE_MSBUILD_SERVER',
    'MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER', 'ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT')

# Each variant maps to the environment the wrapper process starts with. $null removes a variable.
# With the opt-out set the wrapper leaves the environment alone, so the explicit values reach the build.
function Get-VariantEnvironment([string]$Name) {
    $policy = [ordered]@{ UseSharedCompilation = 'false'; DOTNET_gcServer = '0'; DOTNET_gcConcurrent = '0';
        DOTNET_CLI_USE_MSBUILD_SERVER = '0'; MSBUILDDISABLENODEREUSE = '1'; DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1' }
    $vars = [ordered]@{}
    foreach ($n in $policyNames) { $vars[$n] = $null }
    switch ($Name) {
        'Policy' { }
        'Exempt' { $vars['ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT'] = '1' }
        'SharedCompiler' {
            foreach ($k in $policy.Keys) { $vars[$k] = $policy[$k] }
            $vars['UseSharedCompilation'] = $null
            $vars['ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT'] = '1'
        }
        'SharedCompilerServerGc' {
            foreach ($k in $policy.Keys) { $vars[$k] = $policy[$k] }
            $vars['UseSharedCompilation'] = $null; $vars['DOTNET_gcServer'] = $null; $vars['DOTNET_gcConcurrent'] = $null
            $vars['ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT'] = '1'
        }
        'PolicyWithoutGc' {
            foreach ($k in $policy.Keys) { $vars[$k] = $policy[$k] }
            $vars['DOTNET_gcServer'] = $null; $vars['DOTNET_gcConcurrent'] = $null
            $vars['ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT'] = '1'
        }
    }
    return $vars
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class C1625Memory {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad; public ulong ullTotalPhys; public ulong ullAvailPhys;
        public ulong ullTotalPageFile; public ulong ullAvailPageFile; public ulong ullTotalVirtual;
        public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);
    public static long AvailableMb() {
        var m = new MEMORYSTATUSEX();
        return GlobalMemoryStatusEx(m) ? (long)(m.ullAvailPhys / 1048576UL) : -1;
    }
    public static long CommitMb() {
        var m = new MEMORYSTATUSEX();
        return GlobalMemoryStatusEx(m) ? (long)((m.ullTotalPageFile - m.ullAvailPageFile) / 1048576UL) : -1;
    }
}
'@

function Get-Snapshot([int]$RootPid) {
    $all = Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name, WorkingSetSize
    $children = @{}
    foreach ($p in $all) {
        $key = [int]$p.ParentProcessId
        if (-not $children.ContainsKey($key)) { $children[$key] = New-Object System.Collections.Generic.List[object] }
        $children[$key].Add($p)
    }
    $tree = 0L; $seen = @{}; $stack = New-Object System.Collections.Generic.Stack[int]
    $stack.Push($RootPid)
    while ($stack.Count -gt 0) {
        $id = $stack.Pop()
        if ($seen.ContainsKey($id)) { continue }
        $seen[$id] = $true
        if ($children.ContainsKey($id)) {
            foreach ($c in $children[$id]) { $tree += [long]$c.WorkingSetSize; $stack.Push([int]$c.ProcessId) }
        }
    }
    $byName = @{ vbcs = 0L; msbuild = 0L; csc = 0L; vbcsCount = 0; cscCount = 0; dotnetCount = 0 }
    foreach ($p in $all) {
        $n = ('' + $p.Name).ToLowerInvariant()
        if ($n -eq 'vbcscompiler.exe') { $byName.vbcs += [long]$p.WorkingSetSize; $byName.vbcsCount++ }
        elseif ($n -eq 'csc.exe') { $byName.csc += [long]$p.WorkingSetSize; $byName.cscCount++ }
        elseif ($n -eq 'msbuild.exe') { $byName.msbuild += [long]$p.WorkingSetSize }
        elseif ($n -eq 'dotnet.exe') { $byName.dotnetCount++ }
    }
    return [pscustomobject]@{
        availableMb = [C1625Memory]::AvailableMb(); commitMb = [C1625Memory]::CommitMb()
        treeMb = [math]::Round($tree / 1MB); vbcsMb = [math]::Round($byName.vbcs / 1MB); vbcsCount = $byName.vbcsCount
        cscMb = [math]::Round($byName.csc / 1MB); cscCount = $byName.cscCount; dotnetCount = $byName.dotnetCount
    }
}

function Get-Summary($Samples) {
    $list = @($Samples)
    if ($list.Count -eq 0) { return $null }
    $avail = @($list | ForEach-Object { $_.availableMb } | Where-Object { $_ -ge 0 })
    return [ordered]@{
        samples = $list.Count
        vbcsCountAtStart = $list[0].vbcsCount
        minAvailableMb = if ($avail.Count) { ($avail | Measure-Object -Minimum).Minimum } else { $null }
        meanAvailableMb = if ($avail.Count) { [math]::Round(($avail | Measure-Object -Average).Average) } else { $null }
        secondsBelow6144 = @($avail | Where-Object { $_ -lt 6144 }).Count
        peakTreeMb = ($list | Measure-Object treeMb -Maximum).Maximum
        peakVbcsMb = ($list | Measure-Object vbcsMb -Maximum).Maximum
        peakCscMb = ($list | Measure-Object cscMb -Maximum).Maximum
        maxCscCount = ($list | Measure-Object cscCount -Maximum).Maximum
        peakCommitMb = ($list | Measure-Object commitMb -Maximum).Maximum
    }
}

# Runs one command through build-slot.ps1 with the variant environment, sampling every second.
function Invoke-Sampled([string]$Label, [string]$Cwd, [string[]]$Command, $VariantEnv, [string]$LogPath) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = Join-Path $PSHOME 'pwsh.exe'
    foreach ($a in @('-NoProfile', '-NonInteractive', '-File', $wrapper, '-Label', $Label, '--') + $Command) { $psi.ArgumentList.Add($a) }
    $psi.WorkingDirectory = $Cwd
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($k in $VariantEnv.Keys) {
        if ($null -eq $VariantEnv[$k]) { [void]$psi.Environment.Remove($k) } else { $psi.Environment[$k] = $VariantEnv[$k] }
    }
    $samples = New-Object System.Collections.Generic.List[object]
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = [System.Diagnostics.Process]::Start($psi)
    $out = $proc.StandardOutput.ReadToEndAsync()
    $err = $proc.StandardError.ReadToEndAsync()
    while (-not $proc.HasExited) {
        $samples.Add((Get-Snapshot -RootPid $proc.Id))
        [void]$proc.WaitForExit(1000)
    }
    $proc.WaitForExit()
    $watch.Stop()
    $text = $out.GetAwaiter().GetResult() + "`n" + $err.GetAwaiter().GetResult()
    Set-Content -LiteralPath $LogPath -Value $text -Encoding ascii
    $slotLines = @($text -split "`r?`n" | Where-Object { $_ -match 'BUILD SLOT' })
    $waited = $null; $held = $null
    foreach ($l in $slotLines) {
        if ($l -match 'granted .*waited=(\d+)s') { $waited = [int]$Matches[1] }
        if ($l -match 'released .*held=(\d+)s') { $held = [int]$Matches[1] }
    }
    return [ordered]@{
        label = $Label; exitCode = $proc.ExitCode; wallSeconds = [math]::Round($watch.Elapsed.TotalSeconds, 1)
        slotWaitedSeconds = $waited; slotHeldSeconds = $held; slotLines = $slotLines
        memory = Get-Summary $samples; log = $LogPath
    }
}

function Invoke-Git([string]$Cwd, [string[]]$GitArgs) {
    $output = & git -C $Cwd @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') failed in ${Cwd}: $output" }
    return $output
}

# Preconditions: the shared host must be quiet unless the operator says otherwise.
$hostBefore = $null
try { $hostBefore = Invoke-RestMethod -Uri $SlotsUrl -TimeoutSec 10 } catch { Write-Host "warning: $SlotsUrl unreadable: $($_.Exception.Message)" }
if ($hostBefore -and -not $AllowBusyHost) {
    $leaseCount = @($hostBefore.leases).Count
    if ($leaseCount -gt 0) { Write-Host "refused: $leaseCount build-slot lease(s) held; rerun on a quiet host or pass -AllowBusyHost"; exit 2 }
}
foreach ($sha in @($BaseSha, $HeadSha)) { [void](Invoke-Git $Repository @('cat-file', '-e', ($sha + '^{commit}'))) }

$vbcsAtStart = (Get-Snapshot -RootPid 0).vbcsCount
if ($sharedVariants.Count -gt 0 -and $vbcsAtStart -gt 0 -and -not $AllowBusyHost) {
    Write-Host "refused: $vbcsAtStart VBCSCompiler process(es) already running; the shared-compiler variant would reuse a compiler it did not start. Wait for it to idle out (about 10 minutes) or pass -AllowBusyHost"; exit 2
}
Push-Location -LiteralPath $Repository
try { $sdkVersion = ('' + (& dotnet --version)).Trim() } finally { Pop-Location }
New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null
$runId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$result = [ordered]@{
    card = 'CARD-1625'; runId = $runId; startedUtc = (Get-Date).ToUniversalTime().ToString('o')
    machine = $env:COMPUTERNAME; logicalCpus = [Environment]::ProcessorCount
    dotnetSdk = $sdkVersion
    baseSha = $BaseSha; headSha = $HeadSha; variants = $Variants; repeat = $Repeat; testFilter = $TestFilter
    hostBefore = $hostBefore; vbcsBefore = $vbcsAtStart
    runs = New-Object System.Collections.Generic.List[object]
}

$worktrees = @{}
$anyFailed = $false
try {
    foreach ($v in $Variants) {
        $wt = Join-Path $WorkRoot ("wt-$runId-" + $v.ToLowerInvariant())
        [void](Invoke-Git $Repository @('worktree', 'add', '--detach', $wt, $BaseSha))
        $worktrees[$v] = $wt
    }
    for ($r = 1; $r -le $Repeat; $r++) {
        $order = if ($r % 2 -eq 1) { $Variants } else { @($Variants)[($Variants.Count - 1)..0] }
        foreach ($v in $order) {
            $wt = $worktrees[$v]
            $art = Join-Path $WorkRoot ("art-$runId-" + $v.ToLowerInvariant())
            $venv = Get-VariantEnvironment $v
            [void](Invoke-Git $wt @('checkout', '--detach', '--quiet', $BaseSha))
            $warm = Invoke-Sampled "c1625-ab-warm-$v-$r" $wt @('dotnet', 'build', '--artifacts-path', $art, '--configuration', 'Debug') $venv (Join-Path $WorkRoot "log-$runId-$v-$r-warm.txt")
            [void](Invoke-Git $wt @('checkout', '--detach', '--quiet', $HeadSha))
            $timed = Invoke-Sampled "c1625-ab-$v-$r" $wt @('dotnet', 'build', '--artifacts-path', $art, '--configuration', 'Debug') $venv (Join-Path $WorkRoot "log-$runId-$v-$r-build.txt")
            $test = $null
            if ($TestFilter -and $timed.exitCode -eq 0) {
                $results = Join-Path $art ("results-$r")
                $test = Invoke-Sampled "c1625-ab-test-$v-$r" $wt @('dotnet', 'run', '--project', 'tests/Antiphon.Tests', '--no-build',
                    '--artifacts-path', $art, '--configuration', 'Debug', '--', '--treenode-filter', $TestFilter, '--report-trx',
                    '--results-directory', $results) $venv (Join-Path $WorkRoot "log-$runId-$v-$r-test.txt")
            }
            $residency = New-Object System.Collections.Generic.List[object]
            for ($s = 0; $s -lt $ResidencySeconds; $s += 5) {
                $residency.Add((Get-Snapshot -RootPid 0))
                Start-Sleep -Seconds 5
            }
            $run = [ordered]@{ variant = $v; repeat = $r; environment = $venv; warm = $warm; build = $timed; test = $test
                residency = Get-Summary $residency; residencyLastVbcsMb = if ($residency.Count) { $residency[$residency.Count - 1].vbcsMb } else { $null } }
            $result.runs.Add($run)
            foreach ($step in @($warm, $timed, $test)) { if ($step -and $step.exitCode -ne 0) { $anyFailed = $true } }
            $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutFile -Encoding ascii
            Write-Host ("C1625 AB variant={0} repeat={1} build={2}s exit={3} slotWait={4}s minAvail={5}MB peakTree={6}MB peakVbcs={7}MB" -f $v, $r,
                $timed.wallSeconds, $timed.exitCode, $timed.slotWaitedSeconds, $timed.memory.minAvailableMb, $timed.memory.peakTreeMb, $timed.memory.peakVbcsMb)
        }
    }
}
finally {
    $result['finishedUtc'] = (Get-Date).ToUniversalTime().ToString('o')
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutFile -Encoding ascii
    if (-not $KeepOutputs) {
        foreach ($v in @($worktrees.Keys)) {
            & git -C $Repository worktree remove --force $worktrees[$v] 2>&1 | Out-Null
            $art = Join-Path $WorkRoot ("art-$runId-" + $v.ToLowerInvariant())
            if (Test-Path -LiteralPath $art) { Remove-Item -LiteralPath $art -Recurse -Force }
        }
    }
    Write-Host "C1625 AB result $OutFile"
}
if ($anyFailed) { exit 1 }
exit 0
