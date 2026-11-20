#requires -Version 7.0
# Astra audit witnesses for discarded flush failures, journal acknowledgement before
# publication, and an older loaded core. ASCII-only. The cases fail on the assigned base.
param([string]$Case = '', [string]$ResultsDirectory = '')
$ErrorActionPreference = 'Stop'

function New-AstraResultsRoot {
    if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
        $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('astra-b-' + [guid]::NewGuid().ToString('n'))
    }
    New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
    return [IO.Path]::GetFullPath($ResultsDirectory)
}

function New-AstraFresh {
    return [pscustomobject]@{
        chunk = 'chunk-1'
        runId = 'run'
        attempt = '1'
        suite = 'antiphon'
        hostWallSeconds = 0
        trxMissing = $true
        passedCount = 0
        outcomes = @()
        trx = ''
        trxDigest = ''
        exitCode = 0
        coverageComplete = $false
        testsPassed = $false
    }
}

function Open-AstraContext {
    param([string]$Dir)
    $log = Join-Path $Dir 'log'
    $timing = Join-Path $Dir 'timing'
    New-Item -ItemType Directory -Path $log, $timing -Force | Out-Null
    $ctx = Open-TestPhaseExecutedContext -LogRoot $log -TimingRoot $timing -RunId 'run' -Attempt '1' -Suite 'antiphon' -Source ('a' * 40) -InvocationId 'run'
    if ($ctx.Refused) { throw ('context-refused ' + [string]$ctx.Reason) }
    Sync-NightlyDeferredEvidenceWrites
    return $ctx
}

function Get-AstraGenerationDir {
    param($Context)
    return (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation)
}

function Get-AstraDiskRecords {
    param([string]$GenerationDir)
    $loaded = New-Object System.Collections.Generic.List[object]
    $null = [Antiphon.Nightly.TestPhaseJournalRead]::ReadPrefix($GenerationDir, 1, $false, $loaded)
    return $loaded.Count
}

function Enable-AstraBeforeWriteType {
    if ('AstraBeforeWrite' -as [type]) { return }
    $definition = '
public static class AstraBeforeWrite {
    public static string Mode;
    public static string SignalPath;
    public static string GenerationDir;
    public static System.Management.Automation.PSObject Context;
    public static int SleepMs;
    public static int SnapshotLast = -1;
    public static int SnapshotDisk = -1;
    public static bool SnapshotSeen;
    public static void Invoke(string path) {
        if (Mode == "throw") throw new System.IO.IOException("astra-before-write");
        if (Mode == "delay-throw") {
            System.Threading.Thread.Sleep(SleepMs);
            throw new System.IO.IOException("astra-delay-throw");
        }
        if (Mode == "kill") {
            System.IO.File.WriteAllText(SignalPath, "entered");
            System.Threading.Thread.Sleep(SleepMs);
            return;
        }
        if (Mode == "pause") {
            System.Threading.Thread.Sleep(SleepMs);
            object raw = Context.Properties["LastCommitted"].Value;
            SnapshotLast = System.Convert.ToInt32(raw);
            string record = System.IO.Path.Combine(GenerationDir, "00000001.json");
            SnapshotDisk = System.IO.File.Exists(record) ? 1 : 0;
            SnapshotSeen = true;
        }
    }
}
'
    $pwshHome = Split-Path ([psobject].Assembly.Location)
    $threadRef = Join-Path $pwshHome 'ref/System.Threading.Thread.dll'
    if (-not [IO.File]::Exists($threadRef)) { $threadRef = Join-Path $pwshHome 'System.Threading.Thread.dll' }
    Add-Type -TypeDefinition $definition -ReferencedAssemblies @([psobject].Assembly.Location, $threadRef)
}

