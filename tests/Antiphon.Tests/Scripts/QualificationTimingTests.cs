using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.TestPhase;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// The chunk timing sidecar, its terminal reads and the timing report (Antiphon.Nightly.QualificationTiming), called
// in-process: no PowerShell. These were the timing cases of scripts/test-qualification-timing.ps1. The class runs
// alone: the budget and read-log seams are process-wide.
[Category("Unit")]
[NotInParallel(nameof(QualificationTimingTests))]
public sealed class QualificationTimingTests
{
    private const string Run = "2026-10-27T00:00:00.0000000Z";

    [Test]
    public void Heartbeat_terminal_update_and_report()
    {
        var root = NewRoot();
        try
        {
            var log = Path.Combine(root, "logs");
            var timing = Path.Combine(log, "timing");
            Directory.CreateDirectory(log);
            Publish(new ChunkTimingUpdate { LogRoot = log, TimingRoot = timing, Suite = "antiphon", Chunk = "g1", LastPhase = "run", StartedAt = Run, QueueWaitSeconds = 1.5, BuildSeconds = 2, DiscoverySeconds = 0.25 });
            var side = Path.Combine(log, "antiphon-g1.timing.json");
            File.Exists(side).ShouldBeTrue("sidecar-beside-trx-slot");
            File.Exists(Path.Combine(timing, "antiphon-g1.timing.json")).ShouldBeTrue("copy-under-timing-root");
            var heart = Sidecar(side);
            heart.GetProperty("lastPhase").GetString().ShouldBe("run", "heartbeat-last-phase");
            heart.GetProperty("lastPhaseUtc").GetString().ShouldNotBeNullOrWhiteSpace("heartbeat-utc");
            heart.GetProperty("testCount").GetInt32().ShouldBe(0, "heartbeat-stays-non-terminal");

            Publish(new ChunkTimingUpdate { LogRoot = log, TimingRoot = timing, Suite = "antiphon", Chunk = "g1", LastPhase = "timeout", Why = "timeout", RunWallSeconds = 30, Terminal = true });
            var dead = Sidecar(side);
            dead.GetProperty("lastPhase").GetString().ShouldBe("timeout", "timeout-keeps-last-phase");
            dead.GetProperty("why").GetString().ShouldBe("timeout", "timeout-why");
            dead.GetProperty("startedAt").GetString().ShouldBe(Run, "timeout-keeps-start");
            dead.GetProperty("queueWaitSeconds").GetDouble().ShouldBe(1.5, "timeout-keeps-queue");
            dead.GetProperty("trxMissing").GetBoolean().ShouldBeTrue("timeout-without-trx");

            var trx = Path.Combine(log, "antiphon-g2.trx");
            File.WriteAllText(trx, TrxText(("Slow", "00:00:03"), ("Mid", "00:00:02"), ("Fast", "00:00:01")));
            Publish(new ChunkTimingUpdate
            {
                LogRoot = log, TimingRoot = timing, Suite = "antiphon", Chunk = "g2", TrxPath = trx, LastPhase = "complete",
                StartedAt = "2026-10-27T00:01:00.0000000Z", CompletedAt = "2026-10-27T00:02:00.0000000Z",
                QueueWaitSeconds = 0.1, BuildSeconds = 4, DiscoverySeconds = 0.2, RunWallSeconds = 9, Terminal = true,
            });
            var report = QualificationTiming.Report(log, timing);
            report.TimingRoot.ShouldBe(timing, "report-timing-root");
            var g2 = report.Chunks.Single(c => c.Chunk == "g2");
            g2.P50Seconds.ShouldBe(2, "p50");
            g2.P95Seconds.ShouldBe(3, "p95");
            g2.MaxSeconds.ShouldBe(3, "max");
            g2.TestCount.ShouldBe(3, "test-count");
            g2.Slowest.Length.ShouldBe(3, "slowest-three");
            g2.Slowest[0].Name.ShouldBe("Slow", "slowest-order");
            g2.TrxMissing.ShouldBeFalse("trx-found");
            report.Chunks.Single(c => c.Chunk == "g1").Why.ShouldBe("timeout", "no-trx-row-kept");
            var line = report.Timeline.Single(t => t.Suite == "antiphon");
            line.StartedAt.ShouldBe(Run, "timeline-start");
            line.CompletedAt.ShouldBe("2026-10-27T00:02:00.0000000Z", "timeline-end");
        }
        finally { Cleanup(root); }
    }

    [Test]
    public void Shared_fixtures_count_once_and_other_runs_are_ignored()
    {
        var root = NewRoot();
        try
        {
            var chunk = ChunkDir(root, "group-1", "new-run", "1", "antiphon");
            File.WriteAllLines(Path.Combine(chunk, "cases-11-abc.jsonl"), new[]
            {
                Header("new-run", "group-1", "1", "antiphon", 11, "abc"),
                FixtureLine("fix-1", "fixture-setup", 1000),
                "{\"recordKind\":\"complete\",\"phases\":[{\"phase\":\"fixture-setup\",\"startOffsetMs\":0,\"endOffsetMs\":500}]}",
            });
            File.WriteAllLines(Path.Combine(chunk, "cases-13-ghi.jsonl"), new[] { Header("new-run", "group-1", "1", "antiphon", 13, "ghi"), FixtureLine("fix-1", "fixture-setup", 1000) });
            File.WriteAllLines(Path.Combine(chunk, "cases-12-def.jsonl"), new[] { Header("old-run", "group-1", "0", "other", 12, "def"), CompleteLine("t", "process-wait", 0, 9000) });
            var sums = QualificationTiming.PhaseSums(root, "group-1", "new-run", "antiphon", "1");
            sums.FixtureSetupSeconds.ShouldBe(1.0, "shared-fixture-counted-once");
            sums.ProcessWaitSeconds.ShouldBeNull("stale-run-excluded");
            sums.ProcessSpawnSeconds.ShouldBeNull("unobserved-phases-stay-null");
            sums.FixtureTeardownSeconds.ShouldBeNull("unobserved-phases-stay-null");
        }
        finally { Cleanup(root); }
    }

