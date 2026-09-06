using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesHttpAcceptanceTests
{
    [Test, Timeout(120_000)]
    public async Task Real_http_capability_and_session_receipts_exclude_rules_body_with_herdr_disabled(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Owned Windows PtyHost test");
        var root = TestSessionLogRoot.Create("card0395-http");
        var artifacts = Path.Combine(AppContext.BaseDirectory, "TestOutput", "Logs", nameof(GrokRulesHttpAcceptanceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifacts);
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Antiphon.SessionRunner.exe"))
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "--urls", "http://127.0.0.1:0", "--SessionRunner:SessionLogPath", root,
            "--SessionRunner:Herdr:Enabled", "false", "--SessionRunner:PtyBackend", "modern", "--SessionRunner:PtyHostLingerHours", "0.002",
            "--Serilog:LogPath", Path.Combine(root, "logs") }) info.ArgumentList.Add(arg);
        var listening = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new ConcurrentQueue<string>();
        using var process = new Process { StartInfo = info };
        void Capture(string? line)
        {
            if (line is null) return;
            output.Enqueue(line);
            var match = Regex.Match(line, @"Now listening on:\s+(http://127\.0\.0\.1:\d+)");
            if (match.Success) listening.TrySetResult(match.Groups[1].Value);
        }
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);
        process.Start().ShouldBeTrue(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        HttpClient? http = null; var id = Guid.NewGuid(); int? hostPid = null;
        try
        {
            var address = await listening.Task.WaitAsync(TimeSpan.FromSeconds(25), ct);
            new Uri(address).Port.ShouldNotBe(17204);
            http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(30) };
            var capabilities = await http.GetFromJsonAsync<RunnerCapabilitiesDto>("/capabilities", ct);
            capabilities.ShouldNotBeNull().Features.ShouldContain(GrokRulesTransport.Capability);
            capabilities.SessionBackends.ShouldNotContain(SessionBackends.Herdr);
            var sentinel = "http-body-only-" + Guid.NewGuid().ToString("N");
            var body = sentinel + "\r\nfull rules café 😀\n" + new string('x', 6000);
            var request = new RunnerLaunchRequest(id, Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/q", "/k", "prompt $G"], new Dictionary<string,string> { ["GROK_HOME"] = Path.Combine(root, "home") },
                root, 120, 30, TranscriptFormat: TranscriptFormats.Grok, GrokRulesPayload: new(body, 1, Guid.NewGuid()));
            using var started = await http.PostAsJsonAsync("/sessions", request, ct);
            started.StatusCode.ShouldBe(HttpStatusCode.Created);
            var launchJson = await started.Content.ReadAsStringAsync(ct);
            launchJson.ShouldNotContain(sentinel);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var launched = JsonSerializer.Deserialize<RunnerSessionDto>(launchJson, options)!;
            hostPid = launched.HostPid;
            var receipt = launched.GrokRulesReceipt.ShouldNotBeNull();
            (await File.ReadAllBytesAsync(receipt.Path, ct)).ShouldBe(Encoding.UTF8.GetBytes(body));
            var sessionJson = await http.GetStringAsync($"/sessions/{id:D}", ct);
            var listJson = await http.GetStringAsync("/sessions", ct);
            sessionJson.ShouldNotContain(sentinel); listJson.ShouldNotContain(sentinel);
            JsonSerializer.Deserialize<RunnerSessionDto>(sessionJson, options)!.GrokRulesReceipt.ShouldBe(receipt);
            using var invalid = await http.PostAsJsonAsync("/sessions", request with
            {
                SessionId = Guid.NewGuid(), GrokRulesPayload = new(sentinel + "\0", 1, Guid.NewGuid())
            }, ct);
            invalid.IsSuccessStatusCode.ShouldBeFalse();
            var problem = await invalid.Content.ReadAsStringAsync(ct);
            problem.ShouldContain("grok_rules_content_invalid"); problem.ShouldNotContain(sentinel);
            string.Join("\n", output).ShouldNotContain(sentinel);
            await File.WriteAllTextAsync(Path.Combine(artifacts, "http-evidence.json"), JsonSerializer.Serialize(new
            {
                address, capabilities, launchJson, sessionJson, listJson, problem,
                exactBytes = Encoding.UTF8.GetByteCount(body), sha256 = receipt.Sha256
            }), ct);
        }
        finally
        {
            if (http is not null)
            {
                try { using var killed = await http.PostAsync($"/sessions/{id:D}/kill", null, CancellationToken.None); }
                catch (Exception ex) { output.Enqueue("Owned-session HTTP cleanup: " + ex.GetType().Name); }
                finally { http.Dispose(); }
            }
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            if (hostPid is int pid)
            {
                try
                {
                    using var host = Process.GetProcessById(pid);
                    try { await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (System.TimeoutException)
                    {
                        if (host.MainModule?.FileName?.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == true)
                        { host.Kill(entireProcessTree: true); await host.WaitForExitAsync(); }
                    }
                }
                catch (ArgumentException) { /* Owned host already exited. */ }
            }
            await File.WriteAllLinesAsync(Path.Combine(artifacts, "runner-http.log"), output);
        }
    }
}
