using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.BlockedParkWireTests;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NotInParallel("ClaudeConfigDirEnv")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorkspaceSourceVerificationWireTests
{
    [Test]
    public async Task C1065_RepositoryIdentityReadIsBoundToSessionCheckout()
    {
        await using var world = new SeatWorld("Codex");
        await world.StartAsync();
        var adapter = new PhoneHomeRuntimeAdapter(world.Runtime, RunnerBuildIdentity.Resolve());
        await using var wire = await SeatWire.StartAsync(world, adapter, phoneHome: true);
        var reply = await wire.DispatchAsync((PhoneHomeOperation)37, new
        {
            sessionId = world.Tail.SessionId, path = world.Source.Mirror,
            expectedRunnerStoreId = world.Runtime.RunnerStoreId,
            expectedAcceptedStartedAt = world.Request.ExpectedAcceptedStartedAt, version = 1
        });
        reply.Kind.ShouldBe(PhoneHomeFrameKind.Result, "V-28: the session-bound identity operation must exist");
    }

    [Test]
    public async Task C1065_LocalSourceModesVerifyFreshAtRelease()
    {
        await using var world = new SeatWorld("Codex");
        var release = await world.QualifyAsync();
        world.Child.Kill = _ => { world.Child.Exit(); return Task.FromResult(true); };
        var result = await world.ReleaseAsync(release with { ParkVersion = 2 });
        result.Outcome.ShouldBe(TerminalSeatReleaseOutcome.Released,
            "V-29: a typed Published receipt must be freshly verified and release the qualified session");
        world.AssertReleased();
    }
}
