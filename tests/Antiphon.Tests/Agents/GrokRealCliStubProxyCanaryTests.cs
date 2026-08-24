using Antiphon.FakeLlmApi;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0168 S2 A-tier: real <c>grok</c> against FakeLlmApi.
/// Dual-hit oracle: Bearer on GET /api-key == injected key (credential injection) AND nonce on
/// streaming POST /responses (chat redirect via GROK_CLI_CHAT_PROXY_BASE_URL). Chat-path auth is
/// expected to be an OAuth JWT when locally logged in — never force API-key-only by breaking
/// GROK_AUTH_PATH. Opt-in: <c>ANTIPHON_REAL_CLI_STUB_TESTS=1</c>. Always <c>[Explicit]</c>.
///
/// Residual spend risk (design D3): if a future grok binary renames the chat-proxy var, the turn
/// would hit real xAI and this test FAILS LOUDLY on the nonce oracle. That failure is the alarm.
/// </summary>
[Explicit]
[Category("RealCliStubProxy")]
[NotInParallel("RealCliStubProxy")]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokRealCliStubProxyCanaryTests
{
    private const int PerTestBudgetSeconds = 120;

    [Test]
    [Timeout(PerTestBudgetSeconds * 1000)]
    public async Task Print_mode_dual_hit_oracle_key_on_api_key_and_nonce_on_responses(
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        RealCliStubGate.SkipIfNotEligible(AgentKind.Grok);

        var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var reply = $"STUBREPLY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-grok-{Guid.NewGuid():N}";

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Grok = true });
        // /responses = session-title (Responses API); /chat/completions = user turn (re-probed).
        stub.Script.SetDefault(StubEndpointKeys.GrokResponses, new ScriptedTextTurn("title-ok"));
        stub.Script.SetDefault(StubEndpointKeys.GrokChatCompletions, new ScriptedTextTurn(reply));

        var overlay = RealCliStubEnv.ForGrok(stub.BaseUrl, syntheticKey);
        // Ban pin: ForGrok MUST name the chat-proxy var (executable form of the false-safety ban).
        overlay.Env.ShouldContainKey("GROK_CLI_CHAT_PROXY_BASE_URL");

        var grok = RealCliStubGate.ResolveGrokOrThrow();
        var args = new[] { "-p", $"Reply with exactly this token and nothing else is needed: {nonce}" };

        var result = await RealCliStubProcess.RunAsync(
            grok, args, overlay.Env, TimeSpan.FromSeconds(PerTestBudgetSeconds - 10));

        // Dual-hit oracle — BOTH required; neither alone proves safety.
        var apiKeyHit = await stub.Requests.WaitForAsync(
            r => r.Method == "GET"
                 && r.Path == "/api-key"
                 && r.Headers.TryGetValue("Authorization", out var auth)
                 && auth.Any(a => a.Equals($"Bearer {syntheticKey}", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5));
        apiKeyHit.ShouldNotBeNull(
            "Stub never saw GET /api-key with Bearer == injected GROK_CODE_XAI_API_KEY — " +
            "credential injection unproven.");

        // Re-probed: the user turn lands on /chat/completions (not /responses, which is title-only).
        var chatHit = await stub.Requests.WaitForAsync(
            r => r.Method == "POST"
                 && r.Path == "/chat/completions"
                 && r.Body.Contains(nonce, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        chatHit.ShouldNotBeNull(
            "Stub never saw nonce on POST /chat/completions — chat redirect via " +
            "GROK_CLI_CHAT_PROXY_BASE_URL FAILED. Do NOT trust exit code; this is the shape where " +
            "GROK_XAI_API_BASE_URL alone looked safe while completions hit real xAI.");

        // Chat-path auth: OAuth JWT expected when locally logged in (present, not equal to synthetic key).
        chatHit!.Headers.ShouldContainKey("Authorization");
        var chatAuth = chatHit.Headers["Authorization"].ShouldHaveSingleItem();
        chatAuth.ShouldStartWith("Bearer ");
        chatAuth.ShouldNotBe($"Bearer {syntheticKey}",
            "Chat-path auth unexpectedly equaled the injected API key; with intact local login the " +
            "probe observed an OAuth JWT. Investigate before treating this as a pass.");

        result.ExitCode.ShouldBe(0, result.Combined);
        result.Stdout.ShouldContain(reply);
        stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);
    }

    [Test]
    [Timeout(PerTestBudgetSeconds * 1000)]
    public async Task Scripted_400_on_responses_fails_closed_with_no_silent_fallback(
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        RealCliStubGate.SkipIfNotEligible(AgentKind.Grok);

        var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-grok-{Guid.NewGuid():N}";

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Grok = true });
        // Title path OK; fail the actual user-turn path so the pin covers the spend-adjacent surface.
        stub.Script.SetDefault(StubEndpointKeys.GrokResponses, new ScriptedTextTurn("title-ok"));
        stub.Script.SetDefault(StubEndpointKeys.GrokChatCompletions, new ScriptedError(400));

        var overlay = RealCliStubEnv.ForGrok(stub.BaseUrl, syntheticKey);
        var grok = RealCliStubGate.ResolveGrokOrThrow();
        var args = new[] { "-p", $"This prompt contains nonce {nonce} and must fail closed." };

        var result = await RealCliStubProcess.RunAsync(
            grok, args, overlay.Env, TimeSpan.FromSeconds(PerTestBudgetSeconds - 10));

        var chatHit = await stub.Requests.WaitForAsync(
            r => r.Method == "POST"
                 && r.Path == "/chat/completions"
                 && r.Body.Contains(nonce, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        chatHit.ShouldNotBeNull(
            "Fail-closed pin requires the stub to SEE the failing /chat/completions request — " +
            "otherwise we cannot prove the CLI did not silently fall through to real xAI.");

        result.ExitCode.ShouldNotBe(0, result.Combined);

        var chatHits = stub.Requests.All
            .Where(r => r.Method == "POST" && r.Path == "/chat/completions")
            .ToList();
        chatHits.Count.ShouldBeGreaterThanOrEqualTo(1);
        chatHits.Count.ShouldBeLessThanOrEqualTo(5,
            $"Retry storm: {chatHits.Count} /chat/completions hits after a scripted 400.");

        stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);
    }
}
