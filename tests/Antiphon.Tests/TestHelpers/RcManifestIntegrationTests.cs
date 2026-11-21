using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.TestHelpers.JournalFixtures;

namespace Antiphon.Tests.TestHelpers;

// CARD-1514 M2. Real qualification publication and recovery (W2-nightly S08; these were the v9-v14 cases of
// ManifestPublicationHarness.ps1). The publication, the executed manifest and the summary are the production C#
// (TestPhasePublication, TestPhaseManifests, TestPhaseRunSummary: the summarize-test-phases command in-process, S09), in-process
// where a case drives them directly; the qualification loop is still PowerShell, so the cases start its production entry
// (scripts/nightly-tests.ps1 with a seams file, as the harness passed one).
// The harness ran the loop without deferred evidence writes; so do these (ANTIPHON_NIGHTLY_DEFERRED_WRITES=off).
// NotInParallel: the evidence file system and the cultures are process-wide.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel]
public sealed class RcManifestIntegrationTests
{
    private static readonly string Sha40 = new('a', 40);

    [Test]
    [Timeout(600_000)]
    public Task QualificationFinalizesOnEveryTerminalPath(CancellationToken cancellationToken) => InvariantAsync(async () =>
    {
        var work = C1514Support.FreshRoot();
        var one = Classes(("markdown-pdf", ["Fixture.One", "Fixture.Two"]));
        string[] suites = ["markdown-pdf"];
        var ok = await Qualify(work, "success", "success", suites, one, cancellationToken);
        AssertTerminalPublication(ok, "success", requireBarrier: true);
        var failed = await Qualify(work, "failed", "failed", suites, one, cancellationToken);
        AssertTerminalPublication(failed, "failed-run", requireBarrier: true);
        failed.ExitCode.ShouldNotBe(0, "failed-child-became-success");
        var timeout = await Qualify(work, "timeout", "timeout", suites, one, cancellationToken);
        AssertTerminalPublication(timeout, "timeout", requireBarrier: true);
        var missing = await Qualify(work, "missing", "missing-trx", suites, one, cancellationToken);
        AssertTerminalPublication(missing, "failed-run", requireBarrier: true);
        var thrown = await Qualify(work, "thrown", "throw", suites, Classes(("markdown-pdf", ["Fixture.One"])), cancellationToken);
        AssertTerminalPublication(thrown, "orchestration-error", requireBarrier: false);
        Text(thrown.Summary, "outcome").ShouldNotBe("", "orchestration-receipt-empty");
        var bare = await Qualify(work, "bare", "no-planned", suites, one, cancellationToken);
        AssertTerminalPublication(bare, "success", requireBarrier: true);
        File.Exists(Path.Combine(bare.Timing, "groups-planned.json")).ShouldBeFalse("planned-document-returned");
    });

