using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskDispatchBaseGuardTests
{
    [Test]
    [Timeout(90_000)]
    [NotInParallel("card-1593-capacity")]
    public async Task Stale_claim_preserves_capacity_grant_for_the_fresh_inspection(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("capacity-stale-claim");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "project-default", "master");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (card, project) = await SeedCardWithProjectAsync(db, "capacity-stale-claim");
        project.BaseBranch = "master";
        var parent = Guid.NewGuid();
        await SeedParentSessionAsync(db, parent);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parent);
        task.ProjectId = project.Id;
        await db.SaveChangesAsync(ct);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot,
            clock: clock,
            onLeaseAcquired: async () =>
            {
                await using var edit = CreateContext(schema);
                await edit.Projects.Where(p => p.Id == project.Id)
                    .ExecuteUpdateAsync(p => p.SetProperty(x => x.BaseBranch, "project-default"), ct);
            },
            configure: services =>
            {
                services.AddSingleton(Options.Create(new SupervisionSettings
                {
                    CapacityRecovery = new CapacityRecoverySettings
                    {
                        Enabled = true, AdmissionIntervalSeconds = 60, JitterSeconds = 0,
                        MaxEpisodeAttempts = 3,
                    },
                }));
                services.AddSingleton<CapacityRecoveryService>();
            });
        var recovery = provider.GetRequiredService<CapacityRecoveryService>();
        var wait = await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"task:{task.Id:N}", CapacityWaitConsumerKind.QueuedTask,
            taskId: task.Id, holdAlreadyCleared: true, alias: null), ct);
        task.CapacityWaitId = wait.Id;
        await db.SaveChangesAsync(ct);
        (await recovery.GrantReadyAsync(ct)).ShouldBe(1);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();

        await dispatcher.TickInspectedAsync(ct);

        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct))
            .Status.ShouldBe(AgentTaskStatus.Queued);
        var heldWait = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
        heldWait.AdmissionCount.ShouldBe(0, "a rolled-back stale claim must not spend an admission");
        heldWait.LaunchSessionId.ShouldBeNull();
        var grant = await db.CapacityRecoveryProviderStates.AsNoTracking().SingleAsync(ct);
        grant.GrantedWaitId.ShouldBe(wait.Id);
        grant.GrantedActionKey.ShouldBe(wait.ActionKey);
        grant.LastActionKey.ShouldBeNull();

        await dispatcher.TickInspectedAsync(ct);
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseRef.ShouldBe("project-default");
        var accepted = await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id, ct);
        accepted.AdmissionCount.ShouldBe(1);
        accepted.State.ShouldBe(CapacityRecoveryWaitState.StartAccepted);
        accepted.LaunchSessionId.ShouldBe(dispatched.AgentSessionId);
        (await db.CapacityRecoveryProviderStates.AsNoTracking().SingleAsync(ct)).GrantedWaitId.ShouldBeNull();
        (await db.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == task.Id
            && i.WarningKey == DispatchBaseNotificationPayload.MismatchKey, ct)).ShouldBe(0);
    }
}
