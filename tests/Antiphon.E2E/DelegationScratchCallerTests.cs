using System.Net;
using System.Net.Sockets;
using System.Text;
using Antiphon.E2E.Fixtures;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

/// <summary>
/// CARD-2004 A-1. Headed canaries build a scratch repo with <see cref="DelegationScratchRepo"/>
/// and run the delegate command from it. A request made from the scratch repository must carry the
/// registered caller label <c>delegate.ps1</c> (the delegate command, Antiphon.Nightly.DelegateCli,
/// kept the label of the script it replaced in unit 28 of the 2026-11-22 plan). The command runs in
/// process with the scratch repository as its working directory.
/// </summary>
[Category("Integration")]
public sealed class DelegationScratchCallerTests
{
    private const string TaskId = "12345678-1111-2222-3333-444444444444";

    // The name is the scratch-copy witness's (scripts/fixtures/nightly/c1584-e2e-source-roster.txt); the caller is now the
    // command, run from the scratch repository. The comment stays above the attributes: the C1584 source census reads a
    // test as attributes followed directly by its signature.
    [Test]
    public async Task Copied_delegate_loads_the_caller_helper_before_the_server_call()
    {
        using var repo = new DelegationScratchRepo("antiphon-scratch-caller");
        using var stub = new CallerStub();
        var run = await DelegateCommand.RunAsync(stub.BaseUrl.TrimEnd('/'), "synthetic-task-token", repo.Path, "status", TaskId);
        stub.Calls.ShouldNotBeEmpty(run.Stderr);
        stub.Calls.ShouldAllBe(call => call.Caller == "delegate.ps1");
        stub.Calls.ShouldContain(call => call.Path == "/api/agent-tasks/" + TaskId);
        run.ExitCode.ShouldBe(0, run.Stderr);
        run.Stdout.ShouldContain("Working");
    }

    /// <summary>Loopback stand-in for the server. Records <c>X-Antiphon-Caller</c> the way the registered-caller witness does.</summary>
    private sealed class CallerStub : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _pump;
        public readonly List<Call> Calls = [];
        public string BaseUrl { get; }

        public CallerStub()
        {
            BaseUrl = Bind(out _listener);
            _pump = Task.Run(PumpAsync);
        }

        private async Task PumpAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }
                var request = context.Request;
                Calls.Add(new Call(request.HttpMethod, request.Url!.AbsolutePath, request.Headers["X-Antiphon-Caller"]));
                var body = Encoding.UTF8.GetBytes(
                    """{"summary":{"status":"Working","title":"caller","kind":"Grok","role":"Code","modelLevel":"High"}}""");
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = body.Length;
                try { await context.Response.OutputStream.WriteAsync(body); }
                catch (HttpListenerException) { }
                finally { context.Response.Close(); }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); } catch { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch { }
            _listener.Close();
            _stop.Dispose();
        }

        private static string Bind(out HttpListener listener)
        {
            HttpListenerException? last = null;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var port = ((IPEndPoint)probe.LocalEndpoint).Port;
                var candidate = new HttpListener();
                var baseUrl = $"http://localhost:{port}/";
                candidate.Prefixes.Add(baseUrl);
                try
                {
                    probe.Stop();
                    candidate.Start();
                    listener = candidate;
                    return baseUrl;
                }
                catch (HttpListenerException ex)
                {
                    last = ex;
                    candidate.Close();
                    try { probe.Stop(); } catch { }
                }
            }

            throw new InvalidOperationException("Failed to bind a loopback listener for the scratch caller witness.", last);
        }
    }

    private readonly record struct Call(string Method, string Path, string? Caller);
}
