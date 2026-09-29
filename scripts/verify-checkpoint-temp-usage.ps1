param(
    [ValidateSet('Namespace', 'Full', 'Orphans')][string]$Phase,
    [ValidateRange(1, 2)][int]$Pass,
    [string]$OutputPath = 'bin-c804-final/'
)

$ErrorActionPreference = 'Stop'
if (-not $Phase) { throw 'Phase is required.' }
if ($Phase -eq 'Orphans') {
    if ($Pass -ne 1) { throw 'Orphan acceptance has one pass.' }
    & (Join-Path $PSScriptRoot 'verify-checkpoint-orphan-usage.ps1') -OutputPath $OutputPath
    return
}

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sha = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') { throw 'Cannot resolve source SHA.' }
$output = [IO.Path]::GetFullPath((Join-Path $repo (Join-Path 'tests/Antiphon.Tests' $OutputPath)))
$dll = Join-Path $output 'Antiphon.Tests.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Prebuilt test DLL missing: $dll" }
$digest = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
$pairDir = Join-Path $repo ".antiphon/c804-usage/$sha/$($Phase.ToLowerInvariant())"
New-Item -ItemType Directory -Path $pairDir -Force | Out-Null
$bindingPath = Join-Path $pairDir 'pair.json'
if ($Pass -eq 1) {
    if (Test-Path -LiteralPath $bindingPath) {
        $binding = Get-Content -LiteralPath $bindingPath -Raw | ConvertFrom-Json
        if ($binding.sha -cne $sha -or $binding.digest -cne $digest -or $binding.phase -cne $Phase -or
            [IO.Path]::GetFullPath([string]$binding.output) -cne $output) { throw 'Pass-1 binding belongs to another source.' }
        $sandbox = [string]$binding.sandbox
        if (-not (Test-Path -LiteralPath $sandbox -PathType Container)) { throw 'Bound sandbox is missing.' }
    } else {
        $sandbox = Join-Path ([IO.Path]::GetTempPath()) ("c804-usage-" + $Phase.ToLowerInvariant() + '-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $sandbox | Out-Null
        $binding = [ordered]@{ sha = $sha; digest = $digest; sandbox = $sandbox; phase = $Phase; output = $output }
        [IO.File]::WriteAllText($bindingPath, ($binding | ConvertTo-Json -Depth 8))
    }
} else {
    if (-not (Test-Path -LiteralPath $bindingPath)) { throw 'Pass-1 binding is missing.' }
    $binding = Get-Content -LiteralPath $bindingPath -Raw | ConvertFrom-Json
    if ($binding.sha -cne $sha -or $binding.digest -cne $digest -or $binding.phase -cne $Phase -or
        [IO.Path]::GetFullPath([string]$binding.output) -cne $output) { throw 'Pass pair has a different commit, DLL, phase, or output.' }
    $sandbox = [string]$binding.sandbox
    if (-not (Test-Path -LiteralPath $sandbox -PathType Container)) { throw 'Pass-1 sandbox is missing.' }
}

$invocation = [guid]::NewGuid().ToString('N')
$evidence = Join-Path $pairDir "$Pass/$invocation"
New-Item -ItemType Directory -Path $evidence | Out-Null
$events = Join-Path $evidence 'events.jsonl'
$roster = Join-Path $evidence 'roster.json'
$trx = Join-Path $evidence 'run.trx'
$stdout = Join-Path $evidence 'stdout.log'
$stderr = Join-Path $evidence 'stderr.log'
$filter = if ($Phase -eq 'Namespace') { '/*/Antiphon.Tests.Checkpoints/*/*' } else { '/*/*/*/*' }

. (Join-Path $PSScriptRoot 'lib/checkpoint-usage.ps1')

$baseline = Get-Snapshot $sandbox
$buildBaseline = Get-Allocated $output
$peakCount = $baseline.count
$peakBytes = $baseline.allocatedBytes
$samples = @([ordered]@{ at = [DateTimeOffset]::UtcNow.ToString('o'); count = $baseline.count; allocatedBytes = $baseline.allocatedBytes })
$started = [DateTimeOffset]::UtcNow
$psi = [Diagnostics.ProcessStartInfo]::new('dotnet')
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.WorkingDirectory = $repo
foreach ($arg in @($dll, '--treenode-filter', $filter, '--report-trx', '--report-trx-filename', 'run.trx',
        '--results-directory', $evidence, '--output', 'Detailed', '--diagnostic',
        '--diagnostic-output-directory', $evidence, '--diagnostic-verbosity', 'Information')) {
    [void]$psi.ArgumentList.Add($arg)
}
$psi.Environment['TMPDIR'] = $sandbox
$psi.Environment['TEMP'] = $sandbox
$psi.Environment['TMP'] = $sandbox
$psi.Environment['C804_ROOT_EVENTS'] = $events
$psi.Environment['C804_ROSTER_FILE'] = $roster
$child = [Diagnostics.Process]::Start($psi)
if ($null -eq $child) { throw 'Failed to start the prebuilt test DLL.' }
$outStream = [IO.File]::Create($stdout)
$errStream = [IO.File]::Create($stderr)
$outCopy = $child.StandardOutput.BaseStream.CopyToAsync($outStream)
$errCopy = $child.StandardError.BaseStream.CopyToAsync($errStream)
try {
    while (-not $child.HasExited) {
        Start-Sleep -Seconds 1
        $sample = Get-Snapshot $sandbox
        $samples += [ordered]@{ at = [DateTimeOffset]::UtcNow.ToString('o'); count = $sample.count; allocatedBytes = $sample.allocatedBytes }
        if ($sample.count -gt $peakCount) { $peakCount = $sample.count }
        if ($sample.allocatedBytes -gt $peakBytes) { $peakBytes = $sample.allocatedBytes }
    }
    $child.WaitForExit()
    $outCopy.GetAwaiter().GetResult()
    $errCopy.GetAwaiter().GetResult()
} finally {
    if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }
    $outStream.Dispose()
    $errStream.Dispose()
}
$ended = [DateTimeOffset]::UtcNow
$final = Get-Snapshot $sandbox
$buildFinal = Get-Allocated $output

