using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// Native CLI/tool calibration and negative controls. This is not mapped-dispatch or
// model-compliance acceptance; those gates additionally require the server graph.
[Explicit]
[Category("Integration")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesAutoCompactionCalibrationTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Native_auto_compaction_emits_its_own_ACP_boundary(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable(RealCliStubGate.EnvFlag) != "1")
            throw new TUnit.Core.Exceptions.SkipTestException("Windows and ANTIPHON_REAL_CLI_STUB_TESTS=1 required");
        // Synthetic wire auth deliberately requires no local subscription credential.
        var root = Path.Combine(Path.GetFullPath("TestOutput/Logs/GrokRulesAutoCompactionCalibrationTests"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var home = Path.Combine(root, "home"); Directory.CreateDirectory(home);
        var cwd = Path.Combine(Path.GetTempPath(), "card0395-native-wire", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(cwd);
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"), "[session]\nauto_compact_threshold_percent = 1\n[cli]\nuse_leader = false\n[compat.claude]\nhooks = false\nmcps = false\nrules = false\nskills = false\n[compat.codex]\nhooks = false\nskills = false\n", ct);
        var nonce = "WIRE-" + Guid.NewGuid().ToString("N");
        var body = string.Join("\r\n", Enumerable.Range(1, 500).Select(n => $"RULE-LINE-{n:D4} neutral calibration material. " + new string((char)(65 + n % 26), 60)));
        var sessionId = Guid.NewGuid();
        var payload = new GrokRulesPayload(body, 1, Guid.NewGuid());
        var receipt = await new GrokRulesFileStore(Path.Combine(root, "runner"), new()).WriteAsync(sessionId, payload, ct);
        for (var i=1;i<=6;i++) await File.WriteAllTextAsync(Path.Combine(root, $"workload-{i}.md"), $"Unique workload {i}\n" + body, ct);
        var promptPath = Path.Combine(root, "prompt.md");
        await File.WriteAllTextAsync(promptPath, nonce + " Read the entire standing rules file, including all continuation reads.", ct);
        var synthetic = "card0395-synthetic-" + Guid.NewGuid().ToString("N");
        await using var stub = await FakeLlmApiServer.StartAsync(new() { Grok = true });
        var calls = 0;
        stub.Script.SetResponder(StubEndpointKeys.GrokResponses, request => {
            using var parsed = JsonDocument.Parse(request);
            var node = parsed.RootElement;
            if (!node.TryGetProperty("tools", out var tools) || tools.GetArrayLength() <= 1 || !request.Contains(nonce))
                return new ScriptedTextTurn(nonce + " The isolated calibration session is reading a disposable text file through native read_file tools. No repository changes or external actions were requested. Continue the pending reading task until the requested tool exchanges are complete, then report completion. The only purpose is to observe automatic context compaction in the CLI; all content is synthetic neutral material and the real rules-file location remains in the original system bootstrap. Preserve that pointer and the current task.");
            File.WriteAllText(Path.Combine(root, $"user-request-{++calls:D3}.json"), request);
            if (calls >= 6) return new ScriptedTextTurn("NATIVE-READ-CALIBRATION-COMPLETE");
            return new ScriptedFunctionCall("read_file", JsonSerializer.Serialize(new { target_file = Path.Combine(root, $"workload-{calls}.md"), limit = 1000 }), "card0395-read-" + calls);
        });
        var psi = new ProcessStartInfo(RealCliStubGate.ResolveGrokOrThrow()) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = cwd };
        foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("GROK_", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("XAI_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("X_LLM_", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("ANTIPHON_", StringComparison.OrdinalIgnoreCase)).ToList()) psi.Environment.Remove(key);
        foreach (var pair in RealCliStubEnv.ForGrok(stub.BaseUrl, synthetic).Env) psi.Environment[pair.Key] = pair.Value;
        psi.Environment["GROK_HOME"] = home;
        psi.Environment["GROK_AUTH_PATH"] = Path.Combine(home, "synthetic-auth-unused.json");
        psi.Environment["GROK_AUTH_PROVIDER_COMMAND"] = "echo card0395-synthetic-auth";
        psi.Environment["GROK_AUTH_TOKEN_TTL"] = "3600";
        psi.Environment["GROK_CLI_ENABLE_TELEMETRY"] = "0";
        psi.Environment["GROK_CLI_ENABLE_FEEDBACK"] = "0";
        psi.Environment["GROK_DISABLE_AUTO_UPDATER"] = "1";
        foreach (var arg in new[] { "--prompt-file", promptPath, "--output-format", "streaming-json", "--no-subagents",
            "--disable-web-search", "--always-approve", "--model", "grok-4.6", "--max-turns", "8", "--session-id", sessionId.ToString("D"),
            "--rules", GrokRulesTransport.Bootstrap(receipt.Path) }) psi.ArgumentList.Add(arg);
        var started = DateTimeOffset.UtcNow; var elapsed = Stopwatch.StartNew();
        using var process = Process.Start(psi)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(ct); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        elapsed.Stop();
        await File.WriteAllTextAsync(Path.Combine(root, "native-transcript.jsonl"), await output);
        await File.WriteAllTextAsync(Path.Combine(root, "stderr.log"), await error);
        await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new {
            started, ended = DateTimeOffset.UtcNow, elapsedSeconds = elapsed.Elapsed.TotalSeconds, process.ExitCode,
            sessionId, receipt, nativeUserRequests = calls, requests = stub.Requests.All.Select(r => new { r.Method, r.Path, bodyLength = r.Body.Length }), acceptance = "calibration only" }));
        var native = string.Join("\n", Directory.EnumerateFiles(home, "updates.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText));
        await File.WriteAllTextAsync(Path.Combine(root, "native-acp.jsonl"), native, ct);
        native.ShouldContain("auto_compact_completed", customMessage: "Only a genuine CLI automatic boundary satisfies calibration; no rows are seeded");
        stub.Requests.All.Any(r => r.Method == "GET" && r.Path == "/api-key"
            && r.Headers.TryGetValue("Authorization", out var auth) && auth.Contains("Bearer " + synthetic)).ShouldBeTrue();
        process.ExitCode.ShouldBe(0, root);
        (await output).ShouldContain("NATIVE-READ-CALIBRATION-COMPLETE");
    }
}
