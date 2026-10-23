using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Shouldly;

namespace Antiphon.Tests.Scripts;

// Both witnesses retain the production HTTP reader. Only transport and time are fake.
internal static class C1374CensusProbes
{
    private const string Project = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
    private const string Other = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private const string OtherId = "22222222-2222-2222-2222-222222222222";
    private const string DetailPath = "/api/agent-tasks/" + Id;
    private static string ListPath(string project, string kind) => RollingVolumeRecycleScriptTests.C1087ListPath(project, kind);
    private sealed record Vector(string Name, string Path, string[] Faults, int FailedAttempts = 1,
        string Error = "", string Shape = "");
    private static JsonObject Row(string id = Id, string project = Project) => new()
    {
        ["id"] = id, ["status"] = "Blocked", ["runnerId"] = "other", ["projectId"] = project,
        ["scopeSource"] = "Task", ["landRequestedAt"] = null, ["landStartedAt"] = null,
        ["title"] = "SENTINEL_TASK_BODY"
    };
    private static JsonObject Envelope(string project, params JsonObject[] rows) => new()
    {
        ["items"] = new JsonArray(rows.Select(r => r.DeepClone()).ToArray()),
        ["scope"] = new JsonObject { ["projectId"] = project, ["unscoped"] = "include" },
        ["excluded"] = new JsonObject { ["total"] = 0, ["unscoped"] = 0, ["byProject"] = new JsonArray() }
    };
    private static JsonObject Detail(JsonObject row) => new()
        { ["summary"] = row.DeepClone(), ["landRequest"] = null, ["goal"] = "SENTINEL_DETAIL" };

    internal static async Task Check(bool host)
    {
        // Run first: before the fix this fails expected success after the first 502,
        // independently on each side, before any attempt-number assertions.
        var vectors = new List<Vector> { new("primary-502-detail", DetailPath, ["502", "ok"]) };
        var retryable = host ? new[] { "503", "504", "timeout", "send", "receive" } :
            new[] { "503", "504", "timeout", "typed-timeout", "reset", "ConnectionClosed", "ReceiveFailure", "SendFailure" };
        foreach (var fault in retryable) vectors.Add(new("recover-" + fault, DetailPath, [fault, "ok"]));
        foreach (var project in new[] { Project, Other })
        foreach (var kind in new[] { "open", "land" })
            vectors.Add(new("list-" + project + "-" + kind, ListPath(project, kind), ["502", "ok"]));
        vectors.Add(new("third-attempt", DetailPath, ["502", "timeout", "ok"], 2));
        foreach (var fault in new[] { "502" }.Concat(retryable))
            vectors.Add(new("exhaust-" + fault, DetailPath, [fault], 3, "RecycleTaskCensusUnknown cause=Http"));
        foreach (var fault in new[] { "400", "401", "404", "408", "429", "500", "transport", "empty", "malformed" }
                     .Concat(host ? ["tls"] : new[] { "NameResolutionFailure", "TrustFailure" }))
        {
            var cause = fault == "empty" ? "Empty" : fault == "malformed" ? "Malformed" :
                int.TryParse(fault, out _) ? "Http" : "Transport";
            vectors.Add(new("immediate-" + fault, DetailPath, [fault, "ok"], 1, "RecycleTaskCensusUnknown cause=" + cause));
        }
        vectors.Add(new("502-then-malformed", DetailPath, ["502", "malformed", "ok"], 2,
            "RecycleTaskCensusUnknown cause=Malformed"));
        vectors.Add(new("malformed-envelope", ListPath(Project, "open"), ["ok"], 0,
            "RecycleTaskCensusUnknown cause=Malformed", "envelope"));
        vectors.Add(new("malformed-detail", DetailPath, ["ok"], 0, "RecycleLandUnknown", "detail"));
        vectors.Add(new("recovered-bound-work", ListPath(Project, "open"), ["502", "ok"], 1, "RecycleBoundTasks", "bound"));
        vectors.Add(new("recovered-invalid-land", DetailPath, ["502", "ok"], 1, "RecycleLandUnknown", "land"));
        foreach (var vector in vectors) await RunVector(host, vector);
        if (!host)
        {
            // Exercise the actual wrapper phase boundary as well as the direct census.
            foreach (var fault in new[] { "502", "400", "malformed" })
            {
                using var f = new C1008WrapperFixture();
                f.State["readFaults"] = new JsonObject { [ListPath(Project, "open")] = new JsonArray(fault) };
                var result = await f.Run("retire-temp");
                result.Exit.ShouldBe(2, result.Output);
                result.Trace.Any(x => x["kind"]?.GetValue<string>() == "case" || x["method"]?.GetValue<string>() == "POST")
                    .ShouldBeFalse("terminal read failure cannot enter a host phase or change drain state");
            }
        }
        Console.WriteLine($"C1374 {(host ? "host" : "wrapper")}: {vectors.Count} transport/census vectors passed");
    }

