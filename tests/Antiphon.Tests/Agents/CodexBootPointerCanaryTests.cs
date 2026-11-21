using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Core;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CodexBootPointerCanaryTests
{
    [Test]
    [Timeout(150_000)]
    public async Task Real_Codex_consumes_and_submits_the_boot_pointer(CancellationToken ct)
    {
        var executable = ResolveCodex()
            ?? throw new SkipTestException("Codex CLI unavailable (ANTIPHON_CODEX_EXE or PATH); boot pointer canary skipped");
        var model = Environment.GetEnvironmentVariable("ANTIPHON_CODEX_CANARY_MODEL") ?? ModelAlias.Gpt62Sol;
        var sessionId = Guid.NewGuid();
        var root = Directory.CreateTempSubdirectory("antiphon-codex-boot-").FullName;
        var evidence = Path.GetFullPath(Path.Combine(".antiphon", "evidence", "card-2046", sessionId.ToString("N")));
        var queueLog = new RecordingLogger<SessionMessageQueueService>();
        var readyLog = new RecordingLogger<RunnerCodexAdapter>();
        var observations = new List<string>();
        var snapshots = new Dictionary<string, SessionRunnerSnapshotDto>();
        string[] writes = [];

        try
        {
            var cwd = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
            var task = new AgentTask
            {
                Id = Guid.NewGuid(), Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                Title = "Codex boot canary",
            };
            var briefPath = $".antiphon/inbox/{task.Id:D}.md";
            Directory.CreateDirectory(Path.Combine(cwd, ".antiphon", "inbox"));
            var brief = "Reply with the local stub response. No files need changing.";
            await File.WriteAllTextAsync(Path.Combine(cwd, briefPath), brief, ct);
            var settings = new DelegationSettings();
            var pointer = DelegationReportFormatter.BuildBriefPointer(task, settings, briefPath, brief.Length, AgentKind.Codex);

            await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Codex = true }, ct);
            stub.Script.SetDefault(StubEndpointKeys.CodexResponses, new ScriptedTextTurn("local stub response"));
            var overlay = RealCliStubEnv.ForCodex(stub.BaseUrl, "stub-codex-canary", Path.Combine(root, "codex-home"));
            var env = new Dictionary<string, string>(overlay.Env, StringComparer.OrdinalIgnoreCase)
            {
                ["TERM"] = "xterm-256color",
                ["NO_PROXY"] = "127.0.0.1,localhost",
            };
            string[] args = [
                "--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox", "--model", model,
                CodexLaunchArgs.ConfigFlag, CodexLaunchArgs.ReasoningEffortOverride(task.ModelLevel),
                CodexLaunchArgs.ConfigFlag, CodexLaunchArgs.DisablePasteBurst,
                CodexLaunchArgs.ConfigFlag, CodexLaunchArgs.DisableUpdateCheck,
                CodexLaunchArgs.ConfigFlag, $"chatgpt_base_url=\"{stub.BaseUrl}\"",
                CodexLaunchArgs.ConfigFlag, "analytics.enabled=false",
                ..overlay.Args,
            ];
            var (app, launchArgs) = OperatingSystem.IsWindows()
                ? HeadedCodexGate.BuildLaunch(executable, args)
                : (executable, args);

            await using var dbFixture = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var client = new DirectSessionRunnerClient(
                Path.Combine(root, "runner"), ptyBackend: "modern", codexTranscript: true);
            await using var adapter = new RunnerCodexAdapter(client, Options.Create(new AgentRegistrySettings()), readyLog);
            await using var provider = BuildProvider(dbFixture.ConnectionString, client, queueLog);
            var generation = SessionGeneration.Normalize(DateTime.UtcNow);
            var spec = new AgentLaunchSpec("codex", AgentKind.Codex, app, launchArgs, env, cwd, 120, 30,
                SessionId: sessionId, AcceptedStartedAt: generation);

            await adapter.StartAsync(spec, ct);
            var started = await client.GetAsync(sessionId, ct);
            var backend = client.ActualBackend;
            observations.Add($"model={model} backend={backend} generation={generation:o}");
            started.Status.ShouldBe("Running");
            started.Backend.ShouldBe(SessionBackends.PtyHost);
            started.AcceptedStartedAt.ShouldBe(generation);
            backend.ShouldBe(OperatingSystem.IsWindows() ? PtyBackend.ModernConPty : PtyBackend.UnixPty);
            try
            {
                var ready = await adapter.WaitForReadyAsync(ct);
                var idle = await client.GetSnapshotAsync(sessionId, ct);
                snapshots["idle"] = idle;
                ready.ShouldBeTrue("the production Codex ready gate must open");
                CodexStartupScreen.Classify(idle.RenderedScreen, idle.RawOutput).Reason.ShouldBe(CodexStartupReason.Ready);

                await using (var scope = provider.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.AgentSessions.Add(new AgentSession
                    {
                        Id = sessionId, DefinitionName = "codex", AgentKind = AgentKind.Codex,
                        Status = SessionStatus.Running, Cwd = cwd, Cols = 120, Rows = 30,
                        CreatedAt = generation, StartedAt = generation, LastSeenAt = generation,
                    });
                    await db.SaveChangesAsync(ct);
                }

                var expectedWrite = PtyInputEncoding.WrapIfMultiline(PtyInputEncoding.NormalizeBody(pointer));
                client.SnapshotObserved = snapshot =>
                {
                    if (!snapshot.RenderedScreen.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal))
                        return;
                    if (CodexPointerComposerEvidence.IsComplete(snapshot.RenderedScreen, pointer))
                        snapshots["consumed"] = snapshot;
                    else if (snapshot.RenderedScreen.Contains(briefPath, StringComparison.Ordinal)
                        || !snapshots.ContainsKey("typed"))
                        snapshots["typed"] = snapshot;
                };
                var queue = provider.GetRequiredService<SessionMessageQueueService>();
                var delivery = await queue.EnqueueAsync(sessionId, pointer, MessageSendMode.WhenIdle, ct,
                    origin: QueuedMessageOrigin.Delegation);
                var actualWrites = client.BackendWrites.Select(write => write.Input).ToArray();

                var milestones = PointerMilestones(queueLog);
                milestones.ShouldContain("write-return", "the production pointer path must write the formatted brief pointer");
                milestones.ShouldContain("whole-composer", "the production pointer path must recognize whole consumption before Enter");
                milestones.Where(m => m is "write-start" or "whole-composer" or "enter")
                    .ShouldBe(["write-start", "whole-composer", "enter"]);
                actualWrites.SkipWhile(input => input != expectedWrite).ShouldBe([expectedWrite, "\r"]);
                delivery.LastDelivery.ShouldNotBeNull().ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Transcript);
                var transcript = await client.GetTranscriptAsync(sessionId, ct);
                transcript.Entries.Where(entry => entry.Kind == TranscriptKinds.UserPrompt)
                    .ShouldHaveSingleItem().Text.ShouldBe(pointer);

                using var receiptBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                receiptBudget.CancelAfter(TimeSpan.FromSeconds(10));
                while (!stub.Requests.All.Any(request => MatchesTurn(request, pointer, model)))
                {
                    receiptBudget.IsCancellationRequested.ShouldBeFalse(
                        $"the loopback stub must receive POST /v1/responses with the entire pointer and model={model}");
                    await Task.Delay(50, ct);
                }
                observations.Add($"responses model={model} whole-pointer=true");
            }
            finally
            {
                writes = client.BackendWrites.Select(write => write.Input == "\r" ? "enter" : "text").ToArray();
                client.SnapshotObserved = null;
                snapshots["last"] = await client.GetSnapshotAsync(sessionId, CancellationToken.None);
            }
        }
        finally
        {
            Directory.CreateDirectory(evidence);
            foreach (var (name, snapshot) in snapshots)
            {
                await File.WriteAllTextAsync(Path.Combine(evidence, name + ".screen.txt"), snapshot.RenderedScreen);
                await File.WriteAllTextAsync(Path.Combine(evidence, name + ".ansi.txt"), snapshot.RawOutput);
            }
            await File.WriteAllLinesAsync(Path.Combine(evidence, "milestones.txt"),
                observations.Concat(readyLog.Entries.Select(entry => entry.Message))
                    .Concat(PointerMilestones(queueLog)).Append($"backend-writes={string.Join(",", writes)}"));
            Console.WriteLine($"CARD-2046 model={model} evidence={evidence}");
            RealCliStubBServerHarness.TryDelete(root);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString, DirectSessionRunnerClient client, ILogger<SessionMessageQueueService> queueLog)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton<ISessionRunnerClient>(client);
        services.AddSingleton(queueLog);
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static string[] PointerMilestones(RecordingLogger<SessionMessageQueueService> logger) =>
        logger.Entries.Where(entry => entry.State.ContainsKey("Milestone"))
            .Select(entry => (string)entry.State["Milestone"]!).ToArray();

    private static bool MatchesTurn(RecordedRequest request, string pointer, string model)
    {
        if (request.Method != "POST" || request.Path != "/v1/responses")
            return false;

        using var json = JsonDocument.Parse(request.Body);
        var body = json.RootElement;
        return body.GetProperty("model").GetString() == model
            && body.GetProperty("input").EnumerateArray().Any(item =>
                item.TryGetProperty("role", out var role) && role.GetString() == "user"
                && item.GetProperty("content").EnumerateArray().Any(part =>
                    part.TryGetProperty("text", out var text) && PromptSubmissionMatch.IsCompleteIn(pointer, text.GetString())));
    }

    private static string? ResolveCodex()
    {
        var configured = Environment.GetEnvironmentVariable(HeadedCodexGate.ExeOverrideEnv);
        var executable = !string.IsNullOrWhiteSpace(configured)
            ? File.Exists(configured) ? configured : null
            : OperatingSystem.IsWindows() ? HeadedCodexGate.ResolveCodex() : FindUnixCodex();
        return executable is null ? null : Path.GetFullPath(executable);
    }

    private static string? FindUnixCodex() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, "codex")).FirstOrDefault(File.Exists);
}
