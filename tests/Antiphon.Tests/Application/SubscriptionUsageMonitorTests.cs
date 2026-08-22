using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Supervision;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0143 S1–S4. Shared-Postgres: every assertion is scoped to a row this test created.
/// <see cref="NotInParallelAttribute"/> with no group key — the sweep walks every Running
/// session of an eligible kind, so it must not run concurrently with anything creating such
/// rows (the AgentSupervisionTests group-key mistake).
/// </summary>
[Category("Integration")]
[NotInParallel]
public sealed class SubscriptionUsageMonitorTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static Task<BridgeQueueHarness> CreateHarnessAsync() =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions { AlwaysOn = true });

    [Test]
    public void Disabled_by_default()
    {
        var settings = new SubscriptionUsageMonitoringSettings();
        settings.Enabled.ShouldBeFalse();
        settings.IntervalMinutes.ShouldBe(30);
        settings.IncludeDegradedProviders.ShouldBeFalse();
        settings.MinPollIntervalMinutes.ShouldBe(25);
        settings.MaxSessionsPerSweep.ShouldBe(10);
        settings.PerSessionTimeoutSeconds.ShouldBe(20);
        settings.PanelTimeoutSeconds.ShouldBe(5);
        settings.OverlaySettleMs.ShouldBe(400);
        settings.ConsecutiveFailuresBeforeIncident.ShouldBe(3);
    }

    [Test]
    public async Task The_sweep_does_nothing_when_disabled()
    {
        await using var h = await CreateHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);

        await SweepAsync(h, new SubscriptionUsageMonitoringSettings());

        h.Adapter.Inputs.ShouldBeEmpty();
        (await SamplesAsync(h.SessionId)).ShouldBeEmpty();
    }

    [Test]
    public async Task The_hosted_service_exits_without_ticking_when_disabled()
    {
        var factory = new CountingScopeFactory();
        var hosted = new SubscriptionUsageMonitorHostedService(
            factory,
            Options.Create(new SubscriptionUsageMonitoringSettings()),
            NullLogger<SubscriptionUsageMonitorHostedService>.Instance);

        await ((IHostedService)hosted).StartAsync(CancellationToken.None);
        await ((IHostedService)hosted).StopAsync(CancellationToken.None);

        factory.CreateCount.ShouldBe(0);
    }

    [Test]
    public async Task Codex_is_only_ever_sent_slash_status()
    {
        await using var h = await CreateHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.OnSubmitted = null;
        h.Adapter.SubmitAck = "\nWeekly limit:         3% left\n                       (resets 22:13 on 24 Aug)";

        await SweepAsync(h, FastSettings());

        string.Concat(h.Adapter.Inputs).ShouldContain("/status");
        string.Concat(h.Adapter.Inputs).ShouldNotContain("/usage");
        var sample = (await SamplesAsync(h.SessionId)).ShouldHaveSingleItem();
        sample.SourceCommand.ShouldBe("/status");
    }

    [Test]
    public async Task The_catalog_forbids_slash_usage_for_Codex_with_a_reason_at_runtime()
    {
        await using var h = await CreateHarnessAsync();
        var poll = new LocalCommandPoll(
            AgentKind.Codex, "/usage", [], OpensOverlay: false,
            OverlaySettleMs: 0, PanelTimeoutSeconds: 2);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => h.Queue.TryPollLocalCommandAsync(h.SessionId, poll, CancellationToken.None));
        ex.Message.ShouldContain("/usage");
        ex.Message.ShouldContain("reset", Case.Insensitive);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Nothing_is_sent_to_a_session_that_is_not_idle()
    {
        await using var h = await CreateHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.MarkWorkingAsync();

        await SweepAsync(h, FastSettings());

        h.Adapter.Inputs.ShouldBeEmpty();
        (await SamplesAsync(h.SessionId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Skips_a_session_that_is_not_Running_or_not_live()
    {
        await using var h = await CreateHarnessAsync();
        var stoppedId = await SeedRunningSessionAsync(h, AgentKind.Codex);
        await SetStatusAsync(stoppedId, SessionStatus.Stopped);
        h.Runtime.Register(stoppedId, new FakeAgentProtocolAdapter());

        var notLiveId = await SeedRunningSessionAsync(h, AgentKind.Codex);

        await SweepAsync(h, FastSettings());

        (await SamplesAsync(stoppedId)).ShouldBeEmpty();
        (await SamplesAsync(notLiveId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Skips_kinds_with_no_established_command()
    {
        await using var h = await CreateHarnessAsync();
        var openCodeId = await SeedRunningSessionAsync(h, AgentKind.OpenCode);
        var rawId = await SeedRunningSessionAsync(h, AgentKind.Raw);
        h.Runtime.Register(openCodeId, new FakeAgentProtocolAdapter());
        h.Runtime.Register(rawId, new FakeAgentProtocolAdapter());

        var eligible = SubscriptionUsageMonitorService.EligibleKinds(includeDegraded: false);
        eligible.ShouldNotContain(AgentKind.ClaudeCode);
        eligible.ShouldNotContain(AgentKind.OpenCode);
        eligible.ShouldNotContain(AgentKind.Raw);
        eligible.ShouldContain(AgentKind.Codex);

        await SweepAsync(h, FastSettings());

        h.Adapter.Inputs.ShouldBeEmpty();
        (await SamplesAsync(h.SessionId)).ShouldBeEmpty();
        (await SamplesAsync(openCodeId)).ShouldBeEmpty();
        (await SamplesAsync(rawId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Grok_is_skipped_while_it_is_Degraded()
    {
        await using var h = await CreateHarnessAsync();
        var grokId = await SeedRunningSessionAsync(h, AgentKind.Grok);
        var adapter = new FakeAgentProtocolAdapter { OnSubmitted = null };
        h.Runtime.Register(grokId, adapter);

        await SweepAsync(h, FastSettings());
        adapter.Inputs.ShouldBeEmpty();
        (await SamplesAsync(grokId)).ShouldBeEmpty();

        var includeDegraded = FastSettings();
        includeDegraded.IncludeDegradedProviders = true;
        adapter.SubmitAck = "\nWeekly limit (SuperGrok)\n[progress bar]  1%\nResets: August 28, 05:31";
        await SweepAsync(h, includeDegraded);

        string.Concat(adapter.Inputs).ShouldContain("/usage");
        (await SamplesAsync(grokId)).ShouldNotBeEmpty();
    }

    [Test]
    public async Task A_poll_with_no_composer_evidence_withholds_Enter_and_kills_nothing()
    {
        await using var h = await CreateHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.EchoTypedInputToScreen = false;
        h.Adapter.OnSubmitted = null;

        AgentStatus statusBefore;
        await using (var db = CreateContext())
        {
            statusBefore = await db.Agents.Where(a => a.Id == h.AgentId).Select(a => a.Status).SingleAsync();
        }

        var result = await h.Queue.TryPollLocalCommandAsync(
            h.SessionId, CodexPoll(), CancellationToken.None);

        result.ShouldBeOfType<LocalCommandPollResult.NotAccepted>();
        h.Adapter.Inputs.ShouldNotContain("\r");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();

        await using (var db = CreateContext())
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.Status.ShouldBe(SessionStatus.Running);

            var agent = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
            agent.Status.ShouldBe(statusBefore);

            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId)).ShouldBe(0);
            (await db.AgentIncidents.CountAsync(i => i.SessionId == h.SessionId)).ShouldBe(0);
        }
    }

    [Test]
    public async Task A_local_command_poll_starts_no_manual_run_attempt()
    {
        await using var h = await CreateHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.OnSubmitted = null;
        h.Adapter.SubmitAck = "\nok";
        var cardId = await BindCardAsync(h);

        var result = await h.Queue.TryPollLocalCommandAsync(
            h.SessionId, CodexPoll(), CancellationToken.None);
        result.ShouldBeOfType<LocalCommandPollResult.Sent>();

        await Task.Delay(300);

        try
        {
            await using var db = CreateContext();
            (await db.RunAttempts.CountAsync(a => a.CardId == cardId)).ShouldBe(0);
            var card = await db.Cards.SingleAsync(c => c.Id == cardId);
            card.OwnerSessionId.ShouldBe(h.SessionId);
        }
        finally
        {
            await CleanupCardAsync(cardId);
        }
    }

    [Test]
    public async Task An_unparsable_panel_records_an_Unparsed_sample_rather_than_nothing()
    {
        await using var h = await CreateHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.OnSubmitted = null;
        h.Adapter.SubmitAck = "\nthis is not a usage panel at all";

        await SweepAsync(h, FastSettings());

        var sample = (await SamplesAsync(h.SessionId)).ShouldHaveSingleItem();
        sample.ParseStatus.ShouldBe(SubscriptionUsageParseStatus.Unparsed);
        sample.RawExcerpt.ShouldNotBeNullOrWhiteSpace();
        sample.SourceCommand.ShouldBe("/status");
    }

    [Test]
    public async Task Respects_the_per_session_minimum_interval()
    {
        await using var h = await CreateHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.OnSubmitted = null;
        h.Adapter.SubmitAck = "\nWeekly limit:         3% left\n                       (resets 22:13 on 24 Aug)";

        var settings = FastSettings();
        var service = new SubscriptionUsageMonitorService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            h.Queue,
            h.Runtime,
            Options.Create(settings),
            TimeProvider.System,
            NullLogger<SubscriptionUsageMonitorService>.Instance);
        await service.SweepAsync(CancellationToken.None);
        await service.SweepAsync(CancellationToken.None);
        (await SamplesAsync(h.SessionId)).Count.ShouldBe(1);

        await SweepAsync(h, settings);
        (await SamplesAsync(h.SessionId)).Count.ShouldBe(1, "a fresh service instance still sees the stored sample");
    }

    [Test]
    public async Task The_reader_returns_the_newest_parsed_sample_per_subscription()
    {
        await using var h = await CreateHarnessAsync();
        var key = $"t17-{Guid.NewGuid():N}";
        var older = DateTime.UtcNow.AddHours(-3);
        var newer = DateTime.UtcNow.AddHours(-2);
        var newestUnparsed = DateTime.UtcNow.AddHours(-1);
        await SeedSampleAsync(h.SessionId, h.AgentId, key, 40, older, SubscriptionUsageParseStatus.Parsed);
        await SeedSampleAsync(h.SessionId, h.AgentId, key, 10, newer, SubscriptionUsageParseStatus.Parsed);
        await SeedSampleAsync(h.SessionId, h.AgentId, key, remaining: null, newestUnparsed, SubscriptionUsageParseStatus.Unparsed);

        await using var db = CreateContext();
        var reader = new SubscriptionUsageReader(db, TimeProvider.System);
        var snap = await reader.GetLatestAsync(AgentKind.Codex, key, CancellationToken.None);
        snap.ShouldNotBeNull();
        snap.RemainingPercent.ShouldBe(10);
        snap.ObservedAt.ShouldBe(newer, TimeSpan.FromSeconds(1));

        var all = await reader.GetLatestAsync(CancellationToken.None);
        all.ShouldContain(s => s.SubscriptionKey == key && s.RemainingPercent == 10);
        all.ShouldNotContain(s => s.SubscriptionKey == key && s.RemainingPercent == 40);
    }

    private static SubscriptionUsageMonitoringSettings FastSettings() => new()
    {
        Enabled = true,
        IntervalMinutes = 30,
        IncludeDegradedProviders = false,
        MinPollIntervalMinutes = 25,
        MaxSessionsPerSweep = 10,
        PerSessionTimeoutSeconds = 20,
        PanelTimeoutSeconds = 2,
        OverlaySettleMs = 0,
        ConsecutiveFailuresBeforeIncident = 3,
    };

    private static LocalCommandPoll CodexPoll() => new(
        AgentKind.Codex, "/status", [], OpensOverlay: false,
        OverlaySettleMs: 0, PanelTimeoutSeconds: 2);

    private static async Task SweepAsync(BridgeQueueHarness h, SubscriptionUsageMonitoringSettings settings)
    {
        var service = new SubscriptionUsageMonitorService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            h.Queue,
            h.Runtime,
            Options.Create(settings),
            TimeProvider.System,
            NullLogger<SubscriptionUsageMonitorService>.Instance);
        await service.SweepAsync(CancellationToken.None);
    }

    private static async Task SetKindAsync(Guid sessionId, AgentKind kind)
    {
        await using var db = CreateContext();
        await db.AgentSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.AgentKind, kind));
    }

    private static async Task SetStatusAsync(Guid sessionId, SessionStatus status)
    {
        await using var db = CreateContext();
        await db.AgentSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, status));
    }

    private static async Task<Guid> SeedRunningSessionAsync(BridgeQueueHarness h, AgentKind kind)
    {
        var sessionId = Guid.NewGuid();
        var cwd = Path.Combine(h.TempRoot, $"{kind}-{sessionId:N}");
        Directory.CreateDirectory(cwd);
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = kind,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private static async Task<List<SubscriptionUsageSample>> SamplesAsync(Guid sessionId)
    {
        await using var db = CreateContext();
        return await db.SubscriptionUsageSamples
            .Where(s => s.AgentSessionId == sessionId)
            .ToListAsync();
    }

    private static async Task SeedSampleAsync(
        Guid sessionId, Guid agentId, string key, double? remaining, DateTime observedAt,
        SubscriptionUsageParseStatus status)
    {
        await using var db = CreateContext();
        db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
        {
            Id = Guid.NewGuid(),
            Provider = AgentKind.Codex,
            SubscriptionKey = key,
            RemainingPercent = remaining,
            ObservedAt = observedAt,
            AgentId = agentId,
            AgentSessionId = sessionId,
            SourceCommand = "/status",
            ParseStatus = status,
            RawExcerpt = "seeded",
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> BindCardAsync(BridgeQueueHarness h)
    {
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = projectId,
            Name = $"usage-{cardId:N}",
            GitRepositoryUrl = "https://example.invalid/repo.git",
            BaseBranch = "master",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Boards.Add(new Board
        {
            Id = boardId,
            ProjectId = projectId,
            Name = $"board-{cardId:N}",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.BoardColumns.Add(new BoardColumn
        {
            Id = columnId,
            BoardId = boardId,
            StateKey = "in-progress",
            Name = "In Progress",
            ColumnOrder = 1,
            CardStatus = CardStatus.InProgress,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Cards.Add(new Card
        {
            Id = cardId,
            BoardId = boardId,
            BoardColumnId = columnId,
            Identifier = $"USAGE-{cardId.ToString("N")[..8]}",
            Title = "usage poll card",
            Description = "seeded",
            Status = CardStatus.InProgress,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        await db.AgentSessions.Where(s => s.Id == h.SessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.CardId, cardId));
        await db.Cards.Where(c => c.Id == cardId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.OwnerSessionId, h.SessionId));
        return cardId;
    }

    private static async Task CleanupCardAsync(Guid cardId)
    {
        await using var db = CreateContext();
        var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cardId);
        if (card is null)
            return;
        var boardId = card.BoardId;
        var columnId = card.BoardColumnId;
        var projectId = await db.Boards.Where(b => b.Id == boardId).Select(b => b.ProjectId).FirstAsync();
        await db.RunAttempts.Where(a => a.CardId == cardId).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => r.CardId == cardId).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => s.CardId == cardId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.CardId, (Guid?)null));
        await db.Cards.Where(c => c.Id == cardId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(c => c.OwnerSessionId, (Guid?)null)
                .SetProperty(c => c.CurrentWorktreeId, (Guid?)null));
        await db.Cards.Where(c => c.Id == cardId).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => c.Id == columnId).ExecuteDeleteAsync();
        await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
    }

    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        public int CreateCount;

        public IServiceScope CreateScope()
        {
            CreateCount++;
            throw new InvalidOperationException("scope should not be created when disabled");
        }
    }
}
