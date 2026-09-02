using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0256: KillAsync persists the request source before asking the runner to kill, so an
/// exit-event race cannot record ProcessExit and erase operator/system intent.
/// </summary>
[Category("Integration")]
public class SessionTerminationSourcePersistenceTests
{
    [Test]
    public void Record_never_overwrites_a_prior_source()
    {
        var session = new AgentSession { TerminationSource = SessionTerminationSource.OperatorRequest };
        SessionTermination.Record(session, SessionTerminationSource.SystemRequest).ShouldBeFalse();
        session.TerminationSource.ShouldBe(SessionTerminationSource.OperatorRequest);
    }

    [Test]
    public async Task KillAsync_persists_OperatorRequest_before_the_runner_kill()
    {
        var sessionId = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();

        try
        {
            var service = BuildService(db, sessionId);
            await service.KillAsync(sessionId, SessionTerminationSource.OperatorRequest, CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.TerminationSource.ShouldBe(SessionTerminationSource.OperatorRequest);
            session.Status.ShouldBe(SessionStatus.Stopped);
        }
        finally
        {
            await using var cleanup = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await cleanup.RunAttempts.Where(a => a.AgentSessionId == sessionId).ExecuteDeleteAsync();
            await cleanup.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task KillAsync_without_an_explicit_source_records_SystemRequest()
    {
        var sessionId = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();

        try
        {
            var service = BuildService(db, sessionId);
            await service.KillAsync(sessionId, CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        }
        finally
        {
            await using var cleanup = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await cleanup.RunAttempts.Where(a => a.AgentSessionId == sessionId).ExecuteDeleteAsync();
            await cleanup.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
        }
    }

    private static AgentSessionService BuildService(AppDbContext db, Guid sessionId)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        var provider = services.BuildServiceProvider();
        var eventBus = new MockEventBus();
        var sessionSettings = Options.Create(new AgentSessionSettings
        {
            SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-term-src-{Guid.NewGuid():N}"),
        });
        var runtime = new AgentSessionRuntime(
            eventBus,
            sessionSettings,
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
        var adapter = new FakeAgentProtocolAdapter();
        runtime.Register(sessionId, adapter);
        var worktreeManager = new WorktreeManager(
            Options.Create(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-term-src-wt"),
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24,
            }),
            TimeProvider.System,
            NullLogger<WorktreeManager>.Instance);
        var hookService = new WorkspaceHookService(
            new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance),
            NullLogger<WorkspaceHookService>.Instance);
        var messageQueue = new SessionMessageQueueService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            runtime,
            eventBus,
            TimeProvider.System,
            NullLogger<SessionMessageQueueService>.Instance);

        return new AgentSessionService(
            db,
            worktreeManager,
            hookService,
            new UnusedAdapterFactory(),
            runtime,
            eventBus,
            messageQueue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            sessionSettings,
            Options.Create(new SupervisionSettings()),
            TimeProvider.System,
            NullLogger<AgentSessionService>.Instance);
    }

    private sealed class UnusedAdapterFactory : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) =>
            throw new NotSupportedException("KillAsync does not create an adapter.");
    }
}
