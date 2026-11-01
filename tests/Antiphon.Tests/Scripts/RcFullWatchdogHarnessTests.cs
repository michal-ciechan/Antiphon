using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1514 S3. Existing heartbeat and C1397 contracts stay closed while
// progress identity and launch admission are added.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcFullWatchdogHarnessTests
{
    private static readonly string[] HeartbeatAssertions =
    [
        "supervisor monitor CPU does not hold idleMinutes at zero",
        "monitor and heartbeat writer inside the chunk tree are excluded",
        "a busy test host is not a hang while the monitor burns CPU",
        "busy test host keeps idleMinutes at zero",
        "trx growth clears a would-be hang",
        "trx growth resets idleMinutes",
        "chunk-log byte growth clears a would-be hang",
        "chunk-log byte growth resets idleMinutes",
        "plannedSeconds below 10 minutes uses the 10 minute floor",
        "idle for the 10 minute floor is a hang",
        "a 30 minute plan does not hang at the 20 minute default",
        "idle for the planned 30 minutes is a hang",
        "a chunk with no test host is the suspect",
        "groups-planned.json exposes plannedSeconds for the current chunk",
        "heartbeat JSON keeps existing names and adds hangReason plus suspectPids",
        "a reread heartbeat continues the idle clock",
        "heartbeat library is ASCII and does not kill",
    ];

    [Test]
    [Timeout(60_000)]
    public async Task HeartbeatExistingContract(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("pwsh");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(C1514Support.RepoRoot(), "scripts", "test-qualification-supervisor-heartbeat.ps1"));
        var ran = await C1514Support.RunProcess(start, cancellationToken);
        ran.ExitCode.ShouldBe(0, ran.Text);
        var lines = ran.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var pass = lines.Count(line => line.StartsWith("PASS ", StringComparison.Ordinal));
        var fail = lines.Count(line => line.StartsWith("FAIL ", StringComparison.Ordinal));
        pass.ShouldBe(17, ran.Text);
        fail.ShouldBe(0, ran.Text);
        ran.Text.ShouldContain("passed=17 failed=0", Case.Sensitive, ran.Text);
        foreach (var name in HeartbeatAssertions)
            ran.Text.ShouldContain("PASS " + name, Case.Sensitive, ran.Text);
    }

    [Test]
    [Timeout(120_000)]
    public Task C1397ExistingContract(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync(
            "test-nightly-tests.ps1",
            "C1397",
            "C1397_RcAntiphonLaunchGroups",
            1,
            new[] { "C1397 rc antiphon launch groups are far below 1346 and assign every class once" },
            ScriptHarnessOptions.Default,
            cancellationToken);
}
