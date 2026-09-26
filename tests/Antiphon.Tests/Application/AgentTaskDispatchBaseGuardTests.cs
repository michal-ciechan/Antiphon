using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0215: a card-bound Worktree task must not branch from master while a same-card
/// kept sibling is still off to the side. Hold while that sibling's land is in flight;
/// warn (and still dispatch) when the branch is simply not landed; stay silent when the
/// branch is already gone.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public partial class AgentTaskDispatchBaseGuardTests
{
    [Test]
    [Timeout(30_000)]
    public async Task a_sibling_land_in_flight_holds_until_the_base_contains_it(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-hold");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        sibling.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        await SeedPendingSiblingLandAsync(db, repo, sibling);
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();

        await dispatcher.TickAsync(ct);

        var held = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        held.Status.ShouldBe(AgentTaskStatus.Queued);
        held.WorktreePath.ShouldBeNull();
        var heldEvents = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held)
            .ToListAsync(ct);
        heldEvents.ShouldHaveSingleItem();
        heldEvents[0].Detail.ShouldContain(sibling.WorktreeBranch!);
        heldEvents[0].Detail.ShouldContain(DelegationReportFormatter.Short(sibling.Id));
        heldEvents[0].Detail.ShouldContain("is landing");

        var heldSibling = await db.AgentTasks.SingleAsync(t => t.Id == sibling.Id, ct);
        heldSibling.LandRequestedAt = null;
        var landedRequest = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == sibling.CurrentLandRequestId, ct);
        landedRequest.IsPending = false;
        landedRequest.State = LandRequestState.Completed;
        await db.SaveChangesAsync(ct);
        await repo.GitAsync("merge", "--ff-only", sibling.WorktreeBranch!);

        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreePath.ShouldNotBeNull();
        Directory.Exists(dispatched.WorktreePath).ShouldBeTrue();
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(1);
        (await ScratchGitRepo.GitInAsync(
            dispatched.WorktreePath!, "merge-base", "--is-ancestor", sibling.WorktreeBranch!, "HEAD"))
            .Ok.ShouldBeTrue();
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_test_design_worktree_is_held_while_its_card_plan_land_is_in_flight(
        CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0146-td-hold");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0146");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0146");
        sibling.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        await SeedPendingSiblingLandAsync(db, repo, sibling);
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(
            db, repo.Path, card.Id, parentSessionId, role: AgentTaskRole.TestDesign);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();

        await dispatcher.TickAsync(ct);

        var held = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        held.Status.ShouldBe(AgentTaskStatus.Queued);
        held.WorktreePath.ShouldBeNull();
        var heldEvents = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held)
            .ToListAsync(ct);
        heldEvents.ShouldHaveSingleItem();
        heldEvents[0].Detail.ShouldContain(sibling.WorktreeBranch!);
        heldEvents[0].Detail.ShouldContain("is landing");
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_kept_sibling_with_no_land_dispatches_with_a_warning_and_whenidle_note(
        CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-warn");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreePath.ShouldNotBeNull();

        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();

        var warning = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct);
        warning.Detail.ShouldContain(sibling.WorktreeBranch!);
        var tip = (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "--short", sibling.WorktreeBranch!))
            .StdOut.Trim();
        warning.Detail.ShouldContain(tip);
        warning.Detail.ShouldContain("Land " + DelegationReportFormatter.Short(sibling.Id));

        var notes = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == parentSessionId)
            .ToListAsync(ct);
        notes.ShouldHaveSingleItem();
        notes[0].Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        notes[0].Body.ShouldContain(sibling.WorktreeBranch!);
        notes[0].Body.ShouldContain(tip);
    }

    [Test]
    [Timeout(30_000)]
    public async Task C499_V06_ARepairIsHeldWhileItsOwnerIsLanding(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c499-v06");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        // Different card so CARD-0215 sibling-base hold cannot match.
        var ownerCard = await SeedCardAsync(db, "CARD-0499");
        var repairCard = await SeedCardAsync(db, "CARD-0499b");
        var owner = await SeedKeptSiblingAsync(db, repo, ownerCard.Id, "owner work");
        owner.Role = AgentTaskRole.Code;
        owner.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var repair = await SeedQueuedWorktreeTaskAsync(db, repo.Path, repairCard.Id, parentSessionId);
        repair.RepairSourceTaskId = owner.Id;
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);

        var held = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == repair.Id, ct);
        held.Status.ShouldBe(AgentTaskStatus.Queued);
        held.WorktreePath.ShouldBeNull();
        var heldEvents = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Held)
            .ToListAsync(ct);
        heldEvents.ShouldHaveSingleItem();
        heldEvents[0].Detail.ShouldBe($"{DelegationReportFormatter.Short(owner.Id)} is landing");
        heldEvents[0].Detail.ShouldNotContain("kept branch");

        var liveOwner = await db.AgentTasks.SingleAsync(t => t.Id == owner.Id, ct);
        liveOwner.LandRequestedAt = null;
        await db.SaveChangesAsync(ct);
        await repo.GitAsync("checkout", owner.WorktreeBranch!);
        await dispatcher.TickAsync(ct);
        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == repair.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(1);
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_sibling_whose_branch_was_deleted_is_silent(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-gone");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        await repo.GitAsync("branch", "-D", sibling.WorktreeBranch!);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId: null);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct)).ShouldBe(0);
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_stranded_request_row_with_a_null_column_only_warns(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("card0215-stranded");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0215");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, commitMessage: "docs(plan): CARD-0215");
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = sibling.Id,
            Type = AgentTaskEventType.LandRequested,
            Detail = "Land requested",
            At = DateTime.UtcNow.AddMinutes(-1),
        });
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        await dispatcher.TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(0);
        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();
        var warning = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct);
        warning.Detail.ShouldContain(sibling.WorktreeBranch!);
    }

    [Test]
    [Timeout(30_000)]
    public async Task C508_MissingDefaultWarns(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-missing-default");
        await repo.CommitFileAsync("README.md", "base\n");
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0508");
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        var missing = "missing-" + Guid.NewGuid().ToString("N")[..8];
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512 }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = repo.WorktreeRoot,
            WorktreeAddTimeoutSeconds = 180,
            DefaultBranch = missing,
        });
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddSingleton<LandDeliveryBoundary>();
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.RepoHead);
        dispatched.WorktreeBaseRef.ShouldBe("HEAD");
        (await ScratchGitRepo.GitInAsync(dispatched.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(head);
        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();
        var intent = await db.AgentTaskDispatchWarningIntents.AsNoTracking()
            .SingleAsync(i => i.TaskId == task.Id && i.WarningKey == DispatchBaseNotificationPayload.DefaultUnresolvedKey, ct);
        intent.Detail.ShouldContain(missing);
        var warning = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == intent.Id, ct);
        warning.Detail.ShouldContain(missing);
        warning.Type.ShouldBe(AgentTaskEventType.Warning);
    }

    [Test]
    [Timeout(30_000)]
    public async Task C508_RepairRecordsOwnerAndSkipsSiblings(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-repair-skip");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var ownerCard = await SeedCardAsync(db, "CARD-0499");
        var repairCard = await SeedCardAsync(db, "CARD-0508");
        var owner = await SeedKeptSiblingAsync(db, repo, ownerCard.Id, "owner work");
        owner.Role = AgentTaskRole.Code;
        await repo.GitAsync("checkout", owner.WorktreeBranch!);
        var ownerSha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var other = await SeedKeptSiblingAsync(db, repo, repairCard.Id, "other sibling");
        other.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var repair = await SeedQueuedWorktreeTaskAsync(db, repo.Path, repairCard.Id, parentSessionId);
        repair.RepairSourceTaskId = owner.Id;
        await db.SaveChangesAsync(ct);
        await repo.GitAsync("checkout", owner.WorktreeBranch!);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == repair.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Repair);
        dispatched.WorktreeBaseTaskId.ShouldBe(owner.Id);
        dispatched.WorktreeBaseRef.ShouldBe(ownerSha);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Held, ct)).ShouldBe(0);
        await MaterializeAndDeliverAsync(scope.ServiceProvider, repair.Id, ct);
        db.ChangeTracker.Clear();
        (await db.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == repair.Id, ct)).ShouldBe(0);
        (await db.AgentTaskLandNotifications.CountAsync(
            n => n.TaskId == repair.Id && n.Kind == LandNotificationKind.DispatchBase, ct)).ShouldBe(0);
        // The repair path still carries its OWN pre-existing prep warning (the owner branch is
        // checked out, so the snapshot is routed to an isolated branch). That is not a
        // dispatch-base warning: assert on the shape, not on a bare Warning-event count, or this
        // guard silently also asserts unrelated repair behaviour.
        var warnings = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Warning)
            .ToListAsync(ct);
        foreach (var warning in warnings)
        {
            warning.Detail.ShouldNotContain(other.WorktreeBranch!);
            warning.Detail.ShouldNotContain("sibling containment was evaluated against");
            warning.Detail.ShouldNotContain("configured default branch");
        }

        warnings.ShouldContain(w => w.Detail.Contains("occupied source", StringComparison.Ordinal));
    }

    /// <summary>
    /// V-16 / G-48, G-49, G-65, G-116: one <c>base-observation-stale</c> intent per successful
    /// claim whose observed base differs from the base the worktree was actually cut from, with
    /// zero, one and two divergent siblings, and none at all when the two refs agree.
    /// </summary>
    [Test]
    [Timeout(120_000)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(1, false)]
    public async Task C508_GuardRefMismatchWarnedOnce(int divergentSiblings, bool moveTheDefault, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-mismatch");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (card, project) = await SeedCardWithProjectAsync(db, "CARD-0508");
        // M -> P: the project's configured default is observed before the lease, then edited to a
        // different real branch while the lease is held. The locked claim resolves the new one.
        project.BaseBranch = "master";
        await db.SaveChangesAsync(ct);
        await repo.GitAsync("branch", "project-default", "master");
        const string observedRef = "master";
        var expectedRef = moveTheDefault ? "project-default" : "master";

        var siblings = new List<AgentTask>();
        for (var i = 0; i < divergentSiblings; i++)
            siblings.Add(await SeedKeptSiblingAsync(db, repo, card.Id, $"divergent sibling {i}"));

        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        task.ProjectId = project.Id;
        await db.SaveChangesAsync(ct);

        var projectId = project.Id;
        var connection = schema.ConnectionString;
        await using var provider = CreateProvider(
            connection, repo.WorktreeRoot,
            onLeaseAcquired: moveTheDefault
                ? async () =>
                {
                    await using var edit = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
                    await edit.Projects.Where(p => p.Id == projectId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.BaseBranch, "project-default"), ct);
                }
            : null);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseRef.ShouldBe(expectedRef);
        dispatched.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.DefaultBranch);

        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();
        var intents = await db.AgentTaskDispatchWarningIntents.AsNoTracking()
            .Where(i => i.TaskId == task.Id).ToListAsync(ct);
        var mismatches = intents
            .Where(i => i.WarningKey == DispatchBaseNotificationPayload.MismatchKey).ToList();
        if (moveTheDefault)
        {
            mismatches.ShouldHaveSingleItem();
            mismatches[0].Detail.ShouldContain(observedRef);
            mismatches[0].Detail.ShouldContain(expectedRef);
            // The pair exists once, under the intent's own preallocated identity.
            var note = await db.AgentTaskLandNotifications.AsNoTracking()
                .SingleAsync(n => n.Id == mismatches[0].NotificationId, ct);
            note.SourceEventId.ShouldBe(mismatches[0].Id);
            note.Kind.ShouldBe(LandNotificationKind.DispatchBase);
            (await db.AgentTaskEvents.CountAsync(e => e.Id == mismatches[0].Id, ct)).ShouldBe(1);
        }
        else
        {
            mismatches.ShouldBeEmpty();
        }

        var siblingIntents = intents
            .Where(i => i.WarningKey.StartsWith(
                DispatchBaseNotificationPayload.SiblingKeyPrefix, StringComparison.Ordinal))
            .ToList();
        siblingIntents.Count.ShouldBe(divergentSiblings);
        foreach (var sibling in siblings)
        {
            siblingIntents.ShouldContain(
                i => i.WarningKey == DispatchBaseNotificationPayload.SiblingKey(sibling.Id));
        }

        intents.Count.ShouldBe(divergentSiblings + (moveTheDefault ? 1 : 0));
    }

    /// <summary>
    /// V-31 / G-22: containment is evaluated against the ACTUAL configured default, not a hard
    /// <c>master</c>. A sibling rebased (cherry-picked, so a different sha and the same patch) onto
    /// the configured default is contained and stays silent, even though <c>master</c> lacks it.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_RebasedSiblingUsesActualDefault(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-rebased-default");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (card, project) = await SeedCardWithProjectAsync(db, "CARD-0508");
        project.BaseBranch = "release";
        await db.SaveChangesAsync(ct);

        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, "rebased sibling work");
        var siblingTip = (await repo.GitReadAsync("rev-parse", sibling.WorktreeBranch!)).Trim();
        await repo.GitAsync("checkout", "-b", "release", "master");
        // A commit of its own first, so the replay is a genuinely different sha and not the
        // identical object git produces when the cherry-pick parent is the original parent.
        await repo.CommitFileAsync("release-only.md", "release diverges\n");
        await repo.GitAsync("cherry-pick", siblingTip);
        var releaseTip = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        releaseTip.ShouldNotBe(siblingTip);
        await repo.GitAsync("checkout", "master");

        // The control: plain master does NOT carry the patch, so a hard-coded master base would warn.
        CherryContains(
            (await ScratchGitRepo.GitInAsync(repo.Path, "cherry", "master", sibling.WorktreeBranch!)).StdOut)
            .ShouldBeFalse();
        CherryContains(
            (await ScratchGitRepo.GitInAsync(repo.Path, "cherry", "release", sibling.WorktreeBranch!)).StdOut)
            .ShouldBeTrue();

        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        task.ProjectId = project.Id;
        await db.SaveChangesAsync(ct);

        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseRef.ShouldBe("release");
        dispatched.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.DefaultBranch);
        dispatched.WorktreeBaseSha.ShouldBe(releaseTip);
        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();
        (await db.AgentTaskDispatchWarningIntents.CountAsync(i => i.TaskId == task.Id, ct)).ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning, ct)).ShouldBe(0);
    }

    /// <summary>
    /// V-30 / G-104: the guard observes HEAD independently once the chosen default fails to
    /// resolve — it does not cascade to a lower-priority setting and it does not silently fall
    /// back to <c>master</c>. Here master EXISTS and differs from HEAD, and the sibling is
    /// contained in master but not in HEAD, so a base evaluated against master would be silent.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C508_GuardPreservesFailedDefault(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c508-failed-default");
        await repo.CommitFileAsync("README.md", "base\n");
        var basement = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var card = await SeedCardAsync(db, "CARD-0508");
        var sibling = await SeedKeptSiblingAsync(db, repo, card.Id, "sibling above the basement");
        var siblingTip = (await repo.GitReadAsync("rev-parse", sibling.WorktreeBranch!)).Trim();
        // master carries the sibling's patch; the detached HEAD the guard must observe does not.
        await repo.CommitFileAsync("master-only.md", "master diverges\n");
        await repo.GitAsync("cherry-pick", siblingTip);
        var masterTip = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        masterTip.ShouldNotBe(basement);
        // master must contain the patch, so only a base evaluated at HEAD can warn.
        CherryContains(
            (await ScratchGitRepo.GitInAsync(repo.Path, "cherry", "master", sibling.WorktreeBranch!)).StdOut)
            .ShouldBeTrue();
        CherryContains(
            (await ScratchGitRepo.GitInAsync(repo.Path, "cherry", basement, sibling.WorktreeBranch!)).StdOut)
            .ShouldBeFalse();
        await repo.GitAsync("checkout", "--detach", basement);

        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(db, parentSessionId);
        var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parentSessionId);
        await db.SaveChangesAsync(ct);

        var missing = "missing-" + Guid.NewGuid().ToString("N")[..8];
        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot, defaultBranch: missing);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.RepoHead);
        dispatched.WorktreeBaseRef.ShouldBe("HEAD");
        dispatched.WorktreeBaseSha.ShouldBe(basement);

        await MaterializeAndDeliverAsync(scope.ServiceProvider, task.Id, ct);
        db.ChangeTracker.Clear();
        var intents = await db.AgentTaskDispatchWarningIntents.AsNoTracking()
            .Where(i => i.TaskId == task.Id).ToListAsync(ct);
        intents.Count.ShouldBe(2);
        var unresolved = intents.Single(i => i.WarningKey == DispatchBaseNotificationPayload.DefaultUnresolvedKey);
        unresolved.Detail.ShouldContain(missing);
        unresolved.Detail.ShouldNotContain("master");
        var siblingIntent = intents.Single(
            i => i.WarningKey == DispatchBaseNotificationPayload.SiblingKey(sibling.Id));
        siblingIntent.Detail.ShouldContain(sibling.WorktreeBranch!);
        intents.ShouldNotContain(i => i.WarningKey == DispatchBaseNotificationPayload.MismatchKey);
    }

    private static async Task MaterializeAndDeliverAsync(IServiceProvider services, Guid taskId, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var materializer = services.GetRequiredService<DispatchBaseWarningIntentService>();
        var intents = await db.AgentTaskDispatchWarningIntents.Where(i => i.TaskId == taskId).ToListAsync(ct);
        foreach (var intent in intents)
            await materializer.MaterializeAsync(intent.Id, ct);
        var notifier = services.GetRequiredService<AgentTaskLandNotificationService>();
        var notes = await db.AgentTaskLandNotifications.Where(n => n.TaskId == taskId).ToListAsync(ct);
        foreach (var note in notes)
            await notifier.ReconcileAsync(note.Id, ct);
    }

    private static async Task<AgentTask> SeedKeptSiblingAsync(
        AppDbContext db, ScratchGitRepo repo, Guid cardId, string commitMessage,
        string? startRef = null, bool alias = false)
    {
        var id = Guid.NewGuid();
        var branch = $"feat/card-task-{DelegationReportFormatter.Short(id)}";
        await repo.GitAsync("checkout", "-b", branch, startRef ?? "HEAD");
        var file = $"plan-{DelegationReportFormatter.Short(id)}.md";
        if (!alias)
        {
            await File.WriteAllTextAsync(Path.Combine(repo.Path, file), commitMessage + "\n");
            await repo.GitAsync("add", file);
            await repo.GitAsync("commit", "-m", commitMessage);
        }
        await repo.GitAsync("checkout", "master");

        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "CARD-0215 plan",
            Goal = "Write the plan.",
            Role = AgentTaskRole.Plan,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Low,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path,
            RepoPath = repo.Path,
            CardId = cardId,
            WorktreeBranch = branch,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            CompletedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static async Task<AgentTask> SeedQueuedWorktreeTaskAsync(
        AppDbContext db, string repoPath, Guid cardId, Guid? parentSessionId,
        AgentTaskRole role = AgentTaskRole.Code)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = role == AgentTaskRole.TestDesign ? "CARD-0146 test design" : "CARD-0215 execute",
            Goal = role == AgentTaskRole.TestDesign ? "Write the verification section." : "Build the plan.",
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repoPath,
            RepoPath = repoPath,
            CardId = cardId,
            ParentSessionId = parentSessionId,
            ReplyTo = parentSessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static async Task SeedParentSessionAsync(AppDbContext db, Guid parentSessionId)
    {
        db.AgentSessions.Add(new AgentSession
        {
            Id = parentSessionId,
            DefinitionName = "card0215-parent",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            StartedAt = DateTime.UtcNow.AddHours(-1),
            LastSeenAt = DateTime.UtcNow,
        });
        await Task.CompletedTask;
    }

    /// <summary>
    /// The same predicate the production guard uses: <c>git cherry base branch</c> means contained
    /// when it prints nothing, or only '-' lines. A '+' line is a patch the base does not carry.
    /// </summary>
    private static bool CherryContains(string cherryStdOut)
    {
        var lines = cherryStdOut.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 || Array.TrueForAll(lines, static line => line.StartsWith('-'));
    }

    private static async Task<Card> SeedCardAsync(AppDbContext db, string identifier) =>
        (await SeedCardWithProjectAsync(db, identifier)).Card;

    private static async Task<(Card Card, Project Project)> SeedCardWithProjectAsync(
        AppDbContext db, string identifier)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"card0215-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/card0215.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"CARD-0215 {Guid.NewGuid():N}",
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = $"{identifier} ancestry",
            Description = "CARD-0215.",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return (card, project);
    }

    /// <summary>
    /// Runs <paramref name="onAcquired"/> once, the first time the repository mutation lease is
    /// taken. That is exactly the window between the dispatcher's pre-lease base observation and
    /// the locked claim, so a test can move the base under the claim deterministically.
    /// </summary>
    private sealed class LeaseHook(IRepositoryMutationLease inner, Func<Task> onAcquired)
        : IRepositoryMutationLease
    {
        private int _fired;

        public async Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
                await onAcquired();
            return await inner.TryAcquireAsync(repository, ct);
        }

        public bool Owns(RepositoryLease lease, string commonDirectory) =>
            inner.Owns(lease, commonDirectory);
    }

    private static async Task SeedPendingSiblingLandAsync(AppDbContext db, ScratchGitRepo repo, AgentTask sibling)
    {
        var request = new AgentTaskLandRequest
        {
            Id = Guid.NewGuid(), TaskId = sibling.Id, RequestedAt = sibling.LandRequestedAt!.Value,
            LastEvaluatedAt = sibling.LandRequestedAt.Value,
            LastProgressAt = sibling.LandRequestedAt.Value,
            State = LandRequestState.Queued, IsPending = true,
            ExpectedSourceSha = (await repo.GitReadAsync("rev-parse", sibling.WorktreeBranch!)).Trim(),
        };
        db.AgentTaskLandRequests.Add(request);
        sibling.CurrentLandRequestId = request.Id;
    }

    private static ServiceProvider CreateProvider(
        string connectionString,
        string worktreeBase,
        string defaultBranch = "master",
        Func<Task>? onLeaseAcquired = null,
        Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null,
        LandDeliveryBoundary? boundary = null,
        ILandingGit? git = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (git is not null) services.AddSingleton(git);
        if (onLeaseAcquired is not null)
        {
            // Registered BEFORE the graph so its TryAdd keeps this decorator. The hook fires once,
            // after the pre-lease base observation and before the locked claim reads the route.
            services.AddSingleton<IRepositoryMutationLease>(sp =>
                new LeaseHook(new RepositoryMutationLease(sp.GetRequiredService<ILandingGit>()), onLeaseAcquired));
            services.TryAddSingleton<ILandingGit, LandingGit>();
        }

        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(connectionString);
            if (interceptor is not null) o.AddInterceptors(interceptor);
        });
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512 }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = worktreeBase,
            WorktreeAddTimeoutSeconds = 180,
            DefaultBranch = defaultBranch,
        });
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddSingleton<LandDeliveryBoundary>(boundary ?? new LandDeliveryBoundary());
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider();
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
