using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0245 S1b — <c>AppHostWatchdogStateAttentionService</c>. Detection only: a Disabled
/// observer document creates one Critical incident per eligible agent per episode; maintenance
/// and no eligible agent create none. Never restarts or re-enables anything.
/// </summary>
[Category("Integration")]
[NotInParallel]
public sealed class AppHostWatchdogStateAttentionServiceTests
{
    [Test]
    public async Task Disabled_creates_one_critical_incident_for_an_eligible_agent()
    {
        await using var scenario = new Scenario();
        var agentId = await scenario.AddEligibleAgentAsync();
        var episode = Guid.NewGuid();
        scenario.WriteState(state: "Disabled", episodeId: episode, maintenance: false);

        await scenario.TickAsync();

        var row = (await scenario.IncidentsAsync(agentId)).ShouldHaveSingleItem();
        row.Kind.ShouldBe(AgentIncidentKind.AppHostWatchdogDisabled);
        row.Severity.ShouldBe(AlertSeverity.Critical);
        row.FailureReason.ShouldBe($"episode={episode:D}");
        scenario.Recorder.Alerts.ShouldContain(a => a.AgentId == agentId && a.Severity == AlertSeverity.Critical);
        scenario.Recorder.Restarts.ShouldBe(0);
    }

    [Test]
    public async Task Repeated_reads_of_the_same_episode_do_not_duplicate()
    {
        await using var scenario = new Scenario();
        var agentId = await scenario.AddEligibleAgentAsync();
        var episode = Guid.NewGuid();
        scenario.WriteState(state: "Disabled", episodeId: episode, maintenance: false);

        await scenario.TickAsync();
        await scenario.TickAsync();
        await scenario.TickAsync();
        (await scenario.IncidentsAsync(agentId)).Count.ShouldBe(1);
    }

    [Test]
    public async Task Maintenance_creates_none()
    {
        await using var scenario = new Scenario();
        var agentId = await scenario.AddEligibleAgentAsync();
        scenario.WriteState(state: "Disabled", episodeId: Guid.NewGuid(), maintenance: true);

        await scenario.TickAsync();
        (await scenario.IncidentsAsync(agentId)).Count.ShouldBe(0);
        scenario.Recorder.Alerts.ShouldNotContain(a => a.AgentId == agentId);
    }

    [Test]
    public async Task No_eligible_agent_creates_none()
    {
        await using var scenario = new Scenario();
        var idle = await scenario.AddAgentAsync(alwaysOn: false, withChannel: true);
        var unbound = await scenario.AddAgentAsync(alwaysOn: true, withChannel: false);
        scenario.WriteState(state: "Missing", episodeId: Guid.NewGuid(), maintenance: false);

        await scenario.TickAsync();
        (await scenario.IncidentsAsync(idle)).Count.ShouldBe(0);
        (await scenario.IncidentsAsync(unbound)).Count.ShouldBe(0);
    }

    [Test]
    public async Task A_new_episode_creates_a_new_incident()
    {
        await using var scenario = new Scenario();
        var agentId = await scenario.AddEligibleAgentAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        scenario.WriteState(state: "Disabled", episodeId: first, maintenance: false);
        await scenario.TickAsync();

        scenario.WriteState(state: "Disabled", episodeId: second, maintenance: false);
        await scenario.TickAsync();

        var rows = await scenario.IncidentsAsync(agentId);
        rows.Count.ShouldBe(2);
        rows.Select(r => r.FailureReason).ShouldBe([$"episode={first:D}", $"episode={second:D}"]);
    }

    [Test]
    public async Task Unknown_and_missing_are_unhealthy_same_as_disabled()
    {
        await using var scenario = new Scenario();
        var agentId = await scenario.AddEligibleAgentAsync();
        scenario.WriteState(state: "Unknown", episodeId: Guid.NewGuid(), maintenance: false);
        await scenario.TickAsync();
        (await scenario.IncidentsAsync(agentId)).ShouldHaveSingleItem().Kind
            .ShouldBe(AgentIncidentKind.AppHostWatchdogDisabled);
    }

    [Test]
    public async Task Enabled_creates_none()
    {
        await using var scenario = new Scenario();
        var agentId = await scenario.AddEligibleAgentAsync();
        scenario.WriteState(state: "Enabled", episodeId: null, maintenance: false, healthy: true);
        await scenario.TickAsync();
        (await scenario.IncidentsAsync(agentId)).Count.ShouldBe(0);
    }