    [Test]
    public void Malformed_journal_lines_stay_inside_the_budget()
    {
        var root = NewRoot();
        try
        {
            var log = Path.Combine(root, "logs");
            var timing = Path.Combine(log, "timing");
            var chunk = ChunkDir(timing, "g1", "run-bound", "1", "antiphon");
            File.WriteAllText(Path.Combine(chunk, "cases-99-zzzz.jsonl"), string.Concat(Enumerable.Repeat("{invalid\n", 4000)));
            var heart = Stopwatch.StartNew();
            Publish(new ChunkTimingUpdate { LogRoot = log, TimingRoot = timing, Suite = "antiphon", Chunk = "g1", LastPhase = "run", StartedAt = Run });
            heart.Stop();
            heart.Elapsed.TotalSeconds.ShouldBeLessThan(0.75, "heartbeat-parse-is-bounded");
            File.Exists(Path.Combine(log, "antiphon-g1.timing.json")).ShouldBeTrue("heartbeat-written");
            var read = Stopwatch.StartNew();
            var sums = QualificationTiming.PhaseSums(timing, "g1", "run-bound", "antiphon", "1");
            read.Stop();
            read.Elapsed.TotalSeconds.ShouldBeLessThan(0.75, "terminal-parse-is-bounded");
            sums.ProcessWaitSeconds.ShouldBeNull("terminal-parse-is-bounded");
        }
        finally { Cleanup(root); }
    }

