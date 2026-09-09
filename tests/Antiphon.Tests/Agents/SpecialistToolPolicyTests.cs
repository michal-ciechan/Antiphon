using System.Text;
using System.Text.Json;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

// Synthetic endpoint/authentication only. This measures capability, never behavioral qualification.
[Explicit]
[Category("Integration")]
[Category("RealCliStubProxy")]
[NotInParallel(["Headed", "RealCliStubProxy"])]
[ParallelLimiter<ProcessSpawnLimit>]
public class SpecialistToolPolicyTests
{
    [Test]
    [Timeout(180_000)]
    [Arguments(0)]
    [Arguments(1)]
    public Task Card0415_V05_Claude_full_envelope_through_production_launch_queue_and_native_transcript(int spareByte) =>
        RunAsync(true, null, spareByte);

    [Test]
    [Timeout(180_000)]
    [Arguments("Read", true)]
    [Arguments("Read", false)]
    [Arguments("Write", true)]
    [Arguments("Write", false)]
    [Arguments("Bash", true)]
    [Arguments("Bash", false)]
    [Arguments("mcp__fixture__receipt", true)]
    [Arguments("mcp__fixture__receipt", false)]
    public Task Card0415_V05_Claude_native_tool_denial_and_disposable_receipt_controls(string tool, bool protectedSeat) =>
        RunAsync(protectedSeat, tool, 0);

    [Test]
    [Timeout(300_000)]
    [Arguments("small")]
    [Arguments("body-1024")]
    [Arguments("body-2048")]
    [Arguments("fixture-3466")]
    [Arguments("fixture-5166")]
    [Arguments("full-facts")]
    [Arguments("ascii-M")]
    [Arguments("ascii-M-minus-one")]
    [Arguments("multi-M")]
    [Arguments("multi-M-minus-one")]
    [Arguments("ascii-M-plus-one")]
    [Arguments("multi-M-plus-one")]
    public Task Card0415_V05_Claude_finite_grid_fresh_and_warm(string shape) => RunAsync(true, null, 0, shape, 2);

    [Test]
    [Timeout(180_000)]
    public Task Card0415_V05_Claude_warm_small_diagnostic() => RunAsync(true, null, 0, "small", 2);

