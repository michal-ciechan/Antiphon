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

function Get-Allocated([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return [long]0 }
    if ($IsLinux) {
        $line = & du -s -B1 -- $path 2>&1
        if ($LASTEXITCODE -ne 0) {
            if (-not (Test-Path -LiteralPath $path)) { return [long]0 }
            throw "Allocated-byte sample failed for $path : $line"
        }
        return [long]([string]$line).Split([char]9)[0]
    }
    throw 'Native allocated-byte sampling is not implemented for this OS.'
}

function Get-Snapshot([string]$temp) {
    [long]$bytes = 0
    $roots = @()
    foreach ($entry in Get-ChildItem -LiteralPath $temp -Directory -Force) {
        if ($entry.Name -cnotmatch '^c723-[0-9a-f]{32}$') { continue }
        $size = Get-Allocated $entry.FullName
        $roots += [ordered]@{ path = $entry.FullName; allocatedBytes = $size }
        $bytes += $size
    }
    return [ordered]@{ roots = $roots; count = $roots.Count; allocatedBytes = $bytes }
}

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
if (-not (Test-Path -LiteralPath $roster)) { $errors.Add('selection roster missing') }
if (-not (Test-Path -LiteralPath $trx)) { $errors.Add('TRX missing') }
if (-not (Test-Path -LiteralPath $events)) { $errors.Add('event stream missing') }

$selected = @()
if (Test-Path -LiteralPath $roster) {
    try { $selected = @(Get-Content -LiteralPath $roster -Raw | ConvertFrom-Json) }
    catch { $errors.Add('selection roster malformed') }
}
$results = @()
$definitions = @{}
if (Test-Path -LiteralPath $trx) {
    try {
        [xml]$doc = Get-Content -LiteralPath $trx -Raw
        $manager = [Xml.XmlNamespaceManager]::new($doc.NameTable)
        $manager.AddNamespace('t', $doc.DocumentElement.NamespaceURI)
        foreach ($unit in $doc.SelectNodes('//t:UnitTest', $manager)) {
            $method = $unit.SelectSingleNode('t:TestMethod', $manager)
            if ($null -ne $method) { $definitions[[string]$unit.id] = [string]$method.className + '.' + [string]$method.name }
        }
        $results = @($doc.SelectNodes('//t:UnitTestResult', $manager) | ForEach-Object {
            [ordered]@{ id = [string]$_.testId; outcome = [string]$_.outcome; name = $definitions[[string]$_.testId] }
        })
    } catch { $errors.Add('TRX malformed') }
}
$executed = @($results | Where-Object { $_.outcome -ne 'NotExecuted' })
$skipped = @($results | Where-Object { $_.outcome -eq 'NotExecuted' })
$failed = @($results | Where-Object { $_.outcome -in @('Failed', 'Error', 'Timeout', 'Aborted') })
if ($results.Count -ne $selected.Count) { $errors.Add("roster/TRX count mismatch selected=$($selected.Count) terminal=$($results.Count)") }
if (@($results.id | Select-Object -Unique).Count -ne $results.Count) { $errors.Add('duplicate TRX test ID') }
$expectedNames = @($selected | ForEach-Object { [string]$_.className + '.' + [string]$_.method } | Sort-Object)
$actualNames = @($results | ForEach-Object { [string]$_.name } | Sort-Object)
if (($expectedNames -join "`n") -cne ($actualNames -join "`n")) { $errors.Add('selected class/method multiset differs from TRX') }
if ($failed.Count -ne 0) { $errors.Add("failed TRX tests=$($failed.Count)") }
if ($Phase -eq 'Namespace') {
    if ($selected.Count -ne 250 -or $executed.Count -ne 235 -or $skipped.Count -ne 15) {
        $errors.Add("namespace census selected=$($selected.Count) executed=$($executed.Count) skipped=$($skipped.Count)")
    }
} else {
    if ($executed.Count -lt 1000) { $errors.Add("full-suite executed=$($executed.Count) below 1000") }
    if (@($executed | Where-Object { $_.name -like 'Antiphon.Tests.Checkpoints.*' }).Count -lt 235) {
        $errors.Add('full suite omitted checkpoint executions')
    }
}