    [Test]
    [Timeout(120_000)]
    public void A_five_thousand_result_trx_streams_inside_the_budget(CancellationToken cancellationToken)
    {
        var root = NewRoot();
        try
        {
            var log = Path.Combine(root, "logs");
            var timing = Path.Combine(log, "timing");
            Directory.CreateDirectory(timing);
            var trx = Path.Combine(log, "antiphon-g5.trx");
            using (var writer = new StreamWriter(trx, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                writer.WriteLine("<TestRun><Results>");
                for (var i = 1; i <= 5000; i++)
                {
                    var name = "T" + i.ToString("D4");
                    writer.Write("<UnitTestResult testId=\"" + name + "\" testName=\"" + name + "\" duration=\"" + TimeSpan.FromSeconds(i).ToString("c") + "\" outcome=\"Passed\"");
                    if (i == 2500) writer.Write("><Output><StdOut>ANTIPHON_TEST_PHASE_ID native-wide" + new string('x', 200000) + "</StdOut></Output></UnitTestResult>");
                    else writer.Write(" />");
                    writer.WriteLine();
                }
                writer.WriteLine("</Results></TestRun>");
            }
            new FileInfo(trx).Length.ShouldBeGreaterThan(262144, "trx-past-old-cap");
            var chunk = ChunkDir(timing, "g5", "run-5k", "1", "antiphon");
            using (var writer = new StreamWriter(Path.Combine(chunk, "cases-11-abc.jsonl"), false, new UTF8Encoding(false)))
            {
                writer.WriteLine(Header("run-5k", "g5", "1", "antiphon", 11, "abc"));
                writer.WriteLine(FixtureLine("fix-big", "fixture-setup", 1500));
                for (var i = 0; i < 4500; i++) writer.WriteLine("{\"recordKind\":\"complete\",\"testId\":\"n" + i + "\",\"pad\":\"" + new string('p', 80) + "\"}");
            }
            // Correctness uses an injected deadline well above the production budget, so load cannot refuse.
            var stats = QualificationTiming.TrxStats(trx, null, 10000);
            stats.Complete.ShouldBeTrue("five-thousand-complete");
            stats.Count.ShouldBe(5000, "five-thousand-count");
            stats.P50.ShouldBe(2500, "nearest-rank-p50");
            stats.P95.ShouldBe(4750, "nearest-rank-p95");
            stats.Max.ShouldBe(5000, "max");
            stats.Slowest.Length.ShouldBe(20, "slowest-twenty");
            stats.Slowest[0].Name.ShouldBe("T5000", "slowest-first");
            stats.Slowest[0].Seconds.ShouldBe(5000, "slowest-seconds");
            var phases = QualificationTiming.PhaseSums(timing, "g5", "run-5k", "antiphon", "1", Stopwatch.StartNew(), 10000);
            phases.Refused.ShouldBeFalse("phase-sums-complete");
            phases.FixtureSetupSeconds.ShouldBe(1.5, "phase-sums-past-old-caps");
            // Boundedness stays on the production 200 ms budget (2x the largest recorded sample as the ceiling).
            var bound = Stopwatch.StartNew();
            QualificationTiming.TrxStats(trx);
            bound.Stop();
            bound.Elapsed.TotalMilliseconds.ShouldBeLessThan(408, "bounded-read");
            var terminal = Stopwatch.StartNew();
            Publish(new ChunkTimingUpdate { LogRoot = log, TimingRoot = timing, Suite = "antiphon", Chunk = "g5", TrxPath = trx, LastPhase = "complete", StartedAt = Run, CompletedAt = "2026-10-27T00:05:00.0000000Z", RunId = "run-5k", Attempt = "1", Terminal = true });
            terminal.Stop();
            terminal.Elapsed.TotalMilliseconds.ShouldBeLessThan(628, "terminal-inside-budget");
            var doc = QualificationTiming.ReadSidecar(Path.Combine(log, "antiphon-g5.timing.json"));
            doc.ShouldNotBeNull();
            if (doc.Why == QualificationTiming.RefusedWhy)
            {
                doc.P50Seconds.ShouldBeNull("refused-gap-has-no-metrics");
                doc.TestCount.ShouldBe(0, "refused-gap-has-no-metrics");
            }
            else
            {
                doc.Why.ShouldBe("", "complete-publication");
                doc.TestCount.ShouldBe(5000, "complete-publication");
                doc.P95Seconds.ShouldBe(4750, "complete-publication");
                doc.FixtureSetupSeconds.ShouldBe(1.5, "complete-publication");
            }
        }
        finally { Cleanup(root); }
    }

    [Test]
    public void An_unterminated_trx_tag_stops_at_the_tag_cap()
    {
        var root = NewRoot();
        try
        {
            var log = Path.Combine(root, "logs");
            var timing = Path.Combine(log, "timing");
            Directory.CreateDirectory(timing);
            var trx = Path.Combine(log, "antiphon-g6.trx");
            using (var stream = File.Create(trx))
            {
                var head = Encoding.UTF8.GetBytes("<TestRun><Results><UnitTestResult testName=\"");
                stream.Write(head);
                var chunk = Encoding.UTF8.GetBytes(new string('x', 1024 * 1024));
                for (var n = 0; n < 8; n++) stream.Write(chunk);
            }
            // A stopped clock tests the scanner's 256 KiB tag cap rather than host scheduling.
            SetBudget(200);
            var reads = QualificationTiming.StartReadLog();
            Publish(new ChunkTimingUpdate { LogRoot = log, TimingRoot = timing, Suite = "antiphon", Chunk = "g6", TrxPath = trx, LastPhase = "complete", StartedAt = Run, RunId = "run-huge", Attempt = "1", Terminal = true });
            reads.TrxBytes.ShouldBeGreaterThan(0, "unterminated-trx-tag-bounded-read-bytes");
            reads.TrxBytes.ShouldBeLessThanOrEqualTo(262144 + 8192, "unterminated-trx-tag-bounded-read-bytes");
            var doc = QualificationTiming.ReadSidecar(Path.Combine(log, "antiphon-g6.timing.json"));
            doc.Why.ShouldBe(QualificationTiming.RefusedWhy, "unterminated-trx-tag-refused");
            doc.P50Seconds.ShouldBeNull("unterminated-trx-tag-null-metrics");
            doc.FixtureSetupSeconds.ShouldBeNull("unterminated-trx-tag-null-metrics");
            doc.Slowest.ShouldBeEmpty("unterminated-trx-tag-null-metrics");
        }
        finally { Cleanup(root); }
    }

    // The TRX cursor checks the deadline before every refill: a stream that sleeps on each small read stops
    // mid-document under the production budget, and the same stream finishes under a long one.
    [Test]
    public void The_trx_scanner_stops_at_the_deadline_before_the_next_read()
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<TestRun><Results>\n");
        var count = 0;
        const string row = "<UnitTestResult testName=\"D\" duration=\"00:00:01\" outcome=\"Passed\" />\n";
        while (xml.Length + row.Length + 22 < 900) { xml.Append(row); count++; }
        xml.Append("</Results></TestRun>\n");
        var bytes = Encoding.UTF8.GetBytes(xml.ToString());
        var stopping = new SlowStream(bytes, 32, 50);
        var watch = Stopwatch.StartNew();
        var stopped = TrxTimingScanner.Summarize(stopping, watch, 200, 262144);
        watch.Stop();
        stopped.Complete.ShouldBeFalse("stops-mid-document");
        stopping.Reads.ShouldBeGreaterThan(0);
        stopping.Reads.ShouldBeLessThan(stopping.DataChunks, "stops-before-the-next-read");
        watch.Elapsed.TotalMilliseconds.ShouldBeLessThan(400);
        var finishing = new SlowStream(bytes, 32, 50);
        var full = TrxTimingScanner.Summarize(finishing, Stopwatch.StartNew(), 10000, 262144);
        full.Complete.ShouldBeTrue("long-deadline-finishes");
        full.Count.ShouldBe(count, "long-deadline-finishes");
        finishing.Reads.ShouldBeGreaterThanOrEqualTo(finishing.DataChunks);
    }

