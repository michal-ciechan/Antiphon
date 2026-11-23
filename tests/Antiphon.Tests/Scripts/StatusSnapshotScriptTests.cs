using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.CardListSearchScriptTests;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The status snapshot (StatusSnapshot, `antiphon-scripts status-snapshot`), in-process since it replaced
/// scripts/status-snapshot.ps1 (plan 2026-11-22-remaining-ps1-to-csharp.md, unit 7): the production HTTP transport against a
/// loopback stub and the production git reader against a scratch repository with a bare origin.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class StatusSnapshotScriptTests
{
    [Test]
    public async Task Snapshot_reads_scoped_tasks_counts_remote_master_and_rc_without_writes_in_text_and_json()
    {
        const string project = "acacacac-1111-2222-3333-444444444444";
        const string task = "12345678-1111-2222-3333-444444444444";
        using var repo = new ScratchGitRepo("status-snapshot");
        await repo.CommitFileAsync("seed.txt", "master fixture");
        await repo.AddBareOriginAsync();
        var master = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("push", "origin", "HEAD:refs/heads/rc/fixture");
        var logs = Path.Combine(repo.WorktreeRoot, "run-fixture");
        var state = Path.Combine(repo.WorktreeRoot, "state");
        Directory.CreateDirectory(logs);
        Directory.CreateDirectory(state);
        await File.WriteAllTextAsync(Path.Combine(logs, "one.trx"), "<TestRun />");
        await File.WriteAllTextAsync(Path.Combine(logs, "dispatch-schedule.json"), """{"entries":[{"id":"one"},{"id":"two"}]}""");
        await File.WriteAllTextAsync(Path.Combine(logs, "active-chunk.json"), """{"chunk":"two"}""");
        await File.WriteAllTextAsync(Path.Combine(state, "last-run.json"), """{"runId":"run-fixture","phase":"running","exitCode":null}""");
        var heartbeat = Path.Combine(state, "supervisor-heartbeat.json");
        await File.WriteAllTextAsync(heartbeat, JsonSerializer.Serialize(new { observedAtUtc = DateTime.UtcNow }));
        var before = Files(repo.Path, repo.WorktreeRoot);
        using var stub = new Stub("/prefix");
        stub.Respond = (call, _) => call.Path switch
        {
            "/prefix/api/agent-tasks/pipeline" when call.Value("projectId") is null => new(200,
                $$"""{"concurrencyScopes":[{"projectId":"{{project}}"}]}"""),
            "/prefix/api/agent-tasks/pipeline" when call.Value("projectId") == project => new(200,
                $$"""{"taskScope":"project","stages":[{"role":"Code","inFlightCount":1,"inFlight":[{"taskId":"{{task}}","shortId":"12345678","title":"Snapshot fixture","status":"Working"}],"queued":[{"taskId":"{{task}}"},{"taskId":"{{task}}"}],"blocked":[{"taskId":"{{task}}"}],"ready":[]}],"hosts":[{"hostId":"fixture-runner","inFlight":1,"remaining":4}]}"""),
            "/prefix/api/session-runners/fixture-runner/slots" => new(200,
                """{"occupied":1,"declaredCapacity":5,"slots":[{"orphan":true,"occupiesCapacity":true}]}"""),
            "/prefix/api/orchestrator-instructions" => new(200, """{"content":"fixture target 7"}"""),
            "/prefix/api/agent-tasks/12345678-1111-2222-3333-444444444444" => new(200,
                """{"status":"Working","lastActivityAt":"2026-11-06T12:00:00Z","nextStage":"review","handoff":"Review snapshot"}"""),
            _ => new(404, """{"detail":"Unexpected request"}""")
        };

        var text = Run(stub, repo.Path, "--project-id", project);
        text.ExitCode.ShouldBe(0, text.Stdout);
        text.Stdout.ShouldContain("12345678");
        text.Stdout.ShouldContain("Snapshot fixture");
        text.Stdout.ShouldContain("queued=2 blocked=1");
        text.Stdout.ShouldContain(master);
        text.Stdout.ShouldContain("refs/heads/rc/fixture");
        var json = Run(stub, repo.Path, "--json", "--rc-run-log-dir", logs, "--rc-state-dir", state, "--rc-heartbeat-path", heartbeat);
        json.ExitCode.ShouldBe(0, json.Stdout);
        using var parsed = JsonDocument.Parse(json.Stdout);
        var root = parsed.RootElement;
        root.GetProperty("projectId").GetString().ShouldBe(project, "the project is discovered from the pipeline");
        root.GetProperty("master").GetProperty("tip").GetString().ShouldBe(master);
        root.GetProperty("master").GetProperty("cachedOriginMasterLog").GetString()!.ShouldStartWith(master + "\t", customMessage: "the cached history is sha<TAB>subject");
        root.GetProperty("counts").GetProperty("inFlight").GetInt32().ShouldBe(1);
        root.GetProperty("counts").GetProperty("queued").GetInt32().ShouldBe(2);
        root.GetProperty("counts").GetProperty("blocked").GetInt32().ShouldBe(1);
        root.GetProperty("pipeline").GetProperty("stages")[0].GetProperty("inFlight")[0]
            .GetProperty("title").GetString().ShouldBe("Snapshot fixture");
        root.GetProperty("taskDetails")[0].GetProperty("nextStage").GetString().ShouldBe("review");
        root.GetProperty("taskDetails").GetArrayLength().ShouldBe(1, "one GET per unique task id");
        root.GetProperty("rc").GetProperty("trxCount").GetInt32().ShouldBe(1);
        root.GetProperty("rc").GetProperty("scheduledEntries").GetInt32().ShouldBe(2);
        root.GetProperty("rc").GetProperty("activeChunk").GetProperty("chunk").GetString().ShouldBe("two");
        root.GetProperty("rc").GetProperty("lastRun").GetProperty("runId").GetString().ShouldBe("run-fixture");
        root.GetProperty("rc").GetProperty("heartbeatAgeSeconds").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
        root.GetProperty("rcRefs")[0].GetProperty("ref").GetString().ShouldBe("refs/heads/rc/fixture");
        root.GetProperty("errors").GetArrayLength().ShouldBe(0);
        stub.Calls.ShouldAllBe(c => c.Method == "GET" && c.TaskToken == "synthetic-task-token");
        Files(repo.Path, repo.WorktreeRoot).ShouldBe(before, "snapshot must not create or change any file");
    }

    [Test]
    public async Task Snapshot_reports_partial_facts_as_unverified_and_exits_2()
    {
        using var repo = new ScratchGitRepo("status-snapshot-partial");
        await repo.CommitFileAsync("seed.txt", "no origin");
        using var stub = new Stub("");
        stub.Respond = (call, _) => call.Path switch
        {
            "/api/agent-tasks/pipeline" => new(200, """{"concurrencyScopes":[{"projectId":"a"},{"projectId":"b"}]}"""),
            _ => new(503, """{"detail":"down"}""")
        };
        var logs = Path.Combine(repo.WorktreeRoot, "empty-logs");
        Directory.CreateDirectory(logs);
        var run = Run(stub, repo.Path, "--rc-run-log-dir", logs);
        run.ExitCode.ShouldBe(2, run.Stdout);
        run.Stdout.ShouldContain("TASKS counts unknown");
        run.Stdout.ShouldContain("UNVERIFIED Project cannot be resolved uniquely from pipeline; supply --project-id.");
        run.Stdout.ShouldContain("UNVERIFIED GET /api/session-runners/fixture-runner/slots unavailable (HTTP ServiceUnavailable).");
        run.Stdout.ShouldContain("UNVERIFIED Remote master/rc refs unavailable.");
        run.Stdout.ShouldContain("UNVERIFIED Remote master tip unknown.");
        run.Stdout.ShouldContain("UNVERIFIED Active chunk unknown; inspect the recorded timing root.");
        run.Stdout.ShouldNotContain("down", customMessage: "an answer body is never echoed");
        var bad = Run(stub, repo.Path, "--project-id", "not-a-guid");
        bad.Stdout.ShouldContain("UNVERIFIED ProjectId must be a GUID.");
        Should.Throw<NightlyUsageException>(() => StatusSnapshotOptions.Parse(["--runner", "bad runner"], null, null, repo.Path));
    }

    internal static (int ExitCode, string Stdout) Run(Stub stub, string repository, params string[] args) =>
        RunWith(NightlyReport.HttpTransport(timeout: StatusSnapshot.Deadline), stub.BaseUrl, repository, args);

    internal static (int ExitCode, string Stdout) RunWith(NightlyReportHttp http, string api, string repository, params string[] args)
    {
        var output = new StringWriter();
        var options = StatusSnapshotOptions.Parse(new[] { "--repository", repository, "--runner", "fixture-runner" }.Concat(args).ToArray(), api, "synthetic-task-token", null);
        var exit = StatusSnapshot.Run(options, NightlyHost.Production(output, output), new System.IO.Abstractions.FileSystem(), http, StatusSnapshot.ReadGit);
        return (exit, output.ToString());
    }

    private static string[] Files(params string[] roots) => roots.SelectMany(root =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)).Order()
        .Select(path => path + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))
        .ToArray();
}
