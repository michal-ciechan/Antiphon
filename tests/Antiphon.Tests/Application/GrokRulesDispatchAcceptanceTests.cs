using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

// The wire arm uses the existing disclosed HTTP relay and production catch-up pump.
// It cannot certify the separate live mapped-Program/model-compliance gates.
[Explicit]
[Category("Integration")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesDispatchAcceptanceTests
{
    [Test]
    [Timeout(360_000)]
    public async Task Real_cli_delegate_wire_reads_full_rules_before_brief_and_settles(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable(RealCliStubGate.EnvFlag) != "1"
            || Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1")
            throw new SkipTestException("Windows, ANTIPHON_HEADED_TESTS=1 and ANTIPHON_REAL_CLI_STUB_TESTS=1 required");
        var root = Path.Combine(DelegateScriptRunner.RepoRoot, "tests", "Antiphon.Tests", "TestOutput", "Logs",
            nameof(GrokRulesDispatchAcceptanceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var home = Path.Combine(root, "home"); Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"),
            "[cli]\nuse_leader = false\n[compat.claude]\nhooks = false\nmcps = false\nrules = false\nskills = false\n[compat.codex]\nhooks = false\nskills = false\n", ct);
        using var git = await RealCliStubBServerHarness.GitRepo.CreateAsync();
        var nonce = "CARD0395-WIRE-" + Guid.NewGuid().ToString("N");
        var goal = nonce + "-HEAD\nPerform this disposable transport check.\n\n"
            + string.Join("\n\n", Enumerable.Range(1, 80).Select(i => (i == 40 ? nonce + "-MIDDLE\n" : "") + $"Paragraph {i:D3}: preserve the quoted phrase \"neutral transport material\". No external action is requested."))
            + "\nFinish by reporting all three task markers.\n" + nonce + "-TAIL";
        var goalPath = Path.Combine(root, "brief-goal.md");
        await File.WriteAllTextAsync(goalPath, goal, new UTF8Encoding(false), ct);
        Encoding.UTF8.GetByteCount(goal).ShouldBeGreaterThanOrEqualTo(5000);
        var synthetic = "card0395-synthetic-" + Guid.NewGuid().ToString("N");
        await using var stub = await FakeLlmApiServer.StartAsync(new() { Grok = true });
        var env = RealCliStubEnv.ForGrok(stub.BaseUrl, synthetic).Env.ToDictionary(p => p.Key, p => p.Value);
        // Empty inherited provider routing; the child overlay supplies both sanctioned redirects.
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if ((name.StartsWith("GROK_", StringComparison.OrdinalIgnoreCase)
                 || name.StartsWith("XAI_", StringComparison.OrdinalIgnoreCase)
                 || name.StartsWith("X_LLM_", StringComparison.OrdinalIgnoreCase)) && !env.ContainsKey(name)) env[name] = "";
        }
        env["GROK_HOME"] = home;
        env["GROK_AUTH_PATH"] = Path.Combine(home, "synthetic-auth-unused.json");
        env["GROK_AUTH_PROVIDER_COMMAND"] = "echo card0395-synthetic-auth";
        env["GROK_AUTH_TOKEN_TTL"] = "3600";
        env["GROK_DISABLE_AUTO_UPDATER"] = "1";
        env["GROK_CLI_ENABLE_TELEMETRY"] = "0";
        env["GROK_CLI_ENABLE_FEEDBACK"] = "0";
        var grok = RealCliStubGate.ResolveGrokOrThrow();
        await using var h = GrokDelegateEndToEndTests.BuildHarness(git.RepoPath, home, grok,
            grokEnvironment: env, fakeReportLine: false);
        var captured = new List<string>();
        var captureLock = new object();
        Guid taskId = Guid.Empty, sessionId = Guid.Empty;
        string? rulesPath = null;
        var started = DateTimeOffset.UtcNow; var clock = Stopwatch.StartNew();
        stub.Script.SetResponder(StubEndpointKeys.GrokResponses, request =>
        {
            using var json = JsonDocument.Parse(request);
            if (!json.RootElement.TryGetProperty("tools", out var tools) || tools.GetArrayLength() < 2)
                return new ScriptedTextTurn("neutral title");
            var input = json.RootElement.GetProperty("input");
            var text = string.Join("\n", input.EnumerateArray().SelectMany(TextValues));
            var latestUser = input.EnumerateArray().LastOrDefault(i => i.TryGetProperty("role", out var r) && r.GetString() == "user");
            var current = latestUser.ValueKind == JsonValueKind.Undefined ? text : string.Join("\n", TextValues(latestUser.GetProperty("content")));
            if (!current.StartsWith("<user_query>", StringComparison.Ordinal)) return new ScriptedTextTurn("neutral dashboard summary");
            lock (captureLock) { captured.Add(request); File.WriteAllText(Path.Combine(root, $"user-request-{captured.Count:D3}.json"), request); }
            var outputs = input.EnumerateArray().Where(i => i.TryGetProperty("type", out var t) && t.GetString() == "function_call_output").ToList();
            if (current.Contains("[antiphon-grok-rules:"))
            {
                rulesPath = Regex.Match(current, "standing rules file \\\"([^\\\"]+)\\\"").Groups[1].Value;
                if (!outputs.Any(i => i.ToString().Contains("card0395-rules-read")))
                    return new ScriptedFunctionCall("read_file", JsonSerializer.Serialize(new { target_file = rulesPath, limit = 2000 }), "card0395-rules-read");
                if (outputs.Any(i => i.ToString().Contains("card0395-rules-read") && i.ToString().Contains("Error")))
                    return new ScriptedTextTurn(Regex.Match(current, @"ANTIPHON_RULES_FAILED id=[a-f0-9]{32} generation=[a-f0-9]{32}").Value + " reason=unreadable");
                return new ScriptedTextTurn(Regex.Match(current, @"ANTIPHON_RULES_ACK id=[a-f0-9]{32} generation=[a-f0-9]{32} sha256=[a-f0-9]{64}").Value);
            }
            if (!text.Contains(nonce))
            {
                // A spilled brief must be read by the actual native tool before the task oracle can fire.
                var path = Regex.Match(current, @"[A-Za-z]:\\[^\r\n""']+?\.md").Value;
                if (path.Length > 0 && !outputs.Any(i => i.ToString().Contains("card0395-brief-read")))
                    return new ScriptedFunctionCall("read_file", JsonSerializer.Serialize(new { target_file = path, limit = 2000 }), "card0395-brief-read");
                return new ScriptedTextTurn("Unrecognized wire task; no acceptance report.");
            }
            return new ScriptedTextTurn($"{nonce}-HEAD {nonce}-MIDDLE {nonce}-TAIL\n--- next stage ---\nnext: none\nhandoff: Wire acceptance only.\n[antiphon-report:{taskId.ToString("N")[..8]} done]");
        });
        using var pump = new CancellationTokenSource();
        Task? pumping = null;
        string outcome = "failed";
        try
        {
            using var relay = new DelegateTaskApiRelay(git.RepoPath, h.Delegation);
            var wrapper = Path.Combine(root, "dispatch.ps1");
            // Paths alone cross native argv. The complete goal is read and supplied in-process.
            await File.WriteAllTextAsync(wrapper, "param($Script,$GoalFile,$Repo)\n$goalText=Get-Content -LiteralPath $GoalFile -Raw\n& $Script Code -Kind Grok -Level High -Dir $Repo -Worktree -ReadOnly -Goal $goalText -Title 'CARD-0395 wire acceptance' -NoInheritEnv\nexit $LASTEXITCODE\n", ct);
            var psi = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-File", wrapper,
                Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "delegate.ps1"), goalPath, git.RepoPath }) psi.ArgumentList.Add(a);
            psi.Environment["ANTIPHON_API"] = relay.BaseUrl.TrimEnd('/');
            psi.Environment["ANTIPHON_TASK_TOKEN"] = "";
            using (var process = Process.Start(psi)!)
            {
                var stdout = process.StandardOutput.ReadToEndAsync(ct); var stderr = process.StandardError.ReadToEndAsync(ct);
                try { await process.WaitForExitAsync(ct); }
                finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
                var output = await stdout + await stderr;
                await File.WriteAllTextAsync(Path.Combine(root, "delegate.log"), output, ct);
                process.ExitCode.ShouldBe(0, output + relay.LastFailure);
                output.ShouldContain("[Grok]");
            }
            await using (var db = Db())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.WorkingDirectory == git.RepoPath, ct);
                taskId = task.Id; task.Goal.ShouldBe(goal); task.AgentKind.ShouldBe(AgentKind.Grok);
            }
            using (var scope = h.Provider.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            await using (var db = Db())
            {
                var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
                task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
                sessionId = task.AgentSessionId.ShouldNotBeNull();
            }
            pumping = GrokDelegateEndToEndTests.PumpTranscriptAsync(h.Provider, sessionId, pump.Token);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromMinutes(3), ct);
            await using (var initialized = Db())
            {
                var state = await initialized.AgentSessions.SingleAsync(s => s.Id == sessionId, ct);
                state.GrokRulesState.ShouldBe(GrokRulesState.Ready, state.GrokRulesFailure);
            }
            await GrokDelegateEndToEndTests.WaitUntilAsync(async () =>
            {
                await using var db = Db();
                return await db.AgentTasks.AnyAsync(t => t.Id == taskId && t.Status == AgentTaskStatus.Succeeded, ct);
            }, TimeSpan.FromSeconds(90), async () =>
            {
                await using var db = Db(); var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
                var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId, ct);
                return $"{root}: task={task.Status} {task.FailureReason}; rules={session.GrokRulesState} {session.GrokRulesFailure}";
            });
            await using var verify = Db();
            var settled = await verify.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
            settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
            var refresh = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == sessionId && m.RulesRefreshKey != null, ct);
            refresh.RulesAcknowledgedAt.ShouldNotBeNull();
            var brief = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == sessionId && m.RulesRefreshKey == null, ct);
            brief.CreatedAt.ShouldBeGreaterThanOrEqualTo(refresh.RulesAcknowledgedAt!.Value);
            var spec = h.Runner.SpecFor(sessionId).ShouldNotBeNull();
            var rules = spec.GrokRulesPayload.ShouldNotBeNull().Content;
            rules.ShouldContain(InstructionBundles.Get(InstructionBundles.DelegateBasics).Text);
            rules.ShouldContain(InstructionBundles.Get(InstructionBundles.StageKeyFor(AgentTaskRole.Code)).Text);
            spec.Args.ShouldNotContain("--agent"); spec.Args.ShouldNotContain("--rules");
            var all = captured.ToArray();
            var toolText = string.Join("\n", all.SelectMany(FunctionOutputs));
            foreach (var line in rules.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !string.IsNullOrWhiteSpace(l)))
                toolText.ShouldContain(line, customMessage: "Native rules output must cover every composed line");
            all.Any(r => r.Contains(nonce + "-TAIL") && r.Contains("\"tools\"")).ShouldBeTrue("Full task tail must reach a tool-bearing user request");
            stub.Requests.All.Any(r => r.Method == "GET" && r.Path == "/api-key"
                && r.Headers.TryGetValue("Authorization", out var auth) && auth.Contains("Bearer " + synthetic)).ShouldBeTrue();
            await File.WriteAllTextAsync(Path.Combine(root, "rules.md"), rules, ct);
            var briefPath = Regex.Match(brief.Body, @"[A-Za-z]:\\[^\r\n""']+?\.md").Value;
            await File.WriteAllTextAsync(Path.Combine(root, "brief.md"), briefPath.Length > 0 ? await File.ReadAllTextAsync(briefPath, ct) : brief.Body, ct);
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId, ct);
            var receipt = GrokRulesRefreshService.Receipt(session).ShouldNotBeNull();
            rulesPath = receipt.Path;
            await File.WriteAllTextAsync(Path.Combine(root, "timeline.json"), JsonSerializer.Serialize(new {
                receipt, rulesQueueId = refresh.Id, refresh.CreatedAt, refresh.SentAt, refresh.DeliveryVerdict,
                refresh.RulesAcknowledgedAt, briefQueueId = brief.Id, briefCreatedAt = brief.CreatedAt,
                briefSentAt = brief.SentAt, briefVerdict = brief.DeliveryVerdict, settled.Status, settled.ReportEvidence,
                settled.CostUsd, settled.TokensIn, settled.TokensOut, settled.CompletedAt,
                argvLengths = spec.Args.Select(a => a.Length).ToArray(), capability = GrokRulesTransport.Capability,
                codeCommit = "349650c3 + acceptance test changes", rulesSha256 = receipt.Sha256,
                goalSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(goal))).ToLowerInvariant()
            }), ct);
            outcome = "passed";
        }
        finally
        {
            clock.Stop(); pump.Cancel(); if (pumping is not null) await pumping;
            if (sessionId != Guid.Empty)
            {
                var transcript = await h.Runner.GetTranscriptAsync(sessionId, CancellationToken.None);
                await File.WriteAllTextAsync(Path.Combine(root, "transcript.json"), JsonSerializer.Serialize(transcript));
                await h.Runner.KillAsync(sessionId, CancellationToken.None);
            }
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new {
                outcome, started, ended = DateTimeOffset.UtcNow, elapsedSeconds = clock.Elapsed.TotalSeconds,
                taskId, sessionId, nativeSessionId = sessionId, rulesPath, backend = "PtyHost/modern", model = "grok-4.6",
                cliSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(grok))).ToLowerInvariant(),
                requestCount = captured.Count, evidence = "zero-spend HTTP relay + production service graph; not live compliance" }));
        }
    }

    private static AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions());
    private static IEnumerable<string> TextValues(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.String) yield return node.GetString()!;
        else if (node.ValueKind == JsonValueKind.Object)
            foreach (var p in node.EnumerateObject()) foreach (var value in TextValues(p.Value)) yield return value;
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) foreach (var value in TextValues(child)) yield return value;
    }
    private static IEnumerable<string> FunctionOutputs(string request)
    {
        using var document = JsonDocument.Parse(request);
        return document.RootElement.GetProperty("input").EnumerateArray()
            .Where(i => i.TryGetProperty("type", out var type) && type.GetString() == "function_call_output")
            .SelectMany(TextValues).ToArray();
    }
}