$errors = [collections.generic.List[string]]::new()
if ($child.ExitCode -ne 0) { $errors.Add("child exit=$($child.ExitCode)") }
if ((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant() -cne $digest) { $errors.Add('DLL digest changed') }
$evidenceCheck = Test-UsageEvidence -Phase $Phase -RosterPath $roster -TrxPath $trx -EventsPath $events
foreach ($item in $evidenceCheck.errors) { $errors.Add($item) }
if ($final.count -ne 0 -or $final.allocatedBytes -ne 0) { $errors.Add("finished-owned residual count=$($final.count) bytes=$($final.allocatedBytes)") }
if ($baseline.count -ne 0 -or $baseline.allocatedBytes -ne 0) { $errors.Add('pass baseline contains roots') }

$rosterHash = $evidenceCheck.rosterHash
$delta = $null
if ($Pass -eq 2) {
    $comparison = Compare-UsagePass -PriorPath (Join-Path $pairDir 'pass1-report.json') -RosterHash $rosterHash `
        -PeakBytes $peakBytes -FinalBytes $final.allocatedBytes -CreatedRoots $evidenceCheck.createdRoots
    foreach ($item in $comparison.errors) { $errors.Add($item) }
    $delta = $comparison.delta
}
$report = [ordered]@{
    schemaVersion = 1; sha = $sha; dllDigest = $digest; phase = $Phase; pass = $Pass; invocation = $invocation
    sandbox = $sandbox; startedAt = $started.ToString('o'); endedAt = $ended.ToString('o')
    filter = $filter; rosterHash = $rosterHash; selected = $evidenceCheck.selected; executed = $evidenceCheck.executed
    passed = $evidenceCheck.executed - $evidenceCheck.failed; failed = $evidenceCheck.failed; skipped = $evidenceCheck.skipped; childExit = $child.ExitCode
    createdRoots = $evidenceCheck.createdRoots; deletedRoots = $evidenceCheck.deletedRoots; copiedLogicalBytes = $evidenceCheck.copiedLogicalBytes
    eventHighWaterRoots = $evidenceCheck.eventHighWaterRoots; eventHighWaterCopiedBytes = $evidenceCheck.eventHighWaterCopiedBytes
    sampledPeakRoots = $peakCount
    baselineAllocatedBytes = $baseline.allocatedBytes; peakAllocatedBytes = $peakBytes
    finalAllocatedBytes = $final.allocatedBytes; buildBaselineAllocatedBytes = $buildBaseline
    buildFinalAllocatedBytes = $buildFinal; finalRoots = $final.roots; samples = $samples
    deltaFromPass1 = $delta; errors = @($errors)
}
$reportPath = Join-Path $evidence 'report.json'
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 16))
if ($Pass -eq 1 -and $errors.Count -eq 0) {
    [IO.File]::WriteAllText((Join-Path $pairDir 'pass1-report.json'), ($report | ConvertTo-Json -Depth 16))
}
Write-Output "CHECKPOINT USAGE phase=$Phase pass=$Pass selected=$($evidenceCheck.selected) executed=$($evidenceCheck.executed) failed=$($evidenceCheck.failed) skipped=$($evidenceCheck.skipped) roots=$($evidenceCheck.createdRoots)/$($evidenceCheck.deletedRoots) peakBytes=$peakBytes residualBytes=$($final.allocatedBytes) report=$reportPath"
if ($errors.Count -ne 0) { throw ('Usage acceptance failed: ' + ($errors -join '; ')) }
