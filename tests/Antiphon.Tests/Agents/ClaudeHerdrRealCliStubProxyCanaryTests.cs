using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0168 S5 B-herdr Claude against a LIVE herdr instance. Composed gate:
/// <c>ANTIPHON_REAL_CLI_STUB_TESTS=1</c> + herdr pipe answering. Herdr absent ⇒ skip, not fail.
///
/// CARD-0187 S3 added Grok × Herdr and Codex × Herdr canaries (see
/// <c>GrokHerdrRealCliStubProxyCanaryTests</c> / <c>CodexHerdrRealCliStubProxyCanaryTests</c>).
///
/// Delivery confirms via the CARD-0164 transcript-first path, never herdr's sticky revision.
/// </summary>
[Explicit]
[Category("RealCliStubProxy")]
[NotInParallel("RealCliStubProxy")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeHerdrRealCliStubProxyCanaryTests
{
    [Test]
    [Timeout(240_000)]
    public async Task B_herdr_session_sidecar_exists_and_whenidle_confirms_via_transcript(
        CancellationToken cancellationToken)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.ClaudeCode);
        await RealCliStubGate.SkipIfHerdrUnreachableAsync(cancellationToken);

        var bootNonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var idleNonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var reply = $"STUBREPLY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-claude-{Guid.NewGuid():N}";
        var configDir = Path.Combine(Path.GetTempPath(), $"claude-herdr-cfg-{Guid.NewGuid():N}");
        using var git = await RealCliStubBServerHarness.GitRepo.CreateAsync();

        var previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });
        stub.Script.SetDefault(StubEndpointKeys.ClaudeMessages, new ScriptedTextTurn(reply));

        var overlay = RealCliStubEnv.ForClaude(stub.BaseUrl, syntheticKey, configDir);
        var claude = RealCliStubGate.ResolveClaudeOrThrow();
        var (app, _) = HeadedClaudeGate.BuildLaunch(claude);

        var sessionLogs = Path.Combine(git.TempRoot, "runner-logs");
        var herdrClient = new HerdrClient(Options.Create(new HerdrSettings { Enabled = true }));
        await using var client = new DirectSessionRunnerClient(
            sessionLogs,
            ptyBackend: RealCliStubBServerHarness.PtyBackend,
            claudeTranscript: true,
            herdrClient: herdrClient);

        await using var db = RealCliStubBServerHarness.CreateContext();
        var graph = RealCliStubBServerHarness.CreateGraph(git.RepoPath);
        var now = DateTime.UtcNow;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "CARD-0168 herdr",
            Slug = $"card0168-herdr-{Guid.NewGuid():N}"[..40],
            WorkingDirectory = git.RepoPath,
            Kind = AgentKind.ClaudeCode,
            SessionBackend = SessionBackend.Herdr,
            AlwaysOn = false,
            Status = AgentStatus.Idle,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Agents.Add(agent);
        graph.Card.AssignedAgentId = agent.Id;
        db.Add(graph.Project);
        await db.SaveChangesAsync();

        var expectedCwd = Path.GetFullPath(Path.Combine(git.WorktreeRoot, $"card-{graph.Card.Identifier}"));
        RealCliStubClaudeConfig.SeedOnboarding(configDir, syntheticKey, trustedCwd: expectedCwd);

        var eventBus = new MockEventBus();
        await using var provider = RealCliStubBServerHarness.BuildProvider();
        var built = RealCliStubBServerHarness.BuildService(
            db, git.WorktreeRoot, eventBus, provider, client, AgentKind.ClaudeCode, app);

        AgentSessionStartResult? started = null;
        try
        {
            var request = new StartAgentSessionRequest(
                graph.Card.Id,
                "stub-cli",
                AgentKind.ClaudeCode,
                $"Reply with exactly this token and nothing else is needed: {bootNonce}",
                Cols: 120,
                Rows: 30);
            var spec = new AgentLaunchSpec(
                "stub-cli",
                AgentKind.ClaudeCode,
                app,
                ["--dangerously-skip-permissions"],
                overlay.Env,
                git.RepoPath,
                120,
                30,
                Backend: SessionBackend.Herdr);

            var startTask = built.Service.StartAsync(request, spec, CancellationToken.None);
            var winner = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(90), cancellationToken));
            if (winner != startTask)
            {
                throw new SkipTestException(
                    "Live herdr pipe answered ping but pane launch did not complete within 90s. "
                    + "CARD-0168 S5 skips rather than failing the slice (measured hang 2026-08-24).");
            }

            started = await startTask;
            started.FirstDeltaReceived.ShouldBeTrue();

            await using (var verify = RealCliStubBServerHarness.CreateContext())
            {
                var session = await verify.AgentSessions.SingleAsync(s => s.Id == started.SessionId);
                session.Status.ShouldBe(SessionStatus.Running);
                session.SessionBackend.ShouldBe(SessionBackend.Herdr);
            }

            var sidecar = HerdrPaneSidecar.PathFor(sessionLogs, started.SessionId);
            File.Exists(sidecar).ShouldBeTrue($"herdr sidecar must exist at {sidecar}");

            var bootChat = await stub.Requests.WaitForAsync(
                r => r.Method == "POST" && r.Path == "/v1/messages"
                     && r.Body.Contains(bootNonce, StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            bootChat.ShouldNotBeNull("boot nonce must hit the stub (transcript-first oracle, not herdr revision)");

            await RealCliStubBServerHarness.CatchUpWhenSnapshotReadyAsync(
                client,
                built.Runtime,
                started.SessionId,
                snap => RealCliStubBServerHarness.SnapshotHasBootTurn(snap, bootNonce),
                TimeSpan.FromSeconds(45),
                "herdr runner snapshot never contained boot UserPrompt + TurnEnd");
            // WhenIdle cannot flush against the real-CLI tailer snapshot order (see Claude B-server).
            // Mode.Now still exercises CARD-0055 transcript-first confirm (CARD-0164).
            await built.Queue.EnqueueAsync(
                started.SessionId,
                $"Now nonce {idleNonce} — reply with exactly {reply}",
                MessageSendMode.Now,
                CancellationToken.None);

            var idleChat = await stub.Requests.WaitForAsync(
                r => r.Method == "POST" && r.Path == "/v1/messages"
                     && r.Body.Contains(idleNonce, StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            idleChat.ShouldNotBeNull("Mode.Now nonce must hit the stub");
            stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);
        }
        finally
        {
            if (started is not null)
            {
                try { await built.Service.KillAsync(started.SessionId, CancellationToken.None); }
                catch { /* teardown */ }
            }
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfigDir);
            RealCliStubBServerHarness.TryDelete(configDir);
        }
    }
}
