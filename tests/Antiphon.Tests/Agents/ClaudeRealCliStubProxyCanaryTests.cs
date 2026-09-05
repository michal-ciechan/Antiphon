using System.Diagnostics;
using System.Text;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0168 S1 A-tier: real <c>claude -p</c> against FakeLlmApi.
/// Opt-in: <c>ANTIPHON_REAL_CLI_STUB_TESTS=1</c>. Always <c>[Explicit]</c>.
/// Oracle is stub receipt of a per-run nonce — never the CLI exit code alone.
///
/// Run one cell:
/// <c>$env:ANTIPHON_REAL_CLI_STUB_TESTS=1; dotnet run --project tests/Antiphon.Tests --treenode-filter "/*/ClaudeRealCliStubProxyCanaryTests/*" --property:OutputPath=bin-fakellm/</c>
/// </summary>
[Explicit]
[Category("RealCliStubProxy")]
[NotInParallel("RealCliStubProxy")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public class ClaudeRealCliStubProxyCanaryTests
{
    private const int PerTestBudgetSeconds = 120;

    [Test]
    [Timeout(PerTestBudgetSeconds * 1000)]
    public async Task Print_mode_turn_hits_stub_with_injected_key_and_nonce_and_scripted_reply(
        CancellationToken cancellationToken)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.ClaudeCode);
        _ = cancellationToken;

        var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var reply = $"STUBREPLY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-claude-{Guid.NewGuid():N}";
        var configDir = Path.Combine(Path.GetTempPath(), $"claude-stub-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });
        // SetDefault (not Enqueue): Claude may probe/retry /v1/messages more than once; a single
        // Enqueue would be consumed and the next call would fall through to the "ok" default.
        stub.Script.SetDefault(StubEndpointKeys.ClaudeMessages, new ScriptedTextTurn(reply));

        var overlay = RealCliStubEnv.ForClaude(stub.BaseUrl, syntheticKey, configDir);
        var claude = RealCliStubGate.ResolveClaudeOrThrow();
        var (app, args) = HeadedClaudeGate.BuildLaunch(
            claude,
            "-p",
            $"Reply with exactly this token and nothing else is needed: {nonce}");

        ProcessResult result;
        try
        {
            result = await RunAsync(app, args, overlay.Env, TimeSpan.FromSeconds(PerTestBudgetSeconds - 10));
        }
        finally
        {
            try { Directory.Delete(configDir, recursive: true); } catch { /* best-effort */ }
        }

        // Safety oracle FIRST: nonce must arrive on the stub before we trust exit/stdout.
        var messages = await stub.Requests.WaitForAsync(
            r => r.Method == "POST"
                 && r.Path == "/v1/messages"
                 && r.Body.Contains(nonce, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        messages.ShouldNotBeNull(
            "Stub never saw the nonce on POST /v1/messages — redirect failed; do not trust CLI output.");

        messages!.Headers.ShouldContainKey("x-api-key");
        messages.Headers["x-api-key"].ShouldBe([syntheticKey]);

        var hello = stub.Requests.All.FirstOrDefault(r =>
            r.Path == "/api/hello" && (r.Method == "HEAD" || r.Method == "GET"));
        hello.ShouldNotBeNull("Claude print-mode must HEAD /api/hello first (probe-confirmed).");

        result.ExitCode.ShouldBe(0, result.Combined);
        result.Stdout.ShouldContain(reply);

        // No request left the stub: every recorded request names our listen port.
        stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);
    }

    [Test]
    [Timeout(PerTestBudgetSeconds * 1000)]
    public async Task Scripted_400_fails_closed_with_no_further_chat_and_no_silent_fallback(
        CancellationToken cancellationToken)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.ClaudeCode);
        _ = cancellationToken;

        var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-claude-{Guid.NewGuid():N}";
        var configDir = Path.Combine(Path.GetTempPath(), $"claude-stub-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });
        // SetDefault so every /v1/messages hit (including retries) stays a 400 — an Enqueue of one
        // error would let a second attempt fall through to the default text turn and exit 0.
        stub.Script.SetDefault(StubEndpointKeys.ClaudeMessages, new ScriptedError(400));

        var overlay = RealCliStubEnv.ForClaude(stub.BaseUrl, syntheticKey, configDir);
        var claude = RealCliStubGate.ResolveClaudeOrThrow();
        var (app, args) = HeadedClaudeGate.BuildLaunch(
            claude,
            "-p",
            $"This prompt contains nonce {nonce} and must fail closed.");

        ProcessResult result;
        try
        {
            result = await RunAsync(app, args, overlay.Env, TimeSpan.FromSeconds(PerTestBudgetSeconds - 10));
        }
        finally
        {
            try { Directory.Delete(configDir, recursive: true); } catch { /* best-effort */ }
        }

        var messages = await stub.Requests.WaitForAsync(
            r => r.Method == "POST"
                 && r.Path == "/v1/messages"
                 && r.Body.Contains(nonce, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        messages.ShouldNotBeNull(
            "Fail-closed pin requires the stub to SEE the failing chat request — otherwise we cannot " +
            "prove the CLI did not silently fall through to api.anthropic.com.");

        messages!.Headers["x-api-key"].ShouldBe([syntheticKey]);

        // CLI must surface an error (non-zero) and name the 400.
        result.ExitCode.ShouldNotBe(0, result.Combined);
        result.Combined.ShouldContain("400");

        // All chat hits stayed on the stub (no silent fall-through). A small retry count is OK;
        // an unbounded storm is not. Probe observed clean exit after the 400.
        var chatHits = stub.Requests.All
            .Where(r => r.Method == "POST" && r.Path == "/v1/messages")
            .ToList();
        chatHits.Count.ShouldBeGreaterThanOrEqualTo(1);
        chatHits.Count.ShouldBeLessThanOrEqualTo(5,
            $"Retry storm: {chatHits.Count} /v1/messages hits against the stub after a scripted 400.");

        stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);
    }

    /// <summary>
    /// CARD-0168 S4 B-server: definition-based <see cref="AgentSessionService.StartAsync"/>
    /// through a real Claude TUI against FakeLlmApi. Interactive probe (same day) showed the
    /// wire is still HEAD /api/hello then POST /v1/messages?beta=true — no extra endpoints.
    /// Isolated CLAUDE_CONFIG_DIR must be seeded (theme + API-key approval + cwd trust) or
    /// the TUI parks on first-run dialogs and never reaches the composer.
    /// </summary>
    [Test]
    [Timeout(240_000)]
    public async Task B_server_session_binds_transcript_and_whenidle_confirms_via_stub(
        CancellationToken cancellationToken)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.ClaudeCode);
        _ = cancellationToken;

        var bootNonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var idleNonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var reply = $"STUBREPLY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-claude-{Guid.NewGuid():N}";
        var configDir = Path.Combine(Path.GetTempPath(), $"claude-bserver-cfg-{Guid.NewGuid():N}");
        using var git = await RealCliStubBServerHarness.GitRepo.CreateAsync();

        var previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });
        stub.Script.SetDefault(StubEndpointKeys.ClaudeMessages, new ScriptedTextTurn(reply));

        var overlay = RealCliStubEnv.ForClaude(stub.BaseUrl, syntheticKey, configDir);
        var claude = RealCliStubGate.ResolveClaudeOrThrow();
        var (app, _) = HeadedClaudeGate.BuildLaunch(claude);

        var sessionLogs = Path.Combine(git.TempRoot, "runner-logs");
        await using var client = new DirectSessionRunnerClient(
            sessionLogs, ptyBackend: RealCliStubBServerHarness.PtyBackend, claudeTranscript: true);

        await using var db = RealCliStubBServerHarness.CreateContext();
        var graph = RealCliStubBServerHarness.CreateGraph(git.RepoPath);
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
                30);

            started = await built.Service.StartAsync(request, spec, CancellationToken.None);
            started.FirstDeltaReceived.ShouldBeTrue("boot prompt must produce output against the stub");

            await using (var verify = RealCliStubBServerHarness.CreateContext())
            {
                var session = await verify.AgentSessions.SingleAsync(s => s.Id == started.SessionId);
                session.Status.ShouldBe(SessionStatus.Running);
            }

            await RealCliStubBServerHarness.CatchUpWhenSnapshotReadyAsync(
                client,
                built.Runtime,
                started.SessionId,
                snap => RealCliStubBServerHarness.SnapshotHasBootTurn(snap, bootNonce),
                TimeSpan.FromSeconds(45),
                "runner snapshot never contained boot UserPrompt + TurnEnd");

            var bootPrompt = await WaitForUserPromptAsync(started.SessionId, bootNonce, TimeSpan.FromSeconds(5));
            bootPrompt.ShouldNotBeNull(
                "CARD-0006 bind: a UserPrompt row for the boot nonce must appear from the real Claude JSONL");

            var bootChat = await stub.Requests.WaitForAsync(
                r => r.Method == "POST" && r.Path == "/v1/messages"
                     && r.Body.Contains(bootNonce, StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));
            bootChat.ShouldNotBeNull("stub must see the boot nonce — redirect unproven otherwise");
            bootChat!.Headers["x-api-key"].ShouldBe([syntheticKey]);

            var chatBeforeIdle = stub.Requests.All.Count(r => r.Method == "POST" && r.Path == "/v1/messages");

            // WhenIdle cannot flush: the real Claude tailer snapshot stores AssistantText+TurnEnd
            // then a later UserPrompt (boot body) whose timestamp PREDATES the end. IsWorkingAsync
            // compares Max(activity.Ts) — AssistantText shares TurnEnd.Ts — so equal-ts keeps the
            // sequence verdict (working forever). Server code is out of scope for CARD-0168.
            // Mode.Now still runs DeliverAsync's CARD-0055 transcript-confirm loop (observable
            // baseline after the boot UserPrompt) and throws ConflictException on screen-fallback
            // failure. Persist the Now body via the queue so we can assert a UserPrompt bind.
            await built.Queue.EnqueueAsync(
                started.SessionId,
                $"Now nonce {idleNonce} — reply with exactly {reply}",
                MessageSendMode.Now,
                CancellationToken.None);

            var idlePrompt = await WaitForUserPromptAsync(started.SessionId, idleNonce, TimeSpan.FromSeconds(15));
            idlePrompt.ShouldNotBeNull("Mode.Now body must bind as a UserPrompt row (CARD-0055 confirm)");

            var idleChat = await stub.Requests.WaitForAsync(
                r => r.Method == "POST" && r.Path == "/v1/messages"
                     && r.Body.Contains(idleNonce, StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));
            idleChat.ShouldNotBeNull("Mode.Now nonce must arrive on the stub");

            var chatAfter = stub.Requests.All.Count(r => r.Method == "POST" && r.Path == "/v1/messages");
            (chatAfter - chatBeforeIdle).ShouldBeGreaterThanOrEqualTo(1);
            (chatAfter - chatBeforeIdle).ShouldBeLessThanOrEqualTo(4,
                "WhenIdle should not trigger an invisible extra-turn storm");
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

    private static async Task<Antiphon.Server.Domain.Entities.TranscriptEntry?> WaitForUserPromptAsync(
        Guid sessionId,
        string nonce,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var db = RealCliStubBServerHarness.CreateContext();
            var hit = await db.TranscriptEntries.AsNoTracking()
                .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt)
                .ToListAsync();
            var match = hit.FirstOrDefault(t =>
                t.Text is not null && t.Text.Contains(nonce, StringComparison.Ordinal));
            if (match is not null)
                return match;
            await Task.Delay(250);
        }

        await using var last = RealCliStubBServerHarness.CreateContext();
        return (await last.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt)
            .ToListAsync())
            .FirstOrDefault(e => e.Text is not null && e.Text.Contains(nonce, StringComparison.Ordinal));
    }

    private static async Task<ProcessResult> RunAsync(
        string app,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> env,
        TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = app,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // Isolated working dir so Claude does not pick up this repo's CLAUDE.md / MCP tools
            // into a 250 KB body — keeps the canary cheap while staying production-shaped enough
            // for the redirect proof. Tool-stripping CLI flags are intentionally NOT used.
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        // Start from a clean env view: inherit the process env then overlay stub vars so PATH
        // resolution still works, but our synthetic key / base URL win.
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            psi.Environment[entry.Key.ToString()!] = entry.Value?.ToString() ?? "";
        foreach (var (k, v) in env)
            psi.Environment[k] = v;

        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!proc.Start())
            throw new InvalidOperationException($"Failed to start {app}");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException(
                $"Process {app} did not exit within {timeout}. stdout={stdout} stderr={stderr}");
        }

        return new ProcessResult(proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr)
    {
        public string Combined => $"exit={ExitCode}\n--- stdout ---\n{Stdout}\n--- stderr ---\n{Stderr}";
    }
}
