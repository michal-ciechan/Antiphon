using System.Globalization;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.TestHelpers.JournalPublicationStandIn;

namespace Antiphon.Tests.TestHelpers;

// CARD-1514 M1. Publication witnesses on the compiled journal head (Antiphon.Nightly/TestPhaseJournal.cs), in-process
// (W2-nightly S07; these were the v1-v4, zone-map and zone-effective cases of ManifestPublicationHarness.ps1). The
// publication around the head is JournalPublicationStandIn until S08 ports it. The harness ran every case under the
// invariant culture; so do these. NotInParallel: the cases move the process zone (TZ, Linux) and count serialised bytes
// on the process-wide TestPhaseJson.Measure.
[Category("Integration")]
[NotInParallel]
public sealed class RcManifestJournalTests
{
    private static readonly DateTime Moment = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Unspecified);

    [Test]
    [Timeout(180_000)]
    public Task NextPublicationTouchesOnlyCurrentChunk(CancellationToken cancellationToken) => Invariant(() =>
    {
        var work = C1514Support.FreshRoot();
        var trx = Path.Combine(work, "one.trx");
        WriteTrx(trx, ResultRow("live", "Passed"));
        var zero = MeasurePublish(work, "p0", 0, trx);
        zero.PriorRows.ShouldBe(0, "prefix0 prior-rows");
        foreach (var (label, prefix, wide, wideChunks) in new[] { ("p2", 2, 0, 0), ("p240", 240, 90, 152), ("p480", 480, 90, 304) })
        {
            var probe = MeasurePublish(work, label, prefix, trx, wide, wideChunks);
            (probe.PriorRows == 0 && probe.HashSame && probe.Bytes == zero.Bytes).ShouldBeTrue(
                "prior-rows-or-cumulative-bytes " + label + " prior=" + probe.PriorRows + " bytes=" + probe.Bytes + " base=" + zero.Bytes + " hashSame=" + probe.HashSame);
        }
    });

    [Test]
    [Timeout(120_000)]
    public Task WholeRunWorkIsLinear(CancellationToken cancellationToken) => Invariant(() =>
    {
        var work = C1514Support.FreshRoot();
        var trx = Path.Combine(work, "one.trx");
        WriteTrx(trx, ResultRow("n", "Passed"));
        var totals = new Dictionary<int, long>();
        foreach (var count in new[] { 16, 32 })
        {
            var root = Path.Combine(work, "n" + count.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(root);
            var ctx = Open(root, "run-v2", "1", "antiphon", "unknown");
            ctx.Refused.ShouldBeFalse(ctx.Reason);
            var counters = new Counters();
            long bytes;
            try
            {
                StartCounting(counters);
                for (var i = 0; i < count; i++)
                {
                    counters.PublishedChunk = "c" + i.ToString("00", CultureInfo.InvariantCulture);
                    Publish(ctx, root, trx, "antiphon", counters.PublishedChunk, "run-v2", "1", 1.0, 0, false, false, counters);
                }
            }
            finally { bytes = StopCounting(counters); }
            Complete(ctx);
            var ids = ChunkIds(Path.Combine(root, "timing", "groups-executed.json"));
            Close(ctx);
            (counters.PriorRows == 0 && counters.FlatWrites == 0 && counters.HashCount == count && ids.Count == count).ShouldBeTrue(
                "linear-bound n=" + count + " prior=" + counters.PriorRows + " flatWrites=" + counters.FlatWrites + " hashes=" + counters.HashCount
                + " chunks=" + ids.Count + " bytes=" + bytes);
            totals[count] = bytes;
        }
        var ratio = totals[16] > 0 ? (double)totals[32] / totals[16] : 99.0;
        ratio.ShouldBeLessThan(2.6, "quadratic-bytes ratio=" + ratio);
    });

    [Test]
    [Timeout(180_000)]
    public Task ProjectionPreservesLegacyBytes(CancellationToken cancellationToken) => Invariant(() =>
    {
        var root = C1514Support.FreshRoot();
        var zones = new (string Name, int OffsetMinutes)[] { ("UTC", 0), ("Asia/Kolkata", 330), ("America/New_York", -240) };
        var applied = 0;
        var skipped = 0;
        var appliedWriters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zone in zones)
        {
            var work = Path.Combine(root, zone.Name.Replace('/', '_'));
            var write = InZone(zone.Name, zone.OffsetMinutes, () => WriteCorpora(work));
            if (IsZoneNotApplicable(write))
            {
                NoteZoneSkip(zone.Name + " write", write.Reason);
                skipped++;
                continue;
            }
            write.Ok.ShouldBeTrue(zone.Name + " " + write.Reason);
            applied++;
            appliedWriters.Add(zone.Name);
        }

        foreach (var writer in zones)
        {
            var source = Path.Combine(root, writer.Name.Replace('/', '_'), "prod", "timing");
            var expected = Path.Combine(root, writer.Name.Replace('/', '_'), "oracle", "timing", "groups-executed.json");
            foreach (var reducer in zones)
            {
                var label = writer.Name + "->" + reducer.Name;
                if (!appliedWriters.Contains(writer.Name))
                {
                    NoteZoneSkip(label, "writer-not-applicable");
                    skipped++;
                    continue;
                }
                var reduced = InZone(reducer.Name, reducer.OffsetMinutes, () => Reduce(source, expected));
                if (IsZoneNotApplicable(reduced))
                {
                    NoteZoneSkip(label, reduced.Reason);
                    skipped++;
                    continue;
                }
                reduced.Ok.ShouldBeTrue(label + " " + reduced.Reason);
                applied++;
            }
        }

        Console.WriteLine("zone-grid applied=" + applied + " skipped=" + skipped);
        if (OperatingSystem.IsWindows())
        {
            skipped.ShouldBeGreaterThan(0);
            applied.ShouldBeLessThan(12);
        }
        else
        {
            skipped.ShouldBe(0);
            applied.ShouldBe(12);
        }
    });

    [Test]
    [Timeout(120_000)]
    public Task LegacySeedAndReplacementOrder(CancellationToken cancellationToken) => Invariant(() =>
    {
        var work = C1514Support.FreshRoot();
        var oracle = Path.Combine(work, "oracle");
        var prod = Path.Combine(work, "prod");
        var trx = Path.Combine(work, "one.trx");
        var bad = Path.Combine(work, "bad.trx");
        WriteTrx(trx, ResultRow("s", "Passed"), ResultRow("f", "Failed"));
        File.WriteAllText(bad, "{");
        const string single = "{\"version\":1,\"runId\":\"\",\"attempt\":\"\",\"suite\":\"\",\"manufacturedPassed\":false,\"chunk\":\"legacy-1\",\"hostWallSeconds\":1,\"trxMissing\":true,\"passedCount\":0,\"outcomes\":[],\"trx\":\"\",\"trxDigest\":\"\",\"exitCode\":null,\"coverageComplete\":null,\"testsPassed\":null}";
        foreach (var dir in new[] { oracle, prod })
        {
            Directory.CreateDirectory(Path.Combine(dir, "timing"));
            File.WriteAllText(Path.Combine(dir, "timing", "groups-executed.json"), single);
        }
        var ctx = Open(prod, "run-v4", "1", "antiphon", "src-a");
        ctx.Refused.ShouldBeFalse(ctx.Reason);
        for (var i = 0; i < 64; i++)
        {
            var suite = i % 2 == 1 ? "e2e" : "antiphon";
            var chunk = "g" + (i % 7).ToString(CultureInfo.InvariantCulture);
            var trxPath = trx;
            if (i % 11 == 0) trxPath = Path.Combine(work, "missing.trx");
            if (i % 13 == 0) trxPath = bad;
            var attempt = (1 + i % 3).ToString(CultureInfo.InvariantCulture);
            var exit = i % 4;
            var coverage = i % 5 == 0;
            var passed = i % 4 == 0;
            LegacyPublish(oracle, trxPath, suite, chunk, "run-v4", attempt, 1.0 + i, false, exit, coverage, passed);
            Publish(ctx, prod, trxPath, suite, chunk, "run-v4", attempt, 1.0 + i, exit, coverage, passed);
        }
        Complete(ctx, "success");
        Close(ctx);
        Sha(Path.Combine(oracle, "timing", "groups-executed.json")).ShouldBe(Sha(Path.Combine(prod, "timing", "groups-executed.json")), "legacy-seed-bytes-differ");
        SettledJournal(Path.Combine(prod, "timing")).ShouldBe("");

        // v4-successor: the sealed generation is the parent of a new one, seeded with its projection.
        var successor = Open(prod, "run-v4", "1", "antiphon", "src-a");
        successor.Refused.ShouldBeFalse("successor-refused " + successor.Reason);
        try
        {
            var next = Path.Combine(work, "successor", "one.trx");
            WriteTrx(next, ResultRow("next", "Passed"));
            Publish(successor, prod, next, "antiphon", "g0", "run-v4", "1", 3, 0, false, false);
            Complete(successor);
        }
        finally { Close(successor); }
        Directory.GetDirectories(Path.Combine(prod, "timing", "groups-executed.journal")).Length.ShouldBeGreaterThanOrEqualTo(2, "successor-generations");
    });

    [Test]
    [Timeout(60_000)]
    public Task PinnedZoneMapsIanaWhenWindowsIdLookupRefusesIt(CancellationToken cancellationToken) => Invariant(() =>
    {
        // A lookup that knows Windows ids only (as .NET Framework's did).
        static TimeZoneInfo? WindowsShaped(string id)
        {
            if (id.Contains('/')) return null;
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { return null; } catch (InvalidTimeZoneException) { return null; }
        }

        var zone = TestPhaseJournal.ResolveTimeZone("Asia/Kolkata", WindowsShaped);
        zone.ShouldNotBeNull("windows-shaped-iana-unmapped");
        zone.Id.ShouldBe("India Standard Time", "windows-shaped-id=" + zone.Id);
        ((int)zone.GetUtcOffset(Moment).TotalMinutes).ShouldBe(330, "windows-shaped-offset");
        var ny = TestPhaseJournal.ResolveTimeZone("America/New_York", WindowsShaped);
        (ny is not null && ny.Id == "Eastern Standard Time").ShouldBeTrue("windows-shaped-new-york=" + ny?.Id);
        ((int)ny!.GetUtcOffset(Moment).TotalMinutes).ShouldBe(-240, "windows-shaped-new-york-offset");
        var utc = TestPhaseJournal.ResolveTimeZone("UTC", WindowsShaped);
        utc.ShouldNotBeNull("windows-shaped-utc-missing");
        ((int)utc.GetUtcOffset(Moment).TotalMinutes).ShouldBe(0, "windows-shaped-utc-offset");
        var real = TestPhaseJournal.ResolveTimeZone("Asia/Kolkata");
        real.ShouldNotBeNull("real-kolkata-unresolved");
        ((int)real.GetUtcOffset(Moment).TotalMinutes).ShouldBe(330, "real-kolkata-offset");
    });

    [Test]
    [Timeout(60_000)]
    public Task EffectiveZoneCheckRefusesPinnedWindowsId(CancellationToken cancellationToken) => Invariant(() =>
    {
        const string requested = "India Standard Time";
        var local = TimeZoneInfo.Local;
        var localOff = (int)local.GetUtcOffset(Moment).TotalMinutes;
        var pinned = TestPhaseJournal.PinnedTimeZone(requested, null);
        var pinnedOff = (int)pinned.GetUtcOffset(Moment).TotalMinutes;
        if (pinnedOff == localOff && pinned.Id == local.Id)
        {
            Console.WriteLine("SKIP zone-effective-not-applicable local=" + local.Id + " offset=" + localOff);
            return;
        }
        // The writer environment records the zone in effect (Local), not the pinned one.
        var nowOff = local.GetUtcOffset(DateTime.Now);
        var sign = nowOff.Ticks < 0 ? "-" : "+";
        var suffix = "|" + local.Id + "|" + sign + Math.Abs(nowOff.Hours).ToString("00", CultureInfo.InvariantCulture) + ":"
            + Math.Abs(nowOff.Minutes).ToString("00", CultureInfo.InvariantCulture);
        var writer = Writer();
        writer.EndsWith(suffix, StringComparison.Ordinal).ShouldBeTrue("zone-effective-writer=" + writer + " expected-suffix=" + suffix);
        // The applicability check the zone grid uses: a pinned Windows id must not count as in effect.
        var result = OffsetCheck(requested, pinnedOff);
        result.Ok.ShouldBeFalse("zone-effective-pinned-accepted pinned=" + pinned.Id + " offset=" + pinnedOff + " local=" + local.Id + " offset=" + localOff);
        result.Reason.StartsWith("zone-not-applicable", StringComparison.Ordinal).ShouldBeTrue(result.Reason);
        result.Reason.Contains("expected=330", StringComparison.Ordinal).ShouldBeTrue(result.Reason);
        result.Reason.Contains("actual=330", StringComparison.Ordinal).ShouldBeFalse(result.Reason);
        result.Reason.Contains(" id=", StringComparison.Ordinal).ShouldBeTrue(result.Reason);
        Console.WriteLine("zone-effective " + result.Reason);
    });

    // ---- the cases' steps ----

    private sealed record Measured(int PriorRows, long Bytes, bool HashSame, int FlatWrites, int HashCount);

    // Measure-MpPublish: a legacy prefix imported at open (uncounted), then one publication counted.
    private static Measured MeasurePublish(string work, string label, int prefixChunks, string trx, int wideOutcomes = 0, int wideChunks = 0)
    {
        var root = Path.Combine(work, label);
        Directory.CreateDirectory(root);
        var flat = Path.Combine(root, "timing", "groups-executed.json");
        if (prefixChunks > 0)
        {
            if (wideChunks > 0) WriteLegacyPrefix(flat, prefixChunks, 89, wideOutcomes, wideChunks);
            else WriteLegacyPrefix(flat, prefixChunks, 1);
        }
        var before = Sha(flat);
        var ctx = Open(root, "run-v1", "1", "antiphon", "unknown");
        ctx.Refused.ShouldBeFalse("open-refused " + ctx.Reason);
        var counters = new Counters { PublishedChunk = "live-1" };
        long bytes;
        try
        {
            StartCounting(counters);
            Publish(ctx, root, trx, "antiphon", "live-1", "run-v1", "1", 1.25, 0, false, false, counters);
        }
        finally { bytes = StopCounting(counters); }
        var after = Sha(flat);
        Close(ctx);
        var hashSame = prefixChunks == 0 || (before == after && before != "");
        return new Measured(counters.PriorRows, bytes, hashSame, counters.FlatWrites, counters.HashCount);
    }

    private static void StartCounting(Counters counters)
    {
        TestPhaseJson.MeasuredBytes = 0;
        counters.Counting = true;
        TestPhaseJson.Measure = true;
    }

    private static long StopCounting(Counters counters)
    {
        TestPhaseJson.Measure = false;
        counters.Counting = false;
        return TestPhaseJson.MeasuredBytes;
    }

    private static readonly string Date = "2026-10-30T03:02:00+05:00";

    // Invoke-V3Write in one zone: the legacy and the journal corpora give the same bytes, the journal keeps settled keys
    // and its settlement is a fixed point, and the tr-TR i/I suites replace each other as the legacy writer did.
    private static (bool Ok, string Reason) WriteCorpora(string work)
    {
        var oracle = Path.Combine(work, "oracle");
        var prod = Path.Combine(work, "prod");
        Directory.CreateDirectory(oracle);
        Directory.CreateDirectory(prod);
        Corpus(oracle, null);
        var ctx = Open(prod, Date, "2026-10-30T03:02:00Z", Date, "unknown");
        if (ctx.Refused) return (false, "open-refused " + ctx.Reason);
        try
        {
            Corpus(prod, ctx);
            Complete(ctx);
        }
        finally { Close(ctx); }
        if (Sha(Path.Combine(oracle, "timing", "groups-executed.json")) != Sha(Path.Combine(prod, "timing", "groups-executed.json")))
            return (false, "legacy-bytes-differ");
        var settled = SettledJournal(Path.Combine(prod, "timing"));
        if (settled.Length > 0) return (false, settled);
        var fixedPoint = FixedPoint(Path.Combine(prod, "timing"));
        if (fixedPoint.Length > 0) return (false, fixedPoint);
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        CultureInfo.CurrentCulture = tr;
        CultureInfo.CurrentUICulture = tr;
        try
        {
            var oracleTr = Path.Combine(work, "oracle-tr");
            var prodTr = Path.Combine(work, "prod-tr");
            Turkish(oracleTr, null);
            var ctxTr = Open(prodTr, "run-tr", "1", "I", "unknown");
            if (ctxTr.Refused) return (false, "open-refused-tr " + ctxTr.Reason);
            try
            {
                Turkish(prodTr, ctxTr);
                Complete(ctxTr);
            }
            finally { Close(ctxTr); }
            if (Sha(Path.Combine(oracleTr, "timing", "groups-executed.json")) != Sha(Path.Combine(prodTr, "timing", "groups-executed.json")))
                return (false, "tr-TR-i-replacement-bytes-differ");
            var ids = ChunkIds(Path.Combine(prodTr, "timing", "groups-executed.json"));
            if (ids.Count != 1) return (false, "tr-TR-i-count=" + ids.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
        return (true, "v3-write");
    }

    // Invoke-V3Reduce: the writer's journal projected in place in another zone gives the writer zone's legacy bytes, twice.
    private static (bool Ok, string Reason) Reduce(string source, string expected)
    {
        if (!Directory.Exists(source)) return (false, "reduce-source-missing");
        var flat = Path.Combine(source, "groups-executed.json");
        var state = Path.Combine(source, "groups-executed.state.json");
        if (File.Exists(flat)) File.Delete(flat);
        if (File.Exists(state)) File.Delete(state);
        var logRoot = Path.GetDirectoryName(source)!;
        ProjectOnly(logRoot);
        if (Sha(flat) != Sha(expected)) return (false, "reducer-timezone-bytes-differ");
        var again = Sha(flat);
        ProjectOnly(logRoot);
        if (Sha(flat) != again) return (false, "projection-not-stable");
        return (true, "v3-reduce");
    }

    // Invoke-MpCorpus: dates, a missing and an unreadable TRX, quotes and backslashes, soft hyphens, composed and
    // decomposed accents, and case-only chunk differences.
    private static void Corpus(string logRoot, TestPhaseJournalContext? ctx)
    {
        var trxDir = Path.Combine(logRoot, "trx");
        Directory.CreateDirectory(trxDir);
        var one = Path.Combine(trxDir, "one.trx");
        var many = Path.Combine(trxDir, "many.trx");
        var bad = Path.Combine(trxDir, "bad.trx");
        var uni = "id-" + (char)0x00E9 + "-slash\\";
        WriteTrx(one, ResultRow("one-pass", "Passed"));
        WriteTrx(many, ResultRow("many-pass", "Passed"), ResultRow("many-fail", "Failed"), ResultRow("many-skip", "NotExecuted"), ResultRow(uni, "Passed"));
        File.WriteAllText(bad, "not-trx");
        var missing = Path.Combine(trxDir, "missing.trx");
        var soft = "a" + (char)0x00AD + "b";
        var composed = "pre" + (char)0x00E9;
        var decomposed = "pre" + (char)0x0065 + (char)0x0301;
        var third = 1.0 / 3.0;
        var steps = new (string Trx, string Suite, string Chunk, string Run, string Attempt, double Wall, bool Null, int Exit, bool Cov, bool Pass)[]
        {
            (one, Date, "c-date", Date, "2026-10-30T03:02:00Z", third, false, 0, false, false),
            (missing, "antiphon", "c-miss", "run-v3", "1", 0, true, 0, false, false),
            (bad, "antiphon", "c-bad", "run-v3", "1", -0.0, false, 1, false, false),
            (many, "suite \"q\" \\", "c-many", "run-v3", "1", 1.2345678901234567, false, 0, true, false),
            (one, soft, "k-soft", "run-v3", "2", 1, false, 0, false, true),
            (many, "ab", "k-soft", "run-v3", "9", 2, false, 2, false, false),
            (one, composed, "k-accent", "run-v3", "1", 1, false, 0, false, false),
            (one, decomposed, "k-accent", "run-v3", "1", 4, false, 0, false, false),
            (one, "Antiphon", "CaseChunk", "run-v3", "1", 1, false, 0, false, false),
            (many, "antiphon", "casechunk", "run-v3", "1", 5, false, 0, false, true),
        };
        foreach (var step in steps)
        {
            if (ctx is null) LegacyPublish(logRoot, step.Trx, step.Suite, step.Chunk, step.Run, step.Attempt, step.Wall, step.Null, step.Exit, step.Cov, step.Pass);
            else if (step.Null) Publish(ctx, logRoot, step.Trx, step.Suite, step.Chunk, step.Run, step.Attempt, step.Wall, null, null, null);
            else Publish(ctx, logRoot, step.Trx, step.Suite, step.Chunk, step.Run, step.Attempt, step.Wall, step.Exit, step.Cov, step.Pass);
        }
    }

    // Invoke-MpTurkish: suites I and i of one chunk.
    private static void Turkish(string logRoot, TestPhaseJournalContext? ctx)
    {
        var trx = Path.Combine(logRoot, "trx", "tr.trx");
        WriteTrx(trx, ResultRow("tr-one", "Passed"));
        foreach (var suite in new[] { "I", "i" })
        {
            if (ctx is null) LegacyPublish(logRoot, trx, suite, "tr-chunk", "run-tr", "1", 1, false, 0, false, false);
            else Publish(ctx, logRoot, trx, suite, "tr-chunk", "run-tr", "1", 1, 0, false, false);
        }
    }

    // Test-MpSettledJournal: records carry the settled keys, and the projection does not leak the generation.
    private static string SettledJournal(string timing)
    {
        var root = Path.Combine(timing, "groups-executed.journal");
        if (!Directory.Exists(root)) return "settled-keys-absent: no-journal";
        var hit = RecordFiles(timing).Select(File.ReadAllText).Any(text =>
            text.Contains("\"settledRowJson\"", StringComparison.Ordinal) && text.Contains("\"settledSuiteKey\"", StringComparison.Ordinal)
            && text.Contains("\"settledChunkKey\"", StringComparison.Ordinal));
        if (!hit) return "settled-keys-absent";
        var currentPath = Path.Combine(root, "current.json");
        if (!File.Exists(currentPath)) return "settled-keys-absent: no-current";
        var generation = PsValue.Str(EnvelopeField(File.ReadAllText(currentPath), "generation"));
        var flat = Path.Combine(timing, "groups-executed.json");
        if (File.Exists(flat) && !string.IsNullOrWhiteSpace(generation) && File.ReadAllText(flat).Contains(generation, StringComparison.Ordinal))
            return "generation-leaked-into-v1";
        return "";
    }

    // Test-MpFixedPoint: settling a settled row gives the same row.
    private static string FixedPoint(string timing)
    {
        foreach (var file in RecordFiles(timing))
        {
            var settled = PsValue.Str(EnvelopeField(File.ReadAllText(file), "settledRowJson"));
            if (string.IsNullOrWhiteSpace(settled)) continue;
            var parsed = NightlyJson.ParseText(settled, 1024, true);
            var again = TestPhaseJson.Text(TestPhaseJournal.SettledChunk(parsed!), 0);
            if (again != settled) return "settlement-not-fixed-point " + Path.GetFileName(file);
        }
        return "";
    }

    // ---- zones and culture ----

    // Initialize-MpCulture: every case ran under the invariant culture.
    private static Task Invariant(Action body)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try { body(); }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
        return Task.CompletedTask;
    }

    // The harness ran each zone in a child with TZ set. In-process the zone is the process's: Linux reads TZ (the cached
    // zone is cleared before and after); Windows has the machine zone only. Assert-MpOffset then refuses a zone whose
    // offset is not in effect.
    private static (bool Ok, string Reason) InZone(string zone, int offsetMinutes, Func<(bool Ok, string Reason)> body)
    {
        var previous = Environment.GetEnvironmentVariable("TZ");
        if (!OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable("TZ", zone);
            TimeZoneInfo.ClearCachedData();
        }
        try
        {
            var check = OffsetCheck(zone, offsetMinutes);
            return check.Ok ? body() : check;
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                Environment.SetEnvironmentVariable("TZ", previous);
                TimeZoneInfo.ClearCachedData();
            }
        }
    }

    // Assert-MpOffset: dates in the projection follow the process zone, so a requested zone that did not move Local is
    // not the zone under test.
    private static (bool Ok, string Reason) OffsetCheck(string requested, int expected)
    {
        var tz = TimeZoneInfo.Local;
        var got = (int)tz.GetUtcOffset(Moment).TotalMinutes;
        if (got == expected) return (true, "");
        return (false, "zone-not-applicable requested=" + requested + " expected=" + expected + " actual=" + got + " id=" + tz.Id);
    }

    private static bool IsZoneNotApplicable((bool Ok, string Reason) result) =>
        !result.Ok && result.Reason.StartsWith("zone-not-applicable", StringComparison.Ordinal);

    private static void NoteZoneSkip(string label, string reason) => Console.WriteLine("SKIP zone-not-applicable " + label + " " + reason);
}
