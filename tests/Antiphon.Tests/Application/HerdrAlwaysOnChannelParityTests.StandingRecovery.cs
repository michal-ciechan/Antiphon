using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class HerdrAlwaysOnChannelParityTests
{
    [Test]
    [Arguments(AgentKind.ClaudeCode, SessionBackend.PtyHost, true)]
    [Arguments(AgentKind.Grok, SessionBackend.PtyHost, true)]
    [Arguments(AgentKind.ClaudeCode, SessionBackend.Herdr, true)]
    [Arguments(AgentKind.Grok, SessionBackend.Herdr, true)]
    [Arguments(AgentKind.ClaudeCode, SessionBackend.PtyHost, false)]
    [Arguments(AgentKind.Grok, SessionBackend.PtyHost, false)]
    [Arguments(AgentKind.ClaudeCode, SessionBackend.Herdr, false)]
    [Arguments(AgentKind.Grok, SessionBackend.Herdr, false)]
    public async Task Standing_history_recovery_preserves_native_identity_and_queued_reply(AgentKind kind, SessionBackend backend, bool alwaysOn)
    {
        var root = Path.Combine(Path.GetTempPath(), $"c466-native-wire-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var home = Path.Combine(root, "native-home"); Directory.CreateDirectory(home);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var proofs = new List<Task>();
        await using var fake = backend == SessionBackend.Herdr ? new FakeHerdrServer
        { EchoSendTextToScreen = true, LaunchScriptAgentKind = kind == AgentKind.Grok ? HerdrAgentKinds.Grok : HerdrAgentKinds.Claude } : null;
        if (fake is not null) { fake.Start(); await fake.WaitUntilListeningAsync(); }
        try
        {
            await using var h = BuildHarness(root, backend, fake, [], kind, nativePty: backend == SessionBackend.PtyHost, fixtureTranscriptPump: true);
            var agent = await CreateNativeAgentAsync(h, root, home, kind, backend);
            await using (var settings = CreateContext())
                await settings.Agents.Where(a => a.Id == agent.Id).ExecuteUpdateAsync(a => a.SetProperty(x => x.AlwaysOn, alwaysOn));
            var marker = $"native-history-marker-{Guid.NewGuid():N}";
            var first = await StartNativeAsync(h, agent.Id, new(Fresh: true, Prompt: marker));
            var a = Guid.Parse(first.PersistentSessionId!);
            var directory = NativeDirectory(home, root, a, kind);
            var firstProof = ConfirmNativePromptAsync(fake, directory, a, marker, kind, deadline.Token);
            proofs.Add(firstProof);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(50), deadline.Token); await firstProof;
            await RequireCompletedAsync(a);
            if (fake is not null)
            {
                // FakeHerdr emulates the provider's screen/transcript lane. Seed its native store;
                // the real runner still validates Grok's directory and writes the provider argv.
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, "history-marker.txt"), marker);
            }
            else NativeHistory(directory, kind).ShouldContain(marker);
            await StopNativeAsync(h, agent.Id);
            var second = await StartNativeAsync(h, agent.Id, new(Fresh: true));
            var b = Guid.Parse(second.PersistentSessionId!); b.ShouldNotBe(a);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(50), deadline.Token);
            await RequireCompletedAsync(b); await StopNativeAsync(h, agent.Id);
            var chatId = await BindChannelAsync(h, agent.Id);
            var nonce = $"queued-channel-recovery-{Guid.NewGuid():N}";
            var queuedId = Guid.NewGuid();
            await using (var db = CreateContext())
            {
                db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = queuedId, AgentSessionId = b, Sequence = 1,
                    Body = nonce, Origin = QueuedMessageOrigin.Channel, ConversationKey = $"telegram:{chatId}", CreatedAt = DateTime.UtcNow });
                var current = (await db.Agents.FindAsync(agent.Id))!;
                current.LaunchEnvJson = JsonSerializer.Serialize(NativeEnvironment(home, "current"));
                current.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            var resumedProof = ConfirmNativePromptAsync(fake, directory, a, nonce, kind, deadline.Token);
            proofs.Add(resumedProof);
            await StartNativeAsync(h, agent.Id, new(ResumeSessionId: a));
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(50), deadline.Token); await resumedProof;
            await RequireCompletedAsync(a);
            var requests = h.Runner!.StartRequests;
            await StartNativeAsync(h, agent.Id, new(ResumeSessionId: a));
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), deadline.Token);
            h.Runner.StartRequests.Count.ShouldBe(3, "a repeated selection must not enqueue a second launch");
            requests.Count.ShouldBe(3);
            requests.Select(r => r.SessionId).ShouldBe([a, b, a]);
            requests[^1].Args!.Count(x => x == "--resume").ShouldBe(1);
            requests[^1].Args!.ShouldContain(a.ToString("D"));
            requests[^1].Args!.ShouldNotContain("--session-id");
            requests[^1].Env!["C466_POLICY"].ShouldBe("current");
            if (fake is not null)
            {
                fake.LastLaunchScriptContent.ShouldNotBeNull();
                fake.LastLaunchScriptContent.ShouldContain("'--resume'");
                fake.LastLaunchScriptContent.ShouldContain(a.ToString("D"));
                fake.LastLaunchScriptContent.ShouldNotContain("'--session-id'");
                (await File.ReadAllTextAsync(Path.Combine(directory, "history-marker.txt"))).ShouldBe(marker);
                CountAgentPanes(fake).ShouldBe(1);
            }
            else
            {
                NativeHistory(directory, kind).ShouldContain(marker);
                NativePrompts(directory, kind).Count(x => x.Contains(nonce, StringComparison.Ordinal)).ShouldBe(1);
                (await h.Runner.ListAsync(default)).Count(s => s.ExitCode is null && s.Status != "Exited").ShouldBe(1);
            }
            await using (var verify = CreateContext())
            {
                var moved = (await verify.SessionQueuedMessages.FindAsync(queuedId))!;
                moved.AgentSessionId.ShouldBe(a); moved.Status.ShouldBe(QueuedMessageStatus.Sent);
                moved.DeliveryAttempts.ShouldBe(1); moved.LastDeliveryBaselineSequence.ShouldNotBeNull();
                (await verify.TranscriptEntries.AnyAsync(t => t.AgentSessionId == a && t.Kind == TranscriptKinds.UserPrompt
                    && t.Sequence > moved.LastDeliveryBaselineSequence && t.Text == nonce)).ShouldBeTrue();
                (await verify.Agents.FindAsync(agent.Id))!.PersistentSessionId.ShouldBe(a.ToString("D"));
                (await verify.AgentSessions.FindAsync(b))!.StandingAgentId.ShouldBe(agent.Id);
            }
            await h.Dispatcher.OnTurnEndAsync(a, default);
            var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
            reply.ConversationId.ShouldBe(chatId); reply.Text.ShouldContain(nonce);
            (await h.Dispatcher.PendingCountAsync(a)).ShouldBe(0);
        }
        finally
        {
            deadline.Cancel();
            try { await Task.WhenAll(proofs); } catch (Exception) { /* Observe probe failures before removing their fixture. */ }
            await CleanupAsync(root);
        }
    }

    [Test]
    [Arguments(AgentKind.ClaudeCode, SessionBackend.PtyHost, false)]
    [Arguments(AgentKind.Grok, SessionBackend.PtyHost, false)]
    [Arguments(AgentKind.ClaudeCode, SessionBackend.Herdr, false)]
    [Arguments(AgentKind.Grok, SessionBackend.Herdr, false)]
    [Arguments(AgentKind.Grok, SessionBackend.PtyHost, true)]
    [Arguments(AgentKind.Grok, SessionBackend.Herdr, true)]
    public async Task Standing_native_wire_missing_target_never_creates(AgentKind kind, SessionBackend backend, bool unavailable)
    {
        var root = Path.Combine(Path.GetTempPath(), $"c466-native-missing-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        var home = Path.Combine(root, "native-home"); Directory.CreateDirectory(home);
        if (unavailable) await File.WriteAllTextAsync(Path.Combine(home, "sessions"), "not a directory: synthetic unavailable native storage");
        await using var fake = backend == SessionBackend.Herdr ? new FakeHerdrServer
        { EchoSendTextToScreen = true, LaunchScriptAgentKind = kind == AgentKind.Grok ? HerdrAgentKinds.Grok : HerdrAgentKinds.Claude } : null;
        if (fake is not null) { fake.Start(); await fake.WaitUntilListeningAsync(); }
        try
        {
            await using var h = BuildHarness(root, backend, fake, [], kind, nativePty: backend == SessionBackend.PtyHost, fixtureTranscriptPump: true);
            var agent = await CreateNativeAgentAsync(h, root, home, kind, backend);
            var id = Guid.NewGuid(); var now = DateTime.UtcNow.AddHours(-1);
            await using (var db = CreateContext())
            {
                db.AgentSessions.Add(new AgentSession { Id = id, StandingAgentId = agent.Id, AgentKind = kind, SessionBackend = backend,
                    DefinitionName = "fake", Cwd = root, Status = SessionStatus.Stopped, CreatedAt = now, StartedAt = now, LastSeenAt = now });
                (await db.Agents.FindAsync(agent.Id))!.PersistentSessionId = id.ToString("D"); await db.SaveChangesAsync();
            }
            if (fake is not null && kind == AgentKind.ClaudeCode)
                fake.BeforeRequest = method =>
                {
                    if (method != "pane.read") return;
                    var pane = fake.Workspaces.SelectMany(w => w.Tabs.SelectMany(t => t.Panes)).FirstOrDefault(p => p.Agent is not null);
                    if (pane is not null) fake.SetPaneScreenText(pane.PaneId, $"No conversation found with session ID: {id:D}");
                };
            await StartNativeAsync(h, agent.Id, new(ResumeSessionId: id));
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(50), default);
            var request = h.Runner!.StartRequests.ShouldHaveSingleItem();
            request.Args!.ShouldContain("--resume"); request.Args!.ShouldNotContain("--session-id");
            await using var verify = CreateContext();
            var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
            state.ContinuityReason.ShouldBe(unavailable ? null : StandingContinuityReason.NativeSessionMissing);
            state.ContinuitySessionId.ShouldBe(unavailable ? null : id); state.ConsecutiveFailures.ShouldBe(0); state.NextRestartAt.ShouldBeNull();
            (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == agent.Id)).ShouldBe(1);
            (await verify.AgentSessions.FindAsync(id))!.RestartFailureKind.ShouldBe(unavailable ? RestartFailureKind.Infrastructure : RestartFailureKind.ContinuityUnavailable);
            Directory.Exists(NativeDirectory(home, root, id, kind)).ShouldBeFalse();
            if (!unavailable)
            {
                for (var tick = 0; tick < 2; tick++)
                { h.Clock.Advance(TimeSpan.FromHours(1)); await h.Supervisor().TickAsync(default); }
                h.Runner.StartRequests.Count.ShouldBe(1, "a durable continuity hold must suppress automatic retry and create");
            }
        }
        finally { await CleanupAsync(root); }
    }

    private static Dictionary<string, string> NativeEnvironment(string home, string policy) => new()
    { ["ANTIPHON_FAKE_NATIVE_HOME"] = home, ["GROK_HOME"] = home, ["C466_POLICY"] = policy };

    private static async Task<AgentDetailDto> CreateNativeAgentAsync(Harness h, string root, string home, AgentKind kind, SessionBackend backend)
    {
        File.Exists(kind == AgentKind.Grok ? FakeGrokExe : FakeClaudeExe).ShouldBeTrue("the isolated fake provider must be staged");
        var agent = await h.Agents.CreateAsync(new CreateAgentRequest("Native recovery", root, SessionBackend: backend,
            AlwaysOn: true, RemoteControlEnabled: false), default);
        await using var db = CreateContext(); var stored = (await db.Agents.FindAsync(agent.Id))!;
        stored.Kind = kind; stored.LaunchEnvJson = JsonSerializer.Serialize(NativeEnvironment(home, "original"));
        await db.SaveChangesAsync(); return agent;
    }

    private static async Task<AgentDetailDto> StartNativeAsync(Harness h, Guid agentId, StartAgentRequest request)
    { await using var scope = h.Provider.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(agentId, request, default); }

    private static async Task StopNativeAsync(Harness h, Guid agentId)
    { await using var scope = h.Provider.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(agentId, default); }

    private static async Task RequireCompletedAsync(Guid id)
    {
        await using var db = CreateContext(); var row = (await db.AgentSessions.FindAsync(id))!;
        row.Status.ShouldBe(SessionStatus.Running, row.FailureReason); row.InteractiveLaunchCompletedAt.ShouldNotBeNull();
    }

    private static string NativeDirectory(string home, string cwd, Guid id, AgentKind kind) => kind == AgentKind.Grok
        ? Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), id.ToString("D")) : Path.Combine(home, id.ToString("D"));

    private static string NativeHistory(string directory, AgentKind kind) => File.ReadAllText(Path.Combine(directory,
        kind == AgentKind.Grok ? "chat_history.jsonl" : "conversation.jsonl"));

    private static IReadOnlyList<TranscriptPart> NativeParts(string directory, AgentKind kind)
    {
        var path = Path.Combine(directory, kind == AgentKind.Grok ? "updates.jsonl" : "conversation.jsonl");
        if (!File.Exists(path)) return [];
        var grok = new GrokTranscriptNormalizer(); var parts = new List<TranscriptPart>();
        foreach (var line in File.ReadAllLines(path)) parts.AddRange(kind == AgentKind.Grok ? grok.Normalize(line) : TranscriptNormalizer.Normalize(line));
        if (kind == AgentKind.Grok) parts.AddRange(grok.FlushPending());
        return parts;
    }

    private static IEnumerable<string> NativePrompts(string directory, AgentKind kind) =>
        NativeParts(directory, kind).Where(p => p.Kind == TranscriptKinds.UserPrompt).Select(p => p.Text ?? "");

    private static async Task ConfirmNativePromptAsync(FakeHerdrServer? fake, string directory, Guid id, string body, AgentKind kind, CancellationToken ct)
    {
        if (fake is not null)
        {
            await ConfirmHerdrDeliveryAsync(fake, id, body);
            await InsertEntryAsync(id, TranscriptKinds.AssistantText, $"Fake provider reply: {body}", timestamp: DateTime.UtcNow);
            await InsertEntryAsync(id, TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: DateTime.UtcNow);
            return;
        }
        // The actual fake CLI's JSONL is the evidence. Like the established Pty delivery fixture,
        // this pump imports only its owning file; no user home or production tailer is involved.
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<TranscriptPart> parts;
            try { parts = NativeParts(directory, kind); } catch (IOException) { await Task.Delay(50, ct); continue; }
            var prompt = parts.FirstOrDefault(p => p.Kind == TranscriptKinds.UserPrompt && p.Text?.Contains(body, StringComparison.Ordinal) == true);
            var answer = parts.LastOrDefault(p => p.Kind == TranscriptKinds.AssistantText && p.Text?.Contains(body, StringComparison.Ordinal) == true);
            if (prompt.Text is not null && answer.Text is not null)
            {
                await InsertEntryAsync(id, TranscriptKinds.UserPrompt, prompt.Text, timestamp: DateTime.UtcNow);
                await InsertEntryAsync(id, TranscriptKinds.AssistantText, answer.Text, timestamp: DateTime.UtcNow);
                await InsertEntryAsync(id, TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: DateTime.UtcNow);
                return;
            }
            await Task.Delay(50, ct);
        }
    }
}
