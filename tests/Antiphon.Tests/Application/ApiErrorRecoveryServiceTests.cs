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
    public async Task Session_limit_stub_schedules_one_resume_at_reset_plus_padding()
    {
        await using var h = await CreateHarnessAsync();
        await using (var stamp = CreateContext())
        {
            await stamp.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.EffectiveModelId, "fable"));
        }
        var now = new DateTimeOffset(2026, 7, 15, 16, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        await SeedStubAsync(h.SessionId, "rate_limit", 429, UsageLimitWallParser.SessionLimitFixtureText);

        await SweepAsync(h, time: time);

        await using var db = CreateContext();
        var row = (await db.ApiErrorRecoveries.Where(r => r.AgentSessionId == h.SessionId).ToListAsync())
            .ShouldHaveSingleItem();
        row.Classification.ShouldBe(ApiErrorClassification.Wall);
        row.ResolvedAt.ShouldBeNull();
        row.NextAttemptAt.ShouldBe(new DateTime(2026, 7, 15, 17, 12, 0, DateTimeKind.Utc));

        var hold = (await db.ModelAvailabilityHolds
                .Where(x => x.SourceSessionId == h.SessionId && x.ClearedAt == null)
                .ToListAsync())
            .ShouldHaveSingleItem();
        hold.ModelAlias.ShouldNotBe("<synthetic>");
        hold.DisabledUntil.ShouldBe(row.NextAttemptAt);
        hold.Source.ShouldBe(ModelAvailabilitySource.AutoDetected);
    }

    [Test]
    public async Task Claude_production_shape_session_limit_uses_AssistantText_not_the_6h_fallback()
    {
        // CARD-0401: measured 2026-09-05 — error string on AssistantText, TurnEnd.Text null,
        // shared uuid, both IsApiError, rate_limit/429. Sweep used to parse "" and write 6h.
        var now = new DateTimeOffset(2026, 9, 5, 15, 16, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        await using var h = await BridgeQueueHarness.CreateAsync(
            new BridgeQueueHarness.HarnessOptions { AlwaysOn = true, TimeProvider = time });
        await using (var stamp = CreateContext())
        {
            await stamp.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.EffectiveModelId, "fable"));
        }

        var text = UsageLimitWallParser.SessionLimitProductionText;
        text.Length.ShouldBe(61);
        await SeedProductionClaudeStubAsync(h.SessionId, text);

        await SweepAsync(h, time: time);

        await using var db = CreateContext();
        var row = (await db.ApiErrorRecoveries.Where(r => r.AgentSessionId == h.SessionId).ToListAsync())
            .ShouldHaveSingleItem();
        row.Classification.ShouldBe(ApiErrorClassification.Wall);
        row.ResolvedAt.ShouldBeNull();
        row.ResolvedReason.ShouldBeNull();
        var resetPlusPadding = new DateTime(2026, 9, 5, 16, 22, 0, DateTimeKind.Utc);
        row.NextAttemptAt.ShouldBe(resetPlusPadding);

        var hold = (await db.ModelAvailabilityHolds
                .Where(x => x.SourceSessionId == h.SessionId && x.ClearedAt == null)
                .ToListAsync())
            .ShouldHaveSingleItem();
        hold.RawText.ShouldBe(text);
        hold.Reason.ShouldBe("session-limit resets 17:20 Europe/London");
        hold.DisabledUntil.ShouldBe(resetPlusPadding);
        hold.DisabledUntil.ShouldNotBe(now.UtcDateTime.AddHours(6));
        hold.ModelAlias.ShouldBe("fable");
        await db.ModelAvailabilityHolds.Where(x => x.Id == hold.Id).ExecuteDeleteAsync();
    }

    [Test]
    public async Task Codex_TurnEnd_text_without_AssistantText_still_parses_session_limit()
    {
        var now = new DateTimeOffset(2026, 9, 5, 15, 16, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        await using var h = await BridgeQueueHarness.CreateAsync(
            new BridgeQueueHarness.HarnessOptions { AlwaysOn = true, TimeProvider = time });
        await using (var stamp = CreateContext())
        {
            await stamp.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.EffectiveModelId, "fable"));
        }

        await SeedStubAsync(h.SessionId, "rate_limit", 429, UsageLimitWallParser.SessionLimitProductionText);
        await SweepAsync(h, time: time);

        await using var db = CreateContext();
        var hold = (await db.ModelAvailabilityHolds
                .Where(x => x.SourceSessionId == h.SessionId && x.ClearedAt == null)
                .ToListAsync())
            .ShouldHaveSingleItem();
        hold.RawText.ShouldBe(UsageLimitWallParser.SessionLimitProductionText);
        hold.Reason.ShouldBe("session-limit resets 17:20 Europe/London");
        hold.DisabledUntil.ShouldBe(new DateTime(2026, 9, 5, 16, 22, 0, DateTimeKind.Utc));
        await db.ModelAvailabilityHolds.Where(x => x.Id == hold.Id).ExecuteDeleteAsync();
    }

    [Test]
    public async Task Empty_wall_adopt_is_repaired_when_a_later_call_supplies_the_real_text()
    {
        var now = new DateTimeOffset(2026, 9, 5, 15, 16, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        await using var h = await BridgeQueueHarness.CreateAsync(
            new BridgeQueueHarness.HarnessOptions { AlwaysOn = true, TimeProvider = time });
        await using (var stamp = CreateContext())
        {
            await stamp.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.EffectiveModelId, "fable"));
        }

        var text = UsageLimitWallParser.SessionLimitProductionText;
        var (seq, uuid) = await SeedProductionClaudeStubAsync(h.SessionId, text);
        var svc = Recovery(h, time: time);

        await svc.EnsureAdoptedAsync(
            h.SessionId, seq, uuid, "rate_limit", 429, errorText: null, CancellationToken.None);

        await using (var first = CreateContext())
        {
            var paused = await first.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == h.SessionId);
            paused.ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.WallModelPaused);
            var sixHour = (await first.ModelAvailabilityHolds
                    .Where(x => x.SourceSessionId == h.SessionId && x.ClearedAt == null)
                    .ToListAsync())
                .ShouldHaveSingleItem();
            sixHour.DisabledUntil.ShouldBe(now.UtcDateTime.AddHours(6));
            sixHour.RawText.ShouldBeNullOrWhiteSpace();
            sixHour.Reason.ShouldContain("per-model cap");
        }

        await svc.EnsureAdoptedAsync(
            h.SessionId, seq, uuid, "rate_limit", 429, text, CancellationToken.None);

        await using var db = CreateContext();
        var row = await db.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == h.SessionId);
        row.ResolvedAt.ShouldBeNull();
        row.ResolvedReason.ShouldBeNull();
        var resetPlusPadding = new DateTime(2026, 9, 5, 16, 22, 0, DateTimeKind.Utc);
        row.NextAttemptAt.ShouldBe(resetPlusPadding);

        var hold = await db.ModelAvailabilityHolds.SingleAsync(
            x => x.SourceSessionId == h.SessionId && x.ClearedAt == null);
        hold.RawText.ShouldBe(text);
        hold.Reason.ShouldBe("session-limit resets 17:20 Europe/London");
        hold.DisabledUntil.ShouldBe(resetPlusPadding);
        await db.ModelAvailabilityHolds.Where(x => x.Id == hold.Id).ExecuteDeleteAsync();
    }

    [Test]
    public async Task Fable_5_stub_writes_a_fallback_hold_and_does_not_enqueue()
    {
        var now = TruncateUtcNow();
        var time = new FakeTimeProvider(now);
        await using var h = await BridgeQueueHarness.CreateAsync(
            new BridgeQueueHarness.HarnessOptions { AlwaysOn = true, TimeProvider = time });
        await SeedStubAsync(h.SessionId, "rate_limit", 429, UsageLimitWallParser.FableModelCapIncidentText);

        var settings = FastSettings();
        settings.ModelCapFallbackHoldHours = 3;
        await SweepAsync(h, settings, time);

        await using var db = CreateContext();
        var row = await db.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == h.SessionId);
        row.Classification.ShouldBe(ApiErrorClassification.Wall);
        row.ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.WallModelPaused);
        row.NextAttemptAt.ShouldBeNull();

        var hold = await db.ModelAvailabilityHolds.SingleAsync(
            x => x.SourceSessionId == h.SessionId && x.ClearedAt == null);
        hold.ModelAlias.ShouldBe("fable");
        hold.Kind.ShouldBe(AgentKind.ClaudeCode);
        hold.DisabledUntil.ShouldBe(now.UtcDateTime.AddHours(3));
        hold.Source.ShouldBe(ModelAvailabilitySource.AutoDetected);

        (await SupervisionMessagesAsync(h.SessionId)).ShouldBeEmpty();
        await db.ModelAvailabilityHolds.Where(x => x.Id == hold.Id).ExecuteDeleteAsync();
    }

    [Test]
    public async Task Grok_402_stub_writes_a_fallback_hold_for_grok_4_6_and_never_enqueues()
    {
        var now = TruncateUtcNow();
        var time = new FakeTimeProvider(now);
        await using var h = await BridgeQueueHarness.CreateAsync(
            new BridgeQueueHarness.HarnessOptions { AlwaysOn = true, TimeProvider = time });
        await using (var stamp = CreateContext())
        {
            await stamp.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.AgentKind, AgentKind.Grok)
                    .SetProperty(s => s.EffectiveModelId, "grok-4.6"));
        }

        await SeedStubAsync(
            h.SessionId,
            "payment_required",
            402,
            "API error (status 402 Payment Required): Grok Build usage balance exhausted");

        var settings = FastSettings();
        settings.ModelCapFallbackHoldHours = 3;
        await SweepAsync(h, settings, time);

        await using var db = CreateContext();
        var row = await db.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == h.SessionId);
        row.Classification.ShouldBe(ApiErrorClassification.Wall);
        row.ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.WallModelPaused);
        row.NextAttemptAt.ShouldBeNull();

        var hold = await db.ModelAvailabilityHolds.SingleAsync(
            x => x.SourceSessionId == h.SessionId && x.ClearedAt == null);
        hold.Kind.ShouldBe(AgentKind.Grok);
        hold.ModelAlias.ShouldBe("grok-4.6");
        hold.DisabledUntil.ShouldBe(now.UtcDateTime.AddHours(3));
        hold.Source.ShouldBe(ModelAvailabilitySource.AutoDetected);
        hold.Reason.ShouldContain("provider capacity");
        hold.Reason.ShouldContain("HTTP 402");

        (await SupervisionMessagesAsync(h.SessionId)).ShouldBeEmpty();

        await db.ModelAvailabilityHolds.Where(x => x.Id == hold.Id).ExecuteDeleteAsync();
    }

    [Test]
    public async Task Auto_detected_writer_does_not_shorten_a_manual_DisabledUntil()
    {
        await using var h = await CreateHarnessAsync();
        var thursday = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = CreateContext())
        {
            await db.ModelAvailabilityHolds
                .Where(h => h.Kind == AgentKind.ClaudeCode && h.ModelAlias == "fable" && h.ClearedAt == null)
                .ExecuteDeleteAsync();
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = Guid.NewGuid(),
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "fable",
                Source = ModelAvailabilitySource.Manual,
                DisabledUntil = thursday,
                HitAt = DateTime.UtcNow.AddHours(-1),
                Reason = "manual hold",
            });
            await db.SaveChangesAsync();
        }

        await SeedStubAsync(h.SessionId, "rate_limit", 429, UsageLimitWallParser.FableModelCapIncidentText);
        await SweepAsync(h);

        await using var verify = CreateContext();
        var hold = await verify.ModelAvailabilityHolds.SingleAsync(
            x => x.Kind == AgentKind.ClaudeCode && x.ModelAlias == "fable" && x.ClearedAt == null);
        hold.Source.ShouldBe(ModelAvailabilitySource.Manual);
        hold.DisabledUntil.ShouldBe(thursday);
        hold.SourceSessionId.ShouldBe(h.SessionId);
        await verify.ModelAvailabilityHolds.Where(x => x.Id == hold.Id).ExecuteDeleteAsync();
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
            await SeedStubAsync(h.SessionId, "rate_limit", 429, UsageLimitWallParser.SessionLimitFixtureText);

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

    private static DateTimeOffset TruncateUtcNow()
    {
        var n = DateTime.UtcNow;
        return new DateTimeOffset(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second, TimeSpan.Zero);
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
        BridgeQueueHarness h, ApiErrorRecoverySettings? settings = null, TimeProvider? time = null) =>
        await Recovery(h, settings, time).SweepAsync(CancellationToken.None);

    private static ApiErrorRecoveryService Recovery(
        BridgeQueueHarness h, ApiErrorRecoverySettings? settings = null, TimeProvider? time = null) =>
        new(
            h.Provider.GetRequiredService<IServiceScopeFactory>(),
            h.Queue,
            h.Runtime,
            Options.Create(new SupervisionSettings { ApiErrorRecovery = settings ?? FastSettings() }),
            time ?? TimeProvider.System,
            NullLogger<ApiErrorRecoveryService>.Instance);

    private static async Task<long> SeedTransientStubAsync(Guid sessionId) =>
        await SeedStubAsync(sessionId, "server_error", 529, "API Error: 529 Overloaded.");

    /// <summary>
    /// Codex / pre-CARD-0401 shape: error string on the TurnEnd row itself. Claude production
    /// is <see cref="SeedProductionClaudeStubAsync"/>.
    /// </summary>
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

    /// <summary>
    /// CARD-0401 production Claude shape: error text on AssistantText, TurnEnd.Text null,
    /// both rows share a uuid and IsApiError=true.
    /// </summary>
    private static async Task<(long TurnEndSeq, string Uuid)> SeedProductionClaudeStubAsync(
        Guid sessionId, string text)
    {
        await using var db = CreateContext();
        var seq = ((await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        var uuid = Guid.NewGuid().ToString("D");
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = seq,
            Kind = TranscriptKinds.AssistantText,
            Uuid = uuid,
            Role = "assistant",
            Text = text,
            IsApiError = true,
            ApiErrorClass = "rate_limit",
            ApiErrorStatus = 429,
            CreatedAt = DateTime.UtcNow,
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = seq + 1,
            Kind = TranscriptKinds.TurnEnd,
            Uuid = uuid,
            Role = "assistant",
            Text = null,
            StopReason = "stop_sequence",
            IsApiError = true,
            ApiErrorClass = "rate_limit",
            ApiErrorStatus = 429,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (seq + 1, uuid);
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
