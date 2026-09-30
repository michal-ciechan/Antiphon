using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>Isolated copies of the normalized historical tails; no provider or production runner launch.</summary>
[Category("Integration")]
[NotInParallel]
public class ProviderQuotaRefusalAcceptanceTests
{
    private static readonly DateTimeOffset IncidentAt = DateTimeOffset.Parse("2026-09-25T16:34:07Z");
    private static readonly DateTime Reset = DateTime.Parse("2026-09-26T11:16:00Z").ToUniversalTime();
    private static readonly DateTime HoldUntil = Reset.AddMinutes(2);
    private const string Linux = "You’ve hit your usage limit. Visit https://chatgpt.com/codex/settings/usage to purchase more credits or try again at Sep 26th, 2026 11:16 AM.";
    private const string Windows = "Error running remote compact task: You’ve hit your usage limit. Visit https://chatgpt.com/codex/settings/usage to purchase more credits or try again at Sep 26th, 2026 12:16 PM.";

    private sealed class Scenario(IsolatedTestSchema schema, BridgeQueueHarness h, FakeTimeProvider clock, Guid taskId) : IAsyncDisposable
    {
        public BridgeQueueHarness H { get; } = h;
        public FakeTimeProvider Clock { get; } = clock;
        public Guid TaskId { get; } = taskId;
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        public async ValueTask DisposeAsync() { await h.DisposeAsync(); await schema.DisposeAsync(); }

        public async Task EmitAsync(string text, string cls = "usage_limit_exceeded", int? status = null,
            string? zone = "Etc/UTC", DateTimeOffset? at = null, bool laterPrompt = false)
        {
            var timestamp = at ?? IncidentAt;
            var marker = DelegationReportFormatter.TaskMarker(TaskId);
            var prompt = new RunnerTranscriptEvent(H.SessionId, 1, TranscriptKinds.UserPrompt,
                Guid.NewGuid().ToString("D"), null, timestamp.AddSeconds(-1), "user", marker + " Work on the card.",
                null, null, null, null, null);
            var end = new RunnerTranscriptEvent(H.SessionId, 2, TranscriptKinds.TurnEnd,
                Guid.NewGuid().ToString("D"), null, timestamp, "assistant", text,
                null, null, null, null, "end_turn", IsApiError: true,
                ApiErrorClass: cls, ApiErrorStatus: status, ApiErrorTimeZoneId: zone);
            var entries = new List<RunnerTranscriptEvent> { prompt, end };
            if (laterPrompt)
                entries.Add(prompt with { Sequence = 3, Uuid = Guid.NewGuid().ToString("D"), Text = "Continue", Timestamp = timestamp.AddSeconds(1) });
            await H.Runtime.PersistTranscriptAsync(H.SessionId, entries.Select(RunnerContractMapper.MapTranscript).ToList());
        }

        public Task SettleAsync() => H.Provider.GetRequiredService<AgentTaskReplyService>()
            .OnTurnEndAsync(H.SessionId, CancellationToken.None);