    [Test]
    public void Parser_rejects_garbage_and_accepts_observer_json()
    {
        AppHostWatchdogStateDocument.TryParse("", out _).ShouldBeFalse();
        AppHostWatchdogStateDocument.TryParse("{", out _).ShouldBeFalse();
        AppHostWatchdogStateDocument.TryParse(
            """{"observedAtUtc":"2026-08-30T00:00:00Z","state":"Disabled","healthy":false,"maintenance":false,"episodeId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","disabledSinceUtc":"2026-08-30T00:00:00Z"}""",
            out var doc).ShouldBeTrue();
        doc!.IsUnhealthy.ShouldBeTrue();
        doc.State.ShouldBe("Disabled");
        doc.EpisodeId.ShouldBe(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("card0245-s1b-").FullName;
        private readonly List<Guid> _agents = [];
        private readonly List<Guid> _channels = [];
        public RecordingIncidentRecorder Recorder { get; } = new();

        public Scenario()
        {
            Directory.CreateDirectory(Path.Combine(_root, "logs"));
        }

        public string StatePath => Path.Combine(_root, "logs", "apphost-watchdog-state.json");

        public Task<Guid> AddEligibleAgentAsync() => AddAgentAsync(alwaysOn: true, withChannel: true);

        public async Task<Guid> AddAgentAsync(bool alwaysOn, bool withChannel)
        {
            var id = Guid.NewGuid();
            var name = $"wd-{id:N}"[..16];
            await using var db = CreateContext();
            db.Agents.Add(new Agent
            {
                Id = id,
                Name = name,
                Slug = name,
                WorkingDirectory = Path.GetTempPath(),
                Details = "CARD-0245 S1b",
                Status = AgentStatus.Idle,
                ModelLevel = AgentModelLevel.Medium,
                AlwaysOn = alwaysOn,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            if (withChannel)
            {
                var channelId = Guid.NewGuid();
                db.ChatChannels.Add(new ChatChannel
                {
                    Id = channelId,
                    Provider = "slack",
                    ExternalId = $"wd-{id:N}",
                    Kind = ChatChannelKind.Group,
                    AgentId = id,
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
                _channels.Add(channelId);
            }
            await db.SaveChangesAsync();
            _agents.Add(id);
            return id;
        }

        public void WriteState(string state, Guid? episodeId, bool maintenance, bool healthy = false)
        {
            var episode = episodeId is { } g ? $"\"{g:D}\"" : "null";
            var since = healthy ? "null" : "\"2026-08-30T04:19:00Z\"";
            File.WriteAllText(StatePath,
                $$"""
                {"observedAtUtc":"2026-08-30T04:20:00Z","taskName":"Antiphon AppHost Watchdog","state":"{{state}}","healthy":{{healthy.ToString().ToLowerInvariant()}},"maintenance":{{maintenance.ToString().ToLowerInvariant()}},"disabledSinceUtc":{{since}},"episodeId":{{episode}},"detail":"test"}
                """);
        }

        public async Task<int> TickAsync()
        {
            await using var db = CreateContext();
            Recorder.Db = db;
            var settings = Options.Create(new SupervisionSettings
            {
                AppHostWatchdogState = new AppHostWatchdogStateSettings
                {
                    Enabled = true,
                    StateDocumentPath = StatePath,
                    PollSeconds = 30,
                }
            });
            var sut = new AppHostWatchdogStateAttentionService(
                db,
                Recorder,
                settings,
                new FakeEnv(_root),
                NullLogger<AppHostWatchdogStateAttentionService>.Instance);
            return await sut.TickAsync(CancellationToken.None);
        }

        public async Task<List<AgentIncident>> IncidentsAsync(Guid agentId)
        {
            await using var db = CreateContext();
            return await db.AgentIncidents.AsNoTracking()
                .Where(i => i.AgentId == agentId && i.Kind == AgentIncidentKind.AppHostWatchdogDisabled)
                .OrderBy(i => i.CreatedAt)
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.AgentIncidents
                .Where(i => i.Kind == AgentIncidentKind.AppHostWatchdogDisabled
                    && (i.AgentId != null && _agents.Contains(i.AgentId.Value)
                        || i.FailureReason != null && i.FailureReason.StartsWith(AppHostWatchdogStateAttentionService.EpisodeReasonPrefix)))
                .ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => _channels.Contains(c.Id)).ExecuteDeleteAsync();
            await db.Agents.Where(a => _agents.Contains(a.Id)).ExecuteDeleteAsync();
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }

        private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
    }

    private sealed class RecordingIncidentRecorder : IAgentIncidentRecorder
    {
        public AppDbContext? Db { get; set; }
        public List<AlertRaise> Alerts { get; } = [];
        public int Restarts { get; private set; }

        public Task RecordIncidentAsync(
            Guid? agentId,
            Guid? sessionId,
            AgentIncidentKind kind,
            AlertSeverity severity,
            string message,
            int? exitCode = null,
            string? failureReason = null,
            bool raiseAlert = true,
            CancellationToken ct = default)
        {
            if (kind is AgentIncidentKind.RestartScheduled or AgentIncidentKind.RcRestart)
                Restarts++;

            Db!.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                SessionId = sessionId,
                Kind = kind,
                Severity = severity,
                Message = message,
                ExitCode = exitCode,
                FailureReason = failureReason,
                CreatedAt = DateTime.UtcNow,
            });
            if (raiseAlert)
            {
                Alerts.Add(new AlertRaise(
                    severity, "supervisor", $"{kind}: agent supervision", message,
                    DedupKey: $"supervisor:{kind}:{agentId:D}", AgentId: agentId, SessionId: sessionId));
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEnv(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Antiphon.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
