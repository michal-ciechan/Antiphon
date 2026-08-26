using System.Diagnostics;
using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0187 S3 B-herdr Codex: the B-runner shape of
/// <see cref="CodexRealCliStubProxyCanaryTests"/> on the herdr lane.
/// <see cref="SessionRunnerRuntime.StartAsync"/> with a hand-built
/// <see cref="RunnerLaunchRequest"/> (Herdr) carrying
/// <see cref="RealCliStubEnv.ForCodex"/> args/env. Zero CARD-0167 dependency — the runner
/// already takes Exe/Args/Env verbatim. Interactive TUI (not <c>codex exec</c>) so herdr's
/// passive detection can report <c>pane.get.agent == "codex"</c> (K5).
/// Oracle is stub receipt of the nonce + Bearer, plus sidecar + pane agent.
/// Skip-not-fail on a measured launch/boot stall (CARD-0195).
/// </summary>
[Explicit]
[Category("RealCliStubProxy")]
[NotInParallel("RealCliStubProxy")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CodexHerdrRealCliStubProxyCanaryTests
{
    [Test]
    [Timeout(420_000)]
    public async Task B_runner_herdr_launch_hits_stub_and_kills_clean(CancellationToken cancellationToken)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.Codex);
        await RealCliStubGate.SkipIfHerdrUnreachableAsync(cancellationToken);

        var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var reply = $"STUBREPLY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-codex-{Guid.NewGuid():N}";
        var tempRoot = Path.Combine(Path.GetTempPath(), $"codex-herdr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var cwd = Path.Combine(tempRoot, "cwd");
        Directory.CreateDirectory(cwd);
        var logs = Path.Combine(tempRoot, "logs");
        Directory.CreateDirectory(logs);
        var codexHome = Path.Combine(tempRoot, "codex-home");
        Directory.CreateDirectory(codexHome);

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Codex = true });
        stub.Script.SetDefault(StubEndpointKeys.CodexResponses, new ScriptedTextTurn(reply));

        var overlay = RealCliStubEnv.ForCodex(stub.BaseUrl, syntheticKey, codexHome);
        var env = new Dictionary<string, string>(overlay.Env, StringComparer.OrdinalIgnoreCase);

        var codex = HeadedCodexGate.ResolveOrThrow();
        var (app, launchArgs) = HeadedCodexGate.BuildLaunch(codex);
        var args = new List<string>(launchArgs)
        {
            "--no-alt-screen",
            "--dangerously-bypass-approvals-and-sandbox",
        };
        args.AddRange(overlay.Args);

        var herdrClient = new HerdrClient(Options.Create(new HerdrSettings { Enabled = true }));
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = logs,
                PtyHostLingerHours = 0.02,
                PtyBackend = "modern",
            }),
            NullLogger<SessionRunnerRuntime>.Instance,
            herdrClient);

        var sessionId = Guid.NewGuid();
        var k9 = Stopwatch.StartNew();
        try
        {
            var startTask = runtime.StartAsync(
                new RunnerLaunchRequest(
                    sessionId,
                    app,
                    args,
                    env,
                    cwd,
                    120,
                    30,
                    TranscriptEnabled: true,
                    TranscriptFormat: TranscriptFormats.Codex,
                    Backend: SessionBackends.Herdr,
                    Herdr: new HerdrLaunchOptions(
                        WorkspaceKey: $"card0187-s3-{sessionId:N}"[..32],
                        WorkspaceLabel: $"card0187-s3-codex-{sessionId:N}"[..40],
                        WorkspaceCwd: cwd,
                        PaneTitle: "card0187-s3-codex",
                        AgentKind: HerdrAgentKinds.Codex)),
                CancellationToken.None);

            RunnerSessionDto started;
            try
            {
                started = await HerdrRealCliCanarySupport.AwaitOrSkipAndReapAsync(
                    startTask,
                    TimeSpan.FromSeconds(HerdrRealCliCanarySupport.CodexLaunchStallSkipSeconds),
                    HerdrRealCliCanarySupport.LaunchStallSkip(
                        "codex", HerdrRealCliCanarySupport.CodexLaunchStallSkipSeconds).Message,
                    result => runtime.KillAsync(sessionId, TimeSpan.FromSeconds(10), CancellationToken.None));
            }
            catch (HerdrLaunchException ex)
            {
                k9.Stop();
                HerdrRealCliCanarySupport.K9Line =
                    $"K9 Codex StartAsync threw {ex.GetType().Name} after {k9.ElapsedMilliseconds}ms: {ex.Message}. "
                    + "Default LaunchDetectTimeoutMs=60000 left unchanged (failure was a stall, not a slow-but-healthy boot).";
                throw HerdrRealCliCanarySupport.BootStallSkip("codex", ex.Message);
            }

            k9.Stop();
            HerdrRealCliCanarySupport.K9Line =
                $"K9 Codex herdr launch-detect (StartAsync → Running): **{k9.ElapsedMilliseconds} ms**. "
                + (k9.ElapsedMilliseconds < 45_000
                    ? "Default `LaunchDetectTimeoutMs` = 60 000 ms is enough; not changed."
                    : "Approaching or past the 60s default — consider raising LaunchDetectTimeoutMs.");
            HerdrRealCliCanarySupport.Log.Add(
                $"K9 codex launch-detect elapsedMs={k9.ElapsedMilliseconds} status={started.Status}");

            started.Status.ShouldBe("Running");

            await HerdrRealCliCanarySupport.AssertSidecarAndPaneAgentAsync(
                logs, sessionId, herdrClient, HerdrAgentKinds.Codex, cancellationToken);

            await HerdrRealCliCanarySupport.AcceptCodexTrustIfVisibleAsync(runtime, sessionId, cancellationToken);
            // CARD-0195: MCP boot can sit on the composer and swallow the first prompt. Wait until
            // the banner is gone before typing; still skip (not fail) if the stub never sees it.
            await WaitUntilCodexComposerLooksIdleAsync(runtime, sessionId, TimeSpan.FromSeconds(30), cancellationToken);

            var prompt = $"Reply with exactly this token and nothing else is needed: {nonce}";
            await HerdrRealCliCanarySupport.SendWrappedBodyAsync(runtime, sessionId, prompt, cancellationToken);

            var chatHit = await stub.Requests.WaitForAsync(
                r => r.Method == "POST"
                     && r.Path == "/v1/responses"
                     && r.Body.Contains(nonce, StringComparison.Ordinal),
                TimeSpan.FromSeconds(60));
            if (chatHit is null)
            {
                throw HerdrRealCliCanarySupport.BootStallSkip(
                    "codex",
                    "stub never saw the nonce on POST /v1/responses — composer may have swallowed the boot prompt (CARD-0195)");
            }

            chatHit.Headers.ShouldContainKey("Authorization");
            chatHit.Headers["Authorization"].ShouldBe([$"Bearer {syntheticKey}"]);

            var models = stub.Requests.All.FirstOrDefault(r =>
                r.Method == "GET" && r.Path == "/v1/models");
            models.ShouldNotBeNull("Codex B-runner should probe GET /v1/models before the turn.");
            models!.Headers["Authorization"].ShouldBe([$"Bearer {syntheticKey}"]);
            stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);

            await WaitForUserPromptAsync(runtime, sessionId, nonce, TimeSpan.FromSeconds(45), cancellationToken);

            await MeasureD1ViaRuntimeAsync(runtime, sessionId, cancellationToken);

            var killed = await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(10), CancellationToken.None);
            killed.Status.ShouldBe("Exited");
        }
        finally
        {
            try { await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None); }
            catch { /* teardown */ }
            HerdrRealCliCanarySupport.WriteProbeResults(
                HerdrRealCliCanarySupport.Log.RenderMarkdown(HerdrRealCliCanarySupport.K9Line));
            RealCliStubBServerHarness.TryDelete(tempRoot);
        }
    }

    /// <summary>
    /// CARD-0168 / CARD-0187: B-agent Codex stays deferred until CARD-0167. Same cell as the
    /// pty canary — do not implement it here.
    /// </summary>
    [Test]
    public async Task B_agent_path_deferred_until_CARD_0167()
    {
        await Task.CompletedTask;
        throw new SkipTestException(
            "B-agent Codex is deferred until CARD-0167 lands first-class -c argument injection "
            + "through AgentControlService. Acceptance criterion for CARD-0167: this test goes green "
            + "against FakeLlmApi via RealCliStubEnv.ForCodex with the same nonce oracle as A-tier. "
            + "CARD-0187 S3 does not build that path.");
    }

    private static async Task WaitUntilCodexComposerLooksIdleAsync(
        SessionRunnerRuntime runtime, Guid sessionId, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snap = runtime.GetSnapshot(sessionId);
            var text = $"{snap.RawOutput}{snap.RenderedScreen}";
            var mcp = text.Contains("Starting MCP servers", StringComparison.OrdinalIgnoreCase);
            var trust = CodexTrustPromptDetector.IsVisible(snap.RawOutput, snap.RenderedScreen);
            if (!mcp && !trust)
                return;
            if (trust)
                await HerdrRealCliCanarySupport.AcceptCodexTrustIfVisibleAsync(runtime, sessionId, ct);
            await Task.Delay(500, ct);
        }
    }

    private static async Task WaitForUserPromptAsync(
        SessionRunnerRuntime runtime,
        Guid sessionId,
        string needle,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snap = runtime.GetTranscript(sessionId);
            if (snap.Entries.Any(e =>
                    e.Kind == TranscriptKinds.UserPrompt
                    && e.Text is not null
                    && e.Text.Contains(needle, StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(250, ct);
        }

        throw new InvalidOperationException(
            $"Codex herdr runner snapshot never contained UserPrompt with '{needle}' within {timeout.TotalSeconds:0}s "
            + "(stub already saw the nonce; this is a tailer/bind miss, not a CARD-0195 stall).");
    }

    private static async Task MeasureD1ViaRuntimeAsync(
        SessionRunnerRuntime runtime,
        Guid sessionId,
        CancellationToken ct)
    {
        foreach (var size in new[] { 43_200, 86_400 })
        {
            var head = $"CARD0187-D1-CODEX-{size}-{Guid.NewGuid():N}";
            const string tail = "CARD0187D1END";
            var body = HerdrRealCliCanarySupport.BuildMultilineEnvelopeBody(size, head, tail);
            Encoding.UTF8.GetByteCount(body).ShouldBe(size);

            var sw = Stopwatch.StartNew();
            try
            {
                await HerdrRealCliCanarySupport.SendWrappedBodyAsync(runtime, sessionId, body, ct);

                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
                string? record = null;
                while (DateTime.UtcNow < deadline)
                {
                    var snap = runtime.GetTranscript(sessionId);
                    record = snap.Entries.LastOrDefault(e =>
                        e.Kind == TranscriptKinds.UserPrompt
                        && e.Text is not null
                        && e.Text.Contains(head, StringComparison.Ordinal))?.Text;
                    if (record is not null)
                        break;
                    await Task.Delay(250, ct);
                }

                sw.Stop();
                var measured = HerdrRealCliCanarySupport.MeasureRecord(body, record);
                HerdrRealCliCanarySupport.Log.AddMeasurement("codex", size, measured, sw.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not SkipTestException and not OperationCanceledException)
            {
                sw.Stop();
                HerdrRealCliCanarySupport.Log.Add(
                    $"D1 codex {size}B: threw {ex.GetType().Name}: {ex.Message} elapsedMs={sw.ElapsedMilliseconds}");
            }
        }
    }
}
