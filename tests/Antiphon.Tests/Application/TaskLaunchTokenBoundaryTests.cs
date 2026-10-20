using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed partial class TaskLaunchTokenBoundaryTests
{
    [Test]
    [Arguments(AgentTaskRole.Check)]
    [Arguments(AgentTaskRole.Distill)]
    [Arguments(AgentTaskRole.Diagnose)]
    public async Task C1301_tokenless_specialist_fresh_spawn_has_worker_token(AgentTaskRole role)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var seeded = await SeedAsync(schema, workspace.Path, role);
        var sink = new RecordingLaunchSink
        {
            Observe = (sessionId, generation, spec) =>
            {
                // Observe durable state at the external-effect boundary, before any later save.
                using var db = Open(schema);
                var task = db.AgentTasks.AsNoTracking().Single(t => t.Id == seeded.TaskId);
                var session = db.AgentSessions.AsNoTracking().Single(s => s.Id == sessionId);
                var tokenCommitted = spec.Env.TryGetValue("ANTIPHON_TASK_TOKEN", out var token)
                    && !string.IsNullOrWhiteSpace(token)
                    && task.TokenHash == AgentTaskService.HashToken(token);
                var generationCommitted = task.AgentSessionId == sessionId
                    && session.Status == SessionStatus.Starting
                    && session.StartedAt == generation;
                return new BoundaryObservation(tokenCommitted, generationCommitted);
            },
        };
        await using var services = CreateServices(schema, workspace.Path, sink);
        await using var dispatchScope = services.CreateAsyncScope();
        try
        {
            using (var before = Open(schema))
                (await before.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId)).TokenHash.ShouldBeNull();
            AgentTaskService.RawTokens.ContainsKey(seeded.TaskId).ShouldBeFalse();

            await dispatchScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .TickAsync(CancellationToken.None);

            var launch = sink.Launches.ShouldHaveSingleItem();
            launch.Boundary.ShouldNotBeNull().TokenCommitted.ShouldBeTrue(
                "the fresh specialist needs an already committed, matching task token");
            launch.Boundary.GenerationCommitted.ShouldBeTrue(
                "the exact Starting generation must already be committed at enqueue");
            launch.SessionId.ShouldNotBe(seeded.PreviousSessionId);
            launch.AgentId.ShouldBe(seeded.AgentId);
            var token = launch.Spec.Env["ANTIPHON_TASK_TOKEN"];
            launch.Spec.Env["ANTIPHON_TASK_ID"].ShouldBe(seeded.TaskId.ToString("D"));
            launch.Spec.Env["ANTIPHON_SESSION_ID"].ShouldBe(launch.SessionId.ToString("D"));
            launch.Spec.Args.Any(arg => arg.Contains(token, StringComparison.Ordinal)).ShouldBeFalse();

            await using var authScope = services.CreateAsyncScope();
            var caller = await authScope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .AuthenticateAsync(token, CancellationToken.None);
            caller.Task.ShouldNotBeNull().Id.ShouldBe(seeded.TaskId);
            caller.Task.Kind.ShouldBe(AgentTaskKind.Worker);
            caller.Task.Role.ShouldBe(role);
            caller.SessionId.ShouldBe(launch.SessionId);
            caller.MayDelegate.ShouldBeFalse("fresh specialist credentials retain Worker authority");
        }
        finally
        {
            AgentTaskService.RawTokens.TryRemove(seeded.TaskId, out _);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1301_token_hash_save_failure_blocks_launch(bool relaunch)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var seeded = await SeedAsync(schema, workspace.Path, AgentTaskRole.Check);
        var sink = new RecordingLaunchSink();
        var fault = new TokenHashSaveFault(schema, seeded.TaskId);
        await using var services = CreateServices(schema, workspace.Path, sink, fault);
        try
        {
            if (relaunch)
            {
                await using var initialScope = services.CreateAsyncScope();
                await initialScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                    .TickAsync(CancellationToken.None);
                sink.Launches.ShouldHaveSingleItem();
            }

            Guid previousSession;
            using (var before = Open(schema))
            {
                var task = await before.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                // A known prior hash makes the exact late replacement write distinguishable
                // from the preceding claim/session commit and all fixture writes.
                task.TokenHash = AgentTaskService.NewToken().Hash;
                await before.SaveChangesAsync();
                fault.Arm(task.TokenHash);
                previousSession = task.AgentSessionId ?? seeded.PreviousSessionId;
            }
            AgentTaskService.RawTokens.TryRemove(seeded.TaskId, out _);
            var launchesBefore = sink.Launches.Count;

            await using var dispatchScope = services.CreateAsyncScope();
            var dispatcher = dispatchScope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            if (relaunch)
                await dispatcher.RelaunchWedgedAsync(seeded.TaskId, previousSession, CancellationToken.None);
            else
                await dispatcher.TickAsync(CancellationToken.None);

            fault.Fired.ShouldBe(1, "the replacement-hash save must reach the injected persistence failure exactly once");
            fault.PriorHashStillDurable.ShouldBeTrue();
            fault.StartingGenerationCommitted.ShouldBeTrue("the claim must commit before the faulted hash save");
            fault.SessionId.ShouldNotBeNull().ShouldNotBe(previousSession);
            sink.Launches.Count.ShouldBe(launchesBefore, "a failed token-hash save must prevent launch");
            sink.Launches.Any(l => l.SessionId == fault.SessionId).ShouldBeFalse();

            using var after = Open(schema);
            var stored = await after.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == seeded.TaskId);
            stored.AgentSessionId.ShouldBe(fault.SessionId);
            (await after.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == fault.SessionId))
                .StartedAt.ShouldBe(fault.Generation);
            // A later failure/brief save may persist tracked changes. Claim cleanup and the
            // final hash are intentionally outside this launch-ordering contract.
        }
        finally
        {
            AgentTaskService.RawTokens.TryRemove(seeded.TaskId, out _);
        }
    }
}