$created = @{}
$deleted = @{}
[long]$copiedBytes = 0
$eventPeak = 0
$active = @{}
$activeBytes = @{}
[long]$eventPeakBytes = 0
if (Test-Path -LiteralPath $events) {
    $raw = [IO.File]::ReadAllText($events)
    if ($raw.Length -eq 0 -or -not $raw.EndsWith("`n")) { $errors.Add('event tail incomplete') }
    foreach ($line in $raw.Split("`n")) {
        if (-not $line) { continue }
        try { $event = $line | ConvertFrom-Json }
        catch { $errors.Add('event line malformed'); continue }
        switch ([string]$event.kind) {
            'root-create' {
                $created[[string]$event.path] = 1
                $active[[string]$event.path] = 1
                $activeBytes[[string]$event.path] = [long]0
            }
            'root-delete' {
                $deleted[[string]$event.path] = 1
                [void]$active.Remove([string]$event.path)
                [void]$activeBytes.Remove([string]$event.path)
            }
            'copy' {
                $copiedBytes += [long]$event.bytes
                foreach ($root in @($activeBytes.Keys)) {
                    if ([string]$event.path -eq $root -or [string]$event.path -like ($root + [IO.Path]::DirectorySeparatorChar + '*')) {
                        $activeBytes[$root] = [long]$activeBytes[$root] + [long]$event.bytes
                        break
                    }
                }
            }
            default { $errors.Add('unknown event kind') }
        }
        if ($active.Count -gt $eventPeak) { $eventPeak = $active.Count }
        [long]$liveCopyBytes = 0
        foreach ($size in $activeBytes.Values) { $liveCopyBytes += [long]$size }
        if ($liveCopyBytes -gt $eventPeakBytes) { $eventPeakBytes = $liveCopyBytes }
    }
}
if ($created.Count -eq 0) { $errors.Add('no owned roots were observed') }
if ($created.Count -ne $deleted.Count -or $active.Count -ne 0) { $errors.Add('created/deleted root IDs do not reconcile') }
if ($final.count -ne 0 -or $final.allocatedBytes -ne 0) { $errors.Add("finished-owned residual count=$($final.count) bytes=$($final.allocatedBytes)") }
if ($baseline.count -ne 0 -or $baseline.allocatedBytes -ne 0) { $errors.Add('pass baseline contains roots') }

$rosterHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes(($expectedNames -join "`n")))).ToLowerInvariant()
$delta = $null
if ($Pass -eq 2) {
    $priorPath = Join-Path $pairDir 'pass1-report.json'
    if (-not (Test-Path -LiteralPath $priorPath)) { $errors.Add('pass-1 accepted report missing') }
    else {
        $prior = Get-Content -LiteralPath $priorPath -Raw | ConvertFrom-Json
        if ($prior.rosterHash -cne $rosterHash) { $errors.Add('pass rosters differ') }
        $delta = [ordered]@{ peakAllocatedBytes = $peakBytes - [long]$prior.peakAllocatedBytes;
            finalAllocatedBytes = $final.allocatedBytes - [long]$prior.finalAllocatedBytes;
            createdRoots = $created.Count - [int]$prior.createdRoots }
    }
}
$report = [ordered]@{
    schemaVersion = 1; sha = $sha; dllDigest = $digest; phase = $Phase; pass = $Pass; invocation = $invocation
    sandbox = $sandbox; startedAt = $started.ToString('o'); endedAt = $ended.ToString('o')
    filter = $filter; rosterHash = $rosterHash; selected = $selected.Count; executed = $executed.Count
    passed = $executed.Count - $failed.Count; failed = $failed.Count; skipped = $skipped.Count; childExit = $child.ExitCode
    createdRoots = $created.Count; deletedRoots = $deleted.Count; copiedLogicalBytes = $copiedBytes
    eventHighWaterRoots = $eventPeak; eventHighWaterCopiedBytes = $eventPeakBytes
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
Write-Output "CHECKPOINT USAGE phase=$Phase pass=$Pass selected=$($selected.Count) executed=$($executed.Count) failed=$($failed.Count) skipped=$($skipped.Count) roots=$($created.Count)/$($deleted.Count) peakBytes=$peakBytes residualBytes=$($final.allocatedBytes) report=$reportPath"
if ($errors.Count -ne 0) { throw ('Usage acceptance failed: ' + ($errors -join '; ')) }
