using System.Collections.Specialized;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcFailedFirstPlannerTests
{
    // CARD-1404: the real planner's 19,012-node replay plus three companion suites through the compiled schedule
    // (W2-nightly S13: in-process, was the GlobalReplayPriority case of scripts/test-nightly-failed-first.ps1; same rows).
    [Test]
    public void GlobalReplayPriority()
    {
        var rows = new NightlyCaseRows();
        var fx = FailedFirstDispatchCases.Global();
        var entries = PsValue.AsArray(PsValue.Prop(fx.Schedule, "entries"));
        static bool Prior(object e) => (bool)PsValue.Prop(e, "priorFailure")!;
        static int Ordinal(object e, string name) => (int)PsValue.Prop(e, name)!;
        static string S(object e, string name) => PsValue.Str(PsValue.Prop(e, name));
        var priority = entries.Where(Prior).ToArray();
        var ordinary = entries.Where(e => !Prior(e)).ToArray();
        var replayNodes = (object[])fx.Replay["nodes"]!;
        var replayChunks = (object[])fx.Replay["chunks"]!;
        // 151 chunks since 2026-11-25: the Codex CLI tests moved to Antiphon.Tests.Application.Codex, their own launch.
        rows.Check(replayNodes.Length == 19012 && replayChunks.Length == 151, "FF GlobalReplayPriority.replay", "nodes=" + replayNodes.Length + " chunks=" + replayChunks.Length);
        rows.Check(priority.Length > 4 && ordinary.Length > 0 && priority.Max(e => Ordinal(e, "dispatchOrdinal")) < ordinary.Min(e => Ordinal(e, "dispatchOrdinal")),
            "FF GlobalReplayPriority.globalPriority", "priority=" + priority.Length + " ordinary=" + ordinary.Length);
        static bool Sorted(object[] band) => band.Select(e => Ordinal(e, "canonicalOrdinal")).SequenceEqual(band.Select(e => Ordinal(e, "canonicalOrdinal")).Order());
        rows.Check(Sorted(priority) && Sorted(ordinary), "FF GlobalReplayPriority.stable");
        var keys = FailedFirstDispatchCases.Keys(fx.Schedule);
        var canonical = fx.Suites.SelectMany(s => PsValue.AsArray(PsValue.Prop(s, "chunks")).Select(c => S(s, "id") + "/" + S(c, "id"))).ToArray();
        rows.Check(keys.Length == canonical.Length && keys.Distinct(StringComparer.Ordinal).Count() == canonical.Length && NightlyPlanCases.SameSet(keys, canonical),
            "FF GlobalReplayPriority.permutation", "keys=" + keys.Length + " canonical=" + canonical.Length);
        var same = entries.All(entry =>
        {
            var suite = fx.Suites.First(s => S(s, "id") == S(entry, "suite"));
            var chunk = PsValue.AsArray(PsValue.Prop(suite, "chunks")).First(c => S(c, "id") == S(entry, "chunk"));
            return ReferenceEquals(PsValue.Prop(entry, "ChunkRef"), chunk);
        });
        rows.Check(same, "FF GlobalReplayPriority.references");
        rows.Check(priority.Count(e => S(e, "suite") != "antiphon" && Ordinal(e, "dispatchOrdinal") < Ordinal(ordinary[0], "dispatchOrdinal")) == 3, "FF GlobalReplayPriority.crossSuite");
        var splits = PsValue.AsArray(PsValue.Prop(fx.Resolved, "members")).All(m =>
            PsValue.AsArray(PsValue.Prop(m, "chunks")).All(id => priority.Count(e => S(e, "suite") == S(m, "suite") && S(e, "chunk") == PsValue.Str(id)) == 1));
        rows.Check(splits, "FF GlobalReplayPriority.allSplits");
        var plain = NightlyDispatch.Schedule(fx.Suites);
        var plainEntries = PsValue.AsArray(PsValue.Prop(plain, "entries"));
        rows.Check(S(plain, "canonicalPlanDigest") == S(fx.Schedule, "canonicalPlanDigest")
            && plainEntries.Select(e => Ordinal(e, "canonicalOrdinal")).SequenceEqual(plainEntries.Select(e => Ordinal(e, "dispatchOrdinal")))
            && FailedFirstDispatchCases.CanonicalDigestBytes(fx.Suites), "FF GlobalReplayPriority.canonicalDigest");
        var unknown = NightlyDispatch.Schedule(fx.Suites, null, "", [new OrderedDictionary { { "suite", "antiphon" }, { "className", "Retired.Class" }, { "reason", "current-class-missing" } }],
            true, "", "", null, "", DateTime.UtcNow, "");
        rows.Check(PsValue.AsArray(PsValue.Prop(unknown, "unresolved")).Length == 1 && PsValue.AsArray(PsValue.Prop(unknown, "entries")).Length == plainEntries.Length,
            "FF GlobalReplayPriority.unresolved");
        var duplicate = NightlyPlanCases.Refusal(() => NightlyDispatch.Schedule([fx.Suites[0], fx.Suites[0]]));
        rows.Check(duplicate != null && duplicate.Contains("duplicate-canonical-chunk", StringComparison.Ordinal), "FF GlobalReplayPriority.duplicate", duplicate ?? "accepted");
        rows.ShouldAllPass(10);
    }

    // CARD-1514 D-17: lazy preparation without history, the preparation barrier with it, the E2E prerequisite suite and
    // a schedule write fault, through the compiled coordinator and suite runner (W2-nightly S13: in-process, was
    // Test-FFPreparationSequencing over Invoke-FFPreparationArm in FullBytesAndCoverageConserved; same rows).
    [Test]
    public void PreparationSequencing()
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("s13-preparation");
        bool lazy = true, barrier = true, e2e = true, io = true;
        var detail = new List<string>();
        static string Trace(FailedFirstDispatchCases.PreparationArm arm) => string.Join(",", arm.Trace);
        static string S(object row, string name) => PsValue.Str(PsValue.Prop(row, name));
        foreach (var failure in new[] { "discovery", "timeout", "e2e" })
        {
            var absent = FailedFirstDispatchCases.Preparation(fixture, "lazy-" + failure, "absent", failure);
            var empty = FailedFirstDispatchCases.Preparation(fixture, "empty-" + failure, "empty", failure);
            var priority = FailedFirstDispatchCases.Preparation(fixture, "barrier-" + failure, "nonempty", failure);
            detail.Add(failure + ": absent=[" + Trace(absent) + "] " + absent.Error + " empty=[" + Trace(empty) + "] barrier=[" + Trace(priority) + "] " + priority.Error);
            if (Trace(absent) != Trace(empty) || absent.Coverage || empty.Coverage) lazy = false;
            if (failure != "e2e" && Trace(absent) != "prepare-antiphon,launch-antiphon,settle-antiphon,prepare-session-runner,fail-session-runner") lazy = false;
            if (priority.Trace.Any(t => t.StartsWith("launch-", StringComparison.Ordinal)) || priority.Coverage || priority.Schedule is null
                || priority.Schedule.Value.GetProperty("preparationComplete").GetBoolean()) barrier = false;
            if (failure == "e2e" && (absent.Rows.Count(r => S(r, "id") == "e2e" && S(r, "detail") == "e2e-prerequisite-failed") != 1
                || !absent.Trace.Contains("launch-agents-pty") || priority.Rows.Count(r => S(r, "id") == "e2e") != 1)) e2e = false;
            var fault = FailedFirstDispatchCases.Preparation(fixture, "write-fault-" + failure, "absent", failure, writeFault: true);
            if (Trace(fault) != Trace(absent) || fault.Coverage != absent.Coverage || fault.ErrorKind != absent.ErrorKind) io = false;
        }
        var text = string.Join(" | ", detail);
        rows.Check(lazy, "FF PreparationSequencing.lazyPreparation", text);
        rows.Check(barrier, "FF PreparationSequencing.preparationBarrier", text);
        rows.Check(e2e, "FF PreparationSequencing.e2ePreparation", text);
        rows.Check(io, "FF PreparationSequencing.diagnosticWriteFailure", text);
        rows.ShouldAllPass(4);
    }

    // CARD-1404/1514: the full 312-launch conservation replay, the small fixture's terminal matrix, journal, timelines,
    // phase summaries and bindings, the authority conservation, the planned golden and the artifact schemas (W2-nightly
    // S14: in-process, was the driver case; same rows). The preparation rows run in PreparationSequencing (S13).
    [Test, Timeout(1_140_000)]
    [NotInParallel(new[] { "nightly-history-statics", nameof(QualificationTimingTests), "release-authority-card-reader" })]
    public void FullBytesAndCoverageConserved()
    {
        var rows = new NightlyCaseRows();
        using var fixture = NightlyFixture.Create("ff-conserve");
        FailedFirstConservation.Run(fixture, rows, nameof(FullBytesAndCoverageConserved), diagnosticsOnly: false);
        rows.ShouldAllPass(19);
    }
}
