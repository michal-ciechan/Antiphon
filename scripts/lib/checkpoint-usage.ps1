# CARD-0804 usage acceptance: native allocated-byte sampling and the roster/TRX/event gates.
# Dot-sourced by scripts/verify-checkpoint-temp-usage.ps1 and by CheckpointTempUsageTests, which
# drive these functions with supplied inputs. ASCII only.

function Get-Allocated([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return [long]0 }
    if ($IsLinux) {
        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            $line = & du -s -B1 -- $path 2>&1
            if ($LASTEXITCODE -eq 0) { return [long]([string]$line).Split([char]9)[0] }
            if (-not (Test-Path -LiteralPath $path)) { return [long]0 }
            # A test may remove a descendant after du discovers it. Only accept a
            # later complete sample; persistent errors and every other errno stay red.
            if ($attempt -lt 4 -and ([string]($line -join ' ')) -match 'cannot access .+: No such file or directory') {
                Start-Sleep -Milliseconds 20
                continue
            }
            throw "Allocated-byte sample failed for $path : $line"
        }
    }
    if ($IsWindows) { return Get-WindowsAllocated $path }
    throw 'Native allocated-byte sampling is not implemented for this OS.'
}

# Windows allocated bytes: per file, GetCompressedFileSizeW (compressed/sparse aware) rounded up to the
# volume cluster size. Reparse points, the sampled root included, are counted as zero and never followed,
# so a link cannot inflate or hide a root. Only a path that vanished while sampling is skipped; any other
# listing or size error fails the sample instead of reading as zero usage.
if ($IsWindows) {
    if (-not ('C804.NativeAlloc' -as [type])) {
        Add-Type -Namespace C804 -Name NativeAlloc -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
private static extern uint GetCompressedFileSizeW(string name, out uint high);
[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
public static extern bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
public static long CompressedSize(string name, out int error)
{
    uint high;
    uint low = GetCompressedFileSizeW(name, out high);
    error = low == uint.MaxValue ? System.Runtime.InteropServices.Marshal.GetLastWin32Error() : 0;
    return error == 0 ? ((long)high << 32) + low : -1;
}
'@
    }
    $script:clusterBytes = @{}
}

function Get-ClusterBytes([string]$path) {
    $root = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($path))
    if (-not $script:clusterBytes.ContainsKey($root)) {
        [uint32]$spc = 0; [uint32]$bps = 0; [uint32]$free = 0; [uint32]$total = 0
        if (-not [C804.NativeAlloc]::GetDiskFreeSpaceW($root, [ref]$spc, [ref]$bps, [ref]$free, [ref]$total)) {
            throw "Cluster size probe failed for $root"
        }
        $script:clusterBytes[$root] = [long]$spc * [long]$bps
    }
    return $script:clusterBytes[$root]
}

function Get-WindowsFileAllocated([string]$file, [long]$cluster) {
    [int]$err = 0
    $size = [C804.NativeAlloc]::CompressedSize('\\?\' + $file, [ref]$err)
    # ERROR_FILE_NOT_FOUND / ERROR_PATH_NOT_FOUND: removed while sampling.
    if ($err -eq 2 -or $err -eq 3) { return [long]0 }
    if ($err -ne 0) { throw "Allocated-byte sample failed for $file : Win32 error $err" }
    return [long]([Math]::Ceiling($size / $cluster) * $cluster)
}

function Get-WindowsAllocated([string]$path) {
    try { $attributes = [IO.File]::GetAttributes($path) }
    catch [IO.FileNotFoundException] { return [long]0 }
    catch [IO.DirectoryNotFoundException] { return [long]0 }
    if ($attributes -band [IO.FileAttributes]::ReparsePoint) { return [long]0 }
    $cluster = Get-ClusterBytes $path
    if (-not ($attributes -band [IO.FileAttributes]::Directory)) {
        return Get-WindowsFileAllocated ([IO.Path]::GetFullPath($path)) $cluster
    }
    [long]$total = 0
    $stack = [Collections.Generic.Stack[string]]::new()
    $stack.Push([IO.Path]::GetFullPath($path))
    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        try { $entries = [IO.DirectoryInfo]::new($dir).GetFileSystemInfos() }
        catch [IO.DirectoryNotFoundException] { continue }
        catch { throw "Allocated-byte sample failed for $dir : $($_.Exception.Message)" }
        foreach ($entry in $entries) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            if ($entry.Attributes -band [IO.FileAttributes]::Directory) { $stack.Push($entry.FullName); continue }
            $total += Get-WindowsFileAllocated $entry.FullName $cluster
        }
    }
    return $total
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

