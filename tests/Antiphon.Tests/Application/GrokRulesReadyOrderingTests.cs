using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public sealed class GrokRulesReadyOrderingTests
{
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
