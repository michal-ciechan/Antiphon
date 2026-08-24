using System.Diagnostics;
using System.Text;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
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