    // A shared timing root holds one directory per chunk identity. The post-exit read lists only that directory and
    // reads only the chunk's own journal bytes; the owner file own-<identity prefix or whole>-<pid>-<nonce>.txt or the
    // journal's header proves ownership.
    [Test]
    [Timeout(120_000)]
    public void The_own_chunk_reads_only_its_own_journal(CancellationToken cancellationToken)
    {
        QualificationTiming.GroupToken("g-own").ShouldBe("672d6f776e", "group-token-vector");
        var root = NewRoot();
        try
        {
            var log = Path.Combine(root, "logs");
            var timing = Path.Combine(log, "timing");
            Directory.CreateDirectory(timing);
            var line = Encoding.UTF8.GetBytes("{\"recordKind\":\"complete\",\"testId\":\"t000000\",\"phases\":[{\"phase\":\"process-wait\",\"startOffsetMs\":0,\"endOffsetMs\":5000}]}\n");
            var body = new byte[line.Length * (1024 * 1024 / line.Length)];
            for (var i = 0; i + line.Length <= body.Length; i += line.Length) Buffer.BlockCopy(line, 0, body, i, line.Length);
            for (var n = 0; n < 40; n++)
            {
                var pid = 1000 + n;
                var nonce = "f" + n.ToString("00");
                var group = "other-" + n;
                var name = "cases-" + pid + "-" + nonce + ".jsonl";
                using (var stream = File.Create(Path.Combine(timing, name)))
                {
                    stream.Write(Encoding.UTF8.GetBytes(Header("other-run", group, "1", "antiphon", pid, nonce) + "\n"));
                    stream.Write(body);
                }
                File.WriteAllText(Path.Combine(timing, "own-" + QualificationTiming.GroupToken(group) + "-" + pid + "-" + nonce + ".txt"), name + "\n");
            }
            var own = ChunkDir(timing, "g-own", "run-own", "1", "antiphon");
            var ownJournal = Path.Combine(own, "cases-42-own1.jsonl");
            File.WriteAllText(ownJournal, string.Join("\n",
                Header("run-own", "g-own", "1", "antiphon", 42, "own1"),
                FixtureLine("fix-own", "fixture-setup", 1250),
                CompleteLine("own-a", "process-spawn", 0, 400)) + "\n");
            var identity = QualificationTiming.ChunkIdentity("g-own", "run-own", "1", "antiphon");
            File.WriteAllText(Path.Combine(own, "own-" + identity + "-42-own1.txt"), "cases-42-own1.jsonl\n");
            var trx = Path.Combine(log, "antiphon-g-own.trx");
            File.WriteAllText(trx, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<TestRun><!--" + new string(' ', 8200) + "--><Results>\n"
                + "<UnitTestResult testId=\"a\" testName=\"OwnA\" duration=\"00:00:01\" outcome=\"Passed\" />\n"
                + "<UnitTestResult testId=\"b\" testName=\"OwnB\" duration=\"00:00:03\" outcome=\"Passed\" />\n</Results></TestRun>\n");
            SetBudget(200);
            var reads = QualificationTiming.StartReadLog();
            Publish(new ChunkTimingUpdate { LogRoot = log, TimingRoot = timing, Suite = "antiphon", Chunk = "g-own", TrxPath = trx, LastPhase = "complete", StartedAt = Run, CompletedAt = "2026-10-27T00:01:00.0000000Z", RunId = "run-own", Attempt = "1", Terminal = true });
            reads.JournalBytes.ShouldBe(new FileInfo(ownJournal).Length, "own-chunk-reads-only-own-journal-bytes");
            reads.Listings.Count.ShouldBe(1, "own-chunk-lists-only-own-directory");
            reads.Listings[0].ShouldBe(own, "own-chunk-lists-only-own-directory");
            reads.ListedEntries[0].ShouldBe(2, "own-chunk-lists-only-own-directory");
            var doc = QualificationTiming.ReadSidecar(Path.Combine(log, "antiphon-g-own.timing.json"));
            doc.Why.ShouldBe("", "own-chunk-ignores-foreign-journals");
            doc.TestCount.ShouldBe(2);
            doc.P50Seconds.ShouldBe(1);
            doc.P95Seconds.ShouldBe(3);
            doc.MaxSeconds.ShouldBe(3);
            doc.FixtureSetupSeconds.ShouldBe(1.25);
            doc.ProcessSpawnSeconds.ShouldBe(0.4);
            doc.ProcessWaitSeconds.ShouldBeNull();
            doc.FixtureTeardownSeconds.ShouldBeNull();
        }
        finally { Cleanup(root); }
    }

