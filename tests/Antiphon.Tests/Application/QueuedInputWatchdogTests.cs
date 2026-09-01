using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0292 S4 — <c>QueuedInputWatchdogService</c>. Fleet-global sweep against the shared test
/// Postgres, so <c>[NotInParallel]</c> with NO group key, and every assertion is scoped to the
/// session this test seeded. Detection only: never kills, never types.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class QueuedInputWatchdogTests
{
    [Test]
    public async Task Stuck_idle_enqueue_past_threshold_raises_kind_43_warning()
    {
        var watchdog = CreateWatchdog();
        await using var scenario = new Scenario();
        await scenario.SeedStuckEnqueueAsync(minutesAgo: 4);

        (await watchdog.SweepAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);
        var row = (await scenario.IncidentsAsync()).ShouldHaveSingleItem();
        row.Kind.ShouldBe(AgentIncidentKind.QueuedInputNeverConverted);
        row.Severity.ShouldBe(AlertSeverity.Warning);
        row.FailureReason.ShouldBe(QueuedInputWatchdogService.EpisodeKey(scenario.EnqueueSequence));
        row.SessionId.ShouldBe(scenario.SessionId);
    }

    [Test]
    [Arguments(TranscriptKinds.UserPrompt)]
    [Arguments(TranscriptKinds.QueuedUserPrompt)]
    [Arguments(TranscriptKinds.QueueDequeue)]
    [Arguments(TranscriptKinds.QueueRemove)]
    public async Task Closed_by_each_closure_kind_raises_nothing(string closureKind)
    {
        var watchdog = CreateWatchdog();
        await using var scenario = new Scenario();
        await scenario.SeedStuckEnqueueAsync(minutesAgo: 10);
        await scenario.SeedEntryAsync(closureKind, "Hi");

        (await watchdog.SweepAsync(CancellationToken.None)).ShouldBe(0);
        (await scenario.IncidentsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task Working_session_is_suppressed()
    {
        var watchdog = CreateWatchdog();
        await using var scenario = new Scenario();
        await scenario.SeedWorkingPromptAsync();
        await scenario.SeedStuckEnqueueAsync(minutesAgo: 10);

        await using var db = CreateContext();
        (await SessionMessageQueueService.IsWorkingAsync(db, scenario.SessionId, CancellationToken.None))
            .ShouldBeTrue("a UserPrompt with no TurnEnd is the mid-turn shape");

        (await watchdog.SweepAsync(CancellationToken.None)).ShouldBe(0);
        (await scenario.IncidentsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task Episode_dedupe_does_not_re_raise_the_same_warning()
    {
        var watchdog = CreateWatchdog();
        await using var scenario = new Scenario();
        await scenario.SeedStuckEnqueueAsync(minutesAgo: 4);

        (await watchdog.SweepAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);
        (await watchdog.SweepAsync(CancellationToken.None)).ShouldBe(0);
        (await scenario.IncidentsAsync()).ShouldHaveSingleItem();
    }

    [Test]
    public async Task Error_escalation_then_holds()
    {
        var watchdog = CreateWatchdog();
        await using var scenario = new Scenario();
        await scenario.SeedStuckEnqueueAsync(minutesAgo: 20);

        await watchdog.SweepAsync(CancellationToken.None);
        (await scenario.IncidentsAsync()).ShouldHaveSingleItem().Severity.ShouldBe(AlertSeverity.Warning);

        await watchdog.SweepAsync(CancellationToken.None);
        var rows = await scenario.IncidentsAsync();
        rows.Count.ShouldBe(2);
        rows.Select(i => i.Severity).ShouldBe([AlertSeverity.Warning, AlertSeverity.Error]);

        await watchdog.SweepAsync(CancellationToken.None);
        (await scenario.IncidentsAsync()).Count.ShouldBe(2, "already at Error — no third row");
    }

    [Test]
    public async Task Channel_bound_is_Critical_at_the_Error_step_only()
    {
        var watchdog = CreateWatchdog();
        await using var scenario = new Scenario();
        await scenario.SeedStuckEnqueueAsync(minutesAgo: 20, channelBound: true);

        await watchdog.SweepAsync(CancellationToken.None);
        (await scenario.IncidentsAsync()).ShouldHaveSingleItem().Severity.ShouldBe(AlertSeverity.Warning);

        await watchdog.SweepAsync(CancellationToken.None);
        var rows = await scenario.IncidentsAsync();
        rows.Count.ShouldBe(2);
        rows[^1].Severity.ShouldBe(AlertSeverity.Critical);
    }

    [Test]
    public async Task Disabled_means_silent()
    {
        var watchdog = CreateWatchdog(s => s.QueuedInputWatch.Enabled = false);
        await using var scenario = new Scenario();
        await scenario.SeedStuckEnqueueAsync(minutesAgo: 10);

        (await watchdog.SweepAsync(CancellationToken.None)).ShouldBe(0);
        (await scenario.IncidentsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task Detection_only_never_kills_the_session()
    {
        var watchdog = CreateWatchdog();
        await using var scenario = new Scenario();
        await scenario.SeedStuckEnqueueAsync(minutesAgo: 20);

        for (var i = 0; i < 4; i++)
            await watchdog.SweepAsync(CancellationToken.None);

        await using var db = CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        (await scenario.IncidentsAsync()).Count.ShouldBeGreaterThanOrEqualTo(1);
        (await scenario.IncidentsAsync()).ShouldAllBe(i => i.Kind == AgentIncidentKind.QueuedInputNeverConverted);
    }

    private static QueuedInputWatchdogService CreateWatchdog(Action<SupervisionSettings>? configure = null)
    {
        var settings = new SupervisionSettings();
        configure?.Invoke(settings);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(settings));
        services.AddSingleton<QueuedInputWatchdogService>();
        return services.BuildServiceProvider().GetRequiredService<QueuedInputWatchdogService>();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class Scenario : IAsyncDisposable
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid AgentId { get; } = Guid.NewGuid();
        public long EnqueueSequence { get; private set; }
        private readonly List<Guid> _channels = [];
        private long _seq;

        public Task SeedWorkingPromptAsync() => SeedEntryAsync(TranscriptKinds.UserPrompt, "still working on it");

        public async Task SeedStuckEnqueueAsync(int minutesAgo, bool channelBound = false)
        {
            var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
            await using var db = CreateContext();
            await EnsureLiveSessionAsync(db, at, channelBound);

            _seq++;
            EnqueueSequence = _seq;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = SessionId,
                Sequence = _seq,
                Kind = TranscriptKinds.QueueEnqueue,
                Text = "Hi",
                Timestamp = at,
                CreatedAt = at,
            });
            await db.SaveChangesAsync();
        }

        public async Task SeedEntryAsync(string kind, string? text)
        {
            await using var db = CreateContext();
            await EnsureLiveSessionAsync(db, DateTime.UtcNow.AddMinutes(-10), channelBound: false);
            _seq++;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = SessionId,
                Sequence = _seq,
                Kind = kind,
                Text = text,
                Timestamp = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        private async Task EnsureLiveSessionAsync(AppDbContext db, DateTime at, bool channelBound)
        {
            if (await db.AgentSessions.AnyAsync(s => s.Id == SessionId))
                return;

            db.AgentSessions.Add(new AgentSession
            {
                Id = SessionId,
                DefinitionName = "queued-input-watch",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = at,
                StartedAt = at,
                LastSeenAt = DateTime.UtcNow,
            });
            var name = $"qiw-{AgentId:N}"[..16];
            db.Agents.Add(new Agent
            {
                Id = AgentId,
                Name = name,
                Slug = name,
                WorkingDirectory = Path.GetTempPath(),
                Details = "CARD-0292 queued-input watchdog test.",
                Status = AgentStatus.Running,
                ModelLevel = AgentModelLevel.Frontier,
                IsPoolDelegate = true,
                PersistentSessionId = SessionId.ToString("D"),
                CreatedAt = at,
                UpdatedAt = at,
            });
            if (channelBound)
            {
                var channelId = Guid.NewGuid();
                db.ChatChannels.Add(new ChatChannel
                {
                    Id = channelId,
                    Provider = "telegram",
                    ExternalId = $"qiw-{Guid.NewGuid():N}",
                    Kind = ChatChannelKind.Direct,
                    AgentId = AgentId,
                    Enabled = true,
                    CreatedAt = at,
                    UpdatedAt = at,
                });
                _channels.Add(channelId);
            }
        }

        public async Task<List<AgentIncident>> IncidentsAsync()
        {
            await using var db = CreateContext();
            return await db.AgentIncidents
                .Where(i => i.SessionId == SessionId && i.Kind == AgentIncidentKind.QueuedInputNeverConverted)
                .OrderBy(i => i.CreatedAt)
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.AgentIncidents.Where(i => i.SessionId == SessionId).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(e => e.AgentSessionId == SessionId).ExecuteDeleteAsync();
            if (_channels.Count > 0)
                await db.ChatChannels.Where(c => _channels.Contains(c.Id)).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, (string?)null));
            await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == AgentId).ExecuteDeleteAsync();
        }
    }
}