    private static async Task RunVector(bool host, Vector v)
    {
        using var wrapper = new C1008WrapperFixture();
        var row = Row();
        if (v.Shape == "bound") row["runnerId"] = "server2";
        var open = Envelope(Project, row); var land = Envelope(Project);
        var otherRow = Row(OtherId, Other); var otherLand = otherRow.DeepClone().AsObject();
        otherLand["status"] = "Succeeded"; otherLand["landRequestedAt"] = "2020-01-01T00:00:00Z";
        var scopes = new JsonObject
        {
            [Project] = new JsonObject { ["open"] = open, ["land"] = land },
            [Other] = new JsonObject { ["open"] = Envelope(Other, otherRow), ["land"] = Envelope(Other, otherLand) }
        };
        var expectedCounts = new Dictionary<string, int>
            { [ListPath(Project, "open")] = 2, [ListPath(Project, "land")] = 2, [DetailPath] = 2 };
        if (v.Path.Contains(Other, StringComparison.Ordinal))
        {
            var kind = v.Path.EndsWith("landPending=true", StringComparison.Ordinal) ? "land" : "open";
            scopes[Project]![kind]!["excluded"] = new JsonObject { ["total"] = 1, ["unscoped"] = 0,
                ["byProject"] = new JsonArray(new JsonObject { ["projectId"] = Other, ["count"] = 1 }) };
            expectedCounts[v.Path] = 2;
            if (kind == "open") expectedCounts["/api/agent-tasks/" + OtherId] = 2;
        }
        var details = new JsonObject { [Id] = Detail(row), [OtherId] = Detail(otherRow) };
        if (v.Shape == "envelope") open["items"] = "SENTINEL_SHAPE";
        if (v.Shape == "detail") details[Id]!.AsObject().Remove("summary");
        if (v.Shape == "land") details[Id]!["landRequest"] = new JsonObject { ["state"] = "SENTINEL_UNKNOWN" };
        var faults = new JsonObject { [v.Path] = new JsonArray(v.Faults.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()) };
        string root; string output; JsonNode result; string[] paths; JsonNode receipt; string[] sleeps;
        using var hostFixture = host ? new C1008HostFixture { ExerciseHttpClassifier = true } : null;
        if (hostFixture is not null)
        {
            root = hostFixture.Root;
            hostFixture.TaskScopes.Clear();
            foreach (var p in scopes) hostFixture.TaskScopes[p.Key] = p.Value!.DeepClone();
            foreach (var p in details) hostFixture.TaskDetails[p.Key] = p.Value!.DeepClone();
            hostFixture.HttpFaults[v.Path] = faults[v.Path]!.DeepClone();
            var run = await hostFixture.Run(extra: """
                C1008_RUNNER=server2
                sleep() { printf '%s\n' "$1" >> "$C1008_FIXTURE_ROOT/sleeps"; }
                code=0; c1008_tasks_wait > "$C1008_FIXTURE_ROOT/snapshot.json" || code=$?
                jq -n --argjson code "$code" --arg error "$C1008_TASK_ERROR" --arg body "$C1008_HTTP_BODY" \
                    '{code:$code,error:$error,body:$body}' > "$C1008_FIXTURE_ROOT/result.json"
                write_result true '' 0
                """);
            run.Exit.ShouldBe(0, v.Name + " fixture: " + run.Output);
            output = run.Output;
            result = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "result.json")))!;
            paths = File.ReadAllLines(Path.Combine(root, "http-trace"));
            receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "evidence/deploy-parent/census-deploy-parent-server2.json")))!;
            sleeps = File.Exists(Path.Combine(root, "sleeps")) ? File.ReadAllLines(Path.Combine(root, "sleeps")) : [];
            File.ReadAllLines(Path.Combine(root, "http-budgets")).ShouldAllBe(b => b == "60", v.Name);
            hostFixture.Removed.ShouldBeEmpty(v.Name);
            hostFixture.Trace.ShouldBeEmpty(v.Name + " direct census must not reach Docker");
            if (v.Error.Length != 0 && v.Shape.Length == 0) result["body"]!.GetValue<string>().ShouldBe("", v.Name + " no failed body retained");
        }
        else
        {
            root = wrapper.Root;
            var queries = new JsonObject();
            foreach (var scope in scopes)
            foreach (var kind in new[] { "open", "land" }) queries[ListPath(scope.Key, kind)] = scope.Value![kind]!.DeepClone();
            wrapper.State["taskQueries"] = queries; wrapper.State["details"] = details; wrapper.State["readFaults"] = faults;
            File.WriteAllText(wrapper.StatePath, wrapper.State.ToJsonString());
            var run = await WrapperProbe(wrapper);
            run.Exit.ShouldBe(0, v.Name + " fixture: " + run.Output);
            output = run.Output;
            result = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "result.json")))!;
            paths = File.ReadAllLines(wrapper.TracePath).Select(x => JsonNode.Parse(x)!["path"]!.GetValue<string>()).ToArray();
            receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "census-probe-server2.json")))!;
            sleeps = result["sleeps"]!.AsArray().Select(x => x!.ToString()).ToArray();
        }
        var error = result["error"]!.GetValue<string>();
        if (v.Error.Length == 0)
        {
            result["code"]!.GetValue<int>().ShouldBe(0, v.Name + " expected recovery success: " + error + output);
            error.ShouldBe("", v.Name + " stale failure cleared");
            expectedCounts[v.Path] += v.FailedAttempts;
            // Compare the exact path/count map, independently of dictionary insertion order.
            paths.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()).OrderBy(p => p.Key, StringComparer.Ordinal)
                .ShouldBe(expectedCounts.OrderBy(p => p.Key, StringComparer.Ordinal), v.Name + " exactly two passes, retry only failed GET");
            receipt["snapshot"]!["open"]!["tasks"]![Id]!["status"]!.GetValue<string>().ShouldBe("Blocked", v.Name);
            receipt["snapshot"]!["open"]!["land"]!.AsObject().ContainsKey(Id).ShouldBeTrue(v.Name);
            receipt["snapshot"]!["open"]!["land"]![Id].ShouldBeNull(v.Name);
        }
        else
        {
            result["code"]!.GetValue<int>().ShouldBe(host && v.Shape == "bound" ? 4 : 2, v.Name + ": " + error);
            error.ShouldStartWith(v.Error, customMessage: v.Name);
            var attempts = v.Shape.Length == 0 ? v.FailedAttempts : v.FailedAttempts + 1;
            paths.Count(x => x == v.Path).ShouldBe(attempts, v.Name + " bounded physical requests");
            paths.Where(x => x != v.Path).GroupBy(x => x).ShouldAllBe(g => g.Count() == 1, v.Name + " no outer retry");
            if (v.Name.StartsWith("exhaust-", StringComparison.Ordinal))
            {
                error.ShouldContain("attempts=3", customMessage: v.Name);
                if (int.TryParse(v.Faults[0], out var status)) error.ShouldContain("status=" + status);
                else { error.ShouldContain("failure=" + Outcome(v.Faults[0])); error.ShouldNotContain("status="); }
            }
        }
        var expectedSleeps = v.Name.StartsWith("exhaust-", StringComparison.Ordinal) ? 2 :
            v.Error.Length == 0 || v.Shape.Length > 0 ? v.FailedAttempts : v.Name == "502-then-malformed" ? 1 : 0;
        sleeps.ShouldBe(Enumerable.Repeat("1", expectedSleeps), v.Name + " one second between retryable failures only");
        var reads = receipt["reads"]!.AsArray().Where(r => r!["path"]!.GetValue<string>() == v.Path).ToArray();
        reads.Length.ShouldBe(paths.Count(x => x == v.Path), v.Name + " one receipt per physical attempt");
        if (v.Shape.Length == 0)
        {
            for (var i = 0; i < v.FailedAttempts; i++)
            {
                var fault = v.Faults[Math.Min(i, v.Faults.Length - 1)];
                reads[i]!["outcome"]!.GetValue<string>().ShouldBe(Outcome(fault), v.Name);
                reads[i]!["attempt"]!.GetValue<int>().ShouldBe(i + 1, v.Name);
                if (int.TryParse(fault, out var status)) reads[i]!["status"]!.GetValue<int>().ShouldBe(status, v.Name);
            }
        }
        if (v.Error.Length == 0)
        {
            reads.Skip(v.FailedAttempts).Select(r => r!["outcome"]!.GetValue<string>()).ShouldBe(new[] { "ok", "ok" }, v.Name);
            reads.Skip(v.FailedAttempts).Select(r => r!["attempt"]!.GetValue<int>()).ShouldBe(new[] { v.FailedAttempts + 1, 1 }, v.Name);
        }
        foreach (var read in receipt["reads"]!.AsArray()) read!["elapsedMs"]!.GetValue<long>().ShouldBeGreaterThanOrEqualTo(0);
        (receipt.ToJsonString() + error + output).ShouldNotContain("SENTINEL", customMessage: v.Name);
        (receipt.ToJsonString() + error + output).ShouldNotContain("http://127.0.0.1:1", customMessage: v.Name);
        Console.WriteLine($"CASE C1374/{(host ? "host" : "wrapper")}/{v.Name} PASS requests={paths.Length} sleeps={sleeps.Length}");
    }

    private static string Outcome(string fault) => fault is "timeout" or "typed-timeout" ? "Timeout" :
        fault == "empty" ? "Empty" : fault == "malformed" ? "Malformed" : int.TryParse(fault, out _) ? "Http" : "Transport";

    private static async Task<(int Exit, string Output)> WrapperProbe(C1008WrapperFixture fixture)
    {
        var script = Path.Combine(fixture.Root, "probe.ps1");
        File.WriteAllText(script, $$"""
            $ErrorActionPreference='Stop'
            $root='{{fixture.Root}}'; $evidenceRoot=$root
            $ast=[Management.Automation.Language.Parser]::ParseFile('{{DelegateScriptRunner.RepoRoot}}/scripts/deploy-server2.ps1',[ref]$null,[ref]$null)
            foreach ($f in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)) { Invoke-Expression $f.Extent.Text }
            $script:sleeps=[Collections.Generic.List[int]]::new()
            function Start-Sleep { param($Seconds,$Milliseconds)
                if (-not $env:C727_TEST_HTTP_STUB -or $Milliseconds) { throw 'unexpected clock seam' }
                $script:sleeps.Add([int]$Seconds)
                # The failed physical attempt must already be durable before backoff.
                $saved=Get-Content -Raw $script:recycleReceiptPath | ConvertFrom-Json
                if ($saved.reads[-1].outcome -notin @('Http','Timeout','Transport')) { throw 'missing pre-sleep receipt' }
            }
            $LandWaitMinutes=0
            Start-RecycleReceipt -ExecutingPhase probe -RunnerId server2
            $code=0; $errorText=''
            try { $null=Wait-RecycleTaskCensus -RunnerId server2 -ProjectId '{{Project}}' } catch { $code=2; $errorText=$_.Exception.Message }
            @{code=$code;error=$errorText;sleeps=@($script:sleeps)} | ConvertTo-Json -Depth 30 | Set-Content "$root/result.json"
            """);
        var psi = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", script }) psi.ArgumentList.Add(arg);
        psi.Environment["C727_TEST_HTTP_STUB"] = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/fixtures/c727-fake-http.ps1");
        psi.Environment["C727_TEST_STATE"] = fixture.StatePath; psi.Environment["C727_TEST_TRACE"] = fixture.TracePath;
        psi.Environment["C727_TEST_POLL_MS"] = "";
        using var child = Process.Start(psi)!;
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(deadline.Token); }
        catch { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } throw; }
        return (child.ExitCode, await stdout + await stderr);
    }
}