    // CARD-1598: the 200 ms read deadline holds with 600 other journals in the shared timing root; a rerun does not
    // open the earlier attempt; an exhausted budget publishes a gap and the next publication recovers.
    [Test]
    [Timeout(120_000)]
    public void Six_hundred_journals_stay_inside_the_budget(CancellationToken cancellationToken)
    {
        var identity = QualificationTiming.ChunkIdentity("g-600", "run-600", "2", "antiphon");
        identity.ShouldBe("8d1c1172c1260d2d5c0925f638af059f3a8483e982fa7d086f3a20e44d17838f", "chunk-identity-vector");
        var root = NewRoot();
        try
        {
            var log = Path.Combine(root, "logs");
            var timing = Path.Combine(log, "timing");
            var crowd = ChunkDir(timing, "g-600", "run-600", "2", "antiphon");
            var line = Encoding.UTF8.GetBytes("{\"recordKind\":\"complete\",\"testId\":\"old\",\"phases\":[{\"phase\":\"process-wait\",\"startOffsetMs\":0,\"endOffsetMs\":5000}]}\n");
            using (var stream = File.Create(Path.Combine(timing, "cases-9-prior.jsonl")))
            {
                stream.Write(Encoding.UTF8.GetBytes(Header("run-600", "g-600", "1", "antiphon", 9, "prior") + "\n"));
                for (var i = 0; i < 4 * 1024 * 1024 / line.Length; i++) stream.Write(line);
            }
            File.WriteAllText(Path.Combine(timing, "own-" + QualificationTiming.GroupToken("g-600") + "-9-prior.txt"), "cases-9-prior.jsonl\n");
            for (var n = 0; n < 600; n++)
            {
                var pid = 2000 + n;
                var nonce = "c" + n.ToString("000");
                var name = "cases-" + pid + "-" + nonce + ".jsonl";
                File.WriteAllText(Path.Combine(timing, name), Header("other", "other-" + n, "1", "antiphon", pid, nonce) + "\n");
                File.WriteAllText(Path.Combine(timing, "own-" + QualificationTiming.GroupToken("other-" + n) + "-" + pid + "-" + nonce + ".txt"), name + "\n");
            }
            var ownJournal = Path.Combine(crowd, "cases-42-own1.jsonl");
            File.WriteAllText(ownJournal, string.Join("\n", Header("run-600", "g-600", "2", "antiphon", 42, "own1"), FixtureLine("fix-own", "fixture-setup", 1250), CompleteLine("own-a", "process-spawn", 0, 400)) + "\n");
            File.WriteAllText(Path.Combine(crowd, "own-" + identity + "-42-own1.txt"), "cases-42-own1.jsonl\n");
            var bareJournal = Path.Combine(crowd, "cases-7-bare.jsonl");
            File.WriteAllText(bareJournal, string.Join("\n", Header("run-600", "g-600", "2", "antiphon", 7, "bare"), FixtureLine("fix-bare", "fixture-teardown", 500)) + "\n");
            var foreign = QualificationTiming.ChunkIdentity("other", "run-600", "2", "antiphon");
            File.WriteAllText(Path.Combine(crowd, "own-" + foreign + "-7-bare.txt"), "cases-7-bare.jsonl\n");
            var trx = Path.Combine(log, "antiphon-g-600.trx");
            File.WriteAllText(trx, TrxText(("OwnA", "00:00:01")));
            var side = Path.Combine(log, "antiphon-g-600.timing.json");
            ChunkTimingUpdate Request(string why = null)
            {
                var update = new ChunkTimingUpdate { LogRoot = log, TimingRoot = timing, Suite = "antiphon", Chunk = "g-600", TrxPath = trx, LastPhase = "complete", StartedAt = "2026-10-28T00:00:00.0000000Z", CompletedAt = "2026-10-28T00:01:00.0000000Z", RunId = "run-600", Attempt = "2", Terminal = true };
                if (why != null) update.Why = why;
                return update;
            }

            QualificationTiming.BudgetDeadlineMilliseconds.ShouldBe(200, "terminal-reader-default-budget-unchanged");
            QualificationTiming.BudgetWatch.ShouldBeNull("terminal-reader-default-budget-unchanged");
            SetBudget(200);
            var reads = QualificationTiming.StartReadLog();
            Publish(Request());
            QualificationTiming.ReadLog = null;
            reads.Opens.Count.ShouldBe(4, "six-hundred-journals-only-own-bounded-reads");
            reads.Opens.ShouldAllBe(p => p == trx || p == ownJournal || p == bareJournal, "six-hundred-journals-only-own-bounded-reads");
            var complete = QualificationTiming.ReadSidecar(side);
            complete.Why.ShouldBe("", "six-hundred-journals-unexhausted-complete");
            complete.FixtureSetupSeconds.ShouldBe(1.25);
            complete.FixtureTeardownSeconds.ShouldBe(0.5);
            complete.ProcessSpawnSeconds.ShouldBe(0.4);
            complete.ProcessWaitSeconds.ShouldBeNull();
            complete.TestCount.ShouldBe(1);

            SetBudget(0);
            Publish(Request());
            var gap = QualificationTiming.ReadSidecar(side);
            gap.Why.ShouldBe(QualificationTiming.RefusedWhy, "six-hundred-journals-exhausted-refused");
            new double?[] { gap.FixtureSetupSeconds, gap.FixtureTeardownSeconds, gap.ProcessSpawnSeconds, gap.ProcessWaitSeconds, gap.P50Seconds, gap.P95Seconds, gap.MaxSeconds }
                .ShouldAllBe(v => v == null, "six-hundred-journals-exhausted-null-metrics");
            gap.TestCount.ShouldBe(0);
            gap.Slowest.ShouldBeEmpty();

            SetBudget(200);
            Publish(Request());
            var recovered = QualificationTiming.ReadSidecar(side);
            recovered.Why.ShouldBe("", "six-hundred-journals-next-publication-recovers");
            recovered.FixtureSetupSeconds.ShouldBe(1.25);
            recovered.TestCount.ShouldBe(1);

            // Without process memory the persisted gap must not stick either.
            SetBudget(0);
            Publish(Request());
            QualificationTiming.Forget(log, "antiphon", "g-600").ShouldBeTrue();
            SetBudget(200);
            Publish(Request());
            var fromDisk = QualificationTiming.ReadSidecar(side);
            fromDisk.Why.ShouldBe("", "six-hundred-journals-persisted-gap-recovers");
            fromDisk.TestCount.ShouldBe(1);

            Publish(Request("suite-failed"));
            QualificationTiming.ReadSidecar(side).Why.ShouldBe("suite-failed", "six-hundred-journals-explicit-failure-preserved");
        }
        finally { Cleanup(root); }
    }