    private static async Task RunAsync(bool protectedSeat, string? tool, int spareByte, string? gridShape = null, int repetitions = 1)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.ClaudeCode);
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c415-capability", Guid.NewGuid().ToString("N"));
        var cwd = Path.Combine(root, "seat");
        var config = Path.Combine(root, "config");
        Directory.CreateDirectory(cwd);
        var previousConfig = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", config);
        try
        {
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var stub = await FakeLlmApiServer.StartAsync(new() { Claude = true });
            var key = "synthetic-c415-" + Guid.NewGuid().ToString("N");
            var nonce = "C415-NONCE-" + Guid.NewGuid().ToString("N");
            var secretReceipt = "DISPOSABLE-RECEIPT-" + Guid.NewGuid().ToString("N");
            var receiptPath = Path.Combine(cwd, "receipt.txt");
            if (tool == "Read") File.WriteAllText(receiptPath, secretReceipt);
            RealCliStubClaudeConfig.SeedOnboarding(config, key, cwd);
            var overlay = RealCliStubEnv.ForClaude(stub.BaseUrl, key, config);
            var (exe, prefix) = HeadedClaudeGate.BuildLaunch(RealCliStubGate.ResolveClaudeOrThrow());
            var registry = new AgentRegistrySettings
            {
                DefaultDefinition = "c415-stub", ClaudeReadyQuietPeriodMs = 1500,
                ClaudeReadyMinTotalWaitMs = 4000, ClaudeReadyMaxWaitMs = 45000,
                Definitions = { ["c415-stub"] = new AgentDefinition
                {
                    Kind = "ClaudeCode", Exe = exe, ArgsTemplate = [..prefix, "--dangerously-skip-permissions"],
                } },
            };
            if (!protectedSeat && tool == "mcp__fixture__receipt")
            {
                var helper = Path.Combine(root, "mcp-fixture.cjs");
                File.WriteAllText(helper, """
                    const fs = require('fs');
                    const rl = require('readline').createInterface({ input: process.stdin });
                    rl.on('line', line => {
                      let m; try { m = JSON.parse(line); } catch { return; }
                      if (m.id === undefined) return;
                      let result = {};
                      if (m.method === 'initialize') result = { protocolVersion: m.params.protocolVersion, capabilities: { tools: {} }, serverInfo: { name: 'fixture', version: '1' } };
                      if (m.method === 'tools/list') result = { tools: [{ name: 'receipt', description: 'Write an owned disposable test receipt', inputSchema: { type: 'object', properties: {} } }] };
                      if (m.method === 'tools/call') { fs.writeFileSync(process.argv[2], process.argv[3]); result = { content: [{ type: 'text', text: 'receipt written' }] }; }
                      process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: m.id, result }) + '\n');
                    });
                    """);
                var mcpConfig = Path.Combine(root, "mcp-control.json");
                File.WriteAllText(mcpConfig, JsonSerializer.Serialize(new { mcpServers = new { fixture = new
                {
                    command = "node", args = new[] { helper, receiptPath, secretReceipt },
                } } }));
                registry.Definitions["c415-stub"].ArgsTemplate = [..prefix, "--dangerously-skip-permissions", "--strict-mcp-config", "--mcp-config", mcpConfig];
            }
            var delegation = new DelegationSettings
            {
                CheckInterpreterAgentSlug = "c415-" + Guid.NewGuid().ToString("N"),
                ModernPtyBriefInlineMaxBytes = 128, ModernPtySingleWriteMaxBytes = 128,
            };
            await using var runner = new DirectSessionRunnerClient(Path.Combine(root, "runner"), "modern", claudeTranscript: true);
            await using var h = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = schema.ConnectionString, Delegation = delegation, AlwaysOn = protectedSeat,
                ConfigureDeliveryVerification = v =>
                {
                    v.TranscriptConfirmTimeoutSeconds = 30;
                    v.PostFailureConfirmGraceSeconds = 0;
                },
                ConfigureServices = services =>
                {
                    services.AddSingleton<ISessionRunnerClient>(runner);
                    services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(registry));
                    services.AddSingleton<IAgentProtocolAdapterFactory>(new AgentProtocolAdapterFactory(Options.Create(registry), runner));
                    services.AddSingleton(sp => new PtyDeliveryProfile(sp.GetRequiredService<IServiceScopeFactory>(),
                        NullLogger<PtyDeliveryProfile>.Instance, Options.Create(delegation), backendOverride: "modern"));
                },
            });
            RealCliStubBServerHarness.StartEventBridge(runner, h.Runtime);
            await using var scope = h.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            var agent = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
            agent.Slug = protectedSeat ? delegation.CheckInterpreterAgentSlug : "ordinary-tool-control";
            agent.WorkingDirectory = cwd;
            agent.PersistentSessionId = null;
            agent.Kind = AgentKind.ClaudeCode;
            agent.ModelLevel = AgentModelLevel.Low;
            agent.SystemPromptAppend = protectedSeat ? CheckInterpretation.Contract : null;
            agent.LaunchEnvJson = JsonSerializer.Serialize(overlay.Env);
            await db.SaveChangesAsync();
            var taskId = Guid.NewGuid();
            var reply = "Moving — synthetic capability fixture." + "\n" + DelegationReportFormatter.ReportToken(taskId, "done");
            var callId = "toolu_c415_" + Guid.NewGuid().ToString("N");
            var arguments = tool switch
            {
                "Read" => JsonSerializer.Serialize(new { file_path = receiptPath }),
                "Write" => JsonSerializer.Serialize(new { file_path = receiptPath, content = secretReceipt }),
                "Bash" => JsonSerializer.Serialize(new { command = $"printf '%s' '{secretReceipt}' > '{receiptPath.Replace('\\', '/')}'", description = "Write disposable capability receipt" }),
                _ => "{}",
            };
            stub.Script.SetResponder(StubEndpointKeys.ClaudeMessages, body =>
                tool is not null && body.Contains(nonce, StringComparison.Ordinal) && !body.Contains(callId, StringComparison.Ordinal)
                    ? new ScriptedFunctionCall(tool, arguments, callId) : new ScriptedTextTurn(reply));
            await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(agent.Id, new(Fresh: true, RemoteControl: false), CancellationToken.None);
            await h.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(60), CancellationToken.None);
            await db.Entry(agent).ReloadAsync();
            var sessionId = Guid.Parse(agent.PersistentSessionId!);
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
            session.Status.ShouldBe(SessionStatus.Running);
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId)).ShouldBe(0, "a Check launch has no bootstrap/ready prompt");
            for (var iteration = 0; iteration < repetitions; iteration++)
            {
            if (iteration > 0)
            {
                nonce = "C415-NONCE-" + Guid.NewGuid().ToString("N");
                taskId = Guid.NewGuid();
                reply = "Moving - synthetic capability fixture.\n" + DelegationReportFormatter.ReportToken(taskId, "done");
            }
            var task = new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "synthetic capability only", Role = AgentTaskRole.Check,
                ReplyTo = AgentTaskReplyTo.None, AgentKind = AgentKind.ClaudeCode,
                ModelLevel = agent.ModelLevel,
                AgentId = agent.Id, AgentSessionId = sessionId, WorkingDirectory = cwd,
                Goal = nonce + "\nHEAD café 日本語 😀 e\u0301\r\nMIDDLE\r\nTAIL",
                CreatedAt = DateTime.UtcNow, ExecutionDeadlineAt = DateTime.UtcNow.AddSeconds(60),
            };
            string Brief() => DelegationReportFormatter.BuildBrief(task, delegation).ReplaceLineEndings("\n").Trim();
            var provisionalM = gridShape is null ? 8192 : 32768;
            if (gridShape is null) task.Goal += new string('x', provisionalM - spareByte - Encoding.UTF8.GetByteCount(Brief()));
            else if (gridShape.StartsWith("body-"))
                task.Goal += new string('x', int.Parse(gridShape[5..]) - Encoding.UTF8.GetByteCount(task.Goal));
            else if (gridShape.StartsWith("fixture-"))
                task.Goal += new string('x', int.Parse(gridShape[8..]) - task.Goal.Length);
            else if (gridShape == "full-facts") task.Goal = FullFactsGoal(nonce);
            else if (gridShape != "small")
            {
                if (gridShape.StartsWith("ascii-")) task.Goal = nonce + "\nHEAD ASCII\nMIDDLE\nTAIL";
                var target = provisionalM + (gridShape.EndsWith("plus-one") ? 1 : gridShape.EndsWith("minus-one") ? -1 : 0);
                if (gridShape.StartsWith("multi-"))
                    task.Goal += string.Concat(Enumerable.Repeat("日本語😀e\u0301", (target - Encoding.UTF8.GetByteCount(Brief())) / Encoding.UTF8.GetByteCount("日本語😀e\u0301")));
                task.Goal += new string('x', target - Encoding.UTF8.GetByteCount(Brief()));
            }
            var expected = Brief();
            if (gridShape is not null && !gridShape.EndsWith("plus-one"))
                Encoding.UTF8.GetByteCount(expected).ShouldBeLessThanOrEqualTo(provisionalM, "required finite-grid fixtures must fit the measured envelope");
            task.SpecialistInputPolicyJson = new SpecialistInputPolicy(1, task.Id, session.Id, session.StartedAt,
                session.AgentKind, DeliveryBackend.ModernConPty, provisionalM, "isolated-provisional-measurement-only").Serialize();
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            if (Encoding.UTF8.GetByteCount(expected) > provisionalM)
            {
                Should.Throw<SpecialistInputUnsupportedException>(() => AgentTaskDispatcher.FitBriefForTyping(task, delegation,
                    new(DeliveryBackend.ModernConPty, 128, delegation.ReplyInlineMaxChars, 128, "provisional measurement"), agentKind: AgentKind.ClaudeCode));
                (await db.SessionQueuedMessages.CountAsync(m => m.ExecutionTaskId == task.Id)).ShouldBe(0);
                continue;
            }
            var fitted = AgentTaskDispatcher.FitBriefForTyping(task, delegation,
                new(DeliveryBackend.ModernConPty, 128, delegation.ReplyInlineMaxChars, 128, "provisional measurement"), agentKind: AgentKind.ClaudeCode);
            fitted.ShouldBe(expected);
            await h.Queue.EnqueueAsync(sessionId, fitted, MessageSendMode.WhenIdle, CancellationToken.None,
                QueuedMessageOrigin.Delegation, executionDeadlineAt: task.ExecutionDeadlineAt, executionTaskId: task.Id);
            var hit = await stub.Requests.WaitForAsync(r => r.Path == "/v1/messages" && r.Body.Contains(nonce), TimeSpan.FromSeconds(30));
            hit.ShouldNotBeNull("synthetic endpoint must receive the fresh nonce before any result is trusted");
            hit!.Headers["x-api-key"].ShouldBe([key]);
            JsonSerializer.Deserialize<JsonElement>(hit.Body).GetProperty("messages").ToString().ShouldContain(nonce);
            var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.ExecutionTaskId == task.Id);
            if (gridShape is not null && Environment.GetEnvironmentVariable("ANTIPHON_C415_EVIDENCE_DIR") is { Length: > 0 } rawDir)
            {
                Directory.CreateDirectory(rawDir);
                var rawNative = await runner.GetTranscriptAsync(sessionId, CancellationToken.None);
                File.WriteAllText(Path.Combine(rawDir, $"raw-{gridShape}-{iteration}-{task.Id:N}.json"), JsonSerializer.Serialize(new
                {
                    gridShape, iteration, sessionId, taskId = task.Id, expected, queued.DeliveryVerdict, queued.LastDeliveryBaselineSequence,
                    availableTools = JsonSerializer.Deserialize<JsonElement>(hit.Body).GetProperty("tools").EnumerateArray()
                        .Select(t => t.GetProperty("name").GetString()).ToArray(),
                    native = rawNative.Entries.Where(e => e.Kind is TranscriptKinds.UserPrompt or TranscriptKinds.QueuedUserPrompt
                        or TranscriptKinds.AssistantText or TranscriptKinds.TurnEnd).ToArray(),
                }));
            }
            queued.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered, "only the complete native UserPrompt confirms the envelope");
            queued.Body.ShouldBe(expected);
            var prompts = await db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt).ToListAsync();
            prompts.ShouldContain(t => SpecialistInputPolicy.CompletePromptEquals(t.Text, expected));
            if (tool is not null)
            {
                var resultHit = await stub.Requests.WaitForAsync(r => r.Path == "/v1/messages" && r.Body.Contains(callId), TimeSpan.FromSeconds(30));
                resultHit.ShouldNotBeNull("the real CLI must return a native result for the scripted tool request");
                var resultBody = string.Join("\n", JsonSerializer.Deserialize<JsonElement>(resultHit!.Body)
                    .GetProperty("messages").EnumerateArray()
                    .Where(m => m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array)
                    .SelectMany(m => m.GetProperty("content").EnumerateArray())
                    .Where(c => c.TryGetProperty("type", out var type) && type.GetString() == "tool_result"
                        && c.TryGetProperty("tool_use_id", out var id) && id.GetString() == callId)
                    .Select(c => c.GetProperty("content").ToString()));
                if (protectedSeat)
                {
                    if (tool.StartsWith("mcp__", StringComparison.Ordinal))
                    {
                        JsonSerializer.Deserialize<JsonElement>(hit.Body).GetProperty("tools").EnumerateArray()
                            .ShouldNotContain(t => t.GetProperty("name").GetString() == tool);
                        resultBody.ShouldContain("tool");
                        resultBody.ShouldNotBeEmpty("an unexposed forced tool must be rejected by the native CLI");
                    }
                    else resultBody.ShouldContain(CheckInterpretation.DenyHookStderr);
                    resultBody.ShouldNotContain(secretReceipt);
                    if (tool != "Read") File.Exists(receiptPath).ShouldBeFalse();
                }
                else if (tool == "Read") resultBody.ShouldContain(secretReceipt);
                else
                {
                    File.Exists(receiptPath).ShouldBeTrue("the unprotected tool control must perform its harmless action");
                    File.ReadAllText(receiptPath).ShouldBe(secretReceipt);
                }
            }
            if (gridShape is not null)
            {
                var prompt = prompts.Last(t => SpecialistInputPolicy.CompletePromptEquals(t.Text, expected));
                var until = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < until && !await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == sessionId
                    && t.Kind == TranscriptKinds.TurnEnd && t.Sequence > prompt.Sequence))
                {
                    await h.Runtime.CatchUpTranscriptAsync(sessionId, CancellationToken.None);
                    await Task.Delay(100);
                }
                (await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd && t.Sequence > prompt.Sequence)).ShouldBeTrue();
                var native = await runner.GetTranscriptAsync(sessionId, CancellationToken.None);
                native.Entries.ShouldContain(t => t.Kind == TranscriptKinds.UserPrompt && SpecialistInputPolicy.CompletePromptEquals(t.Text, expected));
                var evidenceDir = Environment.GetEnvironmentVariable("ANTIPHON_C415_EVIDENCE_DIR");
                if (!string.IsNullOrWhiteSpace(evidenceDir))
                {
                    Directory.CreateDirectory(evidenceDir);
                    File.WriteAllText(Path.Combine(evidenceDir, $"grid-{gridShape}-{task.Id:N}.json"), JsonSerializer.Serialize(new
                    {
                        shape = gridShape, iteration, taskId = task.Id, sessionId, session.StartedAt,
                        cliSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(exe))),
                        policy = "claude-check-isolated-settings-mcp-v1", backend = "ModernConPty",
                        model = session.EffectiveModelId, provisionalM, bytes = Encoding.UTF8.GetByteCount(expected),
                        inputSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(expected))),
                        prompt.Sequence, endpoint = "owned-synthetic", costUsd = (decimal?)null,
                    }));
                }
            }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfig);
            // Preserve only the owned synthetic conversation files, never a CLI home/config.
            // Native file order is part of the canary evidence, including a failed assertion.
            if (Environment.GetEnvironmentVariable("ANTIPHON_C415_EVIDENCE_DIR") is { Length: > 0 } evidenceRoot
                && Directory.Exists(Path.Combine(config, "projects")))
            {
                var target = Path.Combine(evidenceRoot, "native-" + Path.GetFileName(root));
                Directory.CreateDirectory(target);
                foreach (var file in Directory.EnumerateFiles(Path.Combine(config, "projects"), "*.jsonl", SearchOption.AllDirectories))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }
            RealCliStubBServerHarness.TryDelete(root);
        }
    }

    private static string FullFactsGoal(string nonce)
    {
        var now = DateTime.UtcNow;
        var source = new AgentTask { Id = Guid.NewGuid() };
        var text = new string('語', 190);
        var facts = new DelegateCheckProbe.CheckFacts(now,
            new(source.Id, DelegationReportFormatter.Short(source.Id), nonce + text, AgentTaskKind.Worker,
                AgentKind.ClaudeCode, AgentTaskRole.Code, AgentModelLevel.High, AgentTaskStatus.Working,
                false, 1, 2, now.AddMinutes(-5), null, TimeSpan.FromMinutes(5), 10, 1, false, null),
            new(Guid.NewGuid(), SessionStatus.Running, true, 1000, now, TimeSpan.Zero),
            Enumerable.Range(1, 10).Select(n => new DelegateCheckProbe.CheckTranscriptLine(n, TranscriptKinds.ToolCall,
                $"{n}: {text}", now, new string('x', 120))).ToArray(),
            new("C:\\synthetic\\fixture", DelegateCheckProbe.CheckGitEvidenceScope.TaskBranch, "master..fixture",
                Enumerable.Range(1, 20).Select(n => $"{n:x8} {new string('g', 180)}").ToArray(), 20, 2, null),
            Enumerable.Range(1, 5).Select(n => new DelegateCheckProbe.CheckQueuedMessage(n, QueuedMessageOrigin.Ui,
                now, new string('q', 200), 1, now, false, 3, "synthetic queue item")).ToArray(),
            Enumerable.Range(1, 5).Select(n => new DelegateCheckProbe.CheckIncident(AgentIncidentKind.StartFailure,
                AlertSeverity.Warning, new string('i', 200), now)).ToArray(), now.AddMinutes(-3),
            new("Task", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), false, "synthetic bounded deadline"),
            new("CARD-0415", "synthetic alias"));
        return CheckInterpretation.BuildGoal(source, 1, DelegateCheckProbe.RenderDigest(facts));
    }
}
