using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandRecoveryTests
{
    [Test]
    [Arguments("local-advance")]
    [Arguments("push")]
    [Arguments("directory-remove")]
    public async Task C448_V16_RestartReconcilesAcknowledgementGap(string boundary)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = await h.AddSourceAsync();
        var hit = false;
        h.Fixture.Git.AfterCommand = (_, args, result) =>
        {
            if (!hit && result.Succeeded && (boundary switch
                {
                    "local-advance" => args.Contains("merge") && args.Contains("--ff-only"),
                    "push" => args[0] == "push",
                    _ => args.Contains("worktree") && args.Contains("remove"),
                }))
            {
                hit = true;
                throw new SimulatedServerCrash();
            }
            return Task.CompletedTask;
        };
        await Should.ThrowAsync<SimulatedServerCrash>(() => h.RunAsync());
        hit.ShouldBeTrue();
        var interrupted = (await h.OperationAsync()).ShouldNotBeNull();
        interrupted.Phase.ShouldBe(boundary switch
        {
            "local-advance" => LandPhase.TargetAdvanceStarted,
            "push" => LandPhase.PushStarted,
            _ => LandPhase.CleanupStarted,
        });
        interrupted.VerifiedSourceSha.ShouldBe(sha);
        h.Fixture.Git.AfterCommand = null;
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync(); // New DI scope, DbContext and service, no in-memory operation reuse.
        var recovered = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(recovered).ShouldBeTrue();
        recovered.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        recovered.Id.ShouldBe(interrupted.Id);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase"));
        h.Fixture.Git.Trace.Count(a => a[0] == "push").ShouldBe(boundary == "local-advance" ? 1 : 0);
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(sha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V17_InterruptedRebaseRequiresInspectionAndExplicitRepost()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        h.Fixture.Git.AfterCommand = (_, args, result) =>
        {
            if (args.Contains("rebase") && result.Succeeded) throw new SimulatedServerCrash();
            return Task.CompletedTask;
        };
        await Should.ThrowAsync<SimulatedServerCrash>(() => h.RunAsync());
        h.Fixture.Git.AfterCommand = null;
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        var refused = (await h.OperationAsync()).ShouldNotBeNull();
        refused.Phase.ShouldBe(LandPhase.Refused);
        refused.LastReason.ShouldBe("interrupted_rebase_requires_inspection");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--abort") || a[0] == "push" || a.Contains("remove"));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", refused.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await h.RepostAsync();
        await h.RunAsync();
        var completed = (await h.OperationAsync()).ShouldNotBeNull();
        completed.Id.ShouldNotBe(refused.Id);
        completed.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", refused.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V31_UnknownSchemaCannotResumeMutation()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.Phase = LandPhase.TargetAdvanceStarted;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        await using (var db = h.CreateContext())
        {
            var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == h.Fixture.TaskId);
            op.SchemaVersion = 999;
            await db.SaveChangesAsync();
        }
        h.Fixture.Git.Trace.Clear();
        try { await h.RunAsync(); }
        catch (InvalidOperationException) { /* Older protocol discovers schema only after mutation. */ }
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        var resumedMutation = h.Fixture.Git.Trace.Any(a => a[0] == "push" || a.Contains("merge") || a.Contains("rebase") || a.Contains("remove"));
        resumedMutation.ShouldBeFalse("unknown schema must refuse before any resumed mutation");
        (await h.OperationAsync())!.LastReason.ShouldBe("landing_schema_unsupported");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private sealed class SimulatedServerCrash : Exception;
}
