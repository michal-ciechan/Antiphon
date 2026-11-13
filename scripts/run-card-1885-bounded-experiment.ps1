#requires -Version 7.0
# Local foreground owner, no outer lease. Every child takes one fresh build lease.
param(
    [string]$EvidenceRoot = '.antiphon/c1885-debug/held-performance',
    [string]$CompilerCacheRoot = '/tmp/card-1885-debug-17799819-cache'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsLinux) { throw 'native Linux experiment required' }
$worktree = Split-Path $PSScriptRoot -Parent
$EvidenceRoot = [IO.Path]::GetFullPath((Join-Path $worktree $EvidenceRoot))
if ([IO.Directory]::Exists($EvidenceRoot)) { throw 'fresh evidence root required; preserve previous attempts' }
[void][IO.Directory]::CreateDirectory($EvidenceRoot)
. (Join-Path $PSScriptRoot 'lib/linux-compiler-evidence.ps1')
$project = 'tests/Antiphon.TimeoutAudit.Tests/Antiphon.TimeoutAudit.Tests.csproj'
$identity = 'debug-held-17799819'
$pwsh = (Get-Command pwsh).Source
$watch = [Diagnostics.Stopwatch]::new()
$costs = [Collections.Generic.List[double]]::new()
$outcomes = [Collections.Generic.List[object]]::new()
$failure = $null
$inventory = @(
    @('Baseline', 'Restore', 0),
    @('Baseline', 'Prime', 0), @('Candidate', 'Prime', 0),
    @('Baseline', 'Measured', 1), @('Candidate', 'Measured', 1),
    @('Candidate', 'Measured', 2), @('Baseline', 'Measured', 2),
    @('Baseline', 'Measured', 3), @('Candidate', 'Measured', 3)
)
try {
    foreach ($row in $inventory) {
        if ($row[1] -ne 'Restore' -and -not $watch.IsRunning) { $watch.Start() }
        if ($watch.Elapsed.TotalMinutes -ge 30) { throw '30-minute collection bound reached; no new build authorized' }
        $label = "c1885-held-$($row[0])-$($row[1])-$($row[2])"
        Write-Host "C1885 CONTROLLER starting=$label collectionSeconds=$($watch.Elapsed.TotalSeconds)"
        $child = [Diagnostics.ProcessStartInfo]::new($pwsh)
        $child.UseShellExecute = $false
        $child.WorkingDirectory = $worktree
        # This is a separate leased command per build, never a lease around this loop.
        foreach ($arg in @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'build-slot.ps1'),
            '-SlotWaitMinutes', '5', '-Label', $label, '--', $pwsh, '-NoProfile', '-File',
            (Join-Path $PSScriptRoot 'measure-linux-shared-compiler.ps1'), '-Mode', $row[0],
            '-Phase', $row[1], '-Sample', [string]$row[2], '-Project', $project,
            '-ExperimentId', $identity, '-CompilerCacheRoot', $CompilerCacheRoot, '-EvidenceRoot', $EvidenceRoot)) {
            $child.ArgumentList.Add([string]$arg)
        }
        $cost = [Diagnostics.Stopwatch]::StartNew()
        $process = [Diagnostics.Process]::Start($child)
        try {
            while (-not $process.WaitForExit(1000)) {
                if ($watch.Elapsed.TotalMinutes -ge 30) {
                    $process.Kill($true)
                    if (-not $process.WaitForExit(15000)) { throw 'owned command did not exit after collection cancellation' }
                    throw '30-minute collection bound reached during owned command'
                }
            }
            $outcomes.Add(@{ Mode = $row[0]; Phase = $row[1]; Sample = $row[2]; ExitCode = $process.ExitCode; WallSeconds = $cost.Elapsed.TotalSeconds })
            if ($process.ExitCode -ne 0) { throw "leased command failed: $label exit=$($process.ExitCode)" }
        } finally { $process.Dispose() }
        if ($row[1] -eq 'Prime') { $costs.Add($cost.Elapsed.TotalSeconds) }
        if ($row[0] -eq 'Candidate' -and $row[1] -eq 'Prime') {
            $projectedSeconds = $watch.Elapsed.TotalSeconds + 6 * ($costs | Measure-Object -Maximum).Maximum
            Write-Host "C1885 CONTROLLER projectedSeconds=$projectedSeconds limitSeconds=1800"
            if ($projectedSeconds -gt 1800) { throw 'priming projection exceeds 30 minutes; no measured expansion authorized' }
        }
    }
} catch { $failure = $_.Exception.Message }
finally {
    $watch.Stop()
    $remaining = 0
    foreach ($file in (Get-ChildItem -LiteralPath $EvidenceRoot -Recurse -Filter receipt.json)) {
        $receipt = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ($receipt.ExperimentId -cne $identity -or $receipt.Worktree -cne $worktree) { throw 'foreign receipt in owned experiment root' }
        if ($receipt.Pipe -and @(Get-C1885Compilers $receipt.Pipe).Count) {
            Stop-C1885PrivateCompiler (Get-Command dotnet).Source (Join-Path $receipt.Sdk 'Roslyn/bincore/VBCSCompiler.dll') $receipt.Pipe $worktree
        }
        if ($receipt.Pipe) { $remaining += @(Get-C1885Compilers $receipt.Pipe).Count }
    }
    @{ CollectionSeconds = $watch.Elapsed.TotalSeconds; Commands = @($outcomes); Failure = $failure; RemainingOwnedCompilers = $remaining } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceRoot 'controller.json')
    Write-Host "C1885 CONTROLLER seconds=$($watch.Elapsed.TotalSeconds) commands=$($outcomes.Count) remaining=$remaining failure=$failure"
    if ($remaining) { throw 'owned compiler residue after exact-pipe shutdown' }
}
if ($failure) { throw $failure }
