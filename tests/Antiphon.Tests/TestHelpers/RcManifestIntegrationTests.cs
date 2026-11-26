using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.TestHelpers.JournalFixtures;

namespace Antiphon.Tests.TestHelpers;

// CARD-1514 M2. Real qualification publication and recovery (W2-nightly S08; these were the v9-v14 cases of
// ManifestPublicationHarness.ps1). The publication, the executed manifest and the summary are the production C#
// (TestPhasePublication, TestPhaseManifests, TestPhaseRunSummary: the summarize-test-phases command in-process, S09), in-process
// where a case drives them directly. The qualification loop is NightlyTests.Run since W2-nightly S17 (scripts/nightly-tests.ps1 is
// deleted): the cases run it in this process on the seams the harness passed (SeamedNightlyRun; unit 62 of the 2026-11-22
// remaining-ps1 plan deleted the pwsh child and scripts/lib/c487-nightly-tests.ps1 it ran), its commit fault and bound writer
// read through the journal seams. The harness ran the loop without deferred evidence writes; so do these.
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
        // Resolve-QualificationTimingRoot, compiled (NightlyPaths.TimingRoot; W2-nightly S27 deleted scripts/lib/test-phase-paths.ps1).
        var log = Path.Combine(work, "v14-log");
        var set = Path.Combine(work, "v14-set");
        NightlyPaths.TimingRoot("", "", "").ShouldBe("", "timing-root-removed-key-invented");
        NightlyPaths.TimingRoot("", log, "").ShouldBe(Path.Combine(log, "timing"), "timing-root-present-empty-mismatch");
        NightlyPaths.TimingRoot(set, log, "mp-v14").ShouldBe(set, "timing-root-set-mismatch");
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

    private static Task<Qualified> Qualify(string work, string name, string mode, string[] suites, List<(string Suite, string[] Classes)> chunkClasses,
        CancellationToken cancellationToken, int faultAt = 0, int faultUntil = 0, string faultStage = "Rename", int faultRemaining = 2, string publication = "")
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            var seam = new MpSeam(state, timing, log, mode);
            var run = new SeamedNightlyRun
            {
                StartProcess = seam.StartProcess, DockerInfo = SeamedNightlyRun.DockerSeam, DiskFree = SeamedNightlyRun.DiskSeam,
                PublicationScope = seam.PublicationScope, ExecutedContext = seam.Bind,
            };
            var exit = run.Run(new NightlyTestsOptions
            {
                RepoRoot = C1514Support.RepoRoot(), LogRoot = log, TimingRoot = timing, Suites = suites, Profile = "nightly", Sha = Sha40,
                GitRef = "origin/master", RunId = "mp-" + name, Attempt = 1, Trigger = "manual", PolicyPath = policy, AllowSharedTree = true,
            }).ExitCode;
            ran = (exit, run.Output.ToString());
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
        return Task.FromResult(new Qualified(name, ran.ExitCode, summary, seal, Flag("flat-at-barrier.txt"), Flag("stamp-at-barrier.txt"), timing, log, state, rescans, ran.Text));
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

    private static string Text(JsonElement? e, string name) =>
        e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Null ? "" : v.GetRawText() : "";

    private static bool Bool(JsonElement? e, string name) =>
        e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static List<string> Strings(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : t.GetRawText()).ToList() : [];

    private static string Tail(string text) => text.Length <= 1500 ? text : text[^1500..];

    // Write-MpSeam: the qualification's seams (a fake test host that writes TRX and diagnostics from the argv), with the
    // publication fault window and the barrier observations. The run reads the commit fault through the PublicationScope seam
    // and hands the bound writer context to the ExecutedContext seam (W2-nightly S17: the journal seams of the seamed run).
    private sealed class MpSeam(string state, string timing, string log, string mode)
    {
        private static readonly UTF8Encoding Utf8 = new(false);
        private object? _fault;
        private TestPhaseJournalContext? _bound;

        public TestPhasePublicationScope PublicationScope(string timingRoot) =>
            new(timingRoot, TestPhaseRunSummary.CommandEdition, "1", null, _fault);

        public void Bind(TestPhaseJournalContext context) => _bound = context;

        public object StartProcess(NightlyLaunch launch)
        {
            // The bound writer's prefix rescans so far (the harness counted Invoke-TestPhasePrefixRescan).
            if (_bound is not null) File.WriteAllText(Path.Combine(state, "rescans.txt"), _bound.PrefixRescans.ToString(System.Globalization.CultureInfo.InvariantCulture), Utf8);
            var items = launch.Arguments ?? [];
            var argsText = string.Join(" ", items);
            File.AppendAllText(Path.Combine(state, "launches.txt"), argsText + Environment.NewLine, Encoding.ASCII);
            var classes = File.ReadAllLines(Path.Combine(state, "classes.txt"));
            if (argsText.Contains("--list-tests", StringComparison.OrdinalIgnoreCase))
            {
                var records = classes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => DiagRecord("uid-" + c, c, "Plain", "Discovered")).ToArray();
                WriteDiag(SeamedNightlyRun.Argument(items, "--diagnostic-output-directory"), records);
                return SeamedNightlyRun.Answer();
            }
            if (!argsText.Contains("--report-trx", StringComparison.OrdinalIgnoreCase)) return SeamedNightlyRun.Answer();
            var countPath = Path.Combine(state, "report-count.txt");
            var n = (File.Exists(countPath) ? int.Parse(File.ReadAllText(countPath), System.Globalization.CultureInfo.InvariantCulture) : 0) + 1;
            File.WriteAllText(countPath, n.ToString(System.Globalization.CultureInfo.InvariantCulture), Utf8);
            if (n >= 2)
            {
                File.WriteAllText(Path.Combine(state, "flat-at-barrier.txt"), File.Exists(Path.Combine(timing, "groups-executed.json")) ? "present" : "absent", Utf8);
                var stamped = Directory.Exists(log) && Directory.EnumerateFiles(log, "*-tests.log").Any(f => File.ReadAllText(f).Contains("C487_INVOCATION=", StringComparison.OrdinalIgnoreCase));
                File.WriteAllText(Path.Combine(state, "stamp-at-barrier.txt"), stamped ? "stamped" : "unstamped", Utf8);
            }
            if (mode == "throw" && n == 1) throw new InvalidOperationException("mp-orchestration");
            if (mode == "no-planned")
            {
                foreach (var planned in new[] { "groups-planned.json", "groups-planned-index.json" })
                    if (File.Exists(Path.Combine(timing, planned))) File.Delete(Path.Combine(timing, planned));
            }
            int Number(string file, int fallback) =>
                File.Exists(Path.Combine(state, file)) ? int.Parse(File.ReadAllText(Path.Combine(state, file)).Trim(), System.Globalization.CultureInfo.InvariantCulture) : fallback;
            var faultAt = Number("fault-at.txt", 0);
            var faultUntil = Number("fault-until.txt", 0);
            var stagePath = Path.Combine(state, "fault-stage.txt");
            var stage = File.Exists(stagePath) && !string.IsNullOrWhiteSpace(File.ReadAllText(stagePath)) ? File.ReadAllText(stagePath).Trim() : "Rename";
            if (faultAt > 0 && n >= faultAt && (faultUntil <= 0 || n <= faultUntil)) _fault = Fault(stage, Number("fault-remaining.txt", 2));
            else if (faultAt > 0) _fault = null;
            var filter = Regex.Match(SeamedNightlyRun.Argument(items, "--treenode-filter"), "^/\\*/([^/]+)/([^/]+)/\\*$");
            var className = filter.Success ? filter.Groups[1].Value + "." + filter.Groups[2].Value : classes[0];
            var outcome = mode == "failed" ? "Failed" : "Passed";
            if (mode != "missing-trx")
            {
                var trxName = SeamedNightlyRun.Argument(items, "--report-trx-filename");
                var results = SeamedNightlyRun.Argument(items, "--results-directory");
                if (trxName.Length > 0 && results.Length > 0) WriteDefinedTrx(Path.Combine(results, trxName), className, outcome);
                WriteDiag(SeamedNightlyRun.Argument(items, "--diagnostic-output-directory"), [DiagRecord("uid-" + className, className, "Plain", outcome)]);
            }
            return new PlainObject(("ExitCode", mode == "failed" ? 1 : 0), ("TimedOut", mode == "timeout"), ("ChildrenExited", true), ("CleanupComplete", true),
                ("Pid", 40 + n), ("LogText", ""), ("RootStartedAtUtc", "2026-10-30T03:02:00Z"), ("RootExitedAtUtc", "2026-10-30T03:03:00Z"),
                ("JobEmptyObserved", true), ("DrainMilliseconds", 10 + n));
        }

        // New-MpDiagRecord: one TestNodeUpdateMessage line of the TUnit diagnostic log.
        private static string DiagRecord(string uid, string typeName, string method, string stateName) =>
            "TestNodeUpdateMessage { DisplayName = TestNode update, Description = This data is used to report a TestNode state change., Properties = [], ParentTestNodeUid = <null>, TestNode = TestNode { Uid = TestNodeUid { Value = "
            + uid + " }, DisplayName = " + method + ", Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null, Namespace = Probe, TypeName = "
            + typeName + ", MethodName = " + method + ", MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, " + stateName + "TestNodeStateProperty { Explanation = ok } ] } }";

        // Write-MpDiagFile: the diagnostic directory's fixture.diag.
        private static void WriteDiag(string directory, string[] records)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "fixture.diag"), "Version: '1.44.0.0'\nVersion: 2.2.2\n" + string.Join("\n", records) + "\n", Utf8);
        }
    }
}
