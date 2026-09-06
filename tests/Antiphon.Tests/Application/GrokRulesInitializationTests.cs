using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Services;
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

[Category("Integration")]
public sealed class GrokRulesInitializationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Native_split_ack_and_late_owning_prompt_release_once_after_successful_turn(bool latePrompt)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Rules.ReconcileAsync(fixture.Id, CancellationToken.None);
        await using var db = fixture.Db();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == fixture.Id);
        row.DeliveryAttempts = 1; row.LastDeliveryBaselineSequence = 0;
        await db.SaveChangesAsync();
        var normalizer = new Antiphon.SessionRunner.GrokTranscriptNormalizer();
        var promptId = Guid.NewGuid().ToString("D");
        var ack = $"ANTIPHON_RULES_ACK id={row.Id:N} generation={fixture.Receipt.Generation:N} sha256={fixture.Receipt.Sha256}";
        var user = normalizer.Normalize(Native(new { sessionUpdate = "user_message_chunk", content = new { type = "text", text = row.Body } })).ShouldHaveSingleItem();
        if (!latePrompt) Persist(1, user);
        foreach (var chunk in new[] { ack[..20], ack[20..70], ack[70..] })
            normalizer.Normalize(Native(new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = chunk } })).ShouldBeEmpty();
        var final = normalizer.Normalize(Native(new { sessionUpdate = "turn_completed", prompt_id = promptId, stop_reason = "end_turn" }));
        final.Single(p => p.Kind == TranscriptKinds.AssistantText).Text.ShouldBe(ack);
        long sequence = 2;
        foreach (var part in final) Persist(sequence++, part);
        await db.SaveChangesAsync();
        await fixture.Rules.ReconcileAsync(fixture.Id, CancellationToken.None);
        if (latePrompt)
        {
            (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == fixture.Id)).GrokRulesState.ShouldBe(GrokRulesState.Pending);
            Persist(1, user); await db.SaveChangesAsync();
            await fixture.Rules.ReconcileAsync(fixture.Id, CancellationToken.None);
        }
        await fixture.Rules.ReconcileAsync(fixture.Id, CancellationToken.None);
        await db.Entry(row).ReloadAsync();
        row.RulesAcknowledgedAt.ShouldNotBeNull(); row.RulesPromptSequence.ShouldBe(1);
        row.DeliveryAttempts.ShouldBe(1);
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == fixture.Id)).ShouldBe(1);
        (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == fixture.Id)).GrokRulesState.ShouldBe(GrokRulesState.Ready);

        string Native(object update) => JsonSerializer.Serialize(new { method = "session/update", @params = new
        { sessionId = fixture.Id.ToString("D"), update, _meta = new { eventId = Guid.NewGuid().ToString("D"), promptId } } });
        void Persist(long seq, Antiphon.SessionRunner.TranscriptPart part) => db.TranscriptEntries.Add(new()
        {
            Id = Guid.NewGuid(), AgentSessionId = fixture.Id, Sequence = seq, Kind = part.Kind,
            Text = part.Text, Uuid = part.Uuid, StopReason = part.StopReason, CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Exhausted_delivery_fails_only_after_a_persisted_unconfirmed_verdict(bool inFlight)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Rules.ReconcileAsync(fixture.Id, CancellationToken.None);
        await using var db = fixture.Db();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == fixture.Id);
        row.RulesDeadlineAt.ShouldNotBeNull("startup deadline begins at ready eligibility even before typing");
        row.DeliveryAttempts = 3;
        row.Status = inFlight ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending;
        row.DeliveryVerdict = inFlight ? null : DeliveryVerdict.NoTranscriptRecord;
        await db.SaveChangesAsync();
        await fixture.Rules.ReconcileAsync(fixture.Id, CancellationToken.None);
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == fixture.Id);
        session.GrokRulesState.ShouldBe(inFlight ? GrokRulesState.Pending : GrokRulesState.Failed);
        if (!inFlight) session.GrokRulesFailure.ShouldBe("grok_rules_initialization_failed: delivery_failed");
    }

    [Test]
    [Arguments("valid", true)]
    [Arguments("wrong_id", false)]
    [Arguments("wrong_hash", false)]
    [Arguments("wrong_generation", false)]
    [Arguments("no_prompt", false)]
    [Arguments("no_end", false)]
    [Arguments("provider_error", false)]
    [Arguments("quoted", false)]
    [Arguments("fenced", false)]
    [Arguments("tool", false)]
    [Arguments("unrelated_turn", false)]
    [Arguments("fragment", false)]
    [Arguments("user", false)]
    [Arguments("before_prompt", false)]
    public async Task Only_current_refresh_assistant_ack_with_confirmed_prompt_releases_barrier(string variant, bool released)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Rules.ReconcileAsync(fixture.Id, CancellationToken.None);
        await using var db = fixture.Db();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == fixture.Id);
        message.DeliveryAttempts = 1;
        message.LastDeliveryBaselineSequence = 0;
        message.RulesDeadlineAt = DateTime.UtcNow.AddMinutes(1);
        var ack = $"ANTIPHON_RULES_ACK id={message.Id:N} generation={fixture.Receipt.Generation:N} sha256={fixture.Receipt.Sha256}";
        if (variant == "wrong_id") ack = ack.Replace(message.Id.ToString("N"), Guid.NewGuid().ToString("N"));
        if (variant == "wrong_hash") ack = ack.Replace(fixture.Receipt.Sha256, new string('0', 64));
        if (variant == "wrong_generation") ack = ack.Replace(fixture.Receipt.Generation.ToString("N"), Guid.NewGuid().ToString("N"));
        if (variant == "quoted") ack = "> " + ack;
        if (variant == "fenced") ack = "```\n" + ack + "\n```";
        if (variant == "fragment") ack = ack[..^1];
        if (variant != "no_prompt") Add(1, TranscriptKinds.UserPrompt, message.Body);
        if (variant == "unrelated_turn") Add(2, TranscriptKinds.UserPrompt, "Unrelated user work");
        Add(variant == "before_prompt" ? 0 : 3,
            variant == "tool" ? TranscriptKinds.ToolResult : variant == "user" ? TranscriptKinds.UserPrompt : TranscriptKinds.AssistantText, ack);
        if (variant != "no_end") Add(4, TranscriptKinds.TurnEnd, null, variant == "provider_error");
        await db.SaveChangesAsync();
        await fixture.Rules.ReconcileAsync(fixture.Id, CancellationToken.None);
        await db.Entry(message).ReloadAsync();
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == fixture.Id);
        (session.GrokRulesState == GrokRulesState.Ready).ShouldBe(released, variant);
        (message.RulesAcknowledgedAt is not null).ShouldBe(released, variant);
        if (released)
        {
            message.RulesPromptSequence.ShouldBe(1);
            message.RulesTurnEndSequence.ShouldBe(4);
            message.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
            (await GrokRulesRefreshService.IsRefreshPromptAsync(db, fixture.Id, message.Body, CancellationToken.None)).ShouldBeTrue();
            await fixture.Rules.ReconcileAsync(fixture.Id, CancellationToken.None);
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == fixture.Id)).ShouldBe(1);
        }
        if (variant == "provider_error") session.GrokRulesFailure.ShouldBe("grok_rules_initialization_failed: provider_error");

        void Add(long seq, string kind, string? text, bool error = false) => db.TranscriptEntries.Add(new()
        {
            Id = Guid.NewGuid(), AgentSessionId = fixture.Id, Sequence = seq, Kind = kind,
            Text = text, CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow,
            IsApiError = error, StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
        });
    }

    [Test]
    public async Task Deadline_is_persisted_and_recreation_does_not_reset_it()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Rules.ReconcileAsync(fixture.Id, CancellationToken.None);
        await using var db = fixture.Db();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == fixture.Id);
        row.RulesDeadlineAt = DateTime.UtcNow.AddSeconds(-1);
        db.SessionQueuedMessages.Add(new() { Id = Guid.NewGuid(), AgentSessionId = fixture.Id,
            Body = "original task goal", Sequence = 2, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var recreated = new GrokRulesRefreshService(fixture.Provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            Options.Create(new GrokRulesSettings()));
        await recreated.ReconcileAsync(fixture.Id, CancellationToken.None);
        var state = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == fixture.Id);
        state.GrokRulesState.ShouldBe(GrokRulesState.Failed);
        state.GrokRulesFailure.ShouldBe("grok_rules_initialization_failed: timeout");
        (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == fixture.Id && m.RulesRefreshKey == null))
            .Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public GrokRulesReceipt Receipt { get; private set; } = null!;
        public ServiceProvider Provider { get; }
        public GrokRulesRefreshService Rules { get; }
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions());
        private Fixture()
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => Db());
            Provider = services.BuildServiceProvider();
            Rules = new(Provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, Options.Create(new GrokRulesSettings()));
        }
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            var bytes = Encoding.UTF8.GetBytes("rules\ncontent");
            f.Receipt = new($"C:\\remote runner\\instructions\\grok\\{f.Id:N}\\rules.md", GrokRulesTransport.Hash(bytes), bytes.Length, 1, Guid.NewGuid());
            await using var db = f.Db();
            db.AgentSessions.Add(new()
            {
                Id = f.Id, AgentKind = AgentKind.Grok, Status = SessionStatus.Running, Cwd = "C:\\test",
                CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
                GrokRulesGeneration = f.Receipt.Generation, GrokRulesExpectedSha256 = f.Receipt.Sha256,
                GrokRulesExpectedByteCount = f.Receipt.ByteCount, GrokRulesReceiptJson = JsonSerializer.Serialize(f.Receipt),
                GrokRulesState = GrokRulesState.Pending,
            });
            await db.SaveChangesAsync();
            return f;
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = Db();
            await db.TranscriptEntries.Where(e => e.AgentSessionId == Id).ExecuteDeleteAsync();
            await db.AgentIncidents.Where(e => e.SessionId == Id).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == Id).ExecuteDeleteAsync();
            await Provider.DisposeAsync();
        }
    }
}
