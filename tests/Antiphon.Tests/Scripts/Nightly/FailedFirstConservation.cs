using System.Text.Json;
using Antiphon.Nightly;
using static Antiphon.Tests.Scripts.Nightly.FailedFirstArmCases;
using H = Antiphon.Tests.Scripts.Nightly.FailedFirstHistoryCases;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S14: Test-FullBytesAndCoverageConserved of scripts/test-nightly-failed-first.ps1 (CARD-1404/1514), in-process.
/// The global replay (19,012 nodes plus three companion suites) runs canonically and failed-first and must conserve the
/// plans, coverage and keyed observations; the small fixture's terminal matrix, journal, timelines, phase summaries,
/// phase bindings, the authority conservation, the planned golden and the artifact schemas follow. diagnosticsOnly
/// (ConservationDiagnostics) runs the small fixture's rows only.
/// </summary>
internal static class FailedFirstConservation
{
    public static void Run(NightlyFixture fixture, NightlyCaseRows rows, string caseName, bool diagnosticsOnly)
    {
        string Row(string name) => "FF " + caseName + "." + name;
        Arm a = null!, b = null!;
        string[] canonical = [], actual = [];
        if (!diagnosticsOnly)
        {
            var fx = Global();
            var before = H.JsonText(fx.Suites.SelectMany(s => H.Items(s, "chunks")).ToArray());
            a = Prepared(fixture, fx, "canonical");
            b = Prepared(fixture, fx, "priority", fx.Members);
            canonical = fx.Suites.SelectMany(s => H.Items(s, "chunks").Select(c => H.Str(s, "id") + "/" + H.Str(c, "id"))).ToArray();
            actual = b.Keys;
            var prefix = H.Items(b.Schedule, "entries").Where(e => H.True(e, "priorFailure")).ToArray();
            var companion = "session-runner/" + PsValue.Str(H.Items(fx.Members.First(m => H.Str(m, "suite") == "session-runner"), "chunks")[0]);
            // This must precede all conservation comparisons and uses actual launches.
            rows.Check(a.Keys.SequenceEqual(canonical) && !actual.SequenceEqual(canonical) && actual.Length == canonical.Length && actual.Distinct(StringComparer.Ordinal).Count() == canonical.Length
                && NightlyPlanCases.SameSet(actual, canonical) && Array.IndexOf(actual, companion) < Array.IndexOf(actual, canonical[0]), Row("observedPermutation"));
            var anchorKeys = fx.Anchors.Select(x => "antiphon/" + H.Str(x, "chunk")).ToArray();
            var observedAnchors = actual.Where(k => anchorKeys.Contains(k, StringComparer.Ordinal)).ToArray();
            rows.Check(observedAnchors.SequenceEqual(anchorKeys) && prefix.Count(e => anchorKeys.Contains(H.Str(e, "suite") + "/" + H.Str(e, "chunk"), StringComparer.Ordinal)) == 4, Row("anchors"));
            var samePlans = fx.Suites.All(s => a.Plans[H.Str(s, "id")] == b.Plans[H.Str(s, "id")]);
            rows.Check(samePlans && a.PlansStable && b.PlansStable && before == H.JsonText(fx.Suites.SelectMany(s => H.Items(s, "chunks")).ToArray())
                && H.Str(a.Schedule, "canonicalPlanDigest") == H.Str(b.Schedule, "canonicalPlanDigest"), Row("canonicalBytes"));
            rows.Check(a.Coverage && a.Passed && b.Coverage && b.Passed && a.Outcome == "green" && b.Outcome == "green" && !a.OverallFailed && !b.OverallFailed && a.NativeMax == 1
                && b.NativeMax == 1 && a.States.All(s => s.Completed) && b.States.All(s => s.Completed), Row("coverage"));
            var keyed = true;
            foreach (var r in A(a.Projection, "chunks"))
            {
                var other = A(b.Projection, "chunks").FirstOrDefault(o => S(o, "suite") == S(r, "suite") && S(o, "chunk") == S(r, "chunk"));
                foreach (var f in new[] { "exitCode", "trxMissing", "passedCount", "outcomes", "trxDigest", "manufacturedPassed" })
                    if (Raw(r, f) != Raw(other, f)) keyed = false;
            }
            rows.Check(keyed && !B(a.Projection, "manufacturedPassed") && !B(b.Projection, "manufacturedPassed"), Row("keyedObservations"));
        }
        var small = Small();
        if (diagnosticsOnly)
        {
            a = Prepared(fixture, small, "small-canonical");
            b = Prepared(fixture, small, "small-priority", small.Members);
            canonical = a.Keys;
            actual = b.Keys;
        }
        var matrix = new List<(string Variant, Arm Left, Arm Right, bool Same)>();
        foreach (var variant in new[] { "failure", "missing", "skip", "unknown", "timeout", "publication", "teardown" })
        {
            var left = Prepared(fixture, small, variant + "-canonical", [], variant);
            var right = Prepared(fixture, small, variant + "-priority", small.Members, variant);
            static string Sorted(string[] reasons) => string.Join(",", reasons.OrderBy(x => x, NightlyPlanCases.SortObject));
            var same = left.Outcome == right.Outcome && left.OverallFailed == right.OverallFailed && left.Coverage == right.Coverage && left.Passed == right.Passed
                && left.Error == right.Error && Sorted(left.Reasons) == Sorted(right.Reasons);
            // S3 reduces each chunk's own verdict, so a later pass cannot invent a TESTS event from the earlier timeout's
            // sticky run flags.
            var expectedOutcome = variant switch { "timeout" => "TIMEOUT", "publication" or "teardown" => "green", _ => "TESTS" };
            if (left.Outcome != expectedOutcome || right.Outcome != expectedOutcome) same = false;
            if (variant is "missing" or "skip" or "unknown" or "timeout" && (left.Coverage || right.Coverage || !left.OverallFailed || !right.OverallFailed)) same = false;
            if (variant == "failure" && (left.Passed || right.Passed || !left.Coverage || !right.Coverage)) same = false;
            if (variant == "publication" && (!left.Passed || !right.Passed || !left.Coverage || !right.Coverage)) same = false;
            // Teardown aborts the run at its keyed chunk; no subsequent required work is credited.
            if (variant == "teardown")
                same = !left.Coverage && !right.Coverage && left.Error.Contains("owned-child", StringComparison.OrdinalIgnoreCase) && right.Error.Contains("owned-child", StringComparison.OrdinalIgnoreCase)
                    && left.OverallFailed && right.OverallFailed && left.Outcome == "green" && right.Outcome == "green";
            matrix.Add((variant, left, right, same));
        }
        rows.Check(matrix.All(m => m.Same), Row("terminalMatrix"), string.Join(",", matrix.Where(m => !m.Same).Select(m => m.Variant + ":" + m.Left.Outcome + "/" + m.Right.Outcome + " " + m.Left.Error)));
        var failed = matrix.First(m => m.Variant == "failure");
        var prefixOk = true;
        foreach (var arm in new[] { failed.Left, failed.Right })
        {
            var passed = true;
            var coverage = true;
            foreach (var r in A(arm.Projection, "chunks"))
            {
                if (B(r, "testsPassed") != passed || B(r, "coverageComplete") != coverage) prefixOk = false;
                if (N(r, "exitCode") != 0) passed = false;
            }
        }
        rows.Check(prefixOk, Row("prefixState"));
        var companionMembers = small.Members.Where(m => H.Str(m, "suite") == "session-runner").ToArray();
        var header = Prepared(fixture, small, "companion-first", companionMembers);
        rows.Check(S(a.Projection, "suite") == a.Launches[0].Suite && S(b.Projection, "suite") == b.Launches[0].Suite && S(header.Projection, "suite") == "session-runner"
            && S(header.Projection, "runId") == "conservation-run" && S(header.Projection, "attempt") == "1", Row("stickyHeader"));
        static string Settled(Arm arm) => string.Join(",", arm.Rows.Select(r => H.Str(r, "id") + "/" + H.Str(r, "chunk")));
        rows.Check(Settled(a) == string.Join(",", canonical) && Settled(b) == string.Join(",", actual), Row("settlementOrder"));
        rows.Check(string.Join(",", failed.Left.Reasons) == string.Join(",", failed.Right.Reasons) && !failed.Left.Passed && !failed.Right.Passed, Row("reasonSequence"));
        var journalOk = true;
        foreach (var arm in new[] { a, b, header })
        {
            var journal = Path.Combine(arm.TimingRoot, "groups-executed.journal");
            var current = ReadJson(Path.Combine(journal, "current.json"));
            var generation = Path.Combine(journal, S(current, "generation"));
            var seal = ReadJson(Path.Combine(generation, "seal.json"));
            if (S(seal, "projectionDigest") != PriorFailureHistory.Digest(Path.Combine(arm.TimingRoot, "groups-executed.json")) || N(seal, "sequence") != arm.Launches.Count || !arm.Context.Closed)
                journalOk = false;
            var records = Records(generation);
            for (var i = 0; i < records.Length; i++)
            {
                var record = ReadJson(records[i]);
                if (N(record, "sequence") != i + 1 || i >= arm.Launches.Count || TestPhaseText.FromB64(S(record, "freshSuiteKey")) != arm.Launches[i].Suite
                    || TestPhaseText.FromB64(S(record, "freshChunkKey")) != arm.Launches[i].Chunk) journalOk = false;
            }
        }
        rows.Check(journalOk, Row("journalBindings"));
        var timelinesOk = true;
        foreach (var arm in new[] { a, b })
        {
            foreach (var timeline in arm.Timing.Timeline)
            {
                var chunks = arm.Timing.Chunks.Where(c => c.Suite == timeline.Suite).ToArray();
                if (chunks.Length == 0 || timeline.StartedAt != chunks.Select(c => c.StartedAt).OrderBy(x => x, NightlyPlanCases.SortObject).First()
                    || timeline.CompletedAt != chunks.Select(c => c.CompletedAt).OrderBy(x => x, NightlyPlanCases.SortObject).Last()
                    || timeline.LastPhaseUtc != chunks.Select(c => c.LastPhaseUtc).OrderBy(x => x, NightlyPlanCases.SortObject).Last()
                    || timeline.LastPhase != chunks.OrderBy(c => c.LastPhaseUtc, NightlyPlanCases.SortObject).Last().LastPhase) timelinesOk = false;
            }
            foreach (var t in arm.Timing.Chunks)
                if (t.BuildSlot != "not-used" || t.BuildSlotWaitSeconds != null || t.DistillationSeconds != null) timelinesOk = false;
        }
        rows.Check(timelinesOk, Row("timelines"));
        var summaries = true;
        foreach (var arm in new[] { failed.Left, failed.Right, header })
        {
            foreach (var file in new[] { "summary.json", "tests.csv", "summary.md" }) if (!File.Exists(Path.Combine(arm.TimingRoot, file))) summaries = false;
            if (File.Exists(Path.Combine(arm.TimingRoot, "summary.json")) && N(ReadJson(Path.Combine(arm.TimingRoot, "summary.json")), "version") != 1) summaries = false;
            var singleton = ReadJson(Path.Combine(arm.TimingRoot, "groups-planned.json"));
            var index = ReadJson(Path.Combine(arm.TimingRoot, "groups-planned-index.json"));
            if (S(singleton, "suite") != H.Str(small.Suites[^1], "id") || A(index, "suites").Length != small.Suites.Length) summaries = false;
        }
        rows.Check(summaries, Row("phaseSummariesAndIndex"));
        rows.Check(H.True(a.Schedule, "preparationComplete") && H.True(b.Schedule, "preparationComplete") && H.Str(b.Schedule, "historyDigest") == new string('d', 64)
            && H.Items(b.Schedule, "unresolved").Length == 0, Row("schedulePolicy"));
        PhaseBindings(fixture, small, rows, caseName);
        AuthorityConservation(fixture, rows, caseName);
        PlannedGoldenBytes(fixture, rows, caseName);
        ArtifactSchemas([failed.Left, failed.Right, header], rows, caseName);
    }
}
