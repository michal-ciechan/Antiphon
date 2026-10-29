using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1603 standing witnesses. Human replacement goes through AgentControlService.StartAsync.
/// Capacity starts go through the supervisor. The verdict is ReconcileAsync.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel]
public class CapacityObsoleteStandingTests
{
    [Test]
    [Arguments("fresh-null-generation")]
    [Arguments("resume-null-generation")]
    [Arguments("resume-stamped-generation")]
    [Arguments("fresh-consumer-key-fallback")]
    public async Task C1603_human_start_supersedes_standing_wait(string arm)
    {
        await using var fx = await StandingFixture.OpenAsync();
        var resume = arm.StartsWith("resume", StringComparison.Ordinal);
        var stamped = arm == "resume-stamped-generation";
        var agent = await fx.CreateAgentAsync();
        Guid? previous = null;
        DateTime? previousStarted = null;
        if (resume)
            (previous, previousStarted) = await fx.SeedFailedSessionAsync(agent);
        var wait = await fx.EnsureStandingWaitAsync(agent, includeAgentId: arm != "fresh-consumer-key-fallback",
            sessionId: stamped ? previous : null, sessionStartedAt: stamped ? previousStarted : null,
            admissionCount: stamped ? 1 : 0);
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        var granted = await fx.ReloadWaitAsync(wait.Id);
        granted.State.ShouldBe(CapacityRecoveryWaitState.ActionPending);
        if (stamped && previous is Guid oldSession)
        {
            await using var db = fx.Ctx();
            await db.AgentSessions.Where(s => s.Id == oldSession).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CapacityRecoveryActionKey, granted.ActionKey));
        }

        var competitor = await fx.SeedCompetitorAsync(fx.Now.AddMinutes(-10));
        var detail = await fx.Harness.Control.StartAsync(
            agent.Id,
            new StartAgentRequest(Fresh: !resume, IgnoreSubscriptionQuota: true),
            CancellationToken.None);
        detail.LiveSession.ShouldNotBeNull();
        await using (var db = fx.Ctx())
        {
            var owner = await db.Agents.AsNoTracking().SingleAsync(a => a.Id == agent.Id);
            owner.PersistentSessionId.ShouldBe(detail.LiveSession!.Id.ToString("D"));
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == detail.LiveSession.Id);
            session.StartedAt.ShouldBeGreaterThan(granted.BlockedAt);
            if (stamped)
                session.StartedAt.ShouldNotBe(previousStarted!.Value);
            (await db.AgentSessions.CountAsync(s => s.StandingAgentId == agent.Id || s.Cwd == owner.WorkingDirectory))
                .ShouldBeGreaterThan(0);
        }

        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        var replaced = await fx.ReloadWaitAsync(wait.Id);
        replaced.State.ShouldBe(CapacityRecoveryWaitState.Superseded);
        replaced.OutcomeReason.ShouldBe("standing-consumer-replaced");
        replaced.AdmissionCount.ShouldBe(granted.AdmissionCount);
        replaced.NeedsRevalidationGrant.ShouldBeFalse();
        for (var window = 0; window < 3; window++)
        {
            fx.Advance(TimeSpan.FromSeconds(121));
            await fx.Recovery.ReconcileAsync(CancellationToken.None);
            var stable = await fx.ReloadWaitAsync(wait.Id);
            stable.State.ShouldBe(CapacityRecoveryWaitState.Superseded);
            stable.Version.ShouldBe(replaced.Version);
            await using var db = fx.Ctx();
            var grant = await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                .SingleOrDefaultAsync(s => s.Kind == AgentKind.ClaudeCode);
            if (grant?.GrantedWaitId is Guid holder)
                holder.ShouldNotBe(wait.Id);
        }

        var due = await fx.ReloadWaitAsync(competitor.Id);
        if (due.DueAt is { } at && at > fx.Now)
            fx.Advance(at - fx.Now + TimeSpan.FromSeconds(1));
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        await using (var db = fx.Ctx())
        {
            (await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
                .SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBe(competitor.Id);
        }
    }

    [Test]
    [Arguments("same-running-generation")]
    [Arguments("original-stopped-generation")]
    [Arguments("capacity-fresh")]
    [Arguments("capacity-resume")]
    [Arguments("legacy-episode-key")]
    [Arguments("missing-pointer")]
    [Arguments("conflicting-owner")]
    public async Task C1603_standing_cleanup_keeps_current_or_unproven_consumer(string arm)
    {
        await using var fx = await StandingFixture.OpenAsync();
        if (arm is "capacity-fresh" or "capacity-resume")
        {
            await fx.AssertCapacityStartIsKeptAsync(arm == "capacity-resume");
            return;
        }

        var agent = await fx.CreateAgentAsync();
        var (sessionId, started) = await fx.SeedFailedSessionAsync(agent,
            status: arm == "same-running-generation" ? SessionStatus.Running : SessionStatus.Failed);
        var wait = await fx.EnsureStandingWaitAsync(agent, includeAgentId: true, sessionId: sessionId,
            sessionStartedAt: arm == "legacy-episode-key" ? null : started);
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        var before = await fx.ReloadWaitAsync(wait.Id);
        await using (var db = fx.Ctx())
        {
            if (arm == "legacy-episode-key")
            {
                var older = CapacityRecoveryPolicy.ActionKey(wait.Id, 0);
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.CapacityRecoveryActionKey, older)
                    .SetProperty(x => x.StartedAt, fx.Now));
                await db.CapacityRecoveryWaits.Where(w => w.Id == wait.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.SessionId, (Guid?)null)
                    .SetProperty(w => w.SessionStartedAt, (DateTime?)null)
                    .SetProperty(w => w.BlockedAt, fx.Now.AddMinutes(-5)));
            }
            else if (arm == "missing-pointer")
            {
                await db.Agents.Where(a => a.Id == agent.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.PersistentSessionId, (string?)null));
            }
            else if (arm == "conflicting-owner")
            {
                var other = await fx.CreateAgentAsync();
                await db.Agents.Where(a => a.Id == other.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.PersistentSessionId, sessionId.ToString("D")));
            }
        }

        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        var after = await fx.ReloadWaitAsync(wait.Id);
        after.State.ShouldBe(before.State);
        after.ActionKey.ShouldBe(before.ActionKey);
        after.AdmissionCount.ShouldBe(before.AdmissionCount);
        after.Version.ShouldBe(arm == "legacy-episode-key" ? after.Version : before.Version);
        await using var verify = fx.Ctx();
        (await verify.Set<CapacityRecoveryProviderState>().AsNoTracking()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBe(wait.Id);
    }

    [Test]
    [Arguments("capacity-commit")]
    [Arguments("human-rollback")]
    [Arguments("session-busy")]
    [Arguments("agent-busy")]
    public async Task C1603_standing_cleanup_serializes_with_acceptance(string arm)
    {
        var boundary = new ProbeBoundary();
        var commands = new CommandLog();
        await using var fx = await StandingFixture.OpenAsync(boundary, commands);
        var agent = await fx.CreateAgentAsync();
        var (oldSession, oldStarted) = await fx.SeedFailedSessionAsync(agent);
        var wait = await fx.EnsureStandingWaitAsync(agent, includeAgentId: true,
            sessionId: oldSession, sessionStartedAt: oldStarted);
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        var before = await fx.ReloadWaitAsync(wait.Id);

        if (arm is "session-busy" or "agent-busy")
        {
            await fx.AssertBusyDefersAsync(arm, agent.Id, oldSession, wait.Id, commands);
            var detail = await fx.Harness.Control.StartAsync(agent.Id,
                new StartAgentRequest(Fresh: true, IgnoreSubscriptionQuota: true), CancellationToken.None);
            detail.LiveSession.ShouldNotBeNull();
            commands.Texts.Clear();
            await fx.Recovery.ReconcileAsync(CancellationToken.None);
            var order = string.Join("\n", commands.Texts);
            var sessionLock = order.IndexOf("AgentSessions", StringComparison.Ordinal);
            var agentLock = order.IndexOf("FROM \"Agents\"", StringComparison.Ordinal);
            var provider = order.IndexOf("pg_try_advisory_xact_lock", StringComparison.Ordinal);
            sessionLock.ShouldBeGreaterThanOrEqualTo(0);
            agentLock.ShouldBeGreaterThan(sessionLock);
            provider.ShouldBeGreaterThan(agentLock);
            (await fx.ReloadWaitAsync(wait.Id)).State.ShouldBe(CapacityRecoveryWaitState.Superseded);
            return;
        }

        boundary.OnReached = async (name, waitId, _, ct) =>
        {
            if (name != "capacity-obsolete-after-discovery" || waitId != wait.Id)
                return;
            if (arm == "capacity-commit")
            {
                await using var db = fx.Ctx();
                var session = await db.AgentSessions.SingleAsync(s => s.Id == oldSession, ct);
                session.StartedAt = oldStarted;
                var row = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id, ct);
                row.SessionId = oldSession;
                row.SessionStartedAt = oldStarted;
                await db.SaveChangesAsync(ct);
                return;
            }

            await using var rolling = fx.Ctx();
            await using var tx = await rolling.Database.BeginTransactionAsync(ct);
            var owner = await rolling.Agents.SingleAsync(a => a.Id == agent.Id, ct);
            owner.PersistentSessionId = Guid.NewGuid().ToString("D");
            await rolling.SaveChangesAsync(ct);
            await tx.RollbackAsync(ct);
        };

        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        var after = await fx.ReloadWaitAsync(wait.Id);
        after.State.ShouldBe(before.State);
        after.SessionId.ShouldBe(oldSession);
        await using var grant = fx.Ctx();
        (await grant.Set<CapacityRecoveryProviderState>().AsNoTracking()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBe(wait.Id);
    }

    [Test]
    [Arguments("free")]
    [Arguments("provider-busy")]
    public async Task C1603_standing_cleanup_is_bounded_and_provider_busy_safe(string arm)
    {
        await using var fx = await StandingFixture.OpenAsync(batch: 2);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var agent = await fx.CreateAgentAsync();
            await fx.SeedFailedSessionAsync(agent, startedAt: fx.Now.AddMinutes(-30));
            var wait = await fx.EnsureStandingWaitAsync(agent, includeAgentId: true,
                blockedAt: fx.Now.AddHours(-2).AddMinutes(i));
            var detail = await fx.Harness.Control.StartAsync(agent.Id,
                new StartAgentRequest(Fresh: true, IgnoreSubscriptionQuota: true), CancellationToken.None);
            detail.LiveSession.ShouldNotBeNull();
            ids.Add(wait.Id);
        }

        if (arm == "provider-busy")
        {
            await using var connection = new NpgsqlConnection(fx.Schema.ConnectionString);
            await connection.OpenAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext(@key))", connection, tx);
            command.Parameters.AddWithValue("key", "antiphon.capacity.kind.ClaudeCode");
            await command.ExecuteScalarAsync();
            try
            {
                await fx.Recovery.ReconcileAsync(CancellationToken.None);
                foreach (var id in ids)
                    (await fx.ReloadWaitAsync(id)).State.ShouldNotBe(CapacityRecoveryWaitState.Superseded);
            }
            finally
            {
                await tx.RollbackAsync();
            }
        }

        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        (await fx.CountSupersededAsync(ids)).ShouldBe(arm == "provider-busy" ? 2 : 2);
        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        (await fx.CountSupersededAsync(ids)).ShouldBe(3);
        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        (await fx.CountSupersededAsync(ids)).ShouldBe(3);
        foreach (var id in ids)
            (await fx.ReloadWaitAsync(id)).AdmissionCount.ShouldBe(0);
    }

    [Test]
    public async Task C1603_obsolete_cleanup_loses_compare_and_set_safely()
    {
        var boundary = new ProbeBoundary();
        await using var fx = await StandingFixture.OpenAsync(boundary);
        var agent = await fx.CreateAgentAsync();
        await fx.SeedFailedSessionAsync(agent, startedAt: fx.Now.AddMinutes(-20));
        var wait = await fx.EnsureStandingWaitAsync(agent, includeAgentId: true,
            blockedAt: fx.Now.AddHours(-1));
        await fx.Recovery.GrantReadyAsync(CancellationToken.None);
        var before = await fx.ReloadWaitAsync(wait.Id);
        var detail = await fx.Harness.Control.StartAsync(agent.Id,
            new StartAgentRequest(Fresh: true, IgnoreSubscriptionQuota: true), CancellationToken.None);
        detail.LiveSession.ShouldNotBeNull();
        boundary.OnReached = async (name, waitId, _, ct) =>
        {
            if (name != "capacity-obsolete-before-finish" || waitId != wait.Id)
                return;
            await using var db = fx.Ctx();
            await db.CapacityRecoveryWaits.Where(w => w.Id == wait.Id && w.Version == before.Version)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.State, CapacityRecoveryWaitState.Progressed)
                    .SetProperty(w => w.Version, before.Version + 1)
                    .SetProperty(w => w.OutcomeReason, "observed"), ct);
        };

        await fx.Recovery.ReconcileAsync(CancellationToken.None);
        var after = await fx.ReloadWaitAsync(wait.Id);
        after.State.ShouldBe(CapacityRecoveryWaitState.Progressed);
        after.Version.ShouldBe(before.Version + 1);
        after.OutcomeReason.ShouldNotBe("standing-consumer-replaced");
        await using var db = fx.Ctx();
        (await db.Set<CapacityRecoveryProviderState>().AsNoTracking()
            .SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBeNull();
    }

    private sealed class StandingFixture : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required string TempRoot { get; init; }
        public required AgentSupervisionTests.Harness Harness { get; init; }
        public CapacityRecoveryService Recovery => Harness.Provider.GetRequiredService<CapacityRecoveryService>();
        public DateTime Now => Harness.Clock.GetUtcNow().UtcDateTime;

        public static async Task<StandingFixture> OpenAsync(
            ProbeBoundary? boundary = null, CommandLog? commands = null, int batch = 100)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var tempRoot = AgentSupervisionTests.NewTempRoot();
            var adapters = Enumerable.Range(0, 8).Select(_ => new FakeAgentProtocolAdapter()).ToArray();
            var harness = AgentSupervisionTests.BuildHarness(
                tempRoot, adapters, definitionKind: "ClaudeCode",
                connectionString: schema.ConnectionString,
                configureDb: commands is null ? null : options => options.AddInterceptors(commands),
                supervision: new SupervisionSettings
                {
                    TickSeconds = 1,
                    BackoffBaseSeconds = 5,
                    HealthyUptimeResetMinutes = 10,
                    FreshAfterResumeFailures = 2,
                    CapacityRecovery = new CapacityRecoverySettings
                    {
                        Enabled = true,
                        AdmissionIntervalSeconds = 60,
                        JitterSeconds = 0,
                        MaxEpisodeAttempts = 3,
                        ReconciliationBatchSize = batch,
                    },
                },
                configure: boundary is null ? null : services => services.AddSingleton<LandDeliveryBoundary>(boundary));
            return new StandingFixture { Schema = schema, TempRoot = tempRoot, Harness = harness };
        }

        public AppDbContext Ctx() => CapacityRecoveryTestSupport.CreateContext(Schema);

        public void Advance(TimeSpan by) => Harness.Clock.Advance(by);

        public async Task<AgentDetailDto> CreateAgentAsync()
        {
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(Harness, TempRoot);
            await using var db = Ctx();
            await db.Agents.Where(a => a.Id == agent.Id).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Kind, AgentKind.ClaudeCode));
            return agent;
        }

        public async Task<(Guid SessionId, DateTime StartedAt)> SeedFailedSessionAsync(
            AgentDetailDto agent, SessionStatus status = SessionStatus.Failed, DateTime? startedAt = null)
        {
            var id = Guid.NewGuid();
            var started = startedAt ?? Now.AddMinutes(-15);
            await using var db = Ctx();
            var owner = await db.Agents.SingleAsync(a => a.Id == agent.Id);
            owner.PersistentSessionId = id.ToString("D");
            owner.Kind = AgentKind.ClaudeCode;
            db.AgentSessions.Add(new AgentSession
            {
                Id = id, AgentKind = AgentKind.ClaudeCode, DefinitionName = "fake",
                Cwd = owner.WorkingDirectory, Status = status, CreatedAt = started, StartedAt = started,
                StandingAgentId = agent.Id,
            });
            await db.SaveChangesAsync();
            return (id, started);
        }

        public async Task<CapacityRecoveryWait> EnsureStandingWaitAsync(
            AgentDetailDto agent, bool includeAgentId, Guid? sessionId = null, DateTime? sessionStartedAt = null,
            int admissionCount = 0, DateTime? blockedAt = null)
        {
            var wait = await Recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"agent:{agent.Id:N}", CapacityWaitConsumerKind.StandingStart,
                holdAlreadyCleared: true, agentId: includeAgentId ? agent.Id : null,
                sessionId: sessionId, blockedAt: blockedAt ?? Now.AddMinutes(-8)), CancellationToken.None);
            await using var db = Ctx();
            var row = await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == wait.Id);
            if (!includeAgentId)
                row.AgentId = null;
            row.SessionStartedAt = sessionStartedAt;
            if (sessionId is null)
                row.SessionId = null;
            row.AdmissionCount = admissionCount;
            await db.SaveChangesAsync();
            return await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == wait.Id);
        }

        public async Task<CapacityRecoveryWait> ReloadWaitAsync(Guid id)
        {
            await using var db = Ctx();
            return await db.CapacityRecoveryWaits.AsNoTracking().SingleAsync(w => w.Id == id);
        }

        public async Task<CapacityRecoveryWait> SeedCompetitorAsync(DateTime blockedAt)
        {
            var taskId = Guid.NewGuid();
            await using var db = Ctx();
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "standing competitor", Goal = "grant",
                AgentKind = AgentKind.ClaudeCode, Status = AgentTaskStatus.Queued,
                CreatedAt = Now, ConcurrencyToken = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
            var wait = await Recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"task:{taskId:N}", CapacityWaitConsumerKind.QueuedTask,
                holdAlreadyCleared: true, taskId: taskId, blockedAt: blockedAt), CancellationToken.None);
            await using var due = Ctx();
            await due.CapacityRecoveryWaits.Where(w => w.Id == wait.Id).ExecuteUpdateAsync(s => s
                .SetProperty(w => w.DueAt, Now.AddMinutes(30)));
            return wait;
        }

        public async Task<int> CountSupersededAsync(IReadOnlyList<Guid> ids)
        {
            await using var db = Ctx();
            return await db.CapacityRecoveryWaits.CountAsync(w => ids.Contains(w.Id)
                && w.State == CapacityRecoveryWaitState.Superseded);
        }

        public async Task AssertCapacityStartIsKeptAsync(bool resume)
        {
            var agent = await CreateAgentAsync();
            Guid? sessionId = null;
            if (resume)
                (sessionId, _) = await SeedFailedSessionAsync(agent);
            var wait = await EnsureStandingWaitAsync(agent, includeAgentId: true, sessionId: sessionId,
                sessionStartedAt: null);
            await Recovery.GrantReadyAsync(CancellationToken.None);
            Harness.Clock.Advance(TimeSpan.FromSeconds(10));
            await Harness.Supervisor().TickAsync(CancellationToken.None);
            await Harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            await Recovery.ReconcileAsync(CancellationToken.None);
            var after = await ReloadWaitAsync(wait.Id);
            after.State.ShouldNotBe(CapacityRecoveryWaitState.Superseded);
            after.AdmissionCount.ShouldBeGreaterThan(0);
        }

        public async Task AssertBusyDefersAsync(
            string arm, Guid agentId, Guid sessionId, Guid waitId, CommandLog commands)
        {
            await using var connection = new NpgsqlConnection(Schema.ConnectionString);
            await connection.OpenAsync();
            await using var tx = await connection.BeginTransactionAsync();
            var table = arm == "session-busy" ? "AgentSessions" : "Agents";
            var id = arm == "session-busy" ? sessionId : agentId;
            await using var command = new NpgsqlCommand(
                $"SELECT \"Id\" FROM \"{table}\" WHERE \"Id\" = @id FOR UPDATE", connection, tx);
            command.Parameters.AddWithValue("id", id);
            (await command.ExecuteScalarAsync()).ShouldBe(id);
            commands.Texts.Clear();
            try
            {
                await Recovery.ReconcileAsync(CancellationToken.None);
                (await ReloadWaitAsync(waitId)).State.ShouldNotBe(CapacityRecoveryWaitState.Superseded);
                string.Join("\n", commands.Texts).ShouldNotContain("pg_try_advisory_xact_lock");
            }
            finally
            {
                await tx.RollbackAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            await Schema.DisposeAsync();
            try
            {
                if (Directory.Exists(TempRoot))
                    Directory.Delete(TempRoot, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }

    private sealed class ProbeBoundary : LandDeliveryBoundary
    {
        public Func<string, Guid, Guid, CancellationToken, Task>? OnReached { get; set; }

        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct) =>
            OnReached is null ? Task.CompletedTask : OnReached(boundary, taskId, identity, ct);
    }

    private sealed class CommandLog : DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Texts.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
