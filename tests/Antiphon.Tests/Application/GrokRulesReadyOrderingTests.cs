using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public sealed class GrokRulesReadyOrderingTests
{
    [Test]
    [Arguments("timeout")]
    [Arguments("cancellation")]
    [Arguments("disconnect")]
    public async Task Ambiguous_input_preserves_launch_and_waits_for_transcript(string fault)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var transportClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await PhoneHomeTestHost.StartAsync(transportClock, connectionString: schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var client = new PhoneHomeRunnerClient(live);
        var factory = new Factory();
        await using var h = await BridgeQueueHarness.CreateAsync(new() {
            ConnectionString = schema.ConnectionString, AlwaysOn = false,
            ConfigureServices = s => {
                s.AddSingleton(Options.Create(new GrokRulesSettings { InitializationTimeoutSeconds = 1 }));
                s.AddSingleton<GrokRulesRefreshService>();
                s.AddSingleton<IAgentProtocolAdapterFactory>(factory);
            }});
        factory.Adapter = h.Adapter;
        var payload = new GrokRulesPayload("private rules\n", 1, Guid.NewGuid());
        var bytes = Encoding.UTF8.GetBytes(payload.Content);
        var receipt = new GrokRulesReceipt($"/private/instructions/grok/{h.SessionId:N}/rules.md",
            GrokRulesTransport.Hash(bytes), bytes.Length, 1, payload.Generation);
        h.Runner.Capabilities = new("test", "test", "test", false, Features: [GrokRulesTransport.Capability]);
        h.Runner.SessionResponse = JsonSerializer.Deserialize<SessionRunnerSessionDto>("{}")! with { GrokRulesReceipt = receipt };
        await using var db = h.CreateObserverContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.Status = SessionStatus.Starting;
        await db.SaveChangesAsync();
        var generation = session.StartedAt;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var attempts = 0;
        PhoneHomeFrame? written = null;
        h.Adapter.BeforeInput = async (input, ct) => {
            if (++attempts > 1)
                throw new PhoneHomeTransportException(PhoneHomeProblemTypes.RequestTimeout, "still uncertain");
            peer.SilentFor(PhoneHomeOperation.Input);
            using var inputCancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pending = client.SendInputAsync(h.SessionId, input, inputCancel.Token);
            written = await peer.WaitForAsync(PhoneHomeOperation.Input);
            // The real production transport has written the frame and installed its reply waiter.
            await Task.Delay(50, ct);
            if (fault == "timeout") transportClock.Advance(PhoneHomeLiveConnection.RequestTimeoutFor(PhoneHomeOperation.Input) + TimeSpan.FromSeconds(1));
            else if (fault == "cancellation") inputCancel.Cancel();
            else peer.Socket.Abort();
            await pending;
        };
        var spec = new AgentLaunchSpec("test", AgentKind.Grok, "grok", [], new Dictionary<string,string>(),
            h.TempRoot, 120, 30, GrokRulesPayload: payload);
        using var scope = h.Provider.CreateScope();
        var launch = scope.ServiceProvider.GetRequiredService<AgentSessionService>().LaunchInteractiveAsync(
            h.SessionId, h.AgentId, spec, null, false, null, deadline.Token);
        Exception? error = null;
        try { await launch.WaitAsync(TimeSpan.FromSeconds(8)); }
        catch (Exception ex) { error = ex; }
        finally { deadline.Cancel(); try { await launch; } catch { } }

        written.ShouldNotBeNull($"the uncertain Input really reached the peer: {error}");
        h.Adapter.Killed.ShouldBeFalse($"an ambiguous {fault} must not authorize launch cleanup: {error}");
        error.ShouldBeNull();
        attempts.ShouldBe(1, "no blind retry before transcript/composer reconciliation");
        await db.Entry(session).ReloadAsync();
        session.Status.ShouldBe(SessionStatus.Running);
        session.StartedAt.ShouldBe(generation);
        session.EndedAt.ShouldBeNull();
        session.TerminationSource.ShouldBe(SessionTerminationSource.Unknown);
        session.GrokRulesState.ShouldBe(GrokRulesState.Pending);
        var refresh = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId && m.RulesRefreshKey != null);
        refresh.Status.ShouldBe(QueuedMessageStatus.Sent);
        refresh.DeliveryVerdict.ShouldBeNull("delivery remains unknown until a UserPrompt arrives");
        refresh.DeliveryAttempts.ShouldBe(1);
        refresh.LastDeliveryStartedAt.ShouldNotBeNull();
        refresh.LastDeliveryGeneration.ShouldBe(generation);
        refresh.RulesDeadlineAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        var rules = h.Provider.GetRequiredService<GrokRulesRefreshService>();
        await rules.ReconcileAsync(h.SessionId, CancellationToken.None);
        await db.Entry(session).ReloadAsync();
        session.GrokRulesState.ShouldBe(GrokRulesState.Pending, "time alone cannot turn uncertain input into a failed startup");
        if (fault == "timeout")
        {
            await PhoneHomeFraming.WriteFrameAsync(peer.Socket,
                new PhoneHomeFrame(PhoneHomeFrameKind.Result, written!.Epoch, written.RequestId, written.Operation,
                    JsonSerializer.SerializeToElement(new { ok = true })), 16 * 1024 * 1024, CancellationToken.None);
            await client.GetHealthAsync(CancellationToken.None);
            await db.Entry(refresh).ReloadAsync();
            refresh.DeliveryVerdict.ShouldBeNull("a late wire reply is not a UserPrompt receipt");
        }
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, refresh.Body);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText,
            $"ANTIPHON_RULES_ACK id={refresh.Id:N} generation={receipt.Generation:N} sha256={receipt.Sha256}");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await rules.ReconcileAsync(h.SessionId, CancellationToken.None);
        await db.Entry(refresh).ReloadAsync();
        await db.Entry(session).ReloadAsync();
        session.GrokRulesState.ShouldBe(GrokRulesState.Ready);
        refresh.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        refresh.DeliveryAttempts.ShouldBe(1);
        attempts.ShouldBe(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Receipt_and_refresh_are_committed_while_readiness_holds_all_input(bool ready)
    {
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Factory();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConfigureServices = s => {
            s.AddSingleton(Options.Create(new GrokRulesSettings()));
            s.AddSingleton<GrokRulesRefreshService>();
            s.AddSingleton<IAgentProtocolAdapterFactory>(factory);
        }});
        var adapter = h.Adapter;
        factory.Adapter = adapter;
        adapter.ReadyHold = hold;
        var payload = new GrokRulesPayload("readiness-only private rules\r\n", 1, Guid.NewGuid());
        var bytes = Encoding.UTF8.GetBytes(payload.Content);
        var receipt = new GrokRulesReceipt($"C:\\remote\\instructions\\grok\\{h.SessionId:N}\\rules.md",
            GrokRulesTransport.Hash(bytes), bytes.Length, 1, payload.Generation);
        h.Runner.Capabilities = new("test", "test", "test", false, Features: [GrokRulesTransport.Capability]);
        h.Runner.SessionResponse = JsonSerializer.Deserialize<SessionRunnerSessionDto>("{}")! with { GrokRulesReceipt = receipt };
        await using var db = BridgeQueueHarness.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.Status = SessionStatus.Starting;
        await db.SaveChangesAsync();
        var ordinary = await h.SeedPendingMessageAsync("ordinary work stays held");
        var spec = new AgentLaunchSpec("test", AgentKind.Grok, "grok", [], new Dictionary<string,string>(),
            h.TempRoot, 120, 30, GrokRulesPayload: payload);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var scope = h.Provider.CreateScope();
        var launch = scope.ServiceProvider.GetRequiredService<AgentSessionService>().LaunchInteractiveAsync(
            h.SessionId, h.AgentId, spec, null, false, null, deadline.Token);
        try
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == h.SessionId && m.RulesRefreshKey != null)
                && !launch.IsCompleted && DateTime.UtcNow < until) await Task.Delay(20);
            if (launch.IsFaulted) await launch;
            await db.Entry(session).ReloadAsync();
            GrokRulesRefreshService.Receipt(session).ShouldBe(receipt, "receipt commit must precede waiting for readiness");
            var refresh = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId && m.RulesRefreshKey != null);
            refresh.RulesDeadlineAt.ShouldBeNull();
            session.Status.ShouldBe(SessionStatus.Starting);
            await h.Queue.FlushSessionAsync(h.SessionId, deadline.Token);
            adapter.SubmittedBodies.ShouldBeEmpty("neither rules nor work may precede readiness");
            if (ready)
            {
                adapter.OnSubmitted = async prompt => {
                    prompt.ShouldStartWith(GrokRulesRefreshService.Header(refresh.Id));
                    await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
                    await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText,
                        $"ANTIPHON_RULES_ACK id={refresh.Id:N} generation={receipt.Generation:N} sha256={receipt.Sha256}");
                    await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
                };
                // Isolate the ordinary held row from post-ready launch flushing: it is observed
                // before the ACK and explicitly canceled only after that evidence is collected.
                await db.SessionQueuedMessages.Where(m => m.Id == ordinary).ExecuteUpdateAsync(u =>
                    u.SetProperty(m => m.Status, QueuedMessageStatus.Canceled));
            }
            hold.SetResult(ready);
            if (ready) await launch;
            else await Should.ThrowAsync<Exception>(launch);
            await db.Entry(session).ReloadAsync();
            if (ready)
            {
                session.GrokRulesState.ShouldBe(GrokRulesState.Ready);
                adapter.SubmittedBodies.ShouldHaveSingleItem().ShouldStartWith(GrokRulesRefreshService.Header(refresh.Id));
            }
            else
            {
                adapter.SubmittedBodies.ShouldBeEmpty();
                session.Status.ShouldBe(SessionStatus.Failed);
                adapter.Killed.ShouldBeTrue();
                (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == ordinary)).Status.ShouldBe(QueuedMessageStatus.Pending);
            }
        }
        finally { hold.TrySetResult(false); deadline.Cancel(); try { await launch; } catch { } }
    }

    private sealed class Factory : IAgentProtocolAdapterFactory
    {
        public FakeAgentProtocolAdapter Adapter { get; set; } = null!;
        public IAgentProtocolAdapter Create(AgentKind kind) => Adapter;
    }
}
