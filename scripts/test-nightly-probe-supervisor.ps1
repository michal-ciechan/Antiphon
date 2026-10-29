#requires -Version 7.0
param([string]$Case = 'C1375_DeadlineKillsAndJoinsTree', [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
if (-not $IsLinux) {
    Write-Host 'SKIP C1375_DeadlineKillsAndJoinsTree: CARD-1375 native expiry qualification requires Linux.'
    exit 0
}
if ($Case -cne 'C1375_DeadlineKillsAndJoinsTree') { throw "Unknown case $Case" }
. (Join-Path $PSScriptRoot 'lib/nightly-probe-supervisor.ps1')
. (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')

$nonce = [guid]::NewGuid().ToString('N')
$ownedDirectory = Join-Path $ResultsDirectory $nonce
$null = New-Item -ItemType Directory -Path $ownedDirectory
$evidenceDirectory = Join-Path $PSScriptRoot "../.antiphon/c1375-evidence/supervisor/$nonce"
$null = New-Item -ItemType Directory -Path $evidenceDirectory -Force
$observed = @{}
$state = @{
    phase = 0; ready = $false; noEarlyExpiry = $false
    readiness = [Diagnostics.Stopwatch]::StartNew()
    expiry = [Diagnostics.Stopwatch]::new()
}
$receipt = $null
$beforeFallback = @()
$fallbackNeeded = $false
$fallbackErrors = [Collections.Generic.List[string]]::new()
$fixtureError = $null

function Read-OwnedIdentity {
    param([string]$Role)
    if ($observed.ContainsKey($Role)) { return }
    $path = Join-Path $ownedDirectory "$Role.json"
    if (-not [IO.File]::Exists($path)) { return }
    $identity = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    if ($identity.nonce -cne $nonce -or $identity.role -cne $Role) { throw 'Fixture nonce/role mismatch.' }
    $handle = [Diagnostics.Process]::GetProcessById($identity.pid)
    $identity | Add-Member Process $handle
    $observed[$Role] = $identity
    # /proc start ticks are the shared kernel identity. Process.StartTime converts
    # boot-relative time to wall time separately in each process and can differ.
    $stat = [IO.File]::ReadAllText("/proc/$($identity.pid)/stat")
    $fields = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ')
    if ($fields[19] -cne $identity.start -or $handle.HasExited) {
        throw "Fixture $Role start identity mismatch."
    }
}

function Test-OwnedAlive {
    param($Identity)
    # Independent native identity check; do not use the supervisor's alive helper.
    try {
        $stat = [IO.File]::ReadAllText("/proc/$($Identity.pid)/stat")
        $fields = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ')
        return ($fields[19] -ceq $Identity.start -and $fields[0] -notin @('Z', 'X') -and
            -not $Identity.Process.HasExited)
    } catch [IO.IOException] { return $false }
}

$elapsedProvider = {
    param($Root, $Descendants)
    if ($state.phase -eq 0) {
        foreach ($role in @('root', 'child', 'grandchild')) { Read-OwnedIdentity $role }
        if ($state.readiness.ElapsedMilliseconds -gt 5000) { throw 'Fixture readiness exceeded five seconds.' }
        if ($observed.Count -ne 3) { return 0 }
        foreach ($identity in $observed.Values) {
            if (-not (Test-OwnedAlive $identity)) { throw 'Fixture process exited before readiness.' }
        }
        if ($observed.root.pid -ne $Root.Pid -or $observed.root.start -cne $Root.Start -or
            $observed.child.parentPid -ne $observed.root.pid -or
            $observed.grandchild.parentPid -ne $observed.child.pid) { throw 'Fixture tree identity mismatch.' }
        $expected = @('child', 'grandchild' | ForEach-Object { "$($observed[$_].pid):$($observed[$_].start)" } | Sort-Object)
        $actual = @($Descendants | ForEach-Object { "$($_.Pid):$($_.Start)" } | Sort-Object)
        if (($actual -join ',') -cne ($expected -join ',')) { return 0 }
        $state.ready = $true
        $state.phase = 1
        return 29999
    }
    if ($state.phase -ne 1) { throw 'Execution continued after injected expiry.' }
    $state.noEarlyExpiry = @($observed.Values | Where-Object { -not (Test-OwnedAlive $_) }).Count -eq 0
    $state.phase = 2
    $state.expiry.Start()
    return 30001
}

try {
    $receipt = Invoke-NightlyProbeSupervisor -FilePath (Join-Path $PSHOME 'pwsh') `
        -ArgumentList @('-NoProfile', '-NonInteractive', '-File',
            (Join-Path $PSScriptRoot 'fixtures/nightly/c1375-owned-tree.ps1'),
            '-Mode', 'root', '-Directory', $ownedDirectory, '-Nonce', $nonce) `
        -WorkingDirectory $ownedDirectory -ElapsedMillisecondsProvider $elapsedProvider
    if (-not $state.ready) { throw "Fixture readiness failed: $($receipt.errors -join '; ')" }

    # Snapshot exit/join observations BEFORE the independent finally can rescue anything.
    foreach ($role in @('root', 'child', 'grandchild')) {
        $identity = $observed[$role]
        $exited = $identity.Process.HasExited
        $joined = $identity.Process.WaitForExit([int][Math]::Max(0, 5000 - $state.expiry.ElapsedMilliseconds))
        $beforeFallback += [pscustomobject]@{
            role = $role; pid = $identity.pid; start = $identity.start
            exited = $exited; joined = $joined; alive = (Test-OwnedAlive $identity)
        }
    }
    $state.expiry.Stop()
    $observations = [ordered]@{
        nonce = $nonce; supervisor = $receipt; beforeFallback = $beforeFallback
        elapsedFromExpiryMs = $state.expiry.ElapsedMilliseconds
        ready = $state.ready; noEarlyExpiry = $state.noEarlyExpiry
    }
    $observations | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $evidenceDirectory 'before-fallback.json')

    Assert-C487 $state.ready 'C1375 ready identities'
    Assert-C487 ($state.noEarlyExpiry -and $state.phase -eq 2) 'C1375 no early expiry'
    Assert-C487 $receipt.expired 'C1375 expired'
    foreach ($row in $beforeFallback) {
        $reported = if ($row.role -eq 'root') {
            $receipt.rootJoined -and $receipt.pid -eq $row.pid -and $receipt.start -ceq $row.start
        } else {
            $matches = @($receipt.descendants | Where-Object { $_.pid -eq $row.pid -and $_.start -ceq $row.start })
            $matches.Count -eq 1 -and $matches[0].joined
        }
        Assert-C487 ($row.exited -and $row.joined -and -not $row.alive -and $reported) `
            "C1375 $($row.role) exited/joined" ($row | ConvertTo-Json -Compress)
    }
    $pipes = $receipt.stdoutDrained -and $receipt.stderrDrained
    foreach ($role in @('root', 'child', 'grandchild')) {
        $pipes = $pipes -and $receipt.stdout.Contains("C1375 $role stdout $nonce") -and
            $receipt.stderr.Contains("C1375 $role stderr $nonce")
    }
    Assert-C487 $pipes 'C1375 both EOFs'
    Assert-C487 ($receipt.cleanupElapsedMs -le 5000 -and $state.expiry.ElapsedMilliseconds -le 5000 -and
        $receipt.cleanupSuccess -and $receipt.descendants.Count -eq 2) `
        'C1375 shared cleanup elapsed bound' "supervisor=$($receipt.cleanupElapsedMs)ms observed=$($state.expiry.ElapsedMilliseconds)ms"
} catch {
    $fixtureError = $_.Exception.Message
    @{
        error = $fixtureError; supervisor = $receipt; phase = $state.phase
        observedRoles = @($observed.Keys); readinessElapsedMs = $state.readiness.ElapsedMilliseconds
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'fixture-failure.json')
    Write-C487Fail 'C1375 fixture/observation failure' $fixtureError
} finally {
    # The ScriptHarness owner also has an independent 20-second watchdog and group
    # sweep, covering a stuck supervisor and partial readiness/spawn races.
    $fallbackClock = [Diagnostics.Stopwatch]::StartNew()
    foreach ($role in @('root', 'child', 'grandchild')) {
        try { Read-OwnedIdentity $role } catch { $fallbackErrors.Add($_.Exception.Message) }
    }
    foreach ($role in @('grandchild', 'child', 'root')) {
        if (-not $observed.ContainsKey($role)) { continue }
        $identity = $observed[$role]
        try {
            if (Test-OwnedAlive $identity) {
                $fallbackNeeded = $true
                $identity.Process.Kill()
            }
            if (-not $identity.Process.WaitForExit([int][Math]::Max(0, 5000 - $fallbackClock.ElapsedMilliseconds))) {
                $fallbackErrors.Add("$role fallback join failed")
            }
        } catch { $fallbackErrors.Add($_.Exception.Message) }
    }
    foreach ($identity in $observed.Values) { $identity.Process.Dispose() }
    $fallback = @{
        needed = $fallbackNeeded; errors = $fallbackErrors.ToArray()
        elapsedMs = $fallbackClock.ElapsedMilliseconds; fixtureError = $fixtureError
    }
    $fallback | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'fallback.json')
    Write-Host "C1375 EVIDENCE $evidenceDirectory"
}
Assert-C487 (-not $fallbackNeeded -and $fallbackErrors.Count -eq 0) 'C1375 no fallback needed' ($fallback | ConvertTo-Json -Compress)
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows 9