    [Test]
    [Timeout(300_000)]
    public Task ExplicitSummaryAndLegacyCallerStayCompatible(CancellationToken cancellationToken) => Invariant(() =>
    {
        var work = C1514Support.FreshRoot();
        // The summarizer resumes a generation only of its own writer environment: these writers and the summarizer share
        // one scope (the harness wrote them in the summarizer's process).
        var scope = Scope();
        SeededSummaryLabels(work, scope);

        var legacy = Path.Combine(work, "legacy");
        WriteText(Path.Combine(legacy, "timing", "groups-planned.json"), Planned("run-v10", "antiphon"));
        WriteText(Path.Combine(legacy, "timing", "groups-executed.json"), "{\"version\":1,\"runId\":\"run-v10\",\"attempt\":\"1\",\"suite\":\"antiphon\",\"manufacturedPassed\":false,\"chunks\":[{\"chunk\":\"c1\",\"runId\":\"run-v10\",\"attempt\":\"1\",\"suite\":\"antiphon\",\"hostWallSeconds\":1,\"trxMissing\":true,\"manufacturedPassed\":false,\"passedCount\":0,\"outcomes\":[],\"trx\":\"one.trx\",\"trxDigest\":\"\",\"exitCode\":0,\"coverageComplete\":false,\"testsPassed\":false}]}");
        WriteDefinedTrx(Path.Combine(legacy, "one.trx"), "Fixture.One", "Passed");
        Summarize(legacy, scope: scope);
        using (var legacySummary = JsonDocument.Parse(File.ReadAllText(Path.Combine(legacy, "timing", "summary.json"))))
        {
            legacySummary.RootElement.GetProperty("complete").GetBoolean().ShouldBeFalse("legacy-complete-without-state");
            Strings(legacySummary.RootElement.GetProperty("quality")).ShouldContain("publication-state-missing", "legacy-missing-state-not-recorded");
        }

        // Complete-TestPhaseLaunch with no bound context (the legacy caller): its publication opens, projects and closes.
        var launch = Path.Combine(work, "launch");
        var launchTrx = Path.Combine(launch, "launch.trx");
        WriteDefinedTrx(launchTrx, "Fixture.Launch", "Passed");
        WriteText(Path.Combine(launch, "timing", "groups-planned.json"), Planned("run-v10", "antiphon"));
        Publish(null, launch, launchTrx, "antiphon", "c-launch", "run-v10", "1", 1, 0, true, true, scope);
        File.Exists(Path.Combine(launch, "timing", "groups-executed.json")).ShouldBeTrue("launch-snapshot-absent");

        var journal = Path.Combine(work, "journal");
        WriteText(Path.Combine(journal, "timing-hold"), "x");
        var ctx = Open(journal, "run-v10", "1", "antiphon", "unknown", scope);
        ctx.Refused.ShouldBeFalse("journal-context-refused");
        var trx = Path.Combine(journal, "alpha.trx");
        WriteDefinedTrx(trx, "Fixture.Alpha", "Passed");
        Publish(ctx, journal, trx, "antiphon", "c-alpha", "run-v10", "1", 1, 0, true, true, scope);
        var trxB = Path.Combine(journal, "beta.trx");
        WriteDefinedTrx(trxB, "Fixture.Beta", "Failed");
        Publish(ctx, journal, trxB, "other", "c-alpha", "run-v10", "1", 1, 0, true, true, scope);
        Close(ctx);
        var flat = Path.Combine(journal, "timing", "groups-executed.json");
        if (File.Exists(flat)) File.Delete(flat);
        WriteText(Path.Combine(journal, "timing", "groups-planned.json"), Planned("run-v10", "antiphon"));
        Summarize(journal, scope: scope);
        using (var joined = JsonDocument.Parse(File.ReadAllText(Path.Combine(journal, "timing", "summary.json"))))
        {
            var tests = joined.RootElement.GetProperty("tests").EnumerateArray().ToList();
            (tests.Count(t => Text(t, "groupId") == "c-alpha" && Text(t, "suite") == "antiphon") == 1
                && tests.Count(t => Text(t, "groupId") == "c-alpha" && Text(t, "suite") == "other") == 1).ShouldBeTrue("journal-only-lost-cross-suite-join");
        }
        File.Exists(flat).ShouldBeFalse("summary-rewrote-projection");
        // The command declares the recovery switch (antiphon-scripts summarize-test-phases --recover-executed-manifest).
        File.ReadAllText(Path.Combine(C1514Support.RepoRoot(), "scripts", "lib", "Antiphon.Scripts", "SummarizeTestPhasesCommand.cs"))
            .ShouldContain("\"--recover-executed-manifest\"", Case.Sensitive, "recovery-switch-absent");
        Summarize(journal, recover: true, scope: scope);
        File.Exists(flat).ShouldBeTrue("recovery-did-not-project");
        Directory.GetDirectories(Path.Combine(journal, "timing", "groups-executed.journal")).Any(d => File.Exists(Path.Combine(d, "seal.json")))
            .ShouldBeFalse("recovery-invented-seal");

        var externalLog = Path.Combine(work, "external-log");
        var externalTiming = Path.Combine(work, "external-timing");
        var external = Scope(qualificationTimingRoot: externalTiming, writer: scope.WriterEnvironment);
        var extCtx = Open(externalLog, "external", "1", "external-suite", "unknown", external, timingRoot: externalTiming);
        var extTrx = Path.Combine(externalLog, "external.trx");
        WriteDefinedTrx(extTrx, "Fixture.External", "Passed");
        Publish(extCtx, externalLog, extTrx, "external-suite", "external-chunk", "external", "1", 1, 0, false, false, external);
        Close(extCtx);
        var lines = Summarize(externalLog, recover: true, timingRoot: externalTiming, scope: scope);
        Regex.IsMatch(lines, "publication generation=.+ sequence=1").ShouldBeTrue("external-timing-progress-absent " + lines);
        File.Exists(Path.Combine(externalTiming, "groups-executed.json")).ShouldBeTrue("external-timing-recovery-absent");
        File.Exists(Path.Combine(externalLog, "timing", "groups-executed.json")).ShouldBeFalse("external-timing-recovery-wrong-root");
    });