function Set-AstraBeforeWrite {
    param([string]$Mode, $Context, [string]$GenerationDir, [string]$SignalPath, [int]$SleepMs)
    Enable-AstraBeforeWriteType
    [AstraBeforeWrite]::Mode = $Mode
    [AstraBeforeWrite]::Context = $Context
    [AstraBeforeWrite]::GenerationDir = $GenerationDir
    [AstraBeforeWrite]::SignalPath = $SignalPath
    [AstraBeforeWrite]::SleepMs = $SleepMs
    if ($Mode -eq 'pause') {
        [AstraBeforeWrite]::SnapshotLast = -1
        [AstraBeforeWrite]::SnapshotDisk = -1
        [AstraBeforeWrite]::SnapshotSeen = $false
    }
    $flags = [Reflection.BindingFlags]'Static,NonPublic'
    $buffered = [Antiphon.Nightly.EvidenceFiles].GetField('buffered', $flags).GetValue($null)
    if ($null -eq $buffered) { throw 'write-behind-off' }
    $queueFlags = [Reflection.BindingFlags]'Instance,NonPublic'
    $queue = $buffered.GetType().GetProperty('Queue', $queueFlags).GetValue($buffered)
    $prop = $queue.GetType().GetProperty('BeforeWrite', $queueFlags)
    $prop.SetValue($queue, [Action[string]]([AstraBeforeWrite]::Invoke))
}

function Clear-AstraBeforeWrite {
    try {
        if ('AstraBeforeWrite' -as [type]) { [AstraBeforeWrite]::Mode = '' }
        $flags = [Reflection.BindingFlags]'Static,NonPublic'
        $buffered = [Antiphon.Nightly.EvidenceFiles].GetField('buffered', $flags).GetValue($null)
        if ($null -eq $buffered) { return }
        $queueFlags = [Reflection.BindingFlags]'Instance,NonPublic'
        $queue = $buffered.GetType().GetProperty('Queue', $queueFlags).GetValue($buffered)
        $prop = $queue.GetType().GetProperty('BeforeWrite', $queueFlags)
        $prop.SetValue($queue, $null)
    } catch { }
}

function Close-AstraLock {
    param($Context)
    if ($null -eq $Context -or $null -eq $Context.Lock) { return }
    try { $Context.Lock.Dispose() } catch { }
    $Context.Lock = $null
}

