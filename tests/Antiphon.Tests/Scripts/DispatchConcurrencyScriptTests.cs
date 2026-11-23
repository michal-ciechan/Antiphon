using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0505 V-8. The dispatch-concurrency command (Antiphon.Nightly.DispatchConcurrencyCli, which replaced
/// scripts/dispatch-concurrency.ps1 in unit 24 of the 2026-11-22 plan) in process against a recording transport that answers
/// as the policy API. The token is a header only, and a 409 is not retried. Nothing leaves the process.
/// </summary>
[Category("Integration")]
public sealed class DispatchConcurrencyScriptTests
{
    private const string Token = "c0505-token-sentinel-do-not-print";
    private static readonly Guid Project = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Test]
    public void Get_and_history_use_correct_scope()
    {
        var server = new PolicyApi();
        var global = Run(server, "get");
        var globalHistory = Run(server, "history", "--limit", "2", "--before-revision", "9");
        var project = Run(server, "get", "--project", Project.ToString("D"));
        var projectHistory = Run(server, "history", "--project", Project.ToString("D"), "--limit", "1", "--before-revision", "3");

        global.Exit.ShouldBe(0, global.All);
        globalHistory.Exit.ShouldBe(0, globalHistory.All);
        project.Exit.ShouldBe(0, project.All);
        projectHistory.Exit.ShouldBe(0, projectHistory.All);
        server.Http.Calls.Select(hit => hit.Method).ShouldAllBe(method => method == "GET");
        server.Http.Calls.Select(hit => hit.Path).ShouldBe(new[]
        {
            "/api/dispatch-concurrency",
            "/api/dispatch-concurrency/revisions?limit=2&beforeRevision=9",
            $"/api/projects/{Project:D}/dispatch-concurrency",
            $"/api/projects/{Project:D}/dispatch-concurrency/revisions?limit=1&beforeRevision=3",
        });
        global.Output.Trim().ShouldBe("""{"revision":4,"scope":"global"}""", "compact JSON, one line");
        JsonDocument.Parse(project.Output).RootElement.GetProperty("globalRevision").GetInt32().ShouldBe(9);
        JsonDocument.Parse(globalHistory.Output).RootElement.GetProperty("revisions").GetArrayLength().ShouldBe(0);
        server.Http.Calls.ShouldAllBe(hit => hit.Headers["X-Antiphon-Task-Token"] == Token);
        foreach (var run in new[] { global, globalHistory, project, projectHistory })
            run.All.ShouldNotContain(Token);
    }

    [Test]
    public async Task Set_reads_then_sends_exact_snapshot()
    {
        var inline = """{"maxQueued":null,"maxParallel":0,"roles":{"Code":{"maxParallel":2}}}""";
        var fileJson = """{"mode":"SeparateQueues","maxQueued":0,"roles":{"Review":{"maxQueued":null}}}""";
        var path = Path.Combine(Path.GetTempPath(), "c0505-settings-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, fileJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            var server = new PolicyApi();
            var fromFile = Run(server, "set", "--settings-file", path, "--reason", "from file");
            var inlineGlobal = Run(server, "set", "--settings", inline, "--reason", "inline", "--expected-revision", "11");
            var inlineProject = Run(server, "set", "--project", Project.ToString("D"), "--settings", inline, "--reason", "project inline",
                "--expected-revision", "11", "--expected-global-revision", "2");

            fromFile.Exit.ShouldBe(0, fromFile.All);
            inlineGlobal.Exit.ShouldBe(0, inlineGlobal.All);
            inlineProject.Exit.ShouldBe(0, inlineProject.All);
            var hits = server.Http.Calls;
            hits.Select(hit => hit.Method).ShouldBe(new[] { "GET", "PUT", "GET", "PUT", "GET", "PUT" });
            var filePut = hits[1];
            filePut.Path.ShouldBe("/api/dispatch-concurrency");
            filePut.Body!.ShouldContain(fileJson, Case.Sensitive, "the file's text verbatim, its BOM dropped");
            filePut.Body.ShouldNotContain("expectedGlobalRevision");
            using (var fileDoc = JsonDocument.Parse(filePut.Body))
            {
                fileDoc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(4);
                fileDoc.RootElement.GetProperty("overrides").GetProperty("maxQueued").GetInt32().ShouldBe(0);
                fileDoc.RootElement.GetProperty("overrides").GetProperty("roles").GetProperty("Review")
                    .GetProperty("maxQueued").ValueKind.ShouldBe(JsonValueKind.Null);
                fileDoc.RootElement.GetProperty("overrides").TryGetProperty("maxParallel", out _).ShouldBeFalse();
            }

            var globalPut = hits[3];
            globalPut.Path.ShouldBe("/api/dispatch-concurrency");
            globalPut.Body!.ShouldContain(inline);
            globalPut.Body.ShouldNotContain("expectedGlobalRevision");
            using (var globalDoc = JsonDocument.Parse(globalPut.Body))
                globalDoc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(11);

            var projectPut = hits[5];
            projectPut.Path.ShouldBe($"/api/projects/{Project:D}/dispatch-concurrency");
            projectPut.Body!.ShouldContain(inline);
            using var projectDoc = JsonDocument.Parse(projectPut.Body);
            projectDoc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(11);
            projectDoc.RootElement.GetProperty("expectedGlobalRevision").GetInt64().ShouldBe(2);
            projectDoc.RootElement.GetProperty("overrides").GetProperty("maxQueued").ValueKind.ShouldBe(JsonValueKind.Null);
            projectDoc.RootElement.GetProperty("overrides").GetProperty("maxParallel").GetInt32().ShouldBe(0);
            foreach (var run in new[] { fromFile, inlineGlobal, inlineProject })
                run.All.ShouldNotContain(Token);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Clear_sends_empty_override_with_reason()
    {
        var server = new PolicyApi();
        var global = Run(server, "clear", "--reason", "clear global");
        var project = Run(server, "clear", "--project", Project.ToString("D"), "--reason", "clear project");
        global.Exit.ShouldBe(0, global.All);
        project.Exit.ShouldBe(0, project.All);
        var hits = server.Http.Calls;
        hits.Select(hit => hit.Method).ShouldBe(new[] { "GET", "PUT", "GET", "PUT" });
        using (var doc = JsonDocument.Parse(hits[1].Body!))
        {
            doc.RootElement.GetProperty("overrides").GetRawText().ShouldBe("{}");
            doc.RootElement.GetProperty("reason").GetString().ShouldBe("clear global");
            doc.RootElement.GetProperty("provenance").GetString().ShouldBe("Human");
            doc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(4);
            doc.RootElement.TryGetProperty("expectedGlobalRevision", out _).ShouldBeFalse();
            hits[1].Body!.ShouldContain("\"overrides\":{}");
        }

        using var projectDoc = JsonDocument.Parse(hits[3].Body!);
        projectDoc.RootElement.GetProperty("overrides").GetRawText().ShouldBe("{}");
        projectDoc.RootElement.GetProperty("reason").GetString().ShouldBe("clear project");
        projectDoc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(4);
        projectDoc.RootElement.GetProperty("expectedGlobalRevision").GetInt64().ShouldBe(9);
        hits[3].Body!.ShouldContain("\"overrides\":{}");
        hits[3].Body!.ShouldNotContain("LegacyOpen");
        global.All.ShouldNotContain(Token);
        project.All.ShouldNotContain(Token);
    }

    [Test]
    public async Task Human_and_reasonfile_are_preserved()
    {
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "DispatchConcurrencyCli.cs")).ShouldAllBe(b => b < 128);

        var reason = "  keep\n\"quoted\" and `tick`  \n";
        var path = Path.Combine(Path.GetTempPath(), "c0505-reason-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, reason, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            var server = new PolicyApi();
            var human = Run(server, "set", "--settings", "{}", "--reason-file", path, "--provenance", "Human");
            var auto = Run(server, "set", "--settings", "{}", "--reason", "auto-inline", "--provenance", "auto");
            human.Exit.ShouldBe(0, human.All);
            auto.Exit.ShouldBe(0, auto.All);
            var hits = server.Http.Calls;
            using (var doc = JsonDocument.Parse(hits[1].Body!))
            {
                doc.RootElement.GetProperty("reason").GetString().ShouldBe(reason);
                doc.RootElement.GetProperty("provenance").GetString().ShouldBe("Human");
            }

            using (var doc = JsonDocument.Parse(hits[3].Body!))
            {
                doc.RootElement.GetProperty("reason").GetString().ShouldBe("auto-inline");
                doc.RootElement.GetProperty("provenance").GetString().ShouldBe("Auto", "sent in its listed spelling");
            }

            hits.ShouldAllBe(hit => hit.Headers["X-Antiphon-Task-Token"] == Token);
            human.All.ShouldNotContain(Token);
            auto.All.ShouldNotContain(Token);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Conflict_is_not_retried_or_overwritten()
    {
        var server = new PolicyApi { ConflictOnce = true };
        var run = Run(server, "set", "--settings", """{"maxParallel":2}""", "--reason", "stale");
        run.Exit.ShouldBe(1, "single-conflict-put");
        run.Error.ShouldContain("dispatch_concurrency_revision_conflict", Case.Insensitive, "single-conflict-put");
        run.Error.ShouldStartWith("Antiphon PUT /api/dispatch-concurrency failed: HTTP 409 ", Case.Sensitive, "single-conflict-put");
        run.All.ShouldNotContain(Token);
        server.Http.Calls.Select(hit => hit.Method).ShouldBe(new[] { "GET", "PUT" }, "single-conflict-put");
        using var doc = JsonDocument.Parse(server.Http.Calls[1].Body!);
        doc.RootElement.GetProperty("expectedRevision").GetInt64().ShouldBe(4, "single-conflict-put");
    }

    [Test]
    public async Task Missing_input_and_invalid_json_do_not_write()
    {
        var settings = Path.Combine(Path.GetTempPath(), "c0505-both-" + Guid.NewGuid().ToString("N") + ".json");
        var reason = Path.Combine(Path.GetTempPath(), "c0505-both-reason-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(settings, "{}", new UTF8Encoding(false));
        await File.WriteAllTextAsync(reason, "because", new UTF8Encoding(false));
        try
        {
            var cases = new (string[] Args, int Exit, string Diagnostic)[]
            {
                (["set", "--reason", "because"], 2, "settings"),
                (["set", "--settings", "{bad", "--reason", "because"], 1, "JSON"),
                (["set", "--settings", "{}"], 2, "reason"),
                (["set", "--settings", "{}", "--reason", "because", "--provenance", "Nope"], 2, "Provenance"),
                (["set", "--project", "not-a-guid", "--settings", "{}", "--reason", "because"], 1, "project"),
                (["set", "--settings", "{}", "--settings-file", settings, "--reason", "because"], 2, "mutually exclusive"),
                (["set", "--settings", "{}", "--reason", "because", "--reason-file", reason], 2, "mutually exclusive"),
                (["clear", "--settings", "{}", "--reason", "because"], 2, "empty overrides"),
                (["get", "--settings", "{}"], 2, "does not accept"),
                (["set", "--settings-file", settings + ".missing", "--reason", "because"], 1, "settings file was not found"),
                (["set", "--settings", "{}", "--reason", "because", "--expected-revision", "four"], 1, "expectedRevision must be an integer"),
                (["set", "--settings", "{}", "--reason", "because", "--expected-global-revision", "2"], 2, "only valid with --project"),
                (["history", "--reason", "because"], 2, "does not accept"),
                (["get", "--limit", "2"], 2, "belong to history"),
            };
            foreach (var (args, exit, diagnostic) in cases)
            {
                var server = new PolicyApi();
                var run = Run(server, args);
                run.Exit.ShouldBe(exit, string.Join(" ", args));
                run.All.ShouldContain(diagnostic, Case.Insensitive);
                run.All.ShouldNotContain(Token);
                server.Http.Calls.ShouldBeEmpty(string.Join(" ", args));
            }
        }
        finally
        {
            File.Delete(settings);
            File.Delete(reason);
        }
    }

    private static BoardCommandRun.Result Run(PolicyApi server, params string[] args) =>
        BoardCommandRun.Run(host => DispatchConcurrencyCli.Run(DispatchConcurrencyCliOptions.Parse(args, BoardCommandRun.Api, Token), host,
            new System.IO.Abstractions.FileSystem(), server.Http.Transport));

    // The script driver's loopback recorder, as a transport: a project scope answers revision 4 and global revision 9, the
    // global scope revision 4, the history an empty page; with ConflictOnce the first PUT is a 409.
    private sealed class PolicyApi
    {
        private int _puts;

        public PolicyApi() => Http = new RecordingOperatorHttp(Answer);

        public bool ConflictOnce { get; init; }

        public RecordingOperatorHttp Http { get; }

        private NightlyReportHttpResponse Answer(RecordingOperatorHttp.Call call)
        {
            if (call.Method == "PUT" && ConflictOnce && Interlocked.Increment(ref _puts) == 1)
                return new(409, """{"code":"dispatch_concurrency_revision_conflict","status":409}""");
            var path = call.Path;
            return new(200, path.Contains("/projects/", StringComparison.Ordinal)
                ? """{"revision":4,"globalRevision":9,"scope":"project"}"""
                : path.Contains("/revisions", StringComparison.Ordinal)
                    ? """{"revisions":[],"nextBeforeRevision":null}"""
                    : """{"revision":4,"scope":"global"}""");
        }
    }
}
