using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class SessionGenerationExitTests
{
    [Test]
    [Arguments("same-owner")]
    [Arguments("pointer-moved")]
    public async Task C502_V4_exit_consumer_blocked_on_the_row_lock_sees_B_after_commit(string shape)
    {
        var generationA = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-5));
        var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow);
        var (sessionId, agentId, logPath, runtime, _) = await AgentSessionRuntimeTestsSeed(generationA);
        var otherSessionId = Guid.NewGuid();
        try
        {
            if (shape == "pointer-moved")
            {
                await using var prep = new AppDbContext(TestDbFixture.CreateDbContextOptions());
                prep.AgentSessions.Add(new Antiphon.Server.Domain.Entities.AgentSession
                {
                    Id = otherSessionId,
                    DefinitionName = "claude",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running,
                    Cwd = Path.GetTempPath(),
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = generationB,
                    StartedAt = generationB,
                    LastSeenAt = generationB,
                });
                await prep.SaveChangesAsync();
            }

            await using var holder = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await using var tx = await holder.Database.BeginTransactionAsync();
            await holder.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT * FROM "AgentSessions" WHERE "Id" = {sessionId} FOR UPDATE""");

            var consume = runtime.ObserveExitAsync(
                new SessionRunnerExitedEvent(sessionId, 1, AgentExitReason.KilledByRequest, 0, generationA),
                CancellationToken.None);

            await WaitForLockAsync(sessionId);

            if (shape == "pointer-moved")
            {
                await holder.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "Agents" SET "PersistentSessionId" = {otherSessionId.ToString("D")} WHERE "Id" = {agentId}""");
            }

            await holder.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "AgentSessions" SET "StartedAt" = {generationB}, "Status" = {SessionStatus.Starting}, "TerminationSource" = {SessionTerminationSource.Unknown}, "EndedAt" = NULL WHERE "Id" = {sessionId}""");
            await tx.CommitAsync();

            var disposition = await consume.WaitAsync(TimeSpan.FromSeconds(15));
            disposition.ShouldBe(SessionExitDisposition.Stale);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var row = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            row.Status.ShouldBe(SessionStatus.Starting);
            row.StartedAt.ShouldBe(generationB);
            var agent = await verify.Agents.SingleAsync(a => a.Id == agentId);
            agent.Status.ShouldBe(AgentStatus.Running);
        }
        finally
        {
            await using var cleanup = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await cleanup.AgentIncidents.Where(i => i.SessionId == sessionId || i.AgentId == agentId).ExecuteDeleteAsync();
            await cleanup.Agents.Where(a => a.Id == agentId).ExecuteDeleteAsync();
            await cleanup.AgentSessions.Where(s => s.Id == sessionId || s.Id == otherSessionId).ExecuteDeleteAsync();
            if (Directory.Exists(logPath))
                Directory.Delete(logPath, true);
        }
    }

    private static async Task<(Guid SessionId, Guid AgentId, string LogPath, AgentSessionRuntime Runtime, DateTime StartedAt)>
        AgentSessionRuntimeTestsSeed(DateTime generation)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            db.AgentSessions.Add(new Antiphon.Server.Domain.Entities.AgentSession
            {
                Id = sessionId,
                DefinitionName = "claude",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = generation,
                StartedAt = generation,
                LastSeenAt = generation,
            });
            db.Agents.Add(new Antiphon.Server.Domain.Entities.Agent
            {
                Id = agentId,
                Name = $"c502-v4-{sessionId:N}"[..40],
                Slug = $"c502-v4-{sessionId:N}",
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentStatus.Running,
                PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = generation,
                UpdatedAt = generation,
            });
            await db.SaveChangesAsync();
        }

        var logPath = Path.Combine(Path.GetTempPath(), $"antiphon-c502-v4-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        var provider = services.BuildServiceProvider();
        var runtime = new AgentSessionRuntime(
            new MockEventBus(),
            Options.Create(new Antiphon.Server.Application.Settings.AgentSessionSettings { SessionLogPath = logPath }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
        return (sessionId, agentId, logPath, runtime, generation);
    }

    private static async Task WaitForLockAsync(Guid sessionId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(TestDbFixture.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT 1
                FROM pg_stat_activity
                WHERE wait_event_type = 'Lock'
                  AND query ILIKE '%AgentSessions%'
                  AND query ILIKE '%' || @id || '%'
                """;
            cmd.Parameters.AddWithValue("id", sessionId.ToString());
            var found = await cmd.ExecuteScalarAsync();
            if (found is not null)
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException("consumer did not wait on the AgentSessions row lock");
    }
}
