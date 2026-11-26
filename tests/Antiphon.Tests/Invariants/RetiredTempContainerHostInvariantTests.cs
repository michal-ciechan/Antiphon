using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;
using Antiphon.Tests.Scripts;
using static Antiphon.Tests.Scripts.RetiredTempContainerHostTests;

namespace Antiphon.Tests.Invariants;

[NativeLinuxOnly("CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately.")]
[Category("Unit")]
[Category("RepositoryInvariant")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RetiredTempContainerHostInvariantTests
{
    [Test]
    public async Task Production_compose_mount_roster_is_complete()
    {
        C1008HostFixture.RequireNativeLinux();
        using var f = Fixture("session-runner", "state-init");
        var run = await Cleanup(f);
        run.Exit.ShouldBe(0, run.Output);
        var candidates = Receipt(f)["candidates"]!.AsArray();
        candidates.Select(c=>c!["Topology"]!["mounts"]!.AsArray().Count).ShouldBe(new[]{15,3},"c994-mounts: logical roster including the token bind");
    }
}