function Test-AstraFailureVisible {
    $visible = $false
    $detail = ''
    try {
        $peek = @([Antiphon.Nightly.EvidenceFiles]::PeekFailures())
        $detail = ('peek=' + ($peek -join ' | '))
        if (@($peek | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) { $visible = $true }
    } catch {
        $detail = ('peek-missing ' + $_.Exception.Message)
    }
    if (-not $visible) {
        $flushed = @([Antiphon.Nightly.EvidenceFiles]::Flush())
        $detail = $detail + ' flush=' + ($flushed -join ' | ')
        if (@($flushed | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) { $visible = $true }
    }
    return [pscustomobject]@{ Visible = $visible; Detail = $detail }
}

function Test-AstraFlushFailure {
    $root = New-AstraResultsRoot
    $blocked = Join-Path $root 'flush-block'
    New-Item -ItemType Directory -Path $blocked -Force | Out-Null
    $file1 = Join-Path $blocked 'one.json'
    $file2 = Join-Path $blocked 'two.json'
    $old = '{"sequence":1,"publicationComplete":true}'
    $new = '{"sequence":2,"publicationComplete":false}'
    [IO.File]::WriteAllText($file1, $old)
    [IO.File]::WriteAllText($file2, $old)
    $done = @{}
    try {
        Enable-NightlyDeferredEvidenceWrites
        # Fail both queued writes via the same IOException seam as the journal case.
        Set-AstraBeforeWrite -Mode 'throw'
        [Antiphon.Nightly.EvidenceFiles]::WriteAtomic($file1, $new, $false)
        [Antiphon.Nightly.EvidenceFiles]::WriteAtomic($file2, $new, $false)
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $ready = $false
        while ($watch.ElapsedMilliseconds -lt 8000) {
            $pending1 = [Antiphon.Nightly.EvidenceFiles]::PendingLength($file1)
            $pending2 = [Antiphon.Nightly.EvidenceFiles]::PendingLength($file2)
            if ($pending1 -lt 0 -and $pending2 -lt 0) { $ready = $true; break }
            Start-Sleep -Milliseconds 50
        }
        if (-not $ready) { throw 'setup: replacement stayed queued' }
        if ([IO.File]::ReadAllText($file1) -ne $old -or [IO.File]::ReadAllText($file2) -ne $old) {
            throw 'setup: replacement landed despite the injected write failure'
        }
        $read = [Antiphon.Nightly.TestPhaseFiles]::BoundedRead($file1, 4096, 5000, [Diagnostics.Stopwatch]::StartNew())
        $staleComplete = ([bool]$read.Complete -and -not [bool]$read.Refused)
        Assert-C487 (-not $staleComplete) 'AB FlushFailure.not-complete-stale' ('Complete=' + $read.Complete + ' Refused=' + $read.Refused + ' Kind=' + $read.Kind)
        $done['not-complete-stale'] = $true
        $refused = ([bool]$read.Refused -and [string]$read.Kind -eq 'deferred-write-failed' -and -not [bool]$read.Complete)
        Assert-C487 $refused 'AB FlushFailure.refused' ('Complete=' + $read.Complete + ' Refused=' + $read.Refused + ' Kind=' + $read.Kind)
        $done['refused'] = $true
        $one = @([Antiphon.Nightly.EvidenceFiles]::Flush($file1))
        $oneText = [string]($one -join ' | ')
        $pathKeeps = ($oneText.Contains('one.json') -and -not $oneText.Contains('two.json'))
        Assert-C487 $pathKeeps 'AB FlushFailure.path-keeps-other' $oneText
        $done['path-keeps-other'] = $true
        $afterFlush = [Antiphon.Nightly.TestPhaseFiles]::BoundedRead($file1, 4096, 5000, [Diagnostics.Stopwatch]::StartNew())
        $pathStillFailed = [Antiphon.Nightly.EvidenceFiles]::PathFailed($file1)
        $stillRefused = ($pathStillFailed -and [bool]$afterFlush.Refused -and -not [bool]$afterFlush.Complete -and [string]$afterFlush.Kind -eq 'deferred-write-failed')
        Assert-C487 $stillRefused 'AB FlushFailure.path-still-failed' ('PathFailed=' + $pathStillFailed + ' Complete=' + $afterFlush.Complete + ' Refused=' + $afterFlush.Refused + ' Kind=' + $afterFlush.Kind)
        $done['path-still-failed'] = $true
        $rest = @([Antiphon.Nightly.EvidenceFiles]::Flush())
        $restText = [string]($rest -join ' | ')
        Assert-C487 $restText.Contains('two.json') 'AB FlushFailure.later-flush-keeps-other' $restText
        $done['later-flush-keeps-other'] = $true
        $verdictOk = $false
        $verdictDetail = ''
        try {
            $verdict = Get-NightlyDeferredEvidenceWriteVerdict
            $reasonText = [string](@($verdict.reasons) -join ' | ')
            $verdictOk = ((-not [bool]$verdict.coverageComplete) -and (-not [bool]$verdict.testsPassed) -and $reasonText.Contains('deferred-evidence-write-failed:'))
            $verdictDetail = ('coverage=' + $verdict.coverageComplete + ' tests=' + $verdict.testsPassed + ' reasons=' + $reasonText)
        } catch {
            $verdictDetail = $_.Exception.Message
        }
        Assert-C487 $verdictOk 'AB FlushFailure.verdict' $verdictDetail
        $done['verdict'] = $true
        Clear-AstraBeforeWrite
        $retryText = '{"sequence":3,"publicationComplete":true}'
        [Antiphon.Nightly.EvidenceFiles]::WriteAtomic($file1, $retryText, $false)
        $retryWatch = [Diagnostics.Stopwatch]::StartNew()
        $retryReady = $false
        while ($retryWatch.ElapsedMilliseconds -lt 8000) {
            if ([Antiphon.Nightly.EvidenceFiles]::PendingLength($file1) -lt 0) { $retryReady = $true; break }
            Start-Sleep -Milliseconds 50
        }
        if (-not $retryReady) { throw 'setup: retry stayed queued' }
        if ([IO.File]::ReadAllText($file1) -ne $retryText) { throw 'setup: retry did not land' }
        $retryReported = @([Antiphon.Nightly.EvidenceFiles]::Flush($file1) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        $retryVerdict = Get-NightlyDeferredEvidenceWriteVerdict
        $retryReasons = [string](@($retryVerdict.reasons) -join ' | ')
        $retryKept = ($retryReported.Count -eq 0 -and [Antiphon.Nightly.EvidenceFiles]::PathFailed($file1) -and -not [bool]$retryVerdict.coverageComplete -and -not [bool]$retryVerdict.testsPassed -and $retryReasons.Contains('one.json'))
        Assert-C487 $retryKept 'AB FlushFailure.retry-keeps-failed' ('reported=' + $retryReported.Count + ' pathFailed=' + [Antiphon.Nightly.EvidenceFiles]::PathFailed($file1) + ' coverage=' + $retryVerdict.coverageComplete + ' reasons=' + $retryReasons)
        $done['retry-keeps-failed'] = $true
        $quality = @(Get-TestPhasePublicationQuality -RunDirectory $root)
        $qualityOk = @($quality | Where-Object { [string]$_ -eq 'deferred-evidence-write-failed' }).Count -gt 0
        Assert-C487 $qualityOk 'AB FlushFailure.phase-quality-token' ('tokens=' + ($quality -join ','))
        $done['phase-quality-token'] = $true
        $summaryOk = $false
        $summaryDetail = ''
        try {
            $merged = Merge-NightlyDeferredEvidenceVerdict -Reasons @('kept-reason')
            $mergedReasons = [string](@($merged.reasons) -join ' | ')
            $summaryOk = ($mergedReasons.Contains('kept-reason') -and $mergedReasons.Contains('deferred-evidence-write-failed:') -and -not [bool]$merged.coverageComplete -and -not [bool]$merged.testsPassed)
            $summaryDetail = ('coverage=' + $merged.coverageComplete + ' tests=' + $merged.testsPassed + ' reasons=' + $mergedReasons)
        } catch {
            $summaryDetail = $_.Exception.Message
        }
        Assert-C487 $summaryOk 'AB FlushFailure.summary-calls-verdict' $summaryDetail
        $done['summary-calls-verdict'] = $true
    } catch {
        $detail = $_.Exception.Message
        foreach ($name in @('not-complete-stale','refused','path-keeps-other','path-still-failed','later-flush-keeps-other','verdict','retry-keeps-failed','phase-quality-token','summary-calls-verdict')) {
            if (-not $done[$name]) { Assert-C487 $false ('AB FlushFailure.' + $name) $detail }
        }
    } finally {
        Clear-AstraBeforeWrite
        if ($script:NightlyDeferEvidenceWrites) {
            try { Disable-NightlyDeferredEvidenceWrites } catch { }
        }
    }
    Test-AstraVerdictFlushes
}

function Test-AstraVerdictFlushes {
    $root = New-AstraResultsRoot
    $dir = Join-Path $root 'inflight'
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $target = Join-Path $dir 'late.json'
    $ok = $false
    $detail = ''
    try {
        Enable-NightlyDeferredEvidenceWrites
        Set-AstraBeforeWrite -Mode 'delay-throw' -SleepMs 2000
        [Antiphon.Nightly.EvidenceFiles]::WriteAllText($target, 'late')
        $verdict = Get-NightlyDeferredEvidenceWriteVerdict
        $reasons = [string](@($verdict.reasons) -join ' | ')
        $ok = ((-not [bool]$verdict.coverageComplete) -and (-not [bool]$verdict.testsPassed) -and $reasons.Contains('deferred-evidence-write-failed:'))
        $detail = ('coverage=' + $verdict.coverageComplete + ' tests=' + $verdict.testsPassed + ' reasons=' + $reasons)
        $wait = [Diagnostics.Stopwatch]::StartNew()
        while ($wait.ElapsedMilliseconds -lt 8000 -and [Antiphon.Nightly.EvidenceFiles]::PendingLength($target) -ge 0) {
            Start-Sleep -Milliseconds 50
        }
    } catch {
        $detail = $_.Exception.Message
    } finally {
        Clear-AstraBeforeWrite
        if ($script:NightlyDeferEvidenceWrites) {
            try { Disable-NightlyDeferredEvidenceWrites } catch { }
        }
    }
    Assert-C487 $ok 'AB FlushFailure.verdict-flushes' $detail
}

function Test-AstraJournalPublish {
    $root = New-AstraResultsRoot
    $done = @{}
    $held = @()
    try {
        Enable-NightlyDeferredEvidenceWrites
        $pauseDir = Join-Path $root 'pause'
        $pause = Open-AstraContext $pauseDir
        $held += $pause
        $pauseGen = Get-AstraGenerationDir $pause
        Set-AstraBeforeWrite -Mode 'pause' -Context $pause -GenerationDir $pauseGen -SleepMs 2000
        $paused = Add-TestPhaseExecutedChunk -Context $pause -Fresh (New-AstraFresh) -Suite 'antiphon' -Chunk 'chunk-1' -RunId 'run' -Attempt '1'
        $diskNow = Get-AstraDiskRecords $pauseGen
        $wait = [Diagnostics.Stopwatch]::StartNew()
        while (-not [AstraBeforeWrite]::SnapshotSeen -and $wait.ElapsedMilliseconds -lt 5000) { Start-Sleep -Milliseconds 50 }
        Clear-AstraBeforeWrite
        Assert-C487 ($paused -and $diskNow -ge 1) 'AB JournalPublish.ack-waits-for-disk' ('acked=' + $paused + ' diskNow=' + $diskNow + ' snapDisk=' + [AstraBeforeWrite]::SnapshotDisk)
        $done['ack-waits-for-disk'] = $true
        Assert-C487 ([AstraBeforeWrite]::SnapshotSeen -and [AstraBeforeWrite]::SnapshotLast -eq 0) 'AB JournalPublish.held-write-not-acked' ('seen=' + [AstraBeforeWrite]::SnapshotSeen + ' last=' + [AstraBeforeWrite]::SnapshotLast)
        $done['held-write-not-acked'] = $true

        $throwDir = Join-Path $root 'throw'
        $thrown = Open-AstraContext $throwDir
        $held += $thrown
        $throwGen = Get-AstraGenerationDir $thrown
        $beforeLast = [int]$thrown.LastCommitted
        Set-AstraBeforeWrite -Mode 'throw'
        $acked = Add-TestPhaseExecutedChunk -Context $thrown -Fresh (New-AstraFresh) -Suite 'antiphon' -Chunk 'chunk-1' -RunId 'run' -Attempt '1'
        $throwDisk = Get-AstraDiskRecords $throwGen
        Clear-AstraBeforeWrite
        $visible = Test-AstraFailureVisible
        $throwOk = ((-not $acked) -and ([int]$thrown.LastCommitted -eq $beforeLast) -and ($throwDisk -eq 0) -and $visible.Visible)
        Assert-C487 $throwOk 'AB JournalPublish.throw-does-not-ack' ('acked=' + $acked + ' last=' + $thrown.LastCommitted + ' disk=' + $throwDisk + ' ' + $visible.Detail)
        $done['throw-does-not-ack'] = $true

        $lockDir = Join-Path $root 'lock'
        $locked = Open-AstraContext $lockDir
        $held += $locked
        $lockedJournal = Get-TestPhaseJournalRoot -TimingRoot $locked.TimingRoot
        Set-AstraBeforeWrite -Mode 'throw'
        [Antiphon.Nightly.EvidenceFiles]::WriteAllText((Join-Path $lockedJournal 'held-failure.json'), 'held')
        Close-TestPhaseExecutedContext -Context $locked
        $lockPath = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $locked.TimingRoot) 'writer.lock'
        $reacquired = $false
        $probe = $null
        try {
            $probe = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $reacquired = $true
        } catch {
            $reacquired = $false
        } finally {
            if ($null -ne $probe) { try { $probe.Dispose() } catch { } }
        }
        Clear-AstraBeforeWrite
        $lockVisible = Test-AstraFailureVisible
        $lockOk = ((-not $reacquired) -and $lockVisible.Visible)
        Assert-C487 $lockOk 'AB JournalPublish.close-holds-lock' ('reacquired=' + $reacquired + ' ' + $lockVisible.Detail)
        $done['close-holds-lock'] = $true

        $unrelatedDir = Join-Path $root 'unrelated'
        $unrelated = Open-AstraContext $unrelatedDir
        $held += $unrelated
        Set-AstraBeforeWrite -Mode 'throw'
        [Antiphon.Nightly.EvidenceFiles]::WriteAllText((Join-Path $unrelatedDir 'outside.json'), 'outside')
        Close-TestPhaseExecutedContext -Context $unrelated
        $unrelatedLock = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $unrelated.TimingRoot) 'writer.lock'
        $unrelatedProbe = $null
        $unrelatedFree = $false
        try {
            $unrelatedProbe = [IO.File]::Open($unrelatedLock, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $unrelatedFree = $true
        } catch {
            $unrelatedFree = $false
        } finally {
            if ($null -ne $unrelatedProbe) { try { $unrelatedProbe.Dispose() } catch { } }
        }
        $unrelatedVerdict = Get-NightlyDeferredEvidenceWriteVerdict
        $unrelatedReasons = [string](@($unrelatedVerdict.reasons) -join ' | ')
        $unrelatedFailed = ((-not [bool]$unrelatedVerdict.coverageComplete) -and (-not [bool]$unrelatedVerdict.testsPassed) -and $unrelatedReasons.Contains('outside.json'))
        Clear-AstraBeforeWrite
        $again = Open-TestPhaseExecutedContext -LogRoot (Join-Path $unrelatedDir 'log') -TimingRoot $unrelated.TimingRoot -RunId 'run' -Attempt '1' -Suite 'antiphon' -Source ('b' * 40) -InvocationId 'again'
        if ($null -ne $again -and $null -ne $again.Lock) { $held += $again }
        $againOpen = ($null -ne $again -and -not [bool]$again.Refused)
        $unrelatedOk = ($unrelatedFree -and $unrelatedFailed -and $againOpen)
        Assert-C487 $unrelatedOk 'AB JournalPublish.unrelated-releases-lock' ('free=' + $unrelatedFree + ' failed=' + $unrelatedFailed + ' again=' + $againOpen + ' reason=' + [string]$again.Reason + ' ' + $unrelatedReasons)
        $done['unrelated-releases-lock'] = $true

        $killRoot = Join-Path $root 'kill'
        New-Item -ItemType Directory -Path $killRoot -Force | Out-Null
        $stdout = Join-Path $killRoot 'child-out.txt'
        $stderr = Join-Path $killRoot 'child-err.txt'
        $proc = Start-Process -FilePath 'pwsh' -WorkingDirectory (Split-Path -Parent $PSScriptRoot) -ArgumentList @(
            '-NoProfile', '-File', $PSCommandPath, '-Case', 'JournalKillChild', '-ResultsDirectory', $killRoot
        ) -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $enteredPath = Join-Path $killRoot 'entered'
        $ackedPath = Join-Path $killRoot 'acked'
        $generationPath = Join-Path $killRoot 'generation.path'
        $enteredWatch = [Diagnostics.Stopwatch]::StartNew()
        while (-not [IO.File]::Exists($enteredPath) -and $enteredWatch.Elapsed.TotalSeconds -lt 90) { Start-Sleep -Milliseconds 50 }
        $ackWatch = [Diagnostics.Stopwatch]::StartNew()
        while (-not [IO.File]::Exists($ackedPath) -and $ackWatch.Elapsed.TotalMilliseconds -lt 1500) { Start-Sleep -Milliseconds 50 }
        $disk = 0
        if ([IO.File]::Exists($generationPath)) {
            $generation = [IO.File]::ReadAllText($generationPath).Trim()
            if (-not [string]::IsNullOrWhiteSpace($generation)) { $disk = Get-AstraDiskRecords $generation }
        }
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        $entered = [IO.File]::Exists($enteredPath)
        $killAcked = [IO.File]::Exists($ackedPath)
        $childError = ''
        $errorPath = Join-Path $killRoot 'child-error.txt'
        if ([IO.File]::Exists($errorPath)) { $childError = [IO.File]::ReadAllText($errorPath) }
        $killOk = ($entered -and -not ($killAcked -and $disk -eq 0))
        Assert-C487 $killOk 'AB JournalPublish.kill-keeps-acked-record' ('entered=' + $entered + ' acked=' + $killAcked + ' disk=' + $disk + ' ' + $childError)
        $done['kill-keeps-acked-record'] = $true
    } catch {
        $detail = $_.Exception.Message
        foreach ($name in @('ack-waits-for-disk','held-write-not-acked','throw-does-not-ack','close-holds-lock','unrelated-releases-lock','kill-keeps-acked-record')) {
            if (-not $done[$name]) { Assert-C487 $false ('AB JournalPublish.' + $name) $detail }
        }
    } finally {
        Clear-AstraBeforeWrite
        foreach ($ctx in $held) { Close-AstraLock $ctx }
        if ($script:NightlyDeferEvidenceWrites) {
            try { Disable-NightlyDeferredEvidenceWrites } catch { }
        }
    }
}

function Invoke-AstraJournalKillChild {
    $root = New-AstraResultsRoot
    $errorPath = Join-Path $root 'child-error.txt'
    try {
        Enable-NightlyDeferredEvidenceWrites
        $dir = Join-Path $root 'work'
        $ctx = Open-AstraContext $dir
        $generation = Get-AstraGenerationDir $ctx
        [IO.File]::WriteAllText((Join-Path $root 'generation.path'), $generation)
        $entered = Join-Path $root 'entered'
        Set-AstraBeforeWrite -Mode 'kill' -SignalPath $entered -SleepMs 20000
        $ok = Add-TestPhaseExecutedChunk -Context $ctx -Fresh (New-AstraFresh) -Suite 'antiphon' -Chunk 'chunk-1' -RunId 'run' -Attempt '1'
        if ($ok) { [IO.File]::WriteAllText((Join-Path $root 'acked'), 'acked') }
    } catch {
        [IO.File]::WriteAllText($errorPath, $_.Exception.ToString())
        exit 1
    }
}

function Test-AstraStaleCore {
    $root = New-AstraResultsRoot
    $dll = Join-Path $root 'stale-core.dll'
    $source = '
namespace Antiphon.Nightly {
    public static class TestPhaseJson {
        public static int Marker() { return 1; }
    }
}
'
    Add-Type -TypeDefinition $source -OutputAssembly $dll -OutputType Library
    try { Add-Type -Path $dll } catch {
        if (-not ('Antiphon.Nightly.TestPhaseJson' -as [type])) { throw }
    }
    . (Join-Path $PSScriptRoot 'lib/nightly-core.ps1')
    $script:NightlyCoreAvailable = $null
    $accepted = $false
    $reason = ''
    try { $accepted = [bool](Test-NightlyCoreAvailable) }
    catch { $reason = [string]$_.Exception.Message; $accepted = $false }
    $late = $false
    $lateError = ''
    if ($accepted) {
        . (Join-Path $PSScriptRoot 'lib/nightly-coverage.ps1')
        try {
            $null = [Antiphon.Nightly.NightlyChunkVerdict]::RosterDigest(@())
            $late = $true
        } catch {
            $late = $true
            $lateError = [string]$_.Exception.Message
        }
    }
    Assert-C487 (-not $accepted) 'AB StaleCore.refused' ('accepted=' + $accepted + ' reason=' + $reason)
    Assert-C487 ($reason.StartsWith('nightly-core-identity-mismatch')) 'AB StaleCore.named-reason' $reason
    Assert-C487 ($reason.Contains('NightlyCoverageParts')) 'AB StaleCore.missing-type-at-load' $reason
    Assert-C487 ($reason.Contains('start a fresh pwsh')) 'AB StaleCore.fresh-pwsh' $reason
    Assert-C487 (-not $late) 'AB StaleCore.no-late-call' $lateError
}

function New-AstraCompleteCore {
    . (Join-Path $PSScriptRoot 'lib/nightly-core.ps1')
    $identity = Get-NightlyCoreBuildIdentity
    if ($null -eq $identity) { throw 'identity-unavailable' }
    $other = [guid]::NewGuid().ToString('N').Substring(0, 24)
    if ([string]$identity.Hash -eq $other) { $other = ('0' * 24) }
    $dir = Join-Path ([IO.Path]::GetTempPath()) ('antiphon-nightly-core/Antiphon.Nightly-' + $other)
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $dll = Join-Path $dir 'Antiphon.Nightly.dll'
    $source = @'
namespace Antiphon.Nightly {
    public static class TestPhaseJson { public static int Marker() { return 1; } }
    public static class NightlyCoverageParts { public static int Marker() { return 1; } }
    public static class EvidenceFiles { public static int Marker() { return 1; } }
    public static class TestPhaseFiles { public static int Marker() { return 1; } }
}
'@
    Add-Type -TypeDefinition $source -OutputAssembly $dll -OutputType Library
    try { Add-Type -Path $dll } catch {
        if (-not ('Antiphon.Nightly.TestPhaseJson' -as [type])) { throw }
        if (-not ('Antiphon.Nightly.NightlyCoverageParts' -as [type])) { throw }
        if (-not ('Antiphon.Nightly.EvidenceFiles' -as [type])) { throw }
        if (-not ('Antiphon.Nightly.TestPhaseFiles' -as [type])) { throw }
    }
    return [pscustomobject]@{ Dll = [IO.Path]::GetFullPath($dll); Hash = [string]$identity.Hash }
}

function Test-AstraCompleteCorePath {
    . (Join-Path $PSScriptRoot 'lib/nightly-core.ps1')
    $core = New-AstraCompleteCore
    $script:NightlyCoreAvailable = $null
    $accepted = $false
    $reason = ''
    try { $accepted = [bool](Test-NightlyCoreAvailable) }
    catch { $reason = [string]$_.Exception.Message; $accepted = $false }
    $loaded = ''
    if ($reason -match 'loaded=([^\s;]+)') { $loaded = $Matches[1] }
    Assert-C487 (-not $accepted) 'AB CompleteCorePath.refused' ('accepted=' + $accepted + ' reason=' + $reason)
    Assert-C487 ($reason.StartsWith('nightly-core-identity-mismatch') -and $loaded -eq $core.Dll) 'AB CompleteCorePath.loaded-path' ('loaded=' + $loaded + ' dll=' + $core.Dll + ' reason=' + $reason)
    Assert-C487 (-not $reason.Contains('missing=')) 'AB CompleteCorePath.types-present' $reason
    Assert-C487 ($reason.Contains('start a fresh pwsh')) 'AB CompleteCorePath.fresh-pwsh' $reason
}

function Test-AstraCheckoutIdentity {
    . (Join-Path $PSScriptRoot 'lib/nightly-core.ps1')
    $core = New-AstraCompleteCore
    $script:NightlyCoreAvailable = $true
    $script:NightlyCoreVerifiedHash = 'stale-checkout-hash'
    $script:NightlyCoreVerifiedStamp = ''
    $rechecked = $false
    $reason = ''
    try { $null = Test-NightlyCoreAvailable }
    catch { $rechecked = $true; $reason = [string]$_.Exception.Message }
    $loaded = ''
    if ($reason -match 'loaded=([^\s;]+)') { $loaded = $Matches[1] }
    $recheckOk = ($rechecked -and $reason.StartsWith('nightly-core-identity-mismatch') -and $loaded -eq $core.Dll -and $reason.Contains('start a fresh pwsh'))
    Assert-C487 $recheckOk 'AB CheckoutIdentity.rechecked' ('rechecked=' + $rechecked + ' loaded=' + $loaded + ' dll=' + $core.Dll + ' reason=' + $reason)
    $script:NightlyCoreAvailable = $true
    $script:NightlyCoreVerifiedHash = $core.Hash
    $stable = $false
    $stableError = ''
    try { $stable = [bool](Test-NightlyCoreAvailable) }
    catch { $stable = $false; $stableError = [string]$_.Exception.Message }
    Assert-C487 $stable 'AB CheckoutIdentity.stable-hash' $stableError
}

switch ($Case) {
    'FlushFailure' {
        . (Join-Path $PSScriptRoot 'lib/test-phase-evidence.ps1')
        . (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
        Test-AstraFlushFailure
    }
    'JournalPublish' {
        . (Join-Path $PSScriptRoot 'lib/test-phase-evidence.ps1')
        . (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
        Test-AstraJournalPublish
    }
    'JournalKillChild' {
        . (Join-Path $PSScriptRoot 'lib/test-phase-evidence.ps1')
        Invoke-AstraJournalKillChild
        exit 0
    }
    'StaleCore' {
        . (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
        Test-AstraStaleCore
    }
    'CompleteCorePath' {
        . (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
        Test-AstraCompleteCorePath
    }
    'CheckoutIdentity' {
        . (Join-Path $PSScriptRoot 'lib/c487-harness.ps1')
        Test-AstraCheckoutIdentity
    }
    default { throw ('unknown case ' + $Case) }
}
$expected = 0
if ($Case -eq 'FlushFailure') { $expected = 10 }
elseif ($Case -eq 'JournalPublish') { $expected = 6 }
elseif ($Case -eq 'StaleCore') { $expected = 5 }
elseif ($Case -eq 'CompleteCorePath') { $expected = 4 }
elseif ($Case -eq 'CheckoutIdentity') { $expected = 2 }
Complete-C487Harness -ResultsDirectory (New-AstraResultsRoot) -ExpectedRows $expected
