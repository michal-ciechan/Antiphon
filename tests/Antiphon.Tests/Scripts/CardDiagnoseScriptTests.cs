using System.Diagnostics;
using System.Net;
using System.Text;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0352 S4: <c>scripts/card.ps1 diagnose</c> against a stub API. Posts the forced diagnose
/// route and prints the ledger row, or <c>-NoWait</c> prints the 202.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CardDiagnoseScriptTests
{
    private const string CardId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    [Test]
    public async Task diagnose_posts_the_route_and_prints_the_ledger_row()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "diagnose", "CARD-0001");

        run.ExitCode.ShouldBe(0, run.Output);
        server.PostedDiagnose.ShouldBeTrue();
        server.LastDiagnosePath.ShouldContain($"/api/cards/{CardId}/diagnose");
        run.Output.ShouldContain("CARD-0001");
        run.Output.ShouldContain("Applied");
        run.Output.ShouldContain("complexity=medium ui=no");
    }

    [Test]
    public async Task diagnose_NoWait_prints_the_202_and_does_not_poll()
    {
        using var server = new StubApi();
        var run = await RunAsync(server, "diagnose", "CARD-0001", "-NoWait");

        run.ExitCode.ShouldBe(0, run.Output);
        server.PostedDiagnose.ShouldBeTrue();
        server.PolledDiagnoses.ShouldBeFalse();
        run.Output.ShouldContain("202");
        run.Output.ShouldContain("queued");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(StubApi server, params string[] args)
    {
        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "card.ps1");
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
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public bool PostedDiagnose { get; private set; }
        public bool PolledDiagnoses { get; private set; }
        public string? LastDiagnosePath { get; private set; }

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                var path = context.Request.Url?.AbsolutePath ?? "";
                var method = context.Request.HttpMethod;
                byte[] payload;
                var status = 200;

                if (method == "GET" && path == "/api/cards/limits")
                {
                    payload = Encoding.UTF8.GetBytes(
                        """{"maxTitleLength":300,"maxDescriptionLength":20000,"maxReasonLength":4000,"maxActorLength":200,"maxAliasLength":64,"maxAliasWords":5,"importanceValues":["Low","Normal","High","Critical"],"urgencyValues":["Normal","Soon","Now"]}""");
                }
                else if (method == "GET" && path.StartsWith("/api/cards/", StringComparison.OrdinalIgnoreCase)
                         && !path.Contains("/diagnose", StringComparison.OrdinalIgnoreCase)
                         && !path.Contains("/revisions", StringComparison.OrdinalIgnoreCase))
                {
                    payload = Encoding.UTF8.GetBytes(
                        $$"""
                        {"id":"{{CardId}}","identifier":"CARD-0001","status":"Backlog","title":"Unlabelled",
                         "importance":"Normal","urgency":"Normal","rank":0,"importanceProvenance":"Auto",
                         "concurrencyToken":"11111111-1111-1111-1111-111111111111","revisionCount":0,
                         "boardId":"22222222-2222-2222-2222-222222222222","boardColumnId":"33333333-3333-3333-3333-333333333333",
                         "labels":[],"updatedAt":"2000-01-01T00:00:00Z"}
                        """);
                }
                else if (method == "POST" && path.Contains("/diagnose", StringComparison.OrdinalIgnoreCase))
                {
                    PostedDiagnose = true;
                    LastDiagnosePath = context.Request.Url?.PathAndQuery;
                    status = 202;
                    payload = Encoding.UTF8.GetBytes("""{"queued":true}""");
                }
                else if (method == "GET" && path.StartsWith("/api/diagnoses", StringComparison.OrdinalIgnoreCase))
                {
                    PolledDiagnoses = true;
                    payload = Encoding.UTF8.GetBytes(
                        """
                        [{"id":"44444444-4444-4444-4444-444444444444","kind":"Labels","outcome":"Applied",
                          "cardId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","cardIdentifier":"CARD-0001",
                          "applied":"complexity=medium ui=no","answer":"complexity=medium ui=no",
                          "costUsd":0.0091,"waitMs":1200,"forced":true,"createdAt":"2100-01-01T00:00:00Z"}]
                        """);
                }
                else
                {
                    status = 404;
                    payload = Encoding.UTF8.GetBytes("""{"detail":"not stubbed"}""");
                }

                context.Response.StatusCode = status;
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
            _cts.Dispose();
        }
    }
}