# The Antiphon.Tests.Checkpoints census: every compiled non-explicit case, argument rows expanded.
# CheckpointTempUsageTests checks selected and every skip name against the compiled test list, so
# adding a checkpoint test fails that guard until this census moves with it. Each OS skips the
# other OS's native cases; the skip set is matched by name against the actual roster.
function Get-NamespaceCensus {
    [ordered]@{
        selected = 290
        windowsSkips = @(
            'Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.*'
            'Antiphon.Tests.Checkpoints.DetachedLauncherTests.executor_survives_its_starter'
        )
        linuxSkips = @(
            'Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.*'
            'Antiphon.Tests.Checkpoints.TimeoutTests.windows_quick_row_finishes_beside_a_slow_row'
            'Antiphon.Tests.Checkpoints.TimeoutTests.windows_row_arguments_round_trip_intact'
            'Antiphon.Tests.Checkpoints.TimeoutTests.windows_chatty_row_drains_interleaved_stdout_and_stderr'
            'Antiphon.Tests.Checkpoints.TimeoutTests.windows_row_timeout_kills_the_start_b_grandchild'
        )
    }
}

function Test-DeclaredOsSkip([string]$Name, $Census) {
    $patterns = if ($IsWindows) { $Census.windowsSkips } else { $Census.linuxSkips }
    foreach ($pattern in $patterns) { if ($Name -clike $pattern) { return $true } }
    return $false
}

# The selection roster, TRX and root event stream of one pass, checked against each other.
function Test-UsageEvidence([string]$Phase, [string]$RosterPath, [string]$TrxPath, [string]$EventsPath) {
    $errors = [collections.generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $RosterPath)) { $errors.Add('selection roster missing') }
    if (-not (Test-Path -LiteralPath $TrxPath)) { $errors.Add('TRX missing') }
    if (-not (Test-Path -LiteralPath $EventsPath)) { $errors.Add('event stream missing') }

    $selected = @()
    if (Test-Path -LiteralPath $RosterPath) {
        try { $selected = @(Get-Content -LiteralPath $RosterPath -Raw | ConvertFrom-Json) }
        catch { $errors.Add('selection roster malformed') }
    }
    $results = @()
    $definitions = @{}
    if (Test-Path -LiteralPath $TrxPath) {
        try {
            [xml]$doc = Get-Content -LiteralPath $TrxPath -Raw
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
    $census = Get-NamespaceCensus
    $checkpointSelected = @($selected | Where-Object { [string]$_.className -like 'Antiphon.Tests.Checkpoints.*' })
    # The OS-skip set is derived from the real roster by declared name, never a count.
    $declaredSkips = @($checkpointSelected | ForEach-Object { [string]$_.className + '.' + [string]$_.method } |
        Where-Object { Test-DeclaredOsSkip $_ $census } | Sort-Object)
    if ($Phase -eq 'Namespace') {
        if ($selected.Count -ne $census.selected -or $checkpointSelected.Count -ne $selected.Count) {
            $errors.Add("namespace census selected=$($selected.Count) expected=$($census.selected) executed=$($executed.Count) skipped=$($skipped.Count)")
        }
        $skippedNames = @($skipped | ForEach-Object { [string]$_.name } | Sort-Object)
        if (($skippedNames -join "`n") -cne ($declaredSkips -join "`n")) {
            $errors.Add("namespace skips differ from declared OS skips declared=$($declaredSkips.Count) skipped=$($skipped.Count)")
        }
    } else {
        if ($executed.Count -lt 1000) { $errors.Add("full-suite executed=$($executed.Count) below 1000") }
        if ($checkpointSelected.Count -ne $census.selected) {
            $errors.Add("full roster checkpoint cases=$($checkpointSelected.Count) expected=$($census.selected)")
        }
        $checkpointFloor = $census.selected - $declaredSkips.Count
        if (@($executed | Where-Object { $_.name -like 'Antiphon.Tests.Checkpoints.*' }).Count -lt $checkpointFloor) {
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
    if (Test-Path -LiteralPath $EventsPath) {
        $raw = [IO.File]::ReadAllText($EventsPath)
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

    $rosterHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes(($expectedNames -join "`n")))).ToLowerInvariant()
    return [ordered]@{
        errors = @($errors); selected = $selected.Count; executed = $executed.Count
        failed = $failed.Count; skipped = $skipped.Count; rosterHash = $rosterHash
        createdRoots = $created.Count; deletedRoots = $deleted.Count; copiedLogicalBytes = $copiedBytes
        eventHighWaterRoots = $eventPeak; eventHighWaterCopiedBytes = $eventPeakBytes
    }
}

# Pass 2 must repeat pass 1's accepted roster exactly; the delta is reported, not gated.
function Compare-UsagePass([string]$PriorPath, [string]$RosterHash, [long]$PeakBytes, [long]$FinalBytes, [int]$CreatedRoots) {
    $errors = [collections.generic.List[string]]::new()
    $delta = $null
    if (-not (Test-Path -LiteralPath $PriorPath)) { $errors.Add('pass-1 accepted report missing') }
    else {
        $prior = Get-Content -LiteralPath $PriorPath -Raw | ConvertFrom-Json
        if ($prior.rosterHash -cne $RosterHash) { $errors.Add('pass rosters differ') }
        $delta = [ordered]@{ peakAllocatedBytes = $PeakBytes - [long]$prior.peakAllocatedBytes;
            finalAllocatedBytes = $FinalBytes - [long]$prior.finalAllocatedBytes;
            createdRoots = $CreatedRoots - [int]$prior.createdRoots }
    }
    return [ordered]@{ errors = @($errors); delta = $delta }
}
