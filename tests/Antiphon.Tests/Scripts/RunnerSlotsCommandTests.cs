using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0653. The runner-slots command (Antiphon.Nightly.RunnerSlots, which replaced scripts/runner-slots.ps1 in unit 23 of the
/// 2026-11-22 plan) in process, on a recording transport: the three verbs and their routes, the operator token read from its
/// file for the release verbs only and never printed, the task token passed when set, the ids escaped, a missing or empty token
/// file refused before any request, and the server's failure passed on. Nothing leaves the process.
/// </summary>
[Category("Integration")]
public sealed class RunnerSlotsCommandTests
{
    private const string Session = "0b5c3f0e-2f2a-4b0a-9c55-111111111111";

    [Test]
    public async Task List_reads_without_the_operator_token_and_prints_the_answer_indented()
    {
        using var directory = new RunnerSlotsTokenFolder();
        await directory.WriteAsync("operator-secret");
        var http = new RecordingOperatorHttp(_ => new(200, """{"declaredCapacity":10,"occupied":1,"slots":[{"sessionId":"s","orphan":false}]}"""));

        var list = Run(http, directory.Path, "task-token", "list");
        list.Exit.ShouldBe(0, list.Error);
        var call = http.Calls.Single();
        call.Method.ShouldBe("GET");
        call.Path.ShouldBe("/api/session-runners/server2/slots");
        call.Body.ShouldBeNull();
        call.Headers.ShouldNotContainKey("X-Antiphon-Operator-Token");
        call.Headers["X-Antiphon-Task-Token"].ShouldBe("task-token");
        list.Output.ShouldContain("\n  \"declaredCapacity\": 10,", Case.Sensitive);
        using (var printed = JsonDocument.Parse(list.Output))
            printed.RootElement.GetProperty("slots")[0].GetProperty("sessionId").GetString().ShouldBe("s");

        Run(http, Path.Combine(directory.Path, "missing"), null, "list", "--runner-id", "server2 temp").Exit.ShouldBe(0);
        http.Calls[^1].Path.ShouldBe("/api/session-runners/server2%20temp/slots");
        http.Calls[^1].Headers.ShouldBeEmpty();
    }

    [Test]
    public async Task Release_and_release_orphans_send_the_reason_and_the_operator_token_without_printing_it()
    {
        using var directory = new RunnerSlotsTokenFolder();
        var token = await directory.WriteAsync("operator-secret-" + Guid.NewGuid().ToString("N") + "\r\n");
        var http = new RecordingOperatorHttp(_ => new(200, """{"released":true,"killed":3}"""));

        var release = Run(http, directory.Path, null, "release", "--session-id", Session, "--reason", "CARD-0934 four-hour drain cap");
        release.Exit.ShouldBe(0, release.Error);
        var call = http.Calls.Single();
        call.Method.ShouldBe("POST");
        call.Path.ShouldBe("/api/session-runners/server2/slots/" + Session + "/release");
        call.Body.ShouldBe("""{"reason":"CARD-0934 four-hour drain cap"}""");
        call.Headers["X-Antiphon-Operator-Token"].ShouldBe(token.Trim());
        call.Headers.ShouldNotContainKey("X-Antiphon-Task-Token");

        var orphans = Run(http, directory.Path, "task-token", "release-orphans", "--runner-id", "server2-temp", "--reason", "orphan sweep");
        orphans.Exit.ShouldBe(0, orphans.Error);
        http.Calls[^1].Method.ShouldBe("POST");
        http.Calls[^1].Path.ShouldBe("/api/session-runners/server2-temp/slots/release-orphans");
        http.Calls[^1].Body.ShouldBe("""{"reason":"orphan sweep"}""");
        http.Calls[^1].Headers["X-Antiphon-Operator-Token"].ShouldBe(token.Trim());
        http.Calls[^1].Headers["X-Antiphon-Task-Token"].ShouldBe("task-token");

        foreach (var text in new[] { release.Output, release.Error, orphans.Output, orphans.Error })
            text.ShouldNotContain(token.Trim());
    }

    [Test]
    public async Task A_missing_or_empty_token_file_refuses_before_any_request()
    {
        using var directory = new RunnerSlotsTokenFolder();
        var http = new RecordingOperatorHttp(_ => throw new InvalidOperationException("no request is sent"));
        var missing = Run(http, directory.Path, null, "release-orphans", "--reason", "why");
        missing.Exit.ShouldBe(1);
        missing.Output.ShouldBeEmpty();
        missing.Error.Trim().ShouldBe("Operator token file not found at " + directory.File
            + ". The server creates it at startup; run this as the account the server runs under.");
        await directory.WriteAsync("  \n");
        var empty = Run(http, directory.Path, null, "release", "--session-id", Session, "--reason", "why");
        empty.Exit.ShouldBe(1);
        empty.Error.Trim().ShouldBe("Operator token file at " + directory.File + " is empty.");
        http.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task A_server_refusal_is_one_line_and_exit_1()
    {
        using var directory = new RunnerSlotsTokenFolder();
        await directory.WriteAsync("operator-secret");
        var http = new RecordingOperatorHttp(_ => new(403, """{"error":"operator_token_required"}"""));
        var run = Run(http, directory.Path, null, "release", "--session-id", Session, "--reason", "why");
        run.Exit.ShouldBe(1);
        run.Output.ShouldBeEmpty();
        run.Error.Trim().ShouldBe("runner-slots: POST /api/session-runners/server2/slots/" + Session
            + """/release failed: HTTP 403 {"error":"operator_token_required"}""");
        run.Error.ShouldNotContain("operator-secret");

        var empty = new RecordingOperatorHttp(_ => new(200, ""));
        Run(empty, directory.Path, null, "list").Output.Trim().ShouldBe("null");
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error()
    {
        Should.Throw<NightlyUsageException>(() => RunnerSlotsOptions.Parse(["purge"], null, null, "t")).Message
            .ShouldBe("the first argument is the verb: list, release or release-orphans, not 'purge'");
        Should.Throw<NightlyUsageException>(() => RunnerSlotsOptions.Parse(["release", "--reason", "why"], null, null, "t")).Message
            .ShouldBe("release requires --session-id");
        Should.Throw<NightlyUsageException>(() => RunnerSlotsOptions.Parse(["release", "--session-id", Session], null, null, "t")).Message
            .ShouldBe("release requires --reason");
        Should.Throw<NightlyUsageException>(() => RunnerSlotsOptions.Parse(["release-orphans"], null, null, "t")).Message
            .ShouldBe("release-orphans requires --reason");
        Should.Throw<NightlyUsageException>(() => RunnerSlotsOptions.Parse(["list", "-RunnerId", "server2"], null, null, "t"));
        var list = RunnerSlotsOptions.Parse(["list"], null, null, "t");
        (list.RunnerId, list.Api).ShouldBe(("server2", "http://localhost:17202"));
    }

    private static (int Exit, string Output, string Error) Run(RecordingOperatorHttp http, string tokenDirectory, string? taskToken, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var options = RunnerSlotsOptions.Parse(args, "http://127.0.0.1:17202/", taskToken, Path.Combine(tokenDirectory, "operator-token"));
        var exit = RunnerSlots.Run(options, NightlyHost.Production(output, error), new System.IO.Abstractions.FileSystem(), http.Transport);
        return (exit, output.ToString(), error.ToString());
    }

    private sealed class RunnerSlotsTokenFolder : IDisposable
    {
        public RunnerSlotsTokenFolder() => Path = Directory.CreateTempSubdirectory("c653-operator-").FullName;

        public string Path { get; }

        public string File => System.IO.Path.Combine(Path, "operator-token");

        public async Task<string> WriteAsync(string token)
        {
            await System.IO.File.WriteAllTextAsync(File, token);
            return token;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
