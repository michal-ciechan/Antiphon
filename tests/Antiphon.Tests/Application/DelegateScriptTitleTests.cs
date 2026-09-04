using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0351: <c>scripts/delegate.ps1 -Title</c> is capped at 80 locally, refused rather than
/// clamped, and omitted titles warn when the Goal first line would become a giant excerpt.
/// Driven against the REAL script under pwsh — a source match would pass if the cap never ran.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DelegateScriptTitleTests
{
    [Test]
    public async Task Title_is_posted_as_title()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Title", "add Fizz", "-Goal", "add Fizz(int) in Calc.cs");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("title").GetString().ShouldBe("add Fizz");
    }

    [Test]
    public async Task an_omitted_Title_sends_no_title_at_all()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Test", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("title", out _)
            .ShouldBeFalse("an omitted -Title must leave the request exactly as it was before the flag existed");
        run.Output.ShouldNotContain("WARNING");
    }

    [Test]
    public async Task Title_of_80_chars_is_posted()
    {
        var title = new string('a', 80);
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Title", title, "-Goal", "do the work");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("title").GetString().ShouldBe(title);
    }

    [Test]
    public async Task Title_of_81_chars_is_refused_before_any_request()
    {
        var title = new string('a', 81);
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Title", title, "-Goal", "do the work");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("80");
        server.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task a_multiline_Title_is_refused_before_any_request()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Title", "a\n b", "-Goal", "do the work");

        run.ExitCode.ShouldNotBe(0);
        run.Output.ShouldContain("single line");
        server.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task omitted_Title_with_a_long_Goal_first_line_warns_and_still_creates()
    {
        var firstLine = new string('x', 90);
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Goal", firstLine);

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.TryGetProperty("title", out _)
            .ShouldBeFalse("the warning still omits title so the server falls back to the Goal first line");
        run.Output.ShouldContain("WARNING");
        run.Output.ShouldContain("90");
    }

    [Test]
    public async Task a_padded_Title_is_posted_trimmed()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Title", "  short  ", "-Goal", "do the work");

        run.ExitCode.ShouldBe(0, run.Output);
        var body = server.LastBody.ShouldNotBeNull();
        body.RootElement.GetProperty("title").GetString().ShouldBe("short");
    }

    [Test]
    public async Task titleDiagnosisQueued_true_prints_title_pending()
    {
        using var server = new StubApi(
            """
            {"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111",
             "status":"Queued","modelLevel":"High","warning":null,"agentKind":"ClaudeCode",
             "titleDiagnosisQueued":true}
            """);
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Goal", new string('x', 90));

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldContain("title: pending");
        run.Output.ShouldContain("WARNING");
    }

    [Test]
    public async Task titleDiagnosisQueued_false_does_not_print_title_pending()
    {
        using var server = new StubApi(
            """
            {"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111",
             "status":"Queued","modelLevel":"High","warning":null,"agentKind":"ClaudeCode",
             "titleDiagnosisQueued":false}
            """);
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Goal", new string('x', 90));

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldNotContain("title: pending");
    }

    [Test]
    public async Task an_absent_titleDiagnosisQueued_does_not_print_title_pending()
    {
        using var server = new StubApi();
        var run = await RunDelegateAsync(server, "-Role", "Code", "-Goal", "run the suite");

        run.ExitCode.ShouldBe(0, run.Output);
        run.Output.ShouldNotContain("title: pending");
    }

    private static Task<(int ExitCode, string Output)> RunDelegateAsync(StubApi server, params string[] args) =>
        DelegateScriptRunner.RunAsync(server.BaseUrl, args);

    private sealed class StubApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly string _responseJson;

        public StubApi(string? responseJson = null)
        {
            _responseJson = responseJson ??
                """
                {"id":"11111111-1111-1111-1111-111111111111","shortId":"11111111",
                 "status":"Queued","modelLevel":"High","warning":null,"agentKind":"ClaudeCode"}
                """;
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }

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
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    var raw = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(raw)) LastBody = JsonDocument.Parse(raw);
                }

                var payload = Encoding.UTF8.GetBytes(_responseJson);
                context.Response.StatusCode = 201;
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