    [Test]
    [Timeout(300_000)]
    public Task PublicationFailurePreservesVerdict(CancellationToken cancellationToken) => InvariantAsync(async () =>
    {
        var work = C1514Support.FreshRoot();
        var classes = Enumerable.Range(1, 34).Select(i => "Fixture.C" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var run = await Qualify(work, "outage", "success", ["markdown-pdf"], Classes(("markdown-pdf", classes)), cancellationToken, faultAt: 2, faultUntil: 33, faultRemaining: 2);
        run.FlatAtBarrier.ShouldNotBe("present", "outage-flat-present-before-terminal");
        // The prefix is read in the core: count the rescans that read it (one per recovery), as the writer saw them.
        (run.PrefixRescans <= 2).ShouldBeTrue("outage-rescanned-per-chunk reads=" + run.PrefixRescans);
        run.Seal.ShouldNotBeNull("outage-terminal-seal-absent");
        var rows = JournalRows(run.Timing);
        rows.Count(r => r.Kind == "chunk" && r.Chunk == "same-class-34").ShouldBe(1, "outage-later-chunk-not-committed");
        var gaps = rows.Count(r => r.Kind == "gap");
        (gaps >= 32).ShouldBeTrue("outage-gaps " + gaps);
        var statePath = Path.Combine(run.Timing, "groups-executed.state.json");
        File.Exists(statePath).ShouldBeTrue("outage-state-absent");
        using (var state = JsonDocument.Parse(File.ReadAllText(statePath)))
            state.RootElement.GetProperty("publicationComplete").GetBoolean().ShouldBeFalse("outage-marked-publication-complete");
        var summaryPath = Path.Combine(run.Timing, "summary.json");
        File.Exists(summaryPath).ShouldBeTrue("outage-summary-absent");
        using (var summary = JsonDocument.Parse(File.ReadAllText(summaryPath)))
        {
            summary.RootElement.GetProperty("complete").GetBoolean().ShouldBeFalse("outage-summary-complete");
            Strings(summary.RootElement.GetProperty("quality")).ShouldContain("publication-gap", "outage-summary-missing-gap");
        }
        (Bool(run.Summary, "testsPassed") && Bool(run.Summary, "coverageComplete")).ShouldBeTrue("publication-fault-changed-verdict");
        Matrix(work);
    });

    [Test]
    [Timeout(1_200_000)]
    public Task LedgerAndReceiptBytesAreUnchanged(CancellationToken cancellationToken) => InvariantAsync(async () =>
    {
        var work = C1514Support.FreshRoot();
        var classes = Classes(("markdown-pdf", ["Fixture.One", "Fixture.Two", "Fixture.Three", "Fixture.Four", "Fixture.Five", "Fixture.Six"]));
        foreach (var outcome in new[] { "success", "failed", "missing-trx" })
        {
            var baseline = await Qualify(work, outcome + "-none", outcome, ["markdown-pdf"], classes, cancellationToken, publication: "none");
            File.Exists(Path.Combine(baseline.Timing, "groups-executed.journal", "current.json")).ShouldBeFalse("no-journal-control-created-journal");
            if (outcome == "success") (Bool(baseline.Summary, "succeeded") && baseline.ExitCode == 0).ShouldBeTrue("verdict-baseline-not-green");
            else (Bool(baseline.Summary, "succeeded") || baseline.ExitCode == 0).ShouldBeFalse("verdict-baseline-not-red " + outcome);
            var expected = VerdictOutputs(baseline);
            foreach (var publication in new[] { "clean", "gap", "torn", "missing-state", "held" })
            {
                var gap = publication == "gap";
                var run = await Qualify(work, outcome + "-" + publication, outcome, ["markdown-pdf"], classes, cancellationToken,
                    faultAt: gap ? 2 : 0, faultUntil: gap ? 5 : 0, faultRemaining: 2, publication: publication);
                var actual = VerdictOutputs(run);
                File.WriteAllText(Path.Combine(run.State, "verdict-outputs.json"), actual);
                actual.ShouldBe(expected, "publication-changed-real-verdict " + outcome + "/" + publication);
                run.FlatAtBarrier.ShouldBe("absent", "verdict-flat-before-terminal " + publication);
                if (publication == "held")
                {
                    File.Exists(Path.Combine(run.Timing, "groups-executed.journal", "current.json")).ShouldBeFalse("held-writer-did-not-refuse");
                    File.Exists(Path.Combine(run.Timing, "phase-summary-failed")).ShouldBeTrue("held-writer-marker-absent");
                }
                else if (publication != "clean")
                {
                    var quality = TestPhaseJournal.PublicationQuality(run.Log, run.Timing, null);
                    var token = publication switch { "torn" => "publication-corruption", "missing-state" => "publication-state-missing", _ => "publication-" + publication };
                    quality.ShouldContain(token, "verdict-fault-not-observed " + publication + " " + string.Join(",", quality));
                }
            }
        }
    });

    [Test]
    [Timeout(300_000)]
    public Task ProductionLoopPublishesObservableProgress(CancellationToken cancellationToken) => InvariantAsync(async () =>
    {
        var work = C1514Support.FreshRoot();
        var run = await Qualify(work, "progress", "success", ["markdown-pdf"], Classes(("markdown-pdf", ["Fixture.One", "Fixture.Two"])), cancellationToken);
        run.FlatAtBarrier.ShouldBe("absent", "progress-flat-present-before-terminal " + run.FlatAtBarrier);
        run.StampAtBarrier.ShouldBe("stamped", "progress-stamp-" + run.StampAtBarrier);
        var log = Directory.GetFiles(run.Log, "*-tests.log").Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        log.ShouldNotBeNull("progress-log-absent");
        var fresh = NightlyChunkVerdict.Freshness(log, run.Log, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), "mp-progress");
        fresh.Ok.ShouldBeTrue("progress-freshness " + fresh.Reason);
        var prefix = TestPhasePublication.ReadExecutedPrefix("", run.Timing, "");
        (prefix.LastSequence >= 2).ShouldBeTrue("progress-sequence " + prefix.LastSequence);
        run.Seal.ShouldNotBeNull("progress-seal-absent");
    });

