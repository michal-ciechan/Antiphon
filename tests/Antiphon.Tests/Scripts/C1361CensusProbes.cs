using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;

namespace Antiphon.Tests.Scripts;

// Deterministic HTTP schedules drive the real census functions. No network or Docker calls.
internal static class C1361CensusProbes
{
    private const string Project = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
    private const string OtherProject = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string ThirdProject = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private const string Event = "44444444-4444-4444-4444-444444444444";
    private static string PathFor(string project, string kind = "open") =>
        $"/api/agent-tasks?projectId={project}&unscoped=include&includeChecks=true&" +
        (kind == "open" ? "status=Queued,Dispatched,Working,Blocked" : "landPending=true");
    private static JsonObject Row(string status = "Queued") => new()
    {
        ["id"] = Id, ["status"] = status, ["runnerId"] = "desktop", ["projectId"] = null,
        ["scopeSource"] = "None", ["landRequestedAt"] = null, ["landStartedAt"] = null
    };
    private static JsonObject Proof(string state = "Completed", string? terminal = Event) =>
        new() { ["state"] = state, ["terminalEventId"] = terminal };
    private static JsonObject Detail(JsonObject row, JsonNode? proof = null) =>
        new() { ["summary"] = row.DeepClone(), ["landRequest"] = proof?.DeepClone() };
    private static JsonObject Envelope(string project, params JsonObject[] rows) => new()
    {
        ["items"] = new JsonArray(rows.Select(x => x.DeepClone()).ToArray()),
        ["scope"] = new JsonObject { ["projectId"] = project, ["unscoped"] = "include" },
        ["excluded"] = new JsonObject { ["total"] = 0, ["unscoped"] = 0, ["byProject"] = new JsonArray() }
    };
    private static JsonObject Exclude(JsonObject envelope, params (string Project, int Count)[] entries)
    {
        envelope["excluded"]!["total"] = entries.Sum(x => x.Count);
        envelope["excluded"]!["byProject"] = new JsonArray(entries.Select(x => (JsonNode)new JsonObject
            { ["projectId"] = x.Project, ["count"] = x.Count }).ToArray());
        return envelope;
    }
    private sealed record Vector(string Name, JsonObject Requests, string Error = "", JsonObject? Expected = null,
        JsonNode? Proof = null, int Sleeps = 0, bool Report = false);

    private static Vector StatusVector(string name, bool passes, JsonObject before, JsonObject after,
        JsonNode? proof = null, string error = "", JsonObject? detailOverride = null, JsonNode? laterProof = null,
        bool changeProof = false, bool report = false)
    {
        var firstDetail = passes ? Detail(before, proof) : detailOverride ?? Detail(after, proof);
        var secondDetail = passes ? detailOverride ?? Detail(after, changeProof ? laterProof : proof) : firstDetail.DeepClone();
        var current = passes ? after : before;
        var requests = new JsonObject
        {
            [PathFor(Project)] = new JsonArray(Envelope(Project, before), Envelope(Project, current)),
            [PathFor(Project, "land")] = new JsonArray(Envelope(Project)),
            ["/api/agent-tasks/" + Id] = new JsonArray(firstDetail, secondDetail)
        };
        // A changed task ID has its own detail route; it must still fail the pass comparison.
        if (passes && after["id"]!.GetValue<string>() != Id)
            requests["/api/agent-tasks/" + after["id"]!.GetValue<string>()] = new JsonArray(Detail(after, proof));
        return new Vector(name, requests, error, current, changeProof ? laterProof : proof, Report: report);
    }

