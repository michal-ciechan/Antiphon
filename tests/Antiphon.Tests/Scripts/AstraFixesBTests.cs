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
    // In-process (W2-nightly S07; it was the FlushFailure case of scripts/test-astra-fixes-b.ps1). The writes into the
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
            File.ReadAllText(file1).ShouldBe(old, "setup: replacement landed");

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

    [Test]
    public Task JournalPublish(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-astra-fixes-b.ps1", "AB", nameof(JournalPublish), 6,
            ["AB JournalPublish.ack-waits-for-disk", "AB JournalPublish.held-write-not-acked",
             "AB JournalPublish.throw-does-not-ack", "AB JournalPublish.close-holds-lock",
             "AB JournalPublish.unrelated-releases-lock", "AB JournalPublish.kill-keeps-acked-record"],
            ScriptHarnessOptions.Default with { ExecutionBudget = TimeSpan.FromSeconds(180) }, cancellationToken);

    [Test]
    public Task StaleCore(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-astra-fixes-b.ps1", "AB", nameof(StaleCore), 5,
            ["AB StaleCore.refused", "AB StaleCore.named-reason", "AB StaleCore.missing-type-at-load",
             "AB StaleCore.fresh-pwsh", "AB StaleCore.no-late-call"],
            ScriptHarnessOptions.Default with { ExecutionBudget = TimeSpan.FromSeconds(180) }, cancellationToken);

    [Test]
    public Task CompleteCorePath(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-astra-fixes-b.ps1", "AB", nameof(CompleteCorePath), 4,
            ["AB CompleteCorePath.refused", "AB CompleteCorePath.loaded-path",
             "AB CompleteCorePath.types-present", "AB CompleteCorePath.fresh-pwsh"],
            ScriptHarnessOptions.Default with { ExecutionBudget = TimeSpan.FromSeconds(180) }, cancellationToken);

    [Test]
    public Task CheckoutIdentity(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-astra-fixes-b.ps1", "AB", nameof(CheckoutIdentity), 2,
            ["AB CheckoutIdentity.rechecked", "AB CheckoutIdentity.stable-hash"],
            ScriptHarnessOptions.Default with { ExecutionBudget = TimeSpan.FromSeconds(180) }, cancellationToken);
}
