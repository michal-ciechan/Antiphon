using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0090 S1: <c>scripts/complexity-chain.ps1</c> driven as the REAL script under pwsh
/// against a stub API. A string match on the source would pass just as happily if the flag
/// never reached the request body.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ComplexityChainScriptTests
{
    [Test]
    public async Task Set_puts_the_tier_provenance_and_candidates()
    {
        using var server = new StubApi();
        var run = await RunAsync(
            server, "set", "-Complexity", "Hard",
            "-Candidates", "ClaudeCode/Frontier,Codex/Frontier,Grok/Frontier",
            "-Provenance", "Human", "-Reason", "plan-grade work");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("PUT");
        server.LastPath.ShouldBe("/api/complexity-chains/Hard");
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("provenance").GetString().ShouldBe("Human");
        body.RootElement.GetProperty("reason").GetString().ShouldBe("plan-grade work");
        var candidates = body.RootElement.GetProperty("candidates").EnumerateArray().ToList();
        candidates.Count.ShouldBe(3);
        candidates[0].GetProperty("agentKind").GetString().ShouldBe("ClaudeCode");
        candidates[0].GetProperty("modelLevel").GetString().ShouldBe("Frontier");
        candidates[2].GetProperty("agentKind").GetString().ShouldBe("Grok");
    }

    [Test]
    public async Task A_naive_NotAfter_is_refused_before_any_request()
    {
        using var server = new StubApi();
        var run = await RunAsync(
            server, "set", "-Complexity", "Hard",
            "-Candidates", "Grok/Frontier",
            "-NotAfter", "2026-09-05T00:00:00");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("Naive");
        server.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task Get_hits_the_list_endpoint()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "get");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("GET");
        server.LastPath.ShouldBe("/api/complexity-chains");
        run.Output.ShouldContain("Hard");
        run.Output.ShouldContain("fable");
    }

    [Test]
    public async Task Clear_deletes_by_complexity()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "clear", "-Complexity", "Hard");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("DELETE");
        server.LastPath.ShouldBe("/api/complexity-chains/Hard");
        run.Output.ShouldContain("cleared Hard");
    }

    [Test]
    public async Task Set_without_candidates_is_refused_locally()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "set", "-Complexity", "Hard");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("Candidates");
        server.RequestCount.ShouldBe(0);
    }

    private static Task<(int ExitCode, string Output)> RunAsync(StubApi server, params string[] args)
    {
        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "complexity-chain.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);
        startInfo.Environment["ANTIPHON_API"] = server.BaseUrl.TrimEnd('/');
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;
        return RunProcessAsync(startInfo);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;

        public StubApi()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            BaseUrl = $"http://localhost:{port}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public string? LastMethod { get; private set; }
        public string? LastPath { get; private set; }
        public JsonDocument? LastBody { get; private set; }
        public int RequestCount { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                RequestCount++;
                LastMethod = context.Request.HttpMethod;
                LastPath = context.Request.Url?.PathAndQuery;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(raw))
                        LastBody = JsonDocument.Parse(raw);
                }

                if (context.Request.HttpMethod == "DELETE")
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    continue;
                }

                var payload = Encoding.UTF8.GetBytes(
                    context.Request.HttpMethod == "GET" ? ListJson : ChainJson);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
        }

        private const string ChainJson =
            """
            {"complexity":"Hard","candidates":[{"agentKind":"ClaudeCode","modelLevel":"Frontier","alias":"fable","availableNow":true,"unavailableReason":null}],"provenance":"Human","source":"pin","reason":"test","notAfter":null,"updatedAt":"2026-09-02T00:00:00Z"}
            """;

        private const string ListJson =
            """
            {"chains":[{"complexity":"Hard","candidates":[{"agentKind":"ClaudeCode","modelLevel":"Frontier","alias":"fable","availableNow":true,"unavailableReason":null}],"provenance":"Human","source":"pin","reason":"test","notAfter":null,"updatedAt":"2026-09-02T00:00:00Z"},{"complexity":"Medium","candidates":[],"provenance":null,"source":"config","reason":null,"notAfter":null,"updatedAt":null},{"complexity":"Easy","candidates":[],"provenance":null,"source":"config","reason":null,"notAfter":null,"updatedAt":null}]}
            """;

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Close();
        }
    }
}
