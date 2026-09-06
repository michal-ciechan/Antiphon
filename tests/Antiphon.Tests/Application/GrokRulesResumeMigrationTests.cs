using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
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
[NotInParallel("AgentQueue")]
public sealed class GrokRulesResumeMigrationTests
{
    [Test]
    [Arguments("legacy", false)] [Arguments("legacy", true)]
    [Arguments("removal", false)] [Arguments("removal", true)]
    [Arguments("revision", false)] [Arguments("bare", false)]
    public async Task Standing_start_preserves_native_history_and_requires_explicit_migration(string transition, bool fresh)
    {
        var factory = new Factory();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConfigureServices = s => {
            s.AddSingleton(Options.Create(new GrokRulesSettings()));
            s.AddSingleton<GrokRulesRefreshService>();
            s.AddSingleton<IAgentProtocolAdapterFactory>(factory);
            s.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new() {
                DefaultDefinition = "grok", GrokCredentialProbeEnabled = false,
                Definitions = { ["grok"] = new AgentDefinition { Kind = "Grok", Exe = "grok" } } }));
        }});
        factory.Runtime = h.Runtime;
        var nativeHome = Path.Combine(h.TempRoot, "native-home");
        var cwd = Path.Combine(h.TempRoot, "workspace");
        var nativeDirectory = Path.Combine(nativeHome, "sessions", Uri.EscapeDataString(cwd), h.SessionId.ToString("D"));
        Directory.CreateDirectory(nativeDirectory);
        var historyPath = Path.Combine(nativeDirectory, "updates.jsonl");
        var history = Encoding.UTF8.GetBytes("synthetic old native history: retained-fact-47\r\n");
        await File.WriteAllBytesAsync(historyPath, history);
        var rulesRoot = Path.Combine(h.TempRoot, "runner");
        var store = new GrokRulesFileStore(rulesRoot, new());
        var oldReceipt = transition is "removal" or "revision"
            ? await store.WriteAsync(h.SessionId, new("old revision R1\n", 1, Guid.NewGuid()), CancellationToken.None) : null;
        var oldBytes = oldReceipt is null ? null : await File.ReadAllBytesAsync(oldReceipt.Path);
        var desired = transition is "removal" or "bare" ? null : "new revision R2\nsecond standing rule";
        await h.Runtime.KillAsync(h.SessionId, TimeSpan.FromMilliseconds(100), CancellationToken.None);
        h.Runtime.TryRemove(h.SessionId, out _); // model the owned process-exit cleanup before resume
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            var agent = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
            agent.Kind = AgentKind.Grok; agent.Status = AgentStatus.Idle; agent.SystemPromptAppend = desired;
            agent.ReplyStyle = AgentReplyStyle.Normal; agent.LaunchEnvJson = JsonSerializer.Serialize(new Dictionary<string,string> { ["GROK_HOME"] = nativeHome });
            var previous = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            previous.AgentKind = AgentKind.Grok; previous.Status = SessionStatus.Stopped; previous.EndedAt = DateTime.UtcNow;
            previous.GrokRulesReceiptJson = oldReceipt is null ? null : JsonSerializer.Serialize(oldReceipt);
            previous.GrokRulesGeneration = oldReceipt?.Generation; previous.GrokRulesExpectedSha256 = oldReceipt?.Sha256;
            previous.GrokRulesExpectedByteCount = oldReceipt?.ByteCount;
            previous.GrokRulesState = oldReceipt is null ? GrokRulesState.None : GrokRulesState.Ready;
            previous.GrokRulesReadyAt = oldReceipt is null ? null : DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        h.Runner.Capabilities = new("test", "test", "test", false, Features: [GrokRulesTransport.Capability]);
        var queue = h.Provider.GetRequiredService<AgentSessionLaunchQueue>();
        var refusalExpected = !fresh && transition is "legacy" or "removal";
        using var scope = h.Provider.CreateScope();
        var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
        try
        {
            if (refusalExpected)
            {
                Exception? refusal = null;
                try { await control.StartAsync(h.AgentId, new StartAgentRequest(Fresh: false), CancellationToken.None); }
                catch (Exception ex) { refusal = ex; }
                factory.Created.ShouldBeNull("implicit migration must refuse before spawning");
                refusal.ShouldBeOfType<ConflictException>().Code.ShouldBe(transition == "legacy"
                    ? "grok_rules_legacy_resume_requires_fresh_start" : "grok_rules_removal_requires_fresh_start");
                await using var verify = BridgeQueueHarness.CreateContext();
                (await verify.AgentSessions.CountAsync(s => s.Cwd == cwd)).ShouldBe(1);
                (await verify.AgentSessions.SingleAsync(s => s.Id == h.SessionId)).Status.ShouldBe(SessionStatus.Stopped);
                (await File.ReadAllBytesAsync(historyPath)).ShouldBe(history);
                if (oldReceipt is not null) (await File.ReadAllBytesAsync(oldReceipt.Path)).ShouldBe(oldBytes);
                return;
            }
            await control.StartAsync(h.AgentId, new StartAgentRequest(Fresh: fresh), CancellationToken.None);
            var until = DateTime.UtcNow.AddSeconds(15);
            while (factory.Created is null && DateTime.UtcNow < until) await Task.Delay(20);
            var child = factory.Created.ShouldNotBeNull();
            await using var db = BridgeQueueHarness.CreateContext();
            var agentNow = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
            var id = Guid.Parse(agentNow.PersistentSessionId!);
            if (fresh) id.ShouldNotBe(h.SessionId); else id.ShouldBe(h.SessionId);
            GrokRulesReceipt? currentReceipt = null;
            if (desired is not null)
            {
                var untilPrepared = DateTime.UtcNow.AddSeconds(15);
                while (!await db.AgentSessions.AnyAsync(s => s.Id == id && s.GrokRulesGeneration != null)
                    && DateTime.UtcNow < untilPrepared) await Task.Delay(20);
                var current = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == id);
                var composition = InstructionBundleComposer.Compose([], null, desired);
                currentReceipt = await store.WriteAsync(id, new(composition.Text, 1, current.GrokRulesGeneration!.Value), CancellationToken.None);
                h.Runner.SessionResponse = JsonSerializer.Deserialize<SessionRunnerSessionDto>("{}")! with { GrokRulesReceipt = currentReceipt };
                if (transition == "revision")
                {
                    currentReceipt.Path.ShouldBe(oldReceipt!.Path);
                    currentReceipt.Generation.ShouldNotBe(oldReceipt.Generation);
                    currentReceipt.Sha256.ShouldNotBe(oldReceipt.Sha256);
                }
            }
            child.OnSubmitted = async prompt => {
                await BridgeQueueHarness.InsertEntryAsync(id, TranscriptKinds.UserPrompt, prompt, timestamp: DateTime.UtcNow);
                if (currentReceipt is not null)
                {
                    await using var find = BridgeQueueHarness.CreateContext();
                    var refresh = await find.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == id && m.RulesRefreshKey != null);
                    await BridgeQueueHarness.InsertEntryAsync(id, TranscriptKinds.AssistantText,
                        $"ANTIPHON_RULES_ACK id={refresh.Id:N} generation={currentReceipt.Generation:N} sha256={currentReceipt.Sha256}", timestamp: DateTime.UtcNow);
                }
                await BridgeQueueHarness.InsertEntryAsync(id, TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: DateTime.UtcNow);
            };
            factory.Gate.TrySetResult();
            await queue.WaitForIdleAsync(TimeSpan.FromSeconds(40), CancellationToken.None);
            var result = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == id);
            result.Status.ShouldBe(SessionStatus.Running, result.FailureReason);
            child.StartedArgs.ShouldContain(fresh ? "--session-id" : "--resume");
            child.StartedArgs.ShouldContain(id.ToString("D"));
            (await File.ReadAllBytesAsync(historyPath)).ShouldBe(history);
            if (desired is null)
            {
                result.GrokRulesGeneration.ShouldBeNull();
                (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == id && m.RulesRefreshKey != null)).ShouldBe(0);
                child.StartedArgs.ShouldNotContain("--rules");
                if (oldReceipt is not null) (await File.ReadAllBytesAsync(oldReceipt.Path)).ShouldBe(oldBytes);
            }
            else
            {
                result.GrokRulesState.ShouldBe(GrokRulesState.Ready);
                GrokRulesRefreshService.Receipt(result).ShouldBe(currentReceipt);
                child.SubmittedBodies.ShouldHaveSingleItem().ShouldStartWith("[antiphon-grok-rules:");
            }
            await h.Runtime.KillAsync(id, TimeSpan.FromMilliseconds(100), CancellationToken.None);
        }
        finally { factory.Gate.TrySetResult(); if (factory.Created is { } child) child.ReadyResult = false; }
    }

    private sealed class Factory : IAgentProtocolAdapterFactory
    {
        public AgentSessionRuntime Runtime { get; set; } = null!;
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FakeAgentProtocolAdapter? Created { get; private set; }
        public IAgentProtocolAdapter Create(AgentKind kind) => Created = new() { RegisterOnStart = Runtime, StartGate = Gate };
    }
}