    [Test]
    [Timeout(300_000)]
    public Task InstrumentationAndIdentitySurvivePublication(CancellationToken cancellationToken) => InvariantAsync(async () =>
    {
        var work = C1514Support.FreshRoot();
        // Resolve-QualificationTimingRoot (scripts/lib/test-phase-paths.ps1, still PowerShell).
        var log = Path.Combine(work, "v14-log");
        var set = Path.Combine(work, "v14-set");
        var resolved = await ResolveTimingRoots(cancellationToken, ("", "", ""), ("", log, ""), (set, log, "mp-v14"));
        resolved[0].ShouldBe("", "timing-root-removed-key-invented");
        resolved[1].ShouldBe(Path.Combine(log, "timing"), "timing-root-present-empty-mismatch");
        resolved[2].ShouldBe(set, "timing-root-set-mismatch");
        var run = await Qualify(work, "identity", "success", ["markdown-pdf", "observability"],
            Classes(("markdown-pdf", ["Fixture.One"]), ("observability", ["Fixture.Two"])), cancellationToken);
        run.FlatAtBarrier.ShouldBe("absent", "identity-flat-present-before-terminal " + run.FlatAtBarrier);
        var owners = Directory.GetFiles(run.Timing, "owner-*.json");
        (owners.Length >= 2).ShouldBeTrue("identity-owner-sidecars " + owners.Length);
        var drains = owners.Select(o =>
        {
            using var d = JsonDocument.Parse(File.ReadAllText(o));
            return d.RootElement.TryGetProperty("drainMilliseconds", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;
        }).Distinct().Count();
        (drains >= 2).ShouldBeTrue("identity-owner-stamps-collapsed");
        File.Exists(Path.Combine(run.Timing, "groups-planned-index.json")).ShouldBeTrue("identity-planned-index-absent");
        using var flat = JsonDocument.Parse(File.ReadAllText(Path.Combine(run.Timing, "groups-executed.json")));
        var same = flat.RootElement.GetProperty("chunks").EnumerateArray().Where(c => Text(c, "chunk") == "same").ToList();
        same.Count.ShouldBe(2, "identity-homonym-count " + same.Count);
        same.Select(c => Text(c, "suite")).Distinct(StringComparer.Ordinal).Count().ShouldBe(2, "identity-suites-collapsed");
    });

    // ---- v10: seeded summary labels (Assert-MpSeededSummaryLabels) ----

    private static void SeededSummaryLabels(string work, TestPhasePublicationScope scope)
    {
        foreach (var mode in new[] { "legacy", "successor" })
        {
            var root = Path.Combine(work, "summary-seed-" + mode);
            WriteText(Path.Combine(root, "timing", "groups-planned.json"), "{\"version\":1,\"runId\":\"labels\",\"attempt\":\"1\",\"suite\":\"suite-a\",\"assembly\":\"Antiphon.Tests\",\"groups\":[]}");
            var trx = Path.Combine(root, "a.trx");
            WriteDefinedTrx(trx, "Fixture.Seeded", "Passed");
            if (mode == "legacy") LegacyPublish(root, trx, "suite-a", "chunk-a", "labels", "1", 1, false, 0, true, true);
            else
            {
                var first = Open(root, "labels", "1", "suite-a", "unknown", scope);
                Publish(first, root, trx, "suite-a", "chunk-a", "labels", "1", 1, 0, false, false, scope);
                Complete(first, "orchestration-error", scope);
                Close(first);
            }
            var ctx = Open(root, "labels", "1", "suite-a", "unknown", scope);
            (!ctx.Refused && ctx.HasSeed).ShouldBeTrue("labels-seed-not-created " + mode);
            var trxB = Path.Combine(root, "b.trx");
            WriteDefinedTrx(trxB, "Fixture.Current", "Failed");
            Publish(ctx, root, trxB, "suite-b", "chunk-b", "labels", "1", 2, 0, false, false, scope);
            Complete(ctx, "", scope);
            Close(ctx);
            Summarize(root, scope: scope);
            using (var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "timing", "summary.json"))))
            {
                var tests = summary.RootElement.GetProperty("tests").EnumerateArray().ToList();
                (tests.Count(t => Text(t, "groupId") == "chunk-a" && Text(t, "suite") == "suite-a") == 1
                    && tests.Count(t => Text(t, "groupId") == "chunk-b" && Text(t, "suite") == "suite-b") == 1).ShouldBeTrue("seeded-summary-labels-lost " + mode);
            }
            CsvColumn(Path.Combine(root, "timing", "tests.csv"), "groupId").Count(g => g == "chunk-a").ShouldBe(1, "seeded-csv-labels-lost " + mode);
        }
    }

    private static string Planned(string runId, string suite) =>
        "{\"version\":1,\"runId\":\"" + runId + "\",\"attempt\":\"1\",\"suite\":\"" + suite + "\",\"assembly\":\"Antiphon.Tests\",\"groups\":[]}";

    // Import-Csv's values of one column.
    private static List<string> CsvColumn(string path, string column)
    {
        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        var header = CsvCells(lines[0]);
        var index = header.IndexOf(column);
        index.ShouldBeGreaterThanOrEqualTo(0, "csv-column-absent " + column);
        return lines.Skip(1).Select(CsvCells).Select(cells => index < cells.Count ? cells[index] : "").ToList();
    }

    private static List<string> CsvCells(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells;
    }

    // ---- v11: the in-process fault matrix (Invoke-MpMatrixArm, Assert-MpMatrix) ----

    private static List<JournalRow> MatrixArm(string work, string name, string stage, int remaining)
    {
        var root = Path.Combine(work, "matrix-" + name);
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        var ctx = Open(root, "run-m", "1", "markdown-pdf", "unknown");
        ctx.Refused.ShouldBeFalse(name + " context-refused");
        for (var i = 1; i <= 5; i++)
        {
            var trx = Path.Combine(root, "c" + i + ".trx");
            WriteDefinedTrx(trx, "Fixture.One", "Passed");
            var scope = Scope(fault: i == 2 ? Fault(stage, remaining) : null);
            Publish(ctx, root, trx, "markdown-pdf", "c" + i, "run-m", "1", 1, 0, true, true, scope);
        }
        Close(ctx);
        return JournalRows(Path.Combine(root, "timing"));
    }

    private static void Matrix(string work)
    {
        var held = MatrixArm(work, "held", "Rename", 4);
        var heldGaps = held.Where(r => r.Kind == "gap").ToList();
        heldGaps.Count.ShouldBe(1, "matrix-c-gap-dropped " + heldGaps.Count);
        (heldGaps[0].Suite == "markdown-pdf" && heldGaps[0].Chunk == "c2").ShouldBeTrue("matrix-c-gap-identity");
        foreach (var id in new[] { "c1", "c3", "c4", "c5" }) held.Count(r => r.Kind == "chunk" && r.Chunk == id).ShouldBe(1, "matrix-c-missing-" + id);
        var retry = MatrixArm(work, "retry", "Rename", 1);
        retry.Count(r => r.Kind == "gap").ShouldBe(0, "matrix-a-spurious-gap");
        retry.Count(r => r.Kind == "chunk").ShouldBe(5, "matrix-a-lost-chunk");
        var gap = MatrixArm(work, "gap", "Rename", 2);
        var gapRows = gap.Where(r => r.Kind == "gap").ToList();
        gapRows.Count.ShouldBe(1, "matrix-b-gap-count " + gapRows.Count);
        (gapRows[0].Suite == "markdown-pdf" && gapRows[0].Chunk == "c2").ShouldBeTrue("matrix-b-gap-identity " + gapRows[0].Suite + "/" + gapRows[0].Chunk);
        foreach (var id in new[] { "c1", "c3", "c4", "c5" }) gap.Count(r => r.Kind == "chunk" && r.Chunk == id).ShouldBe(1, "matrix-b-missing-" + id);
        gap.Count(r => r.Kind == "chunk" && r.Chunk == "c2").ShouldBe(0, "matrix-b-duplicate-chunk");
        var ack = MatrixArm(work, "ack", "Acknowledge", 1);
        ack.Count(r => r.Kind == "gap").ShouldBe(0, "matrix-d-spurious-gap");
        ack.Count(r => r.Chunk == "c2").ShouldBe(1, "matrix-d-duplicate-or-missing");
        // The harness re-read the gap arm's journal in a fresh pwsh: here an independent reader of the files on disk.
        var fresh = JournalRows(Path.Combine(work, "matrix-gap", "timing")).Where(r => r.Kind == "gap").ToList();
        ("gaps=" + fresh.Count + " chunk=" + (fresh.Count > 0 ? fresh[^1].Chunk : "")).ShouldBe("gaps=1 chunk=c2", "matrix-b-fresh-process");
    }

    // ---- the qualification loop (Invoke-MpQualification) ----

    private sealed record Qualified(string Name, int ExitCode, JsonElement? Summary, JsonElement? Seal, string FlatAtBarrier, string StampAtBarrier,
        string Timing, string Log, string State, int PrefixRescans, string Output);

    private static List<(string Suite, string[] Classes)> Classes(params (string Suite, string[] Classes)[] chunks) => chunks.ToList();

    private static async Task<Qualified> Qualify(string work, string name, string mode, string[] suites, List<(string Suite, string[] Classes)> chunkClasses,
        CancellationToken cancellationToken, int faultAt = 0, int faultUntil = 0, string faultStage = "Rename", int faultRemaining = 2, string publication = "")
    {
        var root = Path.Combine(work, name);
        var state = Path.Combine(root, "state");
        Directory.CreateDirectory(state);
        File.WriteAllLines(Path.Combine(state, "classes.txt"), chunkClasses.SelectMany(c => c.Classes));
        File.WriteAllText(Path.Combine(state, "fault-at.txt"), faultAt.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(state, "fault-until.txt"), faultUntil.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(state, "fault-stage.txt"), faultStage);
        File.WriteAllText(Path.Combine(state, "fault-remaining.txt"), faultRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(state, "report-count.txt"), "0");
        var policy = Path.Combine(root, "policy.json");
        ScopedPolicy(policy, suites, chunkClasses);
        var seam = Path.Combine(root, "seams.ps1");
        File.WriteAllText(seam, SeamText, new UTF8Encoding(false));
        var log = Path.Combine(root, "log");
        var timing = Path.Combine(root, "timing");
        FileStream? heldWriter = null;
        if (publication == "torn")
        {
            var seed = TestPhasePublication.Open(log, timing, "mp-" + name, "1", "", Sha40, "", false, false, Scope());
            seed.Refused.ShouldBeFalse("verdict-torn-setup-refused");
            File.WriteAllText(Path.Combine(timing, "groups-executed.journal", seed.Generation, "00000001.json"), "{\"torn\":");
            TestPhasePublication.Close(seed);
        }
        else if (publication is "held" or "none")
        {
            // held: another writer holds the journal. none (the harness's no-journal control, a refused open): the same
            // refused context, from the held lock.
            var journal = Path.Combine(timing, "groups-executed.journal");
            Directory.CreateDirectory(journal);
            heldWriter = File.Open(Path.Combine(journal, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        (int ExitCode, string Text) ran;
        try
        {
            ran = await RunEntry("nightly-tests.ps1", [
                "-RepoRoot", C1514Support.RepoRoot(), "-LogRoot", log, "-TimingRoot", timing, "-Suites", string.Join(",", suites), "-Profile", "nightly",
                "-Sha", Sha40, "-GitRef", "origin/master", "-RunId", "mp-" + name, "-Attempt", "1", "-Trigger", "manual", "-SeamsPath", seam,
                "-PolicyPath", policy, "-AllowSharedTree"], cancellationToken, new Dictionary<string, string>
                {
                    ["MP_STATE"] = state, ["MP_TIMING"] = timing, ["MP_LOG"] = log, ["MP_MODE"] = mode, ["ANTIPHON_NIGHTLY_DEFERRED_WRITES"] = "off",
                });
        }
        finally { heldWriter?.Dispose(); }
        // missing-state: the state the terminal projection wrote is gone (the harness removed it after each projection).
        if (publication == "missing-state")
        {
            var statePath = Path.Combine(timing, "groups-executed.state.json");
            if (File.Exists(statePath)) File.Delete(statePath);
        }
        JsonElement? summary = null;
        var summaryPath = Path.Combine(log, "summary.json");
        if (File.Exists(summaryPath)) summary = JsonDocument.Parse(File.ReadAllText(summaryPath)).RootElement.Clone();
        JsonElement? seal = null;
        var generation = CurrentGeneration(timing);
        var sealPath = Path.Combine(timing, "groups-executed.journal", generation, "seal.json");
        if (generation.Length > 0 && File.Exists(sealPath)) seal = JsonDocument.Parse(File.ReadAllText(sealPath)).RootElement.Clone();
        string Flag(string file) => File.Exists(Path.Combine(state, file)) ? File.ReadAllText(Path.Combine(state, file)).Trim() : "";
        var rescans = int.TryParse(Flag("rescans.txt"), out var r) ? r : 0;
        return new Qualified(name, ran.ExitCode, summary, seal, Flag("flat-at-barrier.txt"), Flag("stamp-at-barrier.txt"), timing, log, state, rescans, ran.Text);
    }

    // New-MpScopedPolicy: the repository policy with these suites required, missing ones declared, one chunk 'same' per
    // suite, and its hash restated.
    private static void ScopedPolicy(string path, string[] suites, List<(string Suite, string[] Classes)> chunkClasses)
    {
        var policy = JsonNode.Parse(File.ReadAllText(Path.Combine(C1514Support.RepoRoot(), "tests", "test-execution-policy.json")))!.AsObject();
        policy["profiles"]!["nightly"]!["requiredSuites"] = new JsonArray(suites.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        var declared = policy["suites"]!.AsObject();
        foreach (var name in suites)
        {
            if (declared.Any(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase))) continue;
            declared[name] = new JsonObject
            {
                ["processGroup"] = "native-dotnet", ["name"] = name, ["mode"] = "unattended", ["assembly"] = "Antiphon.MarkdownPdf.Tests",
                ["project"] = "tests/Antiphon.MarkdownPdf.Tests/Antiphon.MarkdownPdf.Tests.csproj",
            };
        }
        var chunks = new JsonObject();
        foreach (var (suite, classes) in chunkClasses)
            chunks[suite] = new JsonArray(new JsonObject { ["id"] = "same", ["classes"] = new JsonArray(classes.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()) });
        policy["chunks"] = chunks;
        policy["policyHash"] = NightlyPolicy.HashText(policy.ToJsonString());
        File.WriteAllText(path, policy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    // Assert-MpTerminalPublication.
    private static void AssertTerminalPublication(Qualified run, string terminal, bool requireBarrier)
    {
        run.Summary.ShouldNotBeNull(run.Name + " receipt-absent " + Tail(run.Output));
        if (requireBarrier) run.FlatAtBarrier.ShouldNotBe("present", run.Name + " flat-present-before-terminal");
        if (requireBarrier) run.FlatAtBarrier.ShouldBe("absent", run.Name + " barrier-not-reached " + run.FlatAtBarrier);
        run.Seal.ShouldNotBeNull(run.Name + " terminal-seal-absent");
        var reasons = run.Summary.Value.TryGetProperty("reasons", out var rs) && rs.ValueKind == JsonValueKind.Array ? string.Join(";", Strings(rs)) : "";
        Text(run.Seal, "terminal").ShouldBe(terminal, run.Name + " terminal=" + Text(run.Seal, "terminal") + " expected=" + terminal + " reasons=" + reasons);
        var records = RecordFiles(run.Timing).Count(f => Path.GetFileName(Path.GetDirectoryName(f)) == Text(run.Seal, "generation"));
        if (terminal != "orchestration-error") (records >= 1).ShouldBeTrue(run.Name + " no-committed-chunk");
        if (terminal != "orchestration-error") File.Exists(Path.Combine(run.Timing, "groups-executed.json")).ShouldBeTrue(run.Name + " final-projection-absent");
    }

    // Get-MpVerdictOutputs: the ledger rows, the receipt and the returned verdict (the entry's exit code) with the TRX
    // evidence bytes; timings, run and root identities and paths are observational.
    private static string VerdictOutputs(Qualified run)
    {
        run.Summary.ShouldNotBeNull("verdict-output-absent " + Tail(run.Output));
        var summary = run.Summary.Value;
        var ledger = new JsonArray();
        if (summary.TryGetProperty("suites", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                var entry = new JsonObject();
                foreach (var key in new[] { "id", "name", "suite", "chunk", "result", "exitCode", "timedOut", "skipped", "coverageComplete", "testsPassed", "evidenceReasons", "requiredUidCount", "excludedUidCount", "censusDigest", "exclusions" })
                    if (row.TryGetProperty(key, out var value)) entry[key] = JsonNode.Parse(value.GetRawText());
                ledger.Add(entry);
            }
        }
        var receipt = new JsonObject();
        foreach (var key in new[] { "outcome", "succeeded", "coverageComplete", "testsPassed", "reasons", "policyHash", "coverageScope", "platform", "requiredSuites", "selectedSuites", "nativeActiveMax" })
        {
            summary.TryGetProperty(key, out var value).ShouldBeTrue("receipt-field-absent " + key);
            receipt[key] = JsonNode.Parse(value.GetRawText());
        }
        var evidence = new JsonArray();
        foreach (var file in Directory.GetFiles(run.Log, "*.trx").Order(StringComparer.OrdinalIgnoreCase))
            evidence.Add(new JsonObject { ["name"] = Path.GetFileName(file), ["sha256"] = Sha(file) });
        var json = new JsonObject { ["ledger"] = ledger, ["receipt"] = receipt, ["returned"] = new JsonObject { ["ExitCode"] = run.ExitCode }, ["originalEvidence"] = evidence }.ToJsonString();
        var root = Path.GetDirectoryName(run.Log)!;
        return json.Replace(JsonSerializer.Serialize(root)[1..^1], "<root>").Replace(root, "<root>").Replace("mp-" + run.Name, "<run>");
    }

    // Resolve-QualificationTimingRoot, from scripts/lib/test-phase-paths.ps1 (still PowerShell).
    private static async Task<string[]> ResolveTimingRoots(CancellationToken cancellationToken, params (string TimingRoot, string LogRoot, string RunId)[] calls)
    {
        var script = new StringBuilder(". (Join-Path '" + C1514Support.RepoRoot().Replace("'", "''") + "' 'scripts/lib/test-phase-paths.ps1');");
        foreach (var (timingRoot, logRoot, runId) in calls)
            script.Append("Write-Output ('RESOLVED=' + [string](Resolve-QualificationTimingRoot -TimingRoot '" + timingRoot.Replace("'", "''") + "' -LogRoot '" + logRoot.Replace("'", "''") + "' -RunId '" + runId + "'));");
        var start = new ProcessStartInfo("pwsh");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "& { " + script + " }" }) start.ArgumentList.Add(argument);
        var ran = await C1514Support.RunProcess(start, cancellationToken);
        var values = ran.Text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("RESOLVED=", StringComparison.Ordinal)).Select(l => l["RESOLVED=".Length..]).ToArray();
        values.Length.ShouldBe(calls.Length, "resolver-output " + ran.Text);
        return values;
    }

    private static string Text(JsonElement? e, string name) =>
        e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Null ? "" : v.GetRawText() : "";

    private static bool Bool(JsonElement? e, string name) =>
        e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static List<string> Strings(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : t.GetRawText()).ToList() : [];

    private static string Tail(string text) => text.Length <= 1500 ? text : text[^1500..];

    // Write-MpSeam: the qualification's seams (a fake test host that writes TRX and diagnostics from the argv), with the
    // publication fault window and the barrier observations. It is dot-sourced inside the entry, so its helpers live in
    // the StartProcess block; $script: is the entry's script scope, where the publication reads the commit seams and the
    // bound writer context.
    private const string SeamText = """
        $NightlySeams = @{
            DockerInfo = { return @{ exitCode = 0; version = "test"; detail = "seam" } }
            DiskFree = { return @{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB } }
            StartProcess = {
                param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
                function Get-MpArgValue {
                    param([string[]]$ArgumentList, [string]$Name)
                    $items = @($ArgumentList)
                    for ($i = 0; $i -lt $items.Count; $i++) {
                        if ([string]$items[$i] -eq $Name -and ($i + 1) -lt $items.Count) { return [string]$items[$i + 1] }
                    }
                    return ""
                }
                function Get-MpFilterClass {
                    param([string]$Filter)
                    if ($Filter -match '^/\*/([^/]+)/([^/]+)/\*$') { return ($Matches[1] + "." + $Matches[2]) }
                    return ""
                }
                function New-MpDiagRecord {
                    param([string]$Uid, [string]$TypeName, [string]$Method, [string]$StateName)
                    $state = $StateName + "TestNodeStateProperty { Explanation = ok }"
                    return ("TestNodeUpdateMessage { DisplayName = TestNode update, Description = This data is used to report a TestNode state change., Properties = [], ParentTestNodeUid = <null>, TestNode = TestNode { Uid = TestNodeUid { Value = " + $Uid + " }, DisplayName = " + $Method + ", Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null, Namespace = Probe, TypeName = " + $TypeName + ", MethodName = " + $Method + ", MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, " + $state + " ] } }")
                }
                function Write-MpDiagFile {
                    param([string]$Directory, [string[]]$Records)
                    if (-not (Test-Path -LiteralPath $Directory)) { New-Item -ItemType Directory -Path $Directory -Force | Out-Null }
                    $text = "Version: '1.44.0.0'`nVersion: 2.2.2`n" + (@($Records) -join "`n") + "`n"
                    [System.IO.File]::WriteAllText((Join-Path $Directory "fixture.diag"), $text, (New-Object System.Text.UTF8Encoding $false))
                }
                function Write-MpDefinedTrx {
                    param([string]$Path, [string]$ClassName, [string]$Outcome)
                    $dir = Split-Path -Parent $Path
                    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
                    $xml = '<?xml version="1.0" encoding="utf-8"?><TestRun><TestDefinitions><UnitTest id="t1" name="Plain"><TestMethod className="' + $ClassName + '" name="Plain" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="t1" testName="Plain" outcome="' + $Outcome + '" /></Results></TestRun>'
                    [System.IO.File]::WriteAllText($Path, $xml, (New-Object System.Text.UTF8Encoding $false))
                }
                $state = [string]$env:MP_STATE
                $utf8 = New-Object System.Text.UTF8Encoding $false
                # The bound writer's prefix rescans so far (the harness counted Invoke-TestPhasePrefixRescan).
                $bound = $script:TestPhaseExecutedContext
                if ($null -ne $bound) { [System.IO.File]::WriteAllText((Join-Path $state "rescans.txt"), [string]$bound.PrefixRescans, $utf8) }
                $items = @($ArgumentList)
                $argsText = $items -join " "
                Add-Content -LiteralPath (Join-Path $state "launches.txt") -Value $argsText -Encoding ascii
                $classes = @([System.IO.File]::ReadAllLines((Join-Path $state "classes.txt")))
                if ($argsText -match "--list-tests") {
                    $diag = Get-MpArgValue -ArgumentList $items -Name "--diagnostic-output-directory"
                    $records = @()
                    foreach ($className in $classes) {
                        if ([string]::IsNullOrWhiteSpace($className)) { continue }
                        $records += (New-MpDiagRecord -Uid ("uid-" + $className) -TypeName $className -Method "Plain" -StateName "Discovered")
                    }
                    Write-MpDiagFile -Directory $diag -Records $records
                    return [pscustomobject]@{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
                }
                if ($argsText -notmatch "--report-trx") {
                    return [pscustomobject]@{ ExitCode = 0; TimedOut = $false; ChildrenExited = $true; CleanupComplete = $true; Pid = 1; LogText = "" }
                }
                $n = 0
                $countPath = Join-Path $state "report-count.txt"
                if (Test-Path -LiteralPath $countPath) { $n = [int][System.IO.File]::ReadAllText($countPath) }
                $n++
                [System.IO.File]::WriteAllText($countPath, ([string]$n), $utf8)
                $timing = [string]$env:MP_TIMING
                $flat = Join-Path $timing "groups-executed.json"
                if ($n -ge 2) {
                    $flag = "absent"
                    if (Test-Path -LiteralPath $flat) { $flag = "present" }
                    [System.IO.File]::WriteAllText((Join-Path $state "flat-at-barrier.txt"), $flag, $utf8)
                    $logRoot = [string]$env:MP_LOG
                    $stamp = ""
                    if (Test-Path -LiteralPath $logRoot) {
                        $logs = @(Get-ChildItem -LiteralPath $logRoot -Filter "*-tests.log" -File -ErrorAction SilentlyContinue)
                        foreach ($log in $logs) {
                            $raw = [System.IO.File]::ReadAllText($log.FullName)
                            if ($raw -match "C487_INVOCATION=") { $stamp = "stamped" }
                        }
                    }
                    if ([string]::IsNullOrWhiteSpace($stamp)) { $stamp = "unstamped" }
                    [System.IO.File]::WriteAllText((Join-Path $state "stamp-at-barrier.txt"), $stamp, $utf8)
                }
                $mode = [string]$env:MP_MODE
                if ($mode -eq "throw" -and $n -eq 1) { throw "mp-orchestration" }
                if ($mode -eq "no-planned") {
                    $planned = Join-Path $timing "groups-planned.json"
                    if (Test-Path -LiteralPath $planned) { Remove-Item -LiteralPath $planned -Force }
                    $index = Join-Path $timing "groups-planned-index.json"
                    if (Test-Path -LiteralPath $index) { Remove-Item -LiteralPath $index -Force }
                }
                $faultAt = 0
                $faultPath = Join-Path $state "fault-at.txt"
                if (Test-Path -LiteralPath $faultPath) { $faultAt = [int][System.IO.File]::ReadAllText($faultPath) }
                $faultUntil = 0
                $untilPath = Join-Path $state "fault-until.txt"
                if (Test-Path -LiteralPath $untilPath) { $faultUntil = [int][System.IO.File]::ReadAllText($untilPath) }
                $stage = "Rename"
                $stagePath = Join-Path $state "fault-stage.txt"
                if (Test-Path -LiteralPath $stagePath) {
                    $stageText = [System.IO.File]::ReadAllText($stagePath).Trim()
                    if (-not [string]::IsNullOrWhiteSpace($stageText)) { $stage = $stageText }
                }
                $inWindow = $false
                if ($faultAt -gt 0 -and $n -ge $faultAt) {
                    if ($faultUntil -le 0 -or $n -le $faultUntil) { $inWindow = $true }
                }
                if ($inWindow) {
                    $remaining = 2
                    $remainPath = Join-Path $state "fault-remaining.txt"
                    if (Test-Path -LiteralPath $remainPath) { $remaining = [int][System.IO.File]::ReadAllText($remainPath) }
                    $script:TestPhaseCommitFault = [pscustomobject]@{ Stage = $stage; Remaining = $remaining }
                } elseif ($faultAt -gt 0) {
                    $script:TestPhaseCommitFault = $null
                }
                $filter = Get-MpArgValue -ArgumentList $items -Name "--treenode-filter"
                $className = Get-MpFilterClass -Filter $filter
                if ([string]::IsNullOrWhiteSpace($className)) { $className = [string]$classes[0] }
                $outcome = "Passed"
                $exitCode = 0
                $timedOut = $false
                if ($mode -eq "failed") { $outcome = "Failed"; $exitCode = 1 }
                if ($mode -eq "timeout") { $timedOut = $true }
                if ($mode -ne "missing-trx") {
                    $trxName = Get-MpArgValue -ArgumentList $items -Name "--report-trx-filename"
                    $results = Get-MpArgValue -ArgumentList $items -Name "--results-directory"
                    if ($trxName -and $results) { Write-MpDefinedTrx -Path (Join-Path $results $trxName) -ClassName $className -Outcome $outcome }
                    $exec = Get-MpArgValue -ArgumentList $items -Name "--diagnostic-output-directory"
                    $stateName = "Passed"
                    if ($outcome -eq "Failed") { $stateName = "Failed" }
                    Write-MpDiagFile -Directory $exec -Records @((New-MpDiagRecord -Uid ("uid-" + $className) -TypeName $className -Method "Plain" -StateName $stateName))
                }
                $drain = 10 + $n
                return [pscustomobject]@{ ExitCode = $exitCode; TimedOut = $timedOut; ChildrenExited = $true; CleanupComplete = $true; Pid = (40 + $n); LogText = ""; RootStartedAtUtc = "2026-10-30T03:02:00Z"; RootExitedAtUtc = "2026-10-30T03:03:00Z"; JobEmptyObserved = $true; DrainMilliseconds = $drain }
            }
        }
        """;
}
