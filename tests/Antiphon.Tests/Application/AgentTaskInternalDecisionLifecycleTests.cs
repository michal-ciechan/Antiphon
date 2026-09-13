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
