using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.CardListSearchScriptTests;

namespace Antiphon.Tests.Scripts;

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

        var text = await RunAsync(stub, repo.Path, "-ProjectId", project);
        text.ExitCode.ShouldBe(0, text.Stderr);
        text.Stdout.ShouldContain("12345678");
        text.Stdout.ShouldContain("Snapshot fixture");
        text.Stdout.ShouldContain("queued=2 blocked=1");
        text.Stdout.ShouldContain(master);
        var json = await RunAsync(stub, repo.Path, "-Json", "-RcRunLogDir", logs, "-RcStateDir", state,
            "-RcHeartbeatPath", heartbeat);
        json.ExitCode.ShouldBe(0, json.Stderr);
        using var parsed = JsonDocument.Parse(json.Stdout);
        var root = parsed.RootElement;
        root.GetProperty("projectId").GetString().ShouldBe(project);
        root.GetProperty("master").GetProperty("tip").GetString().ShouldBe(master);
        root.GetProperty("counts").GetProperty("inFlight").GetInt32().ShouldBe(1);
        root.GetProperty("counts").GetProperty("queued").GetInt32().ShouldBe(2);
        root.GetProperty("counts").GetProperty("blocked").GetInt32().ShouldBe(1);
        root.GetProperty("pipeline").GetProperty("stages")[0].GetProperty("inFlight")[0]
            .GetProperty("title").GetString().ShouldBe("Snapshot fixture");
        root.GetProperty("taskDetails")[0].GetProperty("nextStage").GetString().ShouldBe("review");
        root.GetProperty("rc").GetProperty("trxCount").GetInt32().ShouldBe(1);
        root.GetProperty("rc").GetProperty("scheduledEntries").GetInt32().ShouldBe(2);
        root.GetProperty("rc").GetProperty("lastRun").GetProperty("runId").GetString().ShouldBe("run-fixture");
        root.GetProperty("rc").GetProperty("heartbeatAgeSeconds").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
        root.GetProperty("errors").GetArrayLength().ShouldBe(0);
        stub.Calls.ShouldAllBe(c => c.Method == "GET" && c.TaskToken == "synthetic-task-token");
        Files(repo.Path, repo.WorktreeRoot).ShouldBe(before, "snapshot must not create or change any file");
    }

    private static string[] Files(params string[] roots) => roots.SelectMany(root =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)).Order()
        .Select(path => path + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))
        .ToArray();

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(Stub stub, string repository, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File",
                     Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "status-snapshot.ps1"),
                     "-Repository", repository, "-Runner", "fixture-runner" }.Concat(args))
            start.ArgumentList.Add(arg);
        start.Environment["ANTIPHON_API"] = stub.BaseUrl;
        start.Environment["ANTIPHON_TASK_TOKEN"] = "synthetic-task-token";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        return (process.ExitCode, await stdout, await stderr);
    }
}
