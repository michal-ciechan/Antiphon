using System.Diagnostics;
using System.Text;
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
/// CARD-0187 S3 B-herdr Grok. Oracle matches the B-server Grok pty canary (stub nonce +
/// UserPrompt confirm) on <see cref="SessionBackends.Herdr"/>. Launch is the B-runner path
/// (<see cref="SessionRunnerRuntime.StartAsync"/>) rather than
/// <c>AgentSessionService.StartAsync</c>: the latter waits for the boot turn to complete, and
/// on a live herdr Grok TUI that wait is the CARD-0195-class stall this slice must skip, not hang
/// inside. Herdr evidence: sidecar written and <c>pane.get.agent == "grok"</c>.
/// Composed gate: <c>ANTIPHON_REAL_CLI_STUB_TESTS=1</c> + herdr reachable.
/// </summary>
[Explicit]
[Category("RealCliStubProxy")]
[NotInParallel("RealCliStubProxy")]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokHerdrRealCliStubProxyCanaryTests
{
    [Test]
    [Timeout(420_000)]
    public async Task B_herdr_session_sidecar_exists_and_whenidle_confirms_via_stub(
        CancellationToken cancellationToken)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.Grok);
        await RealCliStubGate.SkipIfHerdrUnreachableAsync(cancellationToken);

        var bootNonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var idleNonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var reply = $"STUBREPLY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-grok-{Guid.NewGuid():N}";
        var tempRoot = Path.Combine(Path.GetTempPath(), $"grok-herdr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var cwd = Path.Combine(tempRoot, "cwd");
        Directory.CreateDirectory(cwd);
        var logs = Path.Combine(tempRoot, "logs");
        Directory.CreateDirectory(logs);

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Grok = true });
        stub.Script.SetDefault(StubEndpointKeys.GrokResponses, new ScriptedTextTurn("title-ok"));
        stub.Script.SetDefault(StubEndpointKeys.GrokChatCompletions, new ScriptedTextTurn(reply));

        var overlay = RealCliStubEnv.ForGrok(stub.BaseUrl, syntheticKey);
        overlay.Env.ShouldContainKey("GROK_CLI_CHAT_PROXY_BASE_URL");
        var grok = RealCliStubGate.ResolveGrokOrThrow();
        var sessionId = Guid.NewGuid();

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

        var k9 = Stopwatch.StartNew();
        try
        {
            var startTask = runtime.StartAsync(
                new RunnerLaunchRequest(
                    sessionId,
                    grok,
                    ["--no-alt-screen", "--always-approve", "--session-id", sessionId.ToString("D")],
                    overlay.Env,
                    cwd,
                    120,
                    30,
                    TranscriptEnabled: true,
                    TranscriptFormat: TranscriptFormats.Grok,
                    Backend: SessionBackends.Herdr,
                    Herdr: new HerdrLaunchOptions(
                        WorkspaceKey: $"card0187-s3-{sessionId:N}"[..32],
                        WorkspaceLabel: $"card0187-s3-grok-{sessionId:N}"[..40],
                        WorkspaceCwd: cwd,
                        PaneTitle: "card0187-s3-grok",
                        AgentKind: HerdrAgentKinds.Grok)),
                CancellationToken.None);

            RunnerSessionDto started;
            try
            {
                started = await HerdrRealCliCanarySupport.AwaitOrSkipAndReapAsync(
                    startTask,
                    TimeSpan.FromSeconds(HerdrRealCliCanarySupport.LaunchStallSkipSeconds),
                    HerdrRealCliCanarySupport.LaunchStallSkip("grok", HerdrRealCliCanarySupport.LaunchStallSkipSeconds).Message,
                    _ => runtime.KillAsync(sessionId, TimeSpan.FromSeconds(10), CancellationToken.None));
            }
            catch (HerdrLaunchException ex)
            {
                k9.Stop();
                HerdrRealCliCanarySupport.Log.Add(
                    $"Grok StartAsync threw after {k9.ElapsedMilliseconds}ms: {ex.Message}");
                throw HerdrRealCliCanarySupport.BootStallSkip("grok", ex.Message);
            }

            k9.Stop();
            HerdrRealCliCanarySupport.Log.Add(
                $"Grok herdr launch-detect elapsedMs={k9.ElapsedMilliseconds} status={started.Status}");
            started.Status.ShouldBe("Running");

            await HerdrRealCliCanarySupport.AssertSidecarAndPaneAgentAsync(
                logs, sessionId, herdrClient, HerdrAgentKinds.Grok, cancellationToken);

            var bootPrompt = $"Reply with exactly this token and nothing else is needed: {bootNonce}";
            await HerdrRealCliCanarySupport.SendWrappedBodyAsync(runtime, sessionId, bootPrompt, cancellationToken);

            var bootChat = await stub.Requests.WaitForAsync(
                r => r.Method == "POST" && r.Path == "/chat/completions"
                     && r.Body.Contains(bootNonce, StringComparison.Ordinal),
                TimeSpan.FromSeconds(45));
            if (bootChat is null)
            {
                throw HerdrRealCliCanarySupport.BootStallSkip(
                    "grok", "stub never saw boot nonce on POST /chat/completions");
            }

            await WaitForUserPromptAsync(runtime, sessionId, bootNonce, TimeSpan.FromSeconds(45), cancellationToken);

            var idlePrompt = $"Now nonce {idleNonce} — reply with exactly {reply}";
            await HerdrRealCliCanarySupport.SendWrappedBodyAsync(runtime, sessionId, idlePrompt, cancellationToken);

            var idleChat = await stub.Requests.WaitForAsync(
                r => r.Method == "POST" && r.Path == "/chat/completions"
                     && r.Body.Contains(idleNonce, StringComparison.Ordinal),
                TimeSpan.FromSeconds(30));
            idleChat.ShouldNotBeNull("second nonce must arrive on /chat/completions");
            stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);

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
            $"Grok herdr runner snapshot never contained UserPrompt with '{needle}' within {timeout.TotalSeconds:0}s "
            + "(stub already saw the nonce; this is a tailer miss, not a launch stall).");
    }

    private static async Task MeasureD1ViaRuntimeAsync(
        SessionRunnerRuntime runtime,
        Guid sessionId,
        CancellationToken ct)
    {
        foreach (var size in new[] { 43_200, 86_400 })
        {
            var head = $"CARD0187-D1-GROK-{size}-{Guid.NewGuid():N}";
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
                HerdrRealCliCanarySupport.Log.AddMeasurement("grok", size, measured, sw.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not SkipTestException and not OperationCanceledException)
            {
                sw.Stop();
                HerdrRealCliCanarySupport.Log.Add(
                    $"D1 grok {size}B: threw {ex.GetType().Name}: {ex.Message} elapsedMs={sw.ElapsedMilliseconds}");
            }
        }
    }
}
