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
    public Task Card0415_V05_Claude_native_tool_denial_and_disposable_receipt_controls(string tool, bool protectedSeat) =>
        RunAsync(protectedSeat, tool, 0);

    private static async Task RunAsync(bool protectedSeat, string? tool, int spareByte)
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
            var task = new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "synthetic capability only", Role = AgentTaskRole.Check,
                ReplyTo = AgentTaskReplyTo.None, AgentKind = AgentKind.ClaudeCode,
                AgentId = agent.Id, AgentSessionId = sessionId, WorkingDirectory = cwd,
                Goal = nonce + "\nHEAD café 日本語 😀 e\u0301\r\nMIDDLE\r\nTAIL",
                CreatedAt = DateTime.UtcNow, ExecutionDeadlineAt = DateTime.UtcNow.AddSeconds(60),
            };
            string Brief() => DelegationReportFormatter.BuildBrief(task, delegation).ReplaceLineEndings("\n").Trim();
            const int provisionalM = 8192;
            task.Goal += new string('x', provisionalM - spareByte - Encoding.UTF8.GetByteCount(Brief()));
            var expected = Brief();
            task.SpecialistInputPolicyJson = new SpecialistInputPolicy(1, task.Id, session.Id, session.StartedAt,
                session.AgentKind, DeliveryBackend.ModernConPty, provisionalM, "isolated-provisional-measurement-only").Serialize();
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
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
                    resultBody.ShouldContain(CheckInterpretation.DenyHookStderr);
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
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfig);
            RealCliStubBServerHarness.TryDelete(root);
        }
    }
}
