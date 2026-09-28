using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class FakeGitMeasurementTests
{
    private static List<FakeGitPilotRow> Golden() =>
        new[] { "CP-3", "CP-4", "CP-5", "CP-6", "CP-7", "CP-8" }
            .Select((cp, i) => new FakeGitPilotRow
            {
                Checkpoint = cp,
                RunId = "run-1", SourceSha = new string('a', 40), BuildSourceSha = new string('a', 40),
                HostId = "host-1", Os = "Linux", SdkVersion = "10.0", GitVersion = "2.0",
                Backend = i is 0 or 3 or 4 ? "real" : "fake",
                Storage = i is 0 or 3 or 4 ? "physical" : "mock",
                OuterNoBuildSeconds = i is 0 or 3 or 4 ? 10 : 6,
                TrxElapsedSeconds = i is 0 or 3 or 4 ? 9 : 5,
                BuildSeconds = 0, SlotWaitSeconds = 0,
                Cases = FakeGitMeasurement.PilotMethods.Select(method => new FakeGitPilotCase
                {
                    Method = method, Observation = "graph:" + method,
                    ObservedGitLaunches = i is 0 or 3 or 4 && FakeGitMeasurement.IsOperational(method) ? 5 : 0
                }).ToList()
            }).ToList();

    [Test]
    public void ValidPilotPassesAndUsesMedian()
    {
        var verdict = FakeGitMeasurement.EvaluatePilot(Golden());
        verdict.Pass.ShouldBeTrue(); verdict.RealMedian.ShouldBe(10);
        verdict.FakeMedian.ShouldBe(6); verdict.SecondsSaved.ShouldBe(4);
        verdict.PercentSaved.ShouldBe(40);
    }

    [Test]
    [Arguments("ratio-edge")]
    [Arguments("seconds-edge")]
    public void InclusiveThresholdEdgesPass(string mode)
    {
        var rows = Golden();
        var r = mode == "ratio-edge" ? 10 : 3;
        var f = mode == "ratio-edge" ? 7 : 2;
        foreach (var row in rows) row.OuterNoBuildSeconds = row.Backend == "real" ? r : f;
        FakeGitMeasurement.EvaluatePilot(rows).Pass.ShouldBeTrue();
    }

    [Test]
    [Arguments("ratio-only")]
    [Arguments("seconds-only")]
    [Arguments("one-slower-pair")]
    [Arguments("equal-pair")]
    public void EachBenefitConditionIsRequired(string mode)
    {
        var rows = Golden();
        if (mode == "ratio-only") foreach (var row in rows) row.OuterNoBuildSeconds = row.Backend == "real" ? 2 : 1.2;
        if (mode == "seconds-only") foreach (var row in rows) row.OuterNoBuildSeconds = row.Backend == "real" ? 10 : 8;
        if (mode == "one-slower-pair") rows[3].OuterNoBuildSeconds = 5;
        if (mode == "equal-pair") rows[3].OuterNoBuildSeconds = 6;
        FakeGitMeasurement.EvaluatePilot(rows).Pass.ShouldBeFalse();
    }

    [Test]
    [Arguments("missing-row")]
    [Arguments("duplicate-row")]
    [Arguments("mixed-sha")]
    [Arguments("mixed-host")]
    [Arguments("wrong-order")]
    [Arguments("stale-output")]
    public void InvalidSampleIdentityRefuses(string mode)
    {
        var rows = Golden();
        switch (mode)
        {
            case "missing-row": rows.RemoveAt(5); break;
            case "duplicate-row": rows[1].Checkpoint = "CP-3"; break;
            case "mixed-sha": rows[2].SourceSha = new string('b', 40); break;
            case "mixed-host": rows[2].HostId = "other"; break;
            case "wrong-order": (rows[1], rows[2]) = (rows[2], rows[1]); break;
            case "stale-output": rows[0].BuildSourceSha = new string('b', 40); break;
        }
        FakeGitMeasurement.EvaluatePilot(rows).Pass.ShouldBeFalse();
    }

    [Test]
    [Arguments("missing-case")]
    [Arguments("duplicate-case")]
    [Arguments("changed-argument")]
    [Arguments("failed")]
    [Arguments("skipped")]
    [Arguments("rerun")]
    [Arguments("parent-order")]
    [Arguments("file-bytes")]
    [Arguments("exit-code")]
    [Arguments("diagnostic")]
    public void RosterOutcomeAndObservationMismatchRefuse(string mode)
    {
        var rows = Golden();
        var row = rows[1];
        switch (mode)
        {
            case "missing-case": row.Cases.RemoveAt(0); break;
            case "duplicate-case": row.Cases[0].Method = row.Cases[1].Method; break;
            case "changed-argument": row.Cases[0].Arguments = "(unexpected)"; break;
            case "failed": row.Cases[0].Outcome = "failed"; row.Failed = 1; break;
            case "skipped": row.Skipped = 1; break;
            case "rerun": row.Reruns = 1; break;
            case "parent-order": row.Cases[10].Observation += ":parents-reversed"; break;
            case "file-bytes": row.Cases[8].Observation += ":truncated"; break;
            case "exit-code": row.Cases[9].Observation += ":exit=1"; break;
            case "diagnostic": row.Cases[9].Observation += ":unknown-error"; break;
        }
        FakeGitMeasurement.EvaluatePilot(rows).Pass.ShouldBeFalse();
    }

    [Test]
    [Arguments("missing-observer")]
    [Arguments("missing-receipt")]
    [Arguments("fake-launch")]
    [Arguments("real-operation-zero")]
    public void MissingOrEscapedLaunchEvidenceRefuses(string mode)
    {
        var rows = Golden();
        switch (mode)
        {
            case "missing-observer": rows[1].Cases[6].LaunchObserverPresent = false; break;
            case "missing-receipt": rows[1].Cases.RemoveAt(6); break;
            case "fake-launch": rows[1].Cases[6].ObservedGitLaunches = 1; break;
            case "real-operation-zero": rows[0].Cases[6].ObservedGitLaunches = 0; break;
        }
        FakeGitMeasurement.EvaluatePilot(rows).Pass.ShouldBeFalse();
    }

    [Test]
    [Arguments("fake-physical-pilot")]
    [Arguments("real-mock")]
    [Arguments("includes-build")]
    [Arguments("includes-slot-wait")]
    [Arguments("missing-outer-wall")]
    [Arguments("case-sum-as-wall")]
    public void WrongStorageAndTimingEvidenceRefuse(string mode)
    {
        var rows = Golden();
        switch (mode)
        {
            case "fake-physical-pilot": rows[1].Storage = "physical"; break;
            case "real-mock": rows[0].Storage = "mock"; break;
            case "includes-build": rows[1].TimingOrigin = "build+test"; break;
            case "includes-slot-wait": rows[1].TimingOrigin = "slot+test"; break;
            case "missing-outer-wall": rows[1].OuterNoBuildSeconds = null; break;
            case "case-sum-as-wall": rows[1].TimingOrigin = "case-sum"; break;
        }
        FakeGitMeasurement.EvaluatePilot(rows).Pass.ShouldBeFalse();
    }

    [Test]
    [Arguments("oid-alias")]
    [Arguments("ref-case")]
    [Arguments("staged-unstaged")]
    [Arguments("unknown-diagnostic")]
    public void NormalizationPreservesIdentityAndBytes(string mode)
    {
        var rows = Golden();
        rows[1].Cases[8].Observation += ":" + mode;
        FakeGitMeasurement.EvaluatePilot(rows).Pass.ShouldBeFalse();
    }

    [Test]
    [Arguments("missing-contract-row")]
    [Arguments("missing-class")]
    [Arguments("different-sha")]
    [Arguments("fake-not-faster")]
    public void SliceGateRequiresAllPairedSelections(string mode)
    {
        var required = new[] { "admission", "boundary", "contract" };
        var pairs = required.Select(x => new FakeGitMeasurement.SlicePair(x, "sha", 10, 6, true)).ToList();
        switch (mode)
        {
            case "missing-contract-row": pairs[2] = pairs[2] with { ContractPresent = false }; break;
            case "missing-class": pairs.RemoveAt(1); break;
            case "different-sha": pairs[1] = pairs[1] with { SourceSha = "other" }; break;
            case "fake-not-faster": pairs[1] = pairs[1] with { FakeSeconds = 10 }; break;
        }
        FakeGitMeasurement.EvaluateSliceGate(pairs, required).ShouldBeFalse();
    }

    [Test]
    [Arguments("missing-slice")]
    [Arguments("failed-g1")]
    [Arguments("failed-g2")]
    [Arguments("linux-for-windows")]
    [Arguments("native-skip")]
    [Arguments("reused-build-other-sha")]
    [Arguments("missing-retention-entry")]
    public void AuditRequiresCompletedNativeEvidence(string mode)
    {
        var entries = Enumerable.Range(1, 12)
            .Select(n => new FakeGitMeasurement.AuditEntry("S" + n, true,
                n == 12 ? "Windows" : "Linux", false, "sha", "sha", true)).ToList();
        switch (mode)
        {
            case "missing-slice": entries.RemoveAt(0); break;
            case "failed-g1": entries[0] = entries[0] with { GatePassed = false }; break;
            case "failed-g2": entries[3] = entries[3] with { GatePassed = false }; break;
            case "linux-for-windows": entries[11] = entries[11] with { Os = "Linux" }; break;
            case "native-skip": entries[11] = entries[11] with { Skipped = true }; break;
            case "reused-build-other-sha": entries[2] = entries[2] with { BuildSourceSha = "other" }; break;
            case "missing-retention-entry": entries[5] = entries[5] with { RetentionPresent = false }; break;
        }
        FakeGitMeasurement.EvaluateAudit(entries).ShouldBeFalse();
    }

    [Test]
    public void AuditPerformsNoBuildOrTestLaunch()
    {
        var entries = Enumerable.Range(1, 12)
            .Select(n => new FakeGitMeasurement.AuditEntry("S" + n, true,
                n == 12 ? "Windows" : "Linux", false, "sha", "sha", true)).ToList();
        FakeGitMeasurement.EvaluateAudit(entries).ShouldBeTrue();
    }
}
