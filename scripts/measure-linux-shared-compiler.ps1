#requires -Version 7.0
# CARD-1885. One invocation, one foreground build, one outer lease. No hidden build loop.
param(
    [Parameter(Mandatory)][ValidateSet('Baseline', 'Candidate')][string]$Mode,
    [Parameter(Mandatory)][ValidateSet('Restore', 'Prime', 'Measured', 'Concurrent')][string]$Phase,
    [Parameter(Mandatory)][ValidateRange(0, 5)][int]$Sample,
    [string]$Worktree,
    [string]$EvidenceRoot,
    [string]$Project = 'Antiphon.sln',
    [string]$ExperimentId = 'measurement-58c3a29f',
    [string]$CompilerCacheRoot
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsLinux) { throw 'Linux native measurement required' }
if (-not $Worktree) { $Worktree = Split-Path $PSScriptRoot -Parent }
$Worktree = [IO.Path]::GetFullPath($Worktree)
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $Worktree '.antiphon/c1885/performance' }
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$grant = 0
if (-not [int]::TryParse($env:ANTIPHON_BUILD_SLOT_MAXCPUCOUNT, [ref]$grant) -or $grant -lt 1) { throw 'one outer lease and its typed CPU grant are required' }
if (($Phase -in @('Restore', 'Prime') -and $Sample -ne 0) -or ($Phase -eq 'Measured' -and $Sample -notin @(1, 2, 3)) -or
    ($Phase -eq 'Concurrent' -and ($Sample -notin @(4, 5) -or $Mode -ne 'Candidate'))) { throw 'invocation is outside the bounded experiment inventory' }
