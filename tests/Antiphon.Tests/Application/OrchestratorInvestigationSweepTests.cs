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
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0247 S3 — hosted sweep: one incident per run, idempotent on a second pass.
/// Every assertion is scoped to the session this test seeded (shared-Postgres rule).
/// </summary>
[Category("Integration")]
public class OrchestratorInvestigationSweepTests
{
    [Test]
    public async Task A_cold_run_raises_one_warning_incident()
    {
        await using var scenario = new Scenario();
        await scenario.SeedColdRunAsync();

        var sweep = CreateSweep();
        var raised = await sweep.SweepSessionAsync(CreateContext(), scenario.SessionId, CancellationToken.None);
        raised.ShouldBe(1);

        var rows = await scenario.IncidentsAsync();
        rows.ShouldHaveSingleItem();
        rows[0].Kind.ShouldBe(AgentIncidentKind.OrchestratorInvestigation);
        rows[0].Severity.ShouldBe(AlertSeverity.Warning);
        rows[0].SessionId.ShouldBe(scenario.SessionId);
        rows[0].AgentId.ShouldBe(scenario.AgentId);
        rows[0].Message.ShouldContain("reads over");
        rows[0].Message.ShouldContain("nudged=no");
        rows[0].FailureReason.ShouldStartWith("runStartSeq=");
    }

    [Test]
    public async Task Running_the_sweep_twice_does_not_duplicate_the_incident()
    {
        await using var scenario = new Scenario();
        await scenario.SeedColdRunAsync();

        var first = CreateSweep();
        (await first.SweepSessionAsync(CreateContext(), scenario.SessionId, CancellationToken.None))
            .ShouldBe(1);
        var second = CreateSweep();
        (await second.SweepSessionAsync(CreateContext(), scenario.SessionId, CancellationToken.None))
            .ShouldBe(0, "a fresh sweep instance must not re-raise the same run");

        (await scenario.IncidentsAsync()).Count.ShouldBe(1);
    }

    [Test]
    public async Task An_unclaimed_session_still_gets_an_incident_hung_on_the_session()
    {
        await using var scenario = new Scenario();
        await scenario.SeedColdRunAsync(claimAgent: false);

        var sweep = CreateSweep();
        (await sweep.SweepSessionAsync(CreateContext(), scenario.SessionId, CancellationToken.None))
            .ShouldBe(1);

        var row = (await scenario.IncidentsAsync()).ShouldHaveSingleItem();
        row.AgentId.ShouldBeNull();
        row.SessionId.ShouldBe(scenario.SessionId);
    }

    [Test]
    public async Task SweepAsync_picks_up_a_session_that_is_an_orchestrator_by_behaviour()
    {
        await using var scenario = new Scenario();
        await scenario.SeedColdRunAsync();

        var raised = await CreateSweep().SweepAsync(CancellationToken.None);
        raised.ShouldBeGreaterThanOrEqualTo(1);

        (await scenario.IncidentsAsync()).ShouldHaveSingleItem()
            .Kind.ShouldBe(AgentIncidentKind.OrchestratorInvestigation);
    }

    private static OrchestratorInvestigationSweepService CreateSweep()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        var provider = services.BuildServiceProvider();
        return new OrchestratorInvestigationSweepService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SupervisionSettings()),
            TimeProvider.System,
            NullLogger<OrchestratorInvestigationSweepService>.Instance);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class Scenario : IAsyncDisposable
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid AgentId { get; } = Guid.NewGuid();
        private readonly List<Guid> _tasks = [];
        private long _seq;

        public async Task SeedColdRunAsync(bool claimAgent = true)
        {
            var now = DateTime.UtcNow;
            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = SessionId,
                DefinitionName = "investigation-sweep-test",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = now.AddHours(-1),
                StartedAt = now.AddHours(-1),
                LastSeenAt = now,
            });
            if (claimAgent)
            {
                var name = $"inv-{AgentId:N}"[..16];
                db.Agents.Add(new Agent
                {
                    Id = AgentId,
                    Name = name,
                    Slug = name,
                    WorkingDirectory = Path.GetTempPath(),
                    Details = "CARD-0247 investigation sweep test agent.",
                    Status = AgentStatus.Running,
                    ModelLevel = AgentModelLevel.High,
                    IsPoolDelegate = true,
                    PersistentSessionId = SessionId.ToString("D"),
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            var taskId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "behaviour parent",
                Goal = "exist so this session is an orchestrator by behaviour",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                ParentSessionId = SessionId,
                Status = AgentTaskStatus.Succeeded,
                // Older than N_dispatch tool-calls of the cold run below — the row is
                // population, not a dispatch that should suppress the investigation.
                CreatedAt = now.AddHours(-2),
                DispatchedAt = now.AddHours(-2),
                CompletedAt = now.AddHours(-2).AddMinutes(10),
            });
            _tasks.Add(taskId);
            await db.SaveChangesAsync();

            for (var i = 0; i < 15; i++)
            {
                await AddEntryAsync(TranscriptKinds.ToolCall, "Bash",
                    """{"command":"git status --short"}""", null, 40 - i);
            }

            await AddEntryAsync(TranscriptKinds.UserPrompt, null, null, "Please look into why launches fail", 10);
            await AddEntryAsync(TranscriptKinds.ToolCall, "Read",
                """{"file_path":"C:\\src\\Antiphon\\server\\Application\\Services\\Foo.cs"}""", null, 9);
            await AddEntryAsync(TranscriptKinds.ToolCall, "Read",
                """{"file_path":"C:\\src\\Antiphon\\server\\Application\\Services\\Bar.cs"}""", null, 8);
            await AddEntryAsync(TranscriptKinds.ToolCall, "Read",
                """{"file_path":"C:\\src\\Antiphon\\server\\Application\\Services\\Baz.cs"}""", null, 7);
        }

        public async Task<List<AgentIncident>> IncidentsAsync()
        {
            await using var db = CreateContext();
            return await db.AgentIncidents
                .Where(i => i.SessionId == SessionId && i.Kind == AgentIncidentKind.OrchestratorInvestigation)
                .OrderBy(i => i.CreatedAt)
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.AgentIncidents.Where(i => i.SessionId == SessionId).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(e => e.AgentSessionId == SessionId).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == AgentId).ExecuteDeleteAsync();
        }

        private async Task AddEntryAsync(
            string kind, string? toolName, string? toolInput, string? text, int minutesAgo)
        {
            var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
            await using var db = CreateContext();
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = SessionId,
                Sequence = ++_seq,
                Kind = kind,
                Uuid = $"inv-{Guid.NewGuid():N}",
                Role = kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
                Text = text,
                ToolName = toolName,
                ToolInput = toolInput,
                Timestamp = at,
                CreatedAt = at,
            });
            await db.SaveChangesAsync();
        }
    }
}
