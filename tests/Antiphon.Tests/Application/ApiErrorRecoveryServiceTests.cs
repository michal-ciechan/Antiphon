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
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0072 S5a — adopt + fire of a dead API-error turn. Shared-Postgres rules: every assertion
/// is scoped to a row this test created. The class takes <c>[NotInParallel]</c> with NO group key
/// because SweepAsync walks every recent IsApiError TurnEnd in the database.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class ApiErrorRecoveryServiceTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static Task<BridgeQueueHarness> CreateHarnessAsync() =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions { AlwaysOn = true });

    [Test]
    public async Task A_stub_adopts_once_and_a_resweep_does_not_double_insert()
    {
        await using var h = await CreateHarnessAsync();
        var seq = await SeedTransientStubAsync(h.SessionId);

        await SweepAsync(h);
        await SweepAsync(h);

        await using var db = CreateContext();
        var row = (await db.ApiErrorRecoveries
                .Where(r => r.AgentSessionId == h.SessionId)
                .ToListAsync())
            .ShouldHaveSingleItem();
        row.StubSequence.ShouldBe(seq);
        row.Classification.ShouldBe(ApiErrorClassification.Transient);
        row.AttemptCount.ShouldBe(0);
        row.NextAttemptAt.ShouldNotBeNull();
        row.ResolvedAt.ShouldBeNull();
    }

    [Test]
    public async Task AssistantText_plus_TurnEnd_from_one_jsonl_line_schedules_once()
    {
        await using var h = await CreateHarnessAsync();
        await h.InsertApiErrorStubAsync(
            errorText: "API Error: 529 Overloaded.",
            apiErrorClass: "server_error",
            apiErrorStatus: 529);

        await SweepAsync(h);

        await using var db = CreateContext();
        var rows = await db.ApiErrorRecoveries.Where(r => r.AgentSessionId == h.SessionId).ToListAsync();
        rows.ShouldHaveSingleItem("one JSONL line becomes two rows; the TurnEnd sequence is the key");
        var turnEnd = await db.TranscriptEntries.SingleAsync(
            t => t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.TurnEnd);
        rows[0].StubSequence.ShouldBe(turnEnd.Sequence);
    }

    [Test]
    public async Task A_benign_is_api_error_false_synthetic_is_never_adopted()
    {
        await using var h = await CreateHarnessAsync();
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd, stopReason: "end_turn", isApiError: false);

        await SweepAsync(h);

        await using var db = CreateContext();
        (await db.ApiErrorRecoveries.CountAsync(r => r.AgentSessionId == h.SessionId)).ShouldBe(0);
    }

    [Test]
    public async Task A_stub_older_than_the_window_is_not_adopted()
    {
        await using var h = await CreateHarnessAsync();
        await using (var db = CreateContext())
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Sequence = 1,
                Kind = TranscriptKinds.TurnEnd,
                StopReason = "stop_sequence",
                IsApiError = true,
                ApiErrorClass = "server_error",
                ApiErrorStatus = 529,
                CreatedAt = DateTime.UtcNow.AddHours(-6),
            });
            await db.SaveChangesAsync();
        }

        await SweepAsync(h, new ApiErrorRecoverySettings { AdoptWindowMinutes = 60 });

        await using var verify = CreateContext();
        (await verify.ApiErrorRecoveries.CountAsync(r => r.AgentSessionId == h.SessionId)).ShouldBe(0);
    }

    [Test]
    public async Task NeedsHuman_never_schedules()
    {
        await using var h = await CreateHarnessAsync();
        await SeedStubAsync(h.SessionId, "authentication_failed", status: null,
            text: "Login expired · Please run /login");

        await SweepAsync(h);

        await using var db = CreateContext();
        var row = (await db.ApiErrorRecoveries.Where(r => r.AgentSessionId == h.SessionId).ToListAsync())
            .ShouldHaveSingleItem();
        row.Classification.ShouldBe(ApiErrorClassification.NeedsHuman);
        row.NextAttemptAt.ShouldBeNull();
        row.ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.NeedsHuman);
        row.ResolvedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Wall_enters_at_the_30_minute_rung()
    {
        await using var h = await CreateHarnessAsync();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await h.InsertApiErrorStubAsync();

        await SweepAsync(h, time: time);

        await using var db = CreateContext();
        var row = (await db.ApiErrorRecoveries.Where(r => r.AgentSessionId == h.SessionId).ToListAsync())
            .ShouldHaveSingleItem();
        row.Classification.ShouldBe(ApiErrorClassification.Wall);
        row.NextAttemptAt.ShouldNotBeNull();
        (row.NextAttemptAt!.Value - row.DetectedAt)
            .ShouldBe(TimeSpan.FromMinutes(ApiErrorRetrySchedule.WallEntryRungMinutes));
    }

    [Test]
    public async Task Fires_at_the_rung_and_not_before_through_enqueue_only()
    {
        await using var h = await CreateHarnessAsync();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await SeedTransientStubAsync(h.SessionId);

        await SweepAsync(h, time: time);
        (await SupervisionMessagesAsync(h.SessionId)).ShouldBeEmpty("not due yet");

        time.Advance(TimeSpan.FromMinutes(1));
        await SweepAsync(h, time: time);

        var sent = (await SupervisionMessagesAsync(h.SessionId)).ShouldHaveSingleItem();
        sent.Origin.ShouldBe(QueuedMessageOrigin.Supervision);
        sent.Body.ShouldBe(new ApiErrorRecoverySettings().TransientPrompt);

        await SweepAsync(h, time: time);
        (await SupervisionMessagesAsync(h.SessionId)).Count.ShouldBe(1, "a due row advances; it does not re-enqueue");
    }

    [Test]
    public async Task A_later_user_prompt_resolves_superseded_and_does_not_fire()
    {
        await using var h = await CreateHarnessAsync();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await SeedTransientStubAsync(h.SessionId);
        await SweepAsync(h, time: time);

        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "Continue");
        time.Advance(TimeSpan.FromMinutes(1));
        await SweepAsync(h, time: time);

        await using var db = CreateContext();
        var row = await db.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == h.SessionId);
        row.ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.Superseded);
        row.NextAttemptAt.ShouldBeNull();
        (await SupervisionMessagesAsync(h.SessionId)).ShouldBeEmpty();
    }

    [Test]
    public async Task A_non_running_session_is_skipped()
    {
        await using var h = await CreateHarnessAsync();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await SeedTransientStubAsync(h.SessionId);
        await SweepAsync(h, time: time);

        await using (var db = CreateContext())
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
        }

        time.Advance(TimeSpan.FromMinutes(1));
        await SweepAsync(h, time: time);

        (await SupervisionMessagesAsync(h.SessionId)).ShouldBeEmpty();
        await using var verify = CreateContext();
        var row = await verify.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == h.SessionId);
        row.ResolvedAt.ShouldBeNull("SessionRestartBoundary owns relaunches; we do not resolve a skip");
        row.AttemptCount.ShouldBe(0);
    }

    [Test]
    public async Task Unknown_parks_at_three_attempts()
    {
        await using var h = await CreateHarnessAsync();
        // A resume that landed as a UserPrompt is Superseded, not a failed attempt. The cap is
        // for three deliveries that did NOT continue the conversation.
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await SeedStubAsync(h.SessionId, apiErrorClass: "mystery", status: null, text: "something new");

        await SweepAsync(h, time: time);
        for (var i = 0; i < 3; i++)
        {
            time.Advance(TimeSpan.FromHours(2));
            await SweepAsync(h, time: time);
        }

        await using var db = CreateContext();
        var row = await db.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == h.SessionId);
        row.Classification.ShouldBe(ApiErrorClassification.Unknown);
        row.AttemptCount.ShouldBe(3);
        row.ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.UnknownExhausted);
        row.NextAttemptAt.ShouldBeNull();
        (await SupervisionMessagesAsync(h.SessionId)).Count.ShouldBe(3);
    }

    [Test]
    public async Task Wall_parks_after_three_deaths()
    {
        await using var h = await CreateHarnessAsync();
        for (var i = 0; i < 3; i++)
            await h.InsertApiErrorStubAsync();

        await SweepAsync(h);

        await using var db = CreateContext();
        var rows = await db.ApiErrorRecoveries
            .Where(r => r.AgentSessionId == h.SessionId)
            .OrderBy(r => r.StubSequence)
            .ToListAsync();
        rows.Count.ShouldBe(3);
        rows[0].ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.Replaced);
        rows[1].ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.Replaced);
        rows[2].ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.WallParked);
        rows[2].NextAttemptAt.ShouldBeNull();
    }

    [Test]
    public async Task Dead_time_incident_is_warning_unless_channel_bound()
    {
        await using var h = await CreateHarnessAsync();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await SeedTransientStubAsync(h.SessionId);
        await SweepAsync(h, time: time);

        time.Advance(TimeSpan.FromHours(3));
        await SweepAsync(h, time: time);

        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId
                    && i.Kind == AgentIncidentKind.ApiErrorTurnDied
                    && i.FailureReason == ApiErrorRecoveryService.DeadTimeFailureReason)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.SessionId.ShouldBe(h.SessionId);
    }

    [Test]
    public async Task Dead_time_incident_is_critical_when_channel_bound()
    {
        await using var h = await CreateHarnessAsync();
        await using (var db = CreateContext())
        {
            db.ChatChannels.Add(new ChatChannel
            {
                Id = Guid.NewGuid(),
                Provider = "telegram",
                ExternalId = $"chat-{Guid.NewGuid():N}",
                Kind = ChatChannelKind.Direct,
                AgentId = h.AgentId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await SeedTransientStubAsync(h.SessionId);
        await SweepAsync(h, time: time);
        time.Advance(TimeSpan.FromHours(3));
        await SweepAsync(h, time: time);

        await using var verify = CreateContext();
        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId
                && i.Kind == AgentIncidentKind.ApiErrorTurnDied
                && i.FailureReason == ApiErrorRecoveryService.DeadTimeFailureReason);
        incident.Severity.ShouldBe(AlertSeverity.Critical);
    }

    [Test]
    public async Task A_taskless_session_is_detected_by_the_sweep()
    {
        await using var h = await CreateHarnessAsync();
        await SeedTransientStubAsync(h.SessionId);

        await using (var db = CreateContext())
            (await db.AgentTasks.CountAsync(t => t.AgentSessionId == h.SessionId)).ShouldBe(0);

        await SweepAsync(h);

        await using var verify = CreateContext();
        (await verify.ApiErrorRecoveries.CountAsync(r => r.AgentSessionId == h.SessionId))
            .ShouldBe(1, "orchestrators and channel-bound agents have no AgentTask; the sweep is their first detection");
        var incident = (await verify.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ApiErrorTurnDied)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.SessionId.ShouldBe(h.SessionId);
        incident.Message.ShouldContain("timed resume");
    }

    private static ApiErrorRecoverySettings FastSettings() => new()
    {
        Enabled = true,
        SweepPeriodSeconds = 60,
        AdoptWindowMinutes = 180,
        UnknownAttemptCap = 3,
        DeadTimeWarningHours = 2,
        WallDeathCap = 3,
    };

    private static async Task SweepAsync(
        BridgeQueueHarness h, ApiErrorRecoverySettings? settings = null, TimeProvider? time = null)
    {
        var service = new ApiErrorRecoveryService(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            h.Queue,
            h.Runtime,
            Options.Create(new SupervisionSettings { ApiErrorRecovery = settings ?? FastSettings() }),
            time ?? TimeProvider.System,
            NullLogger<ApiErrorRecoveryService>.Instance);
        await service.SweepAsync(CancellationToken.None);
    }

    private static async Task<long> SeedTransientStubAsync(Guid sessionId) =>
        await SeedStubAsync(sessionId, "server_error", 529, "API Error: 529 Overloaded.");

    private static async Task<long> SeedStubAsync(
        Guid sessionId, string apiErrorClass, int? status, string text)
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
            Kind = TranscriptKinds.TurnEnd,
            StopReason = "stop_sequence",
            Text = text,
            IsApiError = true,
            ApiErrorClass = apiErrorClass,
            ApiErrorStatus = status,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return seq;
    }

    private static async Task<List<SessionQueuedMessage>> SupervisionMessagesAsync(Guid sessionId)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId && m.Origin == QueuedMessageOrigin.Supervision)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }
}
