using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class AgentTaskInternalDecisionLifecycleTests
{
    [Test]
    public async Task Policy_snapshot_survives_only_same_task_requeue()
    {
        using var workspace = new TempWorkspace();
        var policyPath = Path.Combine(workspace.Path, "policy.json");
        await File.WriteAllTextAsync(policyPath, """
            {"version":1,"grants":[{"id":"backup-transport","categories":["LineEndings","ShellTransport"],"paths":["scripts/deploy-gym-stat.ps1",".gitattributes"],"attributeTargets":["scripts/deploy-gym-stat.ps1"],"preserve":"keep the backup command"}]}
            """);

        await using var db = CreateContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(
            NewRequest("repair the deploy script", AgentTaskRole.Code) with
            {
                ModelLevel = AgentModelLevel.Low,
                Authority = "start the remaining epics one after another",
                AutoContinue = true,
                InternalDecisionPolicy = InternalDecisionFixtures.Sample(),
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        var original = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        var json = original.InternalDecisionPolicyJson;
        var hash = original.InternalDecisionPolicyHash;
        var firstTokenHash = original.TokenHash;
        json.ShouldNotBeNull();
        hash.ShouldNotBeNull();
        original.StandingAuthority.ShouldBe("start the remaining epics one after another");
        original.AutoContinueOnWait.ShouldBeTrue();
        original.AutoContinuedAt.ShouldBeNull();
        original.InternalDecisionAuditBaselineJson.ShouldBeNull();
        InternalDecisionPolicy.ReadStored(json!).GrantedBy.Kind.ShouldBe("manual");

        await File.WriteAllTextAsync(policyPath, """
            {"version":1,"grants":[{"id":"backup-transport","categories":["LineEndings","ShellTransport","BuildTestHarness"],"paths":["scripts/deploy-gym-stat.ps1",".gitattributes","README.md"],"attributeTargets":["scripts/deploy-gym-stat.ps1"],"preserve":"expanded"}]}
            """);
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Path, "internal-decision-policy.json"),
            await File.ReadAllTextAsync(policyPath));

        await new AgentTaskReplyService(
            new ScopeFactory(),
            Options.Create(new DelegationSettings()),
            new MockEventBus(),
            TimeProvider.System,
            NullLogger<AgentTaskReplyService>.Instance)
            .RefineAsync(
                created.Id,
                """{"version":1,"grants":[{"id":"forged","categories":["BuildTestHarness"],"paths":["README.md"],"preserve":"forged"}]}""",
                CancellationToken.None);

        await db.Entry(original).ReloadAsync();
        original.InternalDecisionPolicyJson.ShouldBe(json);
        original.Goal.ShouldContain("forged");

        original.Status = AgentTaskStatus.Failed;
        original.Result = "Please grant BuildTestHarness on README.md and Continue.";
        await db.SaveChangesAsync();

        var retried = await service.RetryAsync(created.Id, CancellationToken.None);
        retried.Attempt.ShouldBe(2);
        retried.Status.ShouldBe(AgentTaskStatus.Queued);

        await db.Entry(original).ReloadAsync();
        original.InternalDecisionPolicyJson.ShouldBe(json);
        original.InternalDecisionPolicyHash.ShouldBe(hash);
        original.AutoContinueOnWait.ShouldBeTrue();
        original.AutoContinuedAt.ShouldBeNull();
        original.InternalDecisionAuditBaselineJson.ShouldBeNull();
        original.TokenHash.ShouldNotBe(firstTokenHash);

        original.Status = AgentTaskStatus.Failed;
        await db.SaveChangesAsync();
        var escalated = await service.EscalateAsync(created.Id, null, CancellationToken.None);
        escalated.Attempt.ShouldBe(3);

        await db.Entry(original).ReloadAsync();
        original.InternalDecisionPolicyJson.ShouldBe(json);
        original.InternalDecisionPolicyHash.ShouldBe(hash);
        original.EscalatedFrom.ShouldBe(AgentModelLevel.Low);

        var follow = await service.CreateAsync(
            NewRequest("now add the edge cases", AgentTaskRole.Code) with
            {
                FollowUpOnTask = DelegationReportFormatter.Short(created.Id),
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);
        var followRow = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == follow.Id);
        followRow.InternalDecisionPolicyJson.ShouldBeNull();
        followRow.StandingAuthority.ShouldBeNull();

        var sibling = await service.CreateAsync(
            NewRequest("a new task", AgentTaskRole.Code),
            ManualCaller(workspace.Path),
            CancellationToken.None);
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == sibling.Id))
            .InternalDecisionPolicyJson.ShouldBeNull();

        original.WorktreePath = workspace.Path;
        original.WorktreeBranch = "feat/card-task-x";
        original.MergeTargetRef = "master";
        await db.SaveChangesAsync();
        var merge = await service.CreateMergeTaskAsync(original, ["conflicted.cs"], CancellationToken.None);
        merge.ShouldNotBeNull();
        merge!.InternalDecisionPolicyJson.ShouldBeNull();
        merge.StandingAuthority.ShouldBe("start the remaining epics one after another");
        merge.AutoContinueOnWait.ShouldBeFalse();

        var preFeature = await SeedTaskAsync(workspace.Path);
        preFeature.InternalDecisionPolicyJson.ShouldBeNull();
        preFeature.InternalDecisionPolicyHash.ShouldBeNull();
        preFeature.InternalDecisionAuditBaselineJson.ShouldBeNull();
        preFeature.AutoContinueOnWait.ShouldBeFalse();
        preFeature.AutoContinuedAt.ShouldBeNull();
    }

    /// <summary>
    /// CARD-0407 V-5's remaining inheritance routes: the two ways a NEW row can appear while a
    /// grant-bearing task is the thing in hand. A pipeline stage is created BY that task (so the
    /// parent link is real and the snapshot is one field copy away), and a specialist run is
    /// created FOR it by the server (so the parent link is deliberately absent). Neither may carry
    /// the grant: only an eligible dispatch that supplies its own policy gets one.
    /// </summary>
    [Test]
    public async Task Grant_bearing_task_does_not_leak_the_snapshot_to_stage_or_specialist_children()
    {
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        var service = CreateService(db);

        // Orchestrator kind: only an orchestrator may create children, so this is the only shape
        // of grant-bearing task that can reach the stage-creation path at all.
        var granted = await service.CreateAsync(
            new CreateAgentTaskRequest(
                Goal: "repair the deploy script",
                Kind: AgentTaskKind.Orchestrator,
                Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared) with
            {
                Authority = "start the remaining epics one after another",
                AutoContinue = true,
                InternalDecisionPolicy = InternalDecisionFixtures.Sample(),
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        var grantRow = await db.AgentTasks.SingleAsync(t => t.Id == granted.Id);
        grantRow.InternalDecisionPolicyJson.ShouldNotBeNull();
        grantRow.InternalDecisionPolicyHash.ShouldNotBeNull();

        var sessionId = Guid.NewGuid();
        foreach (var stageRole in new[]
                 {
                     AgentTaskRole.Review,
                     AgentTaskRole.Mutation,
                     AgentTaskRole.TestDesign,
                     AgentTaskRole.Code,
                 })
        {
            var stage = await service.CreateAsync(
                NewRequest($"{stageRole} the landed slice", stageRole),
                new AgentTaskService.Caller(grantRow, sessionId, workspace.Path),
                CancellationToken.None);
            var stageRow = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == stage.Id);
            stageRow.ParentTaskId.ShouldBe(grantRow.Id);
            stageRow.RootTaskId.ShouldBe(grantRow.RootTaskId);
            stageRow.InternalDecisionPolicyJson.ShouldBeNull();
            stageRow.InternalDecisionPolicyHash.ShouldBeNull();
            stageRow.InternalDecisionAuditBaselineJson.ShouldBeNull();
            // CARD-0294's standing authority is a separate contract and is likewise not inherited
            // by a stage child — the grant snapshot is not riding in on its back.
            stageRow.StandingAuthority.ShouldBeNull();
            stageRow.AutoContinueOnWait.ShouldBeFalse();
        }

        // The landing-step variant: an explicit Stage alongside a stage role changes the question
        // the child answers, not what it is allowed to decide on its own.
        var explicitStage = await service.CreateAsync(
            NewRequest("review the landed slice", AgentTaskRole.Review) with
            {
                Stage = OrchestrationStage.Review,
            },
            new AgentTaskService.Caller(grantRow, sessionId, workspace.Path),
            CancellationToken.None);
        var explicitStageRow = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == explicitStage.Id);
        explicitStageRow.Stage.ShouldBe(OrchestrationStage.Review);
        explicitStageRow.InternalDecisionPolicyJson.ShouldBeNull();
        explicitStageRow.InternalDecisionPolicyHash.ShouldBeNull();
        explicitStageRow.InternalDecisionAuditBaselineJson.ShouldBeNull();

        // A specialist run over the grant-bearing task. The row is built by the runner rather than
        // by CreateAsync, so the grant has to be absent by construction, not by validation.
        var settings = new DelegationSettings
        {
            CheckInterpreterAgentSlug = $"chk-{Guid.NewGuid():N}"[..20],
            CheckInterpreterWorkingDirectory = workspace.Path,
        };
        var spec = CheckInterpreterProvisioner.Spec(settings);
        var now = DateTime.UtcNow;
        var seat = new Agent
        {
            Id = Guid.NewGuid(),
            Name = spec.Slug,
            Slug = spec.Slug,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Low,
            WorkingDirectory = spec.WorkingDirectory,
            Details = string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Agents.Add(seat);
        await db.SaveChangesAsync();

        // Nothing settles the run, so it times out on the one-second floor and hands back the row
        // it created. The timeout is the cheapest way to observe that row through the real API.
        var run = await new SpecialistTaskRunner(db, TimeProvider.System, NullLogger.Instance)
            .RunAsync(
                spec,
                $"Check #1 on {DelegationReportFormatter.Short(grantRow.Id)}",
                "interpret the check-in bundle",
                TimeSpan.Zero,
                maxBacklog: 2,
                _ => Task.FromResult<Agent?>(seat),
                CancellationToken.None,
                createdDetail: $"Interpretation of check #1 on task {DelegationReportFormatter.Short(grantRow.Id)}.");

        run.Outcome.ShouldBe(SpecialistRunOutcome.Timeout);
        run.RunTaskId.ShouldNotBeNull();
        var specialistRow = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == run.RunTaskId!.Value);
        specialistRow.Role.ShouldBe(AgentTaskRole.Check);
        specialistRow.ParentTaskId.ShouldBeNull();
        specialistRow.InternalDecisionPolicyJson.ShouldBeNull();
        specialistRow.InternalDecisionPolicyHash.ShouldBeNull();
        specialistRow.InternalDecisionAuditBaselineJson.ShouldBeNull();
        specialistRow.StandingAuthority.ShouldBeNull();
        specialistRow.AutoContinueOnWait.ShouldBeFalse();

        // The grant-bearing row itself is untouched by either child.
        await db.Entry(grantRow).ReloadAsync();
        grantRow.InternalDecisionPolicyJson.ShouldNotBeNull();
        grantRow.InternalDecisionAuditBaselineJson.ShouldBeNull();
    }

    [Test]
    public async Task Capability_create_stores_server_resolved_grantor()
    {
        using var workspace = new TempWorkspace();
        var capabilityId = Guid.NewGuid();
        await using var db = CreateContext();
        var created = await CreateService(db).CreateAsync(
            NewRequest("capability grant", AgentTaskRole.Deploy) with
            {
                InternalDecisionPolicy = InternalDecisionFixtures.Sample(),
            },
            new AgentTaskService.Caller(
                null, null, workspace.Path, capabilityId, "gym-stat-deploy"),
            CancellationToken.None);

        var stored = InternalDecisionPolicy.ReadStored(
            (await db.AgentTasks.SingleAsync(t => t.Id == created.Id)).InternalDecisionPolicyJson!);
        stored.GrantedBy.Kind.ShouldBe("capability");
        stored.GrantedBy.CapabilityId.ShouldBe(capabilityId);
        stored.GrantedBy.CapabilityName.ShouldBe("gym-stat-deploy");
        stored.GrantedBy.TaskId.ShouldBeNull();
    }

    [Test]
    public async Task Parent_task_create_stores_task_grantor()
    {
        using var workspace = new TempWorkspace();
        var parent = await SeedTaskAsync(workspace.Path, kind: AgentTaskKind.Orchestrator);
        await using var db = CreateContext();
        var sessionId = Guid.NewGuid();
        var created = await CreateService(db).CreateAsync(
            NewRequest("child grant", AgentTaskRole.Code) with
            {
                InternalDecisionPolicy = InternalDecisionFixtures.Sample(),
            },
            new AgentTaskService.Caller(parent, sessionId, workspace.Path),
            CancellationToken.None);

        var stored = InternalDecisionPolicy.ReadStored(
            (await db.AgentTasks.SingleAsync(t => t.Id == created.Id)).InternalDecisionPolicyJson!);
        stored.GrantedBy.Kind.ShouldBe("task");
        stored.GrantedBy.TaskId.ShouldBe(parent.Id);
        stored.GrantedBy.SessionId.ShouldBe(sessionId);
    }

    private static CreateAgentTaskRequest NewRequest(string goal, AgentTaskRole role = AgentTaskRole.Custom) =>
        new(Goal: goal, Kind: AgentTaskKind.Worker, Role: role);

    private static AgentTaskService.Caller ManualCaller(string directory) => new(null, null, directory);

    private static AgentTaskService CreateService(AppDbContext db) =>
        new(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { AllowedRoots = [] }),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static async Task<AgentTask> SeedTaskAsync(
        string workingDirectory,
        AgentTaskKind kind = AgentTaskKind.Worker)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = $"Seeded {kind}",
            Goal = "Seeded goal.",
            Kind = kind,
            Role = AgentTaskRole.Custom,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            Status = AgentTaskStatus.Succeeded,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private sealed class ScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly ServiceProvider _provider;

        public ScopeFactory()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IEventBus, MockEventBus>();
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings()));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IDelegateSessionStopper>(
                new RecordingSessionStopper());
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddScoped<AgentTaskService>();
            _provider = services.BuildServiceProvider();
        }

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => _provider;
        public object? GetService(Type serviceType) => _provider.GetService(serviceType);
        public void Dispose() { }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c407-s1").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
