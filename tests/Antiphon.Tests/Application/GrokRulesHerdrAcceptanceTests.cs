using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

[Explicit]
[Category("Integration")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesHerdrAcceptanceTests
{
    [Test, Timeout(600_000)]
    public Task Real_cli_herdr_standing_attachment_reads_rules_before_work(CancellationToken ct) => RunAsync(true, ct);

    [Test, Timeout(600_000)]
    public Task Live_model_herdr_standing_attachment_obeys_rules_after_startup_ack(CancellationToken ct) => RunAsync(false, ct);

    private static async Task RunAsync(bool wire, CancellationToken ct)
    {
        var sessionName = Environment.GetEnvironmentVariable("ANTIPHON_GROK_TEST_HERDR_SESSION");
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1"
            || Environment.GetEnvironmentVariable(wire ? RealCliStubGate.EnvFlag : "ANTIPHON_GROK_RULES_LIVE_TESTS") != "1"
            || string.IsNullOrWhiteSpace(sessionName))
            throw new SkipTestException("Explicit headed/wire-or-live opt-ins and a dedicated named Herdr test server are required");
        sessionName.ShouldStartWith("card0395-", customMessage: "Never address the default/production Herdr server");
        var root = Path.Combine(DelegateScriptRunner.RepoRoot, "tests/Antiphon.Tests/TestOutput/Logs", nameof(GrokRulesHerdrAcceptanceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var home = Path.Combine(root, "home"); Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"), "[cli]\nuse_leader = false\n[compat.claude]\nhooks = false\nmcps = false\nrules = false\nskills = false\n[compat.codex]\nhooks = false\nskills = false\n", ct);
        using var repo = await RealCliStubBServerHarness.GitRepo.CreateAsync();
        await using var stub = wire ? await FakeLlmApiServer.StartAsync(new() { Grok = true }) : null;
        var env = new Dictionary<string,string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (key.StartsWith("GROK_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("XAI_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("X_LLM_", StringComparison.OrdinalIgnoreCase)) env[key] = "";
        }
        var synthetic = "card0395-herdr-" + Guid.NewGuid().ToString("N");
        if (stub is not null)
        {
            foreach (var pair in RealCliStubEnv.ForGrok(stub.BaseUrl, synthetic).Env) env[pair.Key] = pair.Value;
            env["GROK_AUTH_PROVIDER_COMMAND"] = "echo card0395-synthetic-auth";
            env["GROK_AUTH_TOKEN_TTL"] = "3600";
        }
        env["GROK_HOME"] = home;
        env["GROK_AUTH_PATH"] = wire ? Path.Combine(home, "synthetic-unused.json")
            : Environment.GetEnvironmentVariable("ANTIPHON_GROK_TEST_AUTH_PATH") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok/auth.json");
        if (!wire) File.Exists(env["GROK_AUTH_PATH"]).ShouldBeTrue("Existing native authentication is required");
        env["GROK_DISABLE_AUTO_UPDATER"] = "1";
        var herdr = new HerdrClient(Options.Create(new HerdrSettings { Enabled = true, Session = sessionName }));
        await using var runner = new DirectSessionRunnerClient(Path.Combine(root, "runner"), ptyBackend: "modern", herdrClient: herdr);
        await using var factory = new GrokRulesLiveMappedDispatchTests.LiveFactory(runner, repo, env, RealCliStubGate.ResolveGrokOrThrow());
        using var http = factory.CreateClient();
        using var forwarder = new GrokRulesLiveMappedDispatchTests.MappedForwarder(http);
        var nonce = "HERDR-" + Guid.NewGuid().ToString("N");
        var keys = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
        var answers = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
        string Rule(int i) => $"For acceptance challenge {keys[i]}, include exactly this response value in your report: {answers[i]}.";
        var append = ChannelPreamble.TelegramPresetTemplate + "\n" + Rule(0) + "\n"
            + string.Join("\n", Enumerable.Range(1, 1105).Select(i => (i == 550 ? Rule(1) + "\n" : "") + $"NEUTRAL-LINE-{i:D4} disposable acceptance material.")) + "\n" + Rule(2);
        Guid agentId;
        using (var scope = factory.Services.CreateScope())
        {
            var agent = await scope.ServiceProvider.GetRequiredService<AgentService>().CreateAsync(new CreateAgentRequest(
                "card0395-" + Guid.NewGuid().ToString("N"), repo.RepoPath, ModelLevel: AgentModelLevel.High,
                ModelId: "grok-4.6", ReplyStyle: AgentReplyStyle.Explanatory, SessionBackend: SessionBackend.Herdr,
                BundleKeys: [InstructionBundles.Orchestrator], SystemPromptAppend: append), ct);
            agentId = agent.Id;
        }
        string expectedRules;
        await using (var db = factory.Db())
        {
            var agent = await db.Agents.SingleAsync(a => a.Id == agentId, ct);
            agent.Kind = AgentKind.Grok; // Profile-less fixture uses the explicit Grok registry definition.
            db.ChatChannels.Add(new ChatChannel { Id = Guid.NewGuid(), Provider = "telegram", ExternalId = nonce,
                ReplyHandle = nonce, Kind = ChatChannelKind.Direct, Title = "Bound channel (test)", AgentId = agentId,
                Enabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            expectedRules = ChannelPreamble.Render(InstructionBundleComposer.Compose([InstructionBundles.Orchestrator],
                AgentReplyStyles.ComposedKey(agent.ReplyStyle), append).Text, agent.Name, [("telegram", "Bound channel (test)")]);
        }
        var goal = nonce + "-HEAD\nComplete these acceptance challenges: " + string.Join(", ", keys)
            + ". Include the HEAD/MIDDLE/TAIL markers. Make no changes and no external calls.\n"
            + string.Join("\n\n", Enumerable.Range(1, 75).Select(i => (i == 38 ? nonce + "-MIDDLE\n" : "") + $"Neutral paragraph {i:D3}: \"disposable acceptance material\" exercises a full multiline brief with quotes and paragraphs.")) + "\n" + nonce + "-TAIL";
        await File.WriteAllTextAsync(Path.Combine(root, "goal.md"), goal, ct);
        await File.WriteAllTextAsync(Path.Combine(root, "rules.md"), expectedRules, ct);
        await File.WriteAllTextAsync(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(new { keys, answers }), ct);
        var currentRulesPath = "";
        if (stub is not null) stub.Script.SetResponder(StubEndpointKeys.GrokResponses, request =>
        {
            using var json = JsonDocument.Parse(request); var node = json.RootElement;
            if (!node.TryGetProperty("tools", out var tools) || tools.GetArrayLength() <= 1) return new ScriptedTextTurn("isolated helper");
            var input = node.GetProperty("input").EnumerateArray().ToArray();
            var user = input.LastOrDefault(i => i.TryGetProperty("role", out var role) && role.GetString() == "user");
            var text = user.ValueKind == JsonValueKind.Undefined ? "" : string.Join("\n", Strings(user.GetProperty("content")));
            if (!text.TrimStart().StartsWith("<user_query>")) return new ScriptedTextTurn("isolated helper");
            if (text.Contains("ANTIPHON_RULES_ACK id="))
            {
                currentRulesPath = Regex.Match(text, "standing rules file \\\"([^\\\"]+)\\\"").Groups[1].Value;
                var outputs = input.Where(i => i.TryGetProperty("type", out var type) && type.GetString() == "function_call_output").ToArray();
                if (!outputs.Any(i => i.ToString().Contains("herdr-rules-read")))
                    return new ScriptedFunctionCall("read_file", JsonSerializer.Serialize(new { target_file = currentRulesPath, limit = 1000 }), "herdr-rules-read");
                if (!outputs.Any(i => i.ToString().Contains("herdr-rules-tail")))
                    return new ScriptedFunctionCall("read_file", JsonSerializer.Serialize(new { target_file = currentRulesPath, offset = 1001, limit = 1000 }), "herdr-rules-tail");
                return new ScriptedTextTurn(Regex.Match(text, "ANTIPHON_RULES_ACK id=[a-f0-9]+ generation=[a-f0-9]+ sha256=[a-f0-9]+").Value);
            }
            var brief = Regex.Match(text, "[A-Za-z]:[^'\\\"\\r\\n<>]*task-[a-f0-9]+-brief\\.md").Value;
            if (brief.Length > 0 && !(input.Last().TryGetProperty("type", out var lastType) && lastType.GetString() == "function_call_output"))
                return new ScriptedFunctionCall("read_file", JsonSerializer.Serialize(new { target_file = brief, limit = 2000 }), "herdr-brief-read");
            var token = Regex.Match(text, @"antiphon-task:([a-f0-9]+)").Groups[1].Value;
            return new ScriptedTextTurn(string.Join(" ", answers) + " " + nonce + "-HEAD " + nonce + "-MIDDLE " + nonce + "-TAIL\n[antiphon-report:" + token + " done]");
        });
        Guid sessionId = Guid.Empty, taskId = Guid.Empty; string outcome = "failed"; string? paneId = null;
        var began = DateTimeOffset.UtcNow; var elapsed = Stopwatch.StartNew();
        using var pump = new CancellationTokenSource(); Task? pumping = null;
        try
        {
            using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(agentId, new(), ct);
            await using (var db = factory.Db()) sessionId = Guid.Parse((await db.Agents.SingleAsync(a => a.Id == agentId, ct)).PersistentSessionId!);
            pumping = GrokDelegateEndToEndTests.PumpTranscriptAsync(factory.Services, sessionId, pump.Token);
            await factory.Services.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromMinutes(4), ct);
            await using (var db = factory.Db())
            {
                var readySession = await db.AgentSessions.SingleAsync(s => s.Id == sessionId, ct);
                var standing = await db.Agents.SingleAsync(a => a.Id == agentId, ct);
                await File.WriteAllTextAsync(Path.Combine(root, "ready-state.json"), JsonSerializer.Serialize(new {
                    readySession.Id, readySession.Status, readySession.GrokRulesState, readySession.FailureReason,
                    standing.PersistentSessionId, standing.Kind, standing.IsPoolDelegate
                }), ct);
                readySession.GrokRulesState.ShouldBe(GrokRulesState.Ready);
                readySession.Status.ShouldBe(SessionStatus.Running, readySession.FailureReason);
                standing.PersistentSessionId.ShouldBe(sessionId.ToString("D"));
                standing.IsPoolDelegate.ShouldBeFalse();
            }
            var sidecarPath = Path.Combine(root, "runner", "herdr", sessionId.ToString("N") + ".json");
            var sidecar = JsonSerializer.Deserialize<HerdrPaneSidecar>(await File.ReadAllTextAsync(sidecarPath, ct), new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull();
            paneId = sidecar.PaneId; sidecar.ChildPid.ShouldNotBeNull();
            var detectionUpperBound = sidecar.LaunchedAtUtc - began.UtcDateTime;
            detectionUpperBound.ShouldBeLessThan(TimeSpan.FromSeconds(60), "includes server start overhead before native detection");
            await File.WriteAllTextAsync(Path.Combine(root, "detection.json"), JsonSerializer.Serialize(new { began, sidecar.LaunchedAtUtc, elapsedUpperBoundSeconds = detectionUpperBound.TotalSeconds, configuredDeadlineSeconds = 60 }), ct);
            (await herdr.PaneGetAsync(paneId, ct)).Agent.ShouldBe("grok");
            await File.WriteAllTextAsync(Path.Combine(root, "sidecar.json"), await File.ReadAllTextAsync(sidecarPath, ct), ct);
            var wrapper = Path.Combine(root, "dispatch.ps1");
            await File.WriteAllTextAsync(wrapper, "param($Script,$GoalFile,$Repo,$AgentId)\n$goalText=Get-Content -LiteralPath $GoalFile -Raw\n& $Script Code -Kind Grok -Level High -Dir $Repo -Agent $AgentId -Shared -Goal $goalText -NoInheritEnv\nexit $LASTEXITCODE\n", ct);
            var psi = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", wrapper, Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/delegate.ps1"), Path.Combine(root, "goal.md"), repo.RepoPath, agentId.ToString() }) psi.ArgumentList.Add(arg);
            psi.Environment["ANTIPHON_API"] = forwarder.BaseUrl.TrimEnd('/'); psi.Environment["ANTIPHON_TASK_TOKEN"] = "";
            using (var process = Process.Start(psi)!)
            {
                var stdout = process.StandardOutput.ReadToEndAsync(ct); var stderr = process.StandardError.ReadToEndAsync(ct);
                try { await process.WaitForExitAsync(ct); } finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
                var output = await stdout + await stderr; await File.WriteAllTextAsync(Path.Combine(root, "delegate.log"), output, ct);
                process.ExitCode.ShouldBe(0, output); output.ShouldContain("[Grok]");
            }
            await using (var db = factory.Db()) taskId = (await db.AgentTasks.SingleAsync(t => t.AgentId == agentId, ct)).Id;
            using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            await using (var db = factory.Db())
            {
                var dispatched = await db.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
                dispatched.AgentSessionId.ShouldBe(sessionId, "dispatch must keep the standing Herdr session and its composed rules");
                (await db.AgentSessions.SingleAsync(s => s.Id == sessionId, ct)).SessionBackend.ShouldBe(SessionBackend.Herdr);
            }
            await GrokDelegateEndToEndTests.WaitUntilAsync(async () => { await using var db = factory.Db(); return await db.AgentTasks.AnyAsync(t => t.Id == taskId && t.Status == AgentTaskStatus.Succeeded, ct); }, TimeSpan.FromMinutes(3), () => Task.FromResult(root + ": no marked settlement"));
            await using var verify = factory.Db(); var settled = await verify.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
            settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
            foreach (var answer in answers.Concat(new[] { nonce + "-HEAD", nonce + "-MIDDLE", nonce + "-TAIL" })) settled.Result.ShouldContain(answer);
            var rows = await verify.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId).ToListAsync(ct);
            var refresh = rows.Single(m => m.RulesRefreshKey != null); refresh.RulesAcknowledgedAt.ShouldNotBeNull();
            rows.Single(m => m.RulesRefreshKey == null).CreatedAt.ShouldBeGreaterThanOrEqualTo(refresh.RulesAcknowledgedAt!.Value);
            factory.Messaging.SentReplies.ShouldAllBe(r => !r.Text.Contains("ANTIPHON_RULES_ACK"));
            (await herdr.PaneGetAsync(paneId, ct)).Agent.ShouldBe("grok", "pane remains owned through report");
            var native = string.Join("\n", Directory.EnumerateFiles(home, "updates.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText));
            var toolsText = string.Join("\n", native.Split('\n').Where(l => l.Contains("tool_call_update")).SelectMany(l => { using var j = JsonDocument.Parse(l); return Strings(j.RootElement).ToArray(); }));
            foreach (var line in expectedRules.ReplaceLineEndings("\n").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l))) toolsText.ShouldContain(line);
            await File.WriteAllTextAsync(Path.Combine(root, "result.md"), settled.Result!, ct);
            await File.WriteAllTextAsync(Path.Combine(root, "timeline.json"), JsonSerializer.Serialize(new { refresh.RulesAcknowledgedAt, settled.CompletedAt, settled.Status, settled.TokensIn, settled.TokensOut, settled.CostUsd }), ct);
            if (stub is not null)
            {
                stub.Requests.All.Any(r => r.Path == "/api-key").ShouldBeTrue();
                stub.Requests.All.Any(r => r.Body.Contains(nonce + "-TAIL") && r.Body.Contains("\"tools\"")).ShouldBeTrue();
            }
            outcome = "passed";
        }
        finally
        {
            pump.Cancel(); if (pumping is not null) await pumping;
            if (sessionId != Guid.Empty)
            {
                try { await File.WriteAllTextAsync(Path.Combine(root, "transcript.json"), JsonSerializer.Serialize(await runner.GetTranscriptAsync(sessionId, CancellationToken.None))); await File.WriteAllTextAsync(Path.Combine(root, "snapshot.json"), JsonSerializer.Serialize(await runner.GetSnapshotAsync(sessionId, CancellationToken.None))); }
                finally { await runner.KillAsync(sessionId, CancellationToken.None); }
            }
            await File.WriteAllTextAsync(Path.Combine(root, "native-acp.jsonl"), string.Join("\n", Directory.EnumerateFiles(home, "updates.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText)));
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new { wire, outcome, began, ended = DateTimeOffset.UtcNow, elapsedSeconds = elapsed.Elapsed.TotalSeconds, sessionId, taskId, agentId, paneId, sessionName }));
        }
    }

    private static IEnumerable<string> Strings(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.String) yield return node.GetString()!;
        else if (node.ValueKind == JsonValueKind.Array) foreach (var c in node.EnumerateArray()) foreach (var s in Strings(c)) yield return s;
        else if (node.ValueKind == JsonValueKind.Object) foreach (var p in node.EnumerateObject()) foreach (var s in Strings(p.Value)) yield return s;
    }
}
