using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class SpecialistExecutionIdentityTests
{
    [Test]
    public async Task Card0415_V01_legacy_Codex_producer_reaches_the_live_standing_session()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(), AgentKind = AgentKind.Codex, DefinitionName = "codex",
            Status = SessionStatus.Running, Cwd = Path.GetTempPath(),
            CreatedAt = now, StartedAt = now, LastSeenAt = now,
        };
        var seat = new Agent
        {
            Id = Guid.NewGuid(), Name = "identity-canary", Slug = "identity-canary",
            Kind = AgentKind.Codex, ModelLevel = AgentModelLevel.Low,
            WorkingDirectory = session.Cwd, PersistentSessionId = session.Id.ToString("D"),
            AlwaysOn = true, Status = AgentStatus.Running, CreatedAt = now, UpdatedAt = now,
        };
        db.AgentSessions.Add(session);
        db.Agents.Add(seat);
        await db.SaveChangesAsync();
        var runner = new SpecialistTaskRunner(db, TimeProvider.System, NullLogger.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var spec = CheckInterpreterProvisioner.Spec(new DelegationSettings());
        var pending = runner.RunAsync(spec, "identity reproduction", "synthetic facts",
            TimeSpan.FromSeconds(60), 3, _ => Task.FromResult<Agent?>(seat), stop.Token);
        try
        {
            AgentTask? task = null;
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var read = new AppDbContext(options);
                task = await read.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.AgentId == seat.Id);
                return task is not null;
            });
            var (dispatcher, provider) = AgentTaskStandingAgentDispatchTests.CreateHarness(connectionString: database.ConnectionString);
            using (provider) await dispatcher.TickAsync(stop.Token);
            await using var verify = new AppDbContext(options);
            var result = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task!.Id);
            result.Status.ShouldBe(AgentTaskStatus.Dispatched, result.FailureReason);
            result.AgentKind.ShouldBe(AgentKind.Codex);
            result.AgentSessionId.ShouldBe(session.Id);
            (await verify.SessionQueuedMessages.Where(m => m.AgentSessionId == session.Id).ToListAsync())
                .ShouldContain(m => m.Body.Contains(DelegationReportFormatter.TaskMarker(result.Id)));
            (await verify.AgentSessions.CountAsync()).ShouldBe(1, "standing dispatch must not launch another session");
        }
        finally
        {
            stop.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }
}
