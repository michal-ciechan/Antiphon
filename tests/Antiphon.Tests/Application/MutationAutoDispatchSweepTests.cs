using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0552 V-552-50..72 (D-9, D-12). Every tick goes through the REAL
/// <see cref="AgentTaskService.CreateAsync"/> admission, so a gate that stops creating and a
/// create that is refused are both proven against the door that actually exists.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class MutationAutoDispatchSweepTests
{
    [Test]
    public async Task C552_S01_EligibleTickCreatesOneSourcedMutationTask()
    {
        await using var world = await MutationSweepWorld.CreateAsync();

        var tick = await world.TickAsync();

        tick.Reason.ShouldBe("created");
        tick.Created.ShouldBe(1);
        tick.OperationId.ShouldBe(world.Operation);
        tick.CompanionCardId.ShouldBe(world.Companion);

        await using var db = world.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == tick.TaskId!.Value);
        task.Kind.ShouldBe(AgentTaskKind.Worker);
        task.Role.ShouldBe(AgentTaskRole.Mutation);
        task.Workspace.ShouldBe(WorkspaceMode.Worktree);
        task.CardId.ShouldBe(world.Companion);
        task.SourceLandingOperationId.ShouldBe(world.Operation);
        task.SourceLandingSha.ShouldBe(world.OperationSha);
        task.Title.ShouldBe("post-land mutation checks: CARD-0001");
        task.ExpectedDurationMinutes.ShouldBe(720);
        task.ProjectId.ShouldBe(world.ProjectId);
        task.ReplyTo.ShouldBe(AgentTaskReplyTo.None);
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.AgentSessionId.ShouldBeNull();
        task.MergeTargetRef.ShouldBeNull();
        task.AgentId.ShouldBeNull();
        task.Goal.ShouldBe(await world.ExpectedGoalAsync());

        (await db.AgentSessions.CountAsync()).ShouldBe(0);
        (await db.Cards.SingleAsync(c => c.Id == world.Companion)).Status.ShouldBe(CardStatus.Backlog);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Created)).ShouldBe(1);
        (await world.MutationReadyAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task C552_S02_DisabledCreatesNothing()
    {
        await using var world = await MutationSweepWorld.CreateAsync(s => s.MutationAutoDispatch.Enabled = false);

        var tick = await world.TickAsync();

        tick.Reason.ShouldBe("disabled");
        tick.Created.ShouldBe(0);
        (await world.MutationCountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task C552_S03_PausedCreatesNothing()
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        world.Control.Pause();

        (await world.TickAsync()).Reason.ShouldBe("paused");
        (await world.MutationCountAsync()).ShouldBe(0);

        world.Control.Resume();
        (await world.TickAsync()).Reason.ShouldBe("created");
    }

    [Test]
    [Arguments(AgentTaskStatus.Queued, true)]
    [Arguments(AgentTaskStatus.Queued, false)]
    [Arguments(AgentTaskStatus.Dispatched, true)]
    [Arguments(AgentTaskStatus.Dispatched, false)]
    [Arguments(AgentTaskStatus.Working, true)]
    [Arguments(AgentTaskStatus.Working, false)]
    [Arguments(AgentTaskStatus.Blocked, true)]
    [Arguments(AgentTaskStatus.Blocked, false)]
    public async Task C552_S04_AnyOpenMutationTaskSkipsTheTick(AgentTaskStatus status, bool sameProject)
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        await world.SeedTaskAsync(AgentTaskRole.Mutation, status,
            projectId: sameProject ? world.ProjectId : await world.OtherProjectAsync());

        var tick = await world.TickAsync();

        tick.Reason.ShouldBe("mutation-open");
        tick.Created.ShouldBe(0);
        (await world.MutationCountAsync()).ShouldBe(1);
    }

    [Test]
    [Arguments(AgentTaskStatus.Succeeded)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    public async Task C552_S05_SettledMutationTasksDoNotSkip(AgentTaskStatus status)
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        await world.SeedTaskAsync(AgentTaskRole.Mutation, status);

        (await world.TickAsync()).Reason.ShouldBe("created");
    }

    [Test]
    [Arguments(null, null, "12:00", "created")]
    [Arguments("06:00-22:00", "UTC", "12:00", "created")]
    [Arguments("22:00-06:00", "UTC", "12:00", "outside-window")]
    [Arguments("22:00-06:00", "UTC", "22:00", "created")]
    [Arguments("22:00-06:00", "UTC", "06:00", "outside-window")]
    [Arguments("22:00-06:00", "UTC", "05:59", "created")]
    [Arguments("06:00-21:00", "Asia/Tokyo", "12:00", "outside-window")]
    [Arguments("06:00-21:00", "Asia/Tokyo", "11:59", "created")]
    public async Task C552_S06_ActiveWindow(string? window, string? tz, string clockUtc, string reason)
    {
        await using var world = await MutationSweepWorld.CreateAsync(s =>
        {
            s.MutationAutoDispatch.ActiveWindow = window;
            s.MutationAutoDispatch.TimeZoneId = tz;
        });
        world.SetClock(clockUtc);

        (await world.TickAsync()).Reason.ShouldBe(reason);
    }

    [Test]
    [Arguments(75.00, "budget-met")]
    [Arguments(74.99, "created")]
    [Arguments(75.01, "budget-met")]
    public async Task C552_S07_BudgetCeiling(decimal spend, string reason)
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        await world.SeedTaskAsync(AgentTaskRole.Mutation, AgentTaskStatus.Succeeded, costUsd: spend,
            createdAt: new DateTime(2100, 6, 1, 1, 0, 0, DateTimeKind.Utc));

        (await world.TickAsync()).Reason.ShouldBe(reason);
    }

    [Test]
    [Arguments("2100-05-31T23:59:59Z", 1000.0, "created")]
    [Arguments("2100-06-01T00:00:00Z", 75.0, "budget-met")]
    [Arguments("2100-06-02T00:00:00Z", 1000.0, "created")]
    public async Task C552_S08_OnlyTheCurrentUtcDayCounts(string createdAt, decimal spend, string reason)
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        await world.SeedTaskAsync(AgentTaskRole.Mutation, AgentTaskStatus.Succeeded, costUsd: spend,
            createdAt: DateTime.Parse(createdAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal
                | System.Globalization.DateTimeStyles.AssumeUniversal));

        (await world.TickAsync()).Reason.ShouldBe(reason);
    }

    [Test]
    public async Task C552_S09_NonMutationRoleSpendIsNotCounted()
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        await world.SeedTaskAsync(AgentTaskRole.Code, AgentTaskStatus.Succeeded, costUsd: 1000m,
            createdAt: new DateTime(2100, 6, 1, 1, 0, 0, DateTimeKind.Utc));

        (await world.TickAsync()).Reason.ShouldBe("created");
    }

    [Test]
    public async Task C552_S10_HeldRouteCreatesNothing()
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        world.Availability.Held = true;

        var tick = await world.TickAsync();

        tick.Reason.ShouldBe("route-held");
        (await world.MutationCountAsync()).ShouldBe(0);
        world.Availability.Calls.ShouldHaveSingleItem()
            .ShouldBe((AgentKind.ClaudeCode, ModelLevelAliases.For(AgentKind.ClaudeCode, AgentModelLevel.Frontier)));
    }

    [Test]
    public async Task C552_S11_HeldCheckUsesTheEffectivePin()
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        var pin = await world.PinAsync(AgentKind.Grok, AgentModelLevel.High);

        var tick = await world.TickAsync();

        tick.Reason.ShouldBe("created");
        world.Availability.Calls.ShouldHaveSingleItem()
            .ShouldBe((AgentKind.Grok, ModelLevelAliases.For(AgentKind.Grok, AgentModelLevel.High)));
        await using var db = world.Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == tick.TaskId!.Value);
        task.AgentKind.ShouldBe(AgentKind.Grok);
        task.ModelLevel.ShouldBe(AgentModelLevel.High);
        // AgentTaskService stamps RoutingPinId only when a COMPLEXITY WALK runs (an explicit
        // Complexity on the request, or a multi-candidate walked pin). The sweep deliberately
        // sends no Complexity (plan, Out of scope), so a single-candidate pin resolves the kind
        // and level without stamping the id. The pin's effect on routing is what matters here.
        task.RoutingPinId.ShouldBeNull();
        pin.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task C552_S12_ModelDisabledAtCreateEndsTheTickAndKeepsTheRow()
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        world.UseRealModelAvailability = true;
        await world.HoldModelAsync(AgentKind.ClaudeCode, "fable");

        MutationAutoDispatchTick tick = null!;
        await Should.NotThrowAsync(async () => tick = await world.TickAsync());

        tick.Reason.ShouldBe("create-refused:model_disabled");
        tick.Created.ShouldBe(0);
        (await world.MutationCountAsync()).ShouldBe(0);
        (await world.MutationReadyAsync()).ShouldHaveSingleItem();
        await using var db = world.Host.CreateContext();
        (await db.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.Created)).ShouldBe(0);
    }

    [Test]
    public async Task C552_S13_SubscriptionQuotaLowEndsTheTickAndKeepsTheRow()
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        world.UseQuotaGate = true;
        await world.SeedUsageSampleAsync(AgentKind.ClaudeCode, "ClaudeCode", remainingPercent: 3, hoursToReset: 36);

        var tick = await world.TickAsync();

        tick.Reason.ShouldBe("create-refused:subscription_quota_low");
        (await world.MutationCountAsync()).ShouldBe(0);
        (await world.MutationReadyAsync()).ShouldHaveSingleItem();
    }

    [Test]
    public async Task C552_S14_AbsoluteConcurrencyCapEndsTheTickAndKeepsTheRow()
    {
        await using var world = await MutationSweepWorld.CreateAsync(s => s.MaxOpenTasks = 1);
        await world.SeedTaskAsync(AgentTaskRole.Code, AgentTaskStatus.Queued, projectId: world.ProjectId);

        var tick = await world.TickAsync();

        tick.Reason.ShouldBe("create-refused:concurrency_limit");
        (await world.MutationCountAsync()).ShouldBe(0);
        (await world.MutationReadyAsync()).ShouldHaveSingleItem();
    }

    [Test]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    public async Task C552_S15_PriorAttemptIsNeverAutoRetriedButStaysVisible(AgentTaskStatus status)
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        await world.SeedTaskAsync(AgentTaskRole.Mutation, status, cardId: world.Companion,
            sourceOp: world.Operation);

        var tick = await world.TickAsync();

        tick.Reason.ShouldBe("no-candidate");
        tick.Created.ShouldBe(0);
        var row = (await world.MutationReadyAsync()).ShouldHaveSingleItem();
        row.Card.Id.ShouldBe(world.Companion);
        row.SourceLandingOperationId.ShouldBe(world.Operation);
    }

    [Test]
    public async Task C552_S15b_BlockedPriorAttemptIsMutationOpenAndHidesTheRow()
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        await world.SeedTaskAsync(AgentTaskRole.Mutation, AgentTaskStatus.Blocked, cardId: world.Companion,
            sourceOp: world.Operation);

        (await world.TickAsync()).Reason.ShouldBe("mutation-open");
        (await world.MutationReadyAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task C552_S16_RunnerWithoutCustodyRefusesAndTheRowSurvives()
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        var store = world.Runner.VerificationStoreId;
        world.Runner.VerificationStoreId = null;

        var first = await world.TickAsync();

        first.Reason.ShouldBe("create-refused:verification_custody_unsupported_backend");
        (await world.MutationCountAsync()).ShouldBe(0);

        world.Runner.VerificationStoreId = store;
        (await world.TickAsync()).Reason.ShouldBe("created");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C552_S17_OldestReadyRowIsCreatedFirst(bool secondIsOlder)
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        var second = await world.SeedSecondDebtAsync(secondIsOlder
            ? world.Now.AddHours(-1)
            : world.Now.AddHours(1));

        var tick = await world.TickAsync();

        tick.Reason.ShouldBe("created");
        tick.OperationId.ShouldBe(secondIsOlder ? second.OperationId : world.Operation);
        await using var db = world.Host.CreateContext();
        (await db.AgentTasks.SingleAsync(t => t.Id == tick.TaskId!.Value)).CardId
            .ShouldBe(secondIsOlder ? second.CompanionCardId : world.Companion);
    }

    [Test]
    public async Task C552_S18_OneCreatePerTick()
    {
        await using var world = await MutationSweepWorld.CreateAsync(
            s => s.RolePolicy["Mutation"].RecommendedInFlight = 2);
        await world.SeedSecondDebtAsync(world.Now.AddHours(-1));

        (await world.TickAsync()).Created.ShouldBe(1);
        (await world.TickAsync()).Reason.ShouldBe("mutation-open");
        (await world.MutationCountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task C552_S19_SweepCreatedTaskEntersTheExistingCustodyPath()
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        var tick = await world.TickAsync();
        tick.Reason.ShouldBe("created");

        var binding = await world.ProvisionAndReserveAsync(tick.TaskId!.Value);

        await using var db = world.Host.CreateContext();
        var execution = await db.VerificationExecutions.SingleAsync(e => e.TaskId == tick.TaskId!.Value);
        execution.SourceLandingOperationId.ShouldBe(world.Operation);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == tick.TaskId!.Value);
        task.SourceLandingSha.ShouldBe(world.OperationSha);
        Directory.Exists(task.WorktreePath).ShouldBeTrue();
        binding.ShouldNotBeNull();
    }

    [Test]
    public async Task C552_S20_AcceptedCreateWritesTheCompanionRevision()
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        var before = await world.RevisionsAsync(world.Companion);
        before.Count.ShouldBe(1);
        var previousDescription = await world.DescriptionAsync(world.Companion);

        var tick = await world.TickAsync();

        var after = await world.RevisionsAsync(world.Companion);
        after.Count.ShouldBe(2);
        var revision = after[^1];
        revision.Kind.ShouldBe(CardRevisionKind.ContentEdit);
        revision.Reason.ShouldBe("mutation-auto-dispatch");
        revision.EditedBy.ShouldBe("mutation-sweep");
        revision.Description.ShouldBe(previousDescription);
        (await world.DescriptionAsync(world.Companion)).ShouldContain(
            $"Auto-dispatched Mutation {tick.TaskId!.Value:D} for O={world.Operation:D} at "
            + world.Now.ToString("O"));
    }

    [Test]
    public async Task C552_S21_LostRevisionAfterCreateIsHarmless()
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        var tick = await world.TickAsync();
        await using (var db = world.Host.CreateContext())
        {
            await db.CardRevisions.Where(r => r.CardId == world.Companion && r.Reason == "mutation-auto-dispatch")
                .ExecuteDeleteAsync();
        }

        (await world.TickAsync()).Reason.ShouldBe("mutation-open");

        await using (var cancel = world.Host.CreateContext())
        {
            (await cancel.AgentTasks.SingleAsync(t => t.Id == tick.TaskId!.Value)).Status = AgentTaskStatus.Canceled;
            await cancel.SaveChangesAsync();
        }

        (await world.TickAsync()).Reason.ShouldBe("no-candidate");
        (await world.MutationReadyAsync()).ShouldHaveSingleItem();
    }

    [Test]
    [Arguments("done")]
    [Arguments("canceled")]
    [Arguments("needs-decision")]
    [Arguments("archived")]
    public async Task C552_S22_SweepSkipsTerminalOrArchivedCompanion(string variant)
    {
        await using var world = await MutationSweepWorld.CreateAsync();
        await world.CloseCompanionAsync(variant);

        (await world.TickAsync()).Reason.ShouldBe("no-candidate");
    }

    [Test]
    public async Task C552_S23_NoDebtIsNoCandidate()
    {
        await using var world = await MutationSweepWorld.CreateAsync(land: false);

        (await world.TickAsync()).Reason.ShouldBe("no-candidate");
    }

    /// <summary>
    /// CARD-0552 M-6. A real confirmed publication whose land hook created the companion, plus the
    /// wiring the sweep's create needs. The board is seeded BEFORE the land, which is the whole
    /// difference from <c>PostLandMutationWorld</c>.
    /// </summary>
    internal sealed class MutationSweepWorld : IAsyncDisposable
    {
        public LandingSafetyHarness Host { get; } = new()
        {
            Clock = new FakeTimeProvider(new DateTimeOffset(2100, 6, 1, 0, 0, 0, TimeSpan.Zero)),
        };

        public FakeSessionRunnerClient Runner { get; } = new() { VerificationStoreId = Guid.NewGuid() };
        public OrchestratorControlState Control { get; } = new();
        public RecordingAvailability Availability { get; } = new();
        public DelegationSettings Settings { get; private set; } = new();

        /// <summary>V-552-60: the sweep's own gate stays open and the CREATE door holds the model.</summary>
        public bool UseRealModelAvailability { get; set; }
        public bool UseQuotaGate { get; set; }

        public Guid Operation { get; private set; }
        public Guid Companion { get; private set; }
        public Guid Original { get; private set; }
        public Guid ProjectId { get; private set; }
        public Guid BoardId { get; private set; }
        public string OperationSha { get; private set; } = "";
        public DateTime Now => ((FakeTimeProvider)Host.Clock).GetUtcNow().UtcDateTime;

        public static Task<MutationSweepWorld> CreateAsync(bool land) => CreateAsync(null, land);

        public static async Task<MutationSweepWorld> CreateAsync(
            Action<DelegationSettings>? configure = null, bool land = true)
        {
            var world = new MutationSweepWorld();
            world.Settings = new DelegationSettings();
            configure?.Invoke(world.Settings);
            world.Host.ConfigureServices = services =>
            {
                services.AddSingleton<ISessionRunnerClient>(world.Runner);
                services.AddScoped<SourceLandingAdmission>();
                services.AddScoped<VerificationExecutionService>();
            };
            await world.Host.InitializeAsync();
            try
            {
                await world.Host.Fixture.RequiredAsync(world.Host.Fixture.Repository, "config", "core.autocrlf", "false");
                var seeded = await world.Host.SeedOriginalCardAsync();
                world.ProjectId = seeded.ProjectId;
                world.BoardId = seeded.BoardId;
                world.Original = seeded.CardId;
                if (!land) return world;

                await world.Host.AddSourceAsync();
                await world.Host.RunAsync();
                var op = (await world.Host.OperationAsync())!;
                world.Operation = op.Id;
                world.OperationSha = op.VerifiedSourceSha!;
                world.Companion = op.VerificationCardId
                    ?? throw new InvalidOperationException("the land hook recorded no companion");
                return world;
            }
            catch
            {
                await world.DisposeAsync();
                throw;
            }
        }

        public void SetClock(string utcTimeOfDay) =>
            ((FakeTimeProvider)Host.Clock).SetUtcNow(
                new DateTimeOffset(DateTime.Parse($"2100-06-01T{utcTimeOfDay}:00Z", null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal
                    | System.Globalization.DateTimeStyles.AssumeUniversal)));

        public AgentTaskService TaskService(IServiceProvider services)
        {
            var options = Options.Create(Settings);
            var db = services.GetRequiredService<AppDbContext>();
            SubscriptionQuotaGate? quota = UseQuotaGate
                ? new SubscriptionQuotaGate(new SubscriptionUsageReader(db, Host.Clock),
                    Options.Create(new SubscriptionQuotaGateSettings()), Host.Clock,
                    NullLogger<SubscriptionQuotaGate>.Instance)
                : null;
            return new AgentTaskService(db,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                options, new MockEventBus(), new RecordingSessionStopper(), Host.Clock,
                NullLogger<AgentTaskService>.Instance,
                quotaGate: quota,
                modelAvailability: UseRealModelAvailability
                    ? new ModelAvailability(db, Host.Clock, NullLogger<ModelAvailability>.Instance)
                    : null,
                routingPins: new RoutingPinService(db, Host.Clock, NullLogger<RoutingPinService>.Instance),
                complexityRouting: new ComplexityRoutingService(db, options, Host.Clock),
                openGate: new DelegationOpenGate(db, options),
                sourceLanding: services.GetRequiredService<SourceLandingAdmission>());
        }

        public async Task<MutationAutoDispatchTick> TickAsync()
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sweep = new MutationAutoDispatchSweep(db, TaskService(scope.ServiceProvider),
                Options.Create(Settings), Host.Clock, Control,
                NullLogger<MutationAutoDispatchSweep>.Instance, Availability,
                new RoutingPinService(db, Host.Clock, NullLogger<RoutingPinService>.Instance));
            return await sweep.TickAsync(CancellationToken.None);
        }

        public async Task<IReadOnlyList<AgentTaskPipelineReadyDto>> MutationReadyAsync()
        {
            await using var db = Host.CreateContext();
            var options = Options.Create(Settings);
            var dto = await new AgentTaskPipelineStatusService(db, options,
                new AreaMapLoader(options, NullLogger<AreaMapLoader>.Instance), Host.Clock)
                .GetAsync(CancellationToken.None);
            return dto.Stages.Single(s => s.Role == AgentTaskRole.Mutation).Ready;
        }

        public async Task<int> MutationCountAsync()
        {
            await using var db = Host.CreateContext();
            return await db.AgentTasks.CountAsync(t => t.Role == AgentTaskRole.Mutation);
        }

        public async Task<string> ExpectedGoalAsync()
        {
            await using var db = Host.CreateContext();
            var original = await db.Cards.AsNoTracking().SingleAsync(c => c.Id == Original);
            var companion = await db.Cards.AsNoTracking().SingleAsync(c => c.Id == Companion);
            var owner = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Host.Fixture.TaskId);
            var op = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == Operation);
            return MutationAutoDispatchSweep.ComposeGoal(original, companion, owner, null, null, op);
        }

        public async Task<AgentTask> SeedTaskAsync(
            AgentTaskRole role,
            AgentTaskStatus status,
            Guid? cardId = null,
            Guid? sourceOp = null,
            decimal costUsd = 0m,
            DateTime? createdAt = null,
            Guid? projectId = null)
        {
            await using var db = Host.CreateContext();
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id, RootTaskId = id, Title = "seeded", Goal = "seeded",
                Kind = AgentTaskKind.Worker, Role = role, Status = status,
                Workspace = sourceOp is null ? WorkspaceMode.Shared : WorkspaceMode.Worktree,
                WorkingDirectory = Host.Fixture.Repository, RepoPath = Host.Fixture.Repository,
                CardId = cardId, ProjectId = projectId, CostUsd = costUsd,
                SourceLandingOperationId = sourceOp,
                SourceLandingSha = sourceOp is null ? null : OperationSha,
                CreatedAt = createdAt ?? Now,
                CompletedAt = status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed
                    or AgentTaskStatus.Canceled ? Now : null,
            };
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        /// <summary>A real second project, so the fleet-wide WIP gate is tested across buckets.</summary>
        public async Task<Guid> OtherProjectAsync()
        {
            await using var db = Host.CreateContext();
            var project = new Project
            {
                Id = Guid.NewGuid(), Name = "c552-other",
                GitRepositoryUrl = "https://example.test/other.git", CreatedAt = Now, UpdatedAt = Now,
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            return project.Id;
        }

        public async Task<Guid> PinAsync(AgentKind kind, AgentModelLevel level)
        {
            await using var db = Host.CreateContext();
            var pin = new RoutingPin
            {
                Id = Guid.NewGuid(), CardId = Companion, Role = AgentTaskRole.Mutation,
                Provenance = RoutingPinProvenance.Human, Strength = RoutingPinStrength.Required,
                AgentKind = kind, ModelLevel = level, Reason = "operator: this battery on " + kind,
                CreatedAt = Now, UpdatedAt = Now,
            };
            db.RoutingPins.Add(pin);
            await db.SaveChangesAsync();
            return pin.Id;
        }

        public async Task HoldModelAsync(AgentKind kind, string alias)
        {
            await using var db = Host.CreateContext();
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = Guid.NewGuid(), Kind = kind, ModelAlias = alias,
                Source = ModelAvailabilitySource.Manual, DisabledUntil = Now.AddMinutes(30),
                HitAt = Now, Reason = "fixture hold",
            });
            await db.SaveChangesAsync();
        }

        public async Task SeedUsageSampleAsync(
            AgentKind provider, string subscriptionKey, int remainingPercent, int hoursToReset)
        {
            await using var db = Host.CreateContext();
            db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
            {
                Id = Guid.NewGuid(), Provider = provider, SubscriptionKey = subscriptionKey,
                PlanLabel = "SuperPlan", RemainingPercent = remainingPercent,
                ResetsAt = Now.AddHours(hoursToReset), ObservedAt = Now,
                AgentSessionId = Guid.NewGuid(), SourceCommand = "/status",
                ParseStatus = SubscriptionUsageParseStatus.Parsed, RawExcerpt = "seeded",
            });
            await db.SaveChangesAsync();
        }

        public async Task CloseCompanionAsync(string variant)
        {
            await using var db = Host.CreateContext();
            var card = await db.Cards.SingleAsync(c => c.Id == Companion);
            if (variant == "archived") card.ArchivedAt = Now;
            else
                card.Status = variant switch
                {
                    "done" => CardStatus.Done,
                    "canceled" => CardStatus.Canceled,
                    _ => CardStatus.NeedsDecision,
                };
            await db.SaveChangesAsync();
        }

        /// <summary>A second Done original with its own confirmed operation and Backlog companion.</summary>
        public async Task<(Guid OwnerTaskId, Guid OperationId, Guid CompanionCardId)> SeedSecondDebtAsync(
            DateTime remoteConfirmedAt)
        {
            await using var db = Host.CreateContext();
            var first = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == Operation);
            var doneColumn = await db.BoardColumns.FirstAsync(c => c.BoardId == BoardId && c.CardStatus == CardStatus.Done);
            var backlogColumn = await db.BoardColumns.FirstAsync(c => c.BoardId == BoardId && c.CardStatus == CardStatus.Backlog);
            var original = new Card
            {
                Id = Guid.NewGuid(), BoardId = BoardId, BoardColumnId = doneColumn.Id, Identifier = "CARD-0003",
                Title = "CARD-0003 title", Status = CardStatus.Done, CompletedAt = Now,
                CreatedAt = Now, UpdatedAt = Now,
            };
            var companion = new Card
            {
                Id = Guid.NewGuid(), BoardId = BoardId, BoardColumnId = backlogColumn.Id, Identifier = "CARD-0004",
                Title = "Post-land verification: CARD-0003", Status = CardStatus.Backlog,
                LabelsJson = BoardService.SerializeLabels([PostLandVerificationCompanions.Label]),
                CreatedAt = Now, UpdatedAt = Now,
            };
            var ownerId = Guid.NewGuid();
            var owner = new AgentTask
            {
                Id = ownerId, RootTaskId = ownerId, Title = "second owner", Goal = "second owner",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Status = AgentTaskStatus.Succeeded,
                Workspace = WorkspaceMode.Worktree, WorkingDirectory = Host.Fixture.Repository,
                RepoPath = Host.Fixture.Repository, WorktreeBranch = "feat/second", CardId = original.Id,
                ProjectId = ProjectId, CreatedAt = Now, CompletedAt = Now,
            };
            db.AddRange(original, companion, owner);
            await db.SaveChangesAsync();

            var op = new AgentTaskLanding
            {
                Id = Guid.NewGuid(), TaskId = owner.Id, Active = true,
                Phase = LandPhase.PublicationConfirmed, Publication = LandPublicationOutcome.Landed,
                Cleanup = LandCleanupStatus.Complete, Mode = LandOperationMode.Fresh,
                OriginalSourceSha = Host.Fixture.SeedSha, VerifiedSourceSha = Host.Fixture.SeedSha,
                ObservedRemoteTargetSha = Host.Fixture.SeedSha, TargetBeforeSha = Host.Fixture.SeedSha,
                TargetFullRef = Host.Fixture.TargetRef, DestinationFullRef = Host.Fixture.TargetRef,
                SourceFullRef = "refs/heads/feat/second",
                // Admission resolves the git common directory from disk and compares it to the
                // operation's, so the synthetic op must name what the real land recorded.
                RepositoryPath = first.RepositoryPath, CommonDirectory = first.CommonDirectory,
                WorktreePath = first.WorktreePath, GitDirectory = first.GitDirectory,
                SourcePinned = true, TargetPinned = true,
                VerificationSkipReason = "exact_remote_containment", VerifiedAt = Now,
                RemoteFingerprint = new string('a', 64), RemoteConfirmedAt = remoteConfirmedAt,
                ConfirmationMethod = "push-endpoint-read-fetch-ancestry",
                VerificationCardId = companion.Id, CreatedAt = Now, UpdatedAt = Now,
            };
            op.RecoveryRefPrefix = $"refs/antiphon/land/{owner.Id:N}/{op.Id:N}";
            db.AgentTaskLandings.Add(op);
            await db.SaveChangesAsync();
            new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
            return (owner.Id, op.Id, companion.Id);
        }

        public async Task<List<CardRevision>> RevisionsAsync(Guid cardId)
        {
            await using var db = Host.CreateContext();
            return await db.CardRevisions.AsNoTracking().Where(r => r.CardId == cardId)
                .OrderBy(r => r.RevisionNumber).ToListAsync();
        }

        public async Task<string> DescriptionAsync(Guid cardId)
        {
            await using var db = Host.CreateContext();
            return (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId)).Description;
        }

        /// <summary>V-552-67: the created row through the shipped worktree and custody path.</summary>
        public async Task<VerificationExecutionBinding> ProvisionAndReserveAsync(Guid taskId)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            await using (var lease = await Host.Services.GetRequiredService<IRepositoryMutationLease>()
                .TryAcquireAsync(task.RepoPath!, default))
            {
                await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>()
                    .CreateForTaskAsync(task, lease!, default);
            }

            await db.SaveChangesAsync();

            await using var reserve = Host.Services.CreateAsyncScope();
            var reserveDb = reserve.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await reserveDb.Database.BeginTransactionAsync();
            var locked = await reserveDb.AgentTasks
                .FromSqlInterpolated($"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {taskId} FOR UPDATE").SingleAsync();
            var session = new AgentSession
            {
                Id = Guid.NewGuid(), Status = SessionStatus.Starting, Cwd = locked.WorktreePath!,
                StartedAt = Now, CreatedAt = Now, AgentKind = AgentKind.Raw,
            };
            reserveDb.AgentSessions.Add(session);
            locked.AgentSessionId = session.Id;
            locked.Status = AgentTaskStatus.Dispatched;
            var binding = await reserve.ServiceProvider.GetRequiredService<VerificationExecutionService>()
                .ReserveAsync(locked, session, default);
            await reserveDb.SaveChangesAsync();
            await tx.CommitAsync();
            return binding;
        }

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }
}
