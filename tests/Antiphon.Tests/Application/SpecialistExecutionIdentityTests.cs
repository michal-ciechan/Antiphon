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
    [Arguments(AgentKind.ClaudeCode, false, "none")]
    [Arguments(AgentKind.Codex, true, "none")]
    [Arguments(AgentKind.Codex, false, "model")]
    [Arguments(AgentKind.Codex, true, "tier")]
    [Arguments(AgentKind.Codex, false, "started")]
    [Arguments(AgentKind.Codex, true, "session")]
    [Arguments(AgentKind.Codex, false, "kind")]
    [Arguments(AgentKind.Codex, true, "live-model")]
    public async Task Card0415_V01_execution_snapshot_and_dispatch_drift(
        AgentKind kind, bool deadlinePolicy, string drift)
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        var now = DateTime.UtcNow;
        var exact = kind == AgentKind.Codex ? "gpt-5.6-terra" : "sonnet";
        var session = new AgentSession
        {
            Id = Guid.NewGuid(), AgentKind = kind, DefinitionName = kind == AgentKind.Codex ? "codex" : "claude",
            Status = SessionStatus.Running, Cwd = Path.GetTempPath(), EffectiveModelId = exact,
            CreatedAt = now, StartedAt = now, LastSeenAt = now,
        };
        var seat = new Agent
        {
            Id = Guid.NewGuid(), Name = "snapshot-seat", Slug = "snapshot-seat", Kind = kind,
            ModelLevel = AgentModelLevel.High, ModelId = exact, WorkingDirectory = session.Cwd,
            PersistentSessionId = session.Id.ToString("D"), AlwaysOn = true,
            Status = AgentStatus.Running, CreatedAt = now, UpdatedAt = now,
        };
        db.AgentSessions.Add(session);
        db.Agents.Add(seat);
        await db.SaveChangesAsync();
        // Use the database's timestamp precision as the selection evidence.
        await db.Entry(session).ReloadAsync();
        var availability = new RecordingAvailability();
        var runner = new SpecialistTaskRunner(db, TimeProvider.System, NullLogger.Instance,
            modelAvailability: availability);
        var spec = CheckInterpreterProvisioner.Spec(new DelegationSettings()) with { Slug = seat.Slug };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pending = deadlinePolicy
            ? runner.RunWithPolicyAsync(spec, "snapshot", "synthetic facts", 3,
                _ => Task.FromResult<Agent?>(seat), new(DateTimeOffset.UtcNow.AddSeconds(60)), stop.Token)
            : runner.RunAsync(spec, "snapshot", "synthetic facts", TimeSpan.FromSeconds(60), 3,
                _ => Task.FromResult<Agent?>(seat), stop.Token);
        try
        {
            AgentTask? task = null;
            await SpecialistTaskRunnerDeadlineTests.UntilAsync(async () =>
            {
                await using var read = new AppDbContext(options);
                task = await read.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.AgentId == seat.Id);
                return task is not null;
            });
            task!.AgentKind.ShouldBe(kind);
            task.ModelLevel.ShouldBe(AgentModelLevel.High);
            task.SpecialistModelAlias.ShouldBe(exact);
            task.SpecialistModelId.ShouldBe(exact);
            task.SpecialistSessionId.ShouldBe(session.Id);
            task.SpecialistSessionStartedAt.ShouldBe(session.StartedAt);
            await using (var edit = new AppDbContext(options))
            {
                (await edit.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == task.Id)).ModelLevel
                    .ShouldBe(AgentModelLevel.High);
                if (drift == "model") await edit.Agents.Where(a => a.Id == seat.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ModelId, "gpt-5.6-sol"));
                if (drift == "tier") await edit.Agents.Where(a => a.Id == seat.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ModelLevel, AgentModelLevel.Low));
                if (drift == "kind") await edit.AgentSessions.Where(s => s.Id == session.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.AgentKind, AgentKind.ClaudeCode));
                if (drift == "started") await edit.AgentSessions.Where(s => s.Id == session.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.StartedAt, session.StartedAt.AddSeconds(1)));
                if (drift == "live-model") await edit.AgentSessions.Where(s => s.Id == session.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.EffectiveModelId, "gpt-5.6-sol"));
                if (drift == "session")
                {
                    var replacement = new AgentSession
                    {
                        Id = Guid.NewGuid(), AgentKind = kind, DefinitionName = "codex", EffectiveModelId = exact,
                        Status = SessionStatus.Running, Cwd = session.Cwd, StartedAt = session.StartedAt,
                        CreatedAt = now, LastSeenAt = now,
                    };
                    edit.AgentSessions.Add(replacement);
                    await edit.SaveChangesAsync();
                    await edit.Agents.Where(a => a.Id == seat.Id).ExecuteUpdateAsync(s =>
                        s.SetProperty(a => a.PersistentSessionId, replacement.Id.ToString("D")));
                }
            }
            if (deadlinePolicy)
                availability.Calls.ShouldContain(c => c == (kind, exact));
            var (dispatcher, provider) = AgentTaskStandingAgentDispatchTests.CreateHarness(connectionString: database.ConnectionString);
            using (provider) await dispatcher.TickAsync(stop.Token);
            await using var verify = new AppDbContext(options);
            var result = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
            result.AgentKind.ShouldBe(kind);
            result.ModelLevel.ShouldBe(AgentModelLevel.High);
            if (drift == "none")
            {
                result.Status.ShouldBe(AgentTaskStatus.Dispatched, result.FailureReason);
                result.AgentSessionId.ShouldBe(session.Id);
            }
            else
            {
                result.Status.ShouldBe(AgentTaskStatus.Failed);
                result.FailureCode.ShouldBe(AgentTaskFailureCode.SpecialistIdentityMismatch);
                (await verify.SessionQueuedMessages.CountAsync()).ShouldBe(0);
                var failure = await pending;
                failure.Outcome.ShouldBe(SpecialistRunOutcome.IdentityMismatch);
                failure.Reason.ShouldNotBeNullOrWhiteSpace();
                failure.Reason.ShouldContain("snapshot-seat", Case.Insensitive);
            }
        }
        finally
        {
            stop.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    private sealed class RecordingAvailability : IModelAvailability
    {
        public List<(AgentKind Kind, string Alias)> Calls { get; } = [];
        public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct)
        {
            Calls.Add((kind, alias));
            return Task.FromResult(false);
        }
    }

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
