using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.Application;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

[Category("Integration")]
[NotInParallel("MessageQueue")]
[ClassDataSource<AbandonWebAppFactory>(Shared = SharedType.PerTestSession)]
public sealed class SessionQueueAbandonTests(AbandonWebAppFactory factory)
{
    private readonly Dictionary<Guid, FakeAgentProtocolAdapter> _adapters = [];
    [Test]
    [Arguments(SessionStatus.Running)]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    public async Task Unconfirmable_sent_row_is_visible_until_operator_abandons_it(SessionStatus status)
    {
        var row = await SeedAsync(status: status);
        using var client = factory.CreateClient();
        var queue = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{row.AgentSessionId}/messages");
        var visible = queue.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();
        visible.GetProperty("id").GetGuid().ShouldBe(row.Id);
        visible.GetProperty("status").GetString().ShouldBe("Sent");
        visible.GetProperty("deliveryUnconfirmed").GetBoolean().ShouldBeTrue();
        visible.GetProperty("deliveryAttempts").GetInt32().ShouldBe(1);
        visible.GetProperty("lastDeliveryStartedAt").GetDateTime().ShouldBe(row.LastDeliveryStartedAt!.Value);
        visible.GetProperty("body").GetString().ShouldBe(row.Body);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var feed = await scope.ServiceProvider.GetRequiredService<AttentionService>().GetAsync(default, includeProgressProbe: false);
            var held = feed.Items.Single(i => i.MessageId == row.Id);
            held.Kind.ShouldBe(AttentionKind.ParkedMessage);
            held.Actions.ShouldBe([AttentionAction.OpenAgent, AttentionAction.AbandonMessage]);
            held.Headline.ShouldContain("switching is blocked");
            held.SinceUtc.ShouldBe(row.LastDeliveryStartedAt);
        }
        using var result = await AbandonAsync(row, "operator inspected the stopped pane");
        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reply = await result.Content.ReadFromJsonAsync<JsonElement>();
        reply.GetProperty("warning").GetString()!.ShouldContain("may have been delivered");
        (await ReloadAsync(row)).Status.ShouldBe(QueuedMessageStatus.Canceled);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{row.AgentSessionId}/messages");
        after.GetProperty("messages").GetArrayLength().ShouldBe(0);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var feed = await scope.ServiceProvider.GetRequiredService<AttentionService>().GetAsync(default, includeProgressProbe: false);
            feed.Items.ShouldNotContain(i => i.MessageId == row.Id);
        }
        _adapters[row.AgentSessionId].Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Abandon_requires_operator_and_reason_and_is_idempotently_audited()
    {
        var row = await SeedAsync();
        using var client = factory.CreateClient();
        using (var refused = await client.PostAsJsonAsync(Route(row), new { reason = "explicit decision" }))
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var blank = await AbandonAsync(row, " ")) blank.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        DateTime firstAt;
        using (var first = await AbandonAsync(row, "no longer needed"))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
            firstAt = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("canceledAt").GetDateTime();
        }
        var canceled = await ReloadAsync(row);
        using (var again = await AbandonAsync(row, "a different retry reason"))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("canceledAt").GetDateTime().ShouldBe(firstAt);
        }
        (await ReloadAsync(row)).CanceledAt.ShouldBe(canceled.CanceledAt);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = (await db.AuditRecords.Where(i => i.Id == row.Id).ToListAsync()).ShouldHaveSingleItem();
        audit.Summary.ShouldContain("admin (operator token)");
        audit.Summary.ShouldContain("no longer needed");
        audit.Summary.ShouldNotContain("different retry");
        audit.CreatedAt.ShouldBe(canceled.CanceledAt!.Value);
        canceled.DeliveryAttempts.ShouldBe(1);
        canceled.DeliveryVerdict.ShouldBeNull();
    }

    [Test]
    [Arguments("confirmed")]
    [Arguments("late-confirmed")]
    [Arguments("pending")]
    [Arguments("zero-attempt")]
    [Arguments("ordinary-canceled")]
    public async Task Abandon_refuses_rows_without_an_unresolved_sent_attempt(string shape)
    {
        var row = await SeedAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = (await db.SessionQueuedMessages.FindAsync(row.Id))!;
            if (shape == "confirmed") stored.DeliveryVerdict = DeliveryVerdict.Delivered;
            if (shape == "late-confirmed") stored.DeliveryVerdict = DeliveryVerdict.LateConfirmed;
            if (shape == "pending") stored.Status = QueuedMessageStatus.Pending;
            if (shape == "zero-attempt") stored.DeliveryAttempts = 0;
            if (shape == "ordinary-canceled") stored.Status = QueuedMessageStatus.Canceled;
            await db.SaveChangesAsync();
        }
        using var refused = await AbandonAsync(row, "operator decision");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        if (shape == "pending")
        {
            using var client = factory.CreateClient();
            using var ordinary = await client.DeleteAsync($"/api/sessions/{row.AgentSessionId}/messages/{row.Id}");
            ordinary.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await ReloadAsync(row)).Status.ShouldBe(QueuedMessageStatus.Canceled);
        }
    }

    [Test]
    public async Task Another_session_cannot_abandon_the_row()
    {
        var row = await SeedAsync(); var other = await SeedAsync();
        using var result = await AbandonAsync(new SessionQueuedMessage { Id = row.Id, AgentSessionId = other.AgentSessionId }, "wrong session");
        result.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReloadAsync(row)).Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Late_prompt_after_abandon_leaves_row_terminal_and_never_types(bool refinement)
    {
        var row = await SeedAsync(status: SessionStatus.Running, refinement: refinement);
        using (var abandoned = await AbandonAsync(row, "explicitly give up waiting")) abandoned.StatusCode.ShouldBe(HttpStatusCode.OK);
        await AddReceiptAsync(row);
        var queue = factory.Services.GetRequiredService<SessionMessageQueueService>();
        await queue.ConfirmSentRefinementsAsync(row.AgentSessionId, default);
        await queue.FlushSessionAsync(row.AgentSessionId, default);
        var after = await ReloadAsync(row);
        after.Status.ShouldBe(QueuedMessageStatus.Canceled);
        after.DeliveryVerdict.ShouldBeNull();
        after.DeliveryAttempts.ShouldBe(1);
        _adapters[row.AgentSessionId].Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Grok_rules_late_prompt_does_not_resurrect_abandoned_input()
    {
        var row = await SeedRulesAsync();
        using (var abandoned = await AbandonAsync(row, "Explicitly abandon the unconfirmed rules input"))
            abandoned.StatusCode.ShouldBe(HttpStatusCode.OK);
        await AddReceiptAsync(row);
        await factory.Services.GetRequiredService<GrokRulesRefreshService>().ReconcileAsync(row.AgentSessionId, default);
        var after = await ReloadAsync(row);
        after.Status.ShouldBe(QueuedMessageStatus.Canceled);
        after.DeliveryVerdict.ShouldBeNull();
        after.RulesAcknowledgedAt.ShouldBeNull();
        after.DeliveryAttempts.ShouldBe(1);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().SessionQueuedMessages.CountAsync(m => m.AgentSessionId == row.AgentSessionId)).ShouldBe(1);
        _adapters[row.AgentSessionId].Inputs.ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Grok_rules_confirmation_races_abandon_without_resurrection(bool reconcileFirst)
    {
        var row = await SeedRulesAsync();
        await AddReceiptAsync(row);
        Task? reconcile = null; Task<HttpResponseMessage>? abandon = null;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentSessions\" WHERE \"Id\" = {row.AgentSessionId} FOR UPDATE");
            try
            {
                if (reconcileFirst) reconcile = factory.Services.GetRequiredService<GrokRulesRefreshService>().ReconcileAsync(row.AgentSessionId, default);
                else abandon = AbandonAsync(row, "Operator decision during rules receipt");
                await Should.ThrowAsync<TimeoutException>((reconcileFirst ? reconcile! : abandon!).WaitAsync(TimeSpan.FromMilliseconds(150)));
                if (reconcileFirst) abandon = AbandonAsync(row, "Operator decision after rules receipt");
                else reconcile = factory.Services.GetRequiredService<GrokRulesRefreshService>().ReconcileAsync(row.AgentSessionId, default);
            }
            finally { await tx.CommitAsync(); }
        }
        using var result = await abandon!;
        await reconcile!;
        var after = await ReloadAsync(row);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var audits = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().AuditRecords.CountAsync(i => i.Id == row.Id);
        (await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>().SessionQueuedMessages.CountAsync(m => m.AgentSessionId == row.AgentSessionId)).ShouldBe(1);
        if (after.Status == QueuedMessageStatus.Canceled)
        {
            result.StatusCode.ShouldBe(HttpStatusCode.OK);
            after.DeliveryVerdict.ShouldBeNull();
            audits.ShouldBe(1);
        }
        else
        {
            after.Status.ShouldBe(QueuedMessageStatus.Sent);
            after.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
            result.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            audits.ShouldBe(0);
        }
        after.DeliveryAttempts.ShouldBe(1);
        _adapters[row.AgentSessionId].Inputs.ShouldBeEmpty();
    }

    [Test]
    [Arguments(0)] [Arguments(1001)]
    public async Task Abandon_reason_problem_details_pin_the_exact_field_and_message(int length)
    {
        var row = await SeedAsync();
        using var result = await AbandonAsync(row, length == 0 ? "  " : new string('r', length));
        result.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        result.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await result.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("status").GetInt32().ShouldBe(422);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        var error = problem.GetProperty("errors").EnumerateObject().ShouldHaveSingleItem();
        error.Name.ShouldBe("reason");
        error.Value.EnumerateArray().ShouldHaveSingleItem().GetString()
            .ShouldBe("An operator reason of 1 to 1000 characters is required.");
        (await ReloadAsync(row)).Status.ShouldBe(QueuedMessageStatus.Sent);
        _adapters[row.AgentSessionId].Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Current_unconfirmed_listings_are_bounded_and_every_row_is_reachable()
    {
        var first = await SeedAsync();
        var rows = Enumerable.Range(2, 1002).Select(n => new SessionQueuedMessage
        {
            Id = Guid.NewGuid(), AgentSessionId = first.AgentSessionId, Sequence = n,
            Body = new string('x', 4096), CreatedAt = first.CreatedAt,
            Status = QueuedMessageStatus.Sent, DeliveryAttempts = 1,
            LastDeliveryStartedAt = first.LastDeliveryStartedAt,
        }).ToList();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SessionQueuedMessages.AddRange(rows);
            await db.SaveChangesAsync();
        }
        try
        {
        using var client = factory.CreateClient();
        var queueIds = new List<Guid>();
        long since = 0;
        do
        {
            var page = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{first.AgentSessionId}/messages?since={since}&limit=10000");
            var messages = page.GetProperty("messages").EnumerateArray().ToList();
            messages.Count.ShouldBeInRange(1, 100);
            var sequence = messages.Select(m => m.GetProperty("sequence").GetInt64()).ToArray();
            sequence.ShouldBe(sequence.Order().ToArray());
            sequence[0].ShouldBeGreaterThan(since);
            queueIds.AddRange(messages.Select(m => m.GetProperty("id").GetGuid()));
            since = page.GetProperty("nextSince").GetInt64();
            if (!page.GetProperty("truncated").GetBoolean()) break;
        } while (queueIds.Count < 1100);
        queueIds.ShouldBe(new[] { first.Id }.Concat(rows.Select(r => r.Id)).ToArray());
        var attentionIds = new List<Guid>();
        var skip = 0;
        do
        {
            var page = await client.GetFromJsonAsync<JsonElement>($"/api/attention?parkedSkip={skip}&parkedLimit=10000");
            var parked = page.GetProperty("items").EnumerateArray()
                .Where(i => i.GetProperty("kind").GetString() == "ParkedMessage").ToList();
            parked.Count.ShouldBeLessThanOrEqualTo(100);
            attentionIds.AddRange(parked.Where(i => i.GetProperty("sessionId").GetGuid() == first.AgentSessionId)
                .Select(i => i.GetProperty("messageId").GetGuid()));
            if (!page.GetProperty("parkedMessagesTruncated").GetBoolean()) break;
            var next = page.GetProperty("parkedMessagesNextSkip").GetInt32();
            next.ShouldBeGreaterThan(skip);
            skip = next;
        } while (skip < 2000);
        attentionIds.Distinct().Count().ShouldBe(1003);
        attentionIds.Count.ShouldBe(1003);
        _adapters[first.AgentSessionId].Inputs.ShouldBeEmpty();
        }
        finally
        {
            await using var cleanup = factory.Services.CreateAsyncScope();
            await cleanup.ServiceProvider.GetRequiredService<AppDbContext>().SessionQueuedMessages
                .Where(m => m.AgentSessionId == first.AgentSessionId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Abandoned_current_rules_keep_an_explicit_fresh_start_attention_until_generation_changes()
    {
        var row = await SeedRulesAsync();
        using (var result = await AbandonAsync(row, "Stop waiting for this rules read")) result.StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory.Services.GetRequiredService<GrokRulesRefreshService>().ReconcileAsync(row.AgentSessionId, default);
        await using var scope = factory.Services.CreateAsyncScope();
        var attention = scope.ServiceProvider.GetRequiredService<AttentionService>();
        var item = (await attention.GetAsync(default, includeProgressProbe: false)).Items.Single(i => i.MessageId == row.Id);
        item.Headline.ShouldContain("fresh start");
        item.Headline.ShouldContain("rules");
        item.Actions.ShouldBe([AttentionAction.OpenAgent]);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == row.AgentSessionId);
        session.GrokRulesState.ShouldBe(GrokRulesState.Pending);
        item.Evidence.ShouldContain(session.GrokRulesGeneration!.Value.ToString("N"));
        item.Headline.ShouldContain("fresh=true");
        (await ReloadAsync(row)).Status.ShouldBe(QueuedMessageStatus.Canceled);
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == row.AgentSessionId)).ShouldBe(1);
        session.GrokRulesGeneration = Guid.NewGuid();
        var receipt = GrokRulesRefreshService.Receipt(session)! with { Generation = session.GrokRulesGeneration.Value };
        session.GrokRulesReceiptJson = JsonSerializer.Serialize(receipt);
        await db.SaveChangesAsync();
        (await attention.GetAsync(default, includeProgressProbe: false)).Items.ShouldNotContain(i => i.MessageId == row.Id);
        _adapters[row.AgentSessionId].Inputs.ShouldBeEmpty();
    }

    private async Task<SessionQueuedMessage> SeedRulesAsync()
    {
        var row = await SeedAsync(status: SessionStatus.Running);
        var receipt = new GrokRulesReceipt($"C:\\remote\\instructions\\grok\\{row.AgentSessionId:N}\\rules.md",
            GrokRulesTransport.Hash("standing rules"u8.ToArray()), "standing rules"u8.Length, 1, Guid.NewGuid());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = (await db.AgentSessions.FindAsync(row.AgentSessionId))!;
        session.GrokRulesGeneration = receipt.Generation;
        session.GrokRulesExpectedSha256 = receipt.Sha256;
        session.GrokRulesExpectedByteCount = receipt.ByteCount;
        session.GrokRulesReceiptJson = JsonSerializer.Serialize(receipt);
        session.GrokRulesState = GrokRulesState.Pending;
        var stored = (await db.SessionQueuedMessages.FindAsync(row.Id))!;
        stored.RulesRefreshKey = $"launch:{receipt.Generation:N}";
        stored.RulesReceiptJson = session.GrokRulesReceiptJson;
        stored.RulesDeadlineAt = DateTime.UtcNow.AddMinutes(-2);
        stored.Body = GrokRulesRefreshService.Prompt(row.Id, receipt);
        await db.SaveChangesAsync();
        return await ReloadAsync(row);
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Abandon_and_late_confirmation_serialize_without_resurrection(bool confirmFirst)
    {
        var row = await SeedAsync(refinement: true);
        await AddReceiptAsync(row);
        var queue = factory.Services.GetRequiredService<SessionMessageQueueService>();
        var gate = queue.GetLock(row.AgentSessionId);
        await gate.WaitAsync();
        Task? confirm = null; Task<HttpResponseMessage>? abandon = null;
        try
        {
            if (confirmFirst) confirm = queue.ConfirmSentRefinementsAsync(row.AgentSessionId, default);
            else abandon = AbandonAsync(row, "operator wins the race");
            await Should.ThrowAsync<TimeoutException>((confirmFirst ? confirm! : abandon!).WaitAsync(TimeSpan.FromMilliseconds(150)));
            if (confirmFirst) abandon = AbandonAsync(row, "operator loses the race");
            else confirm = queue.ConfirmSentRefinementsAsync(row.AgentSessionId, default);
        }
        finally { gate.Release(); }
        using var result = await abandon!;
        await confirm!;
        var after = await ReloadAsync(row);
        if (after.Status == QueuedMessageStatus.Canceled)
        {
            result.StatusCode.ShouldBe(HttpStatusCode.OK);
            after.DeliveryVerdict.ShouldBeNull();
        }
        else
        {
            result.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            after.Status.ShouldBe(QueuedMessageStatus.Sent);
            after.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        }
        after.DeliveryAttempts.ShouldBe(1);
        await using var scope = factory.Services.CreateAsyncScope();
        var audits = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditRecords.CountAsync(i => i.Id == row.Id);
        audits.ShouldBe(after.Status == QueuedMessageStatus.Canceled ? 1 : 0);
        _adapters[row.AgentSessionId].Inputs.ShouldBeEmpty();
    }

    private async Task<SessionQueuedMessage> SeedAsync(SessionStatus status = SessionStatus.Stopped, bool refinement = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = new AgentSession { Id = Guid.NewGuid(), DefinitionName = "fake", AgentKind = AgentKind.Grok,
            Status = status, CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, Cwd = factory.TestRoot };
        var row = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = session.Id, Sequence = 1,
            Body = "An operator must decide about this uniquely identifiable lost input reply", CreatedAt = DateTime.UtcNow,
            Status = QueuedMessageStatus.Sent, SentAt = DateTime.UtcNow, DeliveryAttempts = 1,
            LastDeliveryStartedAt = DateTime.UtcNow.AddDays(-2).Date, LastDeliveryBaselineSequence = 0,
            ConversationKey = refinement ? MidTurnInterruptPolicy.ConversationKeyPrefix + "test" : null };
        db.AgentSessions.Add(session); db.SessionQueuedMessages.Add(row); await db.SaveChangesAsync();
        var adapter = new FakeAgentProtocolAdapter();
        _adapters[session.Id] = adapter;
        factory.Services.GetRequiredService<AgentSessionRuntime>().Register(session.Id, adapter);
        return row;
    }
    private async Task AddReceiptAsync(SessionQueuedMessage row)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = row.AgentSessionId,
            Sequence = 1, Kind = TranscriptKinds.UserPrompt, Text = row.Body, CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
    private async Task<SessionQueuedMessage> ReloadAsync(SessionQueuedMessage row)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == row.Id));
    }
    private static string Route(SessionQueuedMessage row) => $"/api/sessions/{row.AgentSessionId}/messages/{row.Id}/abandon";
    private async Task<HttpResponseMessage> AbandonAsync(SessionQueuedMessage row, string reason)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(OperatorTokenFile.Header, factory.OperatorToken);
        return await client.PostAsJsonAsync(Route(row), new { reason });
    }
}

public sealed class AbandonWebAppFactory : AntiphonWebAppFactory
{
    public string TestRoot { get; } = Path.Combine(Path.GetTempPath(), $"ac7f-http-{Guid.NewGuid():N}");
    internal string OperatorToken => OperatorTokenFile.ReadOrCreate(Path.Combine(TestRoot, "test-operator-token"));
    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        services.Configure<PhoneHomeRunnerSettings>(o => o.OperatorTokenPath = Path.Combine(TestRoot, "test-operator-token"));
    }
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(Path.Combine(TestRoot, "test-operator-token"));
        if (Directory.Exists(TestRoot)) Directory.Delete(TestRoot);
    }
}
