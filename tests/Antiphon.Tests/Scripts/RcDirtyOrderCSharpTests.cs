using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1836 S6c. Same witness suffixes as RcDirtyLaunchOrderTests. Red until the compiled type exists.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcDirtyOrderCSharpTests
{
    // CARD-1836 S4 (CP-4, V-4, G-12, G-13): an exact impact-then-cost launch order with same-attempt gate expansion.
    [Test, Timeout(60_000)]
    public void Dispatch_orders_ready_chunks_by_impact_then_cost(CancellationToken cancellationToken)
    {
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        RcPortCorpus.WithScratchRepo(repo =>
        {
            string Git(params string[] args) => Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(repo, args).Trim();
            Git("init", "-q", "-b", "main");
            Git("-c", "user.name=f", "-c", "user.email=f@example.invalid", "-c", "commit.gpgsign=false", "commit", "-q", "--allow-empty", "-m", "x");
            var tip = Git("rev-parse", "HEAD");
            RcImpactObligation O(string key, string impact, double? wall, string disposition = "Dirty", string gate = "", double? members = null, bool equal = false) => new()
            {
                Key = key, Impact = impact, PriorWallSeconds = wall, MemberSeconds = members, Disposition = disposition, Gate = gate, ContractsEqual = equal,
                Origin = disposition == "Carry" ? "receipt:" + key : "",
            };
            RcImpactObligation[] Obligations(bool contractsEqual, string f1Disposition = "Dirty") =>
            [
                O("F1", "mandatory", 90, f1Disposition), O("F2", "transitive", 30), O("P", "product-direct", 70), O("G", "direct", 10, equal: contractsEqual),
                O("D2", "direct", 40), O("D1", "direct", 40), O("T", "transitive", 50), O("H1", "transitive", 20, "Held", "G"), O("H2", "transitive", 60, "Held", "G"),
                O("M", "mandatory", null, members: 20), O("U", "mandatory", null), O("C", "direct", 5, "Carry"),
            ];
            // CARD-1823 history: F1 and F2 failed at an earlier attempt; an unrelated pass of F1 at the same tip discharges nothing.
            object[] attempts =
            [
                new Dictionary<string, object> { ["sequence"] = 1L, ["sourceSha"] = tip, ["results"] = new object[]
                {
                    new Dictionary<string, object> { ["key"] = "F1", ["outcome"] = "failed" }, new Dictionary<string, object> { ["key"] = "F2", ["outcome"] = "failed" },
                } },
                new Dictionary<string, object> { ["sequence"] = 2L, ["sourceSha"] = tip, ["results"] = new object[] { new Dictionary<string, object> { ["key"] = "F1", ["outcome"] = "passed" } } },
            ];
            RcImpactRun Run(RcImpactObligation[] obligations, string gateOutcome) =>
                RcImpactQueue.Dispatch(obligations, attempts, Array.Empty<object>(), repo, tip, key => key == "G" ? gateOutcome : "pass");
            string Keys(RcImpactRun run) => string.Join(",", run.Launches.Select(l => l.Key));
            bool Exact(RcImpactRun run, RcImpactObligation[] obligations)
            {
                var accounted = run.Launches.Select(l => l.Key).Concat(run.Carried.Select(c => c.Key)).ToArray();
                return accounted.Length == obligations.Length && accounted.Distinct(StringComparer.Ordinal).Count() == accounted.Length
                    && accounted.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(obligations.Select(o => o.Key).OrderBy(k => k, StringComparer.Ordinal));
            }

            var green = Run(Obligations(true), "pass");
            rows.Check(Keys(green) == "F2,F1,P,G,D1,D2,T,M,U", "s4-exact-sequence", Keys(green));
            var redGate = Run(Obligations(true), "FAIL");
            rows.Check(Keys(redGate) == "F2,F1,P,G,D1,D2,H1,T,H2,M,U" && redGate.Expanded.SequenceEqual(["H1", "H2"]), "s4-expansion-launches-held", Keys(redGate));
            var drifted = Run(Obligations(false), "pass");
            rows.Check(green.Carried.Any(c => c.Key == "H1" && c.Origin == "trust-gate-passed:G") && green.Carried.Any(c => c.Key == "H2" && c.Origin == "trust-gate-passed:G")
                && green.Carried.Any(c => c.Key == "C" && c.Origin == "receipt:C") && Keys(drifted).Contains("H1") && Keys(drifted).Contains("H2"),
                "s4-gate-pass-carries-held", string.Join(",", green.Carried.Select(c => c.Key + "<-" + c.Origin)));
            rows.Check(Exact(green, Obligations(true)) && Exact(redGate, Obligations(true)) && Exact(drifted, Obligations(false)), "s4-no-duplicate-no-missing");
            var refused = Should.Throw<InvalidOperationException>(() => Run(Obligations(true, "Carry"), "pass"));
            rows.Check(refused.Message == "carry-with-open-failure", "s4-open-failure-not-carried", refused.Message);
            rows.Check(green.Launches.Single(l => l.Key == "F1").Tier == 0 && green.Launches.Single(l => l.Key == "F2").Tier == 0, "s4-same-tip-pass-no-discharge",
                string.Join(",", green.Launches.Select(l => l.Key + ":" + l.Tier)));
            rows.Check(green.Launches.Select(l => l.Rank).SequenceEqual(Enumerable.Range(1, 9)) && green.Launches.Single(l => l.Key == "F2").CostSource == "prior-wall"
                && green.Launches.Single(l => l.Key == "M").CostSource == "member-sum" && green.Launches.Single(l => l.Key == "U").CostSource == "unknown"
                && green.Launches.Single(l => l.Key == "U").Cost == null, "s4-cost-source-recorded",
                string.Join(",", green.Launches.Select(l => l.Rank + ":" + l.Key + ":" + l.CostSource)));
            cancellationToken.ThrowIfCancellationRequested();
        });
        rows.ShouldAllPass(7, "s4-exact-sequence", "s4-expansion-launches-held", "s4-gate-pass-carries-held", "s4-no-duplicate-no-missing", "s4-open-failure-not-carried",
            "s4-same-tip-pass-no-discharge", "s4-cost-source-recorded");
    }

    [Test]
    public void ScratchRepoCleanupClearsReadOnlyObjects()
    {
        var observer = Directory.CreateTempSubdirectory("rc-port-object-").FullName;
        var alias = Path.Combine(observer, "object");
        string? scratch = null;
        try
        {
            RcPortCorpus.WithScratchRepo(repo =>
            {
                scratch = repo;
                var fanout = Directory.CreateDirectory(Path.Combine(repo, ".git", "objects", "ab"));
                var looseObject = Path.Combine(fanout.FullName, new string('c', 38));
                File.WriteAllText(looseObject, "fixture loose object");
                if (OperatingSystem.IsLinux()) NativeHardLink.Create(alias, looseObject);
                File.SetAttributes(looseObject, File.GetAttributes(looseObject) | FileAttributes.ReadOnly);
                File.GetAttributes(looseObject).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
            });

            Directory.Exists(scratch).ShouldBeFalse();
            // Linux can unlink a read-only file. The second name observes whether cleanup cleared the bit.
            if (OperatingSystem.IsLinux()) File.GetAttributes(alias).HasFlag(FileAttributes.ReadOnly).ShouldBeFalse();
        }
        finally
        {
            if (scratch is not null) GitFixtureCleanup.Delete(scratch);
            GitFixtureCleanup.Delete(observer);
        }
    }

    [Test]
    public void OnlyDirtyAndSampleLaunch()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("OnlyDirtyAndSampleLaunch");
    }

    [Test]
    public void OpenFailurePrecedesChanged()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("OpenFailurePrecedesChanged");
    }

    [Test]
    public void SameTipPassCannotClearFailure()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("SameTipPassCannotClearFailure");
    }

    [Test]
    public void CaseDistinctKeysStaySeparate()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("CaseDistinctKeysStaySeparate");
    }

    [Test]
    public void CarryWithOpenFailureRefuses()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("CarryWithOpenFailureRefuses");
    }

    [Test]
    public void UnknownShaRefuses()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("UnknownShaRefuses");
    }

    // CARD-2010. Expected output is the pre-port PowerShell result.
    [Test]
    public void PsObjectWrappedAttempt()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("PsObjectWrappedAttempt");
    }

    [Test]
    public void NonIntegerSequence()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("NonIntegerSequence");
    }

    [Test]
    public void ArrayValuedKey()
    {
        RcPortCorpus.RequireDirty();
        RcPortCorpus.Replay("ArrayValuedKey");
    }
}