if (-not (Test-Path -LiteralPath (Join-Path $Worktree $Project))) { throw 'measurement project does not exist' }
if ($ExperimentId -notmatch '^[a-zA-Z0-9-]+$') { throw 'invalid private experiment identity' }
if ($CompilerCacheRoot -and -not [IO.Path]::IsPathRooted($CompilerCacheRoot)) { throw 'compiler cache root must be absolute' }
if (-not ('Antiphon.Build.BuildDriverEnvironment' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'lib/BuildDriverEnvironment.cs') }
. (Join-Path $PSScriptRoot 'lib/linux-compiler-evidence.ps1')
$sourceSha = (& git -C $Worktree rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'cannot bind source SHA' }
& git -C $Worktree diff --quiet HEAD --
if ($LASTEXITCODE -ne 0) { throw 'measurement source is dirty' }
$runRoot = Join-Path $EvidenceRoot ("$Mode-$Phase-$Sample-" + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($runRoot)
$start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
$start.UseShellExecute = $false; $start.WorkingDirectory = $Worktree
$start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
# This owned experimental child bypasses automatic policy selection. Both paths
# explicitly install a private toolset/pipe; neither can use the live SDK pipe.
[void]$start.Environment.Remove('ANTIPHON_LINUX_COMPILER_POLICY')
if ($CompilerCacheRoot) { $start.Environment['XDG_CACHE_HOME'] = $CompilerCacheRoot }
$tokens = if ($Phase -eq 'Restore') {
    @('restore', $Project, '-p:UseAppHost=false', "-maxcpucount:$grant", '--nologo')
} else {
    @('build', $Project, '--configuration', 'Debug', '--no-incremental', '--no-restore', '-p:UseAppHost=false',
        "-maxcpucount:$grant", '-nodeReuse:false', '--nologo')
}
foreach ($token in $tokens) { $start.ArgumentList.Add($token) }
$roslynLog = Join-Path $runRoot 'roslyn.log'
$start.Environment['RoslynCommandLineLogFile'] = $roslynLog
$diagnostic = $null; $pipe = $null; $sdk = $null; $cpus = $null
$prepare = [Diagnostics.Stopwatch]::StartNew()
if ($Phase -ne 'Restore') {
    # The same 600-second temporary lifetime in both serial modes prevents an
    # opposite-mode build from silently turning a warm sample into a respawn.
    $idleSeconds = if ($Phase -eq 'Concurrent') { 120 } else { 600 }
    $diagnostic = [Antiphon.Build.LinuxCompilerToolset]::Prepare($start, $grant, $idleSeconds, $ExperimentId, ($Mode -eq 'Candidate'))
    Write-Host $diagnostic
    $pipe = [regex]::Match($diagnostic, ' pipe=(\S+)').Groups[1].Value
    $sdk = [regex]::Match($diagnostic, ' sdk=(\S+)').Groups[1].Value
    $cpus = [regex]::Match($diagnostic, ' cpus=(\S+)').Groups[1].Value
}
$prepare.Stop()
$clk = [double]([Antiphon.Build.LinuxCompilerToolset]::Probe((Get-Command getconf).Source, @('CLK_TCK'), $Worktree, $start.EnvironmentVariables))
$records = [Collections.Generic.List[object]]::new()
$previous = @{}; $known = @{}; $cumulativeTicks = 0L; $peakRss = 0L; $threadEscape = $false
$clock = [Diagnostics.Stopwatch]::new()
$initial = @(if ($pipe) { Get-C1885Compilers $pipe })
foreach ($compiler in $initial) { $previous["$($compiler.Pid):$($compiler.Start)"] = $compiler.Ticks }
$loadStart = [IO.File]::ReadAllText('/proc/loadavg').Trim()
$memoryStart = [IO.File]::ReadAllText('/proc/meminfo')
$driverAffinity = [Antiphon.Build.LinuxCompilerToolset]::StatusValue('/proc/self/status', 'Cpus_allowed_list')
$shared = 0; $fallback = $null
$process = $null; $exitCode = $null; $failure = $null; $bodySeconds = $null; $idleExitSeconds = $null
$startedAtUtc = [DateTimeOffset]::UtcNow
$outTask = $null; $errTask = $null
try {
    $clock.Start()
    $process = [Diagnostics.Process]::Start($start)
    $outTask = $process.StandardOutput.ReadToEndAsync(); $errTask = $process.StandardError.ReadToEndAsync()
    do {
        $now = $clock.Elapsed.TotalSeconds
        $current = if ($pipe) { @(Get-C1885Compilers $pipe) } else { @() }
        $rss = 0L
        foreach ($compiler in $current) {
            $identity = "$($compiler.Pid):$($compiler.Start)"
            $known[$identity] = $compiler
            $delta = if ($previous.ContainsKey($identity)) { $compiler.Ticks - $previous[$identity] } else { $compiler.Ticks }
            if ($delta -lt 0) { throw 'non-monotonic private compiler accounting' }
            $cumulativeTicks += $delta; $previous[$identity] = $compiler.Ticks
            $rss += $compiler.RssBytes
            if ($Mode -eq 'Candidate') {
                $chosen = Get-C1885Set $cpus
                foreach ($threadSet in $compiler.Threads) {
                    foreach ($cpu in (Get-C1885Set $threadSet)) { if (-not $chosen.Contains($cpu)) { $threadEscape = $true } }
                }
            }
        }
        $peakRss = [Math]::Max($peakRss, $rss)
        $records.Add(@{ Seconds = $now; CumulativeTicks = $cumulativeTicks; RssBytes = $rss; Compilers = $current;
            DriverCpuSeconds = $process.TotalProcessorTime.TotalSeconds })
        if ($clock.Elapsed.TotalMinutes -gt 10) { throw 'one build exceeded the provisional ten-minute bound; do not expand collection' }
        $ended = $process.WaitForExit(1000)
        if ($ended -and $null -eq $bodySeconds) { $bodySeconds = $clock.Elapsed.TotalSeconds }
        # A small standard project can finish before a complete CPU minute exists.
        # Continue observation, including explicitly reported idle, without adding
        # another build or charging the idle interval to build-body wall time.
        if ($ended -and $pipe -and $clock.Elapsed.TotalSeconds -lt 61) { Start-Sleep -Milliseconds 1000 }
    } while (-not $ended -or ($pipe -and $clock.Elapsed.TotalSeconds -lt 61))
    $clock.Stop()
    $exitCode = $process.ExitCode
    if (-not $outTask.Wait(3000) -or -not $errTask.Wait(3000)) { throw 'measurement output drain deadline' }
    if ($exitCode -ne 0) { throw "standard Linux project command failed with exit $exitCode" }
    if ($Phase -ne 'Restore') {
        $log = [IO.File]::ReadAllText($roslynLog)
        $shared = ([regex]::Matches($log, 'CompilerServer: server - server processed compilation')).Count
        $fallback = $log -match 'CompilerServer: (?!server - server processed compilation)'
        if ($shared -lt 2 -or $fallback) { throw 'shared compiler acceptance failed: insufficient responses or fallback' }
        if ($known.Count -ne 1) { throw 'compiler warm identity changed or was not observed' }
        if ($Phase -eq 'Measured' -and $initial.Count -ne 1) { throw 'timing sample was not warm; no replacement attempt authorized' }
        if ($Phase -eq 'Concurrent') {
            if ($bodySeconds -lt 60) { throw 'concurrent build body did not provide the required active minute' }
            $idleWatch = [Diagnostics.Stopwatch]::StartNew()
            while (@(Get-C1885Compilers $pipe).Count -gt 0 -and $idleWatch.Elapsed.TotalSeconds -lt 130) { Start-Sleep -Milliseconds 200 }
            if (@(Get-C1885Compilers $pipe).Count -gt 0) { throw 'production 120-second idle release failed' }
            $idleExitSeconds = $idleWatch.Elapsed.TotalSeconds
        }
    }
} catch {
    $failure = $_.Exception.Message
} finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(1000) }
        $process.Dispose()
    }
    if ($outTask -and $outTask.IsCompletedSuccessfully) { [IO.File]::WriteAllText((Join-Path $runRoot 'build.stdout.log'), $outTask.Result) }
    if ($errTask -and $errTask.IsCompletedSuccessfully) { [IO.File]::WriteAllText((Join-Path $runRoot 'build.stderr.log'), $errTask.Result) }
    # A failed serial body must not leave its private server behind. Successful
    # serial samples retain their owned warm pipe until the controller finishes.
    # Concurrent children never shutdown their shared pipe while another body may run.
    if ($failure -and $pipe -and $Phase -ne 'Concurrent' -and @(Get-C1885Compilers $pipe).Count -gt 0) {
        Stop-C1885PrivateCompiler (Get-Command dotnet).Source (Join-Path $sdk 'Roslyn/bincore/VBCSCompiler.dll') $pipe $Worktree
    }
    $rolling = @()
    for ($last = 0; $last -lt $records.Count; $last++) {
        for ($first = $last - 1; $first -ge 0; $first--) {
            $seconds = $records[$last].Seconds - $records[$first].Seconds
            if ($seconds -ge 60) { $rolling += ($records[$last].CumulativeTicks - $records[$first].CumulativeTicks) / $clk / $seconds; break }
        }
    }
    $maxMinute = if ($rolling.Count) { ($rolling | Measure-Object -Maximum).Maximum } else { $null }
    $sourceEnd = (& git -C $Worktree rev-parse HEAD).Trim()
    & git -C $Worktree diff --quiet HEAD --
    $cleanEnd = $LASTEXITCODE -eq 0 -and $sourceEnd -ceq $sourceSha
    $receipt = @{ SourceSha = $sourceSha; SourceEndSha = $sourceEnd; SourceClean = $cleanEnd; Worktree = $Worktree; Project = $Project; ExperimentId = $ExperimentId;
        Mode = $Mode; Phase = $Phase; Sample = $Sample; Grant = $grant; Diagnostic = $diagnostic; Pipe = $pipe; Sdk = $sdk;
        Command = @($start.ArgumentList); StartedAtUtc = $startedAtUtc; Host = [Environment]::MachineName;
        PrepareSeconds = $prepare.Elapsed.TotalSeconds; BuildBodySeconds = $bodySeconds; ExitCode = $exitCode;
        CompilerObservationSeconds = $clock.Elapsed.TotalSeconds; PostBuildObservationSeconds = [Math]::Max(0, $clock.Elapsed.TotalSeconds - $bodySeconds);
        Failure = $failure; SharedResponses = $shared; Fallback = $fallback; LoadStart = $loadStart; LoadEnd = [IO.File]::ReadAllText('/proc/loadavg').Trim(); MemoryStart = $memoryStart;
        DriverAffinity = $driverAffinity; ChosenCpus = $cpus; ClockTicksPerSecond = $clk; WarmAtStart = $initial; OwnedIdentities = @($known.Keys);
        PeakAggregateRssBytes = $peakRss; MaximumFullMinuteCores = $maxMinute; ThreadEscape = $threadEscape; IdleExitSeconds = $idleExitSeconds;
        Unknown = @('full driver/server GC CPU', 'exit-interval ticks if a server died between samples', 'unrelated default compiler context CPU', 'per-component restore timings') }
    $records | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runRoot 'samples.json')
    $receipt | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runRoot 'receipt.json')
    Write-Host "C1885 PERFORMANCE mode=$Mode phase=$Phase sample=$Sample builds=$([int]($Phase -ne 'Restore')) exit=$exitCode body=$bodySeconds peakRss=$peakRss minuteCores=$maxMinute evidence=$runRoot"
}
if ($failure -or -not $cleanEnd -or $threadEscape -or $peakRss -gt 8GB -or ($null -ne $maxMinute -and $Mode -eq 'Candidate' -and $maxMinute -gt [Math]::Min(6, $grant))) {
    if ($failure) { Write-Error $failure }
    exit 1
}
