using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.FakeLlmApi;
using Antiphon.Nightly;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Api;

public partial class CallerWakeTests
{
    private sealed class WakeFactory(string database, TimeProvider clock) : AntiphonWebAppFactory
    {
        protected override string ConnectionString => database;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton(clock));
        }
    }

    [Test]
    [Explicit]
    [Category("RealCliStubProxy")]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Adopted_native_recipient_reads_its_report(bool sibling)
    {
        if (Environment.GetEnvironmentVariable("ANTIPHON_REAL_CLI_STUB_TESTS") != "1")
            throw new SkipTestException("Requires an installed Claude CLI and the opt-in local model stub.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false);
        var world = rig.World;
        var sessionId = world.CallerSessionId;
        var agentId = Guid.NewGuid();
        var (token, hash) = AgentTaskService.NewToken();
        var source = C1514Support.RepoRoot();
        var checkout = world.RepositoryPath;
        var cwd = sibling ? Path.Combine(world.CallerDirectory, "generated sibling") : checkout;
        if (sibling)
        {
            var linked = await ScratchGitRepo.GitInAsync(checkout, "worktree", "add", "--detach", cwd, "HEAD");
            linked.Ok.ShouldBeTrue(linked.StdErr);
        }
        var hooks = Path.Combine(checkout, "scripts", "hooks");
        Directory.CreateDirectory(hooks);
        foreach (var leaf in new[] { "orchestrator-caller-wake.mjs", "orchestrator-investigation-hook.mjs", "orchestrator-investigation.mjs" })
            File.Copy(Path.Combine(source, "scripts", "hooks", leaf), Path.Combine(hooks, leaf), true);
        Directory.CreateDirectory(Path.Combine(cwd, ".claude"));
        var settingsPath = Path.Combine(cwd, ".claude", "settings.json");
        if (sibling)
            await File.WriteAllTextAsync(settingsPath, OrchestratorWorkspace.ClaudeSettings(
                Path.Combine(hooks, "orchestrator-investigation-hook.mjs")).ToJsonString(), ct);
        else
            File.Copy(Path.Combine(source, ".claude", "settings.json"), settingsPath, true);

        // Use the built production command behind the workspace's launcher. A Bash tool turn
        // therefore exercises the real token-bearing HTTP status read without a nested build.
        var windows = OperatingSystem.IsWindows();
        var command = Path.Combine(checkout, "scripts", windows ? "antiphon.cmd" : "antiphon");
        var shellTool = windows ? "PowerShell" : "Bash";
        await File.WriteAllTextAsync(command, windows ? "@echo off\r\ndotnet exec \"%WAKE_TEST_CLI%\" %*\r\n"
            : "#!/bin/sh\nexec dotnet exec \"$WAKE_TEST_CLI\" \"$@\"\n", ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(command, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var now = world.Clock.GetUtcNow().UtcDateTime;
        await using var db = world.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId, ct);
        session.DelegationTokenHash = hash;
        session.StandingAgentId = agentId;
        session.Cwd = cwd;
        db.AddRange(new Agent { Id = agentId, Name = "Native recipient", Slug = "native-recipient",
            WorkingDirectory = cwd, PersistentSessionId = sessionId.ToString(), CreatedAt = now, UpdatedAt = now },
            new AgentBundleAttachment { AgentId = agentId, BundleKey = InstructionBundles.Orchestrator, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync(ct);

        await using var factory = new WakeFactory(world.Schema.ConnectionString, world.Clock);
        using var api = factory.CreateClient();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var relay = builder.Build();
        relay.Run(async context =>
        {
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),
                context.Request.Path + context.Request.QueryString.ToString());
            if (context.Request.Headers.TryGetValue(AgentTaskEndpoints.TokenHeader, out var credential))
                request.Headers.TryAddWithoutValidation(AgentTaskEndpoints.TokenHeader, credential.ToString());
            using var response = await api.SendAsync(request, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = "application/json";
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
        await relay.StartAsync(ct);

        await using var stub = await FakeLlmApiServer.StartAsync(new() { Claude = true }, ct);
        var config = Path.Combine(world.CallerDirectory, "isolated-config");
        const string key = "local-test-provider";
        RealCliStubClaudeConfig.SeedOnboarding(config, key, trustedCwd: cwd);
        var env = new Dictionary<string, string>(RealCliStubEnv.ForClaude(stub.BaseUrl, key, config).Env)
        {
            ["ANTIPHON_ORCHESTRATOR"] = "1", ["ANTIPHON_TASK_ID"] = "", ["ANTIPHON_TASK_KIND"] = "",
            ["ANTIPHON_AGENT_ID"] = agentId.ToString(), ["ANTIPHON_TASK_TOKEN"] = token,
            ["ANTIPHON_SESSION_ID"] = sessionId.ToString(), ["ANTIPHON_SESSION_GENERATION"] = session.StartedAt.ToString("O"),
            ["ANTIPHON_API"] = relay.Urls.Single(), ["ANTIPHON_HOOK_STATE_DIR"] = world.CallerDirectory,
            ["WAKE_TEST_CLI"] = Path.Combine(AppContext.BaseDirectory, "antiphon-scripts", "Antiphon.Scripts.dll"),
            ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1", ["ANTHROPIC_AUTH_TOKEN"] = "", ["CLAUDE_CODE_OAUTH_TOKEN"] = "",
        };
        var reportId = Guid.Empty;
        var sawNotice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stub.Script.SetResponder(StubEndpointKeys.ClaudeMessages, body =>
        {
            using var doc = JsonDocument.Parse(body);
            var conversation = doc.RootElement.GetProperty("messages").EnumerateArray()
                .SelectMany(m => m.GetProperty("content").ValueKind == JsonValueKind.Array
                    ? m.GetProperty("content").EnumerateArray().ToArray() : [])
                .Where(block => block.TryGetProperty("text", out _))
                .Select(block => block.GetProperty("text").GetString() ?? "").ToArray();
            var notice = conversation.LastOrDefault(t => t.Contains("[Antiphon]") && t.Contains(reportId.ToString()));
            if (notice is not null && sawNotice.TrySetResult())
            {
                // The fixture model follows the command location supplied in the native notice.
                var instruction = notice.Split('\n').Single(line => line.Contains(" delegate status " + reportId));
                return new ScriptedFunctionCall(shellTool, JsonSerializer.Serialize(new
                {
                    command = instruction.Trim(), description = "Read my delegate's report",
                }), "toolu_native_status");
            }
            return new ScriptedTextTurn("The caller is ready.");
        });
        await using var runner = new DirectSessionRunnerClient(Path.Combine(world.CallerDirectory, "native-runner"),
            ptyBackend: "modern", claudeTranscript: true);
        // The Unix pty inherits its host environment. Remove the outer delegate identity
        // and provider credentials before exec so Claude sees exactly this standing caller.
        string[] args = ["Say ready and wait.", "--session-id", sessionId.ToString(),
            "--permission-mode", "bypassPermissions", "--tools", shellTool];
        var exe = "claude";
        if (!OperatingSystem.IsWindows())
        {
            exe = "/usr/bin/env";
            args = ["-u", "ANTIPHON_TASK_ID", "-u", "ANTIPHON_TASK_KIND",
                "-u", "ANTHROPIC_AUTH_TOKEN", "-u", "CLAUDE_CODE_OAUTH_TOKEN", "claude", .. args];
        }
        var spec = new AgentLaunchSpec("wake", AgentKind.ClaudeCode, exe, args,
            env, cwd, 120, 40, AcceptedStartedAt: session.StartedAt);
        await runner.StartAsync(sessionId, spec, ct);
        try
        {
            SessionRunnerTranscriptEvent? end;
            do
            {
                await Task.Delay(200, ct);
                end = (await runner.GetTranscriptAsync(sessionId, ct)).Entries.LastOrDefault(e => e.Kind == TranscriptKinds.TurnEnd);
            } while (end is null);
            db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = sessionId,
                Sequence = end.Sequence, Kind = end.Kind, Timestamp = end.Timestamp?.UtcDateTime,
                StopReason = end.StopReason, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            await runner.SimulateRunnerRestartAsync(ct);
            runner.KillOnDispose = true;
            (await runner.GetSnapshotAsync(sessionId, ct)).Dialog!.Adopted.ShouldBeTrue();
            var adapter = new RunnerClaudeAdapter(runner, Options.Create(new AgentRegistrySettings()));
            await adapter.AttachAsync(sessionId, ct);
            var runtime = world.Services.GetRequiredService<AgentSessionRuntime>();
            runtime.TryRemove(sessionId, out _);
            runtime.Register(sessionId, adapter);
            var reportText = $"Reviewed the owner. Native receipt {Guid.NewGuid():N}.";
            var settled = await rig.SettleReviewAsync(transformReport: (_, report) => report.Replace("Reviewed the owner.", reportText));
            reportId = settled.TaskId;
            await rig.FlushAsync();
            (await rig.RowAsync(reportId)).LastWithholdReason.ShouldBe(DialogDeliveryAdmission.AdoptedSession);
            world.Clock.Advance(TimeSpan.FromMinutes(11));
            await sawNotice.Task.WaitAsync(TimeSpan.FromSeconds(80), ct);
            var transcript = Path.Combine(config, "projects");
            JsonElement? result;
            do
            {
                await Task.Delay(200, ct);
                var path = Directory.EnumerateFiles(transcript, sessionId + ".jsonl", SearchOption.AllDirectories).Single();
                result = ReadStatusResult(await File.ReadAllLinesAsync(path, ct));
            } while (result is null);
            var content = result.Value.GetProperty("content").ToString();
            var error = result.Value.TryGetProperty("is_error", out var flag) && flag.GetBoolean();
            var diagnosis = error ? content.Split('\n').FirstOrDefault() : "report content absent";
            content.Contains(reportText, StringComparison.Ordinal).ShouldBeTrue(
                $"the native {shellTool} result must contain the actual report ({diagnosis})");
            var received = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == reportId, ct);
            received.LastPolledResultAt.ShouldNotBeNull();
            received.ReceivedReportCompletedAt.ShouldBe(received.CompletedAt);
            received.ReceivedReportSessionId.ShouldBe(sessionId);
            (await rig.NotificationAsync(reportId))!.ConfirmingPromptSequence.ShouldBeNull();
            (await rig.RowAsync(reportId)).DeliveryAttempts.ShouldBe(0);
            Console.WriteLine($"Native parent read layout={(sibling ? "worktree" : "main")} conversation={sessionId:D} " +
                $"task={reportId:D} tool=toolu_native_status settlement={received.ReceivedReportCompletedAt:O} terminalAttempts=0");
        }
        finally
        {
            runner.KillOnDispose = true;
        }
    }

    private static JsonElement? ReadStatusResult(IEnumerable<string> lines)
    {
        foreach (var line in lines.Reverse())
        {
            if (!line.Contains("tool_result") || !line.Contains("toolu_native_status")) continue;
            JsonDocument entry;
            try { entry = JsonDocument.Parse(line); }
            catch (JsonException) { continue; } // The tailer may observe a partially written last line.
            using (entry)
            {
                var root = entry.RootElement;
                if (root.GetProperty("type").GetString() != "user"
                    || !root.GetProperty("message").TryGetProperty("content", out var blocks)
                    || blocks.ValueKind != JsonValueKind.Array) continue;
                foreach (var block in blocks.EnumerateArray())
                    if (block.GetProperty("type").GetString() == "tool_result"
                        && block.GetProperty("tool_use_id").GetString() == "toolu_native_status") return block.Clone();
            }
        }
        return null;
    }
}
