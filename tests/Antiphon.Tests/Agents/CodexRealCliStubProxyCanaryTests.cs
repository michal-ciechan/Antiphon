using Antiphon.FakeLlmApi;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

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

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Codex = true });
        stub.Script.SetDefault(StubEndpointKeys.CodexResponses, new ScriptedTextTurn(reply));

        var overlay = RealCliStubEnv.ForCodex(stub.BaseUrl, syntheticKey);
        overlay.Args.Count.ShouldBe(10); // five (-c, value) pairs
        overlay.Env["OPENAI_API_KEY"].ShouldBe(syntheticKey);

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

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Codex = true });
        // Probe-observed: models 400 → reconnect-looping POST /v1/responses. Keep responses as
        // error too so a loop cannot "succeed" into a real-looking turn.
        stub.Script.SetDefault(StubEndpointKeys.CodexModels, new ScriptedError(400));
        stub.Script.SetDefault(StubEndpointKeys.CodexResponses, new ScriptedError(400));

        var overlay = RealCliStubEnv.ForCodex(stub.BaseUrl, syntheticKey);
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
}
