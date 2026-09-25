using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class PhoneHomeOutageMentionTests
{
    [Test]
    public async Task C696Red_Mention_before_first_List_is_persisted()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        await h.RouteAsync("@fake please retain this accepted mention\n");
        var row = (await h.RowsAsync()).ShouldHaveSingleItem("accepted mention must be durable before any runner List");
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.DeliveryAttempts.ShouldBe(0);
        await using var peer = await h.RecoverAsync();
        (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None)).ShouldBe(1);
        await h.AssertReceiptAsync(row.Body);
        (await h.RowsAsync()).Single().Status.ShouldBe(QueuedMessageStatus.Sent);
        peer.RequestCount(PhoneHomeOperation.Input).ShouldBe(2);
    }

    [Test]
    public async Task C696Red_Mention_in_reconnect_gap_survives_restart()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        await using var a = await h.RecoverAsync();
        await h.DisconnectAsync(a);
        await h.RouteAsync("@fake please survive the desktop graph restart\n");
        var row = (await h.RowsAsync()).ShouldHaveSingleItem("reconnect gap must retain the accepted route");
        await h.RecreateGraphAsync();
        (await h.RowsAsync()).Single().Id.ShouldBe(row.Id);
        await using var b = await h.RecoverAsync();
        (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None)).ShouldBe(1);
        await h.AssertReceiptAsync(row.Body);
        b.RequestCount(PhoneHomeOperation.Input).ShouldBe(2);
    }
    [Test]
    public async Task Busy_local_and_remote_mentions_wait_for_idle()
    {
        foreach (var remote in new[] { false, true })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync(remote);
            await using var peer = remote ? await h.RecoverAsync() : null;
            await h.Bridge.MarkWorkingAsync();
            (await h.MentionAsync("fake", "wait for this turn to end")).ShouldBeTrue();
            var row = (await h.RowsAsync()).ShouldHaveSingleItem();
            row.Origin.ShouldBe(QueuedMessageOrigin.Mention);
            row.DeliveryAttempts.ShouldBe(0);
            h.Bridge.Adapter.Inputs.ShouldBeEmpty();
            if (peer is not null) peer.RequestCount(PhoneHomeOperation.Input).ShouldBe(0);
            await h.Bridge.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
            await h.AssertReceiptAsync(row.Body);
            (await h.RowsAsync()).Single().Status.ShouldBe(QueuedMessageStatus.Sent);
        }
    }

    [Test]
    public async Task Occurrence_replay_is_idempotent_but_identical_new_mentions_are_distinct()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        var occurrence = Guid.NewGuid();
        const string body = "[channel from source] identical\r\nmessage";
        var otherQueue = new SessionMessageQueueService(h.Bridge.Provider.GetRequiredService<IServiceScopeFactory>(),
            h.Runtime, h.Bridge.EventBus, TimeProvider.System, NullLogger<SessionMessageQueueService>.Instance);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(i =>
            (i % 2 == 0 ? h.Queue : otherQueue).EnqueueMentionAsync(h.SessionId, body, occurrence, CancellationToken.None)));
        (await h.RowsAsync()).ShouldHaveSingleItem().Id.ShouldBe(occurrence);
        await h.RecreateGraphAsync();
        await using var peer = await h.RecoverAsync();
        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        var settled = await h.DurableStateAsync();
        await h.Queue.EnqueueMentionAsync(h.SessionId, body.ReplaceLineEndings("\n"), occurrence, CancellationToken.None);
        (await h.DurableStateAsync()).ShouldBe(settled, "replay cannot reset delivery state");
        await h.Queue.EnqueueMentionAsync(h.SessionId, body, Guid.NewGuid(), CancellationToken.None);
        (await h.RowsAsync()).Count.ShouldBe(2);
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await h.AssertReceiptAsync(body.ReplaceLineEndings("\n"), 2);
        await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueMentionAsync(h.SessionId, "different", occurrence, CancellationToken.None));
        await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueMentionAsync(h.SourceId, body, occurrence, CancellationToken.None));
        await using var db = h.Db();
        await db.SessionQueuedMessages.Where(m => m.Id == occurrence).ExecuteUpdateAsync(u => u.SetProperty(m => m.Origin, QueuedMessageOrigin.Ui));
        await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueMentionAsync(h.SessionId, body, occurrence, CancellationToken.None));
    }

    [Test]
    public async Task Split_line_and_completed_newline_accept_one_occurrence()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        h.Router.ObserveDelta(h.SourceId, "@fa");
        await h.RouteAsync("ke please retain the split mention");
        h.Router.ObserveDelta(h.SourceId, "\n");
        await PhoneHomeOutageHarness.UntilAsync(() => h.Diagnostics.Snapshot().Any(e => e.Stage == MentionRouteDiagnostics.CommandSkippedDuplicate));
        var row = (await h.RowsAsync()).ShouldHaveSingleItem();
        await using var peer = await h.RecoverAsync();
        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        await h.AssertReceiptAsync(row.Body);
        peer.RequestCount(PhoneHomeOperation.Input).ShouldBe(2);
    }

    [Test]
    public async Task Accepted_mention_keeps_original_target_after_source_ends()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        (await h.MentionAsync("missing", "nothing")).ShouldBeFalse();
        (await h.MentionAsync("c679-source", "self")).ShouldBeFalse();
        (await h.MentionAsync(h.SessionId.ToString("N")[..8], "frozen target")).ShouldBeTrue();
        var row = (await h.RowsAsync()).Single();
        await using var db = h.Db();
        var sibling = await PhoneHomeStrandedQueueTests.InsertMentionSourceAsync(h.Schema.ConnectionString, h.CardId);
        await db.AgentSessions.Where(s => s.Id == sibling).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.DefinitionName, "fake").SetProperty(s => s.RunnerId, h.Host.AllowedRunnerId)
            .SetProperty(s => s.RunnerStoreId, h.Host.StoreId).SetProperty(s => s.RunnerCwd, h.Bridge.TempRoot));
        (await h.MentionAsync("fake", "ambiguous")).ShouldBeFalse();
        await db.AgentSessions.Where(s => s.Id == h.SourceId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
        await h.RecreateGraphAsync();
        await using var peer = await h.RecoverAsync();
        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        await h.AssertReceiptAsync(row.Body);
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sibling)).ShouldBe(0);
        (await h.RowsAsync()).Single().AgentSessionId.ShouldBe(h.SessionId);
        (await h.MentionAsync("fake", "ended source")).ShouldBeFalse();
    }

    [Test]
    public async Task In_flight_mention_Enter_late_confirms_without_retyping()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        await using var a = await h.RecoverAsync();
        h.Runtime.Register(h.SessionId, h.Bridge.Adapter);
        var client = new RunnerScopedSessionRunnerClient(h.Host.Directory, h.Host.AllowedRunnerId);
        const string body = "[channel from c679-source] submitted before disconnect";
        h.Bridge.Adapter.BeforeInput = async (input, ct) =>
        {
            if (input != "\r") { await client.SendInputAsync(h.SessionId, input, ct); return; }
            a.SilentFor(PhoneHomeOperation.Input);
            var send = client.SendInputAsync(h.SessionId, input, ct);
            await PhoneHomeOutageHarness.UntilAsync(() => a.RequestCount(PhoneHomeOperation.Input) == 2);
            await h.Bridge.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow);
            await h.Bridge.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            await h.DisconnectAsync(a);
            await send;
        };
        (await h.MentionAsync("fake", "submitted before disconnect")).ShouldBeTrue();
        var attempted = (await h.RowsAsync()).Single();
        attempted.Status.ShouldBe(QueuedMessageStatus.Pending);
        attempted.DeliveryAttempts.ShouldBe(1);
        attempted.LastDeliveryBaselineSequence.ShouldNotBeNull();
        await h.RecreateGraphAsync();
        await using var b = await h.RecoverAsync();
        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        var confirmed = (await h.RowsAsync()).Single();
        confirmed.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        confirmed.DeliveryAttempts.ShouldBe(1);
        b.RequestCount(PhoneHomeOperation.Input).ShouldBe(0);
        await h.AssertReceiptAsync(body);
    }

    [Test]
    public async Task Queued_activity_failure_does_not_duplicate_or_block_recovery()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        h.Bridge.EventBus.ThrowOnceOnEvent = "ChannelMessage";
        await h.RouteAsync("@fake first accepted activity fails\n");
        await h.RouteAsync("@fake second accepted activity works\n");
        var rows = await h.RowsAsync();
        rows.Count.ShouldBe(2);
        h.Diagnostics.Snapshot().Count(e => e.Stage == MentionRouteDiagnostics.Queued).ShouldBe(2);
        h.Diagnostics.Snapshot().ShouldNotContain(e => e.Stage == MentionRouteDiagnostics.InputSent);
        await h.RecreateGraphAsync();
        await using var peer = await h.RecoverAsync();
        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        foreach (var row in rows) await h.AssertReceiptAsync(row.Body);
        (await h.RowsAsync()).Count.ShouldBe(2);
        peer.RequestCount(PhoneHomeOperation.Input).ShouldBe(4, "mentions never coalesce into one turn");
    }
}
