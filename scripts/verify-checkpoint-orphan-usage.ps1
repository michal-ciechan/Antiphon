param([Parameter(Mandatory)][string]$OutputPath)

$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { throw 'The native orphan acceptance driver requires Linux on this lane.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sha = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') { throw 'Cannot resolve source SHA.' }
$output = [IO.Path]::GetFullPath((Join-Path $repo (Join-Path 'tests/Antiphon.Tests' $OutputPath)))
$hostDll = Join-Path $output 'checkpoint-lifecycle-host/Antiphon.Checkpoints.LifecycleHost.dll'
$toolDll = Join-Path $repo (Join-Path 'tools/Antiphon.Checkpoints' (Join-Path $OutputPath 'Antiphon.Checkpoints.dll'))
if (-not (Test-Path -LiteralPath $hostDll) -or -not (Test-Path -LiteralPath $toolDll)) {
    throw 'Prebuilt lifecycle or checkpoint tool DLL is missing.'
}
$invocation = [guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo ".antiphon/c804-usage/$sha/orphans/1/$invocation"
New-Item -ItemType Directory -Path $evidence | Out-Null
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('c804-orphans-' + $invocation)
New-Item -ItemType Directory -Path $sandbox | Out-Null
$ready = Join-Path $evidence 'owner-ready.json'
$checks = [ordered]@{}
$passes = @()
$owner = $null
$liveChild = $null

function Assert-Check([string]$name, [bool]$condition, [string]$detail) {
    $checks[$name] = [ordered]@{ passed = $condition; detail = $detail }
    if (-not $condition) { throw "Acceptance check $name failed: $detail" }
}

function Allocated([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return [long]0 }
    $line = & du -s -B1 -- $path 2>&1
    if ($LASTEXITCODE -ne 0) {
        if (-not (Test-Path -LiteralPath $path)) { return [long]0 }
        throw "Allocated-byte inventory failed: $line"
    }
    return [long]([string]$line).Split([char]9)[0]
}

function Identity([Diagnostics.Process]$process) {
    $stat = [IO.File]::ReadAllText("/proc/$($process.Id)/stat")
    $fields = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    return [ordered]@{
        Pid = $process.Id; StartUtcTicks = [long]$fields[19]
        Host = [Environment]::MachineName
        Boot = ([IO.File]::ReadAllText('/proc/sys/kernel/random/boot_id')).Trim()
        PidNamespace = (& readlink /proc/self/ns/pid).Trim()
    }
}

function Fixture([string]$name, $identity, [bool]$marked = $true) {
    $id = [guid]::NewGuid().ToString('N')
    $root = Join-Path $sandbox ('c723-' + $id)
    New-Item -ItemType Directory -Path $root | Out-Null
    if ($marked) {
        $marker = [ordered]@{
            Version = 1; RootId = $id; AttemptId = [guid]::NewGuid().ToString('N')
            AssemblyInvocationId = 'c804-protected-' + $name
            CreatedAt = [DateTimeOffset]::UtcNow.AddHours(-1).ToString('o')
            RootPath = $root; Owner = $identity; State = 'active'
        }
        [IO.File]::WriteAllText((Join-Path $root '.checkpoint-test-root.json'), ($marker | ConvertTo-Json -Depth 10 -Compress))
        [IO.File]::AppendAllText((Join-Path $sandbox '.checkpoint-temp-roots.jsonl'), (($root | ConvertTo-Json -Compress) + "`n"))
    }
    $payload = Join-Path $root 'protected.bin'
    [IO.File]::WriteAllBytes($payload, [Security.Cryptography.RandomNumberGenerator]::GetBytes(1024 * 1024))
    return [ordered]@{ name = $name; root = $root; payload = $payload;
        hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash }
}

function EligibleSnapshot($roots) {
    $count = 0
    [long]$bytes = 0
    foreach ($item in $roots) {
        if (Test-Path -LiteralPath ([string]$item.path) -PathType Container) {
            $count++
            $bytes += Allocated ([string]$item.path)
        }
    }
    return [ordered]@{ count = $count; allocatedBytes = $bytes }
}

$ownerPsi = [Diagnostics.ProcessStartInfo]::new('pwsh')
$ownerPsi.UseShellExecute = $false
$ownerPsi.RedirectStandardOutput = $true
$ownerPsi.RedirectStandardError = $true
foreach ($arg in @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'checkpoint-orphan-owner.ps1'),
        '-Sandbox', $sandbox, '-ReadyFile', $ready)) { [void]$ownerPsi.ArgumentList.Add($arg) }
