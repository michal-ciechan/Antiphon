# CARD-1704 angle B: git CLI versus LibGit2Sharp per-call benchmark, ready to run on the Windows desktop.
# Run from the repository root of a checkout (not while a Windows qualification run is live):
#   pwsh -NoProfile -File docs/investigations/2026-11-04-card-1704-libgit2sharp-bench/run-desktop-bench.ps1 -Iterations 100
# It clones the current checkout into a scratch directory, adds 5,000 packed refs and one detached worktree,
# builds the benchmark through the host build slot and prints median/p95 per operation. Nothing in the
# source checkout is modified. Delete the printed scratch directory afterwards (exact path).
# ASCII only.
param(
    [int]$Iterations = 100,
    [string]$ScratchRoot = (Join-Path ([System.IO.Path]::GetTempPath()) ("card1704-bench-" + [guid]::NewGuid().ToString('N').Substring(0, 8))),
    [string]$LsRemoteUrl = 'https://github.com/libgit2/libgit2sharp.git',
    [switch]$WithDefenderNote
)
$ErrorActionPreference = 'Stop'
$repoRoot = (git rev-parse --show-toplevel).Trim()
$here = $PSScriptRoot
$clone = Join-Path $ScratchRoot 'clone'
$wt = Join-Path $ScratchRoot 'wt'
$bench = Join-Path $ScratchRoot 'bench'
New-Item -ItemType Directory -Force -Path $bench | Out-Null
Write-Host "scratch: $ScratchRoot"

git clone -q --no-hardlinks $repoRoot $clone
if ($LASTEXITCODE -ne 0) { throw 'clone failed' }
$commits = git -C $clone rev-list -n 5000 HEAD
$lines = for ($i = 0; $i -lt $commits.Count; $i++) { 'create refs/bench/r{0:D4} {1}' -f $i, $commits[$i] }
$lines | git -C $clone update-ref --stdin
git -C $clone pack-refs --all
git -C $clone worktree add -q --detach $wt HEAD~10
Write-Host ("refs: " + (git -C $clone for-each-ref | Measure-Object).Count)

Copy-Item (Join-Path $here 'GitBench.csproj.txt') (Join-Path $bench 'GitBench.csproj')
Copy-Item (Join-Path $here 'GitBench.Program.cs.txt') (Join-Path $bench 'Program.cs')

$slot = Join-Path $repoRoot 'scripts/build-slot.ps1'
Push-Location $bench
try {
    pwsh -NoProfile -File $slot -Label card1704-bench-build -- dotnet build -c Release -p:UseAppHost=false
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
    $out = Join-Path $ScratchRoot 'desktop-run.txt'
    pwsh -NoProfile -File $slot -Label card1704-bench-run -- dotnet bin/Release/net9.0/GitBench.dll $clone $wt $Iterations $LsRemoteUrl | Tee-Object -FilePath $out
    Write-Host "results: $out"
}
finally { Pop-Location }

# Also record the environment that dominates Windows process start-up.
Write-Host ("git: " + (git --version))
Write-Host ("cpu load now (%): " + [math]::Round((Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average, 1))
try { $mp = Get-MpPreference; Write-Host ("Defender realtime disabled: " + $mp.DisableRealtimeMonitoring + "; exclusions: " + ($mp.ExclusionPath -join ';')) } catch { Write-Host 'Defender preferences unreadable' }
Write-Host "cleanup: git -C '$repoRoot' worktree prune is not needed (the worktree belongs to the scratch clone); delete $ScratchRoot by exact path."