    internal static async Task Status(bool host, bool passes)
    {
        var unsafeError = passes ? "RecycleTaskCensusUnknown cause=Unstable" : "RecycleLandUnknown";
        // First independent red witness: no helper-extraction or fixture failure can masquerade as red.
        var primary = StatusVector("primary", passes, Row(), Row("Dispatched"));
        await Check(host, primary);
        var vectors = new List<Vector>();
        foreach (var from in new[] { "Queued", "Dispatched", "Working", "Blocked" })
        foreach (var to in new[] { "Queued", "Dispatched", "Working", "Blocked" })
            vectors.Add(StatusVector($"open-pair/{from}/{to}", passes, Row(from), Row(to)));
        foreach (var state in new[] { "Completed", "Superseded", "Canceled" })
            vectors.Add(StatusVector("terminal/" + state, passes, Row(), Row("Dispatched"), Proof(state)));
        foreach (var key in new[] { "landRequestedAt", "landStartedAt" })
        {
            var a = Row(); var b = Row("Dispatched");
            a[key] = "2020-01-01T00:00:00Z"; b[key] = "2020-01-01T00:00:00Z";
            vectors.Add(StatusVector("stable-stamp/" + key, passes, a, b));
        }
        foreach (var key in new[] { "id", "runnerId", "projectId", "scopeSource", "landRequestedAt", "landStartedAt" })
        {
            var a = Row(); var b = Row("Dispatched");
            b[key] = key switch { "id" => Event, "runnerId" => "other", "projectId" => Project,
                "scopeSource" => "Card", _ => "2020-01-01T00:00:00Z" };
            if (key == "projectId") b["scopeSource"] = "Task";
            if (key == "scopeSource") { a["scopeSource"] = "Task"; a["projectId"] = Project; b["projectId"] = Project; }
            vectors.Add(StatusVector("field/" + key, passes, a, b, error: unsafeError));
        }
        foreach (var key in new[] { "id", "status", "runnerId", "projectId", "scopeSource", "landRequestedAt", "landStartedAt" })
        {
            var d = Detail(Row("Dispatched")); d["summary"]!.AsObject().Remove(key);
            vectors.Add(StatusVector("missing-summary/" + key, passes, Row(), Row("Dispatched"),
                error: "RecycleLandUnknown", detailOverride: d));
        }
        foreach (var state in new[] { "Queued", "Held", "Running", "NeedsResolution" })
            vectors.Add(StatusVector("pending/" + state, passes, Row(), Row("Dispatched"), Proof(state, null), unsafeError));
        foreach (var variant in new[] { "unknown", "missing", "missing-event", "null-event", "invalid-event" })
        {
            var d = Detail(Row("Dispatched"), Proof());
            if (variant == "unknown") d["landRequest"]!["state"] = "unknown";
            if (variant == "missing") d.Remove("landRequest");
            if (variant == "missing-event") d["landRequest"]!.AsObject().Remove("terminalEventId");
            if (variant == "null-event") d["landRequest"]!["terminalEventId"] = null;
            if (variant == "invalid-event") d["landRequest"]!["terminalEventId"] = "bad";
            vectors.Add(StatusVector("unsafe-proof/" + variant, passes, Row(), Row("Dispatched"),
                error: "RecycleLandUnknown", detailOverride: d));
        }
        var bound = Row(); bound["runnerId"] = "server2";
        var boundAfter = bound.DeepClone().AsObject(); boundAfter["status"] = "Dispatched";
        vectors.Add(StatusVector("bound/ordinary", passes, bound, boundAfter, error: "RecycleBoundTasks"));
        if (!host)
        {
            vectors.Add(StatusVector("bound/report-drift", passes, bound, boundAfter, error: unsafeError, report: true));
            vectors.Add(StatusVector("bound/report-stable", passes, bound, bound, report: true));
        }
        if (passes)
        {
            vectors.Add(StatusVector("proof-change/event", true, Row(), Row("Dispatched"), Proof(), unsafeError,
                laterProof: Proof(terminal: "55555555-5555-5555-5555-555555555555"), changeProof: true));
            vectors.Add(StatusVector("proof-change/null-terminal", true, Row(), Row("Dispatched"), error: unsafeError,
                laterProof: Proof(), changeProof: true));
            vectors.Add(StatusVector("proof-change/terminal-null", true, Row(), Row("Dispatched"), Proof(), unsafeError,
                changeProof: true));
            foreach (var variant in new[] { "settled", "appeared", "runner", "scope", "land" })
            {
                var v = StatusVector("companion/" + variant, true, Row(), Row("Dispatched"),
                    error: variant is "settled" or "appeared" ? "" : unsafeError, report: variant == "land");
                var other = Row(); other["id"] = Event;
                var lists = v.Requests[PathFor(Project)]!.AsArray();
                if (variant != "appeared") lists[0]!["items"]!.AsArray().Add(other.DeepClone());
                if (variant != "settled") lists[1]!["items"]!.AsArray().Add(other.DeepClone());
                if (variant == "runner") lists[1]!["items"]![1]!["runnerId"] = "other";
                v.Requests["/api/agent-tasks/" + Event] = new JsonArray(
                    Detail(variant == "settled" ? new JsonObject(other.Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))) : other),
                    Detail(variant == "runner" ? lists[1]!["items"]![1]!.AsObject() : other));
                if (variant == "settled") v.Requests["/api/agent-tasks/" + Event]![0]!["summary"]!["status"] = "Succeeded";
                if (variant == "scope")
                {
                    Exclude(lists[1]!.AsObject(), (OtherProject, 1));
                    var scoped = Row(); scoped["id"] = "66666666-6666-6666-6666-666666666666";
                    scoped["projectId"] = OtherProject; scoped["scopeSource"] = "Task";
                    v.Requests[PathFor(OtherProject)] = new JsonArray(Envelope(OtherProject, scoped));
                    v.Requests["/api/agent-tasks/" + scoped["id"]] = new JsonArray(Detail(scoped));
                }
                if (variant == "land")
                {
                    var landRow = Row("Succeeded"); landRow["id"] = Event; landRow["runnerId"] = "server2";
                    landRow["landRequestedAt"] = "2020-01-01T00:00:00Z";
                    v.Requests[PathFor(Project, "land")] = new JsonArray(Envelope(Project), Envelope(Project, landRow));
                    if (host) v = v with { Error = "RecycleLandInFlight" };
                }
                vectors.Add(v);
            }
        }
        foreach (var vector in vectors) await Check(host, vector);
        await Boundaries(host, passes);
        Console.WriteLine($"C1361 {(host ? "host" : "wrapper")}/{(passes ? "passes" : "detail")}: {vectors.Count + 1} census cases plus boundary cases");
    }

    internal static async Task Counts()
    {
        var failures = new List<string>();
        // Both languages run even when the pre-fix baseline fails; each independently diagnoses the defect.
        foreach (var host in new[] { false, true })
        {
            try
            {
                foreach (var kind in new[] { "open", "land" })
                foreach (var site in new[] { "collected", "advertised" })
                foreach (var delta in new[] { -1, 1 })
                foreach (var recover in new[] { true, false })
                {
                    var requests = new JsonObject { [PathFor(Project, "land")] = new JsonArray(Envelope(Project)),
                        [PathFor(Project)] = new JsonArray(Envelope(Project)) };
                    JsonObject Scoped(string project, int number)
                    {
                        var rows = Enumerable.Range(1, number).Select(i =>
                        {
                            var row = Row(kind == "open" ? "Queued" : "Succeeded");
                            row["id"] = project[..8] + "-1111-1111-1111-" + i.ToString("D12");
                            row["projectId"] = project; row["scopeSource"] = "Task";
                            if (kind == "land") row["landRequestedAt"] = "2020-01-01T00:00:00Z";
                            requests["/api/agent-tasks/" + row["id"]] = new JsonArray(Detail(row));
                            return row;
                        }).ToArray();
                        return Envelope(project, rows);
                    }
                    var a = Scoped(Project, 6); var b = Scoped(OtherProject, 1); var c = Scoped(ThirdProject, 2);
                    Exclude(a, site == "collected" ? [(OtherProject, 1)] : [(OtherProject, 1), (ThirdProject, 2)]);
                    Exclude(b, site == "collected" ? [(Project, 6 + delta)] : [(ThirdProject, 2 + delta)]);
                    var stable = b.DeepClone().AsObject(); Exclude(stable, site == "collected" ? [(Project, 6)] : [(ThirdProject, 2)]);
                    requests[PathFor(Project, kind)] = new JsonArray(a);
                    requests[PathFor(OtherProject, kind)] = recover ? new JsonArray(b, stable) : new JsonArray(b);
                    requests[PathFor(ThirdProject, kind)] = new JsonArray(c);
                    var expected = site == "collected" ? 6 + delta : 2;
                    var observed = site == "collected" ? 6 : 2 + delta;
                    var error = recover ? "" : $"RecycleTaskCensusUnknown cause=Drift field=excluded.byProject.count project={(site == "collected" ? Project : ThirdProject)} expected={expected} observed={observed}";
                    await Check(host, new Vector($"{site}/{kind}/{delta}/{recover}", requests, error, Sleeps: recover ? 1 : 2));
                }
                foreach (var type in new[] { "count", "total", "project" })
                foreach (var variant in type == "project" ? new[] { "self", "duplicate", "invalid" } :
                    new[] { "zero", "negative", "fraction", "string", "null", "missing" })
                {
                    var envelope = Exclude(Envelope(Project), (OtherProject, 1));
                    JsonNode? value = variant switch { "zero" => JsonValue.Create(0), "negative" => JsonValue.Create(-1),
                        "fraction" => JsonValue.Create(1.5), "string" => JsonValue.Create("1"), _ => null };
                    if (type == "project")
                    {
                        if (variant == "duplicate") Exclude(envelope, (OtherProject, 1), (OtherProject, 1));
                        else envelope["excluded"]!["byProject"]![0]!["projectId"] = variant == "self" ? Project : "bad";
                    }
                    else
                    {
                        var owner = type == "count" ? envelope["excluded"]!["byProject"]![0]!.AsObject() : envelope["excluded"]!.AsObject();
                        owner[type] = value;
                        if (variant == "missing") owner.Remove(type);
                    }
                    var requests = new JsonObject { [PathFor(Project)] = new JsonArray(envelope) };
                    await Check(host, new Vector($"malformed-{type}/{variant}", requests,
                        "RecycleTaskCensusUnknown cause=Malformed field=excluded." + (type == "total" ? "total" : "byProject." + (type == "project" ? "projectId" : "count"))));
                }
            }
            catch (Exception e) { failures.Add((host ? "host: " : "wrapper: ") + e.Message); }
        }
        failures.ShouldBeEmpty(string.Join("\n", failures));
    }

    private static async Task Check(bool host, Vector vector)
    {
        using var scratch = new Scratch();
        File.WriteAllText(Path.Combine(scratch.Root, "requests.json"), vector.Requests.ToJsonString());
        var body = host ? HostProgram(scratch.Root) : WrapperProgram(scratch.Root, vector.Report);
        var run = await Run(host ? "bash" : "pwsh", body, scratch.Root);
        run.Exit.ShouldBe(0, vector.Name + " fixture: " + run.Output);
        var result = JsonNode.Parse(File.ReadAllText(Path.Combine(scratch.Root, "result.json")))!;
        var error = result["error"]!.GetValue<string>();
        if (vector.Error.Length == 0)
        {
            error.ShouldBe("", vector.Name + ": " + run.Output);
            if (vector.Expected is not null)
            {
                var snapshot = result["snapshot"]!;
                JsonNode.DeepEquals(snapshot["open"]!["tasks"]![Id], vector.Expected).ShouldBeTrue(vector.Name + " exact observed facts: " + snapshot);
                snapshot["open"]!["land"]!.AsObject().ContainsKey(Id).ShouldBeTrue(vector.Name + " explicit proof");
                JsonNode.DeepEquals(snapshot["open"]!["land"]![Id], vector.Proof).ShouldBeTrue(vector.Name + " proof identity");
                result["reads"]!.AsArray().Any(x => x!.GetValue<string>() == "/api/agent-tasks/" + Id).ShouldBeTrue(vector.Name + " detail read");
            }
        }
        else error.ShouldStartWith(vector.Error, vector.Name + ": " + run.Output);
        result["sleeps"]!.GetValue<int>().ShouldBe(vector.Sleeps, vector.Name + " bounded retry count");
        Console.WriteLine($"CASE {(host ? "host" : "wrapper")}/{vector.Name} PASS sleeps={vector.Sleeps}");
    }

    private static string WrapperProgram(string root, bool report) => $$"""
        $ErrorActionPreference='Stop'
        $root='{{root}}'
        $ast=[Management.Automation.Language.Parser]::ParseFile('{{DelegateScriptRunner.RepoRoot}}/scripts/deploy-server2.ps1',[ref]$null,[ref]$null)
        foreach ($f in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)) { Invoke-Expression $f.Extent.Text }
        $script:requests=Get-Content -Raw "$root/requests.json" | ConvertFrom-Json
        $script:positions=@{}; $script:reads=[Collections.Generic.List[string]]::new(); $script:sleeps=0
        $script:recycleReceipt=@{}; $script:recycleReceiptTasks=@{}
        function Write-RecycleReceipt {}
        function Start-Sleep { param($Seconds,$Milliseconds) $script:sleeps++; if ($Seconds -ne 30) { throw 'retry must sleep 30 seconds' } }
        function Invoke-RecycleRead { param($Path)
            $script:reads.Add($Path)
            $values=$script:requests.$Path
            if ($null -eq $values) { throw "fixture missing $Path" }
            $index=[int]$script:positions[$Path]; $script:positions[$Path]=$index+1
            return ($values[[Math]::Min($index,$values.Count-1)] | ConvertTo-Json -Depth 30 -Compress | ConvertFrom-Json)
        }
        $errorText=''; $snapshot=$null
        try { $value=Assert-RecycleTaskCensus -RunnerId server2 -ProjectId '{{Project}}' {{(report ? "-Report" : "")}}; $snapshot=$script:recycleReceipt.snapshot }
        catch { $errorText=$_.Exception.Message }
        @{error=$errorText;snapshot=$snapshot;reads=@($script:reads);sleeps=$script:sleeps} | ConvertTo-Json -Depth 40 | Set-Content "$root/result.json"
        """;

    private static string HostProgram(string root)
    {
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        var functions = Regex.Matches(source, @"(?ms)^c1008_tasks(?:_\w+)?\(\) \{\n.*?^\}").Select(m => m.Value);
        return string.Join('\n', functions) + "\n" + $$"""
            set -u
            root='{{root}}'; CASE_DIR="$root"; CASE=probe
            C1008_RUNNER=server2; C1008_PROJECT_ID={{Project}}
            declare -A positions=()
            reads='[]'; sleeps=0
            sleep() { [ "$1" = 30 ] || return 99; sleeps=$((sleeps+1)); }
            c1008_http() {
                local path="$1" index="${positions[$1]:-0}"
                positions[$path]=$((index+1))
                reads="$(jq -nc --argjson prior "$reads" --arg path "$path" '$prior+[$path]')"
                C1008_HTTP_BODY="$(jq -ce --arg path "$path" --argjson index "$index" '.[$path] | .[([$index,length-1]|min)]' "$root/requests.json")" || {
                    C1008_TASK_ERROR="fixture missing $path"; return 2;
                }
            }
            code=0; c1008_tasks > "$root/snapshot.json" || code=$?
            jq -n --arg error "$C1008_TASK_ERROR" --argjson snapshot "$C1008_TASKS" --argjson reads "$reads" --argjson sleeps "$sleeps" \
                '{error:$error,snapshot:$snapshot,reads:$reads,sleeps:$sleeps}' > "$root/result.json"
            exit 0
            """;
    }

    private static async Task Boundaries(bool host, bool passes)
    {
        // Bind the real shared predicate and, for pass comparisons, the actual reconciliation.
        // These probes intentionally reach guards that normal HTTP shape validation masks.
        using var scratch = new Scratch();
        var data = new JsonArray();
        void Add(string name, JsonObject before, JsonObject after, JsonObject detail, bool expected)
            => data.Add(new JsonObject { ["name"] = name, ["before"] = before, ["after"] = after,
                ["detail"] = detail, ["expected"] = expected });
        Add("safe", Row(), Row("Dispatched"), Detail(Row("Dispatched")), true);
        foreach (var side in new[] { "before", "after" })
        foreach (var fault in new[] { "terminal", "bound", "missing" })
        {
            var a = Row(); var b = Row("Dispatched"); var target = side == "before" ? a : b;
            if (fault == "terminal") target["status"] = "Succeeded";
            if (fault == "bound") target["runnerId"] = "server2";
            if (fault == "missing") target.Remove("id");
            Add(side + "/" + fault, a, b, Detail(b, Proof()), false);
        }
        foreach (var key in new[] { "id", "status", "runnerId", "projectId", "scopeSource", "landRequestedAt", "landStartedAt" })
        {
            var a = Row(); var b = Row("Dispatched"); a.Remove(key); b.Remove(key);
            Add("both-missing/" + key, a, b, Detail(b), false);
        }
        foreach (var key in new[] { "id", "runnerId", "projectId", "scopeSource", "landRequestedAt", "landStartedAt" })
        {
            var a = Row(); var b = Row("Dispatched"); b[key] = "changed";
            Add("field/" + key, a, b, Detail(b), false);
        }
        foreach (var variant in new[] { "missing", "pending", "unknown", "invalid-event", "missing-event" })
        {
            var a = Row(); var b = Row("Dispatched"); var d = Detail(b, Proof());
            if (variant == "missing") d.Remove("landRequest");
            if (variant == "pending") d["landRequest"] = Proof("Running", null);
            if (variant == "unknown") d["landRequest"] = Proof("unknown");
            if (variant == "invalid-event") d["landRequest"] = Proof(terminal: "BAD");
            if (variant == "missing-event") d["landRequest"]!.AsObject().Remove("terminalEventId");
            Add(variant, a, b, d, false);
        }
        File.WriteAllText(Path.Combine(scratch.Root, "boundary.json"), data.ToJsonString());
        string body;
        if (!host)
        {
            body = $$"""
                $ErrorActionPreference='Stop'
                $ast=[Management.Automation.Language.Parser]::ParseFile('{{DelegateScriptRunner.RepoRoot}}/scripts/deploy-server2.ps1',[ref]$null,[ref]$null)
                foreach ($f in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)) { Invoke-Expression $f.Extent.Text }
                $cases=Get-Content -Raw '{{scratch.Root}}/boundary.json' | ConvertFrom-Json
                foreach ($c in $cases) {
                    $before=$c|ConvertTo-Json -Depth 30 -Compress
                    $actual=Test-RecycleOpenStatusMove -Before $c.before -After $c.after -Detail $c.detail -RunnerId server2
                    if ($actual -ne $c.expected) { throw "boundary/$($c.name): expected=$($c.expected) actual=$actual" }
                    if (($c|ConvertTo-Json -Depth 30 -Compress) -cne $before) { throw 'input changed' }
                }
                'BOUNDARIES_DONE'
                """;
        }
        else
        {
            var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
            var matches = Regex.Matches(source, @"(?s)# C1361 predicate begin\n(.*?)# C1361 predicate end");
            matches.Count.ShouldBe(2, "both real qualification call sites");
            matches[0].Groups[1].Value.ShouldBe(matches[1].Groups[1].Value);
            body = "jq -e '" + matches[0].Groups[1].Value +
                " all(.[]; . as $c | (open_status_move($c.before;$c.after;$c.detail;\"server2\"))==$c.expected)' '" + scratch.Root + "/boundary.json' || exit 1\necho BOUNDARIES_DONE\n";
        }
        var run = await Run(host ? "bash" : "pwsh", body, scratch.Root);
        run.Exit.ShouldBe(0, run.Output); run.Output.ShouldContain("BOUNDARIES_DONE");
        Console.WriteLine($"BOUNDARY {(host ? "host" : "wrapper")}/{passes}: {data.Count} cases PASS");
    }

    private static async Task<(int Exit, string Output)> Run(string program, string body, string root)
    {
        var file = Path.Combine(root, program == "pwsh" ? "probe.ps1" : "probe.sh"); File.WriteAllText(file, body);
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (program == "pwsh") { psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-File"); }
        psi.ArgumentList.Add(file);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } throw; }
        return (process.ExitCode, await stdout + await stderr);
    }
    private sealed class Scratch : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("c1361-").FullName;
        public void Dispose() => Directory.Delete(Root, true);
    }
}