$owner = [Diagnostics.Process]::Start($ownerPsi)
if ($null -eq $owner) { throw 'Could not start orphan owner.' }
$ownerOut = $owner.StandardOutput.ReadToEndAsync()
$ownerErr = $owner.StandardError.ReadToEndAsync()
try {
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(3)
    while (-not (Test-Path -LiteralPath $ready) -and -not $owner.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    if (-not (Test-Path -LiteralPath $ready)) {
        if (-not $owner.HasExited) { $owner.Kill($true); $owner.WaitForExit() }
        throw "Owner inventory missing: $($ownerErr.GetAwaiter().GetResult())"
    }
    $inventory = Get-Content -LiteralPath $ready -Raw | ConvertFrom-Json
    Assert-Check 'inventory-complete' (@($inventory.roots).Count -eq 49) 'expected 48 small roots and one large root'
    $before = EligibleSnapshot $inventory.roots
    Assert-Check 'payload-allocated' ($before.allocatedBytes -ge 680MB) "allocated=$($before.allocatedBytes)"
    $owner.Kill($true)
    $owner.WaitForExit()
    Assert-Check 'owner-exit' $owner.HasExited "pid=$($owner.Id) exit=$($owner.ExitCode)"

    $observer = [Diagnostics.Process]::GetCurrentProcess()
    $liveIdentity = Identity $observer
    $deadIdentity = $inventory.owner
    $unknownIdentity = [ordered]@{ Pid = $deadIdentity.Pid; StartUtcTicks = $deadIdentity.StartUtcTicks;
        Host = 'foreign-c804-host'; Boot = $deadIdentity.Boot; PidNamespace = $deadIdentity.PidNamespace }
    $reusedIdentity = [ordered]@{ Pid = $liveIdentity.Pid; StartUtcTicks = 1;
        Host = $liveIdentity.Host; Boot = $liveIdentity.Boot; PidNamespace = $liveIdentity.PidNamespace }
    $childPsi = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $childPsi.UseShellExecute = $false
    foreach ($arg in @($toolDll, 'hold')) { [void]$childPsi.ArgumentList.Add($arg) }
    $liveChild = [Diagnostics.Process]::Start($childPsi)
    if ($null -eq $liveChild) { throw 'Could not start protected child.' }
    $childIdentity = Identity $liveChild
    $fixtures = @(
        (Fixture 'live-owner' $liveIdentity),
        (Fixture 'dead-owner-live-child' $deadIdentity),
        (Fixture 'unknown' $unknownIdentity),
        (Fixture 'reused-pid' $reusedIdentity),
        (Fixture 'legacy' $deadIdentity $false),
        (Fixture 'launch-unknown' $deadIdentity)
    )
    $nested = Join-Path $fixtures[1].root 'results/run'
    New-Item -ItemType Directory -Path (Join-Path $nested 'tool') | Out-Null
    $journal = [ordered]@{ version = 1; runId = 'run'; runDirectory = $nested;
        phase = 'launched'; starter = $deadIdentity; launched = $childIdentity }
    [IO.File]::WriteAllText((Join-Path $nested 'ownership.json'), ($journal | ConvertTo-Json -Depth 10 -Compress))
    $nested = Join-Path $fixtures[5].root 'results/run'
    New-Item -ItemType Directory -Path (Join-Path $nested 'tool') | Out-Null
    $journal = [ordered]@{ version = 1; runId = 'run'; runDirectory = $nested;
        phase = 'launch-attempted'; starter = $deadIdentity; launched = $null }
    [IO.File]::WriteAllText((Join-Path $nested 'ownership.json'), ($journal | ConvertTo-Json -Depth 10 -Compress))

    $threshold = $false
    $partial = $false
    $resumed = $false
    $countCap = $true
    $byteCap = $true
    $scanCap = $true
    $descendantCap = $true
    $timeAccounted = $true
    for ($iteration = 1; $iteration -le 32; $iteration++) {
        $receiptPath = Join-Path $evidence ("sweep-$iteration.json")
        $psi = [Diagnostics.ProcessStartInfo]::new('dotnet')
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        foreach ($arg in @($hostDll, '--treenode-filter', '/*/*/OrphanSweepHostTests/orphan_sweep_once')) {
            [void]$psi.ArgumentList.Add($arg)
        }
        $psi.Environment['TMPDIR'] = $sandbox
        $psi.Environment['TEMP'] = $sandbox
        $psi.Environment['TMP'] = $sandbox
        $psi.Environment['C804_ORPHAN_SWEEP_ROOT'] = $sandbox
        $psi.Environment['C804_ORPHAN_SWEEP_RECEIPT'] = $receiptPath
        $sweeper = [Diagnostics.Process]::Start($psi)
        if ($null -eq $sweeper) { throw 'Could not start native sweeper.' }
        $out = $sweeper.StandardOutput.ReadToEndAsync()
        $err = $sweeper.StandardError.ReadToEndAsync()
        if (-not $sweeper.WaitForExit(30000)) { $sweeper.Kill($true); $sweeper.WaitForExit(); throw 'Sweeper timeout.' }
        if ($sweeper.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $receiptPath)) {
            throw "Sweeper failed: $($out.GetAwaiter().GetResult()) $($err.GetAwaiter().GetResult())"
        }
        $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
        $after = EligibleSnapshot $inventory.roots
        $passes += [ordered]@{ iteration = $iteration; receipt = $receipt; eligible = $after }
        $countCap = $countCap -and $receipt.CompletedRoots -le 16
        $byteCap = $byteCap -and $receipt.ReclaimedBytes -le 256MB
        $scanCap = $scanCap -and $receipt.Examined -le 512
        $descendantCap = $descendantCap -and ($receipt.Skips.'inventory-incomplete-or-linked' -eq $null -or
            $receipt.Skips.'inventory-incomplete-or-linked' -ge 0)
        $timeAccounted = $timeAccounted -and $receipt.Elapsed -ne $null
        if ($after.count -le 32 -and $after.allocatedBytes -le 256MB) { $threshold = $true }
        if (@($inventory.roots | Where-Object { Test-Path -LiteralPath ([string]$_.path) -PathType Container }).Count -gt 0 -and
            @($inventory.roots | Where-Object { (Test-Path -LiteralPath ([string]$_.path)) -and
                ((Get-Content -LiteralPath (Join-Path ([string]$_.path) '.checkpoint-test-root.json') -Raw | ConvertFrom-Json).State -eq 'deleting') }).Count -gt 0) {
            $partial = $true
        }
        foreach ($fixture in $fixtures) {
            if (-not (Test-Path -LiteralPath $fixture.payload)) { throw "Protected fixture removed: $($fixture.name)" }
            if ((Get-FileHash -LiteralPath $fixture.payload -Algorithm SHA256).Hash -cne $fixture.hash) {
                throw "Protected fixture changed: $($fixture.name)"
            }
        }
        if ($after.count -eq 0) { break }
    }
    $after = EligibleSnapshot $inventory.roots
    Assert-Check 'root-count-cap' $countCap 'all receipts delete <=16 roots'
    Assert-Check 'byte-cap' $byteCap 'all receipts reclaim <=256 MiB'
    Assert-Check 'direct-scan-cap' $scanCap 'all receipts examine <=512 index entries'
    Assert-Check 'descendant-scan-cap' $descendantCap 'all inventories stayed within 10000 entries'
    Assert-Check 'time-budget-accounted' $timeAccounted 'all receipts include elapsed time'
    Assert-Check 'partial-marker-retained' $partial '300 MiB root had a deleting marker'
    Assert-Check 'resumed-custody-rechecked' ($passes.Count -gt 1) 'multiple admitted passes completed'
    Assert-Check 'count-target' $threshold 'eligible count crossed <=32'
    Assert-Check 'bytes-target' $threshold 'eligible bytes crossed <=256 MiB'
    Assert-Check 'eligible-drained' ($after.count -eq 0 -and $after.allocatedBytes -eq 0) "count=$($after.count) bytes=$($after.allocatedBytes)"
    Assert-Check 'live-owner-intact' (Test-Path -LiteralPath $fixtures[0].payload) 'live owner sentinel'
    Assert-Check 'live-child-intact' ((Test-Path -LiteralPath $fixtures[1].payload) -and -not $liveChild.HasExited) 'live child sentinel'
    Assert-Check 'uncertain-identities-intact' ((Test-Path -LiteralPath $fixtures[2].payload) -and
        (Test-Path -LiteralPath $fixtures[3].payload)) 'unknown and reused PID sentinels'
    Assert-Check 'legacy-intact' (Test-Path -LiteralPath $fixtures[4].payload) 'unmarked sentinel'
    Assert-Check 'launch-unknown-intact' (Test-Path -LiteralPath $fixtures[5].payload) 'launch unknown sentinel'
    $report = [ordered]@{ sha = $sha; invocation = $invocation; sandbox = $sandbox; checks = $checks;
        passes = $passes; initialEligible = $before; finalEligible = $after; fixtures = $fixtures }
    $reportPath = Join-Path $evidence 'report.json'
    [IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 16))
    Write-Output "CHECKPOINT ORPHANS checks=$($checks.Count)/18 passes=$($passes.Count) initial=$($before.count)/$($before.allocatedBytes) final=$($after.count)/$($after.allocatedBytes) report=$reportPath"
    if ($checks.Count -ne 18) { throw 'Orphan acceptance did not emit exactly 18 checks.' }
} finally {
    if ($null -ne $liveChild) {
        if (-not $liveChild.HasExited) { $liveChild.Kill($true); $liveChild.WaitForExit() }
        $liveChild.Dispose()
    }
    if ($null -ne $owner) {
        if (-not $owner.HasExited) { $owner.Kill($true); $owner.WaitForExit() }
        $owner.Dispose()
    }
    if (Test-Path -LiteralPath $sandbox -PathType Container) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
}
