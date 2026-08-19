using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
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
/// CARD-0082 S3 / CARD-0083 S5 — the idle auto-compact sweep. Shared-Postgres rules: every
/// assertion is scoped to a row this test created, and the class takes <c>[NotInParallel]</c>
/// with NO group key because SweepAsync walks every Running session whose context-window
/// contract is Supported or Degraded.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class ContextCompactionSweepTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static Task<BridgeQueueHarness> CreateHarnessAsync() =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions { AlwaysOn = true });

    [Test]
    public async Task Fires_on_idle_and_full()
    {
        await using var h = await CreateHarnessAsync();
        await SeedIdleFullAsync(h.SessionId);

        (await EligibleSessionIdsAsync()).ShouldContain(h.SessionId);

        await SweepAsync(h, FastSettings());

        (await SupervisionMessagesAsync(h.SessionId))
            .ShouldContain(m => m.Body == ContextCompactionService.CompactTriggerBody
                && (m.Status == QueuedMessageStatus.Sent || m.Status == QueuedMessageStatus.Pending));
        h.Adapter.SubmittedBodies.ShouldContain(ContextCompactionService.CompactTriggerBody);
    }

    [Test]
    public async Task Skips_busy()
    {
        await using var h = await CreateHarnessAsync();
        await SeedIdleFullAsync(h.SessionId);
        // Activity after the last turn-end, timestamped old enough that idle-for would pass,
        // and without InputTokens so it cannot change fullness.
        await SeedUsageAsync(
            h.SessionId, kind: TranscriptKinds.AssistantText, tokens: null, hoursAgo: 3, text: "still working");

        await SweepAsync(h, FastSettings());

        (await SupervisionMessagesAsync(h.SessionId)).ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    public async Task Skips_unknown_fullness()
    {
        await using var h = await CreateHarnessAsync();
        await SeedIdleFullAsync(h.SessionId);
        await SeedBoundaryAsync(h.SessionId, hoursAgo: 3, manual: true);

        await SweepAsync(h, FastSettings());

        (await SupervisionMessagesAsync(h.SessionId)).ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    public async Task Skips_zero_transcript_rows()
    {
        await using var h = await CreateHarnessAsync();

        await SweepAsync(h, FastSettings());

        (await SupervisionMessagesAsync(h.SessionId)).ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    public async Task Skips_cooldown()
    {
        await using var h = await CreateHarnessAsync();
        // A /compact prompt is cooldown without invalidating fullness (only CompactBoundary / /clear
        // do that). Timestamped old enough for idle-for (IdleMinutes=1) but inside CooldownHours=24.
        await SeedUsageAsync(h.SessionId, TranscriptKinds.AssistantText, tokens: 120_000, hoursAgo: 4);
        await SeedUsageAsync(
            h.SessionId, TranscriptKinds.UserPrompt, tokens: null, hoursAgo: 2,
            text: "/compact Focus the summary on: current task state");
        await SeedUsageAsync(
            h.SessionId, TranscriptKinds.TurnEnd, tokens: null, hoursAgo: 2, stopReason: "end_turn");

        await SweepAsync(h, FastSettings());

        (await SupervisionMessagesAsync(h.SessionId)).ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    public async Task Cancel_on_became_busy_does_not_leave_a_pending_compact()
    {
        await using var h = await CreateHarnessAsync();
        await h.MarkWorkingAsync();

        await h.Queue.EnqueueAsync(
            h.SessionId, ContextCompactionService.CompactTriggerBody,
            Antiphon.Server.Application.Dtos.MessageSendMode.WhenIdle,
            CancellationToken.None, QueuedMessageOrigin.Supervision);

        var stored = (await SupervisionMessagesAsync(h.SessionId)).ShouldHaveSingleItem();
        stored.Status.ShouldBe(QueuedMessageStatus.Canceled);
        stored.CanceledAt.ShouldNotBeNull();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    public async Task Boundary_resets_so_the_sweep_does_not_refire()
    {
        await using var h = await CreateHarnessAsync();
        await SeedIdleFullAsync(h.SessionId);

        await SweepAsync(h, FastSettings());
        h.Adapter.SubmittedBodies.ShouldContain(ContextCompactionService.CompactTriggerBody);

        await SeedBoundaryAsync(h.SessionId, hoursAgo: 0, manual: true);
        await SweepAsync(h, FastSettings());

        (await SupervisionMessagesAsync(h.SessionId))
            .Count(m => m.Body == ContextCompactionService.CompactTriggerBody)
            .ShouldBe(1, "a compact boundary makes fullness unknown and resets idle; no second fire");
    }

    [Test]
    public async Task Incident_on_timeout()
    {
        await using var h = await CreateHarnessAsync();
        await SeedSentSupervisionAsync(h.SessionId, sentAtUtc: DateTime.UtcNow.AddMinutes(-15));

        await SweepAsync(h, FastSettings());

        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.AutoCompactFailed)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.SessionId.ShouldBe(h.SessionId);
        incident.FailureReason.ShouldBe("BoundaryTimeout");
        incident.Message.ShouldContain("CompactBoundary");
    }

    [Test]
    public async Task A_second_sweep_does_not_re_raise_the_timeout_incident()
    {
        await using var h = await CreateHarnessAsync();
        await SeedSentSupervisionAsync(h.SessionId, sentAtUtc: DateTime.UtcNow.AddMinutes(-15));

        await SweepAsync(h, FastSettings());
        await SweepAsync(h, FastSettings());

        await using var db = CreateContext();
        (await db.AgentIncidents.CountAsync(
                i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.AutoCompactFailed))
            .ShouldBe(1);
    }

    [Test]
    public async Task An_unclaimed_session_is_included()
    {
        await using var h = await CreateHarnessAsync();
        var (unclaimedId, adapter) = await SeedUnclaimedLiveSessionAsync(h);
        await SeedIdleFullAsync(unclaimedId);
        adapter.OnSubmitted = async submitted =>
        {
            await InsertPromptAsync(unclaimedId, submitted);
        };

        await SweepAsync(h, FastSettings());

        adapter.SubmittedBodies.ShouldContain(ContextCompactionService.CompactTriggerBody);
        (await SupervisionMessagesAsync(unclaimedId))
            .ShouldContain(m => m.Body == ContextCompactionService.CompactTriggerBody);
        // The owned harness session has no usage rows — must not have been swept into a compact.
        (await SupervisionMessagesAsync(h.SessionId)).ShouldBeEmpty();
    }

    [Test]
    public async Task An_owned_session_respects_the_per_agent_enabled_override()
    {
        await using var h = await CreateHarnessAsync();
        await SeedIdleFullAsync(h.SessionId);
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.AutoCompactEnabled, false));
        }

        await SweepAsync(h, FastSettings());

        (await SupervisionMessagesAsync(h.SessionId)).ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    public async Task An_owned_session_respects_the_per_agent_idle_override()
    {
        await using var h = await CreateHarnessAsync();
        await SeedIdleFullAsync(h.SessionId, hoursAgo: 2);
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.AutoCompactIdleMinutes, 1));
        }

        // Global idle is 480 minutes; the session is only 2 hours idle. The agent override of 1
        // minute is what makes it eligible.
        await SweepAsync(h, new ContextCompactionSettings
        {
            Enabled = true,
            IdleMinutes = 480,
            ContextPercent = 50,
            CooldownHours = 24,
            BoundaryTimeoutMinutes = 10,
        });

        h.Adapter.SubmittedBodies.ShouldContain(ContextCompactionService.CompactTriggerBody);
    }

    [Test]
    public void Context_window_eligibility_follows_Supported_or_Degraded()
    {
        ContextCompactionService.IsContextWindowEligible(AgentKind.ClaudeCode).ShouldBeTrue();
        ContextCompactionService.IsContextWindowEligible(AgentKind.Grok).ShouldBeTrue();
        ContextCompactionService.IsContextWindowEligible(AgentKind.Codex).ShouldBeFalse();
        ContextCompactionService.IsContextWindowEligible(AgentKind.OpenCode).ShouldBeFalse();
        ContextCompactionService.IsContextWindowEligible(AgentKind.Raw).ShouldBeFalse();
        ContextCompactionService.IsContextWindowEligible((AgentKind)int.MaxValue).ShouldBeFalse();
    }

    [Test]
    public async Task A_grok_session_passes_eligibility_but_does_not_enqueue_when_fullness_is_unknown()
    {
        await using var h = await CreateHarnessAsync();
        var grokId = await SeedRunningSessionAsync(h, AgentKind.Grok);
        // Transcript exists (so this is not the zero-rows skip) but no usage columns — the
        // production Grok tailer shape today. The session is in the query; Compute returns unknown.
        await SeedUsageAsync(grokId, TranscriptKinds.AssistantText, tokens: null, hoursAgo: 3, text: "reply");
        await SeedUsageAsync(grokId, TranscriptKinds.TurnEnd, tokens: null, hoursAgo: 3, stopReason: "end_turn");
        h.Runtime.Register(grokId, new FakeAgentProtocolAdapter());

        (await EligibleSessionIdsAsync()).ShouldContain(grokId);

        await SweepAsync(h, FastSettings());

        (await SupervisionMessagesAsync(grokId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Unknown_and_Unsupported_kinds_are_excluded_from_the_sweep_query()
    {
        await using var h = await CreateHarnessAsync();
        var rawId = await SeedRunningSessionAsync(h, AgentKind.Raw);
        var codexId = await SeedRunningSessionAsync(h, AgentKind.Codex);
        var openCodeId = await SeedRunningSessionAsync(h, AgentKind.OpenCode);
        // Usage columns populated so IsEligibleFromStoreAsync would fire if the query leaked them.
        await SeedIdleFullAsync(rawId);
        await SeedIdleFullAsync(codexId);
        await SeedIdleFullAsync(openCodeId);
        h.Runtime.Register(rawId, new FakeAgentProtocolAdapter());
        h.Runtime.Register(codexId, new FakeAgentProtocolAdapter());
        h.Runtime.Register(openCodeId, new FakeAgentProtocolAdapter());

        var eligible = await EligibleSessionIdsAsync();
        eligible.ShouldNotContain(rawId);
        eligible.ShouldNotContain(codexId);
        eligible.ShouldNotContain(openCodeId);

        await SweepAsync(h, FastSettings());

        (await SupervisionMessagesAsync(rawId)).ShouldBeEmpty();
        (await SupervisionMessagesAsync(codexId)).ShouldBeEmpty();
        (await SupervisionMessagesAsync(openCodeId)).ShouldBeEmpty();
    }

    [Test]
    public void Compact_submission_recognises_wrapper_and_raw_typed_line()
    {
        var wrapper =
            "<command-name>/compact</command-name>\n<command-message>compact</command-message>";
        ContextCompactionService.IsCompactSubmission(TranscriptKinds.UserPrompt, wrapper).ShouldBeTrue();
        ContextCompactionService.IsCompactSubmission(
            TranscriptKinds.UserPrompt, "/compact Focus the summary on: current task state")
            .ShouldBeTrue();
        ContextCompactionService.IsCompactSubmission(TranscriptKinds.UserPrompt, "/compact").ShouldBeTrue();
        ContextCompactionService.IsCompactSubmission(
            TranscriptKinds.UserPrompt, "/compacting is broken").ShouldBeFalse();
        ContextCompactionService.IsCompactSubmission(TranscriptKinds.AssistantText, wrapper).ShouldBeFalse();
    }

    private static ContextCompactionSettings FastSettings() => new()
    {
        Enabled = true,
        IdleMinutes = 1,
        ContextPercent = 50,
        CooldownHours = 24,
        BoundaryTimeoutMinutes = 10,
    };

    private static async Task SweepAsync(BridgeQueueHarness h, ContextCompactionSettings settings)
    {
        var service = new ContextCompactionService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            h.Queue,
            h.Runtime,
            Options.Create(settings),
            Options.Create(new ContextWindowSettings()),
            TimeProvider.System,
            NullLogger<ContextCompactionService>.Instance);
        await service.SweepAsync(CancellationToken.None);
    }

    private static async Task SeedIdleFullAsync(Guid sessionId, int hoursAgo = 3)
    {
        // Usage first, TurnEnd last: fullness reads the usage row, IsWorkingAsync reads idle.
        await SeedUsageAsync(sessionId, TranscriptKinds.AssistantText, tokens: 120_000, hoursAgo);
        await SeedUsageAsync(sessionId, TranscriptKinds.TurnEnd, tokens: null, hoursAgo, stopReason: "end_turn");
    }

    private static async Task SeedUsageAsync(
        Guid sessionId, string kind, int? tokens, int hoursAgo, string? text = null, string? stopReason = null)
    {
        await using var db = CreateContext();
        var seq = ((await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        var at = DateTime.UtcNow.AddHours(-hoursAgo);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = seq,
            Kind = kind,
            Text = text,
            StopReason = stopReason,
            InputTokens = tokens,
            OutputTokens = 0,
            CacheReadTokens = 0,
            CacheCreationTokens = 0,
            Timestamp = at,
            CreatedAt = at,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedBoundaryAsync(Guid sessionId, int hoursAgo, bool manual)
    {
        await using var db = CreateContext();
        var seq = ((await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        var at = hoursAgo == 0 ? DateTime.UtcNow : DateTime.UtcNow.AddHours(-hoursAgo);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = seq,
            Kind = TranscriptKinds.CompactBoundary,
            Text = manual
                ? $"Context compacted {TranscriptKinds.ManualCompactMarker}"
                : "Context compacted (auto)",
            Timestamp = at,
            CreatedAt = at,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSentSupervisionAsync(Guid sessionId, DateTime sentAtUtc)
    {
        await using var db = CreateContext();
        var seq = ((await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId)
            .MaxAsync(m => (long?)m.Sequence)) ?? 0) + 1;
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Body = ContextCompactionService.CompactTriggerBody,
            Status = QueuedMessageStatus.Sent,
            Sequence = seq,
            Origin = QueuedMessageOrigin.Supervision,
            CreatedAt = sentAtUtc,
            SentAt = sentAtUtc,
            DeliveryAttempts = 1,
            LastDeliveryBaselineSequence = 0,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<Guid>> EligibleSessionIdsAsync()
    {
        await using var db = CreateContext();
        return await ContextCompactionService
            .WhereEligibleForContextWindow(db.AgentSessions.AsNoTracking())
            .Select(s => s.Id)
            .ToListAsync();
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

    private static async Task<(Guid SessionId, FakeAgentProtocolAdapter Adapter)> SeedUnclaimedLiveSessionAsync(
        BridgeQueueHarness h)
    {
        var sessionId = Guid.NewGuid();
        var cwd = Path.Combine(h.TempRoot, $"unclaimed-{sessionId:N}");
        Directory.CreateDirectory(cwd);
        await using (var db = CreateContext())
        {
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = cwd,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            await db.SaveChangesAsync();
        }

        var adapter = new FakeAgentProtocolAdapter();
        adapter.OnSubmitted = submitted => InsertPromptAsync(sessionId, submitted);
        h.Runtime.Register(sessionId, adapter);
        return (sessionId, adapter);
    }

    private static async Task InsertPromptAsync(Guid sessionId, string body)
    {
        await using var db = CreateContext();
        var seq = ((await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = seq,
            Kind = TranscriptKinds.UserPrompt,
            Text = body,
            Timestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = seq + 1,
            Kind = TranscriptKinds.TurnEnd,
            StopReason = "end_turn",
            Timestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<SessionQueuedMessage>> SupervisionMessagesAsync(Guid sessionId)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId && m.Origin == QueuedMessageOrigin.Supervision)
            .ToListAsync();
    }
}
