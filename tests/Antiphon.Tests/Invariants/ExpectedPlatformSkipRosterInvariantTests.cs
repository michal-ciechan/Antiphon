using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.ExpectedPlatformSkipRosterTests;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("RepositoryInvariant")]
public sealed class ExpectedPlatformSkipRosterInvariantTests
{
    [Test]
    public void Committed_skip_roster_matches_its_methods_and_reasons()
    {
        var rows = new NightlyCaseRows();
        var skip = Row(Cleanup, "NotExecuted", Trace);
        var loaded = Verdict([skip]);
        var loadedReasons = Joined(loaded["reasons"]);
        rows.Check(Flag(loaded, "coverageComplete") && Flag(loaded, "testsPassed") && !loadedReasons.Contains("unexecuted-outcome"),
            "EPS autoload-roster-accepted", loadedReasons);

        var committed = NightlyChunkVerdict.SkipRoster(RosterPath);
        var c680Rows = committed.Where(e => PsValue.Str(PsValue.Prop(e, "test")).Contains("ClaudeDialogWatchdogCanaryTests", StringComparison.Ordinal)).ToArray();
        var liveRows = committed.Where(e => PsValue.Str(PsValue.Prop(e, "reasonClass")) == "live-context").ToArray();
        rows.Check(c680Rows.Length == 0 && liveRows.Length > 0, "EPS live-context-labeled", "c680=" + c680Rows.Length + " live-context=" + liveRows.Length);
        Census(rows);
        var impl = File.ReadAllText(Path.Combine(Repo, "scripts", "lib", "Antiphon.Nightly", "NightlySuiteRunner.cs"));
        rows.Check(impl.Contains("SuiteCompletionParts.KnownIdentities(", StringComparison.Ordinal), "EPS discovery-known-identities-wiring");
        rows.ShouldAllPass(9);
    }

    // The committed roster against tests/: its Windows rows, their source and reason text, and WindowsUnitSkipRoster.cs.
    private static void Census(NightlyCaseRows rows)
    {
        var committed = NightlyChunkVerdict.SkipRoster(RosterPath);
        var windows = committed.Where(e => PsValue.Str(PsValue.Prop(e, "platform")) == "windows").ToArray();
        var unique = windows.Select(e => PsValue.Str(PsValue.Prop(e, "test"))).Distinct(StringComparer.Ordinal).Count();
        var gaps = NightlyExpectedSkips.SourceGaps(Repo, "windows", committed, null);
        rows.Check(windows.Length == 33 && unique == windows.Length && gaps.Length == 0, "EPS census-matches-source",
            "windows=" + windows.Length + " unique=" + unique + " gaps=" + string.Join(",", gaps));

        var planted = committed.Append(Entry("windows", Missing, "gone")).ToArray();
        var plantedGaps = NightlyExpectedSkips.SourceGaps(Repo, "windows", planted, null);
        rows.Check(plantedGaps.Length == 1 && plantedGaps[0] == Missing, "EPS census-reports-missing", string.Join(",", plantedGaps));

        var reasonGaps = NightlyExpectedSkips.ReasonGaps(Repo, "windows", committed, null);
        rows.Check(reasonGaps.Length == 0, "EPS reason-text-matches-source", string.Join(",", reasonGaps));

        const string wrongId = "Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105_Resume_extraction_is_closed";
        var wrongReason = committed.Select(e => PsValue.Str(PsValue.Prop(e, "test")) == wrongId
            ? Entry("windows", wrongId, "not-the-method-skip-text") : e).ToArray();
        var wrongGaps = NightlyExpectedSkips.ReasonGaps(Repo, "windows", wrongReason, null);
        rows.Check(wrongGaps.Contains(wrongId), "EPS reason-text-reports-mismatch", string.Join(",", wrongGaps));

        var drift = NightlyExpectedSkips.RosterDrift(Repo, committed, null);
        var overlap = PsValue.AsArray(PsValue.Prop(drift, "Overlap"));
        var mismatches = PsValue.AsArray(PsValue.Prop(drift, "Mismatches")).Select(PsValue.Str).ToArray();
        rows.Check(overlap.Length == 10 && mismatches.Length == 0, "EPS roster-drift-stable",
            "overlap=" + overlap.Length + " mismatches=" + string.Join(",", mismatches));

        var drifted = committed.Select(e => PsValue.Str(PsValue.Prop(e, "test")) == wrongId
            ? Entry(PsValue.Str(PsValue.Prop(e, "platform")), wrongId, "drifted-reason", "platform") : e).ToArray();
        var driftBad = PsValue.AsArray(PsValue.Prop(NightlyExpectedSkips.RosterDrift(Repo, drifted, null), "Mismatches")).Select(PsValue.Str).ToArray();
        rows.Check(driftBad.Contains(wrongId), "EPS roster-drift-reports-mismatch", string.Join(",", driftBad));
    }
}
