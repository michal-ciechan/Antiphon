using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.TestHelpers.JournalFixtures;

namespace Antiphon.Tests.TestHelpers;

// CARD-1514 M3. The scale benchmarks, run by hand (W2-nightly S08; they were the m3-samples and m3-replay cases of
// ManifestPublicationHarness.ps1, which no test ran): the production publication against the pinned pre-journal writer
// on large legacy prefixes and a 385-chunk replay, with the same byte, count and per-chunk checks. MP_SOURCE_SHA names
// the measured source (40 hex); MP_M3_SMOKE=1 runs the small sizes. Metrics go to metrics.json in the work root printed.
[Category("Integration")]
[Explicit]
[NotInParallel]
public sealed class RcManifestScaleTests
{
    [Test]
    [Timeout(3_600_000)]
    public Task ScaleSamples(CancellationToken cancellationToken) => Invariant(() =>
    {
        var work = C1514Support.FreshRoot();
        Console.WriteLine("SCALE work=" + work);
        var metadata = Metadata();
        var prefixes = Smoke ? new[] { 0 } : new[] { 0, 240, 480 };
        var rows = new List<object>();
        var trx = Path.Combine(work, "current.trx");
        WriteTrx(trx, ResultRow("bench-current", "Passed"));
        foreach (var size in prefixes)
        {
            Console.WriteLine("SCALE prefix=" + size + " prepare");
            var root = Path.Combine(work, "prefix-" + size);
            var prod = Path.Combine(root, "prod");
            var oracle = Path.Combine(root, "oracle");
            Directory.Exists(root).ShouldBeFalse("scale-work-not-fresh");
            Directory.CreateDirectory(Path.Combine(prod, "timing"));
            Directory.CreateDirectory(Path.Combine(oracle, "timing"));
            var prep = Stopwatch.StartNew();
            var flat = Path.Combine(prod, "timing", "groups-executed.json");
            var wide = size * 152 / 240;
            if (size > 0)
            {
                WriteLegacyPrefix(flat, size, 89, 90, wide);
                File.Copy(flat, Path.Combine(oracle, "timing", "groups-executed.json"));
            }
            var before = Sha(flat);
            prep.Stop();
            var init = Stopwatch.StartNew();
            var ctx = Open(prod, "run-v1", "1", "antiphon", "unknown");
            init.Stop();
            ctx.Refused.ShouldBeFalse("scale-context-refused");
            var candidate = new List<double>();
            var legacy = new List<double>();
            try
            {
                var first = Stopwatch.StartNew();
                Publish(ctx, prod, trx, "antiphon", "bench-live", "run-v1", "1", 1.25, 0, false, false);
                first.Stop();
                for (var i = 0; i < 20; i++)
                {
                    if (i == 0) legacy.Add(Timed(() => LegacyPublish(oracle, trx, "antiphon", "bench-live", "run-v1", "1", 1.25)));
                    candidate.Add(Timed(() => Publish(ctx, prod, trx, "antiphon", "bench-live", "run-v1", "1", 1.25, 0, false, false)));
                    if (i == 1) legacy.Add(Timed(() => LegacyPublish(oracle, trx, "antiphon", "bench-live", "run-v1", "1", 1.25)));
                    Sha(flat).ShouldBe(before, "scale-flat-changed-before-projection");
                }
                var projection = Timed(() => Complete(ctx));
                ctx.LastCommitted.ShouldBe(21, "scale-lost-record");
                var digest = Sha(flat);
                digest.ShouldBe(Sha(Path.Combine(oracle, "timing", "groups-executed.json")), "scale-legacy-bytes-differ");
                using var doc = JsonDocument.Parse(File.ReadAllText(flat));
                var chunks = doc.RootElement.GetProperty("chunks");
                chunks.GetArrayLength().ShouldBe(size + 1, "scale-lost-seed-chunks");
                var outcomes = chunks.EnumerateArray().Sum(c => c.GetProperty("outcomes").GetArrayLength());
                var expected = size * 89 + wide + 1;
                outcomes.ShouldBe(expected, "scale-outcome-count");
                var records = RecordFiles(Path.Combine(prod, "timing")).Select(f => new FileInfo(f)).ToList();
                rows.Add(new
                {
                    prefixChunks = size, prefixOutcomes = expected - 1, preparationMs = prep.Elapsed.TotalMilliseconds, initializationMs = init.Elapsed.TotalMilliseconds,
                    firstPublicationMs = first.Elapsed.TotalMilliseconds, candidate = Stats(candidate), legacy = Stats(legacy), projectionMs = projection,
                    recordCount = records.Count, recordBytes = records.Sum(f => f.Length), projectionBytes = new FileInfo(flat).Length, sha256 = digest,
                });
                File.WriteAllText(Path.Combine(work, "metrics.json"), JsonSerializer.Serialize(new { metadata, phase = "samples", complete = false, rows }));
                Console.WriteLine("SCALE prefix=" + size + " p95-ms=" + Stats(candidate).p95Ms + " bytes-equal=true");
            }
            finally { Close(ctx); }
        }
        File.WriteAllText(Path.Combine(work, "metrics.json"), JsonSerializer.Serialize(new { metadata, phase = "samples", complete = true, rows }));
    });

