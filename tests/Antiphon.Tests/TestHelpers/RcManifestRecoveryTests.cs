using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.TestHelpers.JournalFixtures;

namespace Antiphon.Tests.TestHelpers;

// CARD-1514 M1. Crash, corruption, lock and live-reader witnesses, in-process on the production publication
// (W2-nightly S08; these were the v5-v8 cases of ManifestPublicationHarness.ps1, whose writers ran in child pwsh
// processes). A killed writer is a publication stopped for good at its commit barrier (an in-process barrier seam that
// never answers), with writer.lock released as the dying process would release it; the summary is the still-PowerShell
// production entry (scripts/summarize-test-phases.ps1). A missing barrier or successor is an assertion failure.
// NotInParallel: the evidence file system and the cultures are process-wide.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel]
public sealed class RcManifestRecoveryTests
{
    private static readonly string[] Stages = ["BeforeTempComplete", "AfterFlushBeforeRename", "AfterRenameBeforeAck", "AfterAckBeforeProjection"];

    [Test]
    [Timeout(120_000)]
    public Task KilledWriterRecoversCommittedPrefix(CancellationToken cancellationToken) => Invariant(() =>
    {
        var work = C1514Support.FreshRoot();
        foreach (var stage in Stages)
        {
            var childWork = Path.Combine(work, stage);
            Directory.CreateDirectory(childWork);
            var killed = KillAtBarrier(childWork, stage);
            killed.Wait(20_000).ShouldBeTrue("barrier-not-observed stage=" + stage + " wait=" + killed.State);
            killed.Kill();
            // The killed writer released writer.lock. Recover this directory in place.
            var trx = Path.Combine(childWork, "one.trx");
            var ctx = Open(childWork, "run-v5", "1", "antiphon", "unknown");
            ctx.Refused.ShouldBeFalse("recover-refused stage=" + stage + " " + ctx.Reason);
            Publish(ctx, childWork, trx, "antiphon", "chunk-c", "run-v5", "1", 1, 0, false, false);
            Complete(ctx);
            Close(ctx);
            var ids = ChunkIds(Path.Combine(childWork, "timing", "groups-executed.json"));
            var expectB = stage is "AfterRenameBeforeAck" or "AfterAckBeforeProjection";
            (ids.Contains("chunk-a") && ids.Contains("chunk-c") && ids.Contains("chunk-b") == expectB && ids.Distinct().Count() == ids.Count)
                .ShouldBeTrue("recovered-prefix stage=" + stage + " ids=" + string.Join(",", ids));
            var oracle = Path.Combine(childWork, "oracle");
            var expected = new List<string> { "chunk-a" };
            if (expectB) expected.Add("chunk-b");
            expected.Add("chunk-c");
            foreach (var chunkId in expected)
                LegacyPublish(childWork, trx, "antiphon", chunkId, "run-v5", "1", 1, false, 0, false, false, oracle);
            Sha(Path.Combine(oracle, "groups-executed.json")).ShouldBe(Sha(Path.Combine(childWork, "timing", "groups-executed.json")), "recovered-prefix-bytes-differ " + stage);
        }
    });

