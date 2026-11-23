using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0459: the worktree-residue command (Antiphon.Nightly.WorktreeResidue, which replaced scripts/worktree-residue.ps1 and its
/// driver scripts/test-worktree-residue.ps1 in unit 21 of the 2026-11-22 plan) in process, on a recording transport. A release
/// names a full task GUID: a short id is refused before any request, and in a batch it fails that item without a request.
/// </summary>
[Category("Integration")]
public sealed class WorktreeResidueScriptTests
{
    private const string TaskId = "12345678-1111-2222-3333-444444444444";

    [Test]
    public async Task C459_BatchRequiresFullIds()
    {
        // The driver's rows: the source is ASCII; a short id is refused; a batch item with a short id fails the batch.
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "WorktreeResidue.cs")).ShouldAllBe(b => b < 128);

        var refused = Should.Throw<NightlyUsageException>(() => WorktreeResidueOptions.Parse(
            ["--action", "release", "--task-id", "aaaaaaaa", "--expected-task-revision", "x", "--source-sha", new string('a', 40), "--reason", "x"]));
        refused.Message.ShouldBe("release requires a full task GUID, not a short id");

        using var work = new TempDirectory("c459-residue-");
        var manifest = Path.Combine(work.Path, "short.json");
        await File.WriteAllTextAsync(manifest, """[{"taskId":"aaaaaaaa","expectedTaskRevision":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","sourceSha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","reason":"x"}]""");
        var http = new RecordingOperatorHttp(_ => throw new InvalidOperationException("a short id must never reach the API"));
        var batch = Run(http, "--action", "batch-release", "--base-url", "http://127.0.0.1:1", "--manifest-path", manifest);
        batch.Exit.ShouldNotBe(0, batch.Stdout + batch.Stderr);
        batch.Stdout.Trim().ShouldBe("invalid-id:aaaaaaaa");
        http.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task C459_Requests_carry_the_server_shapes_and_the_token_stays_in_headers()
    {
        const string token = "c459-synthetic-task-token";
        var http = new RecordingOperatorHttp(call => call.Method == "DELETE" ? new(204, "") : new(200, """{"ok":true}"""));

        var preview = Run(http, "--action", "preview", "--base-url", "http://127.0.0.1:17202/", "--project-id", "p-1", "--token", token);
        preview.Exit.ShouldBe(0, preview.Stderr);
        preview.Stdout.Trim().ShouldBe("""{"ok":true}""");
        http.Calls[^1].Method.ShouldBe("POST");
        http.Calls[^1].Uri.ShouldBe("http://127.0.0.1:17202/api/agent-tasks/worktree-residue/preview");
        JsonDocument.Parse(http.Calls[^1].Body!).RootElement.GetRawText().ShouldBe("""{"projectId":"p-1"}""");

        Run(http, "--action", "status", "--run-id", "run-7", "--page", "2").Exit.ShouldBe(0);
        http.Calls[^1].Path.ShouldBe("/api/agent-tasks/worktree-residue/runs/run-7?page=2");
        http.Calls[^1].Body.ShouldBeNull();

        Run(http, "--action", "release", "--task-id", TaskId, "--expected-task-revision", "rev-3", "--source-sha", new string('b', 40), "--reason", "settled",
            "--token", token).Exit.ShouldBe(0);
        http.Calls[^1].Path.ShouldBe("/api/agent-tasks/" + TaskId + "/worktree-retirement");
        using (var body = JsonDocument.Parse(http.Calls[^1].Body!))
        {
            body.RootElement.GetProperty("expectedTaskRevision").GetString().ShouldBe("rev-3");
            body.RootElement.GetProperty("sourceSha").GetString().ShouldBe(new string('b', 40));
            body.RootElement.GetProperty("reportDigest").GetString().ShouldBe("");
            body.RootElement.GetProperty("noFurtherWorkspaceUse").GetBoolean().ShouldBeTrue();
            body.RootElement.GetProperty("reason").GetString().ShouldBe("settled");
        }

        var revoke = Run(http, "--action", "revoke", "--task-id", TaskId, "--retirement-id", "ret-9");
        revoke.Exit.ShouldBe(0, revoke.Stderr);
        revoke.Stdout.ShouldBeEmpty();
        http.Calls[^1].Method.ShouldBe("DELETE");
        http.Calls[^1].Path.ShouldBe("/api/agent-tasks/" + TaskId + "/worktree-retirement/ret-9");

        http.Calls.Where(call => call.Headers.ContainsKey("X-Antiphon-Task-Token")).Select(call => call.Headers["X-Antiphon-Task-Token"]).Distinct().ShouldBe([token]);
        http.Calls.Count(call => call.Headers.ContainsKey("X-Antiphon-Task-Token")).ShouldBe(2, "only the calls given --token send it");
    }

    [Test]
    public async Task C459_Failures_exit_1_and_a_batch_reports_each_failed_item()
    {
        var http = new RecordingOperatorHttp(call => call.Uri.Contains("bbbbbbbb", StringComparison.Ordinal)
            ? new(409, """{"error":"conflict"}""") : new(200, "{}"));
        var failed = Run(http, "--action", "release", "--task-id", "bbbbbbbb-1111-2222-3333-444444444444", "--expected-task-revision", "r", "--source-sha", "s",
            "--reason", "x");
        failed.Exit.ShouldBe(1);
        failed.Stdout.ShouldBeEmpty();
        failed.Stderr.Trim().ShouldBe("""worktree-residue: HTTP 409 {"error":"conflict"}""");

        using var work = new TempDirectory("c459-batch-");
        var manifest = Path.Combine(work.Path, "batch.json");
        await File.WriteAllTextAsync(manifest, $$"""
            [{"taskId":"{{TaskId}}","expectedTaskRevision":"r1","sourceSha":"s1","reportDigest":"d1","reason":"one"},
             {"taskId":"bbbbbbbb-1111-2222-3333-444444444444","expectedTaskRevision":"r2","sourceSha":"s2","reason":"two"},
             {"taskId":"short"}]
            """);
        http.Calls.Clear();
        var batch = Run(http, "--action", "batch-release", "--manifest-path", manifest);
        batch.Exit.ShouldBe(1);
        batch.Stdout.Trim().ShouldBe("bbbbbbbb-1111-2222-3333-444444444444:HTTP 409; invalid-id:short");
        http.Calls.Count.ShouldBe(2);
        using var first = JsonDocument.Parse(http.Calls[0].Body!);
        first.RootElement.GetProperty("reportDigest").GetString().ShouldBe("d1");
        using var second = JsonDocument.Parse(http.Calls[1].Body!);
        second.RootElement.GetProperty("reportDigest").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static (int Exit, string Stdout, string Stderr) Run(RecordingOperatorHttp http, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = WorktreeResidue.Run(WorktreeResidueOptions.Parse(args), NightlyHost.Production(stdout, stderr), new System.IO.Abstractions.FileSystem(), http.Transport);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix) => Path = Directory.CreateTempSubdirectory(prefix).FullName;

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