    [Test]
    [Timeout(3_600_000)]
    public Task ScaleReplay(CancellationToken cancellationToken) => Invariant(() =>
    {
        var work = C1514Support.FreshRoot();
        Console.WriteLine("REPLAY work=" + work);
        var metadata = Metadata();
        var count = Smoke ? 3 : 385;
        var root = Path.Combine(work, "replay");
        Directory.Exists(root).ShouldBeFalse("replay-work-not-fresh");
        var prod = Path.Combine(root, "prod");
        var oracle = Path.Combine(root, "oracle");
        Directory.CreateDirectory(prod);
        Directory.CreateDirectory(oracle);
        var inputs = new List<(int Index, string Trx, string Outcome, string Suite, string Chunk, double Wall)>();
        var prep = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            var trx = Path.Combine(work, "inputs", "input-" + i.ToString("0000", CultureInfo.InvariantCulture) + ".trx");
            var outcome = new[] { "Passed", "Failed", "NotExecuted" }[i % 3];
            WriteTrx(trx, ResultRow("uid-" + i.ToString("0000", CultureInfo.InvariantCulture), outcome));
            inputs.Add((i, trx, outcome, new[] { "antiphon", "e2e" }[i % 2], "synthetic-" + i.ToString("0000", CultureInfo.InvariantCulture), 1.0 + i % 5 / 4.0));
        }
        prep.Stop();
        var init = Stopwatch.StartNew();
        var ctx = Open(prod, "replay-385", "1", "antiphon", "unknown");
        init.Stop();
        ctx.Refused.ShouldBeFalse("replay-context-refused");
        var candidate = new List<double>();
        var legacy = new List<double>();
        try
        {
            var flat = Path.Combine(prod, "timing", "groups-executed.json");
            foreach (var input in inputs)
            {
                if (input.Index % 16 == 0) Console.WriteLine("REPLAY publication=" + input.Index + "/" + count);
                var exitCode = input.Outcome == "Failed" ? 1 : 0;
                var passed = input.Outcome == "Passed";
                if (input.Index % 2 == 0)
                    legacy.Add(Timed(() => LegacyPublish(oracle, input.Trx, input.Suite, input.Chunk, "replay-385", "1", input.Wall, false, exitCode, false, passed)));
                candidate.Add(Timed(() => Publish(ctx, prod, input.Trx, input.Suite, input.Chunk, "replay-385", "1", input.Wall, exitCode, false, passed)));
                Sha(flat).ShouldBe("", "replay-projected-per-chunk");
                if (input.Index % 2 == 1)
                    legacy.Add(Timed(() => LegacyPublish(oracle, input.Trx, input.Suite, input.Chunk, "replay-385", "1", input.Wall, false, exitCode, false, passed)));
            }
            var projection = Timed(() => Complete(ctx));
            var digest = Sha(flat);
            digest.ShouldBe(Sha(Path.Combine(oracle, "timing", "groups-executed.json")), "replay-full-legacy-bytes-differ");
            using var doc = JsonDocument.Parse(File.ReadAllText(flat));
            var chunks = doc.RootElement.GetProperty("chunks");
            (chunks.GetArrayLength() == count && ctx.LastCommitted == count).ShouldBeTrue("replay-chunks-dropped");
            var actual = chunks.EnumerateArray().SelectMany(c => c.GetProperty("outcomes").EnumerateArray()).Select(o => o.GetProperty("testId").GetString() ?? "")
                .Order(StringComparer.Ordinal).ToList();
            var expected = inputs.Select(i => "uid-" + i.Index.ToString("0000", CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal).ToList();
            string.Join(",", actual).ShouldBe(string.Join(",", expected), "replay-uid-multiset");
            File.WriteAllText(Path.Combine(work, "metrics.json"), JsonSerializer.Serialize(new
            {
                metadata, phase = "replay", complete = true, publications = count, outcomes = actual.Count, preparationMs = prep.Elapsed.TotalMilliseconds,
                initializationMs = init.Elapsed.TotalMilliseconds, candidate = Stats(candidate), legacy = Stats(legacy), projectionMs = projection,
                projectionBytes = new FileInfo(flat).Length, sha256 = digest, historicalReplay = false,
            }));
            Console.WriteLine("REPLAY complete publications=" + count + " bytes-equal=true sha256=" + digest);
        }
        finally { Close(ctx); }
    });

    private static bool Smoke => Environment.GetEnvironmentVariable("MP_M3_SMOKE") == "1";

    private static double Timed(Action body)
    {
        var watch = Stopwatch.StartNew();
        body();
        return watch.Elapsed.TotalMilliseconds;
    }

    private sealed record Sample(int count, double medianMs, double p95Ms, double maxMs, double[] rawMs);

    // Get-MpSampleStats.
    private static Sample Stats(List<double> samples)
    {
        var sorted = samples.Order().ToList();
        sorted.Count.ShouldBeGreaterThan(0, "scale-no-samples");
        var median = sorted[(sorted.Count - 1) / 2];
        if (sorted.Count % 2 == 0) median = (median + sorted[sorted.Count / 2]) / 2;
        return new Sample(sorted.Count, median, sorted[(int)Math.Ceiling(0.95 * sorted.Count) - 1], sorted[^1], samples.ToArray());
    }

    // Get-MpScaleMetadata (the host is this test process; the oracle is JournalFixtures.LegacyPublish).
    private static object Metadata()
    {
        var sha = Environment.GetEnvironmentVariable("MP_SOURCE_SHA") ?? "";
        System.Text.RegularExpressions.Regex.IsMatch(sha, "^[0-9a-f]{40}$").ShouldBeTrue("scale-source-sha-required");
        var load = File.Exists("/proc/loadavg") ? File.ReadAllText("/proc/loadavg").Trim() : "unknown";
        return new
        {
            source = sha, runtime = RuntimeInformation.FrameworkDescription, os = Environment.OSVersion.VersionString, culture = CultureInfo.CurrentCulture.Name,
            timezone = TimeZoneInfo.Local.Id, host = Environment.MachineName, load, synthetic = true, nativeOwnerLaunches = 0, qualificationCredit = false,
            oracle = "JournalFixtures.LegacyPublish", perFilesystemOperationTimings = "unknown; total calls measured separately from initialization and projection",
        };
    }
}
