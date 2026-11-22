using System.Diagnostics;
using System.Reflection;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// Astra audit a5e3ce5c witnesses: a failed deferred write is a refusal the summary can see,
// a journal chunk is acknowledged only after its record is on disk, and a loaded core whose
// identity or required types do not match the cache is refused before a chunk runs.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AstraFixesBTests
{
    // In-process (W2-nightly S07; it was the driver's FlushFailure case). The writes into the
    // blocked directory fail in the write-behind queue (its BeforeWrite hook) where the driver made the directory
    // unwritable with chmod 500, so the case also runs on Windows. NotInParallel: the evidence file system is process-wide.
    [Test]
    [NotInParallel]
    public void FlushFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-b-" + Guid.NewGuid().ToString("n"));
        var blocked = Path.Combine(root, "flush-block");
        Directory.CreateDirectory(blocked);
        var file1 = Path.Combine(blocked, "one.json");
        var file2 = Path.Combine(blocked, "two.json");
        const string old = "{\"sequence\":1,\"publicationComplete\":true}";
        const string fresh = "{\"sequence\":2,\"publicationComplete\":false}";
        File.WriteAllText(file1, old);
        File.WriteAllText(file2, old);
        var block = true;
        var blockedPrefix = Path.GetFullPath(blocked) + Path.DirectorySeparatorChar;
        try
        {
            EvidenceFiles.EnableWriteBehind();
            SetBeforeWrite(path =>
            {
                if (block && Path.GetFullPath(path).StartsWith(blockedPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("astra-blocked " + path);
            });
            EvidenceFiles.WriteAtomic(file1, fresh, false);
            EvidenceFiles.WriteAtomic(file2, fresh, false);
            WaitLanded(file1, file2).ShouldBeTrue("setup: replacement stayed queued");
            (File.ReadAllText(file1) == old && File.ReadAllText(file2) == old).ShouldBeTrue("setup: replacement landed despite the injected write failure");

            var read = TestPhaseFiles.BoundedRead(file1, 4096, 5000, Stopwatch.StartNew());
            var detail = "Complete=" + read.Complete + " Refused=" + read.Refused + " Kind=" + read.Kind;
            (read.Complete && !read.Refused).ShouldBeFalse("AB FlushFailure.not-complete-stale " + detail);
            (read.Refused && read.Kind == "deferred-write-failed" && !read.Complete).ShouldBeTrue("AB FlushFailure.refused " + detail);

            var oneText = string.Join(" | ", EvidenceFiles.Flush(file1));
            (oneText.Contains("one.json") && !oneText.Contains("two.json")).ShouldBeTrue("AB FlushFailure.path-keeps-other " + oneText);

            var afterFlush = TestPhaseFiles.BoundedRead(file1, 4096, 5000, Stopwatch.StartNew());
            var pathStillFailed = EvidenceFiles.PathFailed(file1);
            (pathStillFailed && afterFlush.Refused && !afterFlush.Complete && afterFlush.Kind == "deferred-write-failed").ShouldBeTrue(
                "AB FlushFailure.path-still-failed PathFailed=" + pathStillFailed + " Complete=" + afterFlush.Complete + " Refused=" + afterFlush.Refused + " Kind=" + afterFlush.Kind);

            var restText = string.Join(" | ", EvidenceFiles.Flush());
            restText.Contains("two.json").ShouldBeTrue("AB FlushFailure.later-flush-keeps-other " + restText);

            var verdict = EvidenceFiles.DeferredWriteVerdict();
            var reasonText = string.Join(" | ", verdict.Reasons);
            (!verdict.CoverageComplete && !verdict.TestsPassed && reasonText.Contains("deferred-evidence-write-failed:")).ShouldBeTrue(
                "AB FlushFailure.verdict coverage=" + verdict.CoverageComplete + " tests=" + verdict.TestsPassed + " reasons=" + reasonText);

            block = false;
            const string retryText = "{\"sequence\":3,\"publicationComplete\":true}";
            EvidenceFiles.WriteAtomic(file1, retryText, false);
            WaitLanded(file1).ShouldBeTrue("setup: retry stayed queued");
            File.ReadAllText(file1).ShouldBe(retryText, "setup: retry did not land");
            var retryReported = EvidenceFiles.Flush(file1).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            var retryVerdict = EvidenceFiles.DeferredWriteVerdict();
            var retryReasons = string.Join(" | ", retryVerdict.Reasons);
            (retryReported.Count == 0 && EvidenceFiles.PathFailed(file1) && !retryVerdict.CoverageComplete && !retryVerdict.TestsPassed
                && retryReasons.Contains("one.json")).ShouldBeTrue(
                "AB FlushFailure.retry-keeps-failed reported=" + retryReported.Count + " pathFailed=" + EvidenceFiles.PathFailed(file1)
                + " coverage=" + retryVerdict.CoverageComplete + " reasons=" + retryReasons);

            var quality = TestPhaseJournal.PublicationQuality(root, "", null);
            quality.ShouldContain("deferred-evidence-write-failed", "AB FlushFailure.phase-quality-token tokens=" + string.Join(",", quality));

            var merged = EvidenceFiles.MergeDeferredWriteVerdict(new[] { "kept-reason" });
            var mergedReasons = string.Join(" | ", merged.Reasons);
            (mergedReasons.Contains("kept-reason") && mergedReasons.Contains("deferred-evidence-write-failed:") && !merged.CoverageComplete && !merged.TestsPassed)
                .ShouldBeTrue("AB FlushFailure.summary-calls-verdict coverage=" + merged.CoverageComplete + " tests=" + merged.TestsPassed + " reasons=" + mergedReasons);
        }
        finally
        {
            SetBeforeWrite(null);
            EvidenceFiles.DisableWriteBehind();
        }

        // Test-AstraVerdictFlushes: a write still in flight when the verdict is taken is waited for, and its failure counts.
        var target = Path.Combine(root, "inflight", "late.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            EvidenceFiles.EnableWriteBehind();
            SetBeforeWrite(_ =>
            {
                Thread.Sleep(2000);
                throw new IOException("astra-delay-throw");
            });
            EvidenceFiles.WriteAllText(target, "late");
            var verdict = EvidenceFiles.DeferredWriteVerdict();
            var reasons = string.Join(" | ", verdict.Reasons);
            (!verdict.CoverageComplete && !verdict.TestsPassed && reasons.Contains("deferred-evidence-write-failed:")).ShouldBeTrue(
                "AB FlushFailure.verdict-flushes coverage=" + verdict.CoverageComplete + " tests=" + verdict.TestsPassed + " reasons=" + reasons);
            WaitLanded(target);
        }
        finally
        {
            SetBeforeWrite(null);
            EvidenceFiles.DisableWriteBehind();
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // The queued writes of these paths have run (landed or failed).
    private static bool WaitLanded(params string[] paths)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 8000)
        {
            if (paths.All(p => EvidenceFiles.PendingLength(p) < 0)) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    // Set-AstraBeforeWrite: the write-behind queue's BeforeWrite hook (null clears it).
    private static void SetBeforeWrite(Action<string>? hook)
    {
        var buffered = typeof(EvidenceFiles).GetField("buffered", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        if (buffered is null)
        {
            if (hook is null) return;
            throw new InvalidOperationException("write-behind-off");
        }
        var queue = buffered.GetType().GetProperty("Queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(buffered)!;
        queue.GetType().GetProperty("BeforeWrite", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(queue, hook);
    }

    // In-process (W2-nightly S08; it was the driver's JournalPublish case) on the production
    // publication (TestPhasePublication) under the deferred evidence writes of a nightly run. The killed child is a
    // publication thread held inside its record's queued write and never waited for: what is on disk when it is
    // abandoned is what the killed process left. NotInParallel: the evidence file system is process-wide.
    [Test]
    [NotInParallel]
    [Timeout(180_000)]
    public void JournalPublish(CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-b-" + Guid.NewGuid().ToString("n"));
        var held = new List<TestPhaseJournalContext>();
        var mode = "";
        var snapshotLast = -1;
        var snapshotDisk = -1;
        var snapshotSeen = false;
        TestPhaseJournalContext? pausing = null;
        var pauseGeneration = "";
        var entered = new ManualResetEventSlim(false);
        var killRelease = new ManualResetEventSlim(false);
        Thread? killed = null;
        // Set-AstraBeforeWrite's modes: the queued write throws, or waits (kill), or pauses and snapshots the context.
        void Hook(string path)
        {
            if (mode == "throw") throw new IOException("astra-before-write");
            if (mode == "kill")
            {
                entered.Set();
                killRelease.Wait(20_000);
                return;
            }
            if (mode == "pause")
            {
                Thread.Sleep(2000);
                snapshotLast = pausing!.LastCommitted;
                snapshotDisk = File.Exists(Path.Combine(pauseGeneration, "00000001.json")) ? 1 : 0;
                snapshotSeen = true;
            }
        }
        try
        {
            EvidenceFiles.EnableWriteBehind();
            SetBeforeWrite(Hook);

            var pause = OpenAstra(Path.Combine(root, "pause"));
            held.Add(pause);
            pausing = pause;
            pauseGeneration = GenerationDir(pause);
            mode = "pause";
            var paused = TestPhasePublication.AddExecutedChunk(pause, AstraFresh(), "antiphon", "chunk-1", "run", "1", new TestPhasePublicationScope());
            var diskNow = DiskRecords(pauseGeneration);
            var wait = Stopwatch.StartNew();
            while (!snapshotSeen && wait.ElapsedMilliseconds < 5000) Thread.Sleep(50);
            mode = "";
            (paused && diskNow >= 1).ShouldBeTrue("AB JournalPublish.ack-waits-for-disk acked=" + paused + " diskNow=" + diskNow + " snapDisk=" + snapshotDisk);
            (snapshotSeen && snapshotLast == 0).ShouldBeTrue("AB JournalPublish.held-write-not-acked seen=" + snapshotSeen + " last=" + snapshotLast);

            var thrown = OpenAstra(Path.Combine(root, "throw"));
            held.Add(thrown);
            var throwGeneration = GenerationDir(thrown);
            var beforeLast = thrown.LastCommitted;
            mode = "throw";
            var acked = TestPhasePublication.AddExecutedChunk(thrown, AstraFresh(), "antiphon", "chunk-1", "run", "1", new TestPhasePublicationScope());
            var throwDisk = DiskRecords(throwGeneration);
            mode = "";
            var visible = FailureVisible();
            (!acked && thrown.LastCommitted == beforeLast && throwDisk == 0 && visible.Visible).ShouldBeTrue(
                "AB JournalPublish.throw-does-not-ack acked=" + acked + " last=" + thrown.LastCommitted + " disk=" + throwDisk + " " + visible.Detail);

            var locked = OpenAstra(Path.Combine(root, "lock"));
            held.Add(locked);
            var lockedJournal = TestPhaseJournal.JournalRoot(locked.TimingRoot);
            mode = "throw";
            EvidenceFiles.WriteAllText(Path.Combine(lockedJournal, "held-failure.json"), "held");
            TestPhasePublication.Close(locked);
            var reacquired = CanLock(Path.Combine(lockedJournal, "writer.lock"));
            mode = "";
            var lockVisible = FailureVisible();
            (!reacquired && lockVisible.Visible).ShouldBeTrue("AB JournalPublish.close-holds-lock reacquired=" + reacquired + " " + lockVisible.Detail);

            var unrelatedDir = Path.Combine(root, "unrelated");
            var unrelated = OpenAstra(unrelatedDir);
            held.Add(unrelated);
            mode = "throw";
            EvidenceFiles.WriteAllText(Path.Combine(unrelatedDir, "outside.json"), "outside");
            TestPhasePublication.Close(unrelated);
            var unrelatedFree = CanLock(Path.Combine(TestPhaseJournal.JournalRoot(unrelated.TimingRoot), "writer.lock"));
            var unrelatedVerdict = EvidenceFiles.DeferredWriteVerdict();
            var unrelatedReasons = string.Join(" | ", unrelatedVerdict.Reasons);
            var unrelatedFailed = !unrelatedVerdict.CoverageComplete && !unrelatedVerdict.TestsPassed && unrelatedReasons.Contains("outside.json");
            mode = "";
            var again = TestPhasePublication.Open(Path.Combine(unrelatedDir, "log"), unrelated.TimingRoot, "run", "1", "antiphon", new string('b', 40), "again",
                false, false, new TestPhasePublicationScope());
            if (again.Lock is not null) held.Add(again);
            var againOpen = !again.Refused;
            (unrelatedFree && unrelatedFailed && againOpen).ShouldBeTrue("AB JournalPublish.unrelated-releases-lock free=" + unrelatedFree + " failed=" + unrelatedFailed
                + " again=" + againOpen + " reason=" + again.Reason + " " + unrelatedReasons);

            // Invoke-AstraJournalKillChild: the record's write is held in the queue; the child must not ack before it lands.
            var kill = OpenAstra(Path.Combine(root, "kill", "work"));
            held.Add(kill);
            var killGeneration = GenerationDir(kill);
            var killAcked = false;
            var childError = "";
            mode = "kill";
            killed = new Thread(() =>
            {
                try { killAcked = TestPhasePublication.AddExecutedChunk(kill, AstraFresh(), "antiphon", "chunk-1", "run", "1", new TestPhasePublicationScope()); }
                catch (Exception ex) { childError = ex.ToString(); }
            }) { IsBackground = true, Name = "astra-kill-child" };
            killed.Start();
            entered.Wait(90_000, cancellationToken);
            var ackWatch = Stopwatch.StartNew();
            while (!Volatile.Read(ref killAcked) && ackWatch.ElapsedMilliseconds < 1500) Thread.Sleep(50);
            var disk = DiskRecords(killGeneration);
            var wasEntered = entered.IsSet;
            var wasAcked = Volatile.Read(ref killAcked);
            (wasEntered && !(wasAcked && disk == 0)).ShouldBeTrue("AB JournalPublish.kill-keeps-acked-record entered=" + wasEntered + " acked=" + wasAcked + " disk=" + disk + " " + childError);
        }
        finally
        {
            mode = "";
            killRelease.Set();
            killed?.Join(30_000);
            SetBeforeWrite(null);
            foreach (var ctx in held)
            {
                try { ctx.Lock?.Dispose(); } catch (IOException) { }
                ctx.Lock = null;
            }
            EvidenceFiles.DisableWriteBehind();
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // Open-AstraContext: a writer of its own log and timing roots; its queued metadata lands before the case acts.
    private static TestPhaseJournalContext OpenAstra(string dir)
    {
        var log = Path.Combine(dir, "log");
        var timing = Path.Combine(dir, "timing");
        Directory.CreateDirectory(log);
        Directory.CreateDirectory(timing);
        var ctx = TestPhasePublication.Open(log, timing, "run", "1", "antiphon", new string('a', 40), "run", false, false, new TestPhasePublicationScope());
        if (ctx.Refused) throw new InvalidOperationException("context-refused " + ctx.Reason);
        EvidenceFiles.Flush();
        return ctx;
    }

    // New-AstraFresh.
    private static System.Collections.Specialized.OrderedDictionary AstraFresh() => new(StringComparer.OrdinalIgnoreCase)
    {
        { "chunk", "chunk-1" }, { "runId", "run" }, { "attempt", "1" }, { "suite", "antiphon" }, { "hostWallSeconds", 0 }, { "trxMissing", true },
        { "passedCount", 0 }, { "outcomes", Array.Empty<object>() }, { "trx", "" }, { "trxDigest", "" }, { "exitCode", 0 }, { "coverageComplete", false },
        { "testsPassed", false },
    };

    private static string GenerationDir(TestPhaseJournalContext ctx) => Path.Combine(TestPhaseJournal.JournalRoot(ctx.TimingRoot), ctx.Generation);

    // Get-AstraDiskRecords: the records on disk (not the queue).
    private static int DiskRecords(string generationDir)
    {
        var loaded = new List<object>();
        TestPhaseJournalRead.ReadPrefix(generationDir, 1, false, loaded);
        return loaded.Count;
    }

    private static bool CanLock(string path)
    {
        try
        {
            using var probe = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
    }

    // Test-AstraFailureVisible: a failed deferred write is visible to a peek or, failing that, to a flush.
    private static (bool Visible, string Detail) FailureVisible()
    {
        var peek = EvidenceFiles.PeekFailures();
        var detail = "peek=" + string.Join(" | ", peek);
        if (peek.Any(p => !string.IsNullOrWhiteSpace(p))) return (true, detail);
        var flushed = EvidenceFiles.Flush();
        detail += " flush=" + string.Join(" | ", flushed);
        return (flushed.Any(f => !string.IsNullOrWhiteSpace(f)), detail);
    }

    // The loaded-core witnesses (W2-nightly S16 moved them out of their driver). Their subject is the loader,
    // scripts/lib/nightly-core.ps1 (Test-NightlyCoreAvailable and its identity check), which is still production
    // PowerShell: each case is a fresh pwsh that dot-sources nightly-core.ps1 alone and prints one PASS or FAIL row per
    // assertion, with the same row names the driver had.
    [Test]
    [Timeout(180_000)]
    public Task StaleCore(CancellationToken cancellationToken) =>
        RunLoaderCase(nameof(StaleCore), StaleCoreScript, 5,
            ["AB StaleCore.refused", "AB StaleCore.named-reason", "AB StaleCore.missing-type-at-load",
             "AB StaleCore.fresh-pwsh", "AB StaleCore.no-late-call"], cancellationToken);

    [Test]
    [Timeout(180_000)]
    public Task CompleteCorePath(CancellationToken cancellationToken) =>
        RunLoaderCase(nameof(CompleteCorePath), CompleteCoreScript + CompleteCorePathScript, 4,
            ["AB CompleteCorePath.refused", "AB CompleteCorePath.loaded-path",
             "AB CompleteCorePath.types-present", "AB CompleteCorePath.fresh-pwsh"], cancellationToken);

    [Test]
    [Timeout(180_000)]
    public Task CheckoutIdentity(CancellationToken cancellationToken) =>
        RunLoaderCase(nameof(CheckoutIdentity), CompleteCoreScript + CheckoutIdentityScript, 2,
            ["AB CheckoutIdentity.rechecked", "AB CheckoutIdentity.stable-hash"], cancellationToken);

    // One loader case: the script (after the shared head) in a fresh pwsh with the repository root and a results
    // directory; every row passes, exactly expected rows, each required row present. The fake complete core a case
    // planted in the core cache root (antiphon-nightly-core/Antiphon.Nightly-<other hash>) is removed afterwards.
    private static async Task RunLoaderCase(string name, string body, int expected, string[] required, CancellationToken cancellationToken)
    {
        var results = Path.Combine(Path.GetTempPath(), "astra-b-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(results);
        var script = Path.Combine(results, "case.ps1");
        await File.WriteAllTextAsync(script, LoaderHead + body, new System.Text.UTF8Encoding(false), cancellationToken);
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? ScriptHarnessProcessFixture.ResolveInstalledPowerShell() : "pwsh");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, C1514Support.RepoRoot(), results })
            start.ArgumentList.Add(argument);
        var ran = await C1514Support.RunProcess(start, cancellationToken);
        var rows = ran.Text.ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimEnd()).ToList();
        foreach (var planted in rows.Where(line => line.StartsWith("PLANTED ", StringComparison.Ordinal)).Select(line => line[8..]))
        {
            try { Directory.Delete(planted, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        try { Directory.Delete(results, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        ran.ExitCode.ShouldBe(0, name + "\n" + ran.Text);
        rows.ShouldNotContain(line => line.StartsWith("FAIL ", StringComparison.Ordinal), ran.Text);
        var passed = rows.Where(line => line.StartsWith("PASS AB ", StringComparison.Ordinal)).Select(line => line[5..]).ToList();
        passed.Count.ShouldBe(expected, name + " named assertion inventory\n" + ran.Text);
        foreach (var row in required)
            passed.ShouldContain(row, name + " must assert '" + row + "'\n" + ran.Text);
    }

    private const string LoaderHead = """
        param([string]$Repo, [string]$Results)
        $ErrorActionPreference = 'Stop'
        function Assert-AstraRow([bool]$Ok, [string]$Name, [string]$Detail) {
            if ($Ok) { Write-Output ('PASS ' + $Name) } else { Write-Output ('FAIL ' + $Name + ' - ' + $Detail) }
        }
        $coreLoader = Join-Path $Repo 'scripts/lib/nightly-core.ps1'

        """;

    // A stale loaded core (an older TestPhaseJson without the types the cache requires) is refused at load, by name,
    // and nothing later is called.
    private const string StaleCoreScript = """
        $dll = Join-Path $Results 'stale-core.dll'
        $source = 'namespace Antiphon.Nightly { public static class TestPhaseJson { public static int Marker() { return 1; } } }'
        Add-Type -TypeDefinition $source -OutputAssembly $dll -OutputType Library
        try { Add-Type -Path $dll } catch {
            if (-not ('Antiphon.Nightly.TestPhaseJson' -as [type])) { throw }
        }
        . $coreLoader
        $script:NightlyCoreAvailable = $null
        $accepted = $false
        $reason = ''
        try { $accepted = [bool](Test-NightlyCoreAvailable) }
        catch { $reason = [string]$_.Exception.Message; $accepted = $false }
        $late = $false
        $lateError = ''
        if ($accepted) {
            try {
                $null = [Antiphon.Nightly.NightlyChunkVerdict]::RosterDigest(@())
                $late = $true
            } catch {
                $late = $true
                $lateError = [string]$_.Exception.Message
            }
        }
        Assert-AstraRow (-not $accepted) 'AB StaleCore.refused' ('accepted=' + $accepted + ' reason=' + $reason)
        Assert-AstraRow ($reason.StartsWith('nightly-core-identity-mismatch')) 'AB StaleCore.named-reason' $reason
        Assert-AstraRow ($reason.Contains('NightlyCoverageParts')) 'AB StaleCore.missing-type-at-load' $reason
        Assert-AstraRow ($reason.Contains('start a fresh pwsh')) 'AB StaleCore.fresh-pwsh' $reason
        Assert-AstraRow (-not $late) 'AB StaleCore.no-late-call' $lateError
        """;

    // New-AstraCompleteCore: a core with every required type, loaded from another cache path than the checkout's
    // identity names (antiphon-nightly-core/Antiphon.Nightly-<other hash>).
    private const string CompleteCoreScript = """
        function New-AstraCompleteCore {
            . $coreLoader
            $identity = Get-NightlyCoreBuildIdentity
            if ($null -eq $identity) { throw 'identity-unavailable' }
            $other = [guid]::NewGuid().ToString('N').Substring(0, 24)
            if ([string]$identity.Hash -eq $other) { $other = ('0' * 24) }
            $dir = Join-Path ([IO.Path]::GetTempPath()) ('antiphon-nightly-core/Antiphon.Nightly-' + $other)
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            Write-Output ('PLANTED ' + [IO.Path]::GetFullPath($dir))
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
            $script:AstraCore = [pscustomobject]@{ Dll = [IO.Path]::GetFullPath($dll); Hash = [string]$identity.Hash }
        }

        """;

    // A complete core from another cache path is refused by its loaded path, not by a missing type.
    private const string CompleteCorePathScript = """
        . $coreLoader
        New-AstraCompleteCore
        $core = $script:AstraCore
        $script:NightlyCoreAvailable = $null
        $accepted = $false
        $reason = ''
        try { $accepted = [bool](Test-NightlyCoreAvailable) }
        catch { $reason = [string]$_.Exception.Message; $accepted = $false }
        $loaded = ''
        if ($reason -match 'loaded=([^\s;]+)') { $loaded = $Matches[1] }
        Assert-AstraRow (-not $accepted) 'AB CompleteCorePath.refused' ('accepted=' + $accepted + ' reason=' + $reason)
        Assert-AstraRow ($reason.StartsWith('nightly-core-identity-mismatch') -and $loaded -eq $core.Dll) 'AB CompleteCorePath.loaded-path' ('loaded=' + $loaded + ' dll=' + $core.Dll + ' reason=' + $reason)
        Assert-AstraRow (-not $reason.Contains('missing=')) 'AB CompleteCorePath.types-present' $reason
        Assert-AstraRow ($reason.Contains('start a fresh pwsh')) 'AB CompleteCorePath.fresh-pwsh' $reason
        """;

    // A verified hash that is not the checkout's makes the loader re-check (and refuse); the checkout's own hash is stable.
    private const string CheckoutIdentityScript = """
        . $coreLoader
        New-AstraCompleteCore
        $core = $script:AstraCore
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
        Assert-AstraRow $recheckOk 'AB CheckoutIdentity.rechecked' ('rechecked=' + $rechecked + ' loaded=' + $loaded + ' dll=' + $core.Dll + ' reason=' + $reason)
        $script:NightlyCoreAvailable = $true
        $script:NightlyCoreVerifiedHash = $core.Hash
        $stable = $false
        $stableError = ''
        try { $stable = [bool](Test-NightlyCoreAvailable) }
        catch { $stable = $false; $stableError = [string]$_.Exception.Message }
        Assert-AstraRow $stable 'AB CheckoutIdentity.stable-hash' $stableError
        """;
}
