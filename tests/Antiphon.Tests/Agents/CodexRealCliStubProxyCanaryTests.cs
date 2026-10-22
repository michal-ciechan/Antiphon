using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using SessionRunnerSettings = Antiphon.SessionRunner.SessionRunnerSettings;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0168 S3 A-tier: real <c>codex exec</c> against FakeLlmApi via the five <c>-c</c>
/// provider overrides in <see cref="RealCliStubEnv.ForCodex"/>. Opt-in:
/// <c>ANTIPHON_REAL_CLI_STUB_TESTS=1</c>. Always <c>[Explicit]</c>.
/// Oracle is stub receipt of a per-run nonce — never the CLI exit code alone.
/// </summary>
[Explicit]
[Category("RealCliStubProxy")]
[NotInParallel("RealCliStubProxy")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public class CodexRealCliStubProxyCanaryTests
{
    private const int PerTestBudgetSeconds = 120;

    [Test]
    [Timeout(PerTestBudgetSeconds * 1000)]
    public async Task Exec_turn_hits_stub_with_injected_bearer_and_nonce_and_scripted_reply(
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        RealCliStubGate.SkipIfNotEligible(AgentKind.Codex);

        var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var reply = $"STUBREPLY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-codex-{Guid.NewGuid():N}";
        var codexHome = Directory.CreateTempSubdirectory("antiphon-codex-stub-home").FullName;

        try
        {
            await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Codex = true });
            stub.Script.SetDefault(StubEndpointKeys.CodexResponses, new ScriptedTextTurn(reply));

            var overlay = RealCliStubEnv.ForCodex(stub.BaseUrl, syntheticKey, codexHome);
            overlay.Args.Count.ShouldBe(10); // five (-c, value) pairs
            overlay.Env["OPENAI_API_KEY"].ShouldBe(syntheticKey);
            overlay.Env["CODEX_HOME"].ShouldBe(Path.GetFullPath(codexHome));

            var codex = HeadedCodexGate.ResolveOrThrow();
            var (app, launchArgs) = HeadedCodexGate.BuildLaunch(codex);
            var args = new List<string>(launchArgs)
            {
                "exec",
                "--skip-git-repo-check",
            };
            args.AddRange(overlay.Args);
            args.Add($"Reply with exactly this token and nothing else is needed: {nonce}");

            var result = await RealCliStubProcess.RunAsync(
                app, args, overlay.Env, TimeSpan.FromSeconds(PerTestBudgetSeconds - 10));

        var chatHit = await stub.Requests.WaitForAsync(
            r => r.Method == "POST"
                 && r.Path == "/v1/responses"
                 && r.Body.Contains(nonce, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        chatHit.ShouldNotBeNull(
            "Stub never saw the nonce on POST /v1/responses — Codex -c redirect failed; " +
            "do not trust CLI output.");

        chatHit!.Headers.ShouldContainKey("Authorization");
        chatHit.Headers["Authorization"].ShouldBe([$"Bearer {syntheticKey}"]);

        var models = stub.Requests.All.FirstOrDefault(r =>
            r.Method == "GET" && r.Path == "/v1/models");
        models.ShouldNotBeNull("Codex should probe GET /v1/models before the turn.");
        models!.Headers["Authorization"].ShouldBe([$"Bearer {syntheticKey}"]);

        result.ExitCode.ShouldBe(0, result.Combined);
        result.Combined.ShouldContain(reply);
            stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);
            result.Combined.Contains(
                    "Refusing to create helper binaries under temporary dir",
                    StringComparison.Ordinal)
                .ShouldBeTrue(
                    "Codex currently warns, rather than fails, when CODEX_HOME is a per-test temporary directory.");
        }
        finally
        {
            RealCliStubBServerHarness.TryDelete(codexHome);
        }
    }

    [Test]
    [Timeout(PerTestBudgetSeconds * 1000)]
    public async Task Scripted_400_on_models_fails_closed_and_reconnect_loop_is_bounded(
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        RealCliStubGate.SkipIfNotEligible(AgentKind.Codex);

        var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-codex-{Guid.NewGuid():N}";
        var codexHome = Directory.CreateTempSubdirectory("antiphon-codex-stub-home").FullName;

        try
        {
            await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Codex = true });
            // Probe-observed: models 400 → reconnect-looping POST /v1/responses. Keep responses as
            // error too so a loop cannot "succeed" into a real-looking turn.
            stub.Script.SetDefault(StubEndpointKeys.CodexModels, new ScriptedError(400));
            stub.Script.SetDefault(StubEndpointKeys.CodexResponses, new ScriptedError(400));

        var overlay = RealCliStubEnv.ForCodex(stub.BaseUrl, syntheticKey, codexHome);
        var codex = HeadedCodexGate.ResolveOrThrow();
        var (app, launchArgs) = HeadedCodexGate.BuildLaunch(codex);
        var args = new List<string>(launchArgs) { "exec", "--skip-git-repo-check" };
        args.AddRange(overlay.Args);
        args.Add($"This prompt contains nonce {nonce} and must fail closed.");

        var result = await RealCliStubProcess.RunAsync(
            app, args, overlay.Env, TimeSpan.FromSeconds(PerTestBudgetSeconds - 10));

        // Models probe must have hit the stub (redirect proven) before we talk about fail-closed.
        var modelsHit = await stub.Requests.WaitForAsync(
            r => r.Method == "GET" && r.Path == "/v1/models",
            TimeSpan.FromSeconds(5));
        modelsHit.ShouldNotBeNull(
            "Fail-closed pin requires the stub to SEE GET /v1/models — otherwise redirect is unproven.");
        modelsHit!.Headers["Authorization"].ShouldBe([$"Bearer {syntheticKey}"]);

        result.ExitCode.ShouldNotBe(0, result.Combined);

        // Bounded reconnect loop: investigation saw looping /v1/responses after models 400.
        // Assert it stops (process exited) and the attempt count stays within budget.
        var responseHits = stub.Requests.All
            .Where(r => r.Method == "POST" && r.Path == "/v1/responses")
            .ToList();
        responseHits.Count.ShouldBeLessThanOrEqualTo(20,
            $"Reconnect storm: {responseHits.Count} /v1/responses hits after models 400 — " +
            "loop did not bound itself within the test budget.");

        stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);
        }
        finally
        {
            RealCliStubBServerHarness.TryDelete(codexHome);
        }
    }

    /// <summary>
    /// CARD-0168 S6 B-runner: <see cref="SessionRunnerRuntime.StartAsync"/> with a hand-built
    /// <see cref="RunnerLaunchRequest"/> (PtyHost + modern ConPTY) carrying
    /// <see cref="RealCliStubEnv.ForCodex"/> args/env. Zero CARD-0167 dependency — the runner
    /// already takes Exe/Args/Env verbatim. Oracle is stub receipt of the nonce + Bearer, then
    /// a clean kill.
    /// </summary>
    [Test]
    [Timeout(PerTestBudgetSeconds * 1000)]
    public async Task B_runner_launch_hits_stub_and_kills_clean(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        RealCliStubGate.SkipIfNotEligible(AgentKind.Codex);

        var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var reply = $"STUBREPLY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-codex-{Guid.NewGuid():N}";
        var tempRoot = Path.Combine(Path.GetTempPath(), $"codex-brunner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var cwd = Path.Combine(tempRoot, "cwd");
        Directory.CreateDirectory(cwd);
        var codexHome = Path.Combine(tempRoot, "codex-home");
        Directory.CreateDirectory(codexHome);

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Codex = true });
        stub.Script.SetDefault(StubEndpointKeys.CodexResponses, new ScriptedTextTurn(reply));

        var overlay = RealCliStubEnv.ForCodex(stub.BaseUrl, syntheticKey, codexHome);
        var codex = HeadedCodexGate.ResolveOrThrow();
        var (app, launchArgs) = HeadedCodexGate.BuildLaunch(codex);
        var args = new List<string>(launchArgs)
        {
            "exec",
            "--skip-git-repo-check",
        };
        args.AddRange(overlay.Args);
        args.Add($"Reply with exactly this token and nothing else is needed: {nonce}");

        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = Path.Combine(tempRoot, "logs"),
                PtyHostLingerHours = 0.02,
                PtyBackend = "modern",
            }),
            NullLogger<SessionRunnerRuntime>.Instance);

        var sessionId = Guid.NewGuid();
        try
        {
            var started = await runtime.StartAsync(
                new RunnerLaunchRequest(
                    sessionId,
                    app,
                    args,
                    overlay.Env,
                    cwd,
                    120,
                    30,
                    Backend: SessionBackends.PtyHost),
                CancellationToken.None);
            started.Status.ShouldBe("Running");

            var chatHit = await stub.Requests.WaitForAsync(
                r => r.Method == "POST"
                     && r.Path == "/v1/responses"
                     && r.Body.Contains(nonce, StringComparison.Ordinal),
                TimeSpan.FromSeconds(PerTestBudgetSeconds - 20));
            chatHit.ShouldNotBeNull(
                "Stub never saw the nonce on POST /v1/responses through SessionRunnerRuntime — " +
                "ForCodex -c redirect failed; do not trust launch status.");
            chatHit!.Headers.ShouldContainKey("Authorization");
            chatHit.Headers["Authorization"].ShouldBe([$"Bearer {syntheticKey}"]);

            var models = stub.Requests.All.FirstOrDefault(r =>
                r.Method == "GET" && r.Path == "/v1/models");
            models.ShouldNotBeNull("Codex B-runner should probe GET /v1/models before the turn.");
            models!.Headers["Authorization"].ShouldBe([$"Bearer {syntheticKey}"]);

            stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);

            var killed = await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(10), CancellationToken.None);
            killed.Status.ShouldBe("Exited");
        }
        finally
        {
            try { await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None); }
            catch { /* teardown */ }
            RealCliStubBServerHarness.TryDelete(tempRoot);
        }
    }

    /// <summary>
    /// CARD-0167 S6: project endpoint -> AgentControlService -> real runner adapter -> stub.
    /// Operator-only: isolated synthetic home/key; no operator auth is read or copied.
    /// </summary>
    [Test]
    [Timeout(PerTestBudgetSeconds * 1000)]
    public async Task B_agent_launch_projects_project_endpoint_and_hits_stub(CancellationToken cancellationToken)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.Codex);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var root = Directory.CreateTempSubdirectory("c0167-codex-agent-").FullName;
        try
        {
            var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
            var syntheticKey = $"stub-codex-{Guid.NewGuid():N}";
            await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Codex = true });
            stub.Script.SetDefault(StubEndpointKeys.CodexResponses, new ScriptedTextTurn("STUBREPLY"));
            var overlay = RealCliStubEnv.ForCodex(stub.BaseUrl, syntheticKey, Path.Combine(root, "synthetic-home"));
            var (app, prefix) = HeadedCodexGate.BuildLaunch(HeadedCodexGate.ResolveOrThrow());
            var settings = new AgentRegistrySettings
            {
                DefaultDefinition = "codex-canary",
                Definitions = { ["codex-canary"] = new AgentDefinition
                {
                    Kind = "Codex", Exe = app,
                    ArgsTemplate = [.. prefix, "--dangerously-bypass-approvals-and-sandbox"],
                } },
            };
            await using var runner = new DirectSessionRunnerClient(
                Path.Combine(root, "runner-logs"), ptyBackend: "modern", codexTranscript: true);
            // Refuse before real process creation unless the production endpoint projection is complete.
            runner.BeforeStart = () =>
            {
                var request = runner.StartRequests.Last();
                ProviderEndpointCanaryHarness.AssertProjection(
                    AgentKind.Codex, stub.BaseUrl, syntheticKey, request.Env, request.Args);
                request.Env["CODEX_HOME"].ShouldBe(overlay.Env["CODEX_HOME"]);
                var expected = overlay.Args.Where(arg => arg != "-c")
                    .Select(arg => arg.Replace("model_providers.stub.", "model_providers.antiphon.", StringComparison.Ordinal)
                        .Replace("model_provider=stub", "model_provider=antiphon", StringComparison.Ordinal)
                        .Replace("name=\"Stub\"", "name=\"Antiphon endpoint\"", StringComparison.Ordinal));
                foreach (var arg in expected)
                    request.Args.ShouldContain(arg);
            };
            await using var harness = ProviderEndpointCanaryHarness.Build(root, schema.ConnectionString,
                AgentKind.Codex, [], services =>
                {
                    services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                        new ProviderEndpointCanaryHarness.RegistryOptions(settings));
                    services.AddSingleton<ISessionRunnerClient>(runner);
                    services.AddSingleton<IAgentProtocolAdapterFactory>(
                        new AgentProtocolAdapterFactory(Options.Create(settings), runner));
                });
            var runtime = harness.Provider.GetRequiredService<AgentSessionRuntime>();
            RealCliStubBServerHarness.StartEventBridge(runner, runtime);
            var (agentId, _) = await ProviderEndpointCanaryHarness.SeedAsync(
                schema.ConnectionString, root, AgentKind.Codex, stub.BaseUrl, syntheticKey,
                new Dictionary<string, string> { ["CODEX_HOME"] = overlay.Env["CODEX_HOME"] }, cancellationToken);
            var started = await harness.Control.StartAsync(agentId,
                new StartAgentRequest(Fresh: true, Prompt: $"Reply with exactly {nonce}"), cancellationToken);
            started.PersistentSessionId.ShouldNotBeNull();
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(90), cancellationToken);
            var chat = await stub.Requests.WaitForAsync(r => r.Method == "POST" && r.Path == "/v1/responses"
                && r.Body.Contains(nonce, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
            chat.ShouldNotBeNull("the real agent turn must reach the stub with its nonce");
            (chat!.Headers.TryGetValue("Authorization", out var bearer)
                && bearer.SequenceEqual(new[] { $"Bearer {syntheticKey}" })).ShouldBeTrue("synthetic bearer on chat");
            stub.Requests.All.Any(r => r.Method == "GET" && r.Path == "/v1/models"
                && r.Headers.TryGetValue("Authorization", out var key)
                && key.SequenceEqual(new[] { $"Bearer {syntheticKey}" })).ShouldBeTrue("synthetic bearer on models");
            stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);
            await runtime.CatchUpTranscriptAsync(Guid.Parse(started.PersistentSessionId!), cancellationToken);
            await harness.Control.StopAsync(agentId, cancellationToken);
        }
        finally
        {
            RealCliStubBServerHarness.TryDelete(root);
        }
    }
}