    // A linked chunk-* directory is not a chunk: the phase reader refuses it, and reads a real one beside it.
    [Test]
    public void A_linked_chunk_directory_is_refused()
    {
        var root = NewRoot();
        try
        {
            var timing = Path.Combine(root, "timing");
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(timing);
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "cases-42-link1.jsonl"), Header("run-link", "g-link", "1", "antiphon", 42, "link1") + "\n" + FixtureLine("fix-link", "fixture-setup", 1500) + "\n");
            var link = Path.Combine(timing, QualificationTiming.ChunkDirectoryName("g-link", "run-link", "1", "antiphon"));
            try { Directory.CreateSymbolicLink(link, outside); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Skip.Test("creating a directory link needs SeCreateSymbolicLinkPrivilege (Windows developer mode): " + ex.Message);
                return;
            }
            var refused = QualificationTiming.PhaseSums(timing, "g-link", "run-link", "antiphon", "1");
            refused.Refused.ShouldBeTrue("symlinked-chunk-directory-is-refused");
            refused.FixtureSetupSeconds.ShouldBeNull("symlinked-chunk-directory-is-refused");
            QualificationTiming.DirectoryKind(link).ShouldBe("symlink");
            var real = ChunkDir(timing, "g-real", "run-link", "1", "antiphon");
            File.WriteAllText(Path.Combine(real, "cases-42-link1.jsonl"), Header("run-link", "g-real", "1", "antiphon", 42, "link1") + "\n" + FixtureLine("fix-real", "fixture-setup", 1500) + "\n");
            QualificationTiming.PhaseSums(timing, "g-real", "run-link", "antiphon", "1").FixtureSetupSeconds.ShouldBe(1.5, "real-chunk-beside-the-link-is-read");
        }
        finally { Cleanup(root); }
    }

    // A FIFO TRX, a 4 MB line, a mode-000 journal, a linked journal, a FIFO journal and a dense sidecar stay inside
    // the budget, open nothing unsafe and publish a gap. FIFOs and mode 000 are POSIX properties.
    [Test]
    public void Hostile_files_keep_the_publication_inside_the_budget()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("FIFOs and unix mode 000 are POSIX properties");
        var root = NewRoot();
        var denied = "";
        try
        {
            var log = Path.Combine(root, "logs");
            var timing = Path.Combine(log, "timing");
            var chunk = ChunkDir(timing, "g9", "agg", "1", "antiphon");
            var fifoTrx = Path.Combine(log, "antiphon-g9.trx");
            Mkfifo(fifoTrx).ShouldBe(0, "fifo-created");
            var huge = Path.Combine(chunk, "cases-80-huge.jsonl");
            using (var stream = File.Create(huge)) { var block = Encoding.UTF8.GetBytes(new string('x', 1024 * 1024)); for (var n = 0; n < 4; n++) stream.Write(block); }
            denied = Path.Combine(chunk, "cases-82-denied.jsonl");
            File.WriteAllText(denied, "{\"recordKind\":\"header\"}\n");
            Chmod(denied, 0).ShouldBe(0, "mode-000");
            File.CreateSymbolicLink(Path.Combine(chunk, "cases-83-link.jsonl"), huge);
            Mkfifo(Path.Combine(chunk, "cases-81-fifo.jsonl")).ShouldBe(0, "fifo-created");
            var dense = "{\"v\":[" + string.Concat(Enumerable.Repeat("0,", 100000)) + "0]}";
            var side = Path.Combine(log, "antiphon-g9.timing.json");
            File.WriteAllText(side, dense);
            var reads = QualificationTiming.StartReadLog();
            var wall = Stopwatch.StartNew();
            Publish(new ChunkTimingUpdate { LogRoot = log, TimingRoot = timing, Suite = "antiphon", Chunk = "g9", TrxPath = fifoTrx, LastPhase = "complete", StartedAt = Run, RunId = "agg", Attempt = "1", Terminal = true });
            wall.Stop();
            QualificationTiming.ReadLog = null;
            reads.ReadBytes.ShouldBeLessThanOrEqualTo(Encoding.UTF8.GetByteCount(dense) + 262144 + 8192, "post-run-hostile-files-bounded-reads");
            reads.Opens.Count.ShouldBeLessThanOrEqualTo(3);
            reads.Opens.ShouldAllBe(p => p == side || p == huge || p == denied);
            reads.Streams.ShouldAllBe(p => p == side || p == huge);
            wall.Elapsed.TotalSeconds.ShouldBeLessThan(1.5, "post-run-hostile-files-wall-budget");
            var doc = QualificationTiming.ParseSidecar(File.ReadAllText(side));
            doc.Why.ShouldBe(QualificationTiming.RefusedWhy, "post-run-hostile-files-keep-exit-inside-budget");
            doc.P50Seconds.ShouldBeNull();
            doc.FixtureSetupSeconds.ShouldBeNull();
            doc.ProcessWaitSeconds.ShouldBeNull();
            doc.Slowest.ShouldBeEmpty();
        }
        finally
        {
            if (denied.Length > 0 && File.Exists(denied)) Chmod(denied, Convert.ToUInt32("644", 8));
            Cleanup(root);
        }
    }

    // A FIFO journal is refused by lstat before any open; only the chunk's own directory is listed.
    [Test]
    public void A_fifo_journal_is_never_opened()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("FIFOs are POSIX properties");
        var root = NewRoot();
        try
        {
            var chunk = ChunkDir(root, "g1", "run-fifo", "1", "antiphon");
            Mkfifo(Path.Combine(chunk, "cases-81-fifo.jsonl")).ShouldBe(0, "fifo-created");
            var reads = QualificationTiming.StartReadLog();
            var wall = Stopwatch.StartNew();
            var sums = QualificationTiming.PhaseSums(root, "g1", "run-fifo", "antiphon", "1");
            wall.Stop();
            QualificationTiming.ReadLog = null;
            reads.Opens.ShouldBeEmpty("fifo-cases-file-never-opens-stream");
            reads.Listings.Count.ShouldBe(1, "fifo-cases-file-lists-only-own-chunk");
            reads.Listings[0].ShouldBe(chunk, "fifo-cases-file-lists-only-own-chunk");
            reads.ListedEntries[0].ShouldBe(1, "fifo-cases-file-lists-only-own-chunk");
            sums.ProcessWaitSeconds.ShouldBeNull("fifo-cases-file-refuses-unsafe-input");
            sums.Refused.ShouldBeTrue("fifo-cases-file-refuses-unsafe-input");
            wall.Elapsed.TotalSeconds.ShouldBeLessThan(0.5, "fifo-cases-file-in-process-budget");
        }
        finally { Cleanup(root); }
    }

    // A sidecar is read strictly: the writer's JSON object, never comments, trailing commas, an array, a BOM or a
    // document denser than 4000 commas or colons.
    [Test]
    public void Sidecars_are_read_strictly()
    {
        var plain = QualificationTiming.ParseSidecar("{\"suite\":\"antiphon\",\"chunk\":\"all-group-1\",\"trxMissing\":false,\"queueWaitSeconds\":1.5,\"buildSeconds\":3,\"startedAt\":\"2026-11-18T01:02:03.4567890Z\",\"slowest\":[{\"name\":\"A\",\"seconds\":0.25},{\"name\":\"B\",\"seconds\":1}],\"why\":null,\"empty\":{},\"list\":[]}");
        plain.ShouldNotBeNull();
        plain.Suite.ShouldBe("antiphon");
        plain.Chunk.ShouldBe("all-group-1");
        plain.TrxMissing.ShouldBeFalse();
        plain.QueueWaitSeconds.ShouldBe(1.5);
        plain.BuildSeconds.ShouldBe(3);
        plain.StartedAt.ShouldBe("2026-11-18T01:02:03.4567890Z", "stamps-stay-text");
        plain.Why.ShouldBe("", "null-text-is-empty");
        plain.Slowest.Select(s => (s.Name, s.Seconds)).ShouldBe(new[] { ("A", 0.25), ("B", 1.0) });
        QualificationTiming.ParseSidecar("{ /* note */ \"a\": 1 }").ShouldBeNull("comment");
        QualificationTiming.ParseSidecar("{\"a\":1,}").ShouldBeNull("trailing-comma");
        QualificationTiming.ParseSidecar("[1,2]").ShouldBeNull("root-array");
        QualificationTiming.ParseSidecar("{\"big\":123456789012345678901234567890,\"buildSeconds\":\"x\"}").BuildSeconds.ShouldBeNull("mistyped-field-is-null");
        var root = NewRoot();
        try
        {
            var bom = Path.Combine(root, "bom.json");
            File.WriteAllBytes(bom, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{\"suite\":\"a\"}")).ToArray());
            QualificationTiming.ReadSidecar(bom).ShouldBeNull("bom");
            var dense = Path.Combine(root, "dense.json");
            File.WriteAllText(dense, "{\"x\":[" + string.Join(",", Enumerable.Range(1, 4100)) + "]}");
            QualificationTiming.ReadSidecar(dense).ShouldBeNull("dense");
            var roundTrip = Path.Combine(root, "round.json");
            var written = new ChunkTiming { Suite = "s", Chunk = "c", Why = "é \"q\"", Slowest = new[] { new SlowTest { Name = "N", Seconds = 2 } }, P50Seconds = 0.1 };
            File.WriteAllText(roundTrip, CompactJson.Text(written));
            var read = QualificationTiming.ReadSidecar(roundTrip);
            read.Why.ShouldBe("é \"q\"", "round-trip");
            read.P50Seconds.ShouldBe(0.1, "round-trip");
            read.Slowest.Single().Name.ShouldBe("N", "round-trip");
            File.ReadAllText(roundTrip).ShouldContain("\"buildSlot\":\"not-used\"");
        }
        finally { Cleanup(root); }
    }

    // setup-phases.json keeps ISO-8601 UTC stamps; a rewritten phase replaces its row; the accounting finds the build
    // phases, the first start and overlaps.
    [Test]
    public void Setup_phases_keep_utc_stamps_and_account_once()
    {
        var root = NewRoot();
        try
        {
            QualificationTiming.WriteRunPhase(root, "npm-ci", "2026-10-30T10:00:00.5698141Z", "2026-10-30T12:00:00.5698141Z", 7200);
            QualificationTiming.WriteRunPhase(root, "npm-build", "2026-10-30T06:00:00.5698141Z", "2026-10-30T07:00:00.5698141Z", 3600);
            QualificationTiming.WriteRunPhase(root, "Bad Name", "x", "y", 1);
            var raw = File.ReadAllText(Path.Combine(root, "setup-phases.json"));
            raw.ShouldContain("\"startedAt\":\"2026-10-30T10:00:00.5698141Z\"", Case.Sensitive, "stamp-iso");
            raw.ShouldContain("\"endedAt\":\"2026-10-30T12:00:00.5698141Z\"", Case.Sensitive, "stamp-iso");
            raw.ShouldNotContain("Bad Name");
            var account = QualificationTiming.SetupAccounting(root);
            account.CountedOnce.ShouldBeTrue();
            account.SetupSeconds.ShouldBe(10800);
            account.PerChunkBuildSum.ShouldBe(3600, "build-phases");
            account.Overlapped.ShouldBeFalse("stamp-phases-overlapped");
            account.FirstStartUtcTicks.ShouldBe(DateTimeOffset.Parse("2026-10-30T10:00:00.5698141Z").UtcTicks, "stamp-ticks");
            QualificationTiming.WriteRunPhase(root, "npm-ci", "2026-10-30T06:30:00Z", "2026-10-30T06:40:00Z", 600);
            var replaced = QualificationTiming.SetupAccounting(root);
            replaced.SetupSeconds.ShouldBe(4200, "rewritten-phase-replaces-its-row");
            replaced.Overlapped.ShouldBeTrue("overlap-found");
            QualificationTiming.SetupAccounting(Path.Combine(root, "absent")).CountedOnce.ShouldBeFalse();
        }
        finally { Cleanup(root); }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "qtiming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string root)
    {
        QualificationTiming.Reset();
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void Publish(ChunkTimingUpdate update) => QualificationTiming.Update(update, DateTime.UtcNow);

    // A stopped clock: the budget's deadline decides, not host scheduling.
    private static void SetBudget(int deadline)
    {
        QualificationTiming.BudgetWatch = new Stopwatch();
        QualificationTiming.BudgetDeadlineMilliseconds = deadline;
    }

    private static JsonElement Sidecar(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static string ChunkDir(string timing, string chunk, string run, string attempt, string suite)
    {
        var dir = Path.Combine(timing, QualificationTiming.ChunkDirectoryName(chunk, run, attempt, suite));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Header(string run, string group, string attempt, string suite, int pid, string nonce) =>
        "{\"recordKind\":\"header\",\"version\":1,\"pid\":" + pid + ",\"nonce\":\"" + nonce + "\",\"runId\":\"" + run + "\",\"groupId\":\"" + group
        + "\",\"attempt\":\"" + attempt + "\",\"suite\":\"" + suite + "\",\"assembly\":\"Antiphon.Tests\"}";

    private static string FixtureLine(string id, string phase, int endMs) =>
        "{\"recordKind\":\"fixture\",\"fixtureId\":\"" + id + "\",\"phases\":[{\"phase\":\"" + phase + "\",\"startOffsetMs\":0,\"endOffsetMs\":" + endMs + "}]}";

    private static string CompleteLine(string testId, string phase, int startMs, int endMs) =>
        "{\"recordKind\":\"complete\",\"testId\":\"" + testId + "\",\"phases\":[{\"phase\":\"" + phase + "\",\"startOffsetMs\":" + startMs + ",\"endOffsetMs\":" + endMs + "}]}";

    private static string TrxText(params (string Name, string Duration)[] rows)
    {
        var text = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<TestRun><Results>\n");
        var id = 0;
        foreach (var (name, duration) in rows)
            text.Append("<UnitTestResult testId=\"t").Append(id++).Append("\" testName=\"").Append(name).Append("\" duration=\"").Append(duration).Append("\" outcome=\"Passed\" />\n");
        return text.Append("</Results></TestRun>\n").ToString();
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int Mkfifo(string path, uint mode = 420);

    [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
    private static extern int Chmod(string path, uint mode);

    // A stream that returns at most chunk bytes per read and sleeps before each.
    private sealed class SlowStream(byte[] data, int chunk, int delayMs) : Stream
    {
        private int position;
        public int Reads;
        public int DataChunks => data.Length < 1 ? 0 : (data.Length + chunk - 1) / chunk;

        public override int Read(byte[] buffer, int offset, int count)
        {
            Reads++;
            if (delayMs > 0) Thread.Sleep(delayMs);
            if (position >= data.Length || count < 1) return 0;
            var n = Math.Min(Math.Min(chunk, count), data.Length - position);
            Buffer.BlockCopy(data, position, buffer, offset, n);
            position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