        public async Task<(AgentTask Task, ApiErrorRecovery Recovery, ModelAvailabilityHold? Hold)> ReadAsync()
        {
            await using var db = Db();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == TaskId);
            var recovery = await db.ApiErrorRecoveries.AsNoTracking().SingleAsync(r => r.AgentSessionId == H.SessionId);
            var hold = await db.ModelAvailabilityHolds.AsNoTracking().FirstOrDefaultAsync(x => x.SourceSessionId == H.SessionId);
            return (task, recovery, hold);
        }
    }

    private static async Task<Scenario> CreateAsync(AgentKind kind = AgentKind.Codex,
        string? alias = "gpt-6-sol")
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(IncidentAt);
        var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            TimeProvider = clock,
            ConfigureServices = services => services.AddSingleton<AgentTaskReplyService>(),
        });
        var id = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.AgentKind, kind)
                .SetProperty(s => s.EffectiveModelId, alias));
            db.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, AgentSessionId = h.SessionId, AgentId = h.AgentId,
                Status = AgentTaskStatus.Dispatched, Title = "Quota replay", Goal = "Complete the card",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Docs,
                ModelLevel = AgentModelLevel.High, Workspace = WorkspaceMode.Shared,
                WorkingDirectory = h.TempRoot, ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = IncidentAt.UtcDateTime.AddMinutes(-2),
                DispatchedAt = IncidentAt.UtcDateTime.AddMinutes(-1),
            });
            await db.SaveChangesAsync();
        }
        return new Scenario(schema, h, clock, id);
    }

    private static (string Text, DateTimeOffset Timestamp, string Zone, int Count) LastFixtureError(string sessionPrefix)
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "docs", "superpowers", "plans", "fixtures", "card-0719-transcript-tails.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var session = doc.RootElement.GetProperty("sessions").EnumerateArray()
            .Single(s => s.GetProperty("sessionId").GetString()!.StartsWith(sessionPrefix, StringComparison.Ordinal));
        var errors = session.GetProperty("entries").EnumerateArray()
            .Where(e => e.GetProperty("kind").GetString() == TranscriptKinds.TurnEnd
                && e.GetProperty("isApiError").ValueKind == JsonValueKind.True).ToList();
        var last = errors[^1];
        return (last.GetProperty("text").GetString()!,
            last.GetProperty("timestamp").GetDateTimeOffset(),
            session.GetProperty("replayTimeZoneId").GetString()!, errors.Count);
    }

    private static async Task AssertBlockedAsync(Scenario s, DateTime? expectedReset = null,
        DateTime? expectedHold = null)
    {
        var (task, recovery, hold) = await s.ReadAsync();
        task.Status.ShouldBe(AgentTaskStatus.Blocked);
        task.FailureCode.ShouldBe(AgentTaskFailureCode.SubscriptionQuotaExceeded);
        task.CompletedAt.ShouldBeNull();
        task.Result.ShouldBeNull();
        recovery.Classification.ShouldBe(ApiErrorClassification.Wall);
        recovery.ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.QuotaBlocked);
        recovery.NextAttemptAt.ShouldBeNull();
        recovery.ResetAtUtc.ShouldBe(expectedReset);
        if (expectedHold is not null)
        {
            hold.ShouldNotBeNull();
            hold!.DisabledUntil.ShouldBe(expectedHold);
            hold.Source.ShouldBe(ModelAvailabilitySource.AutoDetected);
            task.FailureReason.ShouldContain(expectedReset!.Value.ToString("yyyy-MM-ddTHH:mm:ss"));
            task.FailureReason.ShouldContain(expectedHold.Value.ToString("yyyy-MM-ddTHH:mm:ss"));
        }
        await using var db = s.Db();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == s.TaskId && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1);
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == s.H.SessionId && m.Origin == QueuedMessageOrigin.Supervision)).ShouldBe(0);
    }

    [Test]
    public async Task Replay_faddd3e4_blocks_without_retry()
    {
        var fixture = LastFixtureError("faddd3e4");
        fixture.Count.ShouldBeGreaterThan(1);
        await using var s = await CreateAsync();
        await s.EmitAsync(fixture.Text, zone: fixture.Zone, at: fixture.Timestamp);
        await s.SettleAsync();
        await AssertBlockedAsync(s, Reset, HoldUntil);
    }

    [Test]
    public async Task Replay_5af622ce_has_quota_attention()
    {
        var fixture = LastFixtureError("5af622ce");
        await using var s = await CreateAsync();
        await s.EmitAsync(fixture.Text, zone: fixture.Zone, at: fixture.Timestamp);
        await s.SettleAsync();
        await AssertBlockedAsync(s, Reset, HoldUntil);
        await using var db = s.Db();
        var attention = await AttentionServiceTests.BuildService(s.H.Runner, db: db, timeProvider: s.Clock)
            .GetAsync(CancellationToken.None, includeProgressProbe: false);
        var row = attention.Items.Single(i => i.TaskId == s.TaskId);
        row.Headline.ShouldContain("quota");
        row.ConditionKey.ShouldBe($"quota-blocked:{s.TaskId:N}");
    }

    [Test]
    public async Task Replay_061581e5_blocks_at_shared_reset()
    {
        var fixture = LastFixtureError("061581e5");
        fixture.Text.ShouldStartWith("Error running remote compact task:");
        await using var s = await CreateAsync();
        await s.EmitAsync(fixture.Text, zone: fixture.Zone, at: fixture.Timestamp);
        await s.SettleAsync();
        await AssertBlockedAsync(s, Reset, HoldUntil);
    }

    [Test]
    public async Task Claude_session_limit_blocks_at_clock_reset()
    {
        await using var s = await CreateAsync(AgentKind.ClaudeCode, "fable");
        await s.EmitAsync(UsageLimitWallParser.SessionLimitFixtureText, "rate_limit", 429, "Europe/London", IncidentAt);
        await s.SettleAsync();
        (await s.ReadAsync()).Task.Status.ShouldBe(AgentTaskStatus.Blocked);
    }

    [Test]
    public async Task Claude_named_model_cap_blocks_with_finite_fallback()
    {
        await using var s = await CreateAsync(AgentKind.ClaudeCode, "fable");
        await s.EmitAsync(UsageLimitWallParser.FableModelCapIncidentText, "rate_limit", 429);
        await s.SettleAsync();
        await AssertBlockedAsync(s);
        (await s.ReadAsync()).Hold!.DisabledUntil.ShouldNotBeNull();
    }

    [Test]
    public async Task Grok_credit_wall_blocks_without_invented_reset()
    {
        await using var s = await CreateAsync(AgentKind.Grok, "grok-4.7");
        await s.EmitAsync("API error (status 402 Payment Required): usage balance exhausted", "payment_required", 402);
        await s.SettleAsync();
        await AssertBlockedAsync(s);
    }

    [Test]
    public async Task Duplicate_delivery_has_one_effective_block()
    {
        await using var s = await CreateAsync();
        await s.EmitAsync(Linux);
        await s.SettleAsync();
        await s.SettleAsync();
        await AssertBlockedAsync(s, Reset, HoldUntil);
    }

    [Test]
    public async Task Manual_timed_hold_outranks_quota()
    {
        await using var s = await CreateAsync();
        var manualUntil = HoldUntil.AddHours(8);
        await using (var db = s.Db())
        {
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = Guid.NewGuid(), Kind = AgentKind.Codex, ModelAlias = "gpt-6-sol",
                Source = ModelAvailabilitySource.Manual, DisabledUntil = manualUntil,
                Reason = "operator", HitAt = IncidentAt.UtcDateTime,
            });
            await db.SaveChangesAsync();
        }
        await s.EmitAsync(Linux);
        await s.SettleAsync();
        var result = await s.ReadAsync();
        result.Task.Status.ShouldBe(AgentTaskStatus.Blocked);
        result.Recovery.ResetAtUtc.ShouldBe(Reset);
        result.Hold!.Source.ShouldBe(ModelAvailabilitySource.Manual);
        result.Hold.DisabledUntil.ShouldBe(manualUntil);
    }

    [Test]
    public async Task Manual_indefinite_hold_remains_indefinite()
    {
        await using var s = await CreateAsync();
        await using (var db = s.Db())
        {
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = Guid.NewGuid(), Kind = AgentKind.Codex, ModelAlias = "gpt-6-sol",
                Source = ModelAvailabilitySource.Manual, DisabledUntil = null,
                Reason = "operator", HitAt = IncidentAt.UtcDateTime,
            });
            await db.SaveChangesAsync();
        }
        await s.EmitAsync(Linux);
        await s.SettleAsync();
        var result = await s.ReadAsync();
        result.Task.Status.ShouldBe(AgentTaskStatus.Blocked);
        result.Hold!.Source.ShouldBe(ModelAvailabilitySource.Manual);
        result.Hold.DisabledUntil.ShouldBeNull();
    }

    [Test]
    public async Task Later_user_prompt_prevents_stale_block()
    {
        await using var s = await CreateAsync();
        await s.EmitAsync(Linux, laterPrompt: true);
        await s.SettleAsync();
        (await s.ReadAsync()).Task.Status.ShouldBe(AgentTaskStatus.Working);
    }

    [Test]
    public async Task Existing_unknown_quota_is_reclassified_before_fire()
    {
        await using var s = await CreateAsync();
        await s.EmitAsync(Linux);
        await using (var db = s.Db())
        {
            db.ApiErrorRecoveries.Add(new ApiErrorRecovery
            {
                Id = Guid.NewGuid(), AgentSessionId = s.H.SessionId, StubSequence = 2,
                Classification = ApiErrorClassification.Unknown, ApiErrorClass = "usage_limit_exceeded",
                DetectedAt = IncidentAt.UtcDateTime, ResolvedAt = IncidentAt.UtcDateTime,
                ResolvedReason = ApiErrorRecoveryReasons.UnknownExhausted,
                AttemptCount = 3,
            });
            await db.SaveChangesAsync();
        }
        await s.SettleAsync();
        await AssertBlockedAsync(s, Reset, HoldUntil);
        (await s.ReadAsync()).Recovery.AttemptCount.ShouldBe(3);
    }

    [Test]
    public async Task Operator_clear_does_not_resurrect_old_hold()
    {
        await using var s = await CreateAsync();
        await s.EmitAsync(Linux);
        await s.SettleAsync();
        var holdId = (await s.ReadAsync()).Hold!.Id;
        await using (var db = s.Db())
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId)
                .ExecuteUpdateAsync(u => u.SetProperty(h => h.ClearedAt, IncidentAt.UtcDateTime));
        }
        await s.H.Provider.GetRequiredService<ApiErrorRecoveryService>().SweepAsync(CancellationToken.None);
        await using var verify = s.Db();
        (await verify.ModelAvailabilityHolds.CountAsync(h => h.SourceSessionId == s.H.SessionId && h.ClearedAt == null)).ShouldBe(0);
    }

    [Test]
    public async Task Genuine_transient_retains_retry_schedule()
    {
        await using var s = await CreateAsync();
        await s.EmitAsync("API Error: 529 Overloaded", "server_error", 529);
        await s.SettleAsync();
        var result = await s.ReadAsync();
        result.Task.Status.ShouldBe(AgentTaskStatus.Working);
        result.Recovery.NextAttemptAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Quota_without_subscription_sample_still_holds_model()
    {
        await using var s = await CreateAsync();
        await s.EmitAsync(Linux);
        await s.SettleAsync();
        await using var db = s.Db();
        (await db.ModelAvailabilityHolds.AnyAsync(h => h.SourceSessionId == s.H.SessionId)).ShouldBeTrue();
        using var scope = s.H.Provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ModelAvailability>()
            .IsHeldAsync(AgentKind.Codex, "gpt-6-sol", CancellationToken.None)).ShouldBeTrue();
    }

    [Test]
    public async Task Sweep_after_settlement_does_not_enqueue_retry()
    {
        await using var s = await CreateAsync();
        await s.EmitAsync(Linux);
        await s.SettleAsync();
        s.Clock.Advance(TimeSpan.FromHours(2));
        await s.H.Provider.GetRequiredService<ApiErrorRecoveryService>().SweepAsync(CancellationToken.None);
        await AssertBlockedAsync(s, Reset, HoldUntil);
    }

    [Test]
    public async Task Missing_alias_or_zone_blocks_without_fabricated_reset()
    {
        await using var s = await CreateAsync(alias: null);
        await s.EmitAsync(Linux, zone: null);
        await s.SettleAsync();
        var result = await s.ReadAsync();
        result.Task.Status.ShouldBe(AgentTaskStatus.Blocked);
        result.Recovery.ResetAtUtc.ShouldBeNull();
        result.Hold.ShouldBeNull();
        result.Task.FailureReason.ShouldContain("model alias unresolved");
    }

    [Test]
    public async Task Pending_owned_retry_is_gated_while_human_prompt_remains()
    {
        await using var s = await CreateAsync();
        await s.EmitAsync(Linux);
        await s.SettleAsync();
        await s.H.Queue.EnqueueAsync(s.H.SessionId,
            $"{DelegationReportFormatter.TaskMarker(s.TaskId)} {new Antiphon.Server.Application.Settings.ApiErrorRecoverySettings().TransientPrompt}",
            MessageSendMode.WhenIdle, CancellationToken.None, origin: QueuedMessageOrigin.Supervision);
        await s.H.Queue.EnqueueAsync(s.H.SessionId, "human message", MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Ui);
        await s.H.Queue.FlushSessionAsync(s.H.SessionId, CancellationToken.None);
        await using var db = s.Db();
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == s.H.SessionId && m.Body.Contains("transient API error") && m.Status == QueuedMessageStatus.Pending)).ShouldBe(1);
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == s.H.SessionId && m.Body == "human message")).ShouldBe(1);
    }

    [Test]
    public async Task Hold_expiry_keeps_task_blocked_until_explicit_action()
    {
        await using var s = await CreateAsync();
        await s.EmitAsync(Linux);
        await s.SettleAsync();
        s.Clock.Advance(TimeSpan.FromDays(2));
        using var scope = s.H.Provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ModelAvailability>()
            .IsHeldAsync(AgentKind.Codex, "gpt-6-sol", CancellationToken.None)).ShouldBeFalse();
        (await s.ReadAsync()).Task.Status.ShouldBe(AgentTaskStatus.Blocked);
    }
}
