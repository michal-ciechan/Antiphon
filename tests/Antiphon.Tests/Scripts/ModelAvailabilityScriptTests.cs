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
/// CARD-0309 S1: <c>scripts/model-availability.ps1</c> get/hold/clear against a stub API.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ModelAvailabilityScriptTests
{
    [Test]
    public async Task Hold_puts_disabledUntil_and_reason()
    {
        using var server = new StubApi();
        var run = await RunAsync(
            server, "hold", "-Kind", "ClaudeCode", "-Model", "fable",
            "-Until", "2026-09-04T00:00:00Z", "-Reason", "weekly cap");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("PUT");
        server.LastPath.ShouldBe("/api/model-availability/ClaudeCode/fable");
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("disabledUntil").GetString().ShouldBe("2026-09-04T00:00:00Z");
        body.RootElement.GetProperty("reason").GetString().ShouldBe("weekly cap");
    }

    [Test]
    public async Task Hold_star_encodes_the_alias()
    {
        using var server = new StubApi();
        var run = await RunAsync(
            server, "hold", "-Kind", "ClaudeCode", "-Model", "*",
            "-Until", "2026-09-04T00:00:00Z");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("PUT");
        server.LastPath.ShouldBe("/api/model-availability/ClaudeCode/%2A");
    }

    [Test]
    public async Task Hold_without_Until_omits_disabledUntil()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "hold", "-Kind", "ClaudeCode", "-Model", "fable");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("disabledUntil", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Naive_Until_is_refused_before_any_request()
    {
        using var server = new StubApi();
        var run = await RunAsync(
            server, "hold", "-Kind", "ClaudeCode", "-Model", "fable",
            "-Until", "2026-09-04T00:00:00");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("Naive");
        server.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task Clear_deletes_the_hold()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "clear", "-Kind", "ClaudeCode", "-Model", "fable");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("DELETE");
        server.LastPath.ShouldBe("/api/model-availability/ClaudeCode/fable");
    }

    [Test]
    public async Task Get_prints_available()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "get");

        run.ExitCode.ShouldBe(0, run.Output);
        server.LastMethod.ShouldBe("GET");
        run.Output.ShouldContain("available:");
        run.Output.ShouldContain("opus");
    }

    private static Task<(int ExitCode, string Output)> RunAsync(StubApi server, params string[] args)
    {
        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "model-availability.ps1");
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
                    """
                    {"holds":[],"available":["opus","sonnet","haiku","grok-4.6"],
                     "id":"11111111-1111-1111-1111-111111111111","kind":"ClaudeCode",
                     "modelAlias":"fable","source":"Manual","disabledUntil":"2026-09-04T00:00:00Z",
                     "hitAt":"2026-09-01T00:00:00Z","reason":"weekly cap","rawText":null,
                     "sourceSessionId":null,"sourceTaskId":null}
                    """);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            _listener.Close();
            LastBody?.Dispose();
            _cts.Dispose();
        }
    }
}