    [Test]
    [Timeout(180_000)]
    public Task CommittedCorruptionCannotFallBack(CancellationToken cancellationToken) => InvariantAsync(async () =>
    {
        var work = C1514Support.FreshRoot();
        SuccessorPrefixes(work);
        const string malformed = "NOT JSON {";
        var root = Path.Combine(work, "malformed");
        var flat = Path.Combine(root, "timing", "groups-executed.json");
        WriteText(flat, malformed);
        WriteText(Path.Combine(root, "timing", "groups-planned.json"), "{\"version\":1,\"runId\":\"run-v6\",\"attempt\":\"1\",\"suite\":\"antiphon\",\"assembly\":\"Antiphon.Tests\",\"groups\":[]}");
        try
        {
            var ctx = Open(root, "run-v6", "1", "antiphon", "unknown");
            ctx.Refused.ShouldBeFalse("corruption-refused " + ctx.Reason);
            var trx = Path.Combine(root, "one.trx");
            WriteTrx(trx, ResultRow("kept", "Passed"));
            Publish(ctx, root, trx, "antiphon", "c-new", "run-v6", "1", 1, 0, false, false);
            Complete(ctx);
            Close(ctx);
        }
        catch (Exception ex) when (ex is not ShouldAssertException)
        {
            throw new InvalidOperationException("corruption-not-isolated " + ex.Message, ex);
        }
        Directory.EnumerateFiles(Path.Combine(root, "timing"), "*", SearchOption.AllDirectories).Any(f => File.ReadAllText(f) == malformed)
            .ShouldBeTrue("malformed-bytes-not-preserved");
        var statePath = Path.Combine(root, "timing", "groups-executed.state.json");
        File.Exists(statePath).ShouldBeTrue("publication-state-missing-after-corruption");
        using (var state = JsonDocument.Parse(File.ReadAllText(statePath)))
            state.RootElement.GetProperty("publicationComplete").GetBoolean().ShouldBeFalse("publicationComplete-not-false");
        await Summarize(root, cancellationToken);
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "timing", "summary.json")));
        summary.RootElement.GetProperty("complete").GetBoolean().ShouldBeFalse("summary-complete-despite-corruption");
        summary.RootElement.GetProperty("quality").EnumerateArray().Any(t => t.GetString() == "publication-corruption")
            .ShouldBeTrue("summary-missing-publication-corruption");
    });

    [Test]
    [Timeout(90_000)]
    public Task TwoWritersRefuseAndGenerationsAdvance(CancellationToken cancellationToken) => Invariant(() =>
    {
        var work = C1514Support.FreshRoot();
        var holderWork = Path.Combine(work, "holder");
        Directory.CreateDirectory(holderWork);
        // The holder (a child pwsh in the harness) holds the writer while the second writer opens.
        var holder = Open(holderWork, "run-v7", "1", "antiphon", "unknown");
        holder.Refused.ShouldBeFalse("holder-refused " + holder.Reason);
        bool refused;
        try
        {
            var second = Open(holderWork, "run-v7", "1", "antiphon", "unknown");
            refused = second.Refused;
            Close(second);
        }
        finally { Close(holder); }
        refused.ShouldBeTrue("second-writer-not-refused");
        var other = Path.Combine(work, "other-root");
        Directory.CreateDirectory(other);
        var otherCtx = Open(other, "run-v7", "1", "antiphon", "unknown");
        otherCtx.Refused.ShouldBeFalse("independent-root-refused");
        Close(otherCtx);
        var after = Open(holderWork, "run-v7", "1", "antiphon", "unknown");
        after.Refused.ShouldBeFalse("resume-after-release-refused");
        Close(after);
    });

    [Test]
    [Timeout(120_000)]
    public Task LiveReaderSeesOnlyWholeRecords(CancellationToken cancellationToken) => InvariantAsync(async () =>
    {
        var work = C1514Support.FreshRoot();
        var childWork = Path.Combine(work, "held");
        Directory.CreateDirectory(childWork);
        var killed = KillAtBarrier(childWork, "AfterRenameBeforeAck");
        try
        {
            killed.Wait(20_000).ShouldBeTrue("live-reader-barrier-absent " + killed.State);
            var prefix = TestPhasePublication.ReadExecutedPrefix(childWork, "", "");
            var json = JsonSerializer.Serialize(prefix);
            using (JsonDocument.Parse(json)) { }
            WriteText(Path.Combine(childWork, "timing", "groups-planned.json"), "{\"version\":1,\"runId\":\"run-v5\",\"attempt\":\"1\",\"suite\":\"antiphon\",\"assembly\":\"Antiphon.Tests\",\"groups\":[]}");
            var current = Path.Combine(childWork, "timing", "groups-executed.journal", "current.json");
            var before = Sha(current);
            await Summarize(childWork, cancellationToken);
            Sha(current).ShouldBe(before, "summary-mutated-writer-state");
        }
        finally { killed.Kill(); }
    });

    // ---- the killed writer ----

    // Invoke-V5Child: chunk-a published by a writer of its own (Write-TestPhaseExecutedManifest's path with no bound
    // context), then chunk-b from a writer that stops for good at the barrier stage. The writer thread never resumes: what
    // is on disk is what the killed process left.
    private static KilledWriter KillAtBarrier(string root, string stage)
    {
        var trx = Path.Combine(root, "one.trx");
        WriteTrx(trx, ResultRow("a", "Passed"));
        Publish(null, root, trx, "antiphon", "chunk-a", "run-v5", "1", 1, 0, false, false).ShouldBeTrue("chunk-a");
        var killed = new KilledWriter(stage);
        var ctx = Open(root, "run-v5", "1", "antiphon", "unknown", Scope(barrier: killed.Barrier));
        ctx.Refused.ShouldBeFalse("child-refused " + ctx.Reason);
        killed.Context = ctx;
        var thread = new Thread(() =>
        {
            try { Publish(ctx, root, trx, "antiphon", "chunk-b", "run-v5", "1", 1, 0, false, false, Scope(barrier: killed.Barrier)); }
            catch (Exception ex) { killed.Failure = ex.Message; }
            killed.Exited.Set();
        }) { IsBackground = true, Name = "killed-writer-" + stage };
        thread.Start();
        return killed;
    }

    private sealed class KilledWriter(string stage)
    {
        private readonly ManualResetEventSlim signaled = new(false);
        private readonly ManualResetEventSlim never = new(false);
        public readonly ManualResetEventSlim Exited = new(false);
        public TestPhaseJournalContext? Context;
        public string Failure = "";

        // Wait-TestPhaseCommitBarrier at every stage: at the named one it signals and never answers.
        public Func<string, bool> Barrier => s =>
        {
            if (!string.Equals(s, stage, StringComparison.OrdinalIgnoreCase)) return true;
            signaled.Set();
            never.Wait();
            return false;
        };

        public string State => Exited.IsSet ? "exited " + Failure : "absent";

        // Wait-MpSignal: the barrier reached (false when the writer exited first or the wait timed out).
        public bool Wait(int milliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < deadline)
            {
                if (signaled.Wait(50)) return true;
                if (Exited.IsSet) return false;
            }
            return signaled.IsSet;
        }

        // Stop-MpProcess: the process is gone; the operating system releases its writer.lock.
        public void Kill()
        {
            var ctx = Context;
            if (ctx?.Lock is not null)
            {
                try { ctx.Lock.Dispose(); } catch (IOException) { }
            }
        }
    }

    // ---- successors of a valid prefix (Assert-MpSuccessorPrefixes, Invoke-MpPrefixSuccessor) ----

    private static void SuccessorPrefixes(string work)
    {
        foreach (var mode in new[] { "torn", "sealed", "identity" })
        {
            var root = Path.Combine(work, "prefix-" + mode);
            Directory.CreateDirectory(root);
            var ctx = Open(root, "prefix", "1", "antiphon", "src-a");
            ctx.Refused.ShouldBeFalse("prefix-parent-refused " + mode);
            var trx = Path.Combine(root, "one.trx");
            WriteTrx(trx, ResultRow("kept", "Passed"));
            for (var i = 1; i <= 3; i++) Publish(ctx, root, trx, "antiphon", "kept-" + i, "prefix", "1", i, 0, false, false);
            WriteText(Path.Combine(root, "expected.json"), TestPhaseJournal.ProjectedDocument(ctx) ?? "");
            var parent = ctx.Generation;
            if (mode == "sealed") Complete(ctx, "success");
            var parentDir = Path.Combine(root, "timing", "groups-executed.journal", parent);
            if (mode == "torn")
            {
                Publish(ctx, root, trx, "antiphon", "excluded-4", "prefix", "1", 4, 0, false, false);
                WriteText(Path.Combine(parentDir, "00000004.json"), "{\"torn\":");
            }
            Close(ctx);
            var before = Directory.GetFiles(parentDir).ToDictionary(f => f, Sha);
            var source = mode == "identity" ? "src-b" : "src-a";
            PrefixSuccessor(root, mode, source, parent);
            foreach (var (file, sha) in before) Sha(file).ShouldBe(sha, "prefix-parent-mutated " + mode);
        }
    }

    private static void PrefixSuccessor(string root, string mode, string source, string parent)
    {
        var ctx = Open(root, "prefix", "1", "antiphon", source);
        ctx.Refused.ShouldBeFalse("prefix-successor-refused " + mode);
        try
        {
            ctx.Generation.ShouldNotBe(parent, "prefix-generation-not-advanced " + mode);
            var expected = Sha(Path.Combine(root, "expected.json"));
            (ctx.HasSeed && Sha(ctx.SeedPath) == expected).ShouldBeTrue("successor-seed-lost-valid-prefix " + mode);
            Complete(ctx);
            var flat = Path.Combine(root, "timing", "groups-executed.json");
            Sha(flat).ShouldBe(expected, "successor-projection-lost-valid-prefix " + mode);
            string.Join(",", ChunkIds(flat)).ShouldBe("kept-1,kept-2,kept-3", "successor-prefix-rows " + mode);
            if (mode == "torn") ctx.Unresolved.ShouldBeTrue("successor-cleared-corruption");
            var trx = Path.Combine(root, "one.trx");
            Publish(ctx, root, trx, "antiphon", "successor-4", "prefix", "1", 4, 0, false, false);
            Complete(ctx);
            var oracle = Path.Combine(root, "successor-oracle");
            var chunks = new[] { "kept-1", "kept-2", "kept-3", "successor-4" };
            for (var i = 0; i < chunks.Length; i++)
                LegacyPublish(root, trx, "antiphon", chunks[i], "prefix", "1", i + 1, false, 0, false, false, oracle);
            Sha(flat).ShouldBe(Sha(Path.Combine(oracle, "groups-executed.json")), "successor-published-chunk-lost-prefix " + mode);
        }
        finally { Close(ctx); }
    }
}
