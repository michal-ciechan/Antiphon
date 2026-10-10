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
/// CARD-0505 V-8. <c>scripts/dispatch-concurrency.ps1</c> against a loopback recorder.
/// The token is a header only, and a 409 is not retried.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DispatchConcurrencyScriptTests
{
    private const string Token = "c0505-token-sentinel-do-not-print";
    private static readonly Guid Project = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Test]
    [Timeout(60_000)]
    public async Task Get_and_history_use_correct_scope()
    {
        using var server = new Recorder();
        var global = await RunAsync(server, "get");
        var globalHistory = await RunAsync(server, "history", "-Limit", "2", "-BeforeRevision", "9");
        var project = await RunAsync(server, "get", "-Project", Project.ToString("D"));
        var projectHistory = await RunAsync(server, "history", "-Project", Project.ToString("D"), "-Limit", "1", "-BeforeRevision", "3");

        global.ExitCode.ShouldBe(0);
        globalHistory.ExitCode.ShouldBe(0);
        project.ExitCode.ShouldBe(0);
        projectHistory.ExitCode.ShouldBe(0);
        server.Hits.Select(hit => hit.Method).ShouldAllBe(method => method == "GET");
        server.Hits.Select(hit => hit.Path).ShouldBe(new[]
        {
            "/api/dispatch-concurrency",
            "/api/dispatch-concurrency/revisions?limit=2&beforeRevision=9",
            $"/api/projects/{Project:D}/dispatch-concurrency",
            $"/api/projects/{Project:D}/dispatch-concurrency/revisions?limit=1&beforeRevision=3",
        });
        JsonDocument.Parse(global.Output).RootElement.GetProperty("revision").GetInt32().ShouldBe(4);
        JsonDocument.Parse(project.Output).RootElement.GetProperty("globalRevision").GetInt32().ShouldBe(9);
        JsonDocument.Parse(globalHistory.Output).RootElement.GetProperty("revisions").GetArrayLength().ShouldBe(0);
        server.Hits.ShouldAllBe(hit => hit.Token == Token);
        foreach (var run in new[] { global, globalHistory, project, projectHistory })
            run.Output.ShouldNotContain(Token);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Set_reads_then_sends_exact_snapshot()
    {
        var inline = """{"maxQueued":null,"maxParallel":0,"roles":{"Code":{"maxParallel":2}}}""";
        var fileJson = """{"mode":"SeparateQueues","maxQueued":0,"roles":{"Review":{"maxQueued":null}}}""";
        var path = Path.Combine(Path.GetTempPath(), "c0505-settings-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, fileJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            using var server = new Recorder();
            var fromFile = await RunAsync(server, "set", "-SettingsFile", path, "-Reason", "from file");
            var inlineGlobal = await RunAsync(
                server, "set", "-Settings", inline, "-Reason", "inline", "-ExpectedRevision", "11");
            var inlineProject = await RunAsync(
                server, "set", "-Project", Project.ToString("D"), "-Settings", inline, "-Reason", "project inline",
                "-ExpectedRevision", "11", "-ExpectedGlobalRevision", "2");

            fromFile.ExitCode.ShouldBe(0);
            inlineGlobal.ExitCode.ShouldBe(0);
            inlineProject.ExitCode.ShouldBe(0);
            server.Hits.Select(hit => hit.Method).ShouldBe(new[] { "GET", "PUT", "GET", "PUT", "GET", "PUT" });
            var filePut = server.Hits[1];
            filePut.Path.ShouldBe("/api/dispatch-concurrency");
            filePut.Body.ShouldContain(fileJson);
            filePut.Body.ShouldNotContain("expectedGlobalRevision");
            using (var fileDoc = JsonDocument.Parse(filePut.Body))
            {
                fileDoc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(4);
                fileDoc.RootElement.GetProperty("overrides").GetProperty("maxQueued").GetInt32().ShouldBe(0);
                fileDoc.RootElement.GetProperty("overrides").GetProperty("roles").GetProperty("Review")
                    .GetProperty("maxQueued").ValueKind.ShouldBe(JsonValueKind.Null);
                fileDoc.RootElement.GetProperty("overrides").TryGetProperty("maxParallel", out _).ShouldBeFalse();
            }

            var globalPut = server.Hits[3];
            globalPut.Path.ShouldBe("/api/dispatch-concurrency");
            globalPut.Body.ShouldContain(inline);
            globalPut.Body.ShouldNotContain("expectedGlobalRevision");
            using (var globalDoc = JsonDocument.Parse(globalPut.Body))
                globalDoc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(11);

            var projectPut = server.Hits[5];
            projectPut.Path.ShouldBe($"/api/projects/{Project:D}/dispatch-concurrency");
            projectPut.Body.ShouldContain(inline);
            using var projectDoc = JsonDocument.Parse(projectPut.Body);
            projectDoc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(11);
            projectDoc.RootElement.GetProperty("expectedGlobalRevision").GetInt64().ShouldBe(2);
            projectDoc.RootElement.GetProperty("overrides").GetProperty("maxQueued").ValueKind.ShouldBe(JsonValueKind.Null);
            projectDoc.RootElement.GetProperty("overrides").GetProperty("maxParallel").GetInt32().ShouldBe(0);
            foreach (var run in new[] { fromFile, inlineGlobal, inlineProject })
                run.Output.ShouldNotContain(Token);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clear_sends_empty_override_with_reason()
    {
        using var server = new Recorder();
        var global = await RunAsync(server, "clear", "-Reason", "clear global");
        var project = await RunAsync(server, "clear", "-Project", Project.ToString("D"), "-Reason", "clear project");
        global.ExitCode.ShouldBe(0);
        project.ExitCode.ShouldBe(0);
        server.Hits.Select(hit => hit.Method).ShouldBe(new[] { "GET", "PUT", "GET", "PUT" });
        server.Hits.Count(hit => hit.Method == "PUT").ShouldBe(2);
        using (var doc = JsonDocument.Parse(server.Hits[1].Body))
        {
            doc.RootElement.GetProperty("overrides").GetRawText().ShouldBe("{}");
            doc.RootElement.GetProperty("reason").GetString().ShouldBe("clear global");
            doc.RootElement.GetProperty("provenance").GetString().ShouldBe("Human");
            doc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(4);
            doc.RootElement.TryGetProperty("expectedGlobalRevision", out _).ShouldBeFalse();
            server.Hits[1].Body.ShouldContain("\"overrides\":{}");
        }

        using var projectDoc = JsonDocument.Parse(server.Hits[3].Body);
        projectDoc.RootElement.GetProperty("overrides").GetRawText().ShouldBe("{}");
        projectDoc.RootElement.GetProperty("reason").GetString().ShouldBe("clear project");
        projectDoc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(4);
        projectDoc.RootElement.GetProperty("expectedGlobalRevision").GetInt64().ShouldBe(9);
        server.Hits[3].Body.ShouldContain("\"overrides\":{}");
        server.Hits[3].Body.ShouldNotContain("LegacyOpen");
        global.Output.ShouldNotContain(Token);
        project.Output.ShouldNotContain(Token);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Human_and_reasonfile_are_preserved()
    {
        var source = await File.ReadAllBytesAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "dispatch-concurrency.ps1"));
        source.All(b => b < 128).ShouldBeTrue();
        var text = Encoding.ASCII.GetString(source);
        text.ShouldNotContain("SkipHttpErrorCheck");
        text.ShouldNotContain("??");

        var reason = "  keep\n\"quoted\" and `tick`  \n";
        var path = Path.Combine(Path.GetTempPath(), "c0505-reason-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, reason, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            using var server = new Recorder();
            var human = await RunAsync(server, "set", "-Settings", "{}", "-ReasonFile", path, "-Provenance", "Human");
            var auto = await RunAsync(server, "set", "-Settings", "{}", "-Reason", "auto-inline", "-Provenance", "Auto");
            human.ExitCode.ShouldBe(0);
            auto.ExitCode.ShouldBe(0);
            using (var doc = JsonDocument.Parse(server.Hits[1].Body))
            {
                doc.RootElement.GetProperty("reason").GetString().ShouldBe(reason);
                doc.RootElement.GetProperty("provenance").GetString().ShouldBe("Human");
            }

            using (var doc = JsonDocument.Parse(server.Hits[3].Body))
            {
                doc.RootElement.GetProperty("reason").GetString().ShouldBe("auto-inline");
                doc.RootElement.GetProperty("provenance").GetString().ShouldBe("Auto");
            }

            server.Hits.ShouldAllBe(hit => hit.Token == Token);
            human.Output.ShouldNotContain(Token);
            auto.Output.ShouldNotContain(Token);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Conflict_is_not_retried_or_overwritten()
    {
        using var server = new Recorder { ConflictOnce = true };
        var run = await RunAsync(server, "set", "-Settings", """{"maxParallel":2}""", "-Reason", "stale");
        run.ExitCode.ShouldNotBe(0, "single-conflict-put");
        run.Output.ShouldContain("dispatch_concurrency_revision_conflict", Case.Insensitive, "single-conflict-put");
        run.Output.ShouldNotContain(Token);
        server.Hits.Select(hit => hit.Method).ShouldBe(new[] { "GET", "PUT" }, "single-conflict-put");
        server.PutCount.ShouldBe(1, "single-conflict-put");
        server.WouldAcceptAnotherPut.ShouldBeTrue("single-conflict-put");
        using var doc = JsonDocument.Parse(server.Hits[1].Body);
        doc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(4, "single-conflict-put");
    }

    [Test]
    [Timeout(120_000)]
    public async Task Missing_input_and_invalid_json_do_not_write()
    {
        var settings = Path.Combine(Path.GetTempPath(), "c0505-both-" + Guid.NewGuid().ToString("N") + ".json");
        var reason = Path.Combine(Path.GetTempPath(), "c0505-both-reason-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(settings, "{}", new UTF8Encoding(false));
        await File.WriteAllTextAsync(reason, "because", new UTF8Encoding(false));
        try
        {
            var cases = new (string[] Args, string Diagnostic)[]
            {
                (["set", "-Reason", "because"], "settings"),
                (["set", "-Settings", "{bad", "-Reason", "because"], "JSON"),
                (["set", "-Settings", "{}"], "reason"),
                (["set", "-Settings", "{}", "-Reason", "because", "-Provenance", "Nope"], "Provenance"),
                (["set", "-Project", "not-a-guid", "-Settings", "{}", "-Reason", "because"], "project"),
                (["set", "-Settings", "{}", "-SettingsFile", settings, "-Reason", "because"], "mutually exclusive"),
                (["set", "-Settings", "{}", "-Reason", "because", "-ReasonFile", reason], "mutually exclusive"),
                (["clear", "-Settings", "{}", "-Reason", "because"], "empty overrides"),
                (["get", "-Settings", "{}"], "does not accept"),
            };
            foreach (var (args, diagnostic) in cases)
            {
                using var server = new Recorder();
                var run = await RunAsync(server, args);
                run.ExitCode.ShouldNotBe(0);
                run.Output.ShouldContain(diagnostic, Case.Insensitive);
                run.Output.ShouldNotContain(Token);
                server.Hits.ShouldBeEmpty();
                server.PutCount.ShouldBe(0);
            }
        }
        finally
        {
            File.Delete(settings);
            File.Delete(reason);
        }
    }

    private static Task<(int ExitCode, string Output)> RunAsync(Recorder server, params string[] args)
    {
        var scriptPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "dispatch-concurrency.ps1");
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
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = Token;
        return RunProcessAsync(startInfo);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private sealed class Recorder : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;

        public Recorder()
        {
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public List<Hit> Hits { get; } = [];
        public bool ConflictOnce { get; init; }
        public int PutCount { get; private set; }
        public bool WouldAcceptAnotherPut => true;

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                string body;
                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                    body = await reader.ReadToEndAsync();
                var token = context.Request.Headers["X-Antiphon-Task-Token"];
                var path = context.Request.Url?.PathAndQuery ?? "";
                Hits.Add(new Hit(context.Request.HttpMethod, path, body, token));
                var conflict = ConflictOnce && context.Request.HttpMethod == "PUT" && Interlocked.Increment(ref PutCountUnsafe) == 1;
                if (context.Request.HttpMethod == "PUT" && !conflict)
                    PutCount = Hits.Count(hit => hit.Method == "PUT");
                if (conflict)
                    PutCount = 1;
                var status = conflict ? 409 : 200;
                var payload = conflict
                    ? """{"code":"dispatch_concurrency_revision_conflict","status":409}"""
                    : path.Contains("/projects/", StringComparison.Ordinal)
                        ? """{"revision":4,"globalRevision":9,"scope":"project"}"""
                        : path.Contains("/revisions", StringComparison.Ordinal)
                            ? """{"revisions":[],"nextBeforeRevision":null}"""
                            : """{"revision":4,"scope":"global"}""";
                var bytes = Encoding.UTF8.GetBytes(payload);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        private int PutCountUnsafe;

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            _listener.Close();
            _cts.Dispose();
        }
    }

    private sealed record Hit(string Method, string Path, string Body, string? Token);
}
