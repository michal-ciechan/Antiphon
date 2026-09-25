using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
public class PhoneHomeImmediateSendTests
{
    [Test]
    public async Task C696Red_ModeNow_before_first_List_returns_503_without_mutation()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        using var http = h.Http; // Finish Program's startup before taking the state snapshot.
        var before = await h.DurableStateAsync();
        using var response = await http.PostAsJsonAsync($"/api/sessions/{h.SessionId}/messages", new { body = "now before first List", mode = "Now" });
        await AssertUnavailableAsync(response);
        (await h.DurableStateAsync()).ShouldBe(before);
        h.Host.Local.Calls.ShouldNotContain("input");
    }

    [Test]
    public async Task C696Red_SendNow_in_reconnect_gap_returns_503_without_mutation()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        await h.Queue.EnqueueAsync(h.SessionId, "previously attempted input", MessageSendMode.WhenIdle, CancellationToken.None);
        var id = (await h.RowsAsync()).Single().Id;
        await using (var db = h.Db())
            await db.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(u => u
                .SetProperty(m => m.DeliveryAttempts, 1)
                .SetProperty(m => m.LastDeliveryStartedAt, DateTime.UtcNow.AddMinutes(-2))
                .SetProperty(m => m.LastDeliveryGeneration, DateTime.UtcNow.AddMinutes(-3))
                .SetProperty(m => m.LastDeliveryBaselineSequence, 7L)
                .SetProperty(m => m.DeliveryVerdict, DeliveryVerdict.NoTranscriptRecord));
        await using var a = await h.RecoverAsync();
        await h.DisconnectAsync(a);
        using var http = h.Http;
        var before = await h.DurableStateAsync();
        using var response = await http.PostAsJsonAsync($"/api/sessions/{h.SessionId}/messages/{id}/send-now", new { });
        await AssertUnavailableAsync(response);
        (await h.DurableStateAsync()).ShouldBe(before);
        a.RequestCount(PhoneHomeOperation.Input).ShouldBe(0);
    }

    internal static async Task AssertUnavailableAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().ShouldBe(PhoneHomeProblemTypes.Unavailable);
    }
    [Test]
    public async Task Immediate_entry_points_share_all_outage_states()
    {
        foreach (var state in new[] { "pending", "recovering", "closed", "expired", "starting" })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync();
            var row = await h.PendingAsync("unchanged old attempt", attempted: true);
            await using var peer = state == "recovering" ? await h.Host.ConnectPeerAsync()
                : state is "closed" or "expired" ? await h.RecoverAsync() : null;
            if (state == "closed") await h.DisconnectAsync(peer!);
            if (state == "expired") h.Clock.Advance(TimeSpan.FromSeconds(91));
            if (state == "starting")
            {
                await using var db = h.Db();
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
            }
            using var http = h.Http;
            var before = await h.DurableStateAsync();
            using var now = await http.PostAsJsonAsync($"/api/sessions/{h.SessionId}/messages", new { body = "immediate", mode = "Now" });
            await AssertUnavailableAsync(now);
            using var queued = await http.PostAsJsonAsync($"/api/sessions/{h.SessionId}/messages/{row.Id}/send-now", new { });
            await AssertUnavailableAsync(queued);
            var refusal = await Should.ThrowAsync<ServiceUnavailableException>(() => h.Queue.EnqueueDeliveringNowAsync(h.SessionId, "durable immediate", CancellationToken.None));
            refusal.Code.ShouldBe(PhoneHomeProblemTypes.Unavailable, state);
            (await h.DurableStateAsync()).ShouldBe(before, state);
            if (peer is not null) peer.RequestCount(PhoneHomeOperation.Input).ShouldBe(0);
        }
    }

    [Test]
    public async Task Recovered_remote_and_local_immediate_sends_deliver_once()
    {
        foreach (var remote in new[] { false, true })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync(remote);
            await using var peer = remote ? await h.RecoverAsync() : null;
            if (remote)
            {
                // Stale inventory is still dispatchable when a heartbeat keeps its lease.
                var live = h.Host.Directory.SnapshotLive()!;
                h.Clock.Advance(TimeSpan.FromSeconds(91));
                await peer!.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Heartbeat, live.Epoch, Guid.NewGuid()));
                await PhoneHomeOutageHarness.UntilAsync(() => live.LastHeartbeatUtc == h.Clock.GetUtcNow());
                h.Runtime.ListLiveSessions().ShouldNotContain(h.SessionId);
            }
            using var http = h.Http;
            using var now = await http.PostAsJsonAsync($"/api/sessions/{h.SessionId}/messages", new { body = "direct now", mode = "Now" });
            now.IsSuccessStatusCode.ShouldBeTrue(await now.Content.ReadAsStringAsync());
            (await h.RowsAsync()).ShouldBeEmpty();
            await h.AssertReceiptAsync("direct now");
            var row = await h.PendingAsync("queued now");
            using var queued = await http.PostAsJsonAsync($"/api/sessions/{h.SessionId}/messages/{row.Id}/send-now", new { });
            queued.IsSuccessStatusCode.ShouldBeTrue(await queued.Content.ReadAsStringAsync());
            await h.AssertReceiptAsync("queued now");
            await h.Queue.EnqueueDeliveringNowAsync(h.SessionId, "durable now", CancellationToken.None);
            await h.AssertReceiptAsync("durable now");
            (await h.RowsAsync()).ShouldAllBe(m => m.Status == QueuedMessageStatus.Sent && m.DeliveryVerdict == DeliveryVerdict.Delivered);
            if (peer is not null) peer.RequestCount(PhoneHomeOperation.Input).ShouldBe(6);
        }
    }

    [Test]
    public async Task Body_not_sent_after_preflight_restores_the_entire_old_attempt()
    {
        foreach (var operation in new[] { "now", "send-now", "durable" })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync();
            var row = await h.PendingAsync("preserve old baseline and spill", attempted: true);
            await using (var db = h.Db())
                await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.RemoteSpillBody, "old retained spill")
                    .SetProperty(m => m.RemoteSpillRelativePath, ".antiphon/inbox/old.md"));
            await using var peer = await h.RecoverAsync();
            var live = h.Host.Directory.SnapshotLive()!;
            h.Runtime.Register(h.SessionId, h.Bridge.Adapter);
            h.Bridge.Adapter.BeforeInput = async (input, ct) =>
            {
                await h.DisconnectAsync(peer);
                await new PhoneHomeRunnerClient(live).SendInputAsync(h.SessionId, input, ct);
            };
            var before = await h.DurableStateAsync();
            var refused = await Should.ThrowAsync<ServiceUnavailableException>(() => operation switch
            {
                "now" => h.Queue.EnqueueAsync(h.SessionId, "new now", MessageSendMode.Now, CancellationToken.None),
                "send-now" => h.Queue.SendNowAsync(h.SessionId, row.Id, CancellationToken.None),
                _ => h.Queue.EnqueueDeliveringNowAsync(h.SessionId, "new durable", CancellationToken.None)
            });
            refused.Code.ShouldBe(PhoneHomeProblemTypes.Unavailable);
            peer.RequestCount(PhoneHomeOperation.Input).ShouldBe(0);
            (await h.DurableStateAsync()).ShouldBe(before, operation + " restores exactly; only a newly inserted provisional row may be removed");
        }
    }

    [Test]
    public async Task Body_sent_but_Enter_unavailable_keeps_uncertain_attempt()
    {
        foreach (var durable in new[] { false, true })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync();
            var row = durable ? null : await h.PendingAsync("body before Enter");
            await using var a = await h.RecoverAsync();
            h.Runtime.Register(h.SessionId, h.Bridge.Adapter);
            var client = new RunnerScopedSessionRunnerClient(h.Host.Directory, h.Host.AllowedRunnerId);
            h.Bridge.Adapter.BeforeInput = async (input, ct) =>
            {
                if (input == "\r") await h.DisconnectAsync(a);
                await client.SendInputAsync(h.SessionId, input, ct);
            };
            var failure = await CaptureAsync(() => durable
                ? h.Queue.EnqueueDeliveringNowAsync(h.SessionId, "body before Enter", CancellationToken.None)
                : h.Queue.SendNowAsync(h.SessionId, row!.Id, CancellationToken.None));
            failure.ShouldBeOfType<PhoneHomeTransportException>().Code.ShouldBe(PhoneHomeProblemTypes.ConnectionClosedInFlight);
            // The body reached the runner; the attempt remains available for normal recovery.
            var after = (await h.RowsAsync()).Single();
            after.DeliveryAttempts.ShouldBe(1);
            after.LastDeliveryBaselineSequence.ShouldNotBeNull();
            a.RequestCount(PhoneHomeOperation.Input).ShouldBe(1);
            h.Bridge.Adapter.Inputs.ShouldBe(["body before Enter"]);
            await using var b = await h.RecoverAsync();
            h.Bridge.Adapter.BeforeInput = (input, ct) => client.SendInputAsync(h.SessionId, input, ct);
            // Let the existing interrupted-Sent path become eligible; do not erase its floor.
            h.QueueClock.Advance(TimeSpan.FromMinutes(1));
            await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
            h.Bridge.Adapter.Inputs.Count(i => i == "body before Enter").ShouldBe(1);
            await h.AssertReceiptAsync("body before Enter");
            b.RequestCount(PhoneHomeOperation.Input).ShouldBe(1);
        }
    }

    [Test]
    public async Task In_flight_send_and_caller_cancellation_are_not_safe_refusals()
    {
        foreach (var phase in new[] { "body", "enter", "cancel" })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync();
            var row = await h.PendingAsync("uncertain body");
            await using var peer = await h.RecoverAsync();
            h.Runtime.Register(h.SessionId, h.Bridge.Adapter);
            var cancel = phase == "cancel";
            using var caller = new CancellationTokenSource();
            var client = new RunnerScopedSessionRunnerClient(h.Host.Directory, h.Host.AllowedRunnerId);
            h.Bridge.Adapter.BeforeInput = async (input, ct) =>
            {
                if (phase == "enter" && input != "\r")
                {
                    await client.SendInputAsync(h.SessionId, input, ct);
                    return;
                }
                peer.SilentFor(PhoneHomeOperation.Input);
                var send = client.SendInputAsync(h.SessionId, input, ct);
                await PhoneHomeOutageHarness.UntilAsync(() => peer.RequestCount(PhoneHomeOperation.Input) == (phase == "enter" ? 2 : 1));
                if (cancel) caller.Cancel(); else await h.DisconnectAsync(peer);
                await send;
            };
            var failure = await CaptureAsync(() => h.Queue.SendNowAsync(h.SessionId, row.Id, caller.Token));
            if (cancel) failure.ShouldBeAssignableTo<OperationCanceledException>();
            else failure.ShouldBeOfType<PhoneHomeTransportException>().Code.ShouldBe(PhoneHomeProblemTypes.ConnectionClosedInFlight);
            var after = (await h.RowsAsync()).Single();
            after.DeliveryAttempts.ShouldBe(1);
            after.LastDeliveryStartedAt.ShouldNotBeNull();
            after.LastDeliveryBaselineSequence.ShouldNotBeNull();
        }
    }

    [Test]
    public async Task Terminal_and_missing_sessions_are_not_outages()
    {
        foreach (var status in new[] { SessionStatus.Stopped, SessionStatus.Failed, SessionStatus.Running })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync(warmEmpty: true);
            var row = await h.PendingAsync("terminal control");
            await using var peer = status == SessionStatus.Running ? await h.RecoverAsync(includeTarget: false) : null;
            await using var db = h.Db();
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, status));
            var before = await h.DurableStateAsync();
            (await CaptureAsync(() => h.Queue.EnqueueAsync(h.SessionId, "now", MessageSendMode.Now, CancellationToken.None))).ShouldBeOfType<ConflictException>();
            (await CaptureAsync(() => h.Queue.SendNowAsync(h.SessionId, row.Id, CancellationToken.None))).ShouldBeOfType<ConflictException>();
            (await CaptureAsync(() => h.Queue.EnqueueDeliveringNowAsync(h.SessionId, "now", CancellationToken.None))).ShouldBeOfType<ConflictException>();
            (await CaptureAsync(() => h.Queue.EnqueueAsync(Guid.NewGuid(), "missing", MessageSendMode.Now, CancellationToken.None))).ShouldBeOfType<NotFoundException>();
            (await CaptureAsync(() => h.Queue.SendNowAsync(h.SessionId, Guid.NewGuid(), CancellationToken.None))).ShouldBeOfType<NotFoundException>();
            (await h.DurableStateAsync()).ShouldBe(before);
        }
    }

    [Test]
    public async Task Local_Herdr_and_modal_guards_keep_their_existing_contract()
    {
        foreach (var remote in new[] { false, true })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync(remote);
            await using var peer = remote ? await h.RecoverAsync() : null;
            if (remote) h.Runtime.Register(h.SessionId, h.Bridge.Adapter);
            h.Runtime.SetTestPending(h.SessionId, HerdrPendingReasons.Unreachable);
            var refusal = await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueAsync(h.SessionId, "herdr", MessageSendMode.Now, CancellationToken.None));
            refusal.Message.ShouldContain("herdr");
            h.Runtime.SetTestPending(h.SessionId, null);
            await using var db = h.Db();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            var originalBackend = session.SessionBackend;
            session.SessionBackend = SessionBackend.Herdr;
            await db.SaveChangesAsync();
            h.Runtime.SetTestAgentStatus(h.SessionId, "blocked");
            (await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueAsync(h.SessionId, "blocked", MessageSendMode.Now, CancellationToken.None)))
                .Message.ShouldContain("blocked in herdr");
            h.Runtime.SetTestAgentStatus(h.SessionId, null);
            session.SessionBackend = originalBackend;
            session.Status = SessionStatus.Starting;
            await db.SaveChangesAsync();
            (await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueAsync(h.SessionId, "starting", MessageSendMode.Now, CancellationToken.None))).Message.ShouldContain("starting");
            session.Status = SessionStatus.Running;
            session.AgentKind = AgentKind.Grok;
            session.GrokRulesState = GrokRulesState.Pending;
            await db.SaveChangesAsync();
            (await CaptureAsync(() => h.Queue.EnqueueAsync(h.SessionId, "rules", MessageSendMode.Now, CancellationToken.None))).ShouldBeOfType<ConflictException>();
            session.GrokRulesState = GrokRulesState.Ready;
            db.RemoteControlModalEpisodes.Add(new Antiphon.Server.Domain.Entities.RemoteControlModalEpisode
            {
                Id = Guid.NewGuid(), SessionId = session.Id, AcceptedStartedAt = SessionGeneration.Normalize(session.StartedAt),
                FirstObservedAt = DateTime.UtcNow, LastObservedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            var modal = await h.Queue.EnqueueAsync(h.SessionId, "modal", MessageSendMode.Now, CancellationToken.None);
            modal.ModalBlocked.ShouldBeTrue();
            h.Bridge.Adapter.Inputs.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task WhenIdle_outage_remains_accepted_and_preserves_attempts()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        var old = await h.PendingAsync("old pending", attempted: true);
        var before = JsonSerializer.Serialize((await h.RowsAsync()).Single());
        await h.Queue.EnqueueAsync(h.SessionId, "new pending", MessageSendMode.WhenIdle, CancellationToken.None);
        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        var rows = await h.RowsAsync();
        JsonSerializer.Serialize(rows.Single(r => r.Id == old.Id)).ShouldBe(before);
        rows.Single(r => r.Id != old.Id).DeliveryAttempts.ShouldBe(0);
        rows.ShouldAllBe(r => r.Status == QueuedMessageStatus.Pending);
        await using var peer = await h.RecoverAsync();
        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await h.AssertReceiptAsync("old pending");
        await h.AssertReceiptAsync("new pending");
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }
}
