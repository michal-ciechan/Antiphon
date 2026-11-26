using System.Diagnostics;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;
using static Antiphon.Tests.Scripts.LiveLaneInventoryTests;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class LiveLaneInventoryInvariantTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void ExactCoreRoster()
    {
        var result = LiveLaneProcess.Roster("ExactCoreRoster", LiveLaneRoster.LiveClaudeProject, LiveClaudeAssembly);
        result.Failures.ShouldBeEmpty(string.Join('\n', result.Failures));
    }

    [Test]
    public void EveryLiveGateHasDisposition()
    {
        var failures = Antiphon.Nightly.LiveLaneGateScan.Matrix(LiveLaneProcess.FindRepo());
        failures.ShouldBeEmpty(string.Join('\n', failures));
    }

    /// <summary>
    /// Windows Full 557cdb63: an [Explicit] gate left "diagnostic" stays required in the rc census, but a class filter
    /// never runs an [Explicit] test, so Full reports it missing (RcManifestScaleTests). Every Explicit gate is core or
    /// policy-excluded.
    /// </summary>
    [Test]
    public void ExplicitGatesAreNeverFullRequired()
    {
        var repo = LiveLaneProcess.FindRepo();
        var exclusions = Antiphon.Nightly.LiveLaneGateScan.RcExclusions(repo);
        var required = Antiphon.Nightly.LiveLaneGateScan.Scan(repo).Gates
            .Where(gate => gate.Shapes.Contains("Explicit", StringComparer.Ordinal))
            .Where(gate => Antiphon.Nightly.LiveLaneGateScan.Disposition(gate, exclusions).Disposition == "diagnostic")
            .Select(gate => gate.Key)
            .ToArray();
        required.ShouldBeEmpty(string.Join('\n', required));
    }
}
