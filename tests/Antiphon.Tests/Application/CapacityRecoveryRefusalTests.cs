using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
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
[NotInParallel]
public class CapacityRecoveryRefusalTests
{
    [Test]
    public async Task Card0412_V21_standing_caller_records_wait_before_409()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var sessionId = Guid.NewGuid();
        var holdId = Guid.NewGuid();
        await using (var db = Ctx(schema))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.Codex,
                Status = SessionStatus.Running,
                Cwd = workspace.Path,
                Cols = 120,
                Rows = 30,
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
            });
            db.ModelAvailabilityHolds.Add(CapacityRecoveryTestSupport.Hold(holdId, "opus"));
            await db.SaveChangesAsync();
        }

        var ex = await Should.ThrowAsync<ModelDisabledException>(() =>
            CreateService(schema).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan the work",
                    Role: AgentTaskRole.Plan,
                    AgentKind: AgentKind.ClaudeCode,
                    ModelLevel: AgentModelLevel.High),
                new AgentTaskService.Caller(null, sessionId, workspace.Path),
                CancellationToken.None));
        ex.Code.ShouldBe(ModelDisabledException.ErrorCode);
        ex.StatusCode.ShouldBe(409);

        await using var verify = Ctx(schema);
        var wait = await verify.CapacityRecoveryWaits.SingleAsync(w => w.SessionId == sessionId);
        wait.ConsumerKind.ShouldBe(CapacityWaitConsumerKind.CreateRefusal);
        wait.ExecutionKind.ShouldBe(AgentKind.Codex);
        wait.RequestedKind.ShouldBe(AgentKind.ClaudeCode);
        wait.RefusalDigest.ShouldNotBeNull();
        (await verify.CapacityRecoveryWaits.CountAsync(w => w.SessionId == sessionId)).ShouldBe(1);

        await Should.ThrowAsync<ModelDisabledException>(() =>
            CreateService(schema).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan the work",
                    Role: AgentTaskRole.Plan,
                    AgentKind: AgentKind.ClaudeCode,
                    ModelLevel: AgentModelLevel.High),
                new AgentTaskService.Caller(null, sessionId, workspace.Path),
                CancellationToken.None));
        await using var again = Ctx(schema);
        (await again.CapacityRecoveryWaits.CountAsync(w => w.SessionId == sessionId)).ShouldBe(1);
    }

    [Test]
    public async Task Card0412_V21_capability_caller_has_no_invented_recipient()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var holdId = Guid.NewGuid();
        await using (var db = Ctx(schema))
        {
            db.ModelAvailabilityHolds.Add(CapacityRecoveryTestSupport.Hold(holdId, "opus"));
            await db.SaveChangesAsync();
        }

        await Should.ThrowAsync<ModelDisabledException>(() =>
            CreateService(schema).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan the work",
                    Role: AgentTaskRole.Plan,
                    AgentKind: AgentKind.ClaudeCode,
                    ModelLevel: AgentModelLevel.High),
                new AgentTaskService.Caller(null, null, workspace.Path, CapabilityId: Guid.NewGuid()),
                CancellationToken.None));
        await using var verify = Ctx(schema);
        (await verify.CapacityRecoveryWaits.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Card0412_V21_capability_caller_with_session_still_has_no_recipient()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var sessionId = Guid.NewGuid();
        var holdId = Guid.NewGuid();
        await using (var db = Ctx(schema))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.Codex,
                Status = SessionStatus.Running,
                Cwd = workspace.Path,
                Cols = 120,
                Rows = 30,
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
            });
            db.ModelAvailabilityHolds.Add(CapacityRecoveryTestSupport.Hold(holdId, "opus"));
            await db.SaveChangesAsync();
        }

        await Should.ThrowAsync<ModelDisabledException>(() =>
            CreateService(schema).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan the work",
                    Role: AgentTaskRole.Plan,
                    AgentKind: AgentKind.ClaudeCode,
                    ModelLevel: AgentModelLevel.High),
                new AgentTaskService.Caller(null, sessionId, workspace.Path, CapabilityId: Guid.NewGuid()),
                CancellationToken.None));
        await using var verify = Ctx(schema);
        (await verify.CapacityRecoveryWaits.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Card0412_V21_unresolved_session_is_not_a_recipient()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var holdId = Guid.NewGuid();
        await using (var db = Ctx(schema))
        {
            db.ModelAvailabilityHolds.Add(CapacityRecoveryTestSupport.Hold(holdId, "opus"));
            await db.SaveChangesAsync();
        }

        await Should.ThrowAsync<ModelDisabledException>(() =>
            CreateService(schema).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan the work",
                    Role: AgentTaskRole.Plan,
                    AgentKind: AgentKind.ClaudeCode,
                    ModelLevel: AgentModelLevel.High),
                new AgentTaskService.Caller(null, Guid.NewGuid(), workspace.Path),
                CancellationToken.None));
        await using var verify = Ctx(schema);
        (await verify.CapacityRecoveryWaits.CountAsync()).ShouldBe(0);
    }

    private static AgentTaskService CreateService(IsolatedTestSchema schema)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 512,
        }));
        services.AddSingleton<CapacityRecoveryService>();
        services.AddScoped<ModelAvailability>();
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c412-refusal-wt"),
        });
        services.AddScoped<AgentTaskService>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<AgentTaskService>();
    }

    private static AppDbContext Ctx(IsolatedTestSchema schema) =>
        CapacityRecoveryTestSupport.CreateContext(schema);

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c412-refusal").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
