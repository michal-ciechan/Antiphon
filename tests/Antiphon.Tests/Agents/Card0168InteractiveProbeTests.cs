using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0168 S4: interactive-mode capture probe. Not the committed B-server oracle — this exists
/// to observe which endpoints a real interactive TUI hits against FakeLlmApi before any B-tier
/// assertion leans on them. Opt-in: <c>ANTIPHON_REAL_CLI_STUB_TESTS=1</c>.
///
/// Writes a dump next to the test output; the slice commit copies the findings into the plan.
/// </summary>
[Explicit]
[Category("RealCliStubProxy")]
[NotInParallel("RealCliStubProxy")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public class Card0168InteractiveProbeTests
{
    private const int BudgetSeconds = 120;

    [Test]
    [Timeout(BudgetSeconds * 1000)]
    public async Task Claude_interactive_capture_probe(CancellationToken cancellationToken)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.ClaudeCode);

        var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var reply = $"STUBREPLY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-claude-{Guid.NewGuid():N}";
        var configDir = Path.Combine(Path.GetTempPath(), $"claude-stub-cfg-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"claude-stub-cwd-{Guid.NewGuid():N}");
        var sessionLogs = Path.Combine(Path.GetTempPath(), $"claude-stub-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);
        RealCliStubClaudeConfig.SeedOnboarding(configDir, syntheticKey, trustedCwd: cwd);

        var previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true });
        stub.Script.SetDefault(StubEndpointKeys.ClaudeMessages, new ScriptedTextTurn(reply));

        var overlay = RealCliStubEnv.ForClaude(stub.BaseUrl, syntheticKey, configDir);
        var claude = RealCliStubGate.ResolveClaudeOrThrow();
        var (app, extra) = HeadedClaudeGate.BuildLaunch(claude, "--dangerously-skip-permissions");

        await using var client = new DirectSessionRunnerClient(
            sessionLogs, ptyBackend: "modern", claudeTranscript: true);
        var adapter = new RunnerClaudeAdapter(client, Options.Create(new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = app } },
            ClaudeReadyQuietPeriodMs = 5_000,
            ClaudeReadyMaxWaitMs = 60_000,
            ClaudeReadyMinTotalWaitMs = 9_000,
            ClaudeDoneMaxWaitMs = 90_000,
        }));

        var sessionId = Guid.NewGuid();
        var spec = new AgentLaunchSpec(
            "claude", AgentKind.ClaudeCode, app, extra, overlay.Env, cwd, 120, 30, SessionId: sessionId);

        try
        {
            await adapter.StartAsync(spec, cancellationToken);
            var ready = await adapter.WaitForReadyAsync(cancellationToken);
            Dump("claude-interactive", stub, nonce, syntheticKey,
                extra: "ready=" + ready + Environment.NewLine + "screen=" + adapter.SnapshotRenderedScreen());
            if (!ready)
            {
                // Capture-probe: the dump IS the result. Ready failing is a finding, not a
                // reason to hide the wire shape.
                return;
            }

            await adapter.SendPromptAsync(
                $"Reply with exactly this token and nothing else is needed: {nonce}",
                cancellationToken);
            await adapter.WaitForTurnCompleteAsync(cancellationToken);

            Dump("claude-interactive-after-prompt", stub, nonce, syntheticKey);
            var chat = await stub.Requests.WaitForAsync(
                r => r.Method == "POST" && r.Path == "/v1/messages" && r.Body.Contains(nonce, StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));
            chat.ShouldNotBeNull("probe oracle: nonce must arrive on the stub");
            stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);
        }
        finally
        {
            try { await adapter.KillAsync(TimeSpan.FromSeconds(5), CancellationToken.None); } catch { /* probe */ }
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfigDir);
            TryDelete(configDir);
            TryDelete(cwd);
            TryDelete(sessionLogs);
        }
    }

    [Test]
    [Timeout(BudgetSeconds * 1000)]
    public async Task Grok_interactive_capture_probe(CancellationToken cancellationToken)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.Grok);

        var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var reply = $"STUBREPLY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-grok-{Guid.NewGuid():N}";
        var cwd = Path.Combine(Path.GetTempPath(), $"grok-stub-cwd-{Guid.NewGuid():N}");
        var sessionLogs = Path.Combine(Path.GetTempPath(), $"grok-stub-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cwd);

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Grok = true });
        stub.Script.SetDefault(StubEndpointKeys.GrokResponses, new ScriptedTextTurn("title-ok"));
        stub.Script.SetDefault(StubEndpointKeys.GrokChatCompletions, new ScriptedTextTurn(reply));

        var overlay = RealCliStubEnv.ForGrok(stub.BaseUrl, syntheticKey);
        var grok = RealCliStubGate.ResolveGrokOrThrow();

        await using var client = new DirectSessionRunnerClient(sessionLogs, ptyBackend: "modern");
        var adapter = new RunnerGrokAdapter(client, Options.Create(new AgentRegistrySettings
        {
            DefaultDefinition = "grok",
            Definitions = { ["grok"] = new AgentDefinition { Kind = "Grok", Exe = grok } },
            GrokReadyQuietPeriodMs = 3_000,
            GrokReadyMaxWaitMs = 60_000,
            GrokDoneMaxWaitMs = 90_000,
        }));

        var sessionId = Guid.NewGuid();
        var spec = new AgentLaunchSpec(
            "grok", AgentKind.Grok, grok, [], overlay.Env, cwd, 120, 30, SessionId: sessionId);

        try
        {
            await adapter.StartAsync(spec, cancellationToken);
            var ready = await adapter.WaitForReadyAsync(cancellationToken);
            ready.ShouldBeTrue("interactive Grok must become ready against the stub");

            await adapter.SendPromptAsync(
                $"Reply with exactly this token and nothing else is needed: {nonce}",
                cancellationToken);
            await adapter.WaitForTurnCompleteAsync(cancellationToken);

            Dump("grok-interactive", stub, nonce, syntheticKey);
            stub.Requests.All.ShouldAllBe(r => r.ListenPort == stub.ListenPort);
        }
        finally
        {
            try { await adapter.KillAsync(TimeSpan.FromSeconds(5), CancellationToken.None); } catch { /* probe */ }
            TryDelete(cwd);
            TryDelete(sessionLogs);
        }
    }

    private static void Dump(
        string label, FakeLlmApiServer stub, string nonce, string syntheticKey, string? extra = null)
    {
        var lines = new List<string>
        {
            $"# CARD-0168 S4 interactive probe — {label}",
            $"utc={DateTime.UtcNow:O}",
            $"nonce={nonce}",
            $"key={syntheticKey}",
            $"listen={stub.ListenPort}",
            $"count={stub.Requests.All.Count}",
            "",
        };
        foreach (var r in stub.Requests.All)
        {
            var auth = r.Headers.TryGetValue("Authorization", out var a)
                ? a.FirstOrDefault()
                : r.Headers.TryGetValue("x-api-key", out var k) ? "x-api-key:" + k.FirstOrDefault() : "-";
            var nonceHit = r.Body.Contains(nonce, StringComparison.Ordinal) ? " NONCE" : "";
            lines.Add($"{r.Seq,3} {r.Method,-6} {r.Path}{r.QueryString} status-port={r.ListenPort} auth={Truncate(auth, 40)} bytes={r.BodyByteLength}{nonceHit}");
        }

        var unmatched = stub.Requests.All
            .GroupBy(r => r.Method + " " + r.Path)
            .Select(g => $"{g.Key} x{g.Count()}")
            .ToList();
        lines.Add("");
        lines.Add("paths:");
        lines.AddRange(unmatched.Select(p => "- " + p));
        if (!string.IsNullOrEmpty(extra))
        {
            lines.Add("");
            lines.Add("extra:");
            lines.Add(extra);
        }

        var dest = Path.Combine(
            Path.GetTempPath(),
            $"card-0168-probe-{label}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.md");
        File.WriteAllLines(dest, lines);
        Console.WriteLine($"PROBE DUMP {dest}");
        Console.WriteLine(string.Join(Environment.NewLine, lines));
    }

    private static string Truncate(string? s, int n)
        => string.IsNullOrEmpty(s) ? "-" : (s.Length <= n ? s : s[..n] + "…");

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best-effort */ }
    }
}
